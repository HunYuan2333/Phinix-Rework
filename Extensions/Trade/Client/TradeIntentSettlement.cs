using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Phinix.InventoryExtension;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    [DataContract] internal sealed class TradeReservationBinding
    {
        [DataMember(Order=0)] public string IntentChecksum;
        [DataMember(Order=1)] public string SaveIdentity;
        [DataMember(Order=2)] public string JournalIdentity;
        [DataMember(Order=3)] public string ReservationId;
        [DataMember(Order=4)] public string ReservationOperation;
        [DataMember(Order=5)] public string ItemsJson;
        [DataMember(Order=6)] public string Checksum;
    }
    internal sealed partial class TradeIntentJournal
    {
        internal event Action Changed;
        private static string ReservationHash(TradeReservationBinding b)=>Hash(FrameworkSerialization.SerializePayload(new TradeReservationBinding
        {IntentChecksum=b.IntentChecksum,SaveIdentity=b.SaveIdentity,JournalIdentity=b.JournalIdentity,ReservationId=b.ReservationId,ReservationOperation=b.ReservationOperation,ItemsJson=b.ItemsJson}));
        internal void BindReservation(TradeIntentRecord intent,InventoryStatus status,InventoryReservation reservation)
        {
            lock(gate)
            {
                intent=Resolve(intent);
                if(!status.CanWrite||status.SaveIdentity?.Length!=64||!Guid.TryParseExact(status.JournalIdentity,"N",out _)||reservation.OwnerExtensionId!="builtin.trade"||reservation.Purpose!="trade.offer")throw new InvalidDataException("Durable reservation binding unavailable.");
                var b=new TradeReservationBinding {IntentChecksum=intent.Checksum,SaveIdentity=status.SaveIdentity,JournalIdentity=status.JournalIdentity,ReservationId=reservation.ReservationId,
                    ReservationOperation=reservation.OperationId,ItemsJson=FrameworkSerialization.SerializePayload(reservation.Items)};b.Checksum=ReservationHash(b);
                string path=Path.ChangeExtension(PathFor(intent),".reservation");string json=FrameworkSerialization.SerializePayload(b);
                if(File.Exists(path)){Private(path,384);if(File.ReadAllText(path)!=json)throw new InvalidDataException("Reservation binding conflicts.");return;}
                // Same private, flushed, non-overwriting publication as original intent evidence.
                byte[] bytes=System.Text.Encoding.UTF8.GetBytes(json);
                if(bytes.Length>MaxRecord)throw new IOException("Reservation binding exceeds budget.");
                Publish(path,bytes);
            }
        }
        internal TradeReservationBinding ReservationBinding(TradeIntentRecord intent)
        {
            lock(gate)
            {
                string path=Path.ChangeExtension(PathFor(intent),".reservation");if(!File.Exists(path))return null;
                Private(path,384);if(new FileInfo(path).Length>MaxRecord)throw new InvalidDataException("Reservation binding exceeds budget.");
                var b=FrameworkSerialization.DeserializePayload<TradeReservationBinding>(File.ReadAllText(path));
                if(b==null||b.IntentChecksum!=intent.Checksum||b.Checksum!=ReservationHash(b)||b.SaveIdentity?.Length!=64||!Guid.TryParseExact(b.JournalIdentity,"N",out _))throw new InvalidDataException("Invalid reservation binding.");
                return b;
            }
        }
        internal TradeOperationQueryResponse DurableResult(TradeIntentRecord intent)
        {lock(gate)return File.Exists(Path.ChangeExtension(PathFor(intent),".query"))?ReadQuery(intent):null;}
        private void ValidateReservationEvidence()
        {foreach(string path in Directory.GetFiles(directory,"*.reservation"))ReservationBinding(ReadFile(Path.ChangeExtension(path,".intent")));}
    }
}
