# Phinix Submod Developer Guide

> **Target audience: third-party submod developers / 面向受众：第三方附属 Mod / Submod 开发者**
>
> **中文版：Phinix附属Mod开发者指南.md**
>
> **Document scope**: This document and [design-philosophy.md](./design-philosophy.md) are cross-branch shared baseline documents. The former explains "why this design"; this document tells you "how to use it in practice."
>
> **Last updated**: 2026-06-01, written against actual code on the `dev` branch. The framework is still under active evolution — this document explicitly marks the current status of each capability: ✅ fully available, ⚠️ half-finished/transitional, 🔮 planned.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Dependency Boundaries](#2-dependency-boundaries)
3. [Extension Entry Point and Lifecycle](#3-extension-entry-point-and-lifecycle)
4. [Registry: What IExtensionBuilder Can Do](#4-registry-what-iextensionbuilder-can-do)
5. [API Exposure and Resolution](#5-api-exposure-and-resolution)
6. [The Three Communication Pipelines](#6-the-three-communication-pipelines)
7. [Integrating with UI](#7-integrating-with-ui)
8. [Common Services Provided by the Host](#8-common-services-provided-by-the-host)
9. [Inter-Plugin Collaboration](#9-inter-plugin-collaboration)
10. [Compatibility Mode and Legacy](#10-compatibility-mode-and-legacy)
11. [Common Anti-Patterns and Pitfalls](#11-common-anti-patterns-and-pitfalls)
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

When the host starts, it calls `ExtensionAssemblyLoader.LoadAssemblies()` to scan `.dll` files under the following directories (see the `GetExtensionProbeDirectories` method at [Client.cs:400-429](Client/Source/Client.cs#L400-L429) for details):

```
YourMod/
  Common/
    Assemblies/           ← Framework base DLLs (01-07) + current official plugin DLLs (08-11)
    Extensions/           ← Dedicated plugin directory (currently also scanned; target state will be independent)
```

Numeric prefixes (e.g., `08-`) cannot be omitted — RimWorld's `ModAssemblyHandler` loads DLLs in filename string order, and dependencies must be loaded before dependents (see [§12.7](#127-load-order-number-explanation) for details). ExtensionAssemblyLoader code location: [Common/Utils/Framework/ExtensionAssemblyLoader.cs](Common/Utils/Framework/ExtensionAssemblyLoader.cs).

The future target state ([Design Philosophy §5.2](design-philosophy.md#52-publishing-boundary-target-state)) will move plugin DLLs to `Extensions/` and separate them from `Assemblies/`.

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

### 3.3 The `[PhinixExtension]` Attribute

Your module class must be marked with `[PhinixExtension("your.id")]`, otherwise the framework's reflection scan will not find you (unless your class implements `IPhinixExtension` and is also marked non-abstract, in which case the old legacy auto-discovery path will still pick it up, but the framework will emit a warning advising you to migrate to `IPhinixExtensionModule`).

```csharp
[PhinixExtension("myname.myfeature")]
public class MyExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public string ExtensionId => "myname.myfeature";
    // ...
}
```

### 3.4 Full Lifecycle

The framework manages extensions in four phases (see the `DiscoverExtensions` and `ActivateExtensions` methods in [PhinixExtensionRegistry.cs](Common/Utils/Framework/PhinixExtensionRegistry.cs)):

```
1. Discover  ── Reflection scans assemblies, finds all IPhinixExtensionModule
                 ↓
2. Register  ── Calls each module's Register(builder)
                Modules register handlers, APIs, capabilities
                After completion, status becomes Registered
                 ↓
3. Activate  ── Calls each module's Activate(hostContext)
                Modules obtain host services, subscribe to events
                After completion, status becomes Active
                 ↓
4. Shutdown  ── Calls each module's Shutdown(hostContext)
                Modules unsubscribe, release resources
                After completion, status becomes Shutdown
```

### 3.5 Error Isolation

Failure of a single module's `Register()`, `Activate()`, or `Shutdown()` does **not** affect other modules:

- `Register()` exceptions are caught, status marked as `Failed`, warning logged
- `Activate()` exceptions are caught, status marked as `Failed`, warning logged
- `Shutdown()` exceptions are likewise isolated

This means **your submod will not bring down the entire framework** — but conversely, the framework will not automatically retry your failed module.

---

## 4. Registry: What IExtensionBuilder Can Do

`Register(IExtensionBuilder builder)` is your core entry point for interacting with the framework. `builder` provides the following capabilities (full interface definition at [FrameworkTypes.cs:105-148](Common/Utils/Framework/FrameworkTypes.cs#L105-L148)):

### 4.1 Registering Handlers (Hooking into Communication Pipelines)

```csharp
// Message pipeline
builder.AddClientMessageHandler(this);           // IClientMessageHandler

// Command pipeline
builder.AddClientCommandHandler(this);           // IClientCommandHandler (inbound)
// If your class implements both IClientCommandHandler and IClientOutgoingCommandHandler,
// AddClientCommandHandler(this) covers both in one registration — the framework runtime
// filters for IClientOutgoingCommandHandler for outbound.
// If you only implement IClientOutgoingCommandHandler (no inbound), you need to register
// separately on the builder — currently AddClientCommandHandler's parameter type is IClientCommandHandler.

// Other pipeline roles
builder.AddMessageInterceptor(this);             // IMessageInterceptor
builder.AddMessageRenderer(this);                // IMessageRenderer
builder.AddCapabilityProvider(this);             // ICapabilityProvider
builder.AddServerMessageHandler(this);           // IServerMessageHandler (server-side extensions)
builder.AddItemCodec(this);                      // IItemCodec (⚠️ half-finished — see §6.3)
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

### 6.3 Item Pipeline ⚠️ Half-Finished / Transitional

**Current actual state** (verified against `dev` branch code):

The Item pipeline is **not independently usable**. Here are the facts:

- ❌ The client-side `packetHandler` has **no** `KindItem` branch — Item data cannot be independently routed from server to client
- ❌ The client side has **no** `IClientIncomingItemHandler` / `IClientOutgoingItemHandler` interfaces
- ✅ The `IItemCodec` interface is defined ([FrameworkTypes.cs:530-541](Common/Utils/Framework/FrameworkTypes.cs#L530-L541))
- ✅ The `builder.AddItemCodec()` registration method exists ([FrameworkTypes.cs:415](Common/Utils/Framework/FrameworkTypes.cs#L415))
- ❌ But registered codecs are **not consumed by the pipeline** — `ProcessIncomingItem` decodes and then discards results

**Currently available Item transmission method**:

Item data is transmitted by **nesting `FrameworkItemPayload` inside the Command pipeline**. Taking Trade as an example:

```
FrameworkPacket (Kind="command")
  └─ PayloadJson = JSON(FrameworkTradeOfferUpdateRequest)
       └─ Items = List<FrameworkItemPayload>
            └─ PayloadBytes = protobuf(FrameworkVanillaItemData)
```

This means:
- Item data **parasitically rides inside the Command pipeline**
- You need to implement `IClientCommandHandler` to handle Item data
- There is no dedicated interceptor/handler/observer chain for Item use

**Current recommendations for submod developers**:

| Your scenario | Currently recommended approach |
|----------|------------------|
| Transmitting a small amount of structured control instructions | Command pipeline — standard, fully available |
| Transmitting binary-payload data (items, creatures, etc.) | Command pipeline nesting `FrameworkItemPayload` — this is exactly what Trade currently does |
| Server-side needs to validate/intercept Item data | ⚠️ Currently not possible — server-side Item codec decodes and discards, no interception chain |
| Want one codec to be automatically routed by the pipeline | ⚠️ Currently not possible — wait for P0 completion |

**Future evolution direction** 🔮:

The framework plans to complete an independent Item pipeline (P0 phase), at which point:
- A new `KindItem` packet type with independent routing will be added
- `IItemCodec` registrations will be consumed by the pipeline
- A server-side three-phase chain (Interceptor → Handler → Observer) will be introduced
- **The current Command nesting approach will remain compatible**

For detailed analysis, see `docs/branch-local/dev/三条Pipeline职责辨析与Item管线补全分析.md` (internal design reference).

### 6.4 Inbound / Outbound Flow Reference Table

| Direction | Message | Command | Item |
|------|---------|---------|------|
| Server → Client (inbound) | `IClientMessageHandler` → `IMessageRenderer` → UI | `IClientCommandHandler` → internal state | ❌ No independent routing |
| Client → Server (outbound) | `TryHandleOutgoingMessage()` → `IClientMessageHandler` chain | `TryHandleOutgoingCommand()` → `IClientOutgoingCommandHandler` chain | ❌ No independent routing |
| Outbound pipeline entry | `IFrameworkClientTransport` | `IFrameworkClientCommandTransport` | — |
| Outbound must go through pipeline | ✅ Yes ([Design Philosophy §3.7]) | ✅ Yes | — |

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

### 7.3 Adding a Badge

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

### 7.4 Adding a Settings Panel

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

### 7.5 Settings Migration (Legacy Settings)

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

### 7.6 Pushing Display Messages

If your submod needs to inject notifications into the message queue (not messages coming from the server via the Message pipeline, but locally generated notifications), use `IDisplayMessageSink` (defined at [IClientExtensionAbstractions.cs:171-175](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L171-L175)):

```csharp
public interface IDisplayMessageSink
{
    void Enqueue(FrameworkDisplayMessage message);
}
```

This service is obtained in `Activate()` via `hostContext.GetRequiredService<IDisplayMessageSink>()`.

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

Entry point for the Message pipeline (outbound). See [§6.1](#61-message-pipeline-fully-available):

```csharp
public interface IFrameworkClientTransport
{
    bool HasRemoteCapability(string capability);
    void SendFrameworkPacket(FrameworkPacket packet);          // ⚠️ Restricted: orthodox communication should use TryHandle
    bool TryHandleOutgoingMessage(string rawMessage);         // ✅ Recommended outbound entry
}
```

> **About `SendFrameworkPacket`**: This method sends a FrameworkPacket directly without going through the handler pipeline. According to Design Philosophy §3.7, plugins should not bypass the pipeline — for orthodox communication, use `TryHandleOutgoingMessage` / `TryHandleOutgoingCommand`.

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

> **Current convention**: Official extensions (Chat/Trade) use `hostContext.Log` (`Action<string, LogLevel>`) to report logs. The `ILoggable` interface is currently a log-producer contract used by host internal components (`NetClient`, `PhinixFrameworkClient`, etc.) and is not yet directly exposed to plugins. Migration to extension-level `ILoggable` support is planned.

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

  <!-- Post-build copy to Extensions directory -->
  <Target Name="AfterBuild">
    <MakeDir Directories="$(SolutionDir)\Output\Client\Common\Extensions" />
    <Copy SourceFiles="$(TargetDir)$(AssemblyName).dll"
          DestinationFiles="$(SolutionDir)\Output\Client\Common\Extensions\12-$(AssemblyName).dll" />
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

1. Compile in Visual Studio or with `dotnet build`
2. Place the DLL file in the `Output/phinix-rework/Common/Extensions/` directory
3. Ensure the filename has the correct numeric prefix (see §12.7)
4. Launch RimWorld with Phinix enabled — your submod should be automatically discovered

Log output at host startup can help confirm loading status:
```
[Phinix] Framework module 'mymod.myfeature' registered from 'MyMod.PhinixExtension.MySubmodExtension' ...
[Phinix] Framework module 'mymod.myfeature' activated for host 'client'.
```

### 12.7 Load Order Number Explanation

The filename prefix (e.g., `08-`, `11-`) on DLLs in the `Extensions/` directory determines RimWorld's loading order. The current framework base DLL number assignments are as follows (see [Design Philosophy §5.1](design-philosophy.md#51-naming-and-ordering)):

| Prefix | Assembly | Content |
|------|--------|------|
| 01-02 | LiteNetLib, Protobuf | Third-party libraries |
| 03 | Utils | `IPhinixExtensionModule`, Framework base |
| 04 | Connections | Network layer |
| 05 | Authentication | Authentication |
| 06 | UserManagement | User management |
| 07 | ClientExtensionAbstractions | UI interfaces, host service interfaces |
| 08 | ChatExtension | Chat domain Contracts |
| 09 | TradeExtension | Trade domain Contracts |
| 10 | ChatExtension.Client | Chat plugin (depends on 03,07,08) |
| 11 | TradeExtension.Client | Trade plugin (depends on 03,07,09) |

Your submod DLL prefix should be **greater than all assemblies it depends on**. For example:
- Only depends on 03 + 07 → prefix >= 12
- Depends on 08 (Chat Contracts) → prefix >= 12 (because 08 already exists, your DLL must come after Chat Contracts, but whether Chat.Client(10) comes before or after your DLL does not affect your reference to Chat Contracts)

### 12.8 Debugging Tips

- **Loading issues**: Check the RimWorld console log, search for the `[Phinix]` keyword, and observe diagnostic output for extension discovery/registration/activation.
- **DLL not discovered**: Check whether the DLL is in an `ExtensionAssemblyLoader` probe directory and whether the filename ends with `.dll`.
- **Type load exception** (`ReflectionTypeLoadException`): Usually a dependent DLL is missing or has a version mismatch — check that all ProjectReferences have been placed in the Extensions directory.
- **Activate not called**: Confirm that the module implements both `IPhinixExtensionModule` and `IActivatablePhinixExtensionModule`.
- **UI not showing**: Confirm that `RegisterApi<IMainTabProvider>` is called in `Register()`; check whether `TabOrder` conflicts with another Tab.

---

## Appendix A: IExtensionBuilder Complete Registration Method Quick Reference

| Method | Parameter Type | Purpose | Current Status |
|------|----------|------|----------|
| `AddCapabilityProvider` | `ICapabilityProvider` | Declare supported capabilities | ✅ |
| `AddMessageInterceptor` | `IMessageInterceptor` | Display message interception | ✅ |
| `AddMessageRenderer` | `IMessageRenderer` | Message renderer | ✅ |
| `AddClientMessageHandler` | `IClientMessageHandler` | Client-side message handling (inbound+outbound) | ✅ |
| `AddClientCommandHandler` | `IClientCommandHandler` | Client-side command handling (inbound) | ✅ |
| `AddServerMessageHandler` | `IServerMessageHandler` | Server-side message handling | ✅ (server only) |
| `AddServerInboundMessageInterceptor` | `IServerInboundMessageInterceptor` | Server-side message interception | ✅ (server only) |
| `AddServerDefaultMessageHandler` | `IServerDefaultMessageHandler` | Server-side default message handling | ✅ (server only) |
| `AddServerMessageObserver` | `IServerMessageObserver` | Server-side message observation | ✅ (server only) |
| `AddItemCodec` | `IItemCodec` | Register item codec | ⚠️ Interface defined, registration valid but pipeline does not consume |
| `AddServerCommandHandler` | `IServerCommandHandler` | Server-side command handling | ✅ (server only) |
| `AddServerInboundCommandInterceptor` | `IServerInboundCommandInterceptor` | Server-side command interception | ✅ (server only) |
| `AddServerDefaultCommandHandler` | `IServerDefaultCommandHandler` | Server-side default command handling | ✅ (server only) |
| `AddServerCommandObserver` | `IServerCommandObserver` | Server-side command observation | ✅ (server only) |
| `AddServerOutboundPacketInterceptor` | `IServerOutboundPacketInterceptor` | Server-side outbound interception | ✅ (server only) |
| `RegisterApi<T>` | `T` implementation | Expose API | ✅ |
| `TryResolveApi<T>` | out `T` | Resolve single API | ✅ |
| `ResolveApis<T>` | — | Resolve all APIs | ✅ |

> **Status symbols**: ✅ = Fully available | ⚠️ = Half-finished/transitional | 🔮 = Planned

## Appendix B: ExtensionHostContext Complete Service Quick Reference

The following services are obtained in `Activate()` via `hostContext.GetRequiredService<T>()`:

| Service Interface | Purpose | Definition Location |
|----------|------|----------|
| `IFrameworkClientTransport` | Message pipeline outbound entry | [IClientExtensionAbstractions.cs:9-21](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L9-L21) |
| `IFrameworkClientCommandTransport` | Command pipeline outbound entry | [IClientExtensionAbstractions.cs:23-31](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L23-L31) |
| `IClientDisplayMessageStore` | Message persistent storage | [IClientExtensionAbstractions.cs:34-43](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L34-L43) |
| `IClientDisplayMessageFeed` | Message stream event subscription | [IClientExtensionAbstractions.cs:45-48](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L45-L48) |
| `IFrameworkClientLifecycle` | Compatibility mode and negotiation | [IClientExtensionAbstractions.cs:60-65](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L60-L65) |
| `IClientSessionContext` | Current session state | [IClientExtensionAbstractions.cs:67-75](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L67-L75) |
| `IClientSettingsContext` | Read/write settings | [IClientExtensionAbstractions.cs:78-93](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L78-L93) |
| `IClientUserDirectory` | User info query | [IClientExtensionAbstractions.cs:95-103](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L95-L103) |
| `IClientUserEventStream` | User event subscription | [IClientExtensionAbstractions.cs:105-113](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L105-L113) |
| `IClientMainThreadDispatcher` | Main thread marshaling | [IClientExtensionAbstractions.cs:115-118](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L115-L118) |
| `IClientWindowService` | Open windows | [IClientExtensionAbstractions.cs:120-125](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L120-L125) |
| `IClientSoundService` | Play sound effects | [IClientExtensionAbstractions.cs:127-130](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L127-L130) |
| `ILegacyModuleTransport` | Raw module communication | [IClientExtensionAbstractions.cs:155-165](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L155-L165) |
| `IDisplayMessageSink` | Inject display messages | [IClientExtensionAbstractions.cs:171-175](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L171-L175) |
| `UserManager` | Low-level user management (injected via `AddService`) | [Client/Source/Client.cs](Client/Source/Client.cs) |
| `Action` | Open settings window (same as `IClientWindowService.OpenSettingsWindow`) | [Client/Source/Client.cs:121](Client/Source/Client.cs#L121) |
| `Action<bool>` | Sync acceptingTrades state | [Client/Source/Client.cs:122](Client/Source/Client.cs#L122) |

> **Additionally**: `hostContext` itself also provides `Log`, `StorageProvider`, `ApiRegistry` (`TryResolveApi` / `ResolveApis`), `GetStoragePath()` and other methods — see [FrameworkTypes.cs:320-439](Common/Utils/Framework/FrameworkTypes.cs#L320-L439).
