using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.InventoryExtension;
using PhinixClient.Trade;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.TradeExtension.Client
{
    // A game instance is a local resource boundary, not evidence of a server outcome.
    // Once invalidated, loading another game never grants this operation a new target.
    internal sealed class TradeOfferGameScope
    {
        private readonly Func<object> current;
        private readonly Func<bool> active;
        private readonly object origin;
        private bool invalid;
        internal TradeOfferGameScope(Func<object> current, Func<bool> active)
        { this.current = current; this.active = active; origin = current(); invalid = origin == null; }
        internal bool IsValid()
        {
            if (!invalid && (!active() || !ReferenceEquals(origin, current()))) invalid = true;
            return !invalid;
        }
    }

    // Per-operation resources, retained by the plugin owner callbacks; no Window references.
    internal sealed class TradeOfferEscrow
    {
        private readonly ClientTradeUiHostContext host;
        private readonly TradeOfferGameScope scope;
        internal readonly List<PoppedThing> Physical=new List<PoppedThing>();
        internal string ReservationId,Token;
        internal string LastError;
        internal TradeOfferEscrow(ClientTradeUiHostContext host):this(host,host.CaptureOfferGameScope()){}
        internal TradeOfferEscrow(ClientTradeUiHostContext host, TradeOfferGameScope scope)
        {this.host=host;this.scope=scope;}
        internal bool ValidateScope()
        {
            if(scope.IsValid())return true;
            LastError="TradeOfferGameScopeExpired";
            return false;
        }
        internal void Unknown()
        {if(ValidateScope()&&!string.IsNullOrEmpty(ReservationId))Resolve(InventoryReservationResolution.MarkUncertain,"trade-offer-unknown:"+Token);}
        private bool Resolve(InventoryReservationResolution resolution,string evidence)
        {
            if(string.IsNullOrEmpty(ReservationId))return true;
            var result=host.InventoryReservationApi?.ResolveReservation(ReservationId,resolution,evidence);
            if(result?.Succeeded==true){LastError=null;return true;}
            LastError=result?.Reason??"Inventory reservation service is unavailable.";
            host.Log(new LogEventArgs("Trade offer reservation settlement blocked: "+ReservationId+"; "+result?.Reason,LogLevel.ERROR));return false;
        }
        internal TradeOfferSettlement Commit()
        {
            if(!ValidateScope())return TradeOfferSettlement.Uncertain;
            if(!Resolve(InventoryReservationResolution.Commit,"trade-update-success:"+Token))return TradeOfferSettlement.Blocked;
            foreach(var popped in Physical)if(popped.Thing!=null&&!popped.Thing.Destroyed)popped.Thing.Destroy();
            return TradeOfferSettlement.Done;
        }
        internal TradeOfferSettlement Restore()=>RestoreWithEvidence("trade-update-rejected:"+Token);
        internal TradeOfferSettlement Abort()=>RestoreWithEvidence("trade-not-sent:"+Token);
        private TradeOfferSettlement RestoreWithEvidence(string evidence)
        {
            if(!ValidateScope())return TradeOfferSettlement.Uncertain;
            if(!Resolve(InventoryReservationResolution.Restore,evidence))return TradeOfferSettlement.Blocked;
            var unrestored=PoppedThing.RestoreAll(Physical,(text,level)=>host.Log(new LogEventArgs(text,level)),"TradeOfferOwner.Restore");
            if(unrestored.Count==0)return TradeOfferSettlement.Done;
            var delivery=host.DeliverPhysical(unrestored);LastError=delivery?.Reason;return FromDelivery(delivery);
        }
        internal TradeOfferSettlement ReturnFrozen(FrameworkItemPayload[] payloads)
        {
            if(!ValidateScope())return TradeOfferSettlement.Uncertain;
            if(host.OfferDelivery==null){LastError="Trade offer delivery service is unavailable.";return TradeOfferSettlement.Blocked;}
            var result=host.OfferDelivery.DeliverFrozenOfferReturn(payloads,host.DropCurrentMap);
            LastError=result?.Reason;
            return FromDelivery(result);
        }
        internal static TradeOfferSettlement FromDelivery(InventoryDeliveryResult result)
        {return result?.Succeeded==true?TradeOfferSettlement.Done:result==null||result.Status==InventoryDeliveryStatus.Uncertain?TradeOfferSettlement.Uncertain:TradeOfferSettlement.Blocked;}
        internal static FrameworkItemPayload[] FreezeReturn(ClientTradeUiHostContext host,IEnumerable<TradeItemSnapshot> offer)
        {
            // Own defensive payload bytes/metadata before sending the clear request.
            var payloads=host.ItemPipeline.EncodeTradeItems(offer).Select(p=>FrameworkSerialization.DeserializePayload<FrameworkItemPayload>(FrameworkSerialization.SerializePayload(p))).ToArray();
            if(payloads.Any(p=>p==null||!host.ItemPipeline.CanDecodeItemExactly(p)))throw new InvalidOperationException("TradeOfferReturnCodecUnavailable");
            return payloads;
        }
    }
}
