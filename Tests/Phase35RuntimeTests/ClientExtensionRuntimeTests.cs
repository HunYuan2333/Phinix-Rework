using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using PhinixClient.Framework;
using Utils.Framework;

internal static partial class Program
{
    private sealed class TypeSetDiscoveryPolicy : IExtensionDiscoveryPolicy
    {
        private readonly HashSet<Type> types;
        internal TypeSetDiscoveryPolicy(params Type[] types) { this.types = new HashSet<Type>(types); }
        public bool ShouldScanAssembly(Assembly assembly) => assembly == typeof(Program).Assembly;
        public bool ShouldDiscoverType(Type type) => types.Contains(type);
    }

    private static void AssertClientExtensionRuntimeLifecycle()
    {
        int mainThread = Thread.CurrentThread.ManagedThreadId;
        Func<bool> isMainThread = () => Thread.CurrentThread.ManagedThreadId == mainThread;
        var host = new ExtensionHostContext();
        var source = new ClientLifecycleFixtures.EventSource();
        host.AddService(source);
        host.AddService<IExtensionDiscoveryPolicy>(new TypeSetDiscoveryPolicy(
            typeof(ClientLifecycleFixtures.Provider), typeof(ClientLifecycleFixtures.Consumer),
            typeof(ClientLifecycleFixtures.RegisterFailure), typeof(ClientLifecycleFixtures.ActivateFailure),
            typeof(ClientLifecycleFixtures.RegisterOnly), typeof(ClientLifecycleFixtures.ConstructorFailure)));
        ClientLifecycleFixtures.Events.Clear();
        var runtime = new ClientExtensionRuntime(host, isMainThread);
        Assert(ClientLifecycleFixtures.Events.Count == 0 && runtime.Extensions.Modules.Count == 0,
            "Constructing the production runtime must not construct, register or activate modules.");
        runtime.Start();
        AssertState(runtime.Extensions, "runtime.a-provider", ExtensionModuleState.Active);
        AssertState(runtime.Extensions, "runtime.b-consumer", ExtensionModuleState.Active);
        AssertState(runtime.Extensions, "runtime.register-failure", ExtensionModuleState.Failed);
        AssertState(runtime.Extensions, "runtime.activate-failure", ExtensionModuleState.Failed);
        AssertState(runtime.Extensions, "runtime.constructor-failure", ExtensionModuleState.Failed);
        Assert(ClientLifecycleFixtures.Events.Count(e => e == "stop:register-failure") == 1,
            "A partial registration must invoke the module's cleanup exactly once.");
        Assert(ClientLifecycleFixtures.Events.Count(e => e == "stop:activate-failure") == 1 && source.Subscribers == 0,
            "A partial activation must remove its borrowed event subscription even if shutdown throws.");
        Assert(!host.TryResolveApi<ClientLifecycleFixtures.IFailedApi>(out _),
            "Partial startup failures must revoke APIs despite cleanup errors.");
        int afterStart = ClientLifecycleFixtures.Events.Count;
        runtime.Start();
        Assert(ClientLifecycleFixtures.Events.Count == afterStart, "Repeated Start must not reconstruct or resubscribe modules.");
        Assert(ThrowsOnWorker(runtime.Stop) is InvalidOperationException,
            "Stopping active modules off the main thread must be rejected before cleanup.");
        Assert(ClientLifecycleFixtures.Events.Count == afterStart, "Rejected worker cleanup must leave main-thread cleanup available.");
        var observer = new ClientLifecycleFixtures.FailingObserver();
        host.AddService<IExtensionModuleLifecycleObserver>(observer);
        runtime.Stop();
        Assert(ClientLifecycleFixtures.Events.IndexOf("stop:consumer") < ClientLifecycleFixtures.Events.IndexOf("stop:provider"),
            "Production Stop must release consumers before providers even if consumer cleanup throws.");
        Assert(!host.TryResolveApi<ClientLifecycleFixtures.IProviderApi>(out _) &&
            !host.TryResolveApi<ClientLifecycleFixtures.IRegisterOnlyApi>(out _),
            "Stop must revoke active and registration-only APIs.");
        Assert(!source.Disposed && host.GetRequiredService<ClientLifecycleFixtures.EventSource>() == source,
            "Stopping the plugin runtime must not dispose or remove borrowed host services.");
        Assert(runtime.Extensions.Warnings.Any(w => w.Contains("cleanup failed")), "Cleanup exceptions must remain observable.");
        Assert(observer.Stops == 3 && runtime.Extensions.Warnings.Any(w => w.Contains("host resource cleanup failed")),
            "Observer cleanup errors must not block cleanup of other modules or registration-only APIs.");
        int afterStop = ClientLifecycleFixtures.Events.Count;
        runtime.Stop(); runtime.Dispose();
        Assert(ClientLifecycleFixtures.Events.Count == afterStop, "Repeated Stop and Dispose must not repeat failed cleanup.");
        bool restartRejected = false;
        try { runtime.Start(); } catch (ObjectDisposedException) { restartRejected = true; }
        Assert(restartRejected, "A stopped runtime is terminal; a new host must construct a new runtime.");

        var idle = new ClientExtensionRuntime(new ExtensionHostContext(), isMainThread);
        Assert(ThrowsOnWorker(idle.Start) is InvalidOperationException, "Wrong-thread Start must fail before discovery.");
        Assert(idle.Extensions.Modules.Count == 0, "Wrong-thread startup must not construct modules.");
        Assert(ThrowsOnWorker(idle.Dispose) == null, "An unstarted runtime has no game resources and may be disposed on a worker.");

        var interruptedHost = new ExtensionHostContext();
        interruptedHost.AddService<IExtensionDiscoveryPolicy>(new TypeSetDiscoveryPolicy(
            typeof(ClientLifecycleFixtures.Provider), typeof(ClientLifecycleFixtures.Interrupted)));
        interruptedHost.AddService<IExtensionActivationPolicy>(new ClientLifecycleFixtures.InterruptPolicy());
        ClientLifecycleFixtures.Events.Clear();
        var interrupted = new ClientExtensionRuntime(interruptedHost, isMainThread);
        bool failed = false;
        try { interrupted.Start(); } catch (InvalidOperationException) { failed = true; }
        Assert(failed && ClientLifecycleFixtures.Events.Contains("stop:provider"),
            "A fatal discovery interruption must clean up modules already registered before activation.");
        Assert(!interruptedHost.TryResolveApi<ClientLifecycleFixtures.IProviderApi>(out _),
            "Fatal discovery cleanup must revoke previously published APIs.");

        var original = new object(); var replacement = new object();
        host.AddService<object>(original); host.AddService<object>(replacement);
        Assert(!host.RemoveService(original) && host.GetRequiredService<object>() == replacement,
            "An old owner must not remove a replacement host service.");
        Assert(host.RemoveService(replacement) && !host.TryGetService<object>(out _),
            "Removing an owned service must detach it without disposing it.");
    }

    private static Exception ThrowsOnWorker(Action action)
    {
        Exception error = null;
        var worker = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        worker.Start();
        Assert(worker.Join(5000), "Lifecycle worker must finish promptly.");
        return error;
    }
}

internal static class ClientLifecycleFixtures
{
    internal static readonly List<string> Events = new List<string>();
    public interface IProviderApi { }
    public interface IFailedApi { }
    public interface IRegisterOnlyApi { }
    public sealed class EventSource : IDisposable
    {
        public event Action Changed;
        internal int Subscribers => Changed == null ? 0 : Changed.GetInvocationList().Length;
        internal bool Disposed;
        public void Dispose() { Disposed = true; }
    }

    [PhinixExtension("runtime.a-provider")]
    public sealed class Provider : IPhinixExtensionModule, IActivatablePhinixExtensionModule, IProviderApi
    {
        public Provider() { Events.Add("construct:provider"); }
        public string ExtensionId => "runtime.a-provider";
        public void Register(IExtensionBuilder builder) { Events.Add("register:provider"); builder.RegisterApi<IProviderApi>(this); }
        public void Activate(ExtensionHostContext context) { Events.Add("activate:provider"); }
        public void Shutdown(ExtensionHostContext context) { Events.Add("stop:provider"); }
    }

    [PhinixExtension("runtime.b-consumer", DependsOn = new[] { "runtime.a-provider" })]
    public sealed class Consumer : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        public string ExtensionId => "runtime.b-consumer";
        public void Register(IExtensionBuilder builder) { }
        public void Activate(ExtensionHostContext context)
        {
            if (!context.TryResolveApi<IProviderApi>(out _)) throw new InvalidOperationException("missing provider");
            Events.Add("activate:consumer");
        }
        public void Shutdown(ExtensionHostContext context) { Events.Add("stop:consumer"); throw new InvalidOperationException("consumer cleanup failure"); }
    }

    [PhinixExtension("runtime.register-failure")]
    public sealed class RegisterFailure : IPhinixExtensionModule, IActivatablePhinixExtensionModule, IFailedApi
    {
        public string ExtensionId => "runtime.register-failure";
        public void Register(IExtensionBuilder builder) { builder.RegisterApi<IFailedApi>(this); throw new InvalidOperationException("partial registration"); }
        public void Activate(ExtensionHostContext context) { throw new Exception("must not activate"); }
        public void Shutdown(ExtensionHostContext context) { Events.Add("stop:register-failure"); }
    }

    [PhinixExtension("runtime.activate-failure")]
    public sealed class ActivateFailure : IPhinixExtensionModule, IActivatablePhinixExtensionModule, IFailedApi
    {
        private EventSource source;
        public string ExtensionId => "runtime.activate-failure";
        public void Register(IExtensionBuilder builder) { builder.RegisterApi<IFailedApi>(this); }
        public void Activate(ExtensionHostContext context)
        {
            source = context.GetRequiredService<EventSource>(); source.Changed += OnChanged;
            throw new InvalidOperationException("partial activation");
        }
        private void OnChanged() { }
        public void Shutdown(ExtensionHostContext context)
        {
            Events.Add("stop:activate-failure"); if (source != null) source.Changed -= OnChanged;
            throw new InvalidOperationException("activation cleanup failure");
        }
    }

    [PhinixExtension("runtime.register-only")]
    public sealed class RegisterOnly : IPhinixExtensionModule, IRegisterOnlyApi
    {
        public string ExtensionId => "runtime.register-only";
        public void Register(IExtensionBuilder builder) { builder.RegisterApi<IRegisterOnlyApi>(this); }
    }

    [PhinixExtension("runtime.constructor-failure")]
    public sealed class ConstructorFailure : IPhinixExtensionModule
    {
        public ConstructorFailure() { throw new InvalidOperationException("construction failure"); }
        public string ExtensionId => "runtime.constructor-failure";
        public void Register(IExtensionBuilder builder) { throw new Exception("must not register"); }
    }

    [PhinixExtension("runtime.z-interrupted", DependsOn = new[] { "runtime.a-provider" })]
    public sealed class Interrupted : IPhinixExtensionModule
    {
        public string ExtensionId => "runtime.z-interrupted";
        public void Register(IExtensionBuilder builder) { throw new Exception("must not register"); }
    }
    internal sealed class InterruptPolicy : IExtensionActivationPolicy
    {
        public IReadOnlyCollection<string> DisabledExtensions => new string[0];
        public bool ShouldActivate(string extensionId, out string reason)
        {
            reason = null;
            if (extensionId == "runtime.z-interrupted") throw new InvalidOperationException("discovery interrupted");
            return true;
        }
    }
    internal sealed class FailingObserver : IExtensionModuleLifecycleObserver
    {
        internal int Stops;
        public void OnActivating(IPhinixExtensionModule module) { }
        public void OnStopped(IPhinixExtensionModule module) { Stops++; throw new InvalidOperationException("host cleanup failure"); }
    }
}
