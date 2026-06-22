using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    internal static class TalentTradeTransport
    {
        private const string RelayBaseUrl = "http://114.55.115.143:8080";
        private const string RelayApiKey = "";
        private const string RelayRoom = "talent-trade";

        private static readonly object OutgoingLock = new object();
        private static readonly Queue<OutgoingProtocol> OutgoingQueue = new Queue<OutgoingProtocol>();
        private static readonly object IncomingLock = new object();
        private static readonly Queue<string> IncomingQueue = new Queue<string>();
        private static readonly object StateLock = new object();

        private static long lastSeenId;
        private static DateTime nextPollUtc = DateTime.MinValue;
        private static DateTime nextSendUtc = DateTime.MinValue;
        private static int pollInFlight;
        private static int sendInFlight;

        private const int PollIntervalMs = 700;
        private const int SendIntervalMs = 80;
        private const int RequestTimeoutMs = 5000;
        private const int FetchLimit = 200;
        private const int InitialHistoryMinutes = 20;
        // 设计哲学 §3.6：队列有界，超出丢最旧并记 Warning
        private const int MaxOutgoingQueue = 1024;
        private const int MaxIncomingQueue = 1024;
        private static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static void Clear()
        {
            lock (OutgoingLock)
            {
                OutgoingQueue.Clear();
            }

            lock (IncomingLock)
            {
                IncomingQueue.Clear();
            }

            lock (StateLock)
            {
                lastSeenId = 0;
                nextPollUtc = DateTime.MinValue;
                nextSendUtc = DateTime.MinValue;
            }

            Interlocked.Exchange(ref pollInFlight, 0);
            Interlocked.Exchange(ref sendInFlight, 0);
        }

        public static void EnqueueProtocol(string protocolMessage, string senderUuid)
        {
            if (string.IsNullOrEmpty(protocolMessage)) return;

            lock (OutgoingLock)
            {
                if (OutgoingQueue.Count >= MaxOutgoingQueue)
                {
                    OutgoingQueue.Dequeue();
                    LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】Relay outgoing queue overflow, dropped oldest message.");
                }
                OutgoingQueue.Enqueue(new OutgoingProtocol
                {
                    Message = protocolMessage,
                    SenderUuid = string.IsNullOrEmpty(senderUuid) ? "unknown" : senderUuid,
                    EventId = Guid.NewGuid().ToString("N")
                });
            }
        }

        public static bool TryDequeueIncoming(out string protocolMessage)
        {
            lock (IncomingLock)
            {
                if (IncomingQueue.Count > 0)
                {
                    protocolMessage = IncomingQueue.Dequeue();
                    return true;
                }
            }

            protocolMessage = null;
            return false;
        }

        public static void Update()
        {
            DateTime now = DateTime.UtcNow;

            bool shouldSend = false;
            lock (StateLock)
            {
                if (now >= nextSendUtc)
                {
                    shouldSend = true;
                    nextSendUtc = now.AddMilliseconds(SendIntervalMs);
                }
            }

            if (shouldSend && HasOutgoing())
            {
                if (Interlocked.CompareExchange(ref sendInFlight, 1, 0) == 0)
                {
                    ThreadPool.QueueUserWorkItem(_ => SendOnceWorker());
                }
            }

            bool shouldPoll = false;
            lock (StateLock)
            {
                if (now >= nextPollUtc)
                {
                    shouldPoll = true;
                    nextPollUtc = now.AddMilliseconds(PollIntervalMs);
                }
            }

            if (shouldPoll && Interlocked.CompareExchange(ref pollInFlight, 1, 0) == 0)
            {
                ThreadPool.QueueUserWorkItem(_ => PollWorker());
            }
        }

        // --- Compression utilities ---

        public static string Compress(string rawData)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(rawData);
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gz = new GZipStream(ms, CompressionLevel.Optimal))
                {
                    gz.Write(bytes, 0, bytes.Length);
                }
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public static string Decompress(string b64Compressed)
        {
            byte[] compressed = Convert.FromBase64String(b64Compressed);
            using (MemoryStream ms = new MemoryStream(compressed))
            using (GZipStream gz = new GZipStream(ms, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gz, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        // --- Private ---

        private static bool HasOutgoing()
        {
            lock (OutgoingLock)
            {
                return OutgoingQueue.Count > 0;
            }
        }

        private static void SendOnceWorker()
        {
            OutgoingProtocol outbound = null;
            try
            {
                lock (OutgoingLock)
                {
                    if (OutgoingQueue.Count > 0)
                    {
                        outbound = OutgoingQueue.Dequeue();
                    }
                }

                if (outbound == null) return;

                string url = RelayBaseUrl.TrimEnd('/') + "/v1/raw";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(outbound.Message);

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = RequestTimeoutMs;
                request.ReadWriteTimeout = RequestTimeoutMs;
                request.Proxy = null;
                request.ContentType = "text/plain; charset=utf-8";
                request.ContentLength = bodyBytes.Length;
                if (!string.IsNullOrEmpty(RelayApiKey))
                    request.Headers["X-Api-Key"] = RelayApiKey;
                request.Headers["X-Room"] = RelayRoom;
                request.Headers["X-Sender"] = outbound.SenderUuid;
                request.Headers["X-Event-Id"] = outbound.EventId;

                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                // §3.5/§8.2：网络失败退避重试，异常必须可观测（带完整堆栈）
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】Relay send failed, will retry: " + ex);
                if (outbound != null)
                {
                    lock (OutgoingLock)
                    {
                        OutgoingQueue.Enqueue(outbound);
                    }
                }

                lock (StateLock)
                {
                    nextSendUtc = DateTime.UtcNow.AddSeconds(1);
                }
            }
            finally
            {
                Interlocked.Exchange(ref sendInFlight, 0);
            }
        }

        private static void PollWorker()
        {
            try
            {
                long afterId;
                lock (StateLock)
                {
                    afterId = lastSeenId;
                }

                long sinceMs = 0;
                if (afterId <= 0)
                {
                    DateTime cutoffUtc = DateTime.UtcNow.AddMinutes(-InitialHistoryMinutes);
                    sinceMs = (long)(cutoffUtc - UnixEpochUtc).TotalMilliseconds;
                    if (sinceMs < 0) sinceMs = 0;
                }

                string url = string.Concat(
                    RelayBaseUrl.TrimEnd('/'),
                    "/v1/raw?room=",
                    Uri.EscapeDataString(RelayRoom),
                    "&after_id=",
                    afterId.ToString(),
                    "&limit=",
                    FetchLimit.ToString(),
                    sinceMs > 0 ? "&since_ms=" : string.Empty,
                    sinceMs > 0 ? sinceMs.ToString() : string.Empty
                );

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = RequestTimeoutMs;
                request.ReadWriteTimeout = RequestTimeoutMs;
                request.Proxy = null;
                if (!string.IsNullOrEmpty(RelayApiKey))
                    request.Headers["X-Api-Key"] = RelayApiKey;

                string responseText;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    responseText = reader.ReadToEnd();
                }

                if (LegacyTalentTradeRuntime.Settings?.EnableDebugLog == true)
                {
                    LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Poll success, response length: {responseText.Length}");
                }
                ParseRawResponse(responseText);
            }
            catch (Exception ex)
            {
                if (LegacyTalentTradeRuntime.Settings?.EnableDebugLog == true)
                {
                    // §3.8：异常日志必须附带完整 Exception（含堆栈）
                    LegacyTalentTradeRuntime.LogWarning($"【三角洲贸易】PollWorker error: {ex}");
                }
                lock (StateLock)
                {
                    nextPollUtc = DateTime.UtcNow.AddSeconds(2);
                }
            }
            finally
            {
                Interlocked.Exchange(ref pollInFlight, 0);
            }
        }

        private static void ParseRawResponse(string responseText)
        {
            if (string.IsNullOrEmpty(responseText)) return;

            string[] lines = responseText.Split(new[] { '\n' }, StringSplitOptions.None);
            if (lines.Length == 0) return;

            long currentLastSeen;
            lock (StateLock)
            {
                currentLastSeen = lastSeenId;
            }

            long maxId = currentLastSeen;

            string firstLine = lines[0].Trim();
            long reportedLastId;
            if (long.TryParse(firstLine, out reportedLastId))
            {
                maxId = Math.Max(maxId, reportedLastId);
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                int tabIndex = line.IndexOf('\t');
                if (tabIndex <= 0) continue;

                string idPart = line.Substring(0, tabIndex).Trim();
                long idValue;
                if (!long.TryParse(idPart, out idValue)) continue;
                if (idValue <= currentLastSeen) continue;

                string b64 = line.Substring(tabIndex + 1).Trim();
                if (string.IsNullOrEmpty(b64)) continue;

                string message;
                try
                {
                    byte[] bytes = Convert.FromBase64String(b64);
                    message = Encoding.UTF8.GetString(bytes);
                }
                catch (Exception)
                {
                    // §3.5：单条损坏报文跳过，不中断其余消息（解析边界隔离）
                    continue;
                }

                lock (IncomingLock)
                {
                    if (IncomingQueue.Count >= MaxIncomingQueue)
                    {
                        IncomingQueue.Dequeue();
                        LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】Relay incoming queue overflow, dropped oldest message.");
                    }
                    IncomingQueue.Enqueue(message);
                }

                if (idValue > maxId) maxId = idValue;
            }

            lock (StateLock)
            {
                if (maxId > lastSeenId)
                {
                    lastSeenId = maxId;
                }
            }
        }

        private class OutgoingProtocol
        {
            public string Message;
            public string SenderUuid;
            public string EventId;
        }
    }
}
