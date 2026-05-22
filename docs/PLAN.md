# Phinix Rework Plan: High-Freedom Framework with Legacy Client Compatibility

## Summary
Rework Phinix into a RimWorld-first extension framework built around a new `v2` protocol, with Phinix’s current chat/trade UX retained only as the default implementation. The framework should expose a message pipeline and an item pipeline that submods can hook into without forking core behavior. The first release should keep the dedicated external server model, keep **new client -> old server** compatibility through a legacy adapter, and treat **new server -> old client** compatibility as optional and non-blocking.

Design constraints from RimWorld modding:
- Prefer Harmony-friendly, low-intrusion integration and keep game-side patch surface small.
- Favor a RimWorld-style architecture where XML remains optional metadata/config, but complex extensibility lives in C#.
- Use reflection-based discovery for submods so addon authors do not need explicit bootstrap wiring.
Sources:
- https://rimworldwiki.com/wiki/Modding_Tutorials
- https://rimworldwiki.com/wiki/Modding_Tutorials/Harmony
- https://rimworldwiki.com/wiki/Modding_Tutorials/Compatibility

## Key Changes
- Introduce a layered architecture: `transport/protocol`, `session/user context`, `message pipeline`, `item pipeline`, `default UI handlers`, `legacy compatibility adapters`.
- Define a new `v2` wire protocol with explicit message envelope/version/type metadata instead of overloading the current chat/trade packet shapes.
- Keep the old protocol as a separate legacy adapter layer used only when talking to old servers; on connect, detect legacy capability, downgrade automatically, and surface a client-visible “legacy mode” notice.
- Split current monolith responsibilities so packet routers only decode/route, while services own business logic and stores own persistence.
- Replace the current hardwired chat behavior with a message pipeline:
  - inbound validation
  - session/user resolution
  - message classification
  - interceptor/filter chain
  - persistence decision
  - fanout/broadcast decision
  - client-side render dispatch
- Replace the fixed `ProtoThing`-only trade path with an item pipeline:
  - domain item abstraction
  - codec/serializer registry
  - transfer payload builder
  - receive/realize handler
  - fallback handler for unknown payloads
- Keep Phinix’s existing chat/trade UI as the built-in default renderer/handler set, but make it fully replaceable:
  - message display can be suppressed, rerouted, or custom-rendered by submods
  - trade completion behavior can be overridden or extended before default drop-pod/letter behavior runs
- Add reflection-based extension discovery:
  - scan loaded assemblies for Phinix extension attributes/interfaces
  - auto-register message types, handlers, renderers, item codecs, and capability declarations
  - fail soft on bad extensions so one broken submod does not break the framework
- Add a capability/negotiation model between client and server so extensions can declare what they support before using custom message or item flows.
- Treat legacy chat/trade as built-in adapters on top of the new core, not as the architecture foundation.

## Public APIs / Extension Interfaces
- Add a framework bootstrap surface such as:
  - `IPhinixExtension`
  - `PhinixExtensionAttribute`
  - `IMessageTypeHandler`
  - `IMessageInterceptor`
  - `IMessageRenderer`
  - `IItemCodec`
  - `ITradeCompletionHandler`
  - `ICapabilityProvider`
- Add a shared message envelope abstraction that includes at minimum:
  - protocol version
  - logical message type
  - sender/session context
  - payload bytes or structured payload
  - metadata bag for extension use
- Add a client/server capability registry so extensions can check remote support before sending custom traffic.
- Add explicit handler result contracts so extensions can:
  - continue default processing
  - replace payload
  - suppress default UI
  - stop propagation
  - mark unsupported and fall back
- Keep XML involvement limited to optional metadata/config for discovered extensions; do not require XML registration for core extension wiring.

## Test Plan
- Legacy compatibility:
  - new client connects to old server and basic chat/trade still works
  - legacy mode is detected automatically and user is informed
  - custom `v2` extension features are disabled cleanly in legacy mode
- Message pipeline:
  - default text chat still renders with no extensions installed
  - blocked/intercepted messages do not force default display
  - a custom message handler can register a new message type and render it without editing core
  - multiple handlers/interceptors compose deterministically
- Item pipeline:
  - existing normal item trades still serialize and deserialize correctly
  - unknown custom payloads fail soft and surface fallback behavior
  - a custom codec can send and receive an extension-defined item payload
- Reflection discovery:
  - valid extension assemblies auto-register on startup
  - duplicate type IDs or capability IDs are rejected with clear logs
  - a broken extension does not crash the whole client/server bootstrap
- Default UI override behavior:
  - built-in chat UI can be suppressed by an extension
  - built-in trade completion letter/drop behavior can be replaced or augmented
- Stability:
  - disconnect/reconnect preserves expected store state
  - persistence still works for core message/trade state
  - extension negotiation failure does not break base login/session flow

## Assumptions and Defaults
- Dedicated external server remains the only supported topology in phase 1.
- The first framework release prioritizes **new client compatibility with old servers**; **new server compatibility with old clients** is optional and may be omitted if it distorts the architecture.
- Reflection-based discovery is the primary extension model; explicit registration APIs may still exist internally, but submod authors should not need manual bootstrap calls.
- Phinix remains a product with built-in chat/trade UX, but that UX is now just the default extension set.
- Phase 1 should focus on architecture and extension seams, not on shipping many new gameplay features; a red-packet submod should be possible after this refactor without further core rewrites.
