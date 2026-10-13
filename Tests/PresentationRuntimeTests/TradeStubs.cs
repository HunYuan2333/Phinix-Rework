using System;
using System.Collections.Generic;
namespace Utils
{
    public static class TextHelper {public static string StripRichText(string text)=>System.Text.RegularExpressions.Regex.Replace(text,"<.*?>","");}
    public enum LogLevel {ERROR}
    public sealed class LogEventArgs {public LogEventArgs(string text,LogLevel level) {}}
}
namespace Utils.Framework.ManagedExtensions
{public static class ManagedExtensionPaths {public static string Hash(byte[] data)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();}}
namespace UserManagement
{
    public struct ImmutableUser
    {public string Uuid,DisplayName;public bool LoggedIn,AcceptingTrades;public ImmutableUser(string id,string name,bool online,bool trades){Uuid=id;DisplayName=name;LoggedIn=online;AcceptingTrades=trades;}}
    public sealed class UserDisplayNameChangedEventArgs : EventArgs {public string Uuid,NewDisplayName;}
}
namespace PhinixClient.Trade
{
    public enum TradeFailureReason {None,Rejected}
    public enum TradeItemQuality {None,Awful,Poor,Normal,Good,Excellent,Masterwork,Legendary}
    public sealed class TradeItemSnapshot
    {
        public string DefName,StuffDefName,StateCodecId;public int StackCount,HitPoints;public TradeItemQuality Quality;public TradeItemSnapshot InnerItem;
        public TradeItemSnapshot(string defName="",int stackCount=1,int hitPoints=0,TradeItemQuality quality=TradeItemQuality.None,string stuffDefName="",TradeItemSnapshot innerItem=null,string stateCodecId="")
        {DefName=defName;StackCount=stackCount;HitPoints=hitPoints;Quality=quality;StuffDefName=stuffDefName;InnerItem=innerItem;StateCodecId=stateCodecId;}
    }
    public sealed class ClientTradeSnapshot
    {
        public string TradeId;public UserManagement.ImmutableUser OtherParty;public TradeItemSnapshot[] ItemsOnOffer,OtherPartyItemsOnOffer;public bool Accepted,OtherPartyAccepted;
        public string OtherPartyUuid=>OtherParty.Uuid;public string OtherPartyDisplayName=>OtherParty.DisplayName;
        public ClientTradeSnapshot(string id,UserManagement.ImmutableUser user,TradeItemSnapshot[] ours,TradeItemSnapshot[] theirs,bool accepted=false,bool otherAccepted=false)
        {TradeId=id;OtherParty=user;ItemsOnOffer=ours;OtherPartyItemsOnOffer=theirs;Accepted=accepted;OtherPartyAccepted=otherAccepted;}
    }
    public sealed class TradesSyncedEventArgs : EventArgs {public ClientTradeSnapshot[] Trades;}
    public sealed class TradeCreationEventArgs : EventArgs {public ClientTradeSnapshot Trade;}
    public sealed class TradeCompletionEventArgs : EventArgs {public string TradeId;}
    public sealed class TradeUpdateEventArgs : EventArgs {public ClientTradeSnapshot Trade;public TradeFailureReason FailureReason;public string FailureMessage,Token;}
}
namespace Phinix.TradeExtension.Client
{
    public interface IClientTradeService
    {
        IEnumerable<PhinixClient.Trade.ClientTradeSnapshot> GetTrades();void CancelTrade(string id);
        event EventHandler<PhinixClient.Trade.TradesSyncedEventArgs> OnTradesSynced;
        event EventHandler<PhinixClient.Trade.TradeCreationEventArgs> OnTradeCreationSuccess;
        event EventHandler<PhinixClient.Trade.TradeCompletionEventArgs> OnTradeCompleted,OnTradeCancelled;
        event EventHandler<PhinixClient.Trade.TradeUpdateEventArgs> OnTradeUpdateSuccess,OnTradeUpdateFailure;
    }
    public interface ITradeUiHostContext
    {
        IClientTradeService TradeService {get;}
        event EventHandler OnDisconnect;
        event EventHandler<UserManagement.UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged;
        void OpenTradeWindow(PhinixClient.Trade.ClientTradeSnapshot trade);void Log(Utils.LogEventArgs args);
    }
}

namespace Verse
{
    public sealed class ThingDef {public string label;public UnityEngine.Texture2D uiIcon;}
    public static class DefDatabase<T> where T:class {public static T GetNamedSilentFail(string id)=>null;}
}
