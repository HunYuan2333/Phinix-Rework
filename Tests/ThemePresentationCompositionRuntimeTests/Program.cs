using System;
using System.Linq;
using System.Reflection;
using PhinixClient.Framework;
using Utils.Framework;

[PhinixExtension("test.theme")]
public sealed class Consumer : IPhinixExtensionModule
{
    public string ExtensionId => "test.theme";
    public void Register(IExtensionBuilder builder) { }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);checks++;}
    private static object New(Type t,params object[] args)=>Activator.CreateInstance(t,BindingFlags.Instance|BindingFlags.NonPublic,null,args,null);
    private static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(target,args);
    private static int Main()
    {
        var assembly=Assembly.Load("PhinixClient");
        var ownerType=assembly.GetType("PhinixClient.HostThemePresentationOwner",true);
        var pointType=assembly.GetType("PhinixClient.Framework.ClientExtensionPointService",true);
        var lifeType=assembly.GetType("PhinixClient.Framework.ClientLifetimeService",true);
        var serviceType=assembly.GetType("PhinixClient.ThemePresentationServices",true);
        Func<bool> main=()=>true;Action<string> log=message=>throw new Exception(message);
        var points=New(pointType,main,log);var life=New(lifeType,main,log);
        Call(points,"SetModuleTypes",new object[]{new[]{ownerType,typeof(Consumer)}});
        Call(life,"SetModuleTypes",new object[]{new[]{ownerType,typeof(Consumer)}});
        Check(ownerType.GetConstructor(Type.EmptyTypes)==null,"Permanent owner must not be discovered as optional module");
        var services=New(serviceType,null,points,life);
        var owner=(IPhinixExtensionModule)serviceType.GetField("Owner",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(services);
        var point=(IClientActionPoint<ClientThemePresentationActionContext>)serviceType.GetField("Point",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(services);
        var directory=(IClientExtensionPointService)points;
        Check(directory.CapturePoints().Single().OwnerId=="builtin.host","One normal Host-owned point");
        var consumer=new Consumer();Call(points,"OnActivating",consumer);Call(life,"OnActivating",consumer);
        var contributor=directory.ForModule(consumer);int count=0;
        contributor.ContributeActions<ClientThemePresentationActionContext>(ClientThemePresentationActionContext.PointId,c=>new[]{new ClientActionDefinition("count","Count",()=>count++)});
        var registered=directory.CapturePoints().Single();
        Check(registered.ContextType==typeof(ClientThemePresentationActionContext)&&registered.ContributorCount==2,"Third party uses same public typed point");
        // Collect/execute enters ClientGuiScope and requires native Unity GUI. Renderer stub checks
        // cover action freshness separately; this actual-DLL probe stays on registration/lifetime.
        Call(points,"OnStopped",consumer);
        Check(directory.CapturePoints().Single().ContributorCount==1,"Consumer stop preserves permanent Host point");
        bool contributionRefused=false;try {contributor.ContributeActions<ClientThemePresentationActionContext>(ClientThemePresentationActionContext.PointId,c=>Array.Empty<ClientActionDefinition>());}catch(InvalidOperationException){contributionRefused=true;}
        Check(contributionRefused&&count==0,"Stopped consumer cannot contribute or execute business callbacks during registration");
        var lifetime=(IClientLifetimeService)life;var scope=lifetime.ForModule(owner);
        ((IDisposable)services).Dispose();((IDisposable)services).Dispose();
        Check(scope.IsClosed,"Host stop closes owner session lifetime");
        Check(directory.CapturePoints().Count==0,"Host stop removes point");
        bool refused=false;try {lifetime.ForModule(owner);}catch(InvalidOperationException){refused=true;}
        Check(refused,"Stopped permanent owner cannot create another session");
        Call(life,"OnStopped",consumer);contributor.Dispose();
        Console.WriteLine("Theme presentation composition: "+checks+" assertions passed (real Host DLL; no native UI).");return 0;
    }
}
