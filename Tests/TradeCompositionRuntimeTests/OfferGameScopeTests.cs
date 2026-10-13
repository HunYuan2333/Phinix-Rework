using System;
using System.Reflection;

internal static partial class Program
{
    private static object NewGameScope(Type module, Func<object> game, Func<bool> active)
    {
        var type=module.Assembly.GetType("Phinix.TradeExtension.Client.TradeOfferGameScope",true);
        return Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{game,active},null);
    }
    private static bool ValidGameScope(object scope) => (bool)scope.GetType().GetMethod("IsValid",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scope,null);
    private static void CheckOfferGameScope(Type module)
    {
        object first=new object(), current=first;bool active=true;
        var scope=NewGameScope(module,()=>current,()=>active);
        Assert(ValidGameScope(scope),"A live operation retains its originating game.");
        current=new object();
        Assert(!ValidGameScope(scope),"Another game invalidates an old settlement target.");
        current=first;
        Assert(!ValidGameScope(scope),"An invalidated operation cannot regain authority by restoring an object reference.");
        scope=NewGameScope(module,()=>current,()=>active);active=false;
        Assert(!ValidGameScope(scope),"Module lifetime expiration invalidates settlement.");active=true;
        Assert(!ValidGameScope(scope),"Module restart cannot revive an old scope.");
        current=null;scope=NewGameScope(module,()=>current,()=>true);current=first;
        Assert(!ValidGameScope(scope),"An operation created without a game cannot acquire one later.");
        scope=NewGameScope(module,()=>current,()=>true);current=null;
        Assert(!ValidGameScope(scope),"Returning to the main menu invalidates settlement.");
    }
}
