using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using PhinixClient;
using PhinixClient.Framework;

internal static class Program
{
    static int checks;
    static void Check(bool value,string message) { checks++; if(!value)throw new Exception(message); }
    static readonly Dictionary<short,OpCode> Codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)
        .Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>o.Value);
    static List<MethodBase> Calls(MethodBase method)
    {
        var result=new List<MethodBase>();var il=method.GetMethodBody().GetILAsByteArray();
        for(int i=0;i<il.Length;)
        {
            short code=il[i++];if(code==0xfe)code=(short)(0xfe00|il[i++]);var op=Codes[code];
            if(op.OperandType==OperandType.InlineMethod)result.Add(method.Module.ResolveMethod(BitConverter.ToInt32(il,i)));
            switch(op.OperandType)
            {
                case OperandType.InlineNone:break;
                case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:i++;break;
                case OperandType.InlineVar:i+=2;break;
                case OperandType.InlineI8:case OperandType.InlineR:i+=8;break;
                case OperandType.InlineSwitch:int n=BitConverter.ToInt32(il,i);i+=4+4*n;break;
                default:i+=4;break;
            }
        }
        return result;
    }
    static void Main()
    {
        var host=typeof(Client);var assembly=host.Assembly;
        var scopeType=assembly.GetType("PhinixClient.Framework.ClientGameScopeService",true);
        var publish=scopeType.GetMethod("Publish",BindingFlags.NonPublic|BindingFlags.Instance);
        var ctorCalls=Calls(host.GetConstructors().Single());
        Check(!ctorCalls.Contains(publish),"Mod constructor must not publish game state on the asynchronous loading thread");
        var updateCalls=Calls(host.GetMethod("Update"));
        int publication=updateCalls.IndexOf(publish),drain=updateCalls.FindIndex(m=>m.Name=="DrainPendingActions");
        Check(publication>=0 && drain>publication,"Main-thread Update must publish before draining plugin startup/delivery callbacks");
        int main=Thread.CurrentThread.ManagedThreadId,reads=0;object game=null;
        Func<object> current=()=>{reads++;return game;};Func<bool> isMain=()=>Thread.CurrentThread.ManagedThreadId==main;
        object scopes=null,dispatcher=null;Exception failed=null;
        var dispatchType=assembly.GetType("PhinixClient.Framework.ClientMainThreadDispatcher",true);
        bool callback=false;
        var loader=new Thread(()=>{try {
            scopes=Activator.CreateInstance(scopeType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{current,isMain},null);
            dispatcher=Activator.CreateInstance(dispatchType,new object[]{null,isMain});
            ((IClientMainThreadDispatcher)dispatcher).Enqueue(()=>{callback=true;Check(((IClientGameScopeService)scopes).Capture().IsCurrent(),"Queued startup sees the published game");});
            Check(((IClientGameScopeService)scopes).Capture()==null,"Loading thread sees no invented game");
            try {publish.Invoke(scopes,null);throw new Exception("Off-thread publication accepted");}
            catch(TargetInvocationException error) {Check(error.InnerException is InvalidOperationException,"Wrong-thread game access remains rejected");}
        }catch(Exception error){failed=error;}});
        loader.Start();Check(loader.Join(5000),"Loading thread terminates");if(failed!=null)throw failed;
        Check(reads==0 && !callback,"Construction and queued startup must not read Current.Game on loading thread");
        publish.Invoke(scopes,null);Check(((IClientGameScopeService)scopes).Capture()==null,"Main menu has no game token");
        game=new object();publish.Invoke(scopes,null);
        dispatchType.GetMethod("DrainPendingActions").Invoke(dispatcher,null);
        Check(callback,"Startup callback executes on dispatcher drain");
        var token=((IClientGameScopeService)scopes).Capture();game=null;publish.Invoke(scopes,null);
        Check(!token.IsCurrent() && ((IClientGameScopeService)scopes).Capture()==null,"Return to menu invalidates old delivery scope");
        Console.WriteLine("HOST STARTUP RESULT passed; checks="+checks+"; actual constructor/Update IL and service/dispatcher; no native GUI or real-game claim");
    }
}
