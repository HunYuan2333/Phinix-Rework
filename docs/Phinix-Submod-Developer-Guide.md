# Phinix Submod Developer Guide

> **Target audience: third-party submod developers / 面向受众：第三方附属 Mod / Submod 开发者**
>
> **中文版：Phinix附属Mod开发者指南.md**
>
> **Document scope**: This document and [design-philosophy.md](./design-philosophy.md) are cross-branch shared baseline documents. The former explains "why this design"; this document tells you "how to use it in practice."
>
> **Last updated**: 2026-09-13, written against current codebase on the `dev` branch. The framework is still under active evolution — this document explicitly marks the current status of each capability: ✅ fully available, ⚠️ half-finished/transitional, 🔮 planned.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Dependency Boundaries](#2-dependency-boundaries)
3. [Extension Entry Point and Lifecycle](#3-extension-entry-point-and-lifecycle)
4. [Registry: What IExtensionBuilder Can Do](#4-registry-what-iextensionbuilder-can-do)
5. [API Exposure and Resolution](#5-api-exposure-and-resolution)
6. [The Three Communication Pipelines](#6-the-three-communication-pipelines)
7. [Integrating with UI](#7-integrating-with-ui)
    - [7.1 Adding a Tab](#71-adding-a-tab)
    - [7.2 Adding a Sidebar](#72-adding-a-sidebar)
    - [7.3 Responsive Layout and Adaptation (ClientExtensionAbstractions 1.1)](#73-responsive-layout-and-adaptation-clientextensionabstractions-11)
        - [7.3.1 Responsive Layout Hints and Fallback Mechanisms](#731-responsive-layout-hints-and-fallback-mechanisms)
        - [7.3.2 Host Sidebar Drawer Collapse Mechanism](#732-host-sidebar-drawer-collapse-mechanism)
        - [7.3.3 Screen Safe Area and Custom Dialogs (UiScreenSafeArea)](#733-screen-safe-area-and-custom-dialogs-uiscreensafearea)
        - [7.3.4 Allocation-Conscious Shared Layout Primitives (ClientExtensionAbstractions.UI)](#734-allocation-conscious-shared-layout-primitives-clientextensionabstractionsui)
        - [7.3.5 Geometry Calculation and Cache Invalidation Principles](#735-geometry-calculation-and-cache-invalidation-principles)
    - [7.4 Adding Badges](#74-adding-badges)
    - [7.5 Adding Settings Panels](#75-adding-settings-panels)
    - [7.6 Settings Migration (Legacy Settings)](#76-settings-migration-legacy-settings)
    - [7.7 Pushing Display Messages](#77-pushing-display-messages)
    - [7.8 Adding Notice Banners (INoticeBannerProvider)](#78-adding-notice-banners-inoticebannerprovider)
    - [7.9 Enter Key Handling (IUiAcceptKeyHandler)](#79-enter-key-handling-iuiacceptkeyhandler)
    - [7.10 UI Theme and Palette (IUiTheme)](#710-ui-theme-and-palette-iuitheme)
8. [Common Services Provided by the Host](#8-common-services-provided-by-the-host)
9. [Inter-Plugin Collaboration](#9-inter-plugin-collaboration)
10. [Compatibility Mode and Legacy](#10-compatibility-mode-and-legacy)
11. [Common Anti-Patterns and Pitfalls](#11-common-anti-patterns-and-pitfalls)
    - [11.1 Bypassing the Pipeline to Directly Access the Transport Layer](#111-bypassing-the-pipeline-to-directly-access-the-transport-layer)
    - [11.2 Calling hostContext.GetRequiredService in Register()](#112-calling-hostcontextgetrequiredservice-in-register)
    - [11.3 Forgetting to Unsubscribe from Events in Shutdown()](#113-forgetting-to-unsubscribe-from-events-in-shutdown)
    - [11.4 Object Allocation on Draw Paths](#114-object-allocation-on-draw-paths)
    - [11.5 Operating on UI from Network Callback Threads](#115-operating-on-ui-from-network-callback-threads)
    - [11.6 Silently Swallowing Exceptions](#116-silently-swallowing-exceptions)
    - [11.7 Failing to Implement IDisposable](#117-failing-to-implement-idisposable)
    - [11.8 Dependent DLL Load Order](#118-dependent-dll-load-order)
    - [11.9 Using Deprecated Legacy GUI Containers (Displayable Series)](#119-using-deprecated-legacy-gui-containers-displayable-series)
    - [11.10 Hardcoded Absolute Coordinates and Ignoring Screen Safe Area](#1110-hardcoded-absolute-coordinates-and-ignoring-screen-safe-area)
12. [Minimal Viable Example](#12-minimal-viable-example)
    - [12.1 Environment Preparation and Prerequisites](#121-environment-preparation-and-prerequisites)
    - [12.2 Directory Structure](#122-directory-structure)
    - [12.3 Project Configuration](#123-project-configuration)
    - [12.4 Complete Extension Entry Point Class Code](#124-complete-extension-entry-point-class-code)
    - [12.5 Optional: Registering a Domain Contracts Project](#125-optional-registering-a-domain-contracts-project)
    - [12.6 Build and Deployment](#126-build-and-deployment)
    - [12.7 Load Order Number Explanation](#127-load-order-number-explanation)
    - [12.8 Debugging Tips](#128-debugging-tips)
- [Appendix A: IExtensionBuilder Complete Registration Method Quick Reference](#appendix-a-iextensionbuilder-complete-registration-method-quick-reference)
- [Appendix B: ExtensionHostContext Complete Service Quick Reference](#appendix-b-extensionhostcontext-complete-service-quick-reference)

---

## 1. Architecture Overview

### 1.1 Layered Architecture

Phinix is divided into four layers from bottom to top:

```
┌─────────────────────────────────────────┐
│  Plugins (Chat, Trade, your Submod)      │  ← Business layer
├─────────────────────────────────────────┤
│  ClientExtensionAbstractions             │  ← Shared contract layer (UI interfaces + host service interfaces)
├─────────────────────────────────────────┤
│  Host (Client / Server)                  │  ← Host layer (networking, auth, extension discovery, UI shell)
├─────────────────────────────────────────┤
│  Common (Utils, Connections, etc.)       │  ← Infrastructure layer (protocols, types, utilities)
└─────────────────────────────────────────┘
```

- **Upper layers can depend on lower layers**. Plugins can reference `Utils`, `ClientExtensionAbstractions`.
- **Lower layers never reverse-depend on upper layers**. `Common/Utils/` knows nothing about any specific plugin.
- **Same-layer modules stay as independent as possible**. Chat and Trade discover each other through the API registry, not through the host.

Relevant source files:
- [Client/ClientExtensionAbstractions/](Client/ClientExtensionAbstractions/) — Shared contract layer, defines all UI and host service interfaces
- [Common/Utils/Framework/FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs) — Definitions of all handler, builder, and context types
- [Common/Utils/Framework/PhinixExtensionRegistry.cs](Common/Utils/Framework/PhinixExtensionRegistry.cs) — Extension discovery engine

### 1.2 Plugin Equal Standing

Chat and Trade are **not** privileged modules. They follow the exact same path as the submod you write:

- Same discovery path: reflection scans for classes implementing `IPhinixExtensionModule`
- Same registration path: `Register(builder)` → register handlers / APIs
- Same activation path: `Activate(hostContext)` → `Shutdown(hostContext)`

**The only difference between your submod and Chat/Trade is the Priority value**: smaller Priority executes first. Chat's Priority is 1000, Trade's is 1100, LegacyAdapter's is 500. Your submod can choose a suitable Priority value to slot between them.

### 1.3 What You Can Touch, What You Cannot

| Can reference | Cannot reference |
|----------|----------|
| `Utils` (Common layer) | `Client.csproj` host project |
| `ClientExtensionAbstractions` | `Server.csproj` host project |
| `UserManagement` | Other plugins' **internal implementation** classes |
| Other plugins' `Contracts` projects (if you want to call their API) | Putting code in the Common directory (Common only contains runtime-neutral code) |

---

## 2. Dependency Boundaries

### 2.1 Required Assemblies

Every client-side submod must reference at least the following assemblies:

| Assembly | What it provides | Project path |
|--------|----------|----------|
| `Utils` | `IPhinixExtensionModule`, `IExtensionBuilder`, `FrameworkPacket`, `FrameworkTypes` and other core types | [Common/Utils/Utils.csproj](Common/Utils/Utils.csproj) |
| `ClientExtensionAbstractions` | `IMainTabProvider`, `IServerSidebarProvider`, `IBadgeProvider`, `IClientSettingsContext` and other host service interfaces | [Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj](Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj) |

Common additional dependencies (if the submod needs to manipulate user data):

| Assembly | What it provides | Project path |
|--------|----------|----------|
| `UserManagement` | `ImmutableUser` and other user types | [Common/UserManagement/UserManagement.csproj](Common/UserManagement/UserManagement.csproj) |

Additionally, the standard RimWorld references are required: `Assembly-CSharp`, `UnityEngine`, `UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, etc.

### 2.2 Optional Assemblies

If your submod needs to call Chat or Trade capabilities:

| Assembly | What it provides | Project path |
|--------|----------|----------|
| `ChatExtension` (Contracts) | `IFrameworkChatClientApi`, `IChatUiHostContext`, etc., for direct inter-plugin calls | [Extensions/Chat/Contracts/ChatExtension.csproj](Extensions/Chat/Contracts/ChatExtension.csproj) |
| `TradeExtension` (Contracts) | `IFrameworkTradeClientApi`, `ITradeRequestApi`, etc. | [Extensions/Trade/Contracts/TradeExtension.csproj](Extensions/Trade/Contracts/TradeExtension.csproj) |

> **Note**: Referencing the Contracts project does not make you depend on Chat/Trade's internal implementation — Contracts only contains interface definitions and protocol constants. This is the recommended way of inter-plugin collaboration (see [§9](#9-inter-plugin-collaboration) for details).

### 2.3 Absolutely Forbidden References

- ❌ **Client host project**: `Client/Source/Client.csproj`. The host does not depend on plugins, and plugins cannot depend on the host.
- ❌ **Server host project**: Client-side plugins do not need it.
- ❌ **Client-specific implementation classes in Common**: e.g., `Connections.Client` (note the `.Client` suffix — it is a client-side Connections sub-project, compiled by the client, not part of Common proper).

### 2.4 Physical Deployment: Where to Place DLLs

When the host starts, it calls `ExtensionAssemblyLoader.LoadAssemblies()` to scan `.dll` files under probe directories (see the `GetExtensionProbeDirectories` method at [Client.cs:543-582](Client/Source/Client.cs#L543-L582) for details).

The physical publishing layout of the framework and official plugins has completed its separation (refer to [Design Philosophy §5.1 and §5.2](design-philosophy.md#51-naming-and-ordering)):

```
PhinixMod/
  1.6/
    Assemblies/           ← Client-specific host (13-PhinixClient.dll)
  Common/
    Assemblies/           ← Framework base DLLs (01-10, including LiteNetLib, Protobuf, Utils, Connections, Auth, UserManagement, ClientExtensionAbstractions)
    Extensions/           ← Official built-in plugin DLLs (08-16, including Chat, Trade, LegacyAdapter, RedPacket, TalentTrade, and their Client implementations)
```

#### Two Distribution and Deployment Methods for Third-Party Submods

1. **Recommended Method: Independent Mod Distribution (Steam Workshop / Standalone Mod folder)**
   - Package and distribute as a standard RimWorld mod; do not modify the Phinix mod installation directory.
   - In your mod's `About/About.xml`, declare Phinix as a prerequisite dependency (configure `<modDependencies>` and `<loadAfter><li>hunyuan.phinixrework</li></loadAfter>`).
   - Place your compiled output DLL directly in your own mod root's `Assemblies/` directory.
   - **How it works**: `GetExtensionProbeDirectories` in `Client.cs` automatically iterates through `ModLister.AllInstalledMods` to scan the `Assemblies/` directories of all active third-party mods loaded after Phinix. When the host boots, it automatically probes your DLL and discovers your `[PhinixExtension]` module classes!

2. **Integrated / Built-in Method (Directly placed inside Phinix directory)**
   - Copy your compiled DLL directly into the Phinix mod's `Common/Extensions/` directory.
   - **Important**: Official built-in plugins occupy prefixes `08-` through `16-`. Any third-party DLL placed directly into `Common/Extensions/` **must use a prefix of `17-` or higher** (e.g., `17-MySubmod.dll`), otherwise RimWorld's `ModAssemblyHandler` may attempt to load your DLL before its dependencies, resulting in class loading exceptions (see [§12.7](#127-load-order-number-explanation) for details).

ExtensionAssemblyLoader code location: [Common/Utils/Framework/ExtensionAssemblyLoader.cs](Common/Utils/Framework/ExtensionAssemblyLoader.cs).

---

## 3. Extension Entry Point and Lifecycle

### 3.1 Minimum Interface: `IPhinixExtensionModule`

Every submod must have a class implementing `IPhinixExtensionModule` (defined at [FrameworkTypes.cs:56-59](Common/Utils/Framework/FrameworkTypes.cs#L56-L59)):

```csharp
public interface IPhinixExtensionModule : IPhinixExtension
{
    string ExtensionId { get; }      // Inherited from IPhinixExtension
    void Register(IExtensionBuilder builder);
}
```

- `ExtensionId`: Globally unique identifier. Recommended format `author.modname` (e.g., `"myname.myfeature"`).
- `Register()`: Called after the extension is discovered. Core responsibility is registering handlers, APIs, capabilities, etc. For whether you can obtain host services during this phase, see [the note at the beginning of §8](#8-common-services-provided-by-the-host).

### 3.2 Optional Interface: `IActivatablePhinixExtensionModule`

If your submod needs to perform initialization after the host is ready, implement this interface (defined at [FrameworkTypes.cs:61-66](Common/Utils/Framework/FrameworkTypes.cs#L61-L66)):

```csharp
public interface IActivatablePhinixExtensionModule : IPhinixExtension
{
    void Activate(ExtensionHostContext hostContext);
    void Shutdown(ExtensionHostContext hostContext);
}
```

- `Activate()`: Obtain required services from `hostContext`, subscribe to events, start working.
- `Shutdown()`: Unsubscribe from events, release resources. **You must** `-=` every `+=` from `Activate()` here.

> **Note**: `IPhinixExtensionModule` and `IActivatablePhinixExtensionModule` are **independent interfaces** — neither inherits from the other. Your module must implement both to get the full lifecycle. See the official Chat extension: [BuiltInChatClientExtension.cs:14](Extensions/Chat/Client/BuiltInChatClientExtension.cs#L14) implements both interfaces.

### 3.3 The `[PhinixExtension]` Attribute and Dependency Declaration

Your module class must be marked with `[PhinixExtension("your.id")]`, otherwise the framework's reflection scan will not find you (unless your class implements `IPhinixExtension` and is also marked non-abstract, in which case the old legacy auto-discovery path will still pick it up, but the framework will emit a warning advising you to migrate to `IPhinixExtensionModule`).

The attribute is defined in [FrameworkTypes.cs:495-509](Common/Utils/Framework/FrameworkTypes.cs#L495-L509), and supports explicitly declaring inter-extension dependencies:

```csharp
[PhinixExtension("mymod.myfeature", DependsOn = new[] { "phinix.chat", "phinix.trade" })]
public class MyExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public string ExtensionId => "mymod.myfeature";
    // ...
}
```

- `ExtensionId`: Globally unique module identifier (e.g., `"mymod.myfeature"`).
- `DependsOn`: Optional array of strings declaring other extension IDs this module depends on. The framework constructs a Directed Acyclic Graph (DAG, see [ExtensionDependencyGraph.cs](Common/Utils/Framework/ExtensionDependencyGraph.cs)) for topological sorting, strictly ensuring dependencies complete `Register` and `Activate` before dependents, and are executed in reverse order during `Shutdown`.
- **Circular Dependency Protection**: If circular or mutually recursive dependencies are detected, DAG topological resolution fails with an error, marking involved modules as `Failed` and skipping activation to avoid deadlocks.

### 3.4 Full Lifecycle and Activation Policy

The framework manages extensions across four phases (see the `DiscoverExtensions`, `ActivateExtensions`, and `ShutdownExtensions` methods in [PhinixExtensionRegistry.cs](Common/Utils/Framework/PhinixExtensionRegistry.cs)):

```
1. Discover  ── Reflection scans candidate assemblies to locate [PhinixExtension] classes
                 ↓
                 Consults IExtensionActivationPolicy to determine enablement
                 ├─ User explicitly disabled ──→ Status set to Disabled (skips subsequent phases)
                 └─ Dependency disabled     ──→ Status set to DependencyDisabled (skips subsequent phases)
                 ↓
2. Register  ── Calls Register(builder) for enabled modules in DAG topological order
                 Modules register handlers, APIs, codecs, capabilities
                 After completion, status becomes Registered
                 ↓
3. Activate  ── Once host subsystems are ready, calls Activate(hostContext) in topological order
                 Modules obtain host services, resolve APIs, subscribe to events
                 After completion, status becomes Active
                 ↓
4. Shutdown  ── When host shuts down or resets, calls Shutdown(hostContext) in reverse order
                 Modules unsubscribe from events and release resources/handles
                 After completion, status becomes Shutdown
```

- **Full Lifecycle State Model** (defined in [FrameworkTypes.cs:51-61](Common/Utils/Framework/FrameworkTypes.cs#L51-L61) `ExtensionModuleState` enum):
  - `Discovered`: Reflection discovered module class, awaiting instantiation and policy check.
  - `Registered`: Successfully instantiated and executed `Register(builder)`.
  - `Active`: Successfully executed `Activate(hostContext)`, running normally.
  - `Failed`: Uncaught exception occurred during instantiation, `Register`, `Activate`, or `Shutdown`, or failed due to dependency cycles/missing dependencies.
  - `Shutdown`: Safely unregistered and released resources.
  - `Disabled`: Explicitly disabled by the user in settings, skipping `Register`.
  - `DependencyDisabled`: Enabled itself, but a parent extension it depends on was disabled, cascading to skip.

- **Host Built-in Management and Observability**:
  - The client host provides a built-in `ExtensionManagerTab` (and an "Extension Management" settings panel, Order=50).
  - Players and developers can inspect the live status, version, originating assembly path, and RimWorld Mod package ID of all extensions, and toggle individual extensions on/off.
  - The host maintains a 300-entry in-memory circular log buffer (with `ExtensionLogVersion` cache invalidation), allowing diagnostic logs to be reviewed directly in this UI.

### 3.5 Error Isolation

Failure of a single module's `Register()`, `Activate()`, or `Shutdown()` does **not** affect other modules:

- `Register()` exceptions are caught, status marked as `Failed`, warning logged
- `Activate()` exceptions are caught, status marked as `Failed`, warning logged
- `Shutdown()` exceptions are likewise isolated

This means **your submod will not bring down the entire framework** — but conversely, the framework will not automatically retry your failed module.

---

## 4. Registry: What IExtensionBuilder Can Do

`Register(IExtensionBuilder builder)` is your core entry point for interacting with the framework. `builder` provides the following capabilities (full interface definition at [FrameworkTypes.cs:129-186](Common/Utils/Framework/FrameworkTypes.cs#L129-L186)):

### 4.1 Registering Handlers (Hooking into Communication Pipelines)

```csharp
// 1. Message pipeline (display messages)
builder.AddClientMessageHandler(this);                  // IClientMessageHandler (inbound + outbound handling)
builder.AddMessageInterceptor(this);                    // IMessageInterceptor (pre-display intercept/modify)
builder.AddMessageRenderer(this);                       // IMessageRenderer (custom message rendering/conversion)

// 2. Command pipeline (control instructions)
builder.AddClientCommandHandler(this);                  // IClientCommandHandler (inbound handling)
// If implementing both IClientCommandHandler and IClientOutgoingCommandHandler,
// AddClientCommandHandler(this) registers both; see §6.2 for outbound details.

// 3. Item pipeline (binary/item payloads, ✅ fully available)
builder.AddItemCodec(this);                             // IItemCodec (codec consumed by Item pipeline)
builder.AddClientItemHandler(this);                     // IClientIncomingItemHandler (client inbound handling)
builder.AddClientOutgoingItemHandler(this);             // IClientOutgoingItemHandler (client outbound handling)

// 4. Other general capability declarations
builder.AddCapabilityProvider(this);                    // ICapabilityProvider (declares supported capabilities)

// 5. Server-side extension roles (for server-side submods)
builder.AddServerMessageHandler(this);                  // IServerMessageHandler
builder.AddServerInboundMessageInterceptor(this);       // IServerInboundMessageInterceptor
builder.AddServerDefaultMessageHandler(this);           // IServerDefaultMessageHandler
builder.AddServerMessageObserver(this);                 // IServerMessageObserver
builder.AddServerCommandHandler(this);                  // IServerCommandHandler
builder.AddServerInboundCommandInterceptor(this);       // IServerInboundCommandInterceptor
builder.AddServerDefaultCommandHandler(this);           // IServerDefaultCommandHandler
builder.AddServerCommandObserver(this);                 // IServerCommandObserver
builder.AddServerItemHandler(this);                     // IServerItemHandler
builder.AddServerInboundItemInterceptor(this);          // IServerInboundItemInterceptor
builder.AddServerDefaultItemHandler(this);              // IServerDefaultItemHandler
builder.AddServerItemObserver(this);                    // IServerItemObserver
builder.AddServerOutboundPacketInterceptor(this);       // IServerOutboundPacketInterceptor
builder.AddConsoleCommandProvider(this);                // IServerConsoleCommandProvider (server console commands)
```

### 4.2 Registering APIs (Exposing Your Own Capabilities)

```csharp
builder.RegisterApi<IMyService>(this);           // Register as IMyService type
builder.RegisterApi<IMainTabProvider>(myTab);    // Register UI contribution
```

### 4.3 Resolving Other Plugins' APIs

```csharp
// Get a single API (if multiple providers exist, returns the first registered)
builder.TryResolveApi<ITradeRequestApi>(out var tradeApi);

// Get all providers
IReadOnlyList<IChatUiHostContext> contexts = builder.ResolveApis<IChatUiHostContext>();
```

### 4.4 Reading ExtensionId and HostContext

```csharp
string myId = builder.ExtensionId;               // Your own ExtensionId
ExtensionHostContext hostCtx = builder.HostContext; // Host context
```

---

## 5. API Exposure and Resolution

### 5.1 RegisterApi\<T\>: Exposing Your Own Capabilities

Call `builder.RegisterApi<T>(implementation)` in `Register()`, and your implementation enters the framework's API registry:

```csharp
public void Register(IExtensionBuilder builder)
{
    var myFeature = new MyFeatureService(/* ... */);
    builder.RegisterApi<IMyFeatureApi>(myFeature);
    builder.RegisterApi<IMainTabProvider>(myFeature); // Also provide a Tab for UI
}
```

Framework internal implementation code: [FrameworkTypes.cs:150-255](Common/Utils/Framework/FrameworkTypes.cs#L150-L255) (`ExtensionApiRegistry` class).

### 5.2 TryResolveApi\<T\> / ResolveApis\<T\>: Discovering Others' Capabilities

- `TryResolveApi<T>()`: Returns the first matching API implementation. Suitable for "only need one implementation" scenarios.
- `ResolveApis<T>()`: Returns a list of all registered API implementations of type `T`. Suitable for "collect all contributors" scenarios (e.g., the host collecting all `IMainTabProvider`).

```csharp
// In Register()
if (builder.TryResolveApi<ITradeRequestApi>(out var tradeApi))
{
    // Trade plugin is registered, can initiate trades
    _tradeApi = tradeApi;
}

// When the host collects all Tabs
IReadOnlyList<IMainTabProvider> tabs = builder.ResolveApis<IMainTabProvider>();
```

**Resolution order**: The API registry is sequential — first registered, first returned (for `TryResolve`). If the same interface has multiple providers, `TryResolve` returns the first, `ResolveAll` returns all (in registration order).

### 5.3 Resolving in Activate

The API registry is fully populated after all modules' `Register()` calls complete, so you can also resolve APIs in `Activate()` via `hostContext`:

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    if (hostContext.TryResolveApi<ITradeRequestApi>(out var tradeApi))
    {
        _tradeApi = tradeApi;
    }
}
```

### 5.4 Comparison with Direct Contracts Assembly References

| Approach | Pros | Cons |
|------|------|------|
| `RegisterApi` + `TryResolveApi` | Loose coupling, no dependency on the other party's assembly | Requires consistent interface definitions; runtime discovery |
| Direct Contracts project reference | Compile-time safety; no `TryResolve` null checks needed | Adds a compile dependency; the other party's DLL must exist |

**Recommendation**: If the other party provides a Contracts project (as both Chat and Trade do), **directly reference the Contracts project**. The API registry approach is better suited for scenarios where "the other party does not provide a Contracts assembly" or "you only need a weak dependency (the other party may not be present)."

---

## 6. The Three Communication Pipelines

The framework defines three communication pipelines. **The current availability of each pipeline differs** — please read this section carefully.

### 6.1 Message Pipeline ✅ Fully Available

**Responsibility**: Transmits "things users should see" (chat messages, system notifications, etc.).

**Inbound** (Server → Client):

```
FrameworkPacket (Kind="message")
  → packetHandler branches to KindMessage
  → IClientMessageHandler chain (sorted by Priority)
  → CanHandleIncomingMessage(message) → HandleIncomingMessage(message, context)
  → IMessageRenderer → FrameworkDisplayMessage → UI
```

**Outbound** (Client → Server):

```
User enters text
  → IFrameworkClientTransport.TryHandleOutgoingMessage(rawMessage)
  → IClientMessageHandler chain (sorted by Priority)
  → CanHandleOutgoingText(rawMessage) → HandleOutgoingText(rawMessage, context)
  → Returns FrameworkPacket → framework sends
```

**Interfaces you need to implement**:

```csharp
public interface IClientMessageHandler : IMessageHandler
{
    int Priority { get; }                                    // Smaller values execute first
    bool CanHandleOutgoingText(string rawMessage);           // Outbound filter
    ClientOutgoingMessageResult HandleOutgoingText(          // Outbound handling
        string rawMessage, ClientFrameworkContext context);
    bool CanHandleIncomingMessage(FrameworkPacket message);  // Inbound filter
    ClientIncomingMessageResult HandleIncomingMessage(       // Inbound handling
        FrameworkPacket message, ClientFrameworkContext context);
}
```

**Supporting roles**:

| Interface | When executed | Purpose |
|------|----------|------|
| `IMessageInterceptor` | After message is rendered as `FrameworkDisplayMessage`, before display | Filter/modify display messages |
| `IMessageRenderer` | `FrameworkPacket` → `FrameworkDisplayMessage` conversion | Custom message rendering |

**Registration**:

```csharp
builder.AddClientMessageHandler(this);
builder.AddMessageInterceptor(this);
builder.AddMessageRenderer(this);
```

### 6.2 Command Pipeline ✅ Fully Available

**Responsibility**: Transmits "operations the system should execute" (Trade state sync, history requests, etc.). The key difference between Command and Message is: Command does not produce display artifacts; it modifies internal state, which may indirectly trigger subsequent Messages.

**Inbound** (Server → Client):

```
FrameworkPacket (Kind="command")
  → packetHandler branches to KindCommand
  → IClientCommandHandler chain (sorted by Priority)
  → CanHandleIncomingCommand(command) → HandleIncomingCommand(command, context)
```

**Outbound** (Client → Server):

```
Plugin constructs FrameworkPacket
  → IFrameworkClientCommandTransport.TryHandleOutgoingCommand(command)
  → IClientOutgoingCommandHandler chain (sorted by Priority)
  → CanHandleOutgoingCommand(command) → HandleOutgoingCommand(command, context)
  → Returns FrameworkPacket → framework sends
```

**Interfaces you need to implement**:

Inbound handling:
```csharp
public interface IClientCommandHandler : ICommandHandler
{
    int Priority { get; }
    bool CanHandleIncomingCommand(FrameworkPacket command);
    ClientIncomingCommandResult HandleIncomingCommand(
        FrameworkPacket command, ClientFrameworkContext context);
}
```

Outbound handling (interface defined at [FrameworkTypes.cs:562-572](Common/Utils/Framework/FrameworkTypes.cs#L562-L572)):
```csharp
public interface IClientOutgoingCommandHandler : ICommandHandler
{
    bool CanHandleOutgoingCommand(FrameworkPacket command);
    ClientOutgoingCommandResult HandleOutgoingCommand(
        FrameworkPacket command, ClientFrameworkContext context);
}
```

**Registration**:

```csharp
builder.AddClientCommandHandler(this); // Covers both inbound and outbound (if the class implements both interfaces)
```

Reference implementation: [BuiltInTradeClientExtension.cs:60](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L60) implements both `IClientCommandHandler` and `IClientOutgoingCommandHandler`.

### 6.3 Item Pipeline ✅ Fully Available (P0 Complete)

**Current actual state** (verified against `dev` branch code, 2026-06-21 update):

The Item pipeline is **now fully independent and usable**. Here are the facts:

- ✅ The server-side three-phase chain is in place: `IServerItemInterceptor` → `IServerDefaultItemHandler` → `IServerItemObserver`
- ✅ The client-side `packetHandler` has a `KindItem` branch — Item data routes independently
- ✅ `IClientIncomingItemHandler` / `IClientOutgoingItemHandler` interfaces exist ([FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs))
- ✅ The `IItemCodec` interface is defined ([FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs))
- ✅ The `builder.AddItemCodec()` registration method is fully available and codecs are directly consumed by the pipeline
- ✅ `IFrameworkClientTransport.TryHandleOutgoingItem()` exists for unified outbound Item packet routing
- ✅ The current Command-nesting path (used by Trade) remains fully compatible

**Inbound routing** (Server → Client):

```
NetClient → packetHandler(KindItem) → handleItem(packet)
  → IClientIncomingItemHandler chain (sorted by Priority)
  → CanHandle(packet) → HandleItem(packet, context)
```

**Outbound routing** (Client → Server):

```
Submod → IFrameworkClientTransport.TryHandleOutgoingItem(itemPayload)
  → IClientOutgoingItemHandler chain (sorted by Priority)
  → HandleOutgoingItem() → FrameworkPacket
  → sendPacket(Flow=Item, Kind=item, PayloadBytes=protobuf)
```

**Server-side three-phase chain**:

```
handleItem → ProcessIncomingItem:
  1. IServerItemInterceptor.CanIntercept → InterceptItem()
  2. IServerDefaultItemHandler.CanHandle → HandleItem()
     (traverses IItemCodecs by CodecId, CanDecode → Decode)
  3. IServerItemObserver.ObserveItem()
```

**Currently available Item transmission methods**:

| Method | Description | Best for |
|--------|-------------|----------|
| Independent `KindItem` packet | Direct Item routing through the Item pipeline | New submods, binary payload data (items, creatures, etc.) |
| Command-nesting `FrameworkItemPayload` | Item data nested inside Command pipeline | Trade backward compatibility, mixed control+payload messages |

**Recommendations for submod developers**:

| Your scenario | Recommended approach |
|----------|------------------|
| Transmitting a small amount of structured control instructions | Command pipeline — standard, fully available |
| Transmitting binary-payload data (items, creatures, etc.) | Item pipeline — implement `IItemCodec` + `IClientOutgoingItemHandler` |
| Server-side needs to validate/intercept Item data | Item pipeline — register `IServerItemInterceptor` / `IServerItemObserver` |
| Want one codec to be automatically routed by the pipeline | Item pipeline — register `IItemCodec` via `AddItemCodec()` |
| Need to stay compatible with existing Trade protocol | Command-nesting `FrameworkItemPayload` (Trade's current approach) |

**Future evolution** 🔮:
- **P1**: `PayloadBytes` direct path to eliminate intermediate JSON serialization and base64 expansion (performance optimization)
- **P2**: Migrate Trade's Item payload from Command nesting to independent `KindItem` routing (optional, the current nesting path will remain compatible)

For detailed analysis, see `docs/branch-local/dev/三条Pipeline职责辨析与Item管线补全分析.md` and `docs/branch-local/dev/Item管线补全实施方案.md`.

### 6.4 Inbound / Outbound Flow Reference Table

| Direction | Message | Command | Item |
|------|---------|---------|------|
| Server → Client (inbound) | `IClientMessageHandler` → `IMessageRenderer` → UI | `IClientCommandHandler` → internal state | `IClientIncomingItemHandler` → internal state |
| Client → Server (outbound) | `TryHandleOutgoingMessage()` → `IClientMessageHandler` chain | `TryHandleOutgoingCommand()` → `IClientOutgoingCommandHandler` chain | `TryHandleOutgoingItem()` → `IClientOutgoingItemHandler` chain |
| Outbound pipeline entry | `IFrameworkClientTransport` | `IFrameworkClientCommandTransport` | `IFrameworkClientTransport` |
| Outbound must go through pipeline | ✅ Yes ([Design Philosophy §3.7]) | ✅ Yes | ✅ Yes |

---

## 7. Integrating with UI

### 7.1 Adding a Tab

Implement `IMainTabProvider` (defined in [IMainTabProvider.cs](Client/ClientExtensionAbstractions/UI/IMainTabProvider.cs)):

```csharp
public interface IMainTabProvider
{
    string TabLabel { get; }    // Tab label text
    float TabOrder { get; }     // Sort order, smaller values appear further left
    void Draw(Rect inRect);     // Draw tab content
}
```

Registration:

```csharp
builder.RegisterApi<IMainTabProvider>(this);
```

`TabOrder` reference values: Chat uses `0` ([ChatMainTabProvider.cs:22](Extensions/Chat/Client/ChatMainTabProvider.cs#L22)), Trade uses `1` ([TradeMainTabProvider.cs:15](Extensions/Trade/Client/TradeMainTabProvider.cs#L15)). Your submod can choose a value to place it where you want (e.g., `0.5` between the two, or `2` after Trade).

### 7.2 Adding a Sidebar

Implement `IServerSidebarProvider` (defined in [IServerSidebarProvider.cs](Client/ClientExtensionAbstractions/UI/IServerSidebarProvider.cs)):

```csharp
public interface IServerSidebarProvider
{
    float Order { get; }           // Sort order, smaller values appear higher
    float PreferredWidth { get; }  // Suggested width (pixels)
    void Draw(Rect inRect);        // Draw sidebar content
}
```

Registration same as above: `builder.RegisterApi<IServerSidebarProvider>(this)`.

### 7.3 Responsive Layout and Adaptation (ClientExtensionAbstractions 1.1)

Full UI adaptability (Phase 9) enables Phinix to run smoothly and stably across a wide spectrum of screen resolutions (from 1024×768 up to 4K), different RimWorld UI scale factors, and multi-language long-text scenarios. The framework maintains complete binary and source backward compatibility with legacy `IMainTabProvider` and `IServerSidebarProvider` implementations while providing full adaptive layout support via optional interfaces and low-allocation geometric primitives.

#### 7.3.1 Responsive Layout Hints and Fallback Mechanisms

Implementations that want to declare content-size preferences can have the same provider instance implement `IResponsiveMainTabProvider` or `IResponsiveSidebarProvider` (defined in the [UI abstraction layer](Client/ClientExtensionAbstractions/UI/)). Do not register the optional interface separately:

```csharp
public sealed class MyTab : IMainTabProvider, IResponsiveMainTabProvider
{
    // Cache Hints in a static field to avoid repeated allocations in Draw or property getters
    private static readonly UiLayoutHints Hints = new UiLayoutHints(
        minimumContentSize: new Vector2(480f, 320f),
        preferredContentSize: new Vector2(760f, 560f),
        supportsCompactLayout: true);

    public UiLayoutHints LayoutHints => Hints;

    // Other IMainTabProvider members...
}

public sealed class MySidebar : IServerSidebarProvider, IResponsiveSidebarProvider
{
    public float MinimumWidth => 160f;
    public bool CanCollapse => true;

    // Other IServerSidebarProvider members...
}
```

- **`MinimumContentSize`**: The lowest suggested content size under normal layout. This is not a mandatory window minimum—when the player runs at very low resolution or shrinks the window, the `inRect` provided by the Host may still be smaller.
- **`PreferredContentSize`**: The recommended size used only for initial window sizing upon first opening or resetting; it never forces the window beyond the screen safe area.
- **`SupportsCompactLayout`**: Indicates whether this Tab provides an explicit compact layout branch (such as collapsing two columns into a single column with sub-tabs) when space falls below `MinimumContentSize`.
- **`MinimumWidth`**: The minimum practical width threshold for the sidebar.
- **`CanCollapse`**: Indicates whether this sidebar allows the Host to collapse it when window space is constrained.
- **Default Fallback Behavior**: Legacy third-party provider classes that do not implement the optional interfaces automatically receive conservative defaults (`UiLayoutHints.Default` with 480×320 min, 700×560 pref, compact = false; sidebars use `PreferredWidth` and `CanCollapse = false`). Old submods continue to load and render safely without requiring code changes.

#### 7.3.2 Host Sidebar Drawer Collapse Mechanism

In the host's main window `ServerTab`, the main content area has a protected minimum width `MAIN_MIN_WIDTH = 480f`.
- When the user drags and shrinks the window such that available width cannot simultaneously accommodate the main content area and the sidebar:
  - If all active sidebar providers implement `IResponsiveSidebarProvider` with `CanCollapse == true`, the Host automatically collapses the sidebar and enters collapsed mode.
  - In collapsed mode, a hamburger drawer button (`☰`) automatically appears in the upper-right corner of the main content area.
  - Clicking `☰` displays a floating drawer overlay covering the sidebar area, allowing players to view the online user directory or notifications. Clicking `☰` again or clicking outside the drawer dismisses it.
- Submod developers **do not need** to build custom collapse buttons or drawer toggle logic; simply implementing `IResponsiveSidebarProvider` with `CanCollapse = true` provides this feature automatically.

#### 7.3.3 Screen Safe Area and Custom Dialogs (UiScreenSafeArea)

If your Submod creates a custom standalone dialog window (inheriting from RimWorld's `Window`, such as a trade window, pawn details window, or red packet popup), **never hardcode absolute screen coordinates or fixed full-screen dimensions**.

Always use `UiScreenSafeArea.ClampWindow` to constrain the window Rect to the current screen safe area:

```csharp
public class MyCustomDialog : Window
{
    private static readonly Vector2 MinimumDialogSize = new Vector2(400f, 300f);

    public override Vector2 InitialSize => new Vector2(600f, 450f);

    public override void PreOpen()
    {
        base.PreOpen();
        // Clamp window size and position to screen safe area, preventing off-screen overflow at low resolutions or high UI scale
        windowRect = UiScreenSafeArea.ClampWindow(windowRect, MinimumDialogSize);
    }

    public override void DoWindowContents(Rect inRect)
    {
        // Draw window contents...
    }
}
```

- `UiScreenSafeArea.Current`: Returns `(0, 0, UI.screenWidth, UI.screenHeight)`, strictly observing RimWorld's UI coordinate space.
- `UiScreenSafeArea.Normalize(Rect rect)`: Normalizes inverted or negative width/height Rects into standard positive bounding boxes, preventing Unity IMGUI clipping crashes.

#### 7.3.4 Allocation-Conscious Shared Layout Primitives (ClientExtensionAbstractions.UI)

To prevent submods from writing fragile layout logic, `ClientExtensionAbstractions` provides 4 high-efficiency, zero-allocation pure geometry helper classes:

##### 1. `ResponsiveSplitLayout` (Two-Pane Splitter)
Automatically toggles between horizontal two-pane (`Horizontal`), vertical two-pane (`Vertical`), and single-pane (`SinglePane`) modes based on container bounds:

```csharp
ResponsiveSplitResult split = ResponsiveSplitLayout.Calculate(
    container: inRect,
    firstMinimum: new Vector2(300f, 200f),
    secondMinimum: new Vector2(240f, 200f),
    firstPreferred: new Vector2(400f, 300f),
    spacing: 10f,
    showFirstInSinglePane: _activeSubTab == 0);

if (split.Mode == ResponsiveSplitMode.SinglePane)
{
    // Space is heavily constrained; render sub-tab toggle buttons to switch between Pane 1 and Pane 2
}

// Result contains FirstRect, SecondRect, and DividerRect
DrawLeftPane(split.FirstRect);
DrawRightPane(split.SecondRect);
```

##### 2. `ResponsiveToolbarLayout` (Toolbar with FloatMenu Overflow)
Arranges action buttons in priority order. When row limits (`maximumRows`) are reached, remaining lower-priority actions automatically overflow into a trailing `⋯` button that opens a `FloatMenu`:

```csharp
// Pre-allocate desiredWidths and actionRects arrays as fields to avoid per-frame allocations in Draw
private static readonly float[] ActionWidths = new float[] { 100f, 90f, 80f, 80f };
private readonly Rect[] _actionRects = new Rect[4];

// In Draw method:
ResponsiveToolbarResult toolbar = ResponsiveToolbarLayout.Calculate(
    container: toolbarRect,
    desiredWidths: ActionWidths,
    actionCount: 4,
    primaryActionCount: 1,      // Ensure at least 1 primary core action remains visible
    rowHeight: 30f,
    spacing: 6f,
    maximumRows: 1,             // Maximum rows allowed
    overflowButtonWidth: 32f,   // Overflow button width
    actionRects: _actionRects);

for (int i = 0; i < toolbar.VisibleActionCount; i++)
{
    if (Widgets.ButtonText(_actionRects[i], _actions[i].Label))
    {
        _actions[i].Execute();
    }
}

if (toolbar.HasOverflow && Widgets.ButtonText(toolbar.OverflowButtonRect, "⋯"))
{
    var options = new List<FloatMenuOption>();
    for (int i = toolbar.VisibleActionCount; i < 4; i++)
    {
        int actionIndex = i;
        options.Add(new FloatMenuOption(_actions[actionIndex].Label, () => _actions[actionIndex].Execute()));
    }
    Find.WindowStack.Add(new FloatMenu(options));
}
```

##### 3. `ResponsiveFormLayout` (Responsive Form Row)
Handles form rows consisting of "Label + Input field + Optional Action button + Optional Error message". Automatically operates in `Inline` mode when width permits, or stacks vertically into `Stacked` mode (label on top, input and action on the second line, error message below) when space is narrow:

```csharp
ResponsiveFormResult form = ResponsiveFormLayout.Calculate(
    container: formRowRect,
    labelWidth: 100f,
    minimumInputWidth: 140f,
    actionWidth: 80f,
    rowHeight: 30f,
    spacing: 8f,
    errorHeight: string.IsNullOrEmpty(_errorText) ? 0f : 22f);

Widgets.Label(form.LabelRect, "Server Address");
_serverAddress = Widgets.TextField(form.InputRect, _serverAddress);
if (Widgets.ButtonText(form.ActionRect, "Connect"))
{
    Connect();
}
if (!string.IsNullOrEmpty(_errorText))
{
    GUI.color = Color.red;
    Widgets.Label(form.ErrorRect, _errorText);
    GUI.color = Color.white;
}
```

##### 4. `VirtualListLayout` (Large List Virtual Scrolling)
When a list contains hundreds or thousands of elements (chat logs, market shelves, player rosters, debug logs), rendering all rows every frame will crash performance. `VirtualListLayout` uses binary search to compute visible row indices:

- **Fixed-Height Lists**:
  ```csharp
  Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect);
  // overscan: buffer 1-2 extra rows above and below for smooth scrolling
  VirtualListRange range = VirtualListLayout.GetFixedRange(
      itemCount: items.Count,
      rowHeight: 32f,
      scrollY: _scrollPosition.y,
      viewportHeight: outRect.height,
      overscan: 1);

  for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
  {
      Rect rowRect = new Rect(0f, i * 32f, viewRect.width, 30f);
      DrawItemRow(rowRect, items[i]);
  }
  Widgets.EndScrollView();
  ```

- **Dynamic-Height Lists (Cached Row Heights)**:
  For lists with variable line heights (such as multi-line chat messages), maintain a cumulative prefix offset array `prefixOffsets` (length `itemCount + 1`, where `prefixOffsets[0] = 0` and `prefixOffsets[i]` is the cumulative height of the first `i` rows). Use `VirtualListLayout.GetDynamicRange(prefixOffsets, itemCount, _scrollPosition.y, outRect.height, overscan: 1)` to achieve identical zero-allocation, instantaneous visible rendering.

#### 7.3.5 Geometry Calculation and Cache Invalidation Principles

- `ResponsiveSplitLayout`, `ResponsiveToolbarLayout`, `ResponsiveFormLayout`, and `VirtualListLayout` are **stateless pure mathematical functions** with no internal caches.
- Callers must store computed Rects or layout results and **only** invalidate/recompute them when:
  - Host container Rect dimensions change (`inRect.size` changes)
  - Data item count changes or items are modified
  - Active language changes (`LanguageDatabase.activeLanguage`)
  - Relevant configuration settings change
- **Never** allocate temporary arrays (such as `new float[]`) or call `Text.CalcHeight` / LINQ on every frame in the `Draw` path! See [§11.4](#114-object-allocation-on-draw-paths) for details.

### 7.4 Adding a Badge

Implement `IBadgeProvider` (defined in [IBadgeProvider.cs](Client/ClientExtensionAbstractions/UI/IBadgeProvider.cs)):

```csharp
public interface IBadgeProvider
{
    string BadgeText { get; }  // Badge text displayed on the Tab button
}
```

- Return `null` or empty string to indicate no badge.
- **Performance warning**: `BadgeText` is a property getter, called on every UI refresh. Do not do computation in the getter — use a cached field, updating it when data changes. See [§11.4](#114-object-allocation-on-draw-paths) for details.

Registration same as above: `builder.RegisterApi<IBadgeProvider>(this)`.

### 7.5 Adding a Settings Panel

Implement `IClientSettingsPanelProvider` (defined at [IClientExtensionAbstractions.cs:182-195](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L182-L195)):

```csharp
public interface IClientSettingsPanelProvider
{
    string SectionId { get; }    // Group identifier, recommended "plugin.category"
    float Order { get; }         // Display order. Host core settings at 0-100, plugin settings at 100+
    void DrawSettings(Listing_Standard listing, IClientSettingsContext settings);
    bool IsVisible(IClientSettingsContext settings);
}
```

Registration: `builder.RegisterApi<IClientSettingsPanelProvider>(this)`.

For complete examples, see Chat's implementation: [ChatSettingsPanelProvider.cs](Extensions/Chat/Client/ChatSettingsPanelProvider.cs) and Trade's implementation: [TradeSettingsPanelProvider.cs](Extensions/Trade/Client/TradeSettingsPanelProvider.cs).

### 7.6 Settings Migration (Legacy Settings)

If your submod needs to migrate settings from old Phinix flat keys to new namespaced keys, also implement `IClientLegacySettingsMigrator` (defined at [IClientExtensionAbstractions.cs:132-135](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L132-L135)):

```csharp
public interface IClientLegacySettingsMigrator
{
    bool TryMigrateLegacySettings(IClientSettingsContext settings,
        IReadOnlyDictionary<string, string> legacyValues);
}
```

Registration: `builder.RegisterApi<IClientLegacySettingsMigrator>(this)`.

The host calls all registered migrators when the settings window is first opened. Reference: [ChatSettingsPanelProvider.cs:53-67](Extensions/Chat/Client/ChatSettingsPanelProvider.cs#L53-L67).

### 7.7 Pushing Display Messages

If your submod needs to inject notifications into the message queue (not messages coming from the server via the Message pipeline, but locally generated notifications), use `IDisplayMessageSink` (defined at [IClientExtensionAbstractions.cs:178-182](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L178-L182)):

```csharp
public interface IDisplayMessageSink
{
    void Enqueue(FrameworkDisplayMessage message);
}
```

This service is obtained in `Activate()` via `hostContext.GetRequiredService<IDisplayMessageSink>()`.

### 7.8 Adding Notice Banners (INoticeBannerProvider)

The host reserves a notice banner area at the top of the main window (`ServerTab`). Implement `INoticeBannerProvider` (defined in [INoticeBannerProvider.cs](Client/ClientExtensionAbstractions/UI/INoticeBannerProvider.cs)):

```csharp
public interface INoticeBannerProvider
{
    float CurrentHeight { get; }  // Desired banner height (0 means not shown)
    void Draw(Rect inRect);       // Draw banner contents
}
```

Registration:

```csharp
builder.RegisterApi<INoticeBannerProvider>(this);
```

- When `CurrentHeight > 0`, `ServerTab` dynamically partitions a rectangle of corresponding height at the top of the window and calls `Draw(inRect)`.
- Ideal for global disconnection alerts, update notifications, pending urgent user actions, or critical missing configuration warnings.

### 7.9 Enter Key Handling (IUiAcceptKeyHandler)

In RimWorld, pressing the Enter / KeypadEnter key often triggers window acceptance or closes the active window. If your tab or sidebar contains text inputs, search fields, or multi-line chat editors, and you want pressing Enter to send a message or confirm a search rather than closing the window, have your `IMainTabProvider` or `IServerSidebarProvider` implementation additionally implement `IUiAcceptKeyHandler` (defined in [IUiAcceptKeyHandler.cs](Client/ClientExtensionAbstractions/UI/IUiAcceptKeyHandler.cs)):

```csharp
public interface IUiAcceptKeyHandler
{
    bool WantsAcceptKey { get; }   // Whether the active control wants to intercept and consume Enter
    bool TryHandleAcceptKey();     // Executes accept logic; returns true if consumed
}
```

- No separate registration needed: as long as the currently active tab or sidebar instance implements this interface, `ServerTab` queries `WantsAcceptKey` when detecting Return key events; if `true`, it delegates to `TryHandleAcceptKey()` and blocks RimWorld's underlying window closure logic.

### 7.10 UI Theme and Palette (IUiTheme)

Phinix provides a centralized UI theme and color service `IUiTheme` (defined in [IUiTheme.cs](Client/ClientExtensionAbstractions/UI/IUiTheme.cs)), eliminating visual inconsistency and hardcoded color clashes:

```csharp
public interface IUiTheme
{
    Color PrimaryText { get; }      // Primary text color
    Color SecondaryText { get; }    // Secondary / subtle text color
    Color Background { get; }       // Container background color
    Color Surface { get; }          // Card / panel surface color
    Color Separator { get; }        // Divider line color
    Color HoverHighlight { get; }   // Hover highlight color
    Color Pending { get; }          // Pending state color
    Color Error { get; }            // Error / failure accent color
    Color Success { get; }          // Success / connected accent color
    Color Warning { get; }          // Warning accent color

    void RegisterColor(string key, Color defaultColor);
    Color GetColor(string key);
    bool TryGetColor(string key, out Color color);
    void RegisterFloat(string key, float defaultValue);
    float GetFloat(string key, float defaultValue = 0f);
    void Reload();
}
```

- **Obtaining**: Call `hostContext.GetRequiredService<IUiTheme>()` in `Activate()`, or resolve via `builder.TryResolveApi<IUiTheme>(out var theme)`.
- **Extending themes**: Submods can register extension-specific themeable colors by calling `theme.RegisterColor("mymod.accent", defaultColor)`.
- **Custom theme providers**: Third-party mods can also implement `IUiTheme` and expose it to the framework via `builder.RegisterApi<IUiTheme>(customTheme)`.

---

## 8. Common Services Provided by the Host

The following services are obtained in `Activate(ExtensionHostContext hostContext)` via `hostContext.GetRequiredService<T>()`.

> **Note on using services during the Register phase**: Currently, the host (Client.cs) injects all services into `ExtensionHostContext` and completes the injection before calling `DiscoverExtensions` → `Register`, so services are actually ready during the `Register()` phase. **Current official extensions (Chat/Trade) heavily use `builder.HostContext.GetRequiredService<T>()` in `Register()`.** The recommended practice is still to move host-service-dependent initialization to `Activate()` — only handler/API registration stays in `Register()`. Future versions will enforce this boundary.

### 8.1 IClientSessionContext

Provides the current session's authentication and login state. Defined at [IClientExtensionAbstractions.cs:67-75](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L67-L75):

```csharp
public interface IClientSessionContext
{
    bool Authenticated { get; }   // Whether authenticated
    bool LoggedIn { get; }        // Whether logged in
    string SessionId { get; }     // Current session ID
    string Uuid { get; }          // Current player's UUID
}
```

### 8.2 IClientSettingsContext

Read and write client settings. Defined at [IClientExtensionAbstractions.cs:78-93](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L78-L93):

```csharp
public interface IClientSettingsContext
{
    T Get<T>(string key, T defaultValue = default);
    void Set<T>(string key, T value);
    IEnumerable<string> BlockedUsers { get; }
    bool CollapseBlockedUsers { get; set; }
    void BlockUser(string uuid);
    void UnBlockUser(string uuid);
    event Action<string, object> OnSettingChanged;  // key and newValue
}
```

**Convention**: Use the `"plugin.category.settingName"` key format (e.g., `"chat.display.showNameFormatting"`) to avoid conflicts with the host or other plugins.

The `OnSettingChanged` event can be used to respond to setting changes in real time. Reference: [BuiltInTradeClientExtension.cs:129-136](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L129-L136).

### 8.3 IClientUserDirectory

Query online and known users:

```csharp
public interface IClientUserDirectory
{
    string Uuid { get; }                                  // Current user UUID
    ImmutableUser[] GetUsers(bool loggedIn = false);      // loggedIn=true for online users only
    bool TryGetUser(string uuid, out ImmutableUser user);
}
```

### 8.4 IClientUserEventStream

Subscribe to user-related events:

```csharp
public interface IClientUserEventStream
{
    event EventHandler Disconnected;                                    // Connection lost
    event EventHandler UsersChanged;                                    // User list changed
    event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;
    event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;
}
```

**Important**: Every `+=` in `Activate()` must have a corresponding `-=` in `Shutdown()`.

### 8.5 IClientMainThreadDispatcher

Marshal operations from network callback threads to the main (UI) thread:

```csharp
public interface IClientMainThreadDispatcher
{
    void Enqueue(Action action);
}
```

**Any code that manipulates UI or shared state, if it may be called on a network thread, must be marshaled through this interface.** Network callbacks (`OnNetworkReceive`, etc.) fire on the poll thread — directly modifying UI state causes race conditions and crashes.

### 8.6 IClientWindowService

Open host-level windows:

```csharp
public interface IClientWindowService
{
    void Open(Window window);
    void OpenSettingsWindow();
}
```

`OpenSettingsWindow()` opens the host settings window — all `IClientSettingsPanelProvider` drawings are aggregated in this window.

### 8.7 IClientSoundService

Play sound effects on the UI thread:

```csharp
public interface IClientSoundService
{
    void Enqueue(SoundDef soundDef);
}
```

Uses a queue pattern — this is not immediate playback; playback happens on the next frame's UI update. Reference: [BuiltInChatClientExtension.cs:120](Extensions/Chat/Client/BuiltInChatClientExtension.cs#L120).

### 8.8 IFrameworkClientTransport

Entry point for Message and Item pipelines (outbound). See [§6.1](#61-message-pipeline-fully-available) and [§6.3](#63-item-pipeline-fully-available-p0-complete):

```csharp
public interface IFrameworkClientTransport
{
    bool HasRemoteCapability(string capability);
    void SendFrameworkPacket(FrameworkPacket packet);          // ⚠️ Restricted: orthodox communication should use TryHandle
    bool TryHandleOutgoingMessage(string rawMessage);         // ✅ Recommended message outbound entry (routes via IClientMessageHandler chain)
    bool TryHandleOutgoingItem(FrameworkItemPayload itemPayload); // ✅ Recommended item outbound entry (routes via IClientOutgoingItemHandler chain)
}
```

> **About `SendFrameworkPacket`**: This method sends a FrameworkPacket directly without going through the handler pipeline. According to Design Philosophy §3.7, plugins should not bypass the pipeline — for orthodox communication, use `TryHandleOutgoingMessage` / `TryHandleOutgoingCommand` / `TryHandleOutgoingItem`.

### 8.9 IFrameworkClientCommandTransport

Entry point for the Command pipeline (outbound). See [§6.2](#62-command-pipeline-fully-available):

```csharp
public interface IFrameworkClientCommandTransport
{
    bool TryHandleOutgoingCommand(FrameworkPacket command);
}
```

### 8.10 IFrameworkClientLifecycle

Get the current compatibility mode and subscribe to mode switches:

```csharp
public interface IFrameworkClientLifecycle
{
    FrameworkCompatibilityMode CompatibilityMode { get; }
    event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged;
}
```

`FrameworkCompatibilityMode` enum values are `FrameworkV2` or `Legacy`. If your submod only works in V2 mode, check this value. If your submod needs to support Legacy mode, see [§10](#10-compatibility-mode-and-legacy) for details.

### 8.11 ILegacyModuleTransport

⚠️ **For Legacy adaptation use only.** New submods should not use this interface.

```csharp
public interface ILegacyModuleTransport
{
    void Send(string moduleName, byte[] data);
    void RegisterHandler(string moduleName, RawPacketHandlerDelegate handler);
    void UnregisterHandler(string moduleName);
}
```

This is the raw module communication capability that directly operates on `NetClient`. Orthodox communication for new submods should be done within the Message/Command pipelines.

### 8.12 IClientDisplayMessageFeed / IClientDisplayMessageStore

Message stream subscription and persistent storage:

```csharp
public interface IClientDisplayMessageFeed
{
    event EventHandler<FrameworkDisplayMessageEventArgs> DisplayMessageReceived;
}

public interface IClientDisplayMessageStore
{
    int UnreadMessages { get; }
    void MarkAsRead();
    FrameworkDisplayMessage[] GetUnreadDisplayMessages(bool markAsRead = true);
    FrameworkDisplayMessage[] GetDisplayMessages();
}
```

If you need to trigger notifications when new messages arrive (e.g., playing a sound), subscribe to `DisplayMessageReceived`. See the Chat extension's usage at [BuiltInChatClientExtension.cs:110-126](Extensions/Chat/Client/BuiltInChatClientExtension.cs#L110-L126).

### 8.13 IExtensionStorageProvider

Plugins can obtain a dedicated file storage path:

```csharp
hostContext.GetStoragePath("my.extension.id", "settings.json");
// Returns something like "framework-extensions/client/my.extension.id/settings.json"
```

Implementation code at [FrameworkTypes.cs:288-318](Common/Utils/Framework/FrameworkTypes.cs#L288-L318) (`FileSystemExtensionStorageProvider`).

### 8.14 Logging

Log via the `hostContext.Log` callback:

```csharp
hostContext.Log?.Invoke("Something happened", LogLevel.INFO);
```

- **Log Levels and Filtering Rules**:
  - Supported log levels: `DEBUG`, `INFO`, `WARNING`, `ERROR`.
  - **Release build filtering**: In Release builds, the client automatically filters out `DEBUG` level log entries (only forwarding `INFO`, `WARNING`, and `ERROR` to the RimWorld console and log files) to avoid performance degradation and disk flooding from high-frequency heartbeat or tracing logs; Debug builds output all levels. Submods should mark high-frequency diagnostic logs as `DEBUG`, and important lifecycle milestones as `INFO`.
  - **In-memory circular log buffer**: The host captures the last 300 log entries reported by all extensions in an in-memory ring buffer (tracked with `ExtensionLogVersion`), allowing players and developers to review and filter logs directly in `ExtensionManagerTab`.

> **Current convention**: Official extensions (Chat/Trade) use `hostContext.Log` (`Action<string, LogLevel>`) to report logs. The `ILoggable` interface is currently a log-producer contract used by host internal components (`NetClient`, `PhinixFrameworkClient`, etc.) and is not yet directly exposed to plugins.

### 8.15 IUiTheme

Unified UI theme and color palette service. Defined in [IUiTheme.cs](Client/ClientExtensionAbstractions/UI/IUiTheme.cs):

```csharp
IUiTheme theme = hostContext.GetRequiredService<IUiTheme>();
Color primaryColor = theme.PrimaryText;
```

See [§7.10 UI Theme and Palette](#710-ui-theme-and-palette-iuitheme) for details. The host registers it during startup as both a Host Service (`GetRequiredService<IUiTheme>()`) and a general API (`TryResolveApi<IUiTheme>()`).

### 8.16 IItemCodecProvider

Query service for all discovered and registered item codecs in the framework. Defined in [FrameworkTypes.cs:607-610](Common/Utils/Framework/FrameworkTypes.cs#L607-L610):

```csharp
public interface IItemCodecProvider
{
    IReadOnlyList<IItemCodec> ItemCodecs { get; }
}
```

- Used by plugins when processing composite payloads or resolving unknown `FrameworkItemPayload` entries by `CodecId` against registered codecs.
- The official Trade plugin uses this service to dynamically query and invoke item codecs registered by third-party extensions.

### 8.17 IExtensionActivationPolicy

Extension activation policy and disabled status query service. Defined in [FrameworkTypes.cs:517-532](Common/Utils/Framework/FrameworkTypes.cs#L517-L532):

```csharp
public interface IExtensionActivationPolicy
{
    bool ShouldActivate(string extensionId, out string reason);
    IReadOnlyCollection<string> DisabledExtensions { get; }
}
```

- Allows extensions at runtime to inspect whether specific peer extensions have been disabled by the user (`DisabledExtensions` collection), in order to trigger graceful degradation or adjust UI options.

---

## 9. Inter-Plugin Collaboration

### 9.1 Recommended Approach: Direct Contracts Assembly Reference

Chat and Trade both provide independent Contracts projects containing only interface definitions and protocol constants. You can reference them directly:

```csharp
// In your submod
using Phinix.TradeExtension;  // Reference TradeExtension Contracts assembly

public void Register(IExtensionBuilder builder)
{
    // Resolve in Activate
}

public void Activate(ExtensionHostContext hostContext)
{
    if (hostContext.TryResolveApi<ITradeRequestApi>(out var tradeApi))
    {
        // Call Trade's capabilities
        tradeApi.CreateTrade("some-player-uuid");
    }
}
```

**Why recommend direct references over pure API registry resolution?**
- Compile-time type safety — no need to maintain duplicate interface definitions
- Full IDE support (autocomplete, go-to-definition)
- The Contracts assembly only contains interfaces, not implementations, and does not violate the layering principle

### 9.2 API Registry Approach (Weak Dependency)

If your submod **optionally** needs another plugin's capabilities (the other party may not be installed), use the API registry:

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    if (hostContext.TryResolveApi<ITradeRequestApi>(out var tradeApi))
    {
        _tradeApi = tradeApi;  // Trade is present
    }
    // Trade not present — gracefully degrade
}
```

### 9.3 Do Not Route Through the Host

**Anti-pattern**:

```csharp
// ❌ Wrong: Asking the host to provide a dedicated bridge for your plugin
// This is not the host's responsibility. The host only provides common services.
public interface IMyPluginBridge { void DoSomething(); }
// Then expecting the host to inject it
```

**Correct approach**: Establish direct reference relationships between plugins. The framework only provides the API registry as a discovery mechanism — it does not act as a business intermediary.

### 9.4 Inter-Plugin Message Collaboration

If Plugin A wants to listen to Plugin B's messages:

- A references B's Contracts, knowing B's `MessageType` constants
- A registers its own `IClientMessageHandler` or `IClientCommandHandler` with a suitable Priority (intercept before B, or observe after B)
- Check `message.MessageType` in `CanHandleIncomingMessage`
- Handle in `HandleIncomingMessage`, return `Action = Continue` to let the pipeline continue

---

## 10. Compatibility Mode and Legacy

### 10.1 Two Compatibility Modes

Phinix can run in two modes:

| Mode | Value | Description |
|------|-----|------|
| `FrameworkV2` | 1 | New Framework protocol server — normal mode |
| `Legacy` | 2 | Old Phinix server — requires LegacyAdapter for protocol translation |

Get the current mode via `IFrameworkClientLifecycle.CompatibilityMode`.

### 10.2 How Legacy Adapter Works

`LegacyAdapter` runs at `Priority=500`, above Chat(1000) and Trade(1100). When `Legacy` mode is detected:
- It registers its own `ILegacyModuleTransport` handler
- Intercepts outbound Messages and Commands, translating them to the old protocol format
- Inbound old-protocol messages are converted to `FrameworkDisplayMessage` and injected into `IDisplayMessageSink`

Code at: [BuiltInLegacyAdapterClientExtension.cs](Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs).

### 10.3 Compatibility Advice for New Submods

**If your submod only supports FrameworkV2** (recommended):

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    _lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
    _lifecycle.CompatibilityModeChanged += OnModeChanged;

    if (_lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
    {
        StartWorking();
    }
}

private void OnModeChanged(object sender, FrameworkCompatibilityModeChangedEventArgs e)
{
    if (e.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
        StartWorking();
    else
        StopWorking();
}
```

**If you need to support Legacy mode**:
- Study `LegacyAdapter`'s approach
- Your outbound data needs to be translated through LegacyAdapter (it automatically intercepts handlers with Priority >= 500)
- Inbound data may need to be parsed from `IDisplayMessageSink` rather than obtained directly from `FrameworkPacket`

---

## 11. Common Anti-Patterns and Pitfalls

### 11.1 Bypassing the Pipeline to Directly Contact the Transport Layer

```csharp
// ❌ Wrong: directly sending FrameworkPacket
hostContext.GetRequiredService<IFrameworkClientTransport>()
    .SendFrameworkPacket(myPacket);
```

**Why it's wrong**: `SendFrameworkPacket` bypasses the handler pipeline — other plugins' interceptors, observers, translators all become ineffective. See Design Philosophy §3.7 for details.

```csharp
// ✅ Correct: go through the pipeline
hostContext.GetRequiredService<IFrameworkClientCommandTransport>()
    .TryHandleOutgoingCommand(myCommand);
```

### 11.2 Calling hostContext.GetRequiredService in Register()

```csharp
public void Register(IExtensionBuilder builder)
{
    // ❌ Wrong: host services may not be ready during Register phase
    var session = builder.HostContext.GetRequiredService<IClientSessionContext>();
}
```

**Correct approach**: `Register()` only does registration; initialization requiring host services goes in `Activate()`.

### 11.3 Forgetting to Unsubscribe Events in Shutdown()

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    _userEvents = hostContext.GetRequiredService<IClientUserEventStream>();
    _userEvents.UsersChanged += OnUsersChanged;  // += added
}

public void Shutdown(ExtensionHostContext hostContext)
{
    // ❌ Forgot -= ! Memory leak and ghost callbacks
}
```

**Rule**: Every `+=` in `Activate()` must have a corresponding `-=` in `Shutdown()`. See the standard pattern at [BuiltInTradeClientExtension.cs:157-180](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L157-L180).

### 11.4 Object Allocation on Draw Paths

RimWorld's IMGUI calls `DoWindowContents` / `Draw` / `DoButton` every frame. Allocating new objects (`new`) on these paths triggers GC, cumulatively leading to frame rate drops:

```csharp
// ❌ Wrong: new Regex, new GUIContent, new List every frame
public void Draw(Rect inRect)
{
    var regex = new Regex(@"<[^>]+>");        // Allocates every frame!
    var content = new GUIContent("hello");    // Allocates every frame!
    var items = messages.Where(m => m.IsNew).ToList(); // LINQ allocation!
}

// ✅ Correct: cache
private static readonly Regex TagRegex = new Regex(@"<[^>]+>",
    RegexOptions.Compiled);  // static readonly, compiled once
private GUIContent _cachedContent;
private bool _dirty = true;  // Dirty flag, recompute only when data changes
```

See Design Philosophy §8.3 for details.

### 11.5 Manipulating UI on Network Callback Threads

```csharp
// ❌ Wrong: directly manipulating UI in IClientCommandHandler.HandleIncomingCommand
// HandleIncomingCommand is called on the poll thread!
public ClientIncomingCommandResult HandleIncomingCommand(...)
{
    _myWindow.SomeState = newValue;  // Race condition!
}

// ✅ Correct: marshal to main thread
public ClientIncomingCommandResult HandleIncomingCommand(...)
{
    _dispatcher.Enqueue(() => _myWindow.SomeState = newValue);
}
```

### 11.6 Silently Swallowing Exceptions

```csharp
// ❌ Wrong
try { DoSomething(); } catch { }

// ❌ Still wrong: only logging Message, discarding stack trace
try { DoSomething(); } catch (Exception ex) { Log(ex.Message); }

// ✅ Correct: preserve stack trace, use framework logging
try { DoSomething(); } catch (Exception ex) {
    hostContext.Log?.Invoke($"DoSomething failed: {ex}", LogLevel.ERROR);
}
```

### 11.7 Not Implementing IDisposable

If your module holds resources that need releasing, such as `Timer`, `FileStream`, `Thread`:

```csharp
public sealed class MyExtension : IActivatablePhinixExtensionModule, IDisposable
{
    private Timer _timer;

    public void Activate(ExtensionHostContext ctx) { _timer = new Timer(...); }
    public void Shutdown(ExtensionHostContext ctx) { Dispose(); }
    public void Dispose() { _timer?.Dispose(); _timer = null; }
}
```

### 11.8 DLL Load Order Dependencies

RimWorld's `ModAssemblyHandler` loads DLLs in filename string order. If your `13-MySubmod.dll` depends on types in `08-ChatExtension.dll`, but your filename sorts before Chat's in string order — loading will fail.

**Rule**: Your numeric prefix must be larger than all your dependencies' numeric prefixes. See §12.7 for details.

### 11.9 Using Deprecated Legacy GUI Containers (Displayable Series)

The legacy `Displayable` flex container classes in the `PhinixClient.GUI` namespace (`HorizontalFlexContainer`, `VerticalFlexContainer`, `TabsContainer`, `ConditionalContainer`, `MinimumContainer`, `VerticalPaddedContainer`) **are all marked `[System.Obsolete]`**:

```csharp
// ❌ WRONG: Using deprecated containers in new UI
var flex = new HorizontalFlexContainer();
flex.Add(new TextWidget("Title"), 100f);
flex.Add(new TextFieldWidget(), Displayable.FLUID);
flex.Draw(inRect);
```

**Why this is wrong**:
1. **Excessive Allocations and Deep Trees**: Legacy containers allocate deep nested object trees during layout and drawing, impeding JIT optimization and introducing GC pauses.
2. **Lack of Overflow Safeguards**: When total fixed child widths exceed available container width and no fluid items exist, legacy flex containers degenerate and generate negative width `Rect`s, causing Unity IMGUI clipping exceptions.
3. **No Support for Modern Responsive Reflow**: Legacy containers cannot handle dynamic form wrapping (`Inline` ↔ `Stacked`), toolbar overflow `FloatMenu`s, sidebar drawer collapse, or virtual list scrolling.

```csharp
// ✅ CORRECT: Use native RimWorld IMGUI + ClientExtensionAbstractions geometry primitives
ResponsiveFormResult form = ResponsiveFormLayout.Calculate(inRect, 100f, 140f, 80f, 30f, 8f, 0f);
Widgets.Label(form.LabelRect, "Title");
_text = Widgets.TextField(form.InputRect, _text);
```

### 11.10 Hardcoded Absolute Coordinates and Ignoring Screen Safe Area

RimWorld runs across a huge variety of player display configurations: from Steam Deck / laptops at 1280×720 up to 4K desktop displays, alongside 1.25×, 1.5×, and 2.0× UI scale factors.

```csharp
// ❌ WRONG: Hardcoding dialog window Rects or expanding past the screen
public override void PreOpen()
{
    base.PreOpen();
    windowRect = new Rect(200f, 200f, 900f, 700f); // Off-screen at 720p or high UI scale!
}

// ❌ WRONG: Fixed pixel widths on buttons ignoring long localized text
Widgets.ButtonText(new Rect(x, y, 60f, 30f), "MyButtonText".Translate()); // Text overlaps in German/French!

// ❌ WRONG: Fixed-height row rendering unwrapped multi-line text without tooltips
Widgets.Label(new Rect(0f, y, width, 24f), longDescription); // Text overflows into the next row!
```

**Correct practices**:
1. **Clamp Dialogs to Safe Area**: All standalone dialog windows must call `windowRect = UiScreenSafeArea.ClampWindow(windowRect, minSize);`.
2. **The Three Text Containment Rules**:
   - Fixed-height cards/rows: Truncate single-line text (`Text.WordWrap = false`) and attach `TooltipHandler.TipRegion` to ensure complete content is accessible.
   - Dynamic long text: Measure height dynamically based on current width caches and enclose within an outer `Widgets.BeginScrollView`.
   - Toolbars & action groups: Use `ResponsiveToolbarLayout` to automatically wrap actions and overflow secondary items into a `⋯` floating menu when width is constrained.

---

## 12. Minimal Viable Example

> **⚠️ Note**: There is currently **no** complete third-party submod example project in this repository. The skeleton code below was extracted by this document's author based on framework code and official extension implementation patterns.

### 12.1 Environment Preparation and Prerequisites

**Client side**:

- Requires RimWorld 1.6 assemblies in the `GameDlls/` directory (`Assembly-CSharp.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.IMGUIModule.dll`, `UnityEngine.TextRenderingModule.dll`)
- Requires the following solution projects as `ProjectReference` in your `.csproj`:
  - `Common/Utils/Utils.csproj`
  - `Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj`
  - `Common/UserManagement/UserManagement.csproj`
- Optional:
  - `Extensions/Chat/Contracts/ChatExtension.csproj` (if you need to call Chat API)
  - `Extensions/Trade/Contracts/TradeExtension.csproj` (if you need to call Trade API)

### 12.2 Directory Structure

Recommended project directory structure (if placed outside the Phinix solution):

```
MySubmod/
  Source/
    MySubmodExtension.cs      ← Extension entry point
    MySubmodMessageHandler.cs  ← Your Message handler
    MySubmodSettingsPanel.cs   ← Settings panel
    ...
  MySubmod.csproj
```

If placed inside the Phinix solution as a project reference (recommended, easier for debugging):

```
Phinix-Rework/
  Extensions/
    MySubmod/
      Client/
        MySubmod.Client.csproj
        MySubmodExtension.cs
        ...
```

### 12.3 Project Configuration

Minimal `.csproj` skeleton (client-side, .NET Framework 4.7.2):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <OutputType>Library</OutputType>
    <RootNamespace>MyMod.PhinixExtension</RootNamespace>
    <AssemblyName>MyMod.PhinixExtension</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>

  <!-- RimWorld assembly references (same as standard Mod projects) -->
  <Choose>
    <When Condition="Exists('$(SolutionDir)\GameDlls\1.6')">
      <PropertyGroup><RimWorldDepDir>$(SolutionDir)\GameDlls\1.6</RimWorldDepDir></PropertyGroup>
    </When>
    <Otherwise>
      <PropertyGroup><RimWorldDepDir>$(SolutionDir)\GameDlls</RimWorldDepDir></PropertyGroup>
    </Otherwise>
  </Choose>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(RimWorldDepDir)\Assembly-CSharp.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(RimWorldDepDir)\UnityEngine.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(RimWorldDepDir)\UnityEngine.CoreModule.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>$(RimWorldDepDir)\UnityEngine.IMGUIModule.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <!-- Framework core dependencies -->
    <ProjectReference Include="..\..\Common\Utils\Utils.csproj">
      <Name>Utils</Name>
    </ProjectReference>
    <ProjectReference Include="..\..\Client\ClientExtensionAbstractions\ClientExtensionAbstractions.csproj">
      <Name>ClientExtensionAbstractions</Name>
    </ProjectReference>
    <ProjectReference Include="..\..\Common\UserManagement\UserManagement.csproj">
      <Name>UserManagement</Name>
    </ProjectReference>

    <!-- Optional: if you need to call Trade API -->
    <!-- <ProjectReference Include="..\Trade\Contracts\TradeExtension.csproj">
      <Name>TradeExtension</Name>
    </ProjectReference> -->
  </ItemGroup>

  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />

  <!-- Post-build copy to Extensions directory (for integrated Phinix directory deployment) -->
  <Target Name="AfterBuild">
    <MakeDir Directories="$(SolutionDir)\Output\Client\Common\Extensions" />
    <Copy SourceFiles="$(TargetDir)$(AssemblyName).dll"
          DestinationFiles="$(SolutionDir)\Output\Client\Common\Extensions\17-$(AssemblyName).dll" />
  </Target>
</Project>
```

> **Note**: If your `.csproj` reference paths point to projects within the Phinix solution, relative paths need to be adjusted based on your actual directory structure. The paths above assume your project is placed under `Extensions/MySubmod/Client/`.

### 12.4 Complete Extension Entry Point Class Code

Below is the complete code skeleton for a minimal viable submod. It:
- Registers a Message handler (logging)
- Registers a settings panel
- Subscribes to events in Activate, unsubscribes in Shutdown

```csharp
using System;
using PhinixClient;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using Verse;

namespace MyMod.PhinixExtension
{
    [PhinixExtension("mymod.myfeature")]
    public sealed class MySubmodExtension :
        IPhinixExtensionModule,
        IActivatablePhinixExtensionModule,
        IClientMessageHandler
    {
        private IFrameworkClientLifecycle _lifecycle;
        private IClientSettingsContext _settings;
        private IClientUserEventStream _userEvents;
        private IClientMainThreadDispatcher _dispatcher;
        private Action<string, LogLevel> _log;

        // Event handler references — cached in fields to ensure reference matching for -=
        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> _modeChangedHandler;
        private EventHandler _usersChangedHandler;

        // ===== IPhinixExtension =====

        public string ExtensionId => "mymod.myfeature";

        // ===== IMessageHandler =====

        public int Priority => 1500; // After Chat(1000) and Trade(1100)

        // ===== IPhinixExtensionModule =====

        public void Register(IExtensionBuilder builder)
        {
            // Only registration — do not obtain host services
            builder.AddClientMessageHandler(this);

            // Register settings panel
            builder.RegisterApi<IClientSettingsPanelProvider>(
                new MySettingsPanelProvider());

            // Register capability declaration
            builder.AddCapabilityProvider(new MyCapabilityProvider());
        }

        // ===== IActivatablePhinixExtensionModule =====

        public void Activate(ExtensionHostContext hostContext)
        {
            // Obtain required host services
            _lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            _settings = hostContext.GetRequiredService<IClientSettingsContext>();
            _userEvents = hostContext.GetRequiredService<IClientUserEventStream>();
            _dispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            _log = hostContext.Log;

            // Subscribe to events — be sure to cache handler references
            _modeChangedHandler = (_, args) =>
            {
                if (args.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                {
                    _log?.Invoke("[MySubmod] FrameworkV2 mode active.", LogLevel.INFO);
                }
            };
            _lifecycle.CompatibilityModeChanged += _modeChangedHandler;

            _usersChangedHandler = (_, __) =>
            {
                _log?.Invoke("[MySubmod] Users changed.", LogLevel.DEBUG);
            };
            _userEvents.UsersChanged += _usersChangedHandler;

            _log?.Invoke("[MySubmod] Activated.", LogLevel.INFO);
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            // Unsubscribe all events
            if (_lifecycle != null && _modeChangedHandler != null)
                _lifecycle.CompatibilityModeChanged -= _modeChangedHandler;

            if (_userEvents != null && _usersChangedHandler != null)
                _userEvents.UsersChanged -= _usersChangedHandler;

            _log?.Invoke("[MySubmod] Shut down.", LogLevel.INFO);
        }

        // ===== IClientMessageHandler =====

        public bool CanHandleOutgoingText(string rawMessage)
        {
            // Don't handle outbound — leave to Chat
            return false;
        }

        public ClientOutgoingMessageResult HandleOutgoingText(
            string rawMessage, ClientFrameworkContext context)
        {
            return null; // Won't be called (CanHandle returns false)
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            // Observe all message-type messages (can filter by MessageType)
            return message != null && message.MessageType != null;
        }

        public ClientIncomingMessageResult HandleIncomingMessage(
            FrameworkPacket message, ClientFrameworkContext context)
        {
            // Observe only, don't intercept — return Continue to let the pipeline proceed
            _log?.Invoke(
                $"[MySubmod] Observed message: type={message.MessageType}, " +
                $"from={context.SenderUuid}",
                LogLevel.DEBUG);

            return new ClientIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Continue
            };
        }
    }

    // ===== Settings Panel Provider =====

    internal sealed class MySettingsPanelProvider : IClientSettingsPanelProvider
    {
        public string SectionId => "mymod.general";
        public float Order => 200f;
        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            bool mySetting = settings.Get("mymod.mySetting", true);
            listing.CheckboxLabeled("My Feature Enabled", ref mySetting);
            settings.Set("mymod.mySetting", mySetting);
        }
    }

    // ===== Capability Declaration =====

    internal sealed class MyCapabilityProvider : ICapabilityProvider
    {
        public System.Collections.Generic.IEnumerable<string> GetCapabilities()
        {
            yield return "mymod.myfeature.v1";
        }
    }
}
```

### 12.5 Optional: Registering a Domain Contracts Project

If your submod has external interfaces that other submods need to call, it is recommended to split out an independent Contracts project, similar to Chat and Trade. This project contains only interfaces and constants:

```
Extensions/
  MySubmod/
    Contracts/
      MySubmod.csproj          ← Interfaces + constants only, no implementation
      IMyFeatureApi.cs
      MyFeatureProtocol.cs     ← MessageType constants
    Client/
      MySubmod.Client.csproj   ← Implementation layer, references Contracts
      MySubmodExtension.cs
```

Other submods can then safely reference `MySubmod/Contracts/MySubmod.csproj` without depending on your implementation details.

### 12.6 Build and Deployment

Third-party submods are recommended to be released and deployed as **standalone RimWorld mods**:

#### Option A: As an Independent Mod (Recommended)
1. Compile your submod project in Visual Studio or using `dotnet build`;
2. Copy the resulting DLL (e.g. `MySubmod.dll`) into the `Assemblies/` directory under your own mod's root folder;
3. In your mod's `About/About.xml`, declare dependencies ensuring your mod loads after Phinix:
   ```xml
   <modDependencies>
     <li>
       <packageId>hunyuan.phinixrework</packageId>
       <displayName>Phinix Rework</displayName>
     </li>
   </modDependencies>
   <loadAfter>
     <li>hunyuan.phinixrework</li>
   </loadAfter>
   ```
4. Start RimWorld and activate both Phinix and your submod in the mod manager; the host will automatically probe your mod's `Assemblies/` directory at startup and load your extension classes!

#### Option B: Integrated into Phinix Extensions Directory
1. Compile to generate your DLL;
2. Copy the DLL into the Phinix mod's `Common/Extensions/` directory;
3. **Ensure the filename prefix is >= 17-** (e.g., `17-MySubmod.dll`, ensuring it loads after official 08-16 plugins, see §12.7);
4. Start RimWorld and enable Phinix.

Log output at host startup can help confirm loading status:
```
[Phinix] Framework module 'mymod.myfeature' registered from 'MyMod.PhinixExtension.MySubmodExtension' ...
[Phinix] Framework module 'mymod.myfeature' activated for host 'client'.
```

### 12.7 Load Order Number Explanation

RimWorld's `ModAssemblyHandler` loads assemblies in filename string order. Current framework base assembly and official plugin number allocations are as follows (see [Design Philosophy §5.1](design-philosophy.md#51-naming-and-ordering)):

| Prefix | Assembly | Physical Directory | Description |
|------|--------|----------|------|
| 01-02 | LiteNetLib, Protobuf | `Common/Assemblies/` | Low-level third-party networking and serialization libraries |
| 03 | Utils | `Common/Assemblies/` | `IPhinixExtensionModule`, Framework protocol core |
| 04-05 | Connections, Connections.Client | `Common/Assemblies/` | Connection abstractions and client implementation |
| 06-07 | Authentication, Authentication.Client | `Common/Assemblies/` | Auth contracts and client implementation |
| 08 | ChatExtension | `Common/Extensions/` | Official Chat domain Contracts |
| 09 | TradeExtension | `Common/Extensions/` | Official Trade domain Contracts |
| 10 | LegacyAdapter.Client | `Common/Extensions/` | Protocol adapter client for legacy servers |
| 11 | ChatExtension.Client | `Common/Extensions/` | Official Chat client extension implementation |
| 12 | TradeExtension.Client | `Common/Extensions/` | Official Trade client extension implementation |
| 13 | LegacyRedPacketExtension | `Common/Extensions/` | Official Red Packet extension contracts |
| 14 | LegacyRedPacketExtension.Client | `Common/Extensions/` | Official Red Packet client extension implementation |
| 15 | LegacyTalentTradeExtension | `Common/Extensions/` | Official Talent/Ability Trade extension contracts |
| 16 | LegacyTalentTradeExtension.Client | `Common/Extensions/` | Official Talent/Ability Trade client extension implementation |
| 13 | PhinixClient | `1.6/Assemblies/` | Client host (version-isolated) |
| 17+ | Third-party Submods (when placed in Extensions) | `Common/Extensions/` | Must use 17 or higher prefix, loaded after all official plugins |

> **Tip**: If distributing via **Option A (Independent Mod)**, your DLL resides in an independent mod's `Assemblies/`. RimWorld loads all Phinix assemblies before loading your mod's assemblies, so numeric prefixes are generally unnecessary; however, if your mod contains multiple interdependent DLLs, alphabetical ordering rules still apply among them.

### 12.8 Debugging Tips

- **Loading issues**: Check the RimWorld console log, search for the `[Phinix]` keyword, and observe diagnostic output for extension discovery/registration/activation.
- **DLL not discovered**: Check whether the DLL is in an `ExtensionAssemblyLoader` probe directory and whether the filename ends with `.dll`.
- **Type load exception** (`ReflectionTypeLoadException`): Usually a dependent DLL is missing or has a version mismatch — check that all ProjectReferences have been placed in the corresponding probe directory.
- **Activate not called**: Confirm that the module implements both `IPhinixExtensionModule` and `IActivatablePhinixExtensionModule`.
- **UI not showing**: Confirm that `RegisterApi<IMainTabProvider>` is called in `Register()`; check whether `TabOrder` conflicts with another Tab.
- **Extension disabled**: Check the in-game Extension Management tab (`ExtensionManagerTab`) or settings panel to ensure the extension was not manually disabled or placed in `DependencyDisabled` due to missing parent dependencies.

---

## Appendix A: IExtensionBuilder Complete Registration Method Quick Reference

| Method | Parameter Type | Purpose | Current Status |
|------|----------|------|----------|
| `AddCapabilityProvider` | `ICapabilityProvider` | Declare supported capabilities | ✅ |
| `AddMessageInterceptor` | `IMessageInterceptor` | Display message interception | ✅ |
| `AddMessageRenderer` | `IMessageRenderer` | Message renderer | ✅ |
| `AddClientMessageHandler` | `IClientMessageHandler` | Client-side message handling (inbound+outbound) | ✅ |
| `AddClientCommandHandler` | `IClientCommandHandler` | Client-side command handling (inbound) | ✅ |
| `AddItemCodec` | `IItemCodec` | Register item codec (consumed by Item pipeline and default handler) | ✅ |
| `AddClientItemHandler` | `IClientIncomingItemHandler` | Client-side inbound item handling | ✅ |
| `AddClientOutgoingItemHandler` | `IClientOutgoingItemHandler` | Client-side outbound item handling | ✅ |
| `AddServerMessageHandler` | `IServerMessageHandler` | Server-side message handling | ✅ (server only) |
| `AddServerInboundMessageInterceptor` | `IServerInboundMessageInterceptor` | Server-side message interception | ✅ (server only) |
| `AddServerDefaultMessageHandler` | `IServerDefaultMessageHandler` | Server-side default message handling | ✅ (server only) |
| `AddServerMessageObserver` | `IServerMessageObserver` | Server-side message observation | ✅ (server only) |
| `AddServerCommandHandler` | `IServerCommandHandler` | Server-side command handling | ✅ (server only) |
| `AddServerInboundCommandInterceptor` | `IServerInboundCommandInterceptor` | Server-side command interception | ✅ (server only) |
| `AddServerDefaultCommandHandler` | `IServerDefaultCommandHandler` | Server-side default command handling | ✅ (server only) |
| `AddServerCommandObserver` | `IServerCommandObserver` | Server-side command observation | ✅ (server only) |
| `AddServerItemHandler` | `IServerItemHandler` | Server-side item handling | ✅ (server only) |
| `AddServerInboundItemInterceptor` | `IServerInboundItemInterceptor` | Server-side item interception | ✅ (server only) |
| `AddServerDefaultItemHandler` | `IServerDefaultItemHandler` | Server-side default item handling | ✅ (server only) |
| `AddServerItemObserver` | `IServerItemObserver` | Server-side item observation | ✅ (server only) |
| `AddServerOutboundPacketInterceptor` | `IServerOutboundPacketInterceptor` | Server-side outbound interception | ✅ (server only) |
| `AddConsoleCommandProvider` | `IServerConsoleCommandProvider` | Server console command extension | ✅ (server only) |
| `RegisterApi<T>` | `T` implementation | Expose API | ✅ |
| `TryResolveApi<T>` | out `T` | Resolve single API | ✅ |
| `ResolveApis<T>` | — | Resolve all APIs | ✅ |

> **Status symbols**: ✅ = Fully available | ⚠️ = Half-finished/transitional | 🔮 = Planned

## Appendix B: ExtensionHostContext Complete Service Quick Reference

The following services are obtained in `Activate()` via `hostContext.GetRequiredService<T>()`:

| Service Interface | Purpose | Definition Location |
|----------|------|----------|
| `IFrameworkClientTransport` | Message & Item pipeline outbound entry (`TryHandleOutgoingMessage` / `TryHandleOutgoingItem`) | [IClientExtensionAbstractions.cs:9-28](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L9-L28) |
| `IFrameworkClientCommandTransport` | Command pipeline outbound entry (`TryHandleOutgoingCommand`) | [IClientExtensionAbstractions.cs:30-39](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L30-L39) |
| `IClientDisplayMessageStore` | Message persistent storage | [IClientExtensionAbstractions.cs:41-50](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L41-L50) |
| `IClientDisplayMessageFeed` | Message stream event subscription | [IClientExtensionAbstractions.cs:52-55](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L52-L55) |
| `IFrameworkClientLifecycle` | Compatibility mode and negotiation | [IClientExtensionAbstractions.cs:67-72](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L67-L72) |
| `IClientSessionContext` | Current session state | [IClientExtensionAbstractions.cs:74-83](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L74-L83) |
| `IClientSettingsContext` | Read/write settings | [IClientExtensionAbstractions.cs:85-100](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L85-L100) |
| `IClientUserDirectory` | User info query | [IClientExtensionAbstractions.cs:102-109](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L102-L109) |
| `IClientUserEventStream` | User event subscription | [IClientExtensionAbstractions.cs:111-120](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L111-L120) |
| `IClientMainThreadDispatcher` | Main thread marshaling | [IClientExtensionAbstractions.cs:122-125](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L122-L125) |
| `IClientWindowService` | Open windows | [IClientExtensionAbstractions.cs:127-132](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L127-L132) |
| `IClientSoundService` | Play sound effects | [IClientExtensionAbstractions.cs:134-137](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L134-L137) |
| `ILegacyModuleTransport` | Raw module communication | [IClientExtensionAbstractions.cs:162-173](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L162-L173) |
| `IDisplayMessageSink` | Inject display messages | [IClientExtensionAbstractions.cs:178-182](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L178-L182) |
| `IUiTheme` | Unified UI theme and palette tokens | [IUiTheme.cs](Client/ClientExtensionAbstractions/UI/IUiTheme.cs) |
| `IItemCodecProvider` | Item codec query and resolution | [FrameworkTypes.cs:607-610](Common/Utils/Framework/FrameworkTypes.cs#L607-L610) |
| `IExtensionActivationPolicy` | Extension activation policy and disabled list | [FrameworkTypes.cs:517-532](Common/Utils/Framework/FrameworkTypes.cs#L517-L532) |
| `UserManager` | Low-level user management (injected via `AddService`) | [Client/Source/Client.cs](Client/Source/Client.cs) |
| `Action` | Open settings window (same as `IClientWindowService.OpenSettingsWindow`) | [Client/Source/Client.cs](Client/Source/Client.cs) |
| `Action<bool>` | Sync acceptingTrades state | [Client/Source/Client.cs](Client/Source/Client.cs) |

> **Tip**: UI extension contracts (`IMainTabProvider`, `IServerSidebarProvider`, `IResponsiveMainTabProvider`, `IResponsiveSidebarProvider`, `IBadgeProvider`, `IClientSettingsPanelProvider`, `IClientLegacySettingsMigrator`, `INoticeBannerProvider`, `IUiAcceptKeyHandler`, `IUiTheme`) are registered declaratively via `builder.RegisterApi<T>()`; `hostContext` itself also provides `Log`, `StorageProvider`, `ApiRegistry` (`TryResolveApi` / `ResolveApis`), `GetStoragePath()` and other methods — see [FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs).
