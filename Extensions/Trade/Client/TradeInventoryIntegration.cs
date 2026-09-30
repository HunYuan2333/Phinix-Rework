using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Phinix.InventoryExtension;
using PhinixClient.Trade;
using RimWorld;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.TradeExtension.Client
{
    internal sealed class TradeInventoryCodec : IInventoryCodec, IInventoryVerifiedDeliveryCodec, IInventoryItemPresentationCodec
    {
        public const string Id = "trade.framework-item-v1";
        private readonly TradeClientItemPipeline itemPipeline;

        public TradeInventoryCodec(TradeClientItemPipeline itemPipeline)
        {
            this.itemPipeline = itemPipeline ?? throw new ArgumentNullException(nameof(itemPipeline));
        }

        public string CodecId => Id;
        public int Version => 1;

        public bool CanStore(InventoryItem item)
        {
            if (item == null || item.CodecId != CodecId || item.CodecVersion != Version ||
                item.Quantity != 1 || item.Payload == null || item.Payload.Length == 0) return false;
            try { return itemPipeline.CanDecodeItemExactly(Decode(item.Payload)); }
            catch (Exception) { return false; }
        }

        public string GetAggregationKey(InventoryItem item) => null;
        public int MaximumStackCount(InventoryEntry entry) => 1;

        public InventoryItemPresentation PresentItem(InventoryEntry entry)
        {
            if (!FrameworkTradeItemPayloadCodec.TryDecodeToTradeItemSnapshot(Decode(entry.Payload), out TradeItemSnapshot snapshot) ||
                snapshot.StackCount < 1 || snapshot.InnerItem != null) return null;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(snapshot.DefName);
            if (def == null) return null;
            string label = def.LabelCap.ToString();
            ThingDef stuff = string.IsNullOrEmpty(snapshot.StuffDefName) ? null :
                DefDatabase<ThingDef>.GetNamedSilentFail(snapshot.StuffDefName);
            if (stuff != null) label += " · " + stuff.LabelCap;
            if (snapshot.Quality != TradeItemQuality.None)
                label += " (" + ((QualityCategory)((int)snapshot.Quality - 1)).GetLabel() + ")";
            return new InventoryItemPresentation
            {
                Label = label,
                GroupKey = snapshot.DefName.Length + ":" + snapshot.DefName + snapshot.StuffDefName.Length + ":" +
                    snapshot.StuffDefName + ":" + (int)snapshot.Quality,
                ItemsPerUnit = snapshot.StackCount
            };
        }

        public Thing Materialize(InventoryEntry entry, int count)
        {
            if (entry == null || count != 1) throw new ArgumentException("A delivered trade stack is indivisible.");
            return itemPipeline.DecodeItemExactly(Decode(entry.Payload));
        }

        public void Deliver(Thing thing, Map map, IntVec3 dropSpot)
        {
            InventoryDeliveryResult result = DeliverVerified(thing, map, dropSpot);
            if (!result.Succeeded) throw new InvalidOperationException(result.Reason ?? "Trade delivery failed.");
        }

        public InventoryDeliveryResult DeliverVerified(Thing thing, Map map, IntVec3 dropSpot)
        {
            return InventoryDropDelivery.DeliverThings(new[] { thing }, map, dropSpot);
        }

        internal static byte[] Encode(FrameworkItemPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(payload));
        }

        internal static FrameworkItemPayload Decode(byte[] payload)
        {
            if (payload == null || payload.Length == 0) throw new InvalidOperationException("Trade inventory payload is empty.");
            FrameworkItemPayload decoded = FrameworkSerialization.DeserializePayload<FrameworkItemPayload>(Encoding.UTF8.GetString(payload));
            if (decoded == null || string.IsNullOrWhiteSpace(decoded.CodecId))
                throw new InvalidOperationException("Trade inventory payload is invalid.");
            return decoded;
        }
    }

    internal sealed class TradeInventorySourcePresenter : IInventorySourcePresenter
    {
        public string SourceId => FrameworkTradeProtocol.Capability;

        public InventorySourcePresentation Present(InventoryEntry entry)
        {
            string name = string.IsNullOrWhiteSpace(entry?.OriginDisplayName)
                ? entry?.OriginUserId : entry.OriginDisplayName;
            if (string.IsNullOrWhiteSpace(name)) name = "Phinix_inventory_originUnknown".Translate().ToString();
            InventorySourcePresentation presentation = new InventorySourcePresentation
            {
                Summary = entry?.OriginKind == "trade-return"
                    ? "Phinix_inventory_originReturn".Translate().ToString()
                    : "Phinix_inventory_originTrade".Translate(name).ToString()
            };
            if (!string.IsNullOrWhiteSpace(entry?.OriginId))
                presentation.Details.Add("Phinix_inventory_originEventId".Translate(entry.OriginId).ToString());
            if (!string.IsNullOrWhiteSpace(entry?.OriginUserId))
                presentation.Details.Add("Phinix_inventory_originUserId".Translate(entry.OriginUserId).ToString());
            return presentation;
        }
    }

    internal sealed class TradeInventoryDelivery
    {
        private readonly IInventoryDepositApi inventory;
        private readonly TradeClientItemPipeline itemPipeline;
        private readonly Func<string> localUuidProvider;

        public TradeInventoryDelivery(IInventoryDepositApi inventory, TradeClientItemPipeline itemPipeline, Func<string> localUuidProvider)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.itemPipeline = itemPipeline ?? throw new ArgumentNullException(nameof(itemPipeline));
            this.localUuidProvider = localUuidProvider ?? throw new ArgumentNullException(nameof(localUuidProvider));
        }

        public InventoryDepositResult Deposit(TradeCompletionEventArgs args, string otherPartyDisplayName)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.TradeId))
                return new InventoryDepositResult
                {
                    Status = InventoryDepositStatus.Rejected,
                    FailureCode = InventoryFailureCode.InvalidRequest,
                    Reason = "Trade completion has no stable ID."
                };

            FrameworkItemPayload[] payloads = args.ItemPayloads != null && args.ItemPayloads.Length > 0
                ? args.ItemPayloads
                : itemPipeline.EncodeTradeItems(args.Items);
            TradeItemSnapshot[] snapshots = args.Items ?? Array.Empty<TradeItemSnapshot>();
            List<InventoryItem> items = new List<InventoryItem>(payloads.Length);
            for (int i = 0; i < payloads.Length; i++)
            {
                FrameworkItemPayload payload = payloads[i];
                if (payload == null || !itemPipeline.CanDecodeItemExactly(payload))
                    return new InventoryDepositResult
                    {
                        Status = InventoryDepositStatus.Rejected,
                        FailureCode = InventoryFailureCode.CodecUnavailable,
                        Reason = "A trade item codec is unavailable."
                    };
                TradeItemSnapshot snapshot = i < snapshots.Length ? snapshots[i] : null;
                items.Add(new InventoryItem
                {
                    CodecId = TradeInventoryCodec.Id,
                    CodecVersion = 1,
                    Payload = TradeInventoryCodec.Encode(payload),
                    Quantity = 1,
                    Label = BuildLabel(snapshot, payload)
                });
            }

            if (items.Count == 0)
                return new InventoryDepositResult { Status = InventoryDepositStatus.AlreadyCommitted };

            string outcome = args.Success ? "completed" : "cancelled";
            string localUuid = localUuidProvider() ?? string.Empty;
            return inventory.TryDeposit(new InventoryDeposit
            {
                DepositId = "trade-delivery:" + outcome + ":" + args.TradeId + ":" + localUuid,
                Source = FrameworkTradeProtocol.Capability,
                OriginKind = args.Success ? "trade-completed" : "trade-return",
                OriginId = args.TradeId,
                OriginUserId = Limit(args.OtherPartyUuid, 100),
                OriginDisplayName = Limit(string.IsNullOrWhiteSpace(otherPartyDisplayName)
                    ? null : TextHelper.StripRichText(otherPartyDisplayName).Trim(), 200),
                Items = items
            });
        }

        public InventoryDeliveryResult DeliverDirect(TradeCompletionEventArgs args, bool dropCurrentMap)
        {
            if (args == null)
                return new InventoryDeliveryResult { Status = InventoryDeliveryStatus.Rejected, Reason = "Trade completion is missing." };

            FrameworkItemPayload[] payloads = args.ItemPayloads != null && args.ItemPayloads.Length > 0
                ? args.ItemPayloads
                : itemPipeline.EncodeTradeItems(args.Items ?? Array.Empty<TradeItemSnapshot>());
            List<Thing> things = new List<Thing>(payloads.Length);
            try
            {
                foreach (FrameworkItemPayload payload in payloads)
                {
                    if (payload == null || !itemPipeline.CanDecodeItemExactly(payload))
                        throw new InvalidOperationException("A trade item codec is unavailable.");
                    things.Add(itemPipeline.DecodeItemExactly(payload));
                }
            }
            catch (Exception exception)
            {
                DiscardUnclaimed(things);
                return new InventoryDeliveryResult
                {
                    Status = InventoryDeliveryStatus.Rejected,
                    Reason = "Trade items could not be restored without loss: " + exception.Message
                };
            }

            Map map = dropCurrentMap ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (!InventoryDropDelivery.TryResolveTradeDropTarget(map, out IntVec3 spot, out string reason))
            {
                DiscardUnclaimed(things);
                return new InventoryDeliveryResult { Status = InventoryDeliveryStatus.Rejected, Reason = reason, Map = map };
            }
            InventoryDeliveryResult result = InventoryDropDelivery.DeliverThings(things, map, spot);
            if (result.Status == InventoryDeliveryStatus.Rejected) DiscardUnclaimed(things);
            return result;
        }

        private static void DiscardUnclaimed(IEnumerable<Thing> things)
        {
            foreach (Thing thing in things ?? Enumerable.Empty<Thing>())
            {
                if (thing != null && !thing.Destroyed && thing.ParentHolder == null && !thing.Spawned)
                    thing.Destroy(DestroyMode.Vanish);
            }
        }

        private static string BuildLabel(TradeItemSnapshot snapshot, FrameworkItemPayload payload)
        {
            if (snapshot != null)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(snapshot.DefName);
                string label = def?.LabelCap ?? snapshot.DefName;
                if (snapshot.StackCount > 1) label += " × " + snapshot.StackCount;
                return Limit(label, 200) ?? "Trade item";
            }
            return Limit(payload?.CodecId, 200) ?? "Trade item";
        }

        private static string Limit(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }
    }
}
