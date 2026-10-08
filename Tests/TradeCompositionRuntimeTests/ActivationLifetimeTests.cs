using System;
using System.Collections.Generic;
using System.Reflection;
using Phinix.InventoryExtension;
using Phinix.TradeExtension.Client;
using Phinix.TradeExtension;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UnityEngine;
using UserManagement;
using Utils.Framework;
using Verse;

internal static partial class Program
{
    private static void CheckActivationLifetime(Type type, bool legacy, List<string> facts)
    {
        foreach (string scenario in legacy ? new[] { "success" } : new[] { "success", "source-failure", "subscription-failure", "stop-failure", "missing-dependency", "ui-failure", "behavior-failure", "missing-inventory" })
        {
            var services = new TradeHost();
            var inventory = new InventoryHost { FailSource = scenario == "source-failure", FailRelease = scenario == "stop-failure" };
            var errors = new List<Exception>();
            using (var factory = new ClientCompositionFactory(() => true, errors.Add))
            {
                var host = new ExtensionHostContext();
                host.AddService<IClientCompositionFactory>(factory);
                host.AddService<IUiTheme>(services);
                host.AddService<IFrameworkClientTransport>(services);
                host.AddService<IFrameworkClientCommandTransport>(services);
                host.AddService<IFrameworkClientLifecycle>(services);
                host.AddService<IClientSessionContext>(services);
                host.AddService<IClientSettingsContext>(services);
                host.AddService<IClientUserEventStream>(services);
                host.AddService<IClientUserDirectory>(services);
                host.AddService<IClientMainThreadDispatcher>(services);
                if (scenario != "missing-dependency") host.AddService<IClientWindowService>(services);
                host.AddService<Action<bool>>(value => services.AcceptingUpdates++);
                var registry = (ExtensionApiRegistry)host.ApiRegistry;
                registry.RegisterApi<IInventoryRegistrationApi>("builtin.inventory", inventory);
                registry.RegisterApi<IInventoryDepositApi>("builtin.inventory", inventory);
                registry.RegisterApi<IInventoryReadApi>("builtin.inventory", inventory);
                if(scenario != "missing-inventory") registry.RegisterApi<IInventoryReservationApi>("builtin.inventory", inventory);
                var module = (IPhinixExtensionModule)Activator.CreateInstance(type);
                module.Register(new Builder(host, false));
                if (scenario == "subscription-failure") services.FailAdd = "compatibility";
                if (scenario == "ui-failure") services.FailAdd = "name";
                if (scenario == "behavior-failure") services.FailAdd = "settings#2";
                bool failed = false;
                try { ((IActivatablePhinixExtensionModule)module).Activate(host); }
                catch (Exception) { failed = true; }
                bool success = scenario == "success" || scenario == "stop-failure";
                Assert(failed != success, "Expected activation outcome: " + scenario);
                Action staleCompatibility = null;
                if (success)
                {
                    Assert(inventory.Codecs == 1 && inventory.Sources == 1, "Activation registers one codec/source.");
                    Assert(services.Count("compatibility") == 1 && services.Count("users") == 1 && services.Count("disconnected") == 2 && services.Count("name") == 1 && services.Count("settings") == 2 && inventory.AvailabilityCount == 1,
                        "Connection, UI and behavior subscriptions match original startup.");
                    Assert(services.Commands.Count == 1 && services.Commands[0].MessageType == FrameworkTradeProtocol.SnapshotType, "Negotiated startup requests trade snapshot.");
                    Assert(services.AcceptingUpdates == 1, "Initial accepting-trades update occurs once.");
                    services.Emit("users"); services.Set("trade.acceptingTrades", false); services.Emit("disconnected"); services.Emit("users"); services.Compatibility();
                    Assert(services.AcceptingUpdates == 3 && services.Commands.Count == 2, "Settings/disconnect and compatibility behavior preserved.");
                    object liveBehavior = Field(module, "defaultTradeBehaviour");
                    services.Set(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.Inventory);
                    services.Drain();
                    var completed = liveBehavior.GetType().GetMethod("onTradeCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
                    completed.Invoke(liveBehavior, new object[] { null, new TradeCompletionEventArgs("empty-completion", true, "remote", new TradeItemSnapshot[0]) });
                    Assert(services.Commands.Count == 2, "Completion ACK waits for main-thread delivery.");
                    services.Drain();
                    Assert(services.Commands.Count == 3 && services.Commands[2].MessageType == FrameworkTradeProtocol.CompletionAckRequestType,
                        "Successful empty/idempotent delivery acknowledges through command pipeline.");
                    if (scenario == "success") facts.Add("activation=" + services.Subscriptions + ";inventory=" + inventory.AvailabilityCount + ";commands=" + services.Commands.Count + ";accepting=" + services.AcceptingUpdates);
                    var callback = (EventHandler<FrameworkCompatibilityModeChangedEventArgs>)services.Listener("compatibility");
                    staleCompatibility = () => callback(services, new FrameworkCompatibilityModeChangedEventArgs(FrameworkCompatibilityMode.FrameworkV2));
                    if (!legacy)
                    {
                        int count = services.Subscriptions;
                        object behavior = Field(module, "defaultTradeBehaviour");
                        ((IActivatablePhinixExtensionModule)module).Activate(host);
                        behavior.GetType().GetMethod("Start").Invoke(behavior, null);
                        Assert(services.Subscriptions == count && inventory.Codecs == 1 && inventory.AvailabilityCount == 1, "Repeated activation/Start is idempotent.");
                        int ran = 0;
                        behavior.GetType().GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behavior, new object[] { (Action)(() => ran++) });
                        var ui = (ITradeUiHostContext)Field(module, "tradeUiHostContext");
                        ui.RunOnMainThread(() => ran++);
                        Assert(services.Queue.Count == 2, "Behavior and UI work wait for main thread.");
                        behavior.GetType().GetMethod("Stop").Invoke(behavior, null);
                        behavior.GetType().GetMethod("Start").Invoke(behavior, null);
                        services.Drain();
                        Assert(ran == 1, "Prior behavior generation discarded while live UI work executes.");
                        behavior.GetType().GetMethod("Enqueue", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behavior, new object[] { (Action)(() => ran++) });
                        ui.RunOnMainThread(() => ran++);
                        completed.Invoke(behavior, new object[] { null, new TradeCompletionEventArgs("stale-completion", true, "remote", new TradeItemSnapshot[0]) });
                        var facade = (IClientTradeService)Field(module, "tradeFacade");
                        if (scenario == "stop-failure") services.FailRemove = "compatibility";
                        ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                        services.Drain(); staleCompatibility();
                        Assert(ran == 1 && services.Commands.Count == 3, "Shutdown rejects queued work and captured connection callback.");
                        Assert(inventory.Deposits == 0, "Shutdown does not deposit/ack an old queued operation.");
                        int rejected = 0;
                        foreach (Action send in new Action[] { () => ((ITradeRequestApi)facade).CreateTrade("remote"), () => facade.CancelTrade("old"),
                            () => facade.UpdateTradeItems("old", new TradeItemSnapshot[0]), () => facade.UpdateTradeStatus("old", true) })
                            try { send(); } catch (InvalidOperationException) { rejected++; }
                        Assert(rejected == 4 && services.Commands.Count == 3, "Stopped public facade rejects all mutation entry points.");
                        int queued = services.Queue.Count; ui.RunOnMainThread(() => ran++);
                        Assert(services.Queue.Count == queued, "Stopped UI does not queue new work.");
                    }
                }
                ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                Assert(services.Subscriptions == 0 && inventory.AvailabilityCount == 0, "All host/inventory subscriptions detached, including failure paths.");
                Assert(inventory.CodecReleases == inventory.Codecs && inventory.SourceReleases == inventory.Sources, "All acquired inventory tokens released once.");
                Assert(services.Disposed == 0 && inventory.Disposed == 0, "Borrowed host and inventory APIs survive scope shutdown.");
                if (!legacy)
                {
                    Assert(Field(module, "composition") == null && Field(module, "activationComposition") == null && Field(module, "lifecycle") == null, "Both scopes and borrowed references cleared.");
                    if (scenario == "stop-failure") Assert(errors.Count >= 2, "Both subscription and token cleanup failures reported without skipping release.");
                }
            }
        }
    }

    private sealed class TradeHost : IFrameworkClientTransport, IFrameworkClientCommandTransport, IFrameworkClientLifecycle,
        IClientSessionContext, IClientSettingsContext, IClientUserEventStream, IClientUserDirectory,
        IClientMainThreadDispatcher, IClientWindowService, IUiTheme, IDisposable
    {
        private readonly Dictionary<string, Delegate> listeners = new Dictionary<string, Delegate>();
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();
        public readonly List<Action> Queue = new List<Action>();
        public readonly List<FrameworkPacket> Commands = new List<FrameworkPacket>();
        public int AcceptingUpdates, Disposed;
        public string FailAdd, FailRemove;
        public int Subscriptions { get { int count = 0; foreach (Delegate value in listeners.Values) count += value?.GetInvocationList().Length ?? 0; return count; } }
        public int Count(string key) => Listener(key)?.GetInvocationList().Length ?? 0;
        public Delegate Listener(string key) { listeners.TryGetValue(key, out Delegate value); return value; }
        private void Add(string key, Delegate value) { listeners[key] = Delegate.Combine(Listener(key), value); if (FailAdd == key || FailAdd == key + "#" + Count(key)) throw new InvalidOperationException("Injected add failure."); }
        private void Remove(string key, Delegate value) { listeners[key] = Delegate.Remove(Listener(key), value); if (FailRemove == key) throw new InvalidOperationException("Injected remove failure."); }
        public event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged { add { Add("compatibility", value); } remove { Remove("compatibility", value); } }
        public event EventHandler UsersChanged { add { Add("users", value); } remove { Remove("users", value); } }
        public event EventHandler Disconnected { add { Add("disconnected", value); } remove { Remove("disconnected", value); } }
        public event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged { add { Add("name", value); } remove { Remove("name", value); } }
        public event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged { add { Add("blocks", value); } remove { Remove("blocks", value); } }
        public event Action<string, object> OnSettingChanged { add { Add("settings", value); } remove { Remove("settings", value); } }
        public void Emit(string key) { ((EventHandler)Listener(key))?.Invoke(this, EventArgs.Empty); }
        public void Compatibility() { ((EventHandler<FrameworkCompatibilityModeChangedEventArgs>)Listener("compatibility"))?.Invoke(this, new FrameworkCompatibilityModeChangedEventArgs(FrameworkCompatibilityMode.FrameworkV2)); }
        public FrameworkCompatibilityMode CompatibilityMode => FrameworkCompatibilityMode.FrameworkV2;
        public bool Authenticated => true; public bool LoggedIn => true; public string SessionId => "session"; public string Uuid => "local";
        public bool HasRemoteCapability(string capability) => true;
        public void SendFrameworkPacket(FrameworkPacket packet) { throw new InvalidOperationException("Must use command pipeline."); }
        public bool TryHandleOutgoingMessage(string message) => false; public bool TryHandleOutgoingItem(FrameworkItemPayload payload) => false;
        public bool TryHandleOutgoingCommand(FrameworkPacket packet) { Commands.Add(packet); return true; }
        public T Get<T>(string key, T defaultValue = default(T)) => values.TryGetValue(key, out object value) ? (T)value : defaultValue;
        public void Set<T>(string key, T value) { values[key] = value; ((Action<string, object>)Listener("settings"))?.Invoke(key, value); }
        public IEnumerable<string> BlockedUsers => new string[0]; public bool CollapseBlockedUsers { get; set; }
        public void BlockUser(string uuid) { } public void UnBlockUser(string uuid) { }
        public ImmutableUser[] GetUsers(bool loggedIn = false) => new ImmutableUser[0];
        public bool TryGetUser(string uuid, out ImmutableUser user) { user = new ImmutableUser(uuid); return true; }
        public void Enqueue(Action action) { Queue.Add(action); }
        public void Drain() { Action[] actions = Queue.ToArray(); Queue.Clear(); foreach (Action action in actions) action(); }
        public void Open(Window window) { throw new InvalidOperationException("Unexpected game UI work."); } public void OpenSettingsWindow() { }
        public Color PrimaryText => default(Color); public Color SecondaryText => default(Color); public Color Background => default(Color);
        public Color Surface => default(Color); public Color Separator => default(Color); public Color HoverHighlight => default(Color);
        public Color Pending => default(Color); public Color Error => default(Color); public Color Success => default(Color); public Color Warning => default(Color);
        public void RegisterColor(string key, Color value) { } public Color GetColor(string key) => default(Color);
        public bool TryGetColor(string key, out Color value) { value = default(Color); return true; }
        public void RegisterFloat(string key, float value) { } public float GetFloat(string key, float defaultValue = 0f) => defaultValue; public void Reload() { }
        public void Dispose() { Disposed++; }
    }

    private sealed class InventoryHost : IInventoryRegistrationApi, IInventoryDepositApi, IInventoryReadApi, IInventoryReservationApi, IDisposable
    {
        private EventHandler availability;
        public bool FailSource, FailRelease;
        public int Codecs, Sources, CodecReleases, SourceReleases, Deposits, Disposed;
        public int AvailabilityCount => availability?.GetInvocationList().Length ?? 0;
        public event EventHandler AvailabilityChanged { add { availability += value; } remove { availability -= value; } }
        public event EventHandler InventoryChanged { add { } remove { } }
        public IDisposable RegisterCodecScoped(IInventoryCodec codec) { Codecs++; return new Token(() => CodecReleases++); }
        public IDisposable RegisterSourcePresenter(IInventorySourcePresenter source)
        { if (FailSource) throw new InvalidOperationException("Injected source failure."); Sources++; return new Token(() => { SourceReleases++; if (FailRelease) throw new InvalidOperationException("Injected release failure."); }); }
        public InventoryDepositResult CheckDeposit(InventoryDeposit deposit) { throw new NotSupportedException(); }
        public InventoryDepositResult TryDeposit(InventoryDeposit deposit) { Deposits++; throw new NotSupportedException(); }
        public IReadOnlyList<InventoryEntry> GetSnapshot() => new InventoryEntry[0]; public InventoryStatus GetStatus() => new InventoryStatus(); public InventoryCapabilities GetCapabilities() => new InventoryCapabilities();
        public IReadOnlyList<InventoryEntry> GetAvailableSnapshot() => new InventoryEntry[0]; public IReadOnlyList<InventoryReservation> GetReservations() => new InventoryReservation[0];
        public InventoryReservationResult TryReserve(InventoryReservationRequest request) { throw new NotSupportedException(); }
        public InventoryMaterializationResult CreatePreview(string entryId) { throw new NotSupportedException(); }
        public InventoryMaterializationResult MaterializeReservation(string reservationId) { throw new NotSupportedException(); }
        public InventoryReservationResolutionResult ResolveReservation(string id, InventoryReservationResolution resolution, string evidence) { throw new NotSupportedException(); }
        public void Dispose() { Disposed++; }
    }
    private sealed class Token : IDisposable
    {
        private Action release;
        public Token(Action release) { this.release = release; }
        public void Dispose() { Action action = release; release = null; action?.Invoke(); }
    }
}
