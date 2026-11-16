using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;

[Utils.Framework.PhinixExtension("test.plugin")]
internal sealed class Module : Utils.Framework.IPhinixExtensionModule
{
    public string ExtensionId => "test.plugin";
}
internal sealed class HostileException : Exception
{
    public override string Message => throw new Exception("broken Message");
    public override string ToString() => throw new Exception("broken formatter");
}
internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); checks++; }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Origin() { throw new InvalidOperationException("original-cause"); }
    [MethodImpl(MethodImplOptions.NoInlining)] private static Exception Captured()
    { try { Origin(); } catch (Exception error) { return error; } throw new Exception("Origin did not throw"); }
    private static ClientErrorService Service(Action<string> log = null) => new ClientErrorService("Host: test\nGame: test\nRuntime: test", () => true, log);

    private static void Main()
    {
        try
        {
            StacksAndIdentity(); NotificationAndCopy(); BoundedAndConcurrent(); Dispatcher(); RootRethrow(); ModuleOwnership(); BuiltInModuleDiagnostics(); Window(); PublicBoundaries();
            Console.WriteLine("CLIENT ERRORS PASS " + checks + " checks; production error service/controller/dispatcher/RootPatch");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }
    private static void StacksAndIdentity()
    {
        var logs = new List<string>(); var errors = Service(logs.Add); var original = Captured();
        string id = errors.ReportHost(original, "Test", "Read");
        string text = errors.BuildReport(id);
        Check(text.Contains("Origin") && text.Contains("original-cause"), "original frame and cause retained");
        Check(errors.ReportHost(original, "Outer", "Outer") == id && errors.Capture()[0].Count == 1, "same exception crossing boundaries is one occurrence");
        for (int i = 0; i < 3; i++) errors.ReportHost(Captured(), "Loop", "Repeated");
        Check(errors.Capture()[0].Count == 3 && logs.Count == 2, "same call site grouped; log emitted once per group");
        string tree = errors.BuildReport(errors.ReportHost(new AggregateException("aggregate", Captured(), new Exception("second-inner")), "Tree", "Aggregate"));
        Check(tree.Contains("Aggregate[0]") && tree.Contains("Aggregate[1]") && tree.Contains("second-inner") && tree.Contains("Origin"), "all aggregate causes retained");
        string loaders = errors.BuildReport(errors.ReportHost(new ReflectionTypeLoadException(new Type[0], new[] { Captured(), new Exception("missing-assembly") }), "Load", "Loader"));
        Check(loaders.Contains("Loader[0]") && loaders.Contains("Loader[1]") && loaders.Contains("missing-assembly"), "all loader causes retained");
        Check(errors.BuildReport(errors.ReportHost(new HostileException(), "Hostile", "Format")).Contains("message unavailable"), "third-party formatter cannot prevent capture");
        string sensitive = errors.BuildReport(errors.ReportHost(new Exception("password=hunter2 token=private-token-value sessionId=private-session-value Authorization: Bearer private-bearer-value https://user:pass@example.test/?key=leak /home/alice/private/file.txt"), "Safe", "Safe"));
        Check(!new[] { "hunter2", "private-token-value", "private-session-value", "private-bearer-value", "user:pass", "key=leak", "/home/alice" }.Any(sensitive.Contains), "known credentials/URL/user path redacted");
        string modernSecrets = ClientErrorText.Safe("{\"NextSecret\":\"private-next-value\",\"access_token\":\"private-access-value\",\"client_secret\":\"private-client-value\",\"session_id\":\"private-session-value\"}", 2048);
        Check(!new[] { "private-next-value", "private-access-value", "private-client-value", "private-session-value" }.Any(modernSecrets.Contains), "JSON and compound identity credential names redacted");
        var sensitiveBoundary = Service();
        var sensitiveReport = sensitiveBoundary.BuildReport(sensitiveBoundary.ReportSensitiveBoundary("builtin.host", "Browser", Captured()));
        Check(sensitiveReport.Contains("Origin") && !sensitiveReport.Contains("original-cause") && sensitiveReport.Contains("sensitive boundary"), "sensitive boundaries retain frames while omitting arbitrary native exception messages");
        Exception deep = new Exception("leaf"); for (int i = 0; i < 30; i++) deep = new Exception("level", deep);
        Check(errors.BuildReport(errors.ReportHost(deep, "Deep", "Deep")).Contains("truncated"), "exception tree limits explicit");
        var large = errors.BuildReport(errors.ReportHost(new Exception(new string('x', 100000)), "Large", "Large"));
        Check(large.Length < ClientErrorService.MaximumDetailCharacters && large.Contains("size limit"), "oversize arbitrary message omitted explicitly");
        string same = errors.ReportHost(original, "Again", "Again");
        Check(same == id && logs[0].Contains(id), "same ID connects captured report and standard log");
    }
    private static void NotificationAndCopy()
    {
        var errors = Service(); bool ready = false, open = false; int openings = 0;
        var ui = new ClientErrorNotificationController(errors, () => ready, () => open, () => { openings++; open = true; });
        string id = errors.ReportHost(Captured(), "Popup", "Action");
        ui.Pump(); Check(openings == 0 && errors.HasPending, "early fault waits for UI readiness");
        ready = true; ui.Pump(); ui.Pump(); Check(openings == 1 && !errors.HasPending, "one popup without business queue");
        string copied = null;
        Check(ui.Copy(id, value => copied = value) && copied.Contains("Origin"), "copy uses full retained report");
        Check(!ui.Copy(id, value => { throw new Exception("clipboard denied"); }) && errors.BuildReport(id).Contains("original-cause"), "clipboard failure leaves original available");
        Check(!errors.HasPending && ui.Copy(id, value => copied = value), "clipboard failure does not trigger recursive popup and can retry");
        open = false; errors.ReportHost(Captured(), "Popup", "Action"); ui.Pump();
        Check(openings == 1 && errors.Capture().First(e => e.Id == id).Count == 2, "dismissed repeat updates count without reopening");
        errors.ReportHost(new Exception("new-fault"), "New", "Action"); ui.Pump();
        Check(openings == 2, "distinct new fault can reopen");
        errors.ReportHost(new Exception("manual"), "Silent", "Manual", false); open = false; ui.Show();
        Check(openings == 3, "recent errors can reopen manually even without pending popup");
        var broken = new ClientErrorNotificationController(errors, () => true, () => false, () => { throw new Exception("window unavailable"); });
        errors.ReportHost(new Exception("pending"), "Pending", "Open"); broken.Pump();
        Check(!errors.HasPending && errors.Capture().Any(e => e.Code == "ErrorWindowFailed"), "open failure retained without recursive window");
        var raceErrors = Service();
        var race = new ClientErrorNotificationController(raceErrors, () => true, () => false,
            () => raceErrors.ReportHost(new Exception("arrived during open"), "Race", "Race"));
        raceErrors.ReportHost(Captured(), "Before", "Open"); race.Pump();
        Check(raceErrors.HasPending, "concurrent new notification is not acknowledged before display");
        var silent = Service(); var error = Captured();
        silent.ReportHost(error, "SilentFirst", "Read", false);
        Check(!silent.HasPending, "silent report retained without popup");
        silent.ReportHost(error, "LaterBoundary", "Read", true);
        Check(silent.HasPending && silent.Capture().Count == 1, "same silent exception can request a popup once at a later boundary");
    }
    private static void BoundedAndConcurrent()
    {
        var errors = Service(value => { throw new Exception("broken sink"); });
        string first = errors.ReportHost(Captured(), "Sink", "Sink");
        Check(errors.BuildReport(first).Contains("Diagnostic sink failures: 1"), "sink failure recorded after original capture");
        ClientErrorService recursive = null;
        recursive = Service(value => recursive.ReportHost(new Exception("recursive"), "Recursive", "Log"));
        string rid = recursive.ReportHost(Captured(), "Primary", "Log");
        Check(recursive.Capture().Count == 1 && recursive.BuildReport(rid).Contains("Suppressed recursive reports: 1"), "sink recursion bounded and counted");
        Parallel.For(0, 100, i => errors.ReportHost(new Exception("unique"), "Unique" + i, "Concurrent"));
        Check(errors.Capture().Count == ClientErrorService.MaximumRecords && errors.BuildReport(first) == null, "record count bounded and oldest evicted");
        Check(errors.BuildReport(errors.Capture()[0].Id).Contains("Evicted groups: 37"), "eviction visible in copied report");
        var shared = Captured(); var concurrent = Service();
        Parallel.For(0, 100, i => concurrent.ReportHost(shared, "Same", "Concurrent"));
        Check(concurrent.Capture().Count == 1 && concurrent.Capture()[0].Count == 1, "concurrent same object deduplicated atomically");
        var serialized = Service();
        Parallel.For(0, 100, i => serialized.ReportHost(new InvalidOperationException("loop"), "Same", "Concurrent"));
        Check(serialized.Capture().Count == 1 && serialized.Capture()[0].Count == 100, "concurrent same fingerprint counts every occurrence");
        var weak = WeakException(serialized); for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Check(!weak.IsAlive, "retained reports do not keep original exception alive");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static WeakReference WeakException(ClientErrorService errors)
    { var exception = Captured(); errors.ReportHost(exception, "Weak", "Weak"); return new WeakReference(exception); }
    private static void Dispatcher()
    {
        var errors = Service(); var dispatcher = new ClientMainThreadDispatcher(null, () => true, errors.ReportTask);
        var module = new Module(); errors.OnActivating(module); dispatcher.SetModuleTypes(new[] { typeof(Module) }); dispatcher.OnActivating(module);
        using (var scope = dispatcher.ForModule(module))
        {
            int next = 0; var failed = scope.Submit(Origin); scope.Submit(() => next++); dispatcher.DrainPendingActions();
            Check(failed.Capture().State == ClientTaskState.Failed && next == 1, "callback fails, unrelated task continues");
            Check(errors.Capture()[0].Owner == module.ExtensionId && errors.BuildReport(errors.Capture()[0].Id).Contains("Origin"), "task report keeps owner and original frame");
            for (int i = 0; i < 3; i++) { scope.Submit(Origin); dispatcher.DrainPendingActions(); }
            Check(errors.Capture().Count == 1 && errors.Capture()[0].Count == 4, "different task IDs do not defeat popup deduplication");
            for (int i = 0; i < 500; i++) scope.Submit(() => { });
            string id = errors.ReportHost(Captured(), "QueueFull", "Notify"); int opened = 0;
            new ClientErrorNotificationController(errors, () => true, () => false, () => opened++).Pump();
            Check(opened == 1 && errors.BuildReport(id) != null && dispatcher.PendingCount == 500, "full business queue cannot starve error popup");
        }
        Check(dispatcher.PendingCount == 0, "scope shutdown still clears queued callbacks");
        string fallback = null; var broken = new ClientMainThreadDispatcher((s, level) => fallback = s, () => true,
            (owner, operation, exception) => { throw new Exception("bad reporter"); });
        broken.Enqueue(Origin); broken.DrainPendingActions();
        Check(fallback.Contains("Origin") && fallback.Contains("original-cause"), "failed reporter falls back to complete callback exception");
    }
    private static void RootRethrow()
    {
        var errors = Service(); var original = Captured();
        PhinixClient.Client.Instance = new PhinixClient.Client { Errors = errors, Work = () => { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw(); } };
        var update = typeof(PhinixClient.Patches.RootPatch).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static);
        Exception propagated = null; try { update.Invoke(null, null); } catch (TargetInvocationException error) { propagated = error.InnerException; }
        Check(ReferenceEquals(original, propagated) && propagated.StackTrace.Contains("Origin"), "production RootPatch rethrows original exception to game entry");
        Check(errors.Capture().Count == 1 && errors.HasPending && errors.BuildReport(errors.Capture()[0].Id).Contains("Origin"), "report exists before player interacts with UI");
        PhinixClient.Client.Instance.ThrowWhileReporting = true;
        try { update.Invoke(null, null); } catch (TargetInvocationException error) { propagated = error.InnerException; }
        Check(ReferenceEquals(original, propagated), "reporter failure cannot mask original game exception");
        PhinixClient.Client.Instance = null;
    }
    private static void ModuleOwnership()
    {
        var errors = Service(); var module = new Module(); bool rejected = false;
        try { errors.ForModule(module); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unactivated module cannot bind reporter");
        errors.OnActivating(module); var reporter = errors.ForModule(module);
        string id = Task.Run(() => reporter.Report(Captured(), "PluginFault", "Read")).GetAwaiter().GetResult();
        Check(id != null && errors.Capture()[0].Owner == module.ExtensionId, "active plugin reporter works on background thread");
        string privateId=reporter.ReportSensitive(Captured(),"PluginPrivateFault","ReadPrivate");
        Check(privateId!=null && errors.BuildReport(privateId).Contains("Origin") && !errors.BuildReport(privateId).Contains("original-cause") && errors.Capture().Any(r=>r.Id==privateId && r.Owner==module.ExtensionId),"plugin-sensitive reporter preserves module ownership and original frames without copying arbitrary payload messages");
        errors.OnStopped(module);
        Check(reporter.ReportSensitive(Captured(),"StoppedPrivate","ReadPrivate")==null,"stopped sensitive plugin reporter is revoked without resurrecting notifications");
        Check(reporter.Report(Captured(), "Stopped", "Read") == null && errors.BuildReport(id) != null, "stopped reporter revoked; prior report retained");
    }
    private sealed class NamedModule : Utils.Framework.IPhinixExtensionModule
    {
        public NamedModule(string id){ExtensionId=id;}
        public string ExtensionId {get;}
    }
    private sealed class BrokenReporter : IClientSensitiveErrorReporter
    {
        public string Report(Exception e,string code,string operation,bool notify=true){throw new HostileException();}
        public string ReportSensitive(Exception e,string code,string operation,bool notify=true){throw new HostileException();}
    }
    private static void BuiltInModuleDiagnostics()
    {
        foreach(string owner in new[]{"builtin.inventory","builtin.trade","builtin.legacy-adapter","builtin.chat","phinix.plugin-store"})
        {
            var errors=Service();var module=new NamedModule(owner);errors.OnActivating(module);
            var scope=new ClientModuleDiagnostics();scope.Bind(errors.ForModule(module));
            var original=Captured();Task.Run(()=>scope.Report("SyntheticBusinessBoundary",original,true)).GetAwaiter().GetResult();
            var row=errors.Capture().Single();string report=errors.BuildReport(row.Id);
            Check(row.Owner==owner && report.Contains("Origin"),"module-owned diagnostic preserves actual origin and owner "+owner);
            Check(!report.Contains("original-cause") && !report.Contains("/home/") && errors.HasPending,"sensitive module copy omits payload/source path and requests a main-thread notice "+owner);
            int continued=0;scope.Cleanup("SyntheticCleanup",Origin);scope.Cleanup("HealthyCleanup",()=>continued++);
            Check(continued==1 && errors.Capture().Count==2,"broken cleanup retained independently while healthy cleanup continues "+owner);
            errors.OnStopped(module);int before=errors.Capture().Count;scope.Report("LateCallback",Captured(),true);
            Check(errors.Capture().Count==before,"stopped module cannot recreate popup diagnostics "+owner);
            scope.Unbind();scope.Report("UnboundFallback",Captured());
            Check(errors.Capture().Count==before,"unbound diagnostic retains fallback without resurrecting Host scope "+owner);
            string directory=Environment.GetEnvironmentVariable("PHINIX_ERROR_SAMPLE_DIRECTORY");
            if(!string.IsNullOrEmpty(directory)){System.IO.Directory.CreateDirectory(directory);System.IO.File.WriteAllText(System.IO.Path.Combine(directory,owner+".txt"),report);}
        }
        var broken=new ClientModuleDiagnostics();broken.Bind(new BrokenReporter());
        broken.Report("BrokenReporter",Captured(),true);int after=0;broken.Cleanup("BrokenCleanup",Origin);broken.Cleanup("AfterBrokenReporter",()=>after++);
        Check(after==1,"throwing hostile reporter cannot mask original business outcome or stop subsequent cleanup");
    }
    private static void Window()
    {
        var errors = Service(); string id = errors.ReportHost(Captured(), "Window", "Open");
        var controller = new ClientErrorNotificationController(errors, () => true, () => true, () => { });
        var window = new PhinixClient.ClientErrorWindow(errors, controller);
        var small = new UnityEngine.Rect(0, 0, 280, 200);
        Verse.Widgets.Buttons.Clear(); Verse.Widgets.Click = "Phinix_errorReport_copy";
        window.DoWindowContents(small);
        Check(window.IsOpen && UnityEngine.GUIUtility.systemCopyBuffer.Contains(id), "production window copies report and stays open");
        Check(Verse.Widgets.ScrollDepth == 0 && Verse.Widgets.LastContent.height >= Verse.Widgets.LastViewport.height, "small-window text is scrollable with balanced native groups");
        Check(Verse.Widgets.Buttons.All(b => b.Rect.y >= 0 && b.Rect.yMax <= small.yMax && b.Rect.width > 0), "copy/close controls remain inside small window");
        errors.ReportHost(new Exception("another"), "Other", "Other");
        Verse.Widgets.Click = "Phinix_errorReport_copy"; window.DoWindowContents(small);
        Check(UnityEngine.GUIUtility.systemCopyBuffer.Contains(id), "new reports do not silently change selected report during copying");
        Verse.Widgets.Click = "Phinix_errorReport_close"; window.DoWindowContents(small);
        Check(!window.IsOpen && errors.BuildReport(id) != null, "close leaves original report available for reopening");
        var broken = new PhinixClient.ClientErrorWindow(errors, controller);
        Verse.Widgets.ThrowLabel = true; broken.DoWindowContents(small); Verse.Widgets.ThrowLabel = false;
        Check(!broken.IsOpen && Verse.Widgets.ScrollDepth == 0 && errors.Capture().Any(e => e.Code == "ErrorWindowDrawFailed"), "draw failure closes emergency UI and records secondary cause without leaking scroll groups");
    }

    private sealed class Resource : IDisposable
    {
        internal Action Release;
        public void Dispose() { Release(); }
    }
    [Utils.Framework.PhinixExtension("test.other")]
    private sealed class OtherModule : Utils.Framework.IPhinixExtensionModule { public string ExtensionId => "test.other"; }
    private static IEnumerable<ClientActionDefinition> BrokenFactory(object context) { Origin(); yield break; }
    private static void PublicBoundaries()
    {
        var errors = Service(); var module = new Module(); errors.OnActivating(module);
        var life = new ClientLifetimeService(() => true, null, errors.ReportBoundary);
        life.SetModuleTypes(new[] { typeof(Module) }); life.OnActivating(module);
        var scope = life.ForModule(module); Action listener = null; int released = 0;
        scope.Subscribe(action => listener = action, action => listener = null, Origin);
        listener();
        Check(errors.Capture().Any(e => e.Operation == "Lifetime/Callback") && errors.BuildReport(errors.Capture()[0].Id).Contains("Origin"), "lifetime callback reaches central fault record with original stack");
        scope.Own(new Resource { Release = () => released++ });
        scope.Own(new Resource { Release = Origin }); scope.Dispose();
        Check(released == 1 && listener == null && errors.Capture().Any(e => e.Operation == "Lifetime/Dispose"), "bad owned resource cannot stop other cleanup or lose diagnostic");
        var events = new ClientShellEventStream(errors.ReportBoundary); int next = 0;
        events.MainWindowOpened += (sender, args) => Origin();
        events.MainWindowOpened += (sender, args) => next++;
        events.RaiseMainWindowOpened();
        Check(next == 1 && errors.Capture()[0].Owner == module.ExtensionId && errors.Capture()[0].Operation.StartsWith("Event/"), "event subscriber fault recorded under unique module assembly; later subscriber runs");
        var contributions = new ClientExtensionPointService(() => true, null, errors.ReportBoundary);
        var other = new OtherModule(); errors.OnActivating(other);
        contributions.SetModuleTypes(new[] { typeof(Module), typeof(OtherModule) });
        contributions.OnActivating(module); contributions.OnActivating(other);
        var owner = contributions.ForModule(module); var peer = contributions.ForModule(other);
        var point = owner.DefineActions<object>("test.plugin.actions");
        owner.ContributeActions<object>("test.plugin.actions", BrokenFactory);
        peer.ContributeActions<object>("test.plugin.actions", context => new[] { new ClientActionDefinition("run", "Run", Origin) });
        using (var actions = point.Collect(new object()))
        {
            Check(actions.Actions.Count == 1 && errors.Capture().Any(e => e.Operation == "Contribution/Collect"), "lazy factory failure does not discard healthy peer contribution");
            var result = actions.Actions[0].TryExecute();
            Check(result.Invoked && !result.Completed && errors.Capture()[0].Operation == "Contribution/Execute", "contributed action failure remains invoked/failed, never retry permission");
            Check(actions.Actions[0].TryExecute().Code == "AlreadyInvoked", "failed side-effect action is not invoked twice");
        }
        owner.Dispose(); peer.Dispose();
        var quiet = Service(); quiet.ReportStructured(new Utils.Framework.HostLogEntry("framework", Utils.LogLevel.WARNING, "MalformedPayload", Captured()));
        Check(quiet.Capture().Count == 1 && !quiet.HasPending, "structured recoverable fault keeps stack without popup storm");
        var levels = new List<Utils.LogLevel>();
        var leveled = new ClientErrorService("test",() => true,null,(text,level) => levels.Add(level));
        leveled.ReportStructured(new Utils.Framework.HostLogEntry("framework",Utils.LogLevel.WARNING,"Recoverable",Captured()));
        Check(levels.Single() == Utils.LogLevel.WARNING && leveled.BuildReport(leveled.Capture()[0].Id).Contains("Level: WARNING"), "recoverable diagnostics preserve warning level instead of opening the game's error log as errors");
        var escalate = Captured();
        leveled.ReportStructured(new Utils.Framework.HostLogEntry("framework",Utils.LogLevel.WARNING,"Escalate",escalate));
        leveled.ReportStructured(new Utils.Framework.HostLogEntry("framework",Utils.LogLevel.ERROR,"Escalate",escalate));
        Check(leveled.HasPending && leveled.Capture().Count == 2 && leveled.BuildReport(leveled.Capture()[0].Id).Contains("Level: ERROR"), "same diagnostic can escalate severity and request one popup without losing its identity");
        int observers = 0; var original = Captured(); Exception sinkFailure = null;
        Action<Utils.Framework.HostLogEntry> observersList = entry => { throw new Exception("broken first sink"); };
        observersList += entry => {
            if (entry.Message == "DiagnosticObserverFailed") { sinkFailure = entry.Exception; return; }
            Check(ReferenceEquals(entry.Exception, original), "fanout receives same original exception object"); observers++;
        };
        Utils.Framework.ExtensionDiagnostics.Write(observersList, null, new Utils.Framework.HostLogEntry("owner", Utils.LogLevel.ERROR, "Test", original));
        Check(observers == 1, "one failed structured sink cannot stop healthy sink");
        Check(sinkFailure != null && sinkFailure.Message == "broken first sink" && !string.IsNullOrEmpty(sinkFailure.StackTrace), "failed diagnostic observer has its own preserved stack");
        string fallback = null; bool originalFallback = false;
        Utils.Framework.ExtensionDiagnostics.Write(entry => { throw new Exception("all sinks broken"); }, (text, level) => { fallback = text; originalFallback |= text.Contains("Origin"); }, new Utils.Framework.HostLogEntry("owner", Utils.LogLevel.ERROR, "Fallback", original));
        Check(originalFallback, "all structured sinks failing falls back with complete original stack");
        Check(fallback.Contains("all sinks broken"), "failed sink details also reach legacy fallback");
        int recursiveCalls = 0; Action<Utils.Framework.HostLogEntry> cyclic = null;
        cyclic = entry => { recursiveCalls++; Utils.Framework.ExtensionDiagnostics.Write(cyclic, null, entry); };
        long before = Utils.Framework.ExtensionDiagnostics.SuppressedWrites;
        Utils.Framework.ExtensionDiagnostics.Write(cyclic, null, new Utils.Framework.HostLogEntry("owner", Utils.LogLevel.ERROR, "Cycle", original));
        Check(recursiveCalls == 8 && Utils.Framework.ExtensionDiagnostics.SuppressedWrites > before, "cyclic structured observer is bounded and counted");
    }
}
