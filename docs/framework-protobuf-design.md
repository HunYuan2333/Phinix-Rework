# Phinix Framework Protobuf Design Draft

## Summary
This document records the current intended direction for the next framework evolution after the JSON-based prototype.

The key decision is:

- the framework should converge on **three first-class flows**
- those flows should become the real protocol backbone
- existing chat/trade-specific protocol design should no longer define the architecture

The three framework flows are:

- `message`: display flow
- `command`: control flow
- `item`: item payload flow

In this model, chat and trade are no longer protocol root domains. They become built-in features implemented on top of these flows.

## Why Change Direction
The current repository already uses Protobuf heavily for the legacy core protocol:

- authentication
- chat
- trading
- user management

However, the current framework prototype is separate from that system:

- `FrameworkPacket`
- JSON/DataContract payloads
- framework-specific message handling and rendering

This worked well for prototyping, but it creates a split architecture:

- the legacy system is Protobuf-first
- the new framework prototype is JSON-first

That split is acceptable for early validation, but not ideal as the long-term direction.

The deeper issue is not "JSON vs Protobuf" by itself.
The deeper issue is that the old protocol layout is too tightly organized around old built-in business domains such as chat and trade.

The new framework should instead be organized around reusable extension flows.

## Core Direction
The framework should explicitly follow the **Open/Closed Principle**:

- open for extension
- closed for modification

In practice, this means new gameplay features and submods should be added by implementing framework extension points, handlers, codecs, renderers, and future command handlers.
They should not require repeated edits to framework core behavior, protocol roots, or built-in feature internals.

### 1. `message` Flow
`message` is the **display flow**.

Responsibilities:

- user-visible communication
- chat-like messages
- system notices meant for display
- renderer-driven output

Rules:

- `message` may produce display output
- `message` is the only flow that should normally go through renderer-style presentation
- future message payloads should be protobuf-defined rather than JSON payload blobs

### 2. `command` Flow
`command` is the **control flow**.

Responsibilities:

- client requests
- server decisions
- acknowledgements
- state synchronization
- private updates not intended for direct player display

Rules:

- `command` does not go through renderers
- `command` updates local state, feature stores, or UI state machines
- command handlers may cause follow-up display messages, but command payloads themselves are not display content

### 3. `item` Flow
`item` is the **item payload flow**.

Responsibilities:

- encoding and decoding transferable item payloads
- supporting trade and future item-centric systems
- decoupling payload shape from any single gameplay feature

Rules:

- `item` is not owned by trade
- trade is just the first built-in consumer of `item`
- future features such as attachments, delivery, market listings, mail, auctions, or storage sync should be able to reuse the same item flow
- `item` should keep a codec-oriented extension model rather than being forced into the exact same payload shape as `message` and `command`

### Item Data Model Direction
The old `Trading.ProtoThing` design is too tightly coupled to trade as a business domain, but its core data shape is still useful.

The following fields have already proven to be the practical minimum set needed to reconstruct a RimWorld item safely in the current codebase:

- `def_name`
- `stack_count`
- `stuff_def_name`
- `quality`
- `hit_points`
- nested inner item data for minified things

These fields should be preserved conceptually in the new `item` flow, but they should no longer live under `Trading` or be defined as a trade-owned payload type.

Instead, the new framework should define an official built-in item payload shape representing the default vanilla RimWorld item model.

Recommended interpretation:

- keep the old `ProtoThing` data model as the conceptual prototype
- rename and relocate it as a framework-owned item payload
- treat it as the payload for the built-in vanilla item codec
- do not let trade continue to define the item schema root

Recommended naming direction for the built-in payload:

- `FrameworkVanillaItemData`

This name makes it clear that:

- it belongs to the framework
- it is a default built-in item representation
- it does not claim to be the only valid item payload shape for all future extensions

### Item Codec Strategy
For the near and medium term, `item` should keep a codec-specialized structure.

Reason:

- item payloads are deeply tied to RimWorld runtime concepts
- current conversion logic depends on item defs, stuff defs, quality restoration, minified inner items, and safe fallback behavior
- forcing `item` into a completely identical shape to `message` and `command` would reduce clarity without reducing real complexity

Therefore the recommended direction is:

- framework item header
- `codec_id`
- payload bytes
- metadata
- built-in vanilla item codec
- future custom item codecs for submods

### Item Collection Semantics
The old protocol does not only move single item payloads.
It also repeatedly transports collections of item payloads and maps of item collections.

That means the new `item` lane should explicitly support both:

- a single item payload
- an item collection payload

This does not mean trade should continue to own item collection semantics.
It means the framework-level item lane should acknowledge that "a transferable set of items" is a real reusable concept.

Recommended interpretation:

- keep the core item payload focused on a single item
- allow framework-level collection wrappers for repeated item payloads
- do not force every built-in feature or submod to reinvent item list containers independently

### Unknown Item Fallback
Unknown item fallback should be treated as a first-class part of item-flow design, not just as an implementation detail.

Rules:

- if a payload cannot be decoded by any registered codec, decoding may fall back to an unknown item representation
- fallback behavior should preserve as much identifying information as possible
- the framework should prefer preserving readable identity data over failing hard during item reconstruction

### Explicit Quality and Missing-Data Semantics
The old protocol treats `quality` as an explicit semantic field, including an explicit `none` state rather than an omitted value.

That behavior should be preserved conceptually in the new built-in vanilla item model.

Recommended interpretation:

- `quality` should remain an explicit value in the built-in vanilla item payload
- "item has no quality" should be represented as an explicit semantic state, not as accidental field absence
- similar care should be taken with reconstructible fields such as `stuff_def_name` and nested inner item data

### What Does Not Belong To `item`
Several old trade packet fields travel alongside item payloads, but they are not part of the item lane itself.

These include:

- request/response correlation tokens
- success/failure flags
- failure reasons and failure messages
- trade-side ownership views such as "our items" vs "other party items"
- pending notification and completed-trade lifecycle state

These must not be pulled into the new `item` lane schema.

They belong to:

- `command`
- built-in trade feature state
- persistence models above the raw item payload layer

This preserves one of the most valuable practical lessons from the legacy implementation while still allowing the new framework to own the item lane cleanly.

## Architectural Consequence
Chat and trade should no longer be treated as protocol roots.

Instead:

- chat should become a built-in feature primarily implemented on top of `message`
- trade should become a built-in feature implemented on top of `command + item`

This is a deliberate architectural inversion of the old system.

Old direction:

- chat protocol
- trade protocol
- users/auth protocol

New direction:

- framework flow protocol
- built-in features implemented on top of that protocol

## Legacy Module Mapping
The old top-level modules should not be copied one-for-one into the new framework model.

Doing that would create a wasteful migration path:

- first rewrite old module boundaries into protobuf again
- then refactor those rewritten boundaries into `message`, `command`, and `item`

That would produce a large amount of short-lived code and schema churn.

The preferred direction is:

- write new framework-facing protocol and runtime pieces directly in the three-flow model
- keep adapters only where legacy compatibility is temporarily required
- avoid introducing new "chat-owned", "trade-owned", or "user-management-owned" protocol roots in the new framework layer

Recommended mapping:

- `Authentication`
  - remains outside the three business flows for now
  - acts as transport/session/bootstrap context
  - provides session and identity context used by framework headers
- `Chat`
  - primarily becomes a consumer of `message`
  - secondarily uses `command` for history sync, state sync, limits, and control actions
- `Trading`
  - primarily becomes a consumer of `command + item`
  - may emit follow-up `message` notifications when user-visible output is needed
- `UserManagement`
  - primarily becomes a consumer of `command`
  - may emit follow-up `message` output only for explicitly visible notices

### Authentication Position
`Authentication` should not be forced into `message`, `command`, or `item`.

Reason:

- it is a prerequisite transport/session concern
- it establishes identity, session, and capability context
- it is not a feature flow in the same sense as display, control, or item transport

So the recommended architecture is:

- authentication completes first
- framework flow traffic starts after authenticated context exists
- `session_id`, `sender_uuid`, and future capability metadata derive from that context

This keeps the three-flow model clean instead of turning it into a catch-all for every protocol concern.

## Flow Pipelines
The three-flow model should not remain only a schema split.
Each flow should also have a corresponding pipeline shape so new code can be written directly against stable insertion points.

This is important for avoiding throwaway migration work.

If the framework only defines three packet types but not three pipeline lifecycles, implementers will tend to recreate old module-specific logic around them.

### `message` Pipeline
Recommended stages:

- transport decode
- header validation
- payload decode by `type_id`
- client/server message handler dispatch
- optional message transformation
- renderer selection
- display message creation
- interceptor/suppression
- final UI delivery

Purpose:

- make `message` the authoritative display lane
- keep visible output logic concentrated in one predictable path

### `command` Pipeline
Recommended stages:

- transport decode
- header validation
- command kind routing
- payload decode by `type_id`
- authorization and guard checks
- client/server command handler dispatch
- local state/store update
- optional follow-up message emission

Purpose:

- make `command` the authoritative control lane
- absorb state sync, request/response, and private update behavior without leaking display assumptions into it

### `item` Pipeline
Recommended stages:

- transport decode
- header validation
- codec resolution by `codec_id`
- payload decode
- fallback to unknown item when necessary
- single-item or collection handling
- consumer-specific delivery

Purpose:

- make `item` the authoritative transferable item lane
- prevent trade from continuing to own item transport semantics

### Why Pipelines Matter
Defining these pipelines now reduces the risk of writing large amounts of temporary migration code.

The desired implementation strategy is:

- new framework code should target the new flow pipelines directly
- old systems may be bridged into those pipelines through adapters
- migration code should converge into permanent flow insertion points instead of creating a second temporary architecture

## Protobuf Direction
The long-term direction is to make the framework itself **Protobuf-first**.

This means:

- `message` should eventually move from JSON/DataContract payloads to protobuf-defined structures
- `command` should be introduced directly as protobuf-defined structures
- `item` should remain a first-class payload lane with protobuf-friendly contracts

The intent is not to copy the old legacy `.proto` design into the new framework.
Instead, the framework should define a new protocol shape with reusable flow-oriented boundaries.

## Compatibility Assessment
If this redesign is followed through completely, compatibility with legacy Phinix will be minimal.

More precisely:

- compatibility with the current JSON-based framework prototype will be weak or transitional only
- compatibility with legacy chat/trade protocol behavior will likely be partial at best
- long-term protocol-level compatibility with legacy Phinix should not be treated as a primary design goal

This is because the breaking change is not just the serialization format.
The breaking change is the protocol model itself:

- old model: business-domain-first
- new model: flow-first framework protocol

## Adapter Strategy
An adapter layer is allowed and recommended as a migration tool, but it must not become the new architecture.

### Adapter Role
Adapters should exist at the system boundary:

- legacy chat packets -> framework `message`
- legacy trade packets -> framework `command`
- legacy trade item payloads -> framework `item`

This allows the new framework core to be exercised without forcing an immediate full deletion of legacy behavior.

### Adapter Rules
Adapters are migration tools, not permanent framework primitives.

Rules:

- new features should target `message`, `command`, and `item` directly
- new submods must not be built on legacy packet structures
- adapters may translate old packets into new internal flows
- the framework core should not absorb legacy-specific branching as a permanent design constraint
- adapters must be removable without breaking the new framework architecture
- deleting adapters in the future should only remove legacy compatibility behavior, not invalidate the structure of `message`, `command`, `item`, or their extension contracts

### Adapter Positioning
The correct shape is:

- new framework core = authoritative
- legacy packet formats = external input formats
- adapters = translation layer at the boundary

If an adapter cannot be deleted cleanly later, it is too deep in the architecture and has been designed incorrectly.

The incorrect shape is:

- framework core permanently carrying legacy packet semantics everywhere

## Recommended Migration Order
1. Finalize the framework-level protobuf design for `message`, `command`, and `item`.
2. Finalize the flow-pipeline model so implementation targets stable long-term insertion points.
3. Migrate the current framework `message` prototype away from JSON payloads.
4. Introduce `command` as a parallel protobuf-first control channel.
5. Rework built-in chat as a consumer of `message + command`.
6. Rework built-in trade as a consumer of `command + item`.
7. Rework user-management-style built-in state sync as a consumer of `command`.
8. Keep legacy adapters only as long as they are needed for staged migration.

## Practical Interpretation For Phase 4
Phase 4 should no longer be interpreted only as "legacy fallback plus sample submod verification".

It should now be interpreted as:

- finishing the framework boundary cleanup
- deciding the formal protocol direction
- preparing the migration from prototype transport to proper framework protobuf flows

Sample submods such as red packet remain useful, but they are validation cases for the framework, not the thing that defines the protocol model.

## Current Naming Direction
At the framework level, the preferred conceptual naming is:

- `message`
- `command`
- `item`

This naming is intentionally generic and framework-oriented.
It is not tied to red packet, trade, or any single built-in feature.

## Status
This is a design draft, not an implementation-complete spec.

It exists to lock the architectural direction before the next large implementation pass.
