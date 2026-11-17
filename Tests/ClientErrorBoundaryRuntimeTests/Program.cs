using System;
using Connections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal sealed class SendTransport : IClientTransport
{
    public bool IsConnected = true;
    public bool Connected => IsConnected;
    public string RemoteEndpoint => "test";
    public int Calls;
    public Exception Failure, ConnectFailure, DisposeFailure, DisconnectFailure, RegistrationFailure;
    public int DisconnectCalls;
    public readonly Dictionary<string,NetCommon.PacketHandlerDelegate> Handlers = new Dictionary<string,NetCommon.PacketHandlerDelegate>();
    public event EventHandler OnConnecting;
    public event EventHandler OnDisconnect;
    public event EventHandler<LogEventArgs> OnLogEntry;
    public void RaiseLogEntry(LogEventArgs args) { }
    public void RaiseDisconnect() { OnDisconnect?.Invoke(this, EventArgs.Empty); }
    public void Connect(string host, int port) { if(ConnectFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ConnectFailure).Throw(); }
    public void Disconnect() { DisconnectCalls++; IsConnected = false; if(DisconnectFailure != null) throw DisconnectFailure; }
    public void Dispose() { if(DisposeFailure != null) throw DisposeFailure; }
    public void RegisterPacketHandler(string module, NetCommon.PacketHandlerDelegate handler) { if(RegistrationFailure != null) throw RegistrationFailure; Handlers[module] = handler; }
    public void UnregisterPacketHandler(string module) { Handlers.Remove(module); }
    public void Send(string module, byte[] bytes) { Calls++; if (Failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Failure).Throw(); }
}
internal sealed class Policy : IExtensionDiscoveryPolicy
{
    private readonly HashSet<Type> types;
    internal Policy(params Type[] types) { this.types = new HashSet<Type>(types); }
    public bool ShouldScanAssembly(Assembly assembly) => assembly == typeof(Policy).Assembly;
    public bool ShouldDiscoverType(Type type) => types.Contains(type);
}
[PhinixExtension("e2.good")]
public sealed class Good : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public string ExtensionId => "e2.good";
    public void Register(IExtensionBuilder builder) { }
    public void Activate(ExtensionHostContext context) { }
    public void Shutdown(ExtensionHostContext context) { }
}
[PhinixExtension("e2.constructor")]
public sealed class BrokenConstructor : IPhinixExtensionModule
{
    public BrokenConstructor() { Program.FaultOrigin(); }
    public string ExtensionId => "e2.constructor";
    public void Register(IExtensionBuilder builder) { }
}
[PhinixExtension("e2.register")]
public sealed class BrokenRegister : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public string ExtensionId => "e2.register";
    public void Register(IExtensionBuilder builder) { builder.RegisterApi<ICloneable>(new Marker()); Program.FaultOrigin(); }
    public void Activate(ExtensionHostContext context) { }
    public void Shutdown(ExtensionHostContext context) { throw new ApplicationException("cleanup-after-register"); }
}
public sealed class Marker : ICloneable { public object Clone() => this; }
public sealed class HostileMessageException : Exception
{
    public override string Message => throw new Exception("broken exception message getter");
}
[PhinixExtension("e2.hostile")]
public sealed class BrokenMessage : IPhinixExtensionModule
{
    public string ExtensionId => "e2.hostile";
    public void Register(IExtensionBuilder builder) { throw new HostileMessageException(); }
}
[PhinixExtension("e2.flaky-id")]
public sealed class FlakyIdentity : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public static int Reads;
    public static bool Stopped;
    public string ExtensionId { get { if (++Reads > 1) Program.FaultOrigin(); return "e2.flaky-id"; } }
    public void Register(IExtensionBuilder builder) { }
    public void Activate(ExtensionHostContext context) { }
    public void Shutdown(ExtensionHostContext context) { Stopped = true; }
}
[PhinixExtension("e2.activate")]
public sealed class BrokenActivate : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public string ExtensionId => "e2.activate";
    public void Register(IExtensionBuilder builder) { }
    public void Activate(ExtensionHostContext context) { Program.FaultOrigin(); }
    public void Shutdown(ExtensionHostContext context) { throw new ApplicationException("cleanup-after-activate"); }
}
[PhinixExtension("e2.consumer", DependsOn = new[] { "e2.activate" })]
public sealed class Dependent : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public static bool Activated;
    public string ExtensionId => "e2.consumer";
    public void Register(IExtensionBuilder builder) { }
    public void Activate(ExtensionHostContext context) { Activated = true; }
    public void Shutdown(ExtensionHostContext context) { }
}
[PhinixExtension(null)]
public sealed class InvalidMetadata : IPhinixExtensionModule
{
    public string ExtensionId => "e2.invalid";
    public void Register(IExtensionBuilder builder) { }
}
[PhinixExtension("e2.priority")]
public sealed class BrokenPriority : IPhinixExtensionModule, IMessageInterceptor
{
    public string ExtensionId => "e2.priority";
    public int Priority { get { Program.FaultOrigin(); return 0; } }
    public void Register(IExtensionBuilder builder) { builder.AddMessageInterceptor(this); }
    public MessageHandlingResultAction Intercept(FrameworkDisplayMessage message) => MessageHandlingResultAction.Continue;
}
public sealed class BadCapability : ICapabilityProvider
{
    public IEnumerable<string> GetCapabilities() { yield return "e2.before-failure"; Program.FaultOrigin(); }
}
public sealed class PartialAssembly : Assembly
{
    public override AssemblyName GetName() => new AssemblyName("e2.partial");
    public override Type[] GetTypes() => throw new ReflectionTypeLoadException(new[] { typeof(Good), null }, new Exception[] { new FileNotFoundException("missing-e2-helper") });
}
public sealed class Interceptor : IMessageInterceptor
{
    public bool Broken;
    public int Calls;
    public int Priority => 0;
    public MessageHandlingResultAction Intercept(FrameworkDisplayMessage message)
    { Calls++; if (Broken) Program.FaultOrigin(); return MessageHandlingResultAction.Continue; }
}
public static partial class Program
{
    private static int checks;
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void FaultOrigin() { throw new InvalidOperationException("e2-original-cause"); }
    private static void StaticLogFault(object sender,LogEventArgs args) { FaultOrigin(); }
    public static void Main(string[] args)
    {
        string runtime = Path.GetFullPath(args[0]); string game = args.Length > 1 ? Path.GetFullPath(args[1]) : runtime;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string directory in new[] { runtime, game }) { string path = Path.Combine(directory, name); if (File.Exists(path)) return Assembly.LoadFrom(path); }
            return null;
        };
        try
        {
            Registry(); ReflectionAndLoader(); ClientPipelines(runtime); PacketSubmission(runtime); ConnectionBoundaries(); ControlBoundaries(runtime); SettingsBoundaries(); LocalizationAndLinks(runtime);
            Console.WriteLine("CLIENT ERROR BOUNDARIES PASS " + checks + " checks; real shared registry/loader/SDK and Host pipeline helpers");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }
    private static ClientErrorService Errors() => new ClientErrorService("Host: e2 tests", () => true, null);
    private static void Registry()
    {
        var errors = Errors(); var records = new List<HostLogEntry>();
        var host = new ExtensionHostContext { HostKind = "test", StructuredLog = entry => { records.Add(entry); errors.ReportStructured(entry); } };
        host.AddService<IExtensionDiscoveryPolicy>(new Policy(typeof(Good), typeof(BrokenConstructor), typeof(BrokenRegister), typeof(BrokenActivate), typeof(Dependent), typeof(BrokenPriority), typeof(InvalidMetadata), typeof(BrokenMessage), typeof(FlakyIdentity)));
        var discovered = PhinixExtensionRegistry.DiscoverExtensions(host);
        Check(discovered.ExtensionResults.Any(r => r.ExtensionId == "e2.good" && r.State == ExtensionModuleState.Registered), "constructor/register/metadata errors do not block unrelated registration");
        var constructed = records.Single(r => r.Message == "ModuleConstructFailed");
        Check(constructed.Exception is TargetInvocationException && constructed.Exception.InnerException.StackTrace.Contains("FaultOrigin"), "constructor wrapper and original inner stack retained");
        Check(records.Any(r => r.Message == "ModuleRegisterFailed") && records.Any(r => r.Message == "ModuleShutdownFailed"), "register and cleanup failures separately retained");
        Check(discovered.ExtensionResults.Single(r => r.ExtensionId == "e2.hostile").State == ExtensionModuleState.Failed && errors.BuildReport(errors.Capture().Single(r => r.Owner == "e2.hostile").Id).Contains("message unavailable"), "throwing exception Message getter cannot replace register failure or block peers");
        Check(!host.ApiRegistry.ResolveAll<ICloneable>().Any(), "failed registration revokes contributed APIs");
        Check(records.Any(r => r.Message == "DependencyMetadataFailed") && discovered.ExtensionResults.Any(r => r.DisplayName == nameof(InvalidMetadata) && r.State == ExtensionModuleState.Failed), "invalid attribute metadata isolated and observable");
        Check(records.Any(r => r.Message == "ParticipantPriorityFailed") && discovered.MessageInterceptors.Count == 0, "throwing Priority excluded at its participant boundary");
        PhinixExtensionRegistry.ActivateExtensions(discovered, host);
        Check(discovered.ExtensionResults.Single(r => r.ExtensionId == "e2.good").State == ExtensionModuleState.Active && !Dependent.Activated, "activation fault blocks its dependent without stopping unrelated module");
        Check(records.Any(r => r.Message == "ModuleActivateFailed") && records.Count(r => r.Message == "ModuleShutdownFailed") >= 2, "activation original captured before failing cleanup");
        Check(errors.Capture().Any(r => errors.BuildReport(r.Id).Contains("FaultOrigin")), "actual shared fault reaches copyable Host error store");
        discovered.CapabilityProviders.Add(new BadCapability());
        Check(PhinixExtensionRegistry.CollectCapabilities(discovered, host).Contains("core.framework.v2") && records.Any(r => r.Message == "CapabilityProviderFailed"), "lazy capability enumeration failure leaves core capabilities available");
        PhinixExtensionRegistry.ShutdownExtensions(discovered, host);
        Check(discovered.ExtensionResults.Single(r => r.ExtensionId == "e2.good").State == ExtensionModuleState.Shutdown, "shutdown still runs for healthy module");
        Check(FlakyIdentity.Reads == 1 && FlakyIdentity.Stopped, "lifecycle uses validated cached module identity instead of unsafe repeated plugin getter");
    }
    private static void ReflectionAndLoader()
    {
        var records = new List<HostLogEntry>();
        var getTypes = typeof(PhinixExtensionRegistry).GetMethod("getLoadableTypes", BindingFlags.NonPublic | BindingFlags.Static);
        var types = (IEnumerable<Type>)getTypes.Invoke(null, new object[] { new PartialAssembly(), new Action<HostLogEntry>(records.Add) });
        Check(types.Single() == typeof(Good) && records.Single().Exception is ReflectionTypeLoadException, "partial type discovery keeps valid types and entire loader exception");
        var errors = Errors(); errors.ReportStructured(records[0]);
        Check(errors.BuildReport(errors.Capture()[0].Id).Contains("missing-e2-helper"), "LoaderExceptions reach copied report");
        string path = Path.Combine(Path.GetTempPath(), "phinix-e2-malformed-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try
        {
            File.WriteAllBytes(Path.Combine(path, "broken.dll"), new byte[] { 0, 1, 2, 3 }); records.Clear();
            using (ExtensionAssemblyLoader.LoadOwnedAssemblies(new[] { path }, null, records.Add)) { }
            Check(records.Count == 1 && records[0].Message == "OwnedAssemblyCandidateFailed" && records[0].Exception != null && records[0].Exception.StackTrace.Length > 0, "malformed owned DLL produces original exception, not Message-only warning");
        }
        finally { Directory.Delete(path, true); }
        records.Clear(); FrameworkItemPayload payload;
        bool ok = FrameworkSerialization.TryExtractItemPayload(new FrameworkPacket { PayloadJson = "{" }, out payload, records.Add);
        Check(!ok && records.Count == 1 && records[0].Level == LogLevel.WARNING && records[0].Exception != null, "invalid item JSON is rejected with detailed recoverable diagnostic");
        Check(!FrameworkSerialization.TryExtractItemPayload(null, out payload, records.Add), "null payload is ordinary rejection");
    }
    private static void ClientPipelines(string directory)
    {
        // Construct only pure boundaries, not a game session. No Unity methods are called.
        var hostAssembly = Assembly.LoadFrom(Path.Combine(directory, "PhinixClient.dll"));
        var type = hostAssembly.GetType("PhinixClient.Framework.PhinixFrameworkClient", true);
        object client = FormatterServices.GetUninitializedObject(type);
        var records = new List<HostLogEntry>(); var context = new ExtensionHostContext { StructuredLog = records.Add };
        type.GetField("extensionHostContext", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(client, context);
        type.GetField("extensionLogLock", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(client, new object());
        type.GetField("extensionLog", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(client, new List<FrameworkLogEntry>());
        var probe = type.GetMethod("Probe", BindingFlags.NonPublic | BindingFlags.Instance);
        Func<bool> broken = () => { FaultOrigin(); return true; };
        Check(!(bool)probe.Invoke(client, new object[] { new Marker(), "CanHandleTest", broken }) && records.Single().Exception.StackTrace.Contains("FaultOrigin"), "actual Host predicate boundary returns false and retains original failure");
        var ordered = type.GetMethod("Ordered", BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(typeof(int));
        Func<int,int> priority = n => { if (n == 2) FaultOrigin(); return n; };
        var values = (IEnumerable<int>)ordered.Invoke(client, new object[] { new[] { 3, 2, 1 }, priority });
        Check(values.SequenceEqual(new[] { 1, 3 }) && records.Any(e => e.Message == "ParticipantPriorityFailed"), "actual Host runtime priority query isolates faulty participant");
        int next = 0; EventHandler<LogEventArgs> bad = StaticLogFault; EventHandler<LogEventArgs> good = (sender, e) => next++;
        type.GetEvent("OnLogEntry").AddEventHandler(client, bad); type.GetEvent("OnLogEntry").AddEventHandler(client, good);
        type.GetMethod("RaiseLogEntry").Invoke(client, new object[] { new LogEventArgs("test") });
        Check(next == 1 && records.Any(e => e.Message == "LogObserverFailed"), "actual Host log multicast continues after failing observer");
        Check(records.Single(e => e.Message == "LogObserverFailed").ExtensionId == typeof(Program).Assembly.GetName().Name,"static observer faults retain declaring assembly ownership instead of blaming Host");
        var runtimeType = hostAssembly.GetType("PhinixClient.Framework.ClientExtensionRuntime", true);
        object runtime = Activator.CreateInstance(runtimeType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { context, new Func<bool>(() => true) }, null);
        var discovered = (DiscoveredPhinixExtensions)runtimeType.GetProperty("Extensions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runtime, null);
        var first = new Interceptor { Broken = true }; var second = new Interceptor(); discovered.MessageInterceptors.Add(first); discovered.MessageInterceptors.Add(second);
        type.GetField("extensionRuntime", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(client, runtime);
        var suppress = type.GetMethod("shouldSuppress", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(!(bool)suppress.Invoke(client, new object[] { new FrameworkDisplayMessage() }) && second.Calls == 1 && records.Any(e => e.Message == "MessageInterceptFailed"), "actual display interceptor failure does not block healthy peer");
        type.GetField("displayMessagesLock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(client,new object());
        var stored = new List<FrameworkDisplayMessage>();
        type.GetField("displayMessages",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(client,stored);
        int displayEvents = 0;
        type.GetEvent("OnDisplayMessageReceived").AddEventHandler(client,new EventHandler<FrameworkDisplayMessageEventArgs>((sender,e) => FaultOrigin()));
        type.GetEvent("OnDisplayMessageReceived").AddEventHandler(client,new EventHandler<FrameworkDisplayMessageEventArgs>((sender,e) => displayEvents++));
        var addDisplay = type.GetMethod("addDisplayMessage",BindingFlags.NonPublic|BindingFlags.Instance);
        var message = new FrameworkDisplayMessage { MessageId = "e2.display", Source = "e2" };
        addDisplay.Invoke(client,new object[] {message}); addDisplay.Invoke(client,new object[] {message});
        Check(displayEvents == 1 && stored.Count == 1 && records.Any(e => e.Message == "DisplayMessageObserverFailed"),"display observer failure is isolated without replaying stored message");
        int modeEvents = 0;
        type.GetEvent("CompatibilityModeChanged").AddEventHandler(client,new EventHandler<FrameworkCompatibilityModeChangedEventArgs>((sender,e) => FaultOrigin()));
        type.GetEvent("CompatibilityModeChanged").AddEventHandler(client,new EventHandler<FrameworkCompatibilityModeChangedEventArgs>((sender,e) => modeEvents++));
        type.GetMethod("setCompatibilityMode",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(client,new object[] {FrameworkCompatibilityMode.FrameworkV2,null,null,new string[0]});
        Check(modeEvents == 1 && records.Any(e => e.Message == "CompatibilityModeObserverFailed"),"compatibility observer failure reaches central diagnostic without blocking later subscriber");
    }

    private static void PacketSubmission(string directory)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(directory, "PhinixClient.dll"));
        var type = assembly.GetType("PhinixClient.Framework.PhinixFrameworkClient", true);
        var client = FormatterServices.GetUninitializedObject(type);
        var records = new List<HostLogEntry>();
        var context = new ExtensionHostContext { StructuredLog = records.Add };
        var transport = new SendTransport();
        var admissionType = assembly.GetType("PhinixClient.Framework.FrameworkAdmissionControlState", true);
        var admission = Activator.CreateInstance(admissionType);
        Action<string,object> set = (name,value) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(client,value);
        set("extensionHostContext", context); set("netClient",transport); set("admissionControl",admission);
        var send = type.GetMethod("sendPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        Func<FrameworkPacket,Exception> attempt = packet => {
            try { send.Invoke(client,new object[] {packet}); return null; }
            catch (TargetInvocationException error) { return error.InnerException; }
        };
        Func<FrameworkPacket> packetFactory = () => new FrameworkPacket { MessageId = Guid.NewGuid().ToString(), Kind = FrameworkProtocol.KindCommand, MessageType = "test" };
        transport.IsConnected = false;
        var local = attempt(packetFactory());
        Check(local is ClientPacketNotSentException && local.InnerException is NotConnectedException && transport.Calls == 0, "disconnected send explicitly reports NotSent without entering transport");
        Check(records.Any(e => e.Message == "PacketPreparationFailed" && ReferenceEquals(e.Exception,local.InnerException)), "local send failure retains original diagnostic cause");
        transport.IsConnected = true;
        Check(attempt(null) is ClientPacketNotSentException && transport.Calls == 0, "null packet cannot silently succeed");
        set("disposed",true);
        Check(attempt(packetFactory()) is ClientPacketNotSentException && transport.Calls == 0, "disposed sender cannot silently succeed");
        set("disposed",false);
        Exception original; try { FaultOrigin(); original = null; } catch (Exception error) { original = error; }
        transport.Failure = original;
        Check(ReferenceEquals(attempt(packetFactory()),original) && original.StackTrace.Contains("FaultOrigin") && transport.Calls == 1, "transport fault propagates original instance and stack, with no second send");
        Check(records.Any(e => e.Message == "PacketSendOutcomeUnknown" && ReferenceEquals(e.Exception,original)), "transport failure records unknown outcome instead of falsely proving NotSent");
        transport.Failure = null;
        Check(attempt(packetFactory()) == null && transport.Calls == 2, "successful submission enters transport exactly once");
        admissionType.GetMethod("Begin").Invoke(admission,new object[] {"test-session",Guid.NewGuid().ToString()});
        admissionType.GetMethod("RejectMalformed").Invoke(admission,new object[] {"test-session"});
        Check(attempt(packetFactory()) is ClientPacketNotSentException && transport.Calls == 2, "rejected negotiation fails before transport instead of silent return");
    }

    private static void LocalizationAndLinks(string directory)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(directory,"PhinixClient.dll"));
        var errors = Errors();
        var type = assembly.GetType("PhinixClient.Framework.ClientLocalizationService",true);
        object value = Activator.CreateInstance(type,BindingFlags.NonPublic|BindingFlags.Instance,null,
            new object[] { new[] {typeof(Good)}, new Func<Type,ExtensionLocalizationCatalog>(t => ExtensionLocalizationCatalog.Empty), new Func<bool>(() => true), null, new Func<string,string,Exception,string>(errors.ReportBoundary) },null);
        var module = new Good();
        ((IExtensionModuleLifecycleObserver)value).OnActivating(module);
        var localizer = ((IClientLocalizationService)value).ForModule(module);
        int later = 0;
        localizer.LanguageChanged += FaultOrigin;
        localizer.LanguageChanged += () => later++;
        type.GetMethod("UpdateLanguage",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(value,new object[] {"en-US"});
        Check(later == 1 && errors.Capture().Any(r => r.Operation == "LocalizationListenerFailed") && errors.Capture().Any(r => errors.BuildReport(r.Id).Contains("FaultOrigin")),"actual localization listener keeps original stack and later subscribers run");
        Check(localizer.Format("missing",Enumerable.Repeat((object)"value",33).ToArray()) == "missing" && errors.Capture().Any(r => r.Operation == "LocalizationFormatFailed"),"actual localization format rejection retains detailed diagnostic and returns fallback");
        ((IDisposable)value).Dispose();

        int fallback = 0;
        type = assembly.GetType("PhinixClient.Framework.ClientLinkService",true);
        value = Activator.CreateInstance(type,BindingFlags.NonPublic|BindingFlags.Instance,null,
            new object[] { new Func<bool>(() => true), new Func<bool>(() => true), new Action<string>(url => FaultOrigin()), new Action<string>(url => fallback++), null, new Func<string,string,Exception,string>(errors.ReportSensitiveBoundary) },null);
        Check(((IClientLinkService)value).Open("https://example.test/?token=private-link-token") == ClientLinkOpenResult.ExternalBrowserRequested && fallback == 1,"actual browser failure still makes one external fallback request");
        var report = errors.BuildReport(errors.Capture().Single(r => r.Operation == "GameBrowserUnavailable").Id);
        Check(report.Contains("FaultOrigin") && !report.Contains("e2-original-cause") && !report.Contains("private-link-token"),"browser diagnostics preserve frames without exporting arbitrary native messages or link queries");

        type = assembly.GetType("PhinixClient.ClientPresentationService",true);
        object presentation = FormatterServices.GetUninitializedObject(type);
        type.GetField("fault",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(presentation,new Func<string,string,Exception,string>(errors.ReportBoundary));
        var sessionType = type.GetNestedType("Session",BindingFlags.NonPublic);
        object session = Activator.CreateInstance(sessionType,BindingFlags.NonPublic|BindingFlags.Instance,null,
            new object[] {presentation,new TestLifetime(),"e2.page","e2.page"},null);
        Exception original = null;
        try { FaultOrigin(); } catch(Exception error) { original = error; }
        sessionType.GetMethod("Fail",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,new object[] {"RenderFailed",original});
        Check((string)sessionType.GetProperty("FailureCode").GetValue(session,null) == "RenderFailed" && errors.Capture().Any(r => r.Operation == "Presentation/RenderFailed" && errors.BuildReport(r.Id).Contains("FaultOrigin")),"actual presentation failure preserves session status and original frames in Host report");
    }

    private sealed class TestLifetime : IClientLifetimeScope
    {
        public string OwnerId => "e2.presentation";
        public bool IsClosed => false;
        public System.Threading.CancellationToken Stopping => System.Threading.CancellationToken.None;
        public IDisposable Own(IDisposable value) => value;
        public IDisposable Subscribe(Action<Action> subscribe,Action<Action> unsubscribe,Action callback) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
