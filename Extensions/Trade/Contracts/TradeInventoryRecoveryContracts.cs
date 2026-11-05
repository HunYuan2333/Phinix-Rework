using System.Collections.Generic;
using System.Linq;

namespace Phinix.TradeExtension
{
    public sealed class TradeInventoryRecoveryLine
    {
        public TradeInventoryRecoveryLine(string entryId,long units){EntryId=entryId;Units=units;}
        public string EntryId {get;}
        public long Units {get;}
    }
    // Existing inventory journal facts; no acknowledgement, physical escrow or settlement authority.
    public sealed class TradeInventoryRecoveryRecord
    {
        public TradeInventoryRecoveryRecord(string reservationId,string operationId,string state,string evidence,
            string tradeId,string correlationToken,IEnumerable<TradeInventoryRecoveryLine> lines)
            :this(reservationId,operationId,state,evidence,tradeId,correlationToken,lines,"Reservation",null) {}
        public TradeInventoryRecoveryRecord(string reservationId,string operationId,string state,string evidence,
            string tradeId,string correlationToken,IEnumerable<TradeInventoryRecoveryLine> lines,string recordKind,string saveBinding)
        {ReservationId=reservationId;OperationId=operationId;State=state;Evidence=evidence;TradeId=tradeId;CorrelationToken=correlationToken;
            Lines=new List<TradeInventoryRecoveryLine>(lines??Enumerable.Empty<TradeInventoryRecoveryLine>()).AsReadOnly();RecordKind=recordKind;SaveBinding=saveBinding;}
        public string ReservationId {get;} public string OperationId {get;}
        public string State {get;} public string Evidence {get;}
        public string TradeId {get;} public string CorrelationToken {get;}
        public IReadOnlyList<TradeInventoryRecoveryLine> Lines {get;}
        public string RecordKind {get;} public string SaveBinding {get;}
    }
    public interface ITradeInventoryRecoveryApi
    {
        long Revision {get;}
        IReadOnlyList<TradeInventoryRecoveryRecord> Capture();
    }
}

namespace Phinix.TradeExtension
{
    // Read-only server reconciliation. Submission is never an asset retry/refund.
    public interface ITradeOperationQueryApi
    {
        bool CanQueryOperation { get; }
        void QueryOperation(string operationId);
    }
}
