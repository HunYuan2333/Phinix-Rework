# Phinix Framework Protobuf Schema Draft

## Summary
This document defines the intended schema direction for the framework's three first-class protobuf flows:

- `message`
- `command`
- `item`

It is a schema draft for implementation guidance.
It is not yet the final `.proto` source.

The goal is to make the framework protocol:

- flow-first instead of feature-first
- open for extension
- closed for modification
- independent from legacy chat/trade packet structure

## Shared Header Model
All three flows should share the same conceptual header model even if the generated protobuf messages are separate concrete types.

The shared framework header should contain:

- `framework_version`
- `flow`
- `type_id`
- `packet_id`
- `session_id`
- `sender_uuid`
- `timestamp`
- `metadata`

### Shared Header Semantics
`framework_version`

- identifies the framework protocol version
- should be explicit rather than inferred from assembly version

`flow`

- identifies which first-class framework lane is carrying the payload
- valid values are conceptually:
  - `message`
  - `command`
  - `item`

`type_id`

- identifies the payload contract inside the current flow
- is the primary extension dispatch key
- must remain string-based to support extension-oriented registration and the open/closed principle

`packet_id`

- uniquely identifies a transmitted framework unit
- is used for tracing, correlation, deduplication, and later debugging

`session_id`

- carries authenticated session context when required by the surrounding connection model

`sender_uuid`

- identifies the logical sender inside the Phinix user model

`timestamp`

- records when the packet was created
- should remain explicit and framework-visible rather than hidden inside payload-specific schemas

`metadata`

- is reserved for framework-level or extension-level auxiliary key/value annotations
- must not replace structured payload design for primary business data

### Shared Header Representation
The preferred schema direction is:

- define a reusable protobuf message for metadata entries
- define a reusable protobuf message for the common framework header
- each flow message should embed that common header rather than duplicating every field manually

This keeps the schema readable while still leaving each flow free to define its own body.

### Suggested Proto-Level Shape
At the proto authoring level, the shared part should be represented by small reusable messages rather than duplicated flat fields.

Recommended building blocks:

- `FrameworkMetadataEntry`
- `FrameworkFlow`
- `FrameworkHeader`

Recommended conceptual shape:

```proto
message FrameworkMetadataEntry {
  string key = 1;
  string value = 2;
}

enum FrameworkFlow {
  FRAMEWORK_FLOW_UNSPECIFIED = 0;
  FRAMEWORK_FLOW_MESSAGE = 1;
  FRAMEWORK_FLOW_COMMAND = 2;
  FRAMEWORK_FLOW_ITEM = 3;
}

message FrameworkHeader {
  uint32 framework_version = 1;
  FrameworkFlow flow = 2;
  string type_id = 3;
  string packet_id = 4;
  string session_id = 5;
  string sender_uuid = 6;
  google.protobuf.Timestamp timestamp = 7;
  repeated FrameworkMetadataEntry metadata = 8;
}
```

Notes:

- `framework_version` should remain explicit
- `packet_id` should remain a framework-level identifier even when higher-level business ids also exist
- `type_id` is still the primary extension dispatch key
- `metadata` is additive and optional, never the primary payload carrier

## `message` Flow Schema
`message` is the display flow.
It exists only for player-visible or UI-visible content.

### Responsibilities
- visible chat-like content
- visible system notices
- visible extension-rendered messages

### Non-Responsibilities
- request/response control logic
- state synchronization
- private control data
- trade lifecycle control

### Schema Direction
The `message` flow should be represented as:

- shared framework header
- `payload_bytes`

Payload dispatch should use:

- `type_id + bytes`

The framework should not hardcode all message payload variants into a fixed `oneof`.

### Renderer Contract
Only `message` should feed renderer-style presentation.

That means:

- renderer lookup is driven by `type_id`
- decoded message payloads may produce display output
- `message` packets that do not produce display output should be treated as suspicious and discouraged

### Suggested Concrete Shape
The schema draft assumes a message container conceptually equivalent to:

- `header`
- `payload_bytes`

No `message_kind` enum is needed at this stage because the entire lane is already semantically constrained to display behavior.

Recommended conceptual shape:

```proto
message FrameworkMessagePacket {
  FrameworkHeader header = 1;
  bytes payload_bytes = 2;
}
```

Constraints:

- `header.flow` must always be `FRAMEWORK_FLOW_MESSAGE`
- `header.type_id` identifies the concrete message payload contract
- `payload_bytes` contains the encoded extension-defined message body

Suggested payload examples for later submod and built-in usage:

- visible chat text
- visible system notice
- rendered gift/red-packet announcement
- rendered mailbox notice

## `command` Flow Schema
`command` is the control flow.
It exists for interaction, synchronization, service decisions, and internal feature state transitions.

### Responsibilities
- client requests
- server responses
- state synchronization
- internal events
- private feature updates

### Non-Responsibilities
- direct display rendering
- user-facing chat content as a primary payload purpose

### Schema Direction
The `command` flow should be represented as:

- shared framework header
- `command_kind`
- `payload_bytes`

Payload dispatch should use:

- `type_id + bytes`

### `command_kind`
`command_kind` should be explicit in the framework header/body contract rather than postponed for later.

Recommended values:

- `request`
- `response`
- `state`
- `event`

### Why `command_kind` Is Required Now
Adding `command_kind` later would force a broader framework-level migration:

- handler dispatch logic would need to be widened
- extension contracts would need to be revised
- payload expectations would drift across submods

Defining it now keeps the command lane stable for future stateful features such as:

- red packet claiming
- mailbox interactions
- auction or market actions
- tab synchronization
- feature-specific state refresh

### Renderer Rule
`command` must never be treated as a renderer-driven display lane.

Command handlers may trigger later visible output indirectly, but command payloads are not themselves display messages.

Recommended conceptual shape:

```proto
enum FrameworkCommandKind {
  FRAMEWORK_COMMAND_KIND_UNSPECIFIED = 0;
  FRAMEWORK_COMMAND_KIND_REQUEST = 1;
  FRAMEWORK_COMMAND_KIND_RESPONSE = 2;
  FRAMEWORK_COMMAND_KIND_STATE = 3;
  FRAMEWORK_COMMAND_KIND_EVENT = 4;
}

message FrameworkCommandPacket {
  FrameworkHeader header = 1;
  FrameworkCommandKind command_kind = 2;
  bytes payload_bytes = 3;
}
```

Constraints:

- `header.flow` must always be `FRAMEWORK_FLOW_COMMAND`
- `command_kind` is part of framework-level routing semantics, not a submod-local convention
- `header.type_id` still selects the concrete payload contract within the command lane

Examples of intended use:

- claim red packet request
- claim result response
- tab state sync snapshot
- server-side event notification
- feature-specific private state update

## `item` Flow Schema
`item` is the item payload flow.
It is a first-class lane, but it should remain codec-specialized instead of being forced into the exact same abstraction shape as `message` and `command`.

### Responsibilities
- transportable item payload definition
- item decoding and encoding
- item collection transport
- support for built-in and extension-provided item codecs

### Schema Direction
The `item` flow should be represented as:

- shared framework header
- `codec_id`
- `payload_bytes`
- `metadata`

This keeps the lane extension-friendly while respecting the fact that item payloads are tightly coupled to RimWorld runtime reconstruction rules.

### Built-in Vanilla Item Payload
The old `Trading.ProtoThing` should not remain the trade-owned protocol root for items.
However, its core data model is still the right prototype for the built-in vanilla item codec.

The built-in vanilla item payload should conceptually preserve these fields:

- `def_name`
- `stack_count`
- `stuff_def_name`
- `quality`
- `hit_points`
- `inner_item`

Recommended conceptual name:

- `FrameworkVanillaItemData`

This should be treated as:

- the default built-in item payload for normal RimWorld item transport
- not the only possible payload shape for future submods

### Item Collection Support
The old protocol clearly transports both:

- a single item payload
- repeated item payload collections

The new schema should preserve that capability at the framework level.

Recommended interpretation:

- the built-in item lane must support single item payloads
- the framework should also define an item collection wrapper for repeated item payloads
- features such as trade, mail, attachments, market listings, and delivery should consume those collection shapes without redefining item transport from scratch

### Explicit Quality Semantics
`quality` must remain an explicit semantic field in the built-in vanilla item payload.

In particular:

- "no quality" is a meaningful state
- it should not be represented merely by field absence

This preserves a real behavior currently relied upon by reconstruction logic.

### Unknown Fallback Semantics
Unknown item fallback must be part of the formal item-lane design.

Rules:

- if no codec can decode an item payload, fallback to an unknown item representation is allowed
- fallback behavior should preserve human-meaningful identifying information where possible
- decode failure should prefer graceful degradation over hard failure during runtime item reconstruction

### What Does Not Belong To `item`
The following old trade-side semantics are not item-lane responsibilities and must not be pulled into the item schema:

- request/response correlation tokens
- success/failure flags
- failure reasons and messages
- "our items" vs "other party items" view ownership semantics
- pending-notification lifecycle state
- completed-trade lifecycle flags

These belong to:

- `command`
- built-in trade feature state
- higher-level persistence structures above raw item payload transport

### Suggested Proto-Level Shape
The item lane should have a framework packet plus at least one built-in vanilla codec payload and one collection wrapper.

Recommended conceptual shape:

```proto
message FrameworkItemPacket {
  FrameworkHeader header = 1;
  string codec_id = 2;
  bytes payload_bytes = 3;
}

message FrameworkVanillaItemData {
  string def_name = 1;
  int32 stack_count = 2;
  string stuff_def_name = 3;
  FrameworkItemQuality quality = 4;
  int32 hit_points = 5;
  FrameworkVanillaItemData inner_item = 6;
}

message FrameworkItemCollection {
  repeated FrameworkItemPacket items = 1;
}
```

Notes:

- `header.flow` must always be `FRAMEWORK_FLOW_ITEM`
- `codec_id` is the dispatch key for item decoding
- `payload_bytes` remains codec-owned rather than framework-owned business data
- the built-in vanilla codec may encode `FrameworkVanillaItemData`
- custom codecs remain free to define richer item payloads

### Quality Enum Direction
The built-in vanilla item payload should keep an explicit quality enum instead of using nullable or omitted semantics.

Recommended conceptual shape:

```proto
enum FrameworkItemQuality {
  FRAMEWORK_ITEM_QUALITY_UNSPECIFIED = 0;
  FRAMEWORK_ITEM_QUALITY_AWFUL = 1;
  FRAMEWORK_ITEM_QUALITY_POOR = 2;
  FRAMEWORK_ITEM_QUALITY_NORMAL = 3;
  FRAMEWORK_ITEM_QUALITY_GOOD = 4;
  FRAMEWORK_ITEM_QUALITY_EXCELLENT = 5;
  FRAMEWORK_ITEM_QUALITY_MASTERWORK = 6;
  FRAMEWORK_ITEM_QUALITY_LEGENDARY = 7;
  FRAMEWORK_ITEM_QUALITY_NONE = 8;
}
```

This keeps "no quality" as a deliberate representable state.

## Proto Organization Direction
The framework protobuf sources should live outside the old `Chat` and `Trading` protocol roots.

Implemented direction for the first protobuf skeleton:

- `Common/Utils/Framework/Proto/Shared/`
- `Common/Utils/Framework/Proto/Message/`
- `Common/Utils/Framework/Proto/Command/`
- `Common/Utils/Framework/Proto/Item/`

Generated outputs should continue to follow repository convention:

- generated C# committed into `compiled/`
- explicit `compile-proto.sh`
- `.csproj` explicitly referencing generated files

## Migration Implications
This schema direction intentionally shifts the architecture away from legacy protocol ownership.

That means:

- legacy chat packets should eventually adapt into `message`
- legacy trade control should eventually adapt into `command`
- legacy trade item payloads should eventually adapt into `item`

It also means implementers should avoid recreating legacy top-level module ownership inside the new framework layer.

The intended target is:

- new framework-visible work should be written directly against `message`, `command`, and `item`
- old modules may temporarily adapt into those lanes
- the migration should converge into permanent flow pipelines rather than a temporary second copy of old chat/trade/user-management boundaries

Practical mapping guidance:

- `Authentication`
  - remains outside the three business flows as a transport/session prerequisite
- `Chat`
  - mostly maps to `message`
  - uses `command` where sync or control behavior is required
- `Trading`
  - mostly maps to `command + item`
  - uses `message` only for visible notices
- `UserManagement`
  - mostly maps to `command`

The schema therefore assumes corresponding runtime pipelines will exist:

- `message` pipeline for visible output
- `command` pipeline for control/state behavior
- `item` pipeline for codec-based transferable item handling

Adapters are acceptable only as removable migration layers.
If deleting adapters would break the architecture itself, the architecture is wrong.

## Status
The first implementation-oriented pass has now landed the initial `.proto` skeleton in the repository.

Current concrete decisions:

- package name: `Phinix.Framework`
- shared/header field numbers: fixed as drafted above
- `FrameworkItemCollection`: currently wraps full `FrameworkItemPacket` entries
- shared Google import: `google/protobuf/timestamp.proto`

The main remaining open work is now narrower:

- generate and commit the first protobuf C# outputs
- define the first built-in message payload contracts
- define the first built-in command payload contracts
- migrate runtime transport incrementally from JSON prototype objects to protobuf-backed framework packets
