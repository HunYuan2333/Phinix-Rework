using System;
using System.Text;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public enum TalentTradeMessageType
    {
        None = 0,
        // Direct trade
        TradeRequest,
        TradeAccept,
        TradeReject,
        TradeOffer,
        TradeLock,
        TradeExecute,
        TradeCancel,
        // Market
        MarketList,
        MarketDelist,
        MarketBuy,
        MarketSell,
        MarketPaid,
        MarketSync,
        // Rental
        RentalList,
        RentalDelist,
        RentalRent,
        RentalConfirm,
        RentalReturn,
        RentalExpiry,
        RentalDead,
        RentalRevive,
        // Def exchange
        DefManifest,
        DefAck,
        // Blob
        BlobPart
    }

    public static class TalentTradeProtocol
    {
        public const string Prefix = "PHXTT";
        public const string Version = "v1";

        public static bool IsProtocolMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.IndexOf(Prefix + "|", StringComparison.Ordinal) >= 0;
        }

        public static string EncodeField(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
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

        // --- Direct Trade ---

        public static string BuildTradeRequest(string tradeId, string initiatorUuid, string targetUuid, string initiatorName)
        {
            return Join("treq", tradeId, initiatorUuid, targetUuid, EncodeField(initiatorName));
        }

        public static string BuildTradeAccept(string tradeId, string responderUuid)
        {
            return Join("tacc", tradeId, responderUuid);
        }

        public static string BuildTradeReject(string tradeId, string responderUuid)
        {
            return Join("trej", tradeId, responderUuid);
        }

        public static string BuildTradeOffer(string tradeId, string senderUuid, string b64OfferJson)
        {
            return Join("toff", tradeId, senderUuid, b64OfferJson);
        }

        public static string BuildTradeLock(string tradeId, string senderUuid)
        {
            return Join("tlock", tradeId, senderUuid);
        }

        public static string BuildTradeExecute(string tradeId, string senderUuid, string b64CompressedPawnData, string b64CompressedItems)
        {
            return Join("texe", tradeId, senderUuid, b64CompressedPawnData, b64CompressedItems);
        }

        public static string BuildTradeCancel(string tradeId, string senderUuid)
        {
            return Join("tcan", tradeId, senderUuid);
        }

        // --- Market ---

        public static string BuildMarketList(string listingId, string sellerUuid, string b64PawnSummary, int priceSilver, string sellerName)
        {
            return Join("mlist", listingId, sellerUuid, b64PawnSummary, priceSilver.ToString(), EncodeField(sellerName));
        }

        public static string BuildMarketDelist(string listingId, string sellerUuid)
        {
            return Join("mdel", listingId, sellerUuid);
        }

        public static string BuildMarketBuy(string listingId, string buyerUuid, string buyerName)
        {
            return Join("mbuy", listingId, buyerUuid, EncodeField(buyerName));
        }

        public static string BuildMarketSell(string listingId, string sellerUuid, string buyerUuid, string b64CompressedPawnData)
        {
            return Join("msell", listingId, sellerUuid, buyerUuid, b64CompressedPawnData);
        }

        public static string BuildMarketPaid(string listingId, string buyerUuid, string b64CompressedSilverItems)
        {
            return Join("mpaid", listingId, buyerUuid, b64CompressedSilverItems);
        }

        public static string BuildMarketSync(string requestorUuid)
        {
            return Join("msync", requestorUuid);
        }

        // --- Rental ---

        public static string BuildRentalList(string rentalId, string ownerUuid, string b64PawnSummary, int pricePerDay, int maxDays, int deposit, string ownerName)
        {
            return Join("rlist", rentalId, ownerUuid, b64PawnSummary, pricePerDay.ToString(), maxDays.ToString(), deposit.ToString(), EncodeField(ownerName));
        }

        public static string BuildRentalDelist(string rentalId, string ownerUuid)
        {
            return Join("rdel", rentalId, ownerUuid);
        }

        public static string BuildRentalRent(string rentalId, string renterUuid, int days, string renterName)
        {
            return Join("rrent", rentalId, renterUuid, days.ToString(), EncodeField(renterName));
        }

        public static string BuildRentalConfirm(string rentalId, string ownerUuid, string renterUuid, string b64CompressedPawnData)
        {
            return Join("rconf", rentalId, ownerUuid, renterUuid, b64CompressedPawnData);
        }

        public static string BuildRentalReturn(string rentalId, string renterUuid, string b64CompressedPawnData)
        {
            return Join("rret", rentalId, renterUuid, b64CompressedPawnData);
        }

        public static string BuildRentalExpiry(string rentalId)
        {
            return Join("rexp", rentalId);
        }

        public static string BuildRentalDead(string rentalId, string renterUuid)
        {
            return Join("rdead", rentalId, renterUuid);
        }

        public static string BuildRentalRevive(string rentalId, string ownerUuid, string b64CompressedPawnData)
        {
            return Join("rrev", rentalId, ownerUuid, b64CompressedPawnData);
        }

        // --- Def exchange ---

        public static string BuildDefManifest(string sessionId, string senderUuid, string targetUuid, string b64CompressedDefList)
        {
            return Join("defs", sessionId, senderUuid, targetUuid, b64CompressedDefList);
        }

        public static string BuildDefAck(string sessionId, string senderUuid, string b64MissingDefs)
        {
            return Join("dack", sessionId, senderUuid, b64MissingDefs);
        }

        // --- Blob ---

        public static string BuildBlobPart(string blobId, int partIndex, int totalParts, string b64PartData)
        {
            return Join("blob", blobId, partIndex.ToString(), totalParts.ToString(), b64PartData);
        }

        // --- Parsing ---

        public static bool TryParse(string message, out TalentTradeMessageType messageType, out string[] parts)
        {
            messageType = TalentTradeMessageType.None;
            parts = null;

            if (string.IsNullOrEmpty(message)) return false;

            int prefixIndex = message.IndexOf(Prefix + "|", StringComparison.Ordinal);
            if (prefixIndex < 0) return false;

            string payload = message.Substring(prefixIndex).Trim();
            string[] tokens = payload.Split('|');
            if (tokens.Length < 3) return false;
            if (tokens[0] != Prefix || tokens[1] != Version) return false;

            switch (tokens[2])
            {
                case "treq": messageType = TalentTradeMessageType.TradeRequest; break;
                case "tacc": messageType = TalentTradeMessageType.TradeAccept; break;
                case "trej": messageType = TalentTradeMessageType.TradeReject; break;
                case "toff": messageType = TalentTradeMessageType.TradeOffer; break;
                case "tlock": messageType = TalentTradeMessageType.TradeLock; break;
                case "texe": messageType = TalentTradeMessageType.TradeExecute; break;
                case "tcan": messageType = TalentTradeMessageType.TradeCancel; break;
                case "mlist": messageType = TalentTradeMessageType.MarketList; break;
                case "mdel": messageType = TalentTradeMessageType.MarketDelist; break;
                case "mbuy": messageType = TalentTradeMessageType.MarketBuy; break;
                case "msell": messageType = TalentTradeMessageType.MarketSell; break;
                case "mpaid": messageType = TalentTradeMessageType.MarketPaid; break;
                case "msync": messageType = TalentTradeMessageType.MarketSync; break;
                case "rlist": messageType = TalentTradeMessageType.RentalList; break;
                case "rdel": messageType = TalentTradeMessageType.RentalDelist; break;
                case "rrent": messageType = TalentTradeMessageType.RentalRent; break;
                case "rconf": messageType = TalentTradeMessageType.RentalConfirm; break;
                case "rret": messageType = TalentTradeMessageType.RentalReturn; break;
                case "rexp": messageType = TalentTradeMessageType.RentalExpiry; break;
                case "rdead": messageType = TalentTradeMessageType.RentalDead; break;
                case "rrev": messageType = TalentTradeMessageType.RentalRevive; break;
                case "defs": messageType = TalentTradeMessageType.DefManifest; break;
                case "dack": messageType = TalentTradeMessageType.DefAck; break;
                case "blob": messageType = TalentTradeMessageType.BlobPart; break;
                default: return false;
            }

            parts = tokens;
            return true;
        }

        private static string Join(string type, params string[] fields)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Prefix);
            sb.Append('|');
            sb.Append(Version);
            sb.Append('|');
            sb.Append(type);
            for (int i = 0; i < fields.Length; i++)
            {
                sb.Append('|');
                sb.Append(fields[i] ?? string.Empty);
            }
            return sb.ToString();
        }
    }
}
