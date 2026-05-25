using System.Collections.Generic;
using System.Linq;
using Trading;
using UserManagement;

namespace PhinixClient.Trade
{
    public static class TradeLegacyConversions
    {
        public static ClientTradeSnapshot ToClientTrade(ImmutableTrade trade)
        {
            return new ClientTradeSnapshot(
                trade.TradeId,
                trade.OtherParty,
                trade.ItemsOnOffer.Select(ToTradeItemSnapshot),
                trade.OtherPartyItemsOnOffer.Select(ToTradeItemSnapshot),
                trade.Accepted,
                trade.OtherPartyAccepted);
        }

        public static TradeItemSnapshot ToTradeItemSnapshot(ProtoThing item)
        {
            if (item == null)
            {
                return null;
            }

            return new TradeItemSnapshot(
                item.DefName,
                item.StackCount,
                item.HitPoints,
                ToTradeItemQuality(item.Quality),
                item.StuffDefName,
                ToTradeItemSnapshot(item.InnerProtoThing));
        }

        public static ProtoThing ToLegacyProtoThing(TradeItemSnapshot item)
        {
            if (item == null)
            {
                return null;
            }

            return new ProtoThing
            {
                DefName = item.DefName,
                StackCount = item.StackCount,
                HitPoints = item.HitPoints,
                StuffDefName = item.StuffDefName,
                Quality = ToLegacyQuality(item.Quality),
                InnerProtoThing = ToLegacyProtoThing(item.InnerItem)
            };
        }

        public static TradeFailureReason ToClientFailureReason(Trading.TradeFailureReason reason)
        {
            switch (reason)
            {
                case Trading.TradeFailureReason.SessionId: return TradeFailureReason.SessionInvalid;
                case Trading.TradeFailureReason.Uuid: return TradeFailureReason.LoginInvalid;
                case Trading.TradeFailureReason.OtherPartyOffline: return TradeFailureReason.OtherPartyOffline;
                case Trading.TradeFailureReason.OtherPartyDoesNotExist: return TradeFailureReason.OtherPartyDoesNotExist;
                case Trading.TradeFailureReason.AlreadyTrading: return TradeFailureReason.AlreadyTrading;
                case Trading.TradeFailureReason.TradeDoesNotExist: return TradeFailureReason.TradeDoesNotExist;
                case Trading.TradeFailureReason.NotAcceptingTrades: return TradeFailureReason.NotAcceptingTrades;
                case Trading.TradeFailureReason.InternalServerError:
                default:
                    return TradeFailureReason.InternalServerError;
            }
        }

        public static TradeItemQuality ToTradeItemQuality(Quality quality)
        {
            switch (quality)
            {
                case Quality.Awful: return TradeItemQuality.Awful;
                case Quality.Poor: return TradeItemQuality.Poor;
                case Quality.Normal: return TradeItemQuality.Normal;
                case Quality.Good: return TradeItemQuality.Good;
                case Quality.Excellent: return TradeItemQuality.Excellent;
                case Quality.Masterwork: return TradeItemQuality.Masterwork;
                case Quality.Legendary: return TradeItemQuality.Legendary;
                case Quality.None:
                default:
                    return TradeItemQuality.None;
            }
        }

        public static Quality ToLegacyQuality(TradeItemQuality quality)
        {
            switch (quality)
            {
                case TradeItemQuality.Awful: return Quality.Awful;
                case TradeItemQuality.Poor: return Quality.Poor;
                case TradeItemQuality.Normal: return Quality.Normal;
                case TradeItemQuality.Good: return Quality.Good;
                case TradeItemQuality.Excellent: return Quality.Excellent;
                case TradeItemQuality.Masterwork: return Quality.Masterwork;
                case TradeItemQuality.Legendary: return Quality.Legendary;
                case TradeItemQuality.None:
                default:
                    return Quality.None;
            }
        }
    }
}
