using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhinixClient.Framework;
using Utils.Framework;

internal static partial class Program
{
    private static void ProbeProductionComposition()
    {
        bool mainThread = true;
        var errors = new List<Exception>();
        var factory = new ClientCompositionFactory(() => mainThread, errors.Add);
        AsyncOnly.Constructions = 0;
        ExpectFailure(() => factory.CreateScope(local => local.Register<AsyncOnly, AsyncOnly>()),
            "Async-only owned resources must be rejected before construction, never silently skipped by guarded release.");
        Assert(AsyncOnly.Constructions == 0, "Rejected asynchronous resources must not acquire work during registration.");
        var host = new BorrowedApi();
        var sibling = new BorrowedApi();
        IClientCompositionBuilder escaped = null;
        DisabledService.Constructions = 0;
        var scope = factory.CreateScope(local =>
        {
            escaped = local;
            local.Borrow<IHostApi>(host);
            local.Borrow<ISiblingApi>(sibling);
            local.Register<PluginService, PluginService>();
            local.Register<DisabledService, DisabledService>();
        });
        Assert(host.Subscribers == 0 && DisabledService.Constructions == 0,
            "Production composition must remain passive and lazy.");
        ExpectFailure(() => escaped.Register<Dependency, Dependency>(), "Registration must close after scope construction.");
        var service = scope.Resolve<PluginService>();
        Assert(ReferenceEquals(service, scope.Resolve<PluginService>()) && host.Subscribers == 0,
            "Production graph resolution is single-instance and does not start work.");
        service.Start(); service.Start();
        Assert(host.Subscribers == 1, "Production activation must not duplicate subscriptions.");
        mainThread = false;
        ExpectFailure(() => factory.CreateScope(_ => { }), "Wrong-thread composition must be rejected.");
        ExpectFailure(() => scope.Resolve<PluginService>(), "Wrong-thread resolution must be rejected.");
        ExpectFailure(() => scope.Dispose(), "Wrong-thread cleanup must be rejected without mutating ownership.");
        ExpectFailure(() => factory.Dispose(), "Wrong-thread factory cleanup must be rejected.");
        mainThread = true;
        var token = service.Token;
        scope.Dispose(); scope.Dispose();
        Assert(host.Subscribers == 0 && token.IsCancellationRequested,
            "Production scope cleanup must stop subscriptions and cancel owned work.");
        Assert(!host.Disposed && !sibling.Disposed, "Production cleanup must preserve borrowed resources.");
        ExpectFailure(() => scope.Resolve<PluginService>(), "Stopped scope is terminal.");
        Assert(DisabledService.Constructions == 0, "Cleanup must never instantiate unused services.");

        Stops.Clear();
        var partial = factory.CreateScope(local =>
        {
            local.Register<Dependency, Dependency>();
            local.Register<BrokenConsumer, BrokenConsumer>();
        });
        ExpectFailure(() => partial.Resolve<BrokenConsumer>(), "Production resolution exposes constructor failure.");
        partial.Dispose();
        Assert(Stops.SequenceEqual(new[] { "dependency" }), "Partial resolution owns and releases completed dependencies once.");

        Stops.Clear();
        var failing = factory.CreateScope(local =>
        {
            local.Register<Dependency, Dependency>();
            local.Register<ThrowingConsumer, ThrowingConsumer>();
        });
        failing.Resolve<ThrowingConsumer>();
        failing.Dispose(); failing.Dispose();
        Assert(errors.Count == 1 && Stops.SequenceEqual(new[] { "throwing-consumer", "dependency" }),
            "Production guarded release continues consumer-first cleanup and reports each failure once.");

        Stops.Clear();
        var last = factory.CreateScope(local =>
        {
            local.Register<Dependency, Dependency>();
            local.Register<Consumer, Consumer>();
        });
        last.Resolve<Consumer>();
        factory.Dispose(); factory.Dispose();
        Assert(Stops.SequenceEqual(new[] { "consumer", "dependency" }), "Host fallback releases remaining scopes in dependency order.");
        ExpectFailure(() => factory.CreateScope(_ => { }), "Stopped factory is terminal.");

        Stops.Clear();
        using (var nested = new ClientCompositionFactory(() => true, errors.Add))
        {
            var earlier = nested.CreateScope(local => local.Register<Dependency, Dependency>());
            earlier.Resolve<Dependency>();
            var later = nested.CreateScope(local =>
            {
                local.Borrow<IClientCompositionScope>(earlier);
                local.Register<NestedCleanup, NestedCleanup>();
            });
            later.Resolve<NestedCleanup>();
        }
        Assert(Stops.SequenceEqual(new[] { "nested", "dependency" }),
            "Reentrant disposal of another scope must not corrupt host cleanup iteration.");

        Stops.Clear();
        using (var noisy = new ClientCompositionFactory(() => true, _ => { throw new Exception("logger"); }))
        {
            var noisyScope = noisy.CreateScope(local =>
            {
                local.Register<Dependency, Dependency>();
                local.Register<ThrowingConsumer, ThrowingConsumer>();
            });
            noisyScope.Resolve<ThrowingConsumer>();
        }
        Assert(Stops.SequenceEqual(new[] { "throwing-consumer", "dependency" }), "A broken host logger must not interrupt production cleanup.");
    }

    private sealed class AsyncOnly : IAsyncDisposable
    {
        internal static int Constructions;
        public AsyncOnly() { Constructions++; }
        public System.Threading.Tasks.ValueTask DisposeAsync() => default(System.Threading.Tasks.ValueTask);
    }

    private sealed class NestedCleanup : IDisposable
    {
        private readonly IClientCompositionScope other;
        public NestedCleanup(IClientCompositionScope other) { this.other = other; }
        public void Dispose() { Stops.Add("nested"); other.Dispose(); }
    }

    private static void ExpectFailure(Action action, string message)
    {
        bool failed = false;
        try { action(); } catch { failed = true; }
        Assert(failed, message);
    }

    private sealed class CompositionDiscovery : IExtensionDiscoveryPolicy
    {
        public bool ShouldScanAssembly(Assembly assembly) => assembly == typeof(Program).Assembly;
        public bool ShouldDiscoverType(Type type) => type == typeof(ComposedModule) || type == typeof(DisabledModule)
            || type == typeof(RegisterFailureModule) || type == typeof(ActivationFailureModule);
    }

    private static void ProbeModuleComposition()
    {
        ComposedModule.Events = new BorrowedApi();
        DisabledModule.Constructions = 0;
        PluginService.Disposals = 0;
        var host = new ExtensionHostContext();
        using (var factory = new ClientCompositionFactory(() => true, _ => { }))
        {
            host.AddService<IClientCompositionFactory>(factory);
            host.AddService<IExtensionDiscoveryPolicy>(new CompositionDiscovery());
            host.AddService<IExtensionActivationPolicy>(new CompositionActivation());
            var runtime = new ClientExtensionRuntime(host, () => true);
            Assert(DisabledModule.Constructions == 0 && ComposedModule.Events.Subscribers == 0,
                "Production module runtime is passive before Start.");
            runtime.Start(); runtime.Start();
            Assert(DisabledModule.Constructions == 0, "Discovery must not construct a disabled composition module.");
            Assert(ComposedModule.Events.Subscribers == 1 && PluginService.Disposals == 2,
                "Ordinary registry rolls back partial Register/Activate failures while leaving the healthy composed module active.");
            runtime.Stop(); runtime.Stop();
            Assert(ComposedModule.Events.Subscribers == 0 && PluginService.Disposals == 3 && !ComposedModule.Events.Disposed,
                "Ordinary module shutdown owns its scope exactly once and preserves borrowed host APIs.");
        }
    }

    private sealed class CompositionActivation : IExtensionActivationPolicy
    {
        public IReadOnlyCollection<string> DisabledExtensions => new[] { "composition.disabled" };
        public bool ShouldActivate(string extensionId, out string reason)
        { reason = "test"; return extensionId != "composition.disabled"; }
    }

    [PhinixExtension("composition.healthy")]
    public class ComposedModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        internal static BorrowedApi Events;
        private IClientCompositionScope scope;
        private PluginService service;
        public virtual string ExtensionId => "composition.healthy";
        public int Priority => 0;
        public virtual void Register(IExtensionBuilder builder)
        {
            scope = builder.HostContext.GetRequiredService<IClientCompositionFactory>().CreateScope(local =>
            {
                local.Borrow<IHostApi>(Events);
                local.Borrow<ISiblingApi>(Events);
                local.Register<PluginService, PluginService>();
            });
            service = scope.Resolve<PluginService>();
        }
        public virtual void Activate(ExtensionHostContext host) { service.Start(); }
        public void Shutdown(ExtensionHostContext host) { scope?.Dispose(); }
    }

    [PhinixExtension("composition.disabled")]
    public sealed class DisabledModule : ComposedModule
    {
        internal static int Constructions;
        public DisabledModule() { Constructions++; }
        public override string ExtensionId => "composition.disabled";
    }
    [PhinixExtension("composition.register-failure")]
    public sealed class RegisterFailureModule : ComposedModule
    {
        public override string ExtensionId => "composition.register-failure";
        public override void Register(IExtensionBuilder builder) { base.Register(builder); throw new Exception("register"); }
    }
    [PhinixExtension("composition.activate-failure")]
    public sealed class ActivationFailureModule : ComposedModule
    {
        public override string ExtensionId => "composition.activate-failure";
        public override void Activate(ExtensionHostContext host) { base.Activate(host); throw new Exception("activate"); }
    }
}
