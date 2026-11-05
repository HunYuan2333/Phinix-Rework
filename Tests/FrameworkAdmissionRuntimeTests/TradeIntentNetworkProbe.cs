using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Collections.Generic;
using System.Security.Cryptography;
using Phinix.TradeExtension;
using Utils.Framework;

// Own literal wire expectations, real peer transport, actual client journal/query DLL.
// The server Adapter never supplies expected fingerprints, status, or original IDs.
internal static partial class TradeNetworkProbe
{
    internal static void RunIntents(int port,string phase,string statePath,string clientDll,string journalPath)
    {
        bool initial=phase=="initial";
        var state=initial?new State{Keys=new[]{Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N")}}:JsonSerializer.Deserialize<State>(File.ReadAllText(statePath));
        using var a=new Peer(port,state.Keys[0],queries:true);using var b=new Peer(port,state.Keys[1],queries:true);
        Wait(a,b,()=>a.Ready&&b.Ready);
        var product=Assembly.LoadFrom(Path.GetFullPath(clientDll));
        Check(SHA256.HashData(File.ReadAllBytes(product.Location)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(clientDll))),"Probe loaded different client DLL bytes");
        var jt=product.GetType("Phinix.TradeExtension.Client.TradeIntentJournal",true);
        var qt=product.GetType("Phinix.TradeExtension.Client.TradeOperationQueryClient",true);
        object journal=Activator.CreateInstance(jt,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{journalPath},null);
        string endpoint="127.0.0.1:"+port;
        object Call(string method,params object[] args)=>jt.GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(journal,args);
        string Field(object record,string name)=>(string)record.GetType().GetField(name).GetValue(record);
        object Prepare(FrameworkPacket packet,FrameworkPacket[] items)
        {var record=Call("Prepare",endpoint,a.Principal,packet,items);Call("BeginAttempt",record);return record;}
        if(initial)
        {
            state.Principals=new[]{a.Principal,b.Principal};state.IntentIds=new[]{Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N")};
            var create=new FrameworkPacket{Kind=FrameworkProtocol.KindCommand,CommandKind=global::Phinix.Framework.FrameworkCommandKind.Request,MessageType=FrameworkTradeProtocol.CreateRequestType,MessageId=state.IntentIds[0],PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateRequest{OtherPartyUuid=b.Principal})};
            Prepare(create,Array.Empty<FrameworkPacket>());a.Send(create);Wait(a,b,()=>a.Find(FrameworkTradeProtocol.CreateResponseType,create.MessageId)!=null);
            var response=FrameworkSerialization.DeserializePayload<FrameworkTradeCreateResponse>(a.Find(FrameworkTradeProtocol.CreateResponseType,create.MessageId).PayloadJson);
            Check(response.Success,"Journaled creation refused");state.Trade=response.TradeId;
            var item=new FrameworkPacket{Kind=FrameworkProtocol.KindItem,MessageType=FrameworkTradeProtocol.Capability,MessageId=Guid.NewGuid().ToString("N"),PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkItemPayload{CodecId="synthetic.opaque",PayloadBytes=new byte[]{17,0,255}})};
            var offer=new FrameworkPacket{Kind=FrameworkProtocol.KindCommand,CommandKind=global::Phinix.Framework.FrameworkCommandKind.Request,MessageType=FrameworkTradeProtocol.OfferUpdateRequestType,MessageId=state.IntentIds[1],PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateRequest{TradeId=state.Trade,ItemPacketRefs=new List<string>{item.MessageId}})};
            Prepare(offer,new[]{item});a.Send(item);a.Send(offer);Wait(a,b,()=>a.Find(FrameworkTradeProtocol.OfferUpdateResponseType,offer.MessageId)!=null);
            Check(FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateResponse>(a.Find(FrameworkTradeProtocol.OfferUpdateResponseType,offer.MessageId).PayloadJson).Success,"Journaled offer refused");
            // Fault barrier: deliberately do not apply either authoritative response
            // to the client journal; the next phase is a fresh client process.
            foreach(object record in (Array)Call("Read",endpoint,a.Principal))Check((string)Call("State",record)=="OutcomeUnknown","Lost response became a local success");
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write};if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using var file=new FileStream(statePath,options);JsonSerializer.Serialize(file,state);
        }
        else
        {
            Check(a.Principal==state.Principals[0]&&b.Principal==state.Principals[1],"Client restart changed stable identity");
            var caps=FrameworkSerialization.DeserializePayload<FrameworkCapabilitiesPayload>(a.Packets.First(p=>p.Kind==FrameworkProtocol.KindCapabilities).PayloadJson).Capabilities;
            Check(caps.Contains("trade.operation.query.request")&&caps.Contains("trade.operation.query.response"),"Query capabilities not negotiated");
            FrameworkPacket sent=null;
            object queries=Activator.CreateInstance(qt,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{journal,(Func<string>)(()=>endpoint),(Func<string>)(()=>a.Principal),(Func<string>)(()=>a.SessionId),(Func<bool>)(()=>a.Ready),(Func<FrameworkPacket,bool>)(p=>{sent=p;p.Kind=FrameworkProtocol.KindCommand;a.Send(p);return true;})},null);
            void Query(object record)
            {
                qt.GetMethod("Query",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(queries,new object[]{Field(record,"OperationId")});
                Wait(a,b,()=>a.Find("trade.operation.query.response",sent.GetCorrelationId())!=null);
                var packet=a.Find("trade.operation.query.response",sent.GetCorrelationId());
                qt.GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(queries,new object[]{packet});
                using var json=JsonDocument.Parse(packet.PayloadJson);
                if(state.IntentIds.Contains(Field(record,"OperationId")))
                {
                    Check(json.RootElement.GetProperty("TradeId").GetString()==state.Trade&&json.RootElement.GetProperty("Success").GetBoolean(),"Original query result differs");
                    Check(json.RootElement.GetProperty("SnapshotVersion").GetInt64()==(Field(record,"OperationId")==state.IntentIds[0]?1:2),"Query returned latest state instead of original result");
                }
            }
            var records=((Array)Call("Read",endpoint,a.Principal)).Cast<object>().Where(r=>state.IntentIds.Contains(Field(r,"OperationId"))).ToArray();
            Check(records.Length==2,"Original durable intents lost across process restart");
            foreach(object record in records)
            {
                Check((string)Call("State",record)==(phase=="recover"?"OutcomeUnknown":"CommittedSuccess"),"Intent result recovery state differs");
                Query(record);Check((string)Call("State",record)=="CommittedSuccess","Queried result was not durably associated");
            }
            if(phase=="recover")
            {
                var unsent=new FrameworkPacket{MessageId="never-sent",MessageType=FrameworkTradeProtocol.CreateRequestType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateRequest{OtherPartyUuid=b.Principal})};
                object record=Call("Prepare",endpoint,a.Principal,unsent,Array.Empty<FrameworkPacket>());Query(record);
                Check((string)Call("State",record)=="NotFoundObserved","Missing query became final rejection/refund permission");
            }
        }
        Console.WriteLine("CLIENT INTENT NETWORK RESULT passed; phase="+phase+"; actual client journal/query DLL; original-result/no-resend; no game delivery proof");
    }
}
