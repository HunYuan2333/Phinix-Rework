using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using Autofac;

internal static partial class Program
{
    private static int assertions;
    private static readonly List<string> Stops = new List<string>();

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--host-dependencies")
            {
                foreach (string name in new[] { "System.Memory", "System.Runtime.CompilerServices.Unsafe", "System.Numerics.Vectors" })
                {
                    Assembly.LoadFrom(Path.Combine(args[1], name + ".dll"));
                }
            }
            ProbeProductionComposition();
            ProbeModuleComposition();
            ProbeChatServices();
            ProbeOwnershipAndLaziness();
            ProbeDiagnosticDependencies();
            ProbePartialResolution();
            ProbeCleanupFailure();
            ProbeGuardedCleanup();
            Console.WriteLine("Autofac assembly: " + typeof(ContainerBuilder).Assembly.FullName);
            Console.WriteLine("Client composition runtime passed: " + assertions + " assertions (net472 binary).");
            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies().Where(a =>
                a.GetName().Name == "Autofac" || a.GetName().Name == "Microsoft.Bcl.AsyncInterfaces" ||
                a.GetName().Name.StartsWith("System.Diagnostics.DiagnosticSource") ||
                a.GetName().Name.StartsWith("System.Memory") || a.GetName().Name.StartsWith("System.Runtime.CompilerServices.Unsafe")))
                Console.WriteLine("Loaded: " + loaded.FullName + " at " + loaded.Location);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void ProbeOwnershipAndLaziness()
    {
        PluginService.Disposals = 0;
        var host = new BorrowedApi();
        var siblingApi = new BorrowedApi();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(host).As<IHostApi>().ExternallyOwned();
        builder.RegisterType<DisabledService>();
        DisabledService.Constructions = 0;
        var root = builder.Build();
        using (var plugin = root.BeginLifetimeScope(local =>
        {
            local.RegisterInstance(siblingApi).As<ISiblingApi>().ExternallyOwned();
            local.RegisterType<PluginService>().InstancePerLifetimeScope();
        }))
        {
            Assert(host.Subscribers == 0, "Building a scope must not activate its service graph.");
            var service = plugin.Resolve<PluginService>();
            Assert(ReferenceEquals(service, plugin.Resolve<PluginService>()), "Plugin services must remain local single instances.");
            Assert(host.Subscribers == 0, "Resolving dependencies must not subscribe before explicit activation.");
            service.Start(); service.Start();
            Assert(host.Subscribers == 1, "Explicit repeated activation must not duplicate subscriptions.");
            service.Stop(); service.Stop();
            Assert(host.Subscribers == 0 && service.Token.IsCancellationRequested,
                "Lifecycle coordination must unsubscribe and cancel before scope disposal.");
            Assert(!host.Disposed && !siblingApi.Disposed, "Stopping consumers must not dispose borrowed APIs.");
        }
        Assert(PluginService.Disposals == 1, "The owning plugin scope must dispose its service once.");
        root.Dispose(); root.Dispose();
        Assert(!host.Disposed && !siblingApi.Disposed, "Root disposal must respect externally-owned host and cross-plugin APIs.");
        Assert(DisabledService.Constructions == 0, "Container registration must not instantiate a disabled/unresolved service.");
    }

    private static void ProbePartialResolution()
    {
        Stops.Clear();
        var builder = new ContainerBuilder();
        using (var root = builder.Build())
        {
            var plugin = root.BeginLifetimeScope(local =>
            {
                local.RegisterType<Dependency>().InstancePerLifetimeScope();
                local.RegisterType<BrokenConsumer>();
            });
            bool failed = false;
            try { plugin.Resolve<BrokenConsumer>(); } catch (Autofac.Core.DependencyResolutionException) { failed = true; }
            Assert(failed, "Constructor failures must remain observable.");
            plugin.Dispose(); plugin.Dispose();
            Assert(Stops.SequenceEqual(new[] { "dependency" }), "Failed resolution must retain ownership of previously constructed dependencies for one cleanup.");
        }

        Stops.Clear();
        builder = new ContainerBuilder();
        using (var root = builder.Build())
        using (var plugin = root.BeginLifetimeScope(local =>
        {
            local.RegisterType<Dependency>().InstancePerLifetimeScope();
            local.RegisterType<Consumer>().InstancePerLifetimeScope();
        }))
        {
            plugin.Resolve<Consumer>();
        }
        Assert(Stops.SequenceEqual(new[] { "consumer", "dependency" }), "Consumers must dispose before their constructor dependencies.");
    }

    private static void ProbeDiagnosticDependencies()
    {
        using (var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Phinix.F2.Probe",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData
        })
        using (var source = new ActivitySource("Phinix.F2.Probe"))
        {
            ActivitySource.AddActivityListener(listener);
            ActivityContext context;
            Assert(ActivityContext.TryParse("00-0123456789abcdef0123456789abcdef-0123456789abcdef-01", null, out context),
                "The packaged diagnostic library and its Span dependencies must parse a real trace context.");
            using (var activity = source.StartActivity("operation", ActivityKind.Internal, context))
            {
                Assert(activity != null && !string.IsNullOrEmpty(activity.Id), "Diagnostic activity creation must work on the target runtime.");
                activity.SetTag("phinix.probe", "net472");
            }
        }
    }

    private static void ProbeCleanupFailure()
    {
        Stops.Clear();
        var builder = new ContainerBuilder();
        using (var root = builder.Build())
        {
            var plugin = root.BeginLifetimeScope(local =>
            {
                local.RegisterType<Dependency>().InstancePerLifetimeScope();
                local.RegisterType<ThrowingConsumer>().InstancePerLifetimeScope();
            });
            plugin.Resolve<ThrowingConsumer>();
            bool failureObserved = false;
            try { plugin.Dispose(); } catch (Exception) { failureObserved = true; }
            Assert(failureObserved, "A cleanup failure must be reported to the lifecycle coordinator.");
            Assert(Stops.Contains("throwing-consumer"), "The failing consumer must receive cleanup.");
            // The coordinator must not assume container disposal isolates exceptions.
            Console.WriteLine("Cleanup after throwing consumer: " + string.Join(",", Stops));
            if (!Stops.Contains("dependency")) Console.WriteLine("LIMITATION: unguarded Dispose failure skipped dependency cleanup.");
            plugin.Dispose();
            Assert(Stops.Count(s => s == "throwing-consumer") == 1, "Repeated disposal must not invoke a failed consumer twice.");
        }
    }

    private static void ProbeGuardedCleanup()
    {
        Stops.Clear();
        var failures = new List<Exception>();
        var builder = new ContainerBuilder();
        using (var root = builder.Build())
        using (var plugin = root.BeginLifetimeScope(local =>
        {
            local.RegisterType<Dependency>().InstancePerLifetimeScope().OnRelease(value => Release(value, failures));
            local.RegisterType<ThrowingConsumer>().InstancePerLifetimeScope().OnRelease(value => Release(value, failures));
        }))
        {
            plugin.Resolve<ThrowingConsumer>();
        }
        Assert(failures.Count == 1, "Guarded release must retain the consumer cleanup error for reporting.");
        Assert(Stops.SequenceEqual(new[] { "throwing-consumer", "dependency" }),
            "Guarded release must clean the remaining dependency after a consumer throws.");
    }

    private static void Release(IDisposable owned, ICollection<Exception> failures)
    {
        try { owned.Dispose(); }
        catch (Exception error) { failures.Add(error); }
    }

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private interface IHostApi { event Action Changed; }
    private interface ISiblingApi { }
    internal sealed class BorrowedApi : IHostApi, ISiblingApi, IDisposable
    {
        public event Action Changed;
        internal bool Disposed;
        internal int Subscribers => Changed == null ? 0 : Changed.GetInvocationList().Length;
        public void Dispose() { Disposed = true; }
    }
    private sealed class PluginService : IDisposable
    {
        private readonly IHostApi host;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool running;
        internal static int Disposals;
        public PluginService(IHostApi host, ISiblingApi sibling) { this.host = host; }
        internal CancellationToken Token => cancellation.Token;
        internal void Start() { if (running) return; running = true; host.Changed += OnChanged; }
        internal void Stop() { if (!running) return; running = false; host.Changed -= OnChanged; cancellation.Cancel(); }
        private void OnChanged() { }
        public void Dispose() { Stop(); cancellation.Dispose(); Disposals++; }
    }
    private sealed class DisabledService
    {
        internal static int Constructions;
        public DisabledService() { Constructions++; }
    }
    private sealed class Dependency : IDisposable
    {
        public void Dispose() { Stops.Add("dependency"); }
    }
    private sealed class Consumer : IDisposable
    {
        public Consumer(Dependency dependency) { }
        public void Dispose() { Stops.Add("consumer"); }
    }
    private sealed class BrokenConsumer
    {
        public BrokenConsumer(Dependency dependency) { throw new InvalidOperationException("partial construction"); }
    }
    private sealed class ThrowingConsumer : IDisposable
    {
        public ThrowingConsumer(Dependency dependency) { }
        public void Dispose() { Stops.Add("throwing-consumer"); throw new InvalidOperationException("cleanup failure"); }
    }
}
