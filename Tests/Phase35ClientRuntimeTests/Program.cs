using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Google.Protobuf;
using Utils;
using Utils.Framework;
using System.Threading;
using Authentication;
using Connections;
using PhinixClient.Framework;
internal static partial class Program
{
    private static int assertions;
    private static int Main() { try {
        AssertLegacyApisRemoved();
        AssertClientKeyGenerationStillWorks();
        AssertExtensionDependencyValidationAndLifecycle();
        AssertClientExtensionRuntimeLifecycle();
        AssertClientEnvironmentCaptureRequiresMainThread();
        AssertExtensionManagementWindowLifecycle();
        AssertClientLinkOpening();
        Console.WriteLine("PASS ClientFull 7 cases; " + assertions + " assertions"); return 0;
    } catch(Exception error) { Console.Error.WriteLine(error); return 1; } }
    private static void AssertLegacyApisRemoved()
    {
        string repoRoot = GetRepositoryRoot();
        AssertFileDoesNotContain(Path.Combine(repoRoot, "Client", "Common", "Connections.Client", "NetClient.cs"), "Abort(");
        AssertFileDoesNotContain(Path.Combine(repoRoot, "Client", "Common", "Authentication.Client", "ClientAuthenticator.cs"), "RNGCryptoServiceProvider");
    }
    private static void AssertClientKeyGenerationStillWorks()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "PhinixPhase35RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);

        try
        {
            string credentialStorePath = Path.Combine(testDirectory, "credentials.bin");
            NetClient netClient = new NetClient();
            ClientAuthenticator authenticator = new ClientAuthenticator(
                netClient,
                (sessionId, serverName, serverDescription, authType, callback) => { },
                credentialStorePath
            );

            Assert(File.Exists(credentialStorePath), "ClientAuthenticator should create its credential store on first use.");

            CredentialStore initialStore = ReadCredentialStore(credentialStorePath);
            Assert(!string.IsNullOrEmpty(initialStore.ClientKey), "Generated client key should not be empty.");
            Assert(Convert.FromBase64String(initialStore.ClientKey).Length == 64, "Generated client key should decode to 64 random bytes.");

            ClientAuthenticator secondAuthenticator = new ClientAuthenticator(
                new NetClient(),
                (sessionId, serverName, serverDescription, authType, callback) => { },
                credentialStorePath
            );

            CredentialStore secondStore = ReadCredentialStore(credentialStorePath);
            Assert(initialStore.ClientKey == secondStore.ClientKey, "Existing credential store should preserve the original client key.");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }
    private static void AssertExtensionDependencyValidationAndLifecycle()
    {
        LifecycleEvents.Clear();
        ExtensionHostContext host = new ExtensionHostContext { HostKind = "runtime-test" };
        host.AddService<IExtensionDiscoveryPolicy>(new TypeSetDiscoveryPolicy(typeof(BaseModule), typeof(DependentModule),
            typeof(RegisterFailureModule), typeof(RegisterDependentModule), typeof(ActivateFailureModule),
            typeof(ActivateDependentModule), typeof(MissingDependencyModule), typeof(CycleAModule), typeof(CycleBModule)));
        DiscoveredPhinixExtensions discovered = PhinixExtensionRegistry.DiscoverExtensions(host);

        AssertState(discovered, "tests.missing", ExtensionModuleState.Failed);
        AssertState(discovered, "tests.cycle-a", ExtensionModuleState.Failed);
        AssertState(discovered, "tests.cycle-b", ExtensionModuleState.Failed);
        AssertState(discovered, "tests.register-failure", ExtensionModuleState.Failed);
        AssertState(discovered, "tests.register-dependent", ExtensionModuleState.Failed);
        Assert(!RegisterFailureModule.ActivateCalled, "A module whose Register failed must never activate.");
        Assert(!host.TryResolveApi<IRegisterFailureApi>(out _), "Register failure must revoke APIs published before the exception.");

        PhinixExtensionRegistry.ActivateExtensions(discovered, host);
        AssertState(discovered, "tests.base", ExtensionModuleState.Active);
        AssertState(discovered, "tests.dependent", ExtensionModuleState.Active);
        AssertState(discovered, "tests.activate-failure", ExtensionModuleState.Failed);
        AssertState(discovered, "tests.activate-dependent", ExtensionModuleState.Failed);
        Assert(!host.TryResolveApi<IActivateFailureApi>(out _), "Activation failure must revoke registered APIs.");
        Assert(LifecycleEvents.IndexOf("shutdown:activate-dependent") >= 0 &&
            LifecycleEvents.IndexOf("shutdown:activate-dependent") < LifecycleEvents.IndexOf("shutdown:activate-failure"),
            "Activation rollback must clean registered consumers before their failed provider.");
        Assert(LifecycleEvents.IndexOf("register:base") < LifecycleEvents.IndexOf("register:dependent"), "Dependencies must register first.");
        Assert(LifecycleEvents.IndexOf("activate:base") < LifecycleEvents.IndexOf("activate:dependent"), "Dependencies must activate first.");

        int lifecycleEventsAfterStart = LifecycleEvents.Count;
        PhinixExtensionRegistry.ActivateExtensions(discovered, host);
        Assert(LifecycleEvents.Count == lifecycleEventsAfterStart, "Repeated activation must not activate modules twice.");

        PhinixExtensionRegistry.ShutdownExtensions(discovered, host);
        Assert(LifecycleEvents.IndexOf("shutdown:dependent") < LifecycleEvents.IndexOf("shutdown:base"), "Active modules must shut down in reverse dependency order.");
        AssertState(discovered, "tests.base", ExtensionModuleState.Shutdown);
        AssertState(discovered, "tests.dependent", ExtensionModuleState.Shutdown);
        Assert(!host.TryResolveApi<IBaseApi>(out _), "Shutdown must revoke APIs owned by the stopped extension.");
        int lifecycleEventsAfterStop = LifecycleEvents.Count;
        PhinixExtensionRegistry.ShutdownExtensions(discovered, host);
        Assert(LifecycleEvents.Count == lifecycleEventsAfterStop, "Repeated shutdown must not stop modules twice.");

        DisabledProbeModule.ConstructorCount = 0;
        ExtensionHostContext disabledHost = new ExtensionHostContext();
        var disabledProbePolicy = new DisabledProbeActivationPolicy();
        disabledHost.AddService<IExtensionActivationPolicy>(disabledProbePolicy);
        disabledHost.AddService<IExtensionDiscoveryPolicy>(disabledProbePolicy);
        var disabledRuntime = new ClientExtensionRuntime(disabledHost, () => true);
        disabledRuntime.Start();
        DiscoveredPhinixExtensions disabled = disabledRuntime.Extensions;
        AssertState(disabled, "tests.disabled-probe", ExtensionModuleState.Disabled);
        Assert(DisabledProbeModule.ConstructorCount == 0, "Disabled modules must not be constructed during discovery.");
        disabledRuntime.Stop();
    }
    private static void AssertClientEnvironmentCaptureRequiresMainThread()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhinixEnvironment", Guid.NewGuid().ToString("N"));
        var paths = new ClientEnvironmentPaths(Path.Combine(root, "Mods"), Path.Combine(root, "SaveData"));
        var snapshot = new ClientEnvironmentSnapshot(paths, Path.Combine(root, "Mods", "host"),
            "1.6", "0.9.7", "1.2.0", null, null, null, null);
        int calls = 0;
        int gameThreadId = Thread.CurrentThread.ManagedThreadId;
        ClientEnvironmentService service = null;
        Exception initializationError = null;
        var initializer = new Thread(() =>
        {
            try
            {
                service = new ClientEnvironmentService(() => { calls++; return snapshot; },
                    () => Thread.CurrentThread.ManagedThreadId == gameThreadId);
            }
            catch (Exception ex) { initializationError = ex; }
        });
        initializer.Start();
        Assert(initializer.Join(5000) && initializationError == null && service != null,
            "Environment service may initialize on a loading thread and later capture on the game thread.");
        Assert(calls == 0, "Constructing the environment service must not capture game facts.");
        Assert(ReferenceEquals(service.Capture(), snapshot), "Main-thread environment service should return captured facts.");
        Exception workerError = null;
        var worker = new Thread(() =>
        {
            try { service.Capture(); }
            catch (Exception ex) { workerError = ex; }
        });
        worker.Start();
        Assert(worker.Join(5000), "Environment capture worker should terminate promptly.");
        Assert(workerError is InvalidOperationException, "Background environment capture must fail before touching game facts.");
        Assert(calls == 1, "Wrong-thread requests must never invoke the game capture factory.");
        Assert(ReferenceEquals(service.Capture(), snapshot) && calls == 2, "A rejected worker request must not poison future main-thread captures.");
        Assert(!Directory.Exists(root), "Capturing paths must not create persistent state.");
    }
    private static void AssertExtensionManagementWindowLifecycle()
    {
        bool isOpen = false;
        int probes = 0;
        int opens = 0;
        int gameThreadId = Thread.CurrentThread.ManagedThreadId;
        IClientExtensionManagementWindowService service = null;
        Exception initializationError = null;
        var initializer = new Thread(() =>
        {
            try
            {
                service = new ClientExtensionManagementWindowService(
                    () => { probes++; return isOpen; }, () => { opens++; isOpen = true; },
                    () => Thread.CurrentThread.ManagedThreadId == gameThreadId);
            }
            catch (Exception ex) { initializationError = ex; }
        });
        initializer.Start();
        Assert(initializer.Join(5000) && initializationError == null && service != null,
            "Management may initialize on a loading thread without capturing it as the game thread.");
        Assert(probes == 0 && opens == 0, "Loading-thread construction must not invoke game window callbacks.");
        service.OpenExtensionManagerWindow();
        service.OpenExtensionManagerWindow();
        Assert(opens == 1, "Repeated management actions must not open duplicate windows.");
        isOpen = false;
        service.OpenExtensionManagerWindow();
        Assert(opens == 2, "Closing management must allow a new window to open.");
        int priorProbes = probes;
        Exception workerError = null;
        var worker = new Thread(() =>
        {
            try { service.OpenExtensionManagerWindow(); }
            catch (Exception ex) { workerError = ex; }
        });
        worker.Start();
        Assert(worker.Join(5000), "Management worker must terminate promptly.");
        Assert(workerError is InvalidOperationException, "Worker requests must be rejected before game window callbacks.");
        Assert(probes == priorProbes && opens == 2, "Wrong-thread requests must not probe or open game windows.");
        isOpen = false;
        service.OpenExtensionManagerWindow();
        Assert(opens == 3, "Rejected worker calls must not prevent subsequent main-thread recovery.");

        int attempts = 0;
        var retry = new ClientExtensionManagementWindowService(() => false, () =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Simulated window creation failure.");
        }, () => Thread.CurrentThread.ManagedThreadId == gameThreadId);
        try { retry.OpenExtensionManagerWindow(); }
        catch (InvalidOperationException) { }
        retry.OpenExtensionManagerWindow();
        Assert(attempts == 2, "A failed window creation must allow retry rather than lock the recovery entry.");
    }
    private static CredentialStore ReadCredentialStore(string credentialStorePath)
    {
        using (FileStream stream = File.OpenRead(credentialStorePath))
        using (CodedInputStream input = new CodedInputStream(stream))
        {
            return CredentialStore.Parser.ParseFrom(input);
        }
    }
    private static void AssertState(DiscoveredPhinixExtensions discovered, string extensionId, ExtensionModuleState expected)
    {
        ExtensionDiscoveryResult result = discovered.ExtensionResults.FirstOrDefault(candidate => candidate.ExtensionId == extensionId);
        Assert(result != null && result.State == expected, $"Expected extension '{extensionId}' to be {expected}, got {result?.State.ToString() ?? "missing"}.");
    }
    private static readonly List<string> LifecycleEvents = new List<string>();

    private sealed class DisabledProbeActivationPolicy : IExtensionActivationPolicy, IExtensionDiscoveryPolicy
    {
        public IReadOnlyCollection<string> DisabledExtensions => new[] { "tests.disabled-probe" };
        public bool ShouldScanAssembly(System.Reflection.Assembly assembly) => assembly == typeof(DisabledProbeModule).Assembly;
        public bool ShouldDiscoverType(Type type) => type == typeof(DisabledProbeModule);
        public bool ShouldActivate(string extensionId, out string reason)
        {
            bool enabled = !string.Equals(extensionId, "tests.disabled-probe", StringComparison.OrdinalIgnoreCase);
            reason = enabled ? null : "disabled for lifecycle regression test";
            return enabled;
        }
    }

    [PhinixExtension("tests.disabled-probe")]
    public sealed class DisabledProbeModule : IPhinixExtensionModule
    {
        public static int ConstructorCount;
        public DisabledProbeModule() { ConstructorCount++; }
        public string ExtensionId => "tests.disabled-probe";
        public void Register(IExtensionBuilder builder) { }
    }

    private interface IBaseApi { }
    private interface IRegisterFailureApi { }
    private interface IActivateFailureApi { }

    [PhinixExtension("tests.base")]
    public sealed class BaseModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule, IBaseApi
    {
        public string ExtensionId => "tests.base";
        public void Register(IExtensionBuilder builder) { LifecycleEvents.Add("register:base"); builder.RegisterApi<IBaseApi>(this); }
        public void Activate(ExtensionHostContext hostContext) { LifecycleEvents.Add("activate:base"); }
        public void Shutdown(ExtensionHostContext hostContext) { LifecycleEvents.Add("shutdown:base"); }
    }

    [PhinixExtension("tests.dependent", DependsOn = new[] { "tests.base" })]
    public sealed class DependentModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        public string ExtensionId => "tests.dependent";
        public void Register(IExtensionBuilder builder) { LifecycleEvents.Add("register:dependent"); }
        public void Activate(ExtensionHostContext hostContext) { LifecycleEvents.Add("activate:dependent"); }
        public void Shutdown(ExtensionHostContext hostContext) { LifecycleEvents.Add("shutdown:dependent"); }
    }

    [PhinixExtension("tests.register-failure")]
    public sealed class RegisterFailureModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule, IRegisterFailureApi
    {
        public static bool ActivateCalled;
        public string ExtensionId => "tests.register-failure";
        public void Register(IExtensionBuilder builder) { builder.RegisterApi<IRegisterFailureApi>(this); throw new InvalidOperationException("register failure"); }
        public void Activate(ExtensionHostContext hostContext) { ActivateCalled = true; }
        public void Shutdown(ExtensionHostContext hostContext) { }
    }

    [PhinixExtension("tests.register-dependent", DependsOn = new[] { "tests.register-failure" })]
    public sealed class RegisterDependentModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        public string ExtensionId => "tests.register-dependent";
        public void Register(IExtensionBuilder builder) { throw new InvalidOperationException("must be skipped"); }
        public void Activate(ExtensionHostContext hostContext) { throw new InvalidOperationException("must be skipped"); }
        public void Shutdown(ExtensionHostContext hostContext) { }
    }

    [PhinixExtension("tests.activate-failure")]
    public sealed class ActivateFailureModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule, IActivateFailureApi
    {
        public string ExtensionId => "tests.activate-failure";
        public void Register(IExtensionBuilder builder) { builder.RegisterApi<IActivateFailureApi>(this); }
        public void Activate(ExtensionHostContext hostContext) { throw new InvalidOperationException("activate failure"); }
        public void Shutdown(ExtensionHostContext hostContext) { LifecycleEvents.Add("shutdown:activate-failure"); }
    }

    [PhinixExtension("tests.activate-dependent", DependsOn = new[] { "tests.activate-failure" })]
    public sealed class ActivateDependentModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        public string ExtensionId => "tests.activate-dependent";
        public void Register(IExtensionBuilder builder) { }
        public void Activate(ExtensionHostContext hostContext) { throw new InvalidOperationException("must be skipped"); }
        public void Shutdown(ExtensionHostContext hostContext) { LifecycleEvents.Add("shutdown:activate-dependent"); }
    }

    [PhinixExtension("tests.missing", DependsOn = new[] { "tests.absent" })]
    public sealed class MissingDependencyModule : IPhinixExtensionModule
    {
        public string ExtensionId => "tests.missing";
        public void Register(IExtensionBuilder builder) { throw new InvalidOperationException("must be skipped"); }
    }

    [PhinixExtension("tests.cycle-a", DependsOn = new[] { "tests.cycle-b" })]
    public sealed class CycleAModule : IPhinixExtensionModule
    {
        public string ExtensionId => "tests.cycle-a";
        public void Register(IExtensionBuilder builder) { throw new InvalidOperationException("must be skipped"); }
    }

    [PhinixExtension("tests.cycle-b", DependsOn = new[] { "tests.cycle-a" })]
    public sealed class CycleBModule : IPhinixExtensionModule
    {
        public string ExtensionId => "tests.cycle-b";
        public void Register(IExtensionBuilder builder) { throw new InvalidOperationException("must be skipped"); }
    }

    private static void AssertFileDoesNotContain(string path, string forbiddenText)
    {
        string content = File.ReadAllText(path);
        Assert(!content.Contains(forbiddenText), $"Expected '{path}' to stop using '{forbiddenText}'.");
    }
    private static string GetRepositoryRoot()
    {
        for (DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PhinixClient.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate Phinix.sln above the test output directory.");
    }
    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
