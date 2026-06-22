using System;
using System.Collections.Generic;
using System.Text;
using PhinixClient.Trade;
using Utils;

namespace Phinix.LegacyRedPacketExtension
{
    /// <summary>
    /// v1 红包协议（与老 submod PhinixRedPacket 线格式完全一致，兼容老客户端）。
    ///
    /// 线格式：payload = "PHXRP|v1|{create|claim|assign|timeout}|..."（pipe 分隔）。
    /// 中继传输的是纯文本 payload；零宽字符（U+200C=0 / U+200D=1，哨兵 U+2060×4）
    /// 编码仅用于历史报文/聊天内嵌场景，解析库需兼容。
    /// </summary>
    public static class RedPacketProtocol
    {
        public const string Prefix = "PHXRP";
        public const string Version = "v1";
        private const string ZeroSentinel = "\u2060\u2060\u2060\u2060";
        private const char Zero0 = '\u200C';
        private const char Zero1 = '\u200D';

        public static bool IsProtocolMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            string payload = ExtractPayload(message);
            if (!string.IsNullOrEmpty(payload))
            {
                return payload.IndexOf(Prefix + "|", StringComparison.Ordinal) >= 0;
            }

            string stripped = message.IndexOf('<') >= 0 ? TextHelper.StripRichText(message) : message;
            if (!ReferenceEquals(stripped, message))
            {
                payload = ExtractPayload(stripped);
                if (!string.IsNullOrEmpty(payload))
                {
                    return payload.IndexOf(Prefix + "|", StringComparison.Ordinal) >= 0;
                }
            }

            return stripped.IndexOf(Prefix + "|", StringComparison.Ordinal) >= 0;
        }

        public static string BuildCreate(RedPacketCreateData packet)
        {
            string payload = string.Join("|", new[]
            {
                Prefix,
                Version,
                "create",
                packet.Id,
                packet.SenderUuid ?? string.Empty,
                packet.DefName ?? string.Empty,
                packet.StuffDefName ?? string.Empty,
                ToWireQuality(packet.Quality).ToString(),
                packet.HitPoints.ToString(),
                packet.TotalCount.ToString(),
                packet.TotalPackets.ToString(),
                ((int) packet.Type).ToString(),
                packet.CreatedAtUtc.Ticks.ToString(),
                packet.ExpiresAtUtc.Ticks.ToString(),
                EncodeField(packet.SenderDisplayName ?? string.Empty),
                packet.LuckyAlgorithmVersion.ToString()
            });

            return payload;
        }

        public static string DecodeField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string BuildClaim(string packetId, string claimerUuid, string claimerDisplayName = null)
        {
            string encodedName = EncodeField(claimerDisplayName ?? string.Empty);
            string payload = string.Join("|", new[]
            {
                Prefix,
                Version,
                "claim",
                packetId ?? string.Empty,
                claimerUuid ?? string.Empty,
                encodedName
            });

            return payload;
        }

        public static string BuildAssign(string packetId, string claimerUuid, int amount, int remainingPackets, int remainingCount)
        {
            string payload = string.Join("|", new[]
            {
                Prefix,
                Version,
                "assign",
                packetId ?? string.Empty,
                claimerUuid ?? string.Empty,
                amount.ToString(),
                remainingPackets.ToString(),
                remainingCount.ToString()
            });

            return payload;
        }

        public static string BuildTimeout(string packetId, string claimerUuid)
        {
            string payload = string.Join("|", new[]
            {
                Prefix,
                Version,
                "timeout",
                packetId ?? string.Empty,
                claimerUuid ?? string.Empty,
                DateTime.UtcNow.Ticks.ToString()
            });

            return payload;
        }

        public static bool TryParse(string message, out RedPacketMessageType messageType, out string[] parts)
        {
            messageType = RedPacketMessageType.None;
            parts = null;

            if (string.IsNullOrEmpty(message)) return false;
            string payload = ExtractPayload(message);
            if (string.IsNullOrEmpty(payload))
            {
                string stripped = message.IndexOf('<') >= 0 ? TextHelper.StripRichText(message) : message;
                if (!ReferenceEquals(stripped, message))
                {
                    payload = ExtractPayload(stripped);
                }

                int prefixIndex = stripped.IndexOf(Prefix + "|", StringComparison.Ordinal);
                if (prefixIndex < 0) return false;
                payload = stripped.Substring(prefixIndex).Trim();
            }

            string[] tokens = payload.Split('|');
            if (tokens.Length < 3) return false;
            if (tokens[0] != Prefix || tokens[1] != Version) return false;

            switch (tokens[2])
            {
                case "create":
                    messageType = RedPacketMessageType.Create;
                    break;
                case "claim":
                    messageType = RedPacketMessageType.Claim;
                    break;
                case "assign":
                    messageType = RedPacketMessageType.Assign;
                    break;
                case "timeout":
                    messageType = RedPacketMessageType.Timeout;
                    break;
                default:
                    return false;
            }

            parts = tokens;
            return true;
        }

        /// <summary>
        /// TradeItemQuality → v1 线格式 quality（旧 Verse.Quality 数值：Awful=0 … Legendary=6）。
        /// 老客户端按 Verse.Quality 直接还原，数值必须保持旧范围。
        /// </summary>
        public static int ToWireQuality(TradeItemQuality quality)
        {
            switch (quality)
            {
                case TradeItemQuality.Awful: return 0;
                case TradeItemQuality.Poor: return 1;
                case TradeItemQuality.Normal: return 2;
                case TradeItemQuality.Good: return 3;
                case TradeItemQuality.Excellent: return 4;
                case TradeItemQuality.Masterwork: return 5;
                case TradeItemQuality.Legendary: return 6;
                default: return 0;
            }
        }

        /// <summary>
        /// v1 线格式 quality → TradeItemQuality。范围外回退 Awful（与旧版 ?? 0 行为一致）。
        /// </summary>
        public static TradeItemQuality FromWireQuality(int qualityValue)
        {
            switch (qualityValue)
            {
                case 0: return TradeItemQuality.Awful;
                case 1: return TradeItemQuality.Poor;
                case 2: return TradeItemQuality.Normal;
                case 3: return TradeItemQuality.Good;
                case 4: return TradeItemQuality.Excellent;
                case 5: return TradeItemQuality.Masterwork;
                case 6: return TradeItemQuality.Legendary;
                default: return TradeItemQuality.Awful;
            }
        }

        private static string ExtractPayload(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            int sentinelIndex = message.IndexOf(ZeroSentinel, StringComparison.Ordinal);
            if (sentinelIndex < 0) return null;

            int startIndex = sentinelIndex + ZeroSentinel.Length;
            if (startIndex >= message.Length) return string.Empty;

            return DecodeZeroWidth(message, startIndex);
        }

        private static string EncodeField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string DecodeZeroWidth(string message, int startIndex)
        {
            var bytes = new List<byte>();
            int bitCount = 0;
            byte current = 0;

            for (int i = startIndex; i < message.Length; i++)
            {
                char candidate = message[i];
                if (candidate != Zero0 && candidate != Zero1) continue;

                current = (byte)((current << 1) | (candidate == Zero1 ? 1 : 0));
                bitCount++;

                if (bitCount == 8)
                {
                    bytes.Add(current);
                    bitCount = 0;
                    current = 0;
                }
            }

            return bytes.Count == 0 ? string.Empty : Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    public enum RedPacketMessageType
    {
        None = 0,
        Create = 1,
        Claim = 2,
        Assign = 3,
        Timeout = 4
    }
}
