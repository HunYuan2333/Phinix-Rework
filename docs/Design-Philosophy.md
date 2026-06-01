# Phinix Rework — Design Philosophy

> **Target audience: code maintainers and AI agents**
>
> **中文版**：[设计哲学.md](./设计哲学.md)
>
> **Last updated**: 2026-06-01, compiled based on Phase 5 architecture migration landing point and codebase audit.

---

## 1. Core Principles

### 1.1 Plugin Equality

Chat and Trade have the same status as third-party submods. There are no "official super-citizens":

- Same discovery path (reflection scan `IPhinixExtensionModule`)
- Same registration path (`Register(builder)` → `RegisterApi<T>()` / `AddClientMessageHandler()`)
- Same activation path (`Activate(hostContext)` → `Shutdown(hostContext)`)

**Anti-pattern**: The host reserves dedicated interface resolution, dedicated startup branches, or dedicated UI entry points for a specific plugin.

### 1.2 Host Does Not Depend on Plugins

The host only references `ClientExtensionAbstractions` (the general contract layer), and does not reference any plugin's Contracts project:

- `Client.csproj`'s ProjectReference **must not include** `ChatExtension`, `TradeExtension`, or any plugin project
- The host dynamically collects plugin capabilities through `ResolveExtensionApis<T>()`, without strong type binding
- Plugins can depend on the host, but not the other way around

**Anti-pattern**: `Client.csproj` explicitly references plugin projects; host code contains type references to concrete business interfaces such as `IClientChatService`, `IClientTradeService`.

### 1.3 Host Only Provides General Services

The basic services provided by the host are business-agnostic:

```
Network layer (NetClient)
Extension discovery (PhinixExtensionRegistry)
General services (IClientSessionContext, IClientSettingsContext, IClientUserDirectory,
           IClientUserEventStream, IClientMainThreadDispatcher, IClientWindowService)
ServerTab (pure shell, collects IMainTabProvider / IServerSidebarProvider for dynamic rendering)
Basic UI (SettingsWindow, CredentialsWindow)
```

Business logic (chat, trade, red packets, etc.) is entirely in plugins. The host does not care what businesses currently exist.

---

## 2. Software Engineering Principles

### 2.1 Loose Coupling

Modules interact through interface contracts, not directly depending on concrete implementations:

- Plugins and host are coupled through general interfaces defined in `ClientExtensionAbstractions`, not through concrete types
- Inter-plugin interaction is defined by plugins themselves through interfaces and called directly; the framework neither acts as an intermediary nor prevents it
- Adding a new plugin does not affect host compilation or the operation of existing plugins

**Judgment criterion**: Can a complete business plugin (including Tab, sidebar, badge) be added without modifying the host code?

### 2.2 Layering

The system is divided into clear layers, with upper layers depending on lower layers; reverse dependencies are forbidden:

```
┌─────────────────────────────────────────┐
│  Plugins (Chat, Trade, Third-party)      │  ← Business layer
├─────────────────────────────────────────┤
│  ClientExtensionAbstractions             │  ← Shared contract layer
├─────────────────────────────────────────┤
│  Host (Client)                           │  ← Host layer
│  ├─ Network / Auth / User Management     │
│  ├─ Extension Discovery / Lifecycle      │
│  └─ General UI Shell                     │
├─────────────────────────────────────────┤
│  Common (Utils, Connections, etc.)       │  ← Infrastructure layer
└─────────────────────────────────────────┘
```

- Upper layers can call lower layers; lower layers **must never** reverse-depend on upper layers
- Modules within the same layer should stay as independent as possible, minimizing horizontal coupling
- Each layer only exposes interfaces within its responsibility scope, without leaking internal implementation details

**Anti-pattern**: `Common` compiles `../../Server/*.cs` source files; host code directly accesses plugin-internal types.

### 2.3 Minimize Hardcoding

Anything that may change with business requirements should be abstracted through contracts, not hardcoded in the host:

- Tab content → `IMainTabProvider`, do not hardcode "Chat Tab / Trade Tab"
- Sidebar content → `IServerSidebarProvider`, do not hardcode "User List Panel"
- Badges → `IBadgeProvider`, do not hardcode "Unread Message Count"
- Message handling → `module → Kind → MessageType → handler` dynamic routing, do not hardcode dispatch for specific message types
- Event notification → `IClientUserEventStream`, do not provide dedicated event bridges for specific plugins

**Judgment criterion**: When adding a new business capability (such as mail, announcements, quests), is the host code change count zero?

---

## 3. Key Design Decisions

### 3.1 Dynamic Tab Mechanism

The communication layer and UI layer use the same dynamic dispatch pattern: plugins register handlers/providers → host discovers and collects through interfaces → routes by sort key (Priority/TabOrder).

- `ServerTab` is a pure container, containing no business UI
- `ServerTabButtonWorker` aggregates all `IBadgeProvider`s, only displaying the first badge with content
- Adding a new Tab/sidebar only requires implementing the corresponding interface and registering it in `Register()`

### 3.2 Three Communication Pipeline Categories

Framework communication is divided into three main pipelines, each with its own responsibility:

| Pipeline | Responsibility | Example | Current Status |
|------|------|------|----------|
| Message | User-visible messages | Chat messages, system notifications | ✅ Fully available |
| Command | Background control instructions | Trade creation/update, snapshot sync | ✅ Fully available |
| Item | Item data | Trade item encoding/decoding | ⚠️ Half-finished |

> **About the current state of the Item pipeline**: The `IItemCodec` interface and `AddItemCodec()` registration method are already defined, but the Client side has no independent Item routing (the `packetHandler` lacks a `KindItem` branch), and registered codecs have not yet been consumed by the pipeline. Currently, Item data is transmitted via the Command pipeline nesting `FrameworkItemPayload`. An independent Item pipeline is a P0 evolution goal; when it arrives, the current approach will remain compatible. See the "Submod Developer Guide" §6.3 for details.

When adding a new message type, first determine which pipeline it belongs to, then choose the corresponding handler interface to implement.

### 3.3 Inter-Plugin Interaction

Collaboration between plugins is the responsibility of the plugins themselves; the framework does not act as an intermediary:

- Chat needs to initiate a trade → Chat directly references `TradeExtension`'s Contracts, calling `ITradeRequestApi.CreateTrade()`
- Two plugins need to share data → each defines its own interface, resolving each other through the API registry
- The framework's role is only to provide the discovery mechanism (API registry), not to undertake business coordination

### 3.4 General Event Stream Replaces Dedicated Bridges

Plugins do not obtain host events through host-customized bridge interfaces tailored for them, but instead consume general event streams (`IClientUserEventStream`, `IClientMainThreadDispatcher`, `IClientSettingsContext`, `IClientSessionContext`, etc.). See the "Submod Developer Guide" §8 for the specific interface list and usage.

**Anti-pattern**: The host defines plugin-specific interfaces such as `IChatUiEventSink`, `ITradeUiHostContext`, creating a new set each time a plugin is added.

### 3.5 Error Isolation and Retry Mechanism

A single failure in network processing, message parsing, or extension execution **must not** interrupt the overall pipeline or cause service crashes. The framework employs a layered isolation strategy for various error types:

**In-pipeline isolation:**

- Exceptions thrown by individual interceptors/handlers are caught and logged by PipelineRunner, and the pipeline continues processing the next candidate
- A single message parsing failure (e.g., Protobuf `ParseFrom` exception) only skips that message, without affecting other messages in the same frame
- Extension `Activate()` / `Shutdown()` failures do not affect the lifecycle of other extensions

**Network layer resilience:**

- Critical handshake packet (Hello / Auth / Login / ExtendSession) send failures must actively disconnect the corresponding connection to prevent the client from waiting indefinitely
- Non-critical message send failures log errors and notify the caller (via return value or callback), without silently dropping
- On connection disconnect, immediately clean up associated session, user state, and event subscriptions, without relying on timeout timers as a fallback

**Retry strategy:**

- Client connection failures should provide clear error information (distinguishing "network unreachable", "authentication rejected", "protocol mismatch"), rather than a vague "connection failed"
- Messages sent when the client is not connected should trigger observable alerts (Error-level log or callback notification), without silently dropping
- When the server fails to send to a specific connection, distinguish "connection already disconnected" and "send timeout" — the former triggers the cleanup flow, the latter may consider retry

**Anti-pattern**: catch-all silently swallowing exceptions; background thread exceptions not notifying the main thread; send failures only writing a single line of Debug log while the caller is unaware.

### 3.6 Backpressure and Resource Boundaries

All unbounded queues must have capacity limits to prevent unbounded memory growth when producers are faster than consumers:

- Message display queue (`displayMessages`): limit 1000, remove the oldest entry and log a Warning when exceeded
- Main thread dispatch queue (`pendingActions`): limit 500, discard and log an Error when exceeded
- Chat history sync: send in batches (50-100 per batch), do not block-send all history within a single poll cycle

Classes holding `IDisposable` resources such as `Timer`, `NetManager`, `Thread`, `FileStream` must implement `IDisposable` and release them in `Shutdown` / `Dispose`. Event subscriptions (`+=`) must be paired with unsubscriptions (`-=`) in the corresponding `Shutdown` or `Dispose`, without relying on process exit as the ultimate cleanup mechanism.

### 3.7 Plugins Must Not Bypass the Communication Pipeline to Directly Access the Underlying Transport

When communicating with the server, plugins **must go through the framework-defined handler pipeline** (`IClientMessageHandler`, `IClientCommandHandler`), and **must not** directly call `IFrameworkClientTransport.SendFrameworkPacket()` or `ILegacyModuleTransport.Send()` to bypass the pipeline.

**Why:**

- The handler pipeline is the mechanism by which the framework implements "plugin equality" (§1.1) — Priority ordering, interception, replacement, and fallback all depend on handlers executing in sequence
- A plugin directly sending a FrameworkPacket bypassing the pipeline = that protocol packet **skips all other plugins' handlers**. If another plugin (such as LegacyAdapter) tries to intercept traffic for protocol translation through Priority ordering, it will be completely ineffective
- A well-positioned plugin in the ecosystem (such as message auditing, content filtering, protocol adaptation) should not be rendered ineffective because other plugins "took a shortcut"

**Correct approach:**

- Outbound messages: return `FrameworkPacket` through `IClientMessageHandler.HandleOutgoingText()`, letting `PhinixFrameworkClient.TryHandleOutgoingMessage()` send uniformly
- Outbound commands (Trade, etc.): through the `IClientCommandHandler` pipeline, or through `IFrameworkClientLifecycle.CompatibilityMode` to determine the routing strategy after checking the current mode
- Inbound messages: distributed by the framework from `NetClient` to the handler pipeline after reception; plugins do not register `NetClient` handlers themselves to intercept inbound traffic (except Legacy protocol adaptation, since Legacy inbound traffic is entirely outside the Framework pipeline)

**Sole exception**: The Legacy protocol adapter can register raw module handlers through `ILegacyModuleTransport.RegisterHandler()`, because Legacy inbound data does not pass through the Framework pipeline at all (old servers send `"Chat"` / `"Trading"` module packets, not `"PhinixFramework"` packets).

**Judgment criterion**: Can any new plugin insert itself into the communication pipeline through Priority ordering, intercepting/modifying/replacing messages without requiring active cooperation from other plugins?

### 3.8 Logging and Observability

The framework provides a unified log event mechanism through the `ILoggable` interface (defined in `Common/Utils/ILoggable.cs`). Host internal components (`NetClient`, `ClientAuthenticator`, `UserManager`, `PhinixFrameworkClient`, etc.) implement this interface to produce log events, and the host uniformly subscribes and aggregates them at startup.

**Current plugin-side convention**: Plugins report logs through the `hostContext.Log` callback (`Action<string, LogLevel>`). This callback is injected by the host when constructing `ExtensionHostContext`, pointing to the same log outlet as the `ILoggable` events. **Current official extensions (Chat/Trade) use `hostContext.Log` and have not yet directly used `ILoggable`. They will subsequently be unified and migrated to plugin `ILoggable` support.**

No code may bypass the framework logging mechanism to write directly to the console or files.

**Log level conventions:**

| Level | Meaning | Usage Scenario |
|------|------|----------|
| `DEBUG` | Development diagnostic info | Pipeline message routing details, extension loading process, temporary debugging |
| `INFO` | Normal operational info | Connection established/disconnected, extension activation/shutdown, configuration loaded |
| `WARNING` | Recoverable anomaly | Queue overflow, message parsing failure but pipeline continues, retry success |
| `ERROR` | Failure requiring attention | Handshake packet send failure, extension activation failure, send timeout |
| `FATAL` | Service cannot continue | Critical resource exhaustion, unrecoverable state corruption |

**Log content standards:**

- All exception logs must include the `Exception` object (if `LogEventArgs` supports it), and must not log only `ex.Message`
- Network message logs **must not** record sensitive fields such as plaintext tokens, passwords, or keys; protocol packet content recording must be sanitized
- Message-level logs (such as pipeline observer `IServerMessageObserver`) should include message type identifiers for easy filtering by module

**Production minimum observability requirements:**

- Current connection count (`connectedPeers.Count`) should be periodically visible in logs or queryable via command
- Message throughput (inbound/outbound QPS) should be summarizable and loggable through `IServerMessageObserver`
- Error rate: at a minimum, distinguish "in-pipeline business errors" and "network layer transmission errors", counting them separately

**Client-side special conventions:**

- The RimWorld console swallows part of stdout output; client logs should not rely solely on `Console.Write` — they should be bridged to the host's standard log path through the `hostContext.Log` callback (current) or `ILoggable` events (future migration target)
- Debug-level logs are not output to the RimWorld console by default, to avoid overwhelming the user

**Anti-pattern**: Plugins bypassing the logging mechanism to directly `Console.WriteLine`; exception logs only writing `ex.Message` and discarding the stack trace; production log level set to DEBUG.

---

## 4. Boundary Rules

### 4.1 Reference Direction

```
Client → Common (shared contracts & abstractions) ← Server

Forbidden: Common → ../../Server/*.cs
Forbidden: Common → ../../Client/*.cs
Forbidden: Client → concrete plugin projects (ChatExtension, TradeExtension, ...)
```

### 4.2 What Can Go in Common

- Protocols (protobuf contracts, packet DTO)
- Abstractions (extension module contracts, handler contracts, API registry abstractions)
- Runtime-neutral utilities (serialization, text processing, basic logging interface)
- Infrastructure needed by both ends and not bound to a specific runtime

### 4.3 What Cannot Go in Common

- Business handlers (chat, trade, or any concrete business logic)
- Server-side state managers
- Client-side UI models
- Any client-only or server-only runtime implementations
- Host assumptions about official plugins

### 4.4 Judgment Criterion

> If moving a piece of code to one side (Client or Server) means the other side **is completely unaffected**, then it should not be in Common.

### 4.5 Common Source File Sharing Pattern (Current Transitional State)

Currently, some end-specific implementations (`ClientAuthenticator.cs`, `NetClient.cs`, `NetServer.cs`, `ClientUserManager.cs`, etc.) physically reside under the `Common/` directory, but through the `Compile Remove` / `Compile Include` linking mechanism in `.csproj`, it is ensured that **the compilation attribution is single-end** — Common's `Connections.csproj` excludes `NetClient.cs` and `NetServer.cs`, which are compiled by `Connections.Client.csproj` and `Connections.Server.csproj` respectively through path linking.

This pattern is a transitional state; physical location ≠ compilation attribution. The criterion for determining whether a file crosses the boundary is **compilation attribution**, not physical path:

- A file compiled by `Common/Authentication/Authentication.csproj` → belongs to Common
- A file only compiled by `Client/Common/Authentication.Client/Authentication.Client.csproj` → belongs to Client, regardless of where it physically resides

**Target state**: End-specific implementations are physically moved to their respective end directories, and the Common directory retains only truly runtime-neutral source files. This adjustment should be carried out after the assembly split (§5.2) is completed.

---

## 5. Directory and Assembly Conventions

### 5.1 Naming and Ordering

DLLs in `Client/Common/Assemblies/` use zero-padded numeric prefixes to ensure string order = load order:

```
01-LiteNetLib.dll         ← Third-party library
02-Protobuf.dll
03-Utils.dll               ← IPhinixExtensionModule, framework basics
04-Connections.dll
05-Authentication.dll
06-UserManagement.dll
07-ClientExtensionAbstractions.dll  ← IMainTabProvider, IServerSidebarProvider, general host services
08-ChatExtension.dll       ← Chat domain contracts
09-TradeExtension.dll      ← Trade domain contracts
10-ChatExtension.Client.dll  ← Chat plugin (depends on 03, 07, 08)
11-TradeExtension.Client.dll ← Trade plugin (depends on 03, 07, 09)
```

When adding new DLLs, assign numbers according to dependency relationships. RimWorld's `ModAssemblyHandler` will only avoid throwing `ReflectionTypeLoadException` if and only if the string order guarantees that all dependencies are loaded before their dependents.

### 5.2 Release Boundaries (Target State)

Currently, official plugin DLLs and framework base DLLs are still mixed together in `Assemblies/`. Final target:

```
Client/
  Common/
    Extensions/          ← Plugin DLLs in independent directory (RimWorld does not touch it)
      ChatExtension.Client.dll
      TradeExtension.Client.dll
      SomeSubMod.dll
    Assemblies/          ← Only framework base DLLs (01-07)

Server/
  Extensions/            ← Server-side plugin DLLs in independent directory
    ChatExtension.Server.dll
    TradeExtension.Server.dll
```

The prerequisite for this adjustment is that `ExtensionAssemblyLoader` can load from this directory and `PhinixExtensionRegistry` can discover from it — the mechanism is already in place; only the directory structure and build chain need alignment.

### 5.3 Versioning and API Compatibility

**Unified assembly versioning:**

All assembly version numbers are centrally managed through `Directory.Build.props`, not scattered across individual `AssemblyInfo.cs` files for manual maintenance. Currently, project versions are inconsistent and need to be aligned to a unified version.

**Git tags and releases:**

- Semantic versioning (Semver): `MAJOR.MINOR.PATCH`
  - `MAJOR`: Breaking API changes (e.g., interface removal, method signature change)
  - `MINOR`: Backward-compatible additions (new interfaces, new methods, new plugin mount points)
  - `PATCH`: Pure fixes, no public API changes
- Each release gets a Git tag `v<semver>`, triggering Docker image build and release

**Protobuf message compatibility:**

- Field numbers are **permanent and immutable**. Deleted fields must use `reserved` to reserve the number and name, preventing future reuse
- New fields can only be appended, not inserted between existing fields
- Enum values must not be deleted or renumbered — deprecated values should be marked `[Obsolete]` or commented, but retain their numeric values
- When changing message type semantics, a new message type must be created (e.g., `LoginRequestV2`), with the old type retained as legacy compatibility

**API deprecation lifecycle:**

Aligned with the §6 Host/Core incremental update rules, `[Obsolete]` marking follows this cadence:

```
Mark [Obsolete] → retain for at least 1 MINOR version → remove after confirming no downstream references
```

- Deprecated interfaces must indicate the alternative in XML documentation comments (`<summary>Use IXxx instead.</summary>`)
- Before removal, search the entire repository and known third-party submods to confirm no remaining references
- Expired deprecations can be cleaned up centrally during `MAJOR` version upgrades

---

## 6. Incremental Migration Principles

- **Each phase must remain compilable, runnable, and verifiable**. No "one-shot mass migration".
- **Close boundaries first, then do runtime migration, and finally complete plugin-ization**.
- **Refactoring does not change behavior**. Every change in Phase 5 (type movement, reference adjustment) only changes code attribution, not runtime behavior. If behavior changes, it is a bug.
- **Directory closure precedes assembly splitting**. Physical directories can be cleaned up first; assembly boundaries can be gradually tightened in subsequent phases.
- **New code follows new rules; old code migrates gradually**. New features must meet the boundary requirements of the current phase; existing code migrates out gradually by priority.
- **Host and Core only receive incremental updates**. All changes to Host and Core must be incremental — no deletion or removal of any existing public interfaces. If breaking changes are needed, retain the original interface and mark it `[Obsolete]` (or comment `// outdated`); internal implementation can be rewritten but external behavior must remain consistent. This rule ensures that downstream plugins and third-party submods do not experience compilation failures or runtime breakage due to framework upgrades.

---

## 7. Commit and Review Checklist

When adding or modifying code, check the following:

- [ ] Has the host project added a new reference to a plugin?
- [ ] Has Common added client-only or server-only code? If such addition is necessary, is the compilation attribution only through `.csproj` linking, rather than directly compiled into the Common assembly?
- [ ] Is any concrete business type hardcoded (e.g., directly using `IClientChatService`) instead of through a general interface (e.g., `IMainTabProvider`)?
- [ ] Does the new extension entry connect through existing general mount points (Tab/sidebar/badge/message handling), or was a new dedicated opening created in the host?
- [ ] Is inter-plugin interaction completed directly between plugins, rather than relayed through the host?
- [ ] Is the DLL load order correct under string ordering?
- [ ] Does the new network handling/message parsing have try-catch isolation? Would a single message failure interrupt the entire pipeline?
- [ ] Does the new `IDisposable` resource holder implement `IDisposable`? Are event subscriptions paired with unsubscriptions in `Shutdown`?
- [ ] Does the new queue/buffer have a capacity limit? Is the scenario of producers faster than consumers handled?
- [ ] Are changes to Host/Core incremental? Has any existing public interface been deleted or removed? If breaking changes are needed, has the original interface been retained and marked `[Obsolete]`?
- [ ] Are log calls reported through the `ILoggable` interface, rather than bypassing the framework to write directly to the console/file? Do exception logs include the complete `Exception` object?
- [ ] Has a new public API been added? If so, does the version number need upgrading to `MINOR`? If modified/removed, has it been marked `[Obsolete]` and the alternative indicated in documentation?
- [ ] Are Protobuf field changes compatible — no reuse of deleted field numbers, no modification of enum values, and have breaking changes created new message types?

---

## 8. Pre-Release Performance and Stability Review

Before each milestone release, review item by item against the following categories. Review pass standard: **All CRITICAL items cleared, HIGH items have a clear disposition plan**.

### 8.1 Memory Leaks

**Review points:**

- Are all `IDisposable` resource-holding classes released in `Shutdown` / `Dispose`? Check objects: `Timer`, `NetManager`, `Thread`, `FileStream`, `StreamReader`/`StreamWriter`
- Do event subscriptions have paired unsubscriptions (`+=` ↔ `-=`)? Relying on process exit as the sole cleanup mechanism is considered a failure
- When removing elements from collections such as dictionaries/lists, do the keys match? (e.g., session dictionary uses connectionId as key but Remove uses sessionId)

**Inspection method:** Start server → connect 5 clients → disconnect → repeat 3 rounds → check if memory baseline continuously rises. Same for client: enter server → switch Tabs → exit → repeat.

**Anti-pattern**: Relying on timeout timers as a fallback to clean up resources that should be cleaned up immediately upon event triggering.

### 8.2 Error Handling

**Review points:**

- Network layer: Are reads/writes to `connectedPeers` / `probePeers` lock-protected? Are callbacks triggered by background threads marshalled to the main thread?
- Pipeline layer: Are individual interceptor/handler exceptions caught by PipelineRunner? Does the pipeline continue processing the next candidate after an exception?
- Parsing layer: Do Protobuf `ParseFrom`, `Unpack` and other deserialization operations have try-catch? Are malicious or corrupted messages only skipped without interrupting the pipeline?
- Critical handshake packets: Does a Hello / Auth / Login / SessionExtend response send failure trigger connection disconnect and resource cleanup?
- Client disconnection: Do messages sent when not connected trigger Error-level logging or callback notification, rather than silent discard?
- Exception granularity: Have catch-all (bare `catch` or no exception type filter) been replaced with catching specific exception types? Has `catch` as flow control been eliminated?

**Inspection method:** Send corrupted protobuf packets to the server, confirming the pipeline continues processing subsequent normal messages. Force-disconnect the client network, confirming the server cleans up session and user state after timeout.

### 8.3 UI Rendering Performance

**Review points:**

- Per-frame allocation: Are there `new` objects on the `DoWindowContents` / `Draw` / `DoButton` paths? (Regex, TextWidget, GUIContent, List, HeightContainer, etc.)
- Regex: Are all Regex used for rich text tag stripping `static readonly` precompiled instances? (`RegexOptions.Compiled`)
- Layout caching: Is `CalcHeight` / `CalcWidth` computed once on data change and cached, rather than recomputed every frame on the Draw path?
- LINQ allocations: Are there LINQ calls on the Draw path that allocate enumerators, such as `.Where()` `.Sum()` `.Select()` `.ToList()` `.Count()`?
- Property getters: Are property getters called every frame (e.g., `BadgeText`) and performing computation or allocating new objects each time? Should they be changed to push-style cached updates?
- Scroll lists: Do large lists (chat messages, trade items) traverse all entries every frame to compute layout? Should dirty flags be introduced to skip recomputation on no change?

**Inspection method:** RimWorld developer mode → open Performance Profiler → enter server Tab → send 100 chat messages → scroll up and down → check GC.Alloc and frame time. Per-frame GC allocation should be near zero.

### 8.4 Thread Safety

**Review points:**

- Are reads/writes to shared collections (Dictionary, List) protected by `lock` or using `ConcurrentDictionary` / `ConcurrentQueue`?
- When network callbacks (`OnPeerConnected`, `OnNetworkReceive`, etc.) fire on the poll thread, is code operating on UI or shared state marshalled to the main thread?
- Is there a TOCTOU race between the `Connected` check and `Send`? Is `TrySend` used instead of `Send`?

**Inspection method:** High-concurrency connection test (10 clients simultaneously connecting + sending messages) → check for no `InvalidOperationException` (collection modified), no crashes.

### 8.5 Review Checklist

| Category | Check Item | Severity |
|------|--------|--------|
| Memory | `IDisposable` resource holders implement `IDisposable` | CRITICAL |
| Memory | Event subscriptions have paired unsubscriptions (`-=`) | HIGH |
| Memory | Session/Peer removal key matches | CRITICAL |
| Error Handling | Shared collections have lock protection | CRITICAL |
| Error Handling | Protobuf parsing has try-catch isolation | HIGH |
| Error Handling | Handshake packet send failure triggers connection disconnect | HIGH |
| Error Handling | Offline send triggers observable alert (not silent discard) | HIGH |
| UI | Regex is `static readonly` precompiled | CRITICAL |
| UI | Draw path has no `new` object allocation | HIGH |
| UI | Layout calculation is cached (dirty flag), not recomputed every frame | HIGH |
| UI | Draw path has no LINQ allocations | HIGH |
| UI | Property getters do not perform real-time queries/allocations | MEDIUM |
| Threading | Network callbacks marshalled to main thread | HIGH |
| Threading | Unbounded queues have capacity limits | HIGH |
| Threading | `TrySend` / TOCTOU protection | MEDIUM |
