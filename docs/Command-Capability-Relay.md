# Command Capability Relay

> **中文版**：[命令能力Relay.md](./命令能力Relay.md)

Command Capability Relay provides a shared service for client plugins to communicate through the Framework Server. The server selects eligible receiving connections based on explicit capability declarations, while business logic is handled by the target plugins.

## Processing Modes & Lifecycle

When `CommandRoute.Mode` is `Server`, the server executes business logic; when `Relay`, target client plugins execute it. Legacy packets without `CommandRoute` default to `Server`. Authoritative Trade operations and Chat history queries continue using existing server handlers.

Relay passes through authentication, rate/resource limits, Command Interceptor, final packet validation, capability Router, Outbound Interceptor, pre-send eligibility checks, delivery, and Observer. Server plugins may match on `CommandRoute.PluginId`, `CommandType`, `Version`, and target player (`Continue`, `Replace`, `Blocked`, or `Handled`; mode switching is rejected). Server processing errors abort the request; Observer exceptions are isolated.

Observers access routing results via `ServerFrameworkContext.CommandRelayResult`. Outbound Interceptors may narrow the recipient set; final packets must satisfy authorization, receiver capabilities, and the reserved size budget.

## Declaring & Receiving Capabilities

Plugins obtain `IClientCommandCapabilities` from `ExtensionHostContext`, using the module's `ExtensionId` as `ownerId` and disposing the declaration handle on shutdown (declarations are also cleaned up on module stop, activation failure, or registration rollback).

```csharp
private IDisposable declaration;

public void Activate(ExtensionHostContext host)
{
    declaration = host.GetRequiredService<IClientCommandCapabilities>()
        .Declare(ExtensionId, "author.research", "request", version: 1);
}

public void Shutdown(ExtensionHostContext host)
{
    declaration?.Dispose();
    declaration = null;
}
```

Handlers register via `builder.AddClientCommandHandler` and match incoming packets independently:

```csharp
public bool CanHandleIncomingCommand(FrameworkPacket packet)
{
    var route = packet.CommandRoute;
    return route?.Mode == CommandProcessingMode.Relay &&
        route.PluginId == "author.research" &&
        route.CommandType == "request" && route.Version == 1;
}
```

`PluginId` and `CommandType` use lowercase ASCII letters, digits, `.`, `_`, and `-`, starting with an alphanumeric character (max 128 chars each). `Version` is a positive integer with exact matching. `core`, `builtin`, `framework`, and their dotted namespaces are reserved. `PluginId` carries no authentication authority. Multiple handles for the same capability are tracked independently; the capability is withdrawn when the last handle is disposed.

## Sending & Results

```csharp
var relay = host.GetRequiredService<IClientCommandRelay>();
string payload = FrameworkSerialization.SerializePayload(request);

// Player unicast delivers once to each eligible connection of that player.
RelayResult direct = await relay.SendRelay(
    "author.research", "request", payload,
    targetPlayerUuid: targetUuid, version: 1);

// Omitting target broadcasts by capability, excluding the sender connection by default.
RelayResult broadcast = await relay.SendRelay(
    "author.research", "request", payload);
```

Each `CommandType` must be explicitly declared by receivers. `RelayResult.Status` reports `Submitted` (network layer accepted delivery set), `Handled` (server plugin consumed request), `Partial`, or `Unknown` (timeout, send exception, or disconnect); `Deliveries` records per-connection `Submitted`, `NotSent`, or `Unknown`. Zero-match broadcasts return `NoRecipients`; unicast returns `TargetOffline`, `UnsupportedRelay`, or `NoCapability` when applicable. Other errors include `InvalidCommand`, `InvalidCapability`, `NotAuthenticated`, `Forbidden`, `PayloadTooLarge`, `RateLimited`, `CapabilitiesNotSynced`, `Blocked`, `ProcessingFailed`, `DeliveryFailed`, and local `NotSent`.

Business acknowledgements, timeouts, and idempotency are owned by plugins; Framework does not auto-resend `Submitted` or `Unknown` commands. Task continuations run asynchronously—use `IClientMainThreadDispatcher` before touching game state.

## Capability Synchronization

Hello negotiates `core.command-relay.v1` and returns the connection's `relay_epoch`. The client sends full declaration snapshots with `epoch` and `revision`; the server validates and atomically replaces its index before ACKing.

Same `revision` + same content is idempotent; same `revision` + conflicting content is rejected; older revisions never overwrite newer state. Changes are coalesced over a 500 ms window, with exponential retry backoff up to 10 s on failure. `IClientCommandRelay.SyncState` exposes `Disconnected`, `Pending`, `Synced`, and `Failed` (`SyncError` gives the reason). Calling `SendRelay` before the first snapshot ACK returns `CapabilitiesNotSynced`.

## Budgets & Authorization

| Item | Current Limit |
| :--- | :--- |
| Capabilities per connection | Max 256 items (max 4,096 local handles) |
| Local capability payload | ~60 KiB (reserving envelope headroom) |
| Snapshot & Relay envelope | Max 64 KiB encoded |
| Snapshot control rate | 4/sec per connection, burst 4 |
| Snapshot replacement interval | Min 250 ms |
| Relay requests | 10/sec per connection, burst 20 |
| Broadcast recipients | Max 128 connections |
| Sender fanout | 1 MiB/sec, burst 2 MiB |
| Global fanout | 8 MiB/sec, burst 16 MiB |
| Pending client requests | Max 64, 15 s timeout |
| UDP Relay queuing | 2,048 packets per receiver, 8,192 global watermark |

Server composition roots may register `RelayLimits` and `ICommandRelayAuthorization` before creating the Framework. By default, authenticated players may unicast and broadcast within budgets; sender identity is always taken from the authenticated `Ready` connection.

## Protocol Compatibility & Security

| Combination | Behavior |
| :--- | :--- |
| New client + New server | Negotiates Relay and syncs plugin capabilities |
| Old client + New server | Uses existing protocols; Relay excluded |
| New client + Old server | Uses existing protocols; `SendRelay` returns `UnsupportedRelay` |
| Old client + Old server | Uses existing protocols |

The outer envelope uses `DataContractJsonSerializer` with an optional `CommandRoute` field (defaulting to `Server`). Both client (`PinnedClientUdpReceiveGuard`) and server share `Common/Connections/PinnedUdpReceiveBudget.cs` for pre-reassembly UDP fragment/coalesced packet protection. Both sides pin Common `911047cf8a6dd34e661e107e182e9c921b8e9586`; see [R5 Final Verification](branch-local/dev/roadmaps/Command-Capability-Relay-R5-Final-Verification.md).

## Sample & Diagnostics

A compilable standalone sample is in [Command Relay Sample](../Samples/CommandRelay/README.md). Client, shared contracts, and plugins must match as a set—protocol negotiation cannot fix missing CLR types at load time.

| Symptom | Cause & Action |
| :--- | :--- |
| `Could not load type 'Utils.Framework.RelayResult'` | Loaded Host/Common lacks Relay contracts; replace the full client and remove duplicate installs |
| `UnsupportedRelay` | Network session did not negotiate Relay; check both endpoints |
| `CapabilitiesNotSynced` / `Pending` / `Failed` | Check connection, `SyncError`, and snapshot ACK |
| `NoRecipients` / `NoCapability` | Check receiver's exact capability declaration, version, and `Ready` state |
| `Example Relay handler fault injection.` | Intentional test exception from Example fault-injection mode |
| Legacy `RedPacketRelay` HTTP poll timeout | RedPacket's own HTTP relay, unrelated to Command Capability Relay |
