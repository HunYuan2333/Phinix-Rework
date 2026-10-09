# Lightweight engineering for simple managed DLLs

Read after choosing the managed route when organizing initial code, adding a second feature, or untangling responsibilities. For small edits, use only the relevant section. This reference ships with the Phinix skill; simple DLLs do not require downloading, installing, or activating a RimWorld engineering skill.

## Define the current slice in a few sentences

Before implementing, identify the player-visible result, affected state/dependencies, and how to verify completion. Beginners can explain what clicking does, where data is saved, and what appears on failure. Technical users can name interfaces, owners, and invariants. Three sentences suffice for a counter tab; no separate design report is required.

A slice covers the complete path from user action to result. First make the smallest tab/settings/localization path work, add relevant failure handling and verification, then package and sideload. Messaging/item slices include missing dependencies, rejection, and unknown outcomes from the start. Do not extract a shared framework before completing the feature.

## Start with one project; split by actual responsibility

Default to one plugin project and assembly. Current Example already contains Extension, State, Tab, and SettingsPanel responsibilities in one source file; retaining that layout is acceptable. If separate files help, keep Example's `example/` directory. This is a suggested layout, not mandatory files:

```text
example/
  MyPlugin.csproj
  PluginExtension.cs              # Compose, activation, shutdown, registration
  PluginState.cs                  # Feature state and operation rules
  PluginTab.cs                    # State presentation and user input
  PluginSettingsPanel.cs          # Only when settings UI is needed
  package-config.json
  Resources/Localization/
pack.py
```

Keeping the existing project filename is also valid. If renaming it, check pack.py project lookup too. Current Example has `EnableDefaultCompileItems=false` and explicit Compile Include entries. Update those after adding/moving source files, or files may exist without entering the DLL. Project/directory names are organizational; rename independent identities together according to [local-loop.md](local-loop.md).

| Actual pressure | Suitable organization |
| --- | --- |
| One tab and a few settings | One project and a few classes or one file; no layered solution |
| Several independent features | Feature groups such as Notifications/ and History/, each with state/operations/UI and one common composition entry |
| Network/files/inventory boundaries | Narrow adapters within the owning feature; translate results without deciding domain success |
| Other plugins really need the API | Extract stable public Contracts; a separate assembly depends on consumers/versioning needs, not appearance |
| Extensive native lifecycle, Defs/assets, or deep Harmony | Return to [routes.md](routes.md), evaluate the mod route, then combine the external engineering skill |

Organize by the feature that causes change, not a pile of Utils/Helpers/Managers. Do not split solely to shorten files or add an interface for every class. Reference public contracts for required dependencies. Add your own abstraction only for multiple implementations, volatile boundaries, or useful test seams. Neutral DI can register a concrete State directly; it does not require IState.

## Responsibilities and dependency direction

- **Extension is the composition entry**: connect to the host, create scopes, register providers, and arrange Start/Stop. Keep UI, messaging business rules, and item algorithms out of Compose.
- **State/operation classes own rules**: explicit valid states and transitions, not scattered booleans or implicit call order. Use enum/state machines when multiple stages justify them; counters do not need them.
- **Tabs/settings panels are views**: read state, call operations, show rejection/waiting. Do not modify another feature's collections, create authoritative confirmation, or repeatedly persist inside Draw.
- **Adapters connect public services**: translate host/inventory/transport/storage shapes without weakening whole-batch delivery, acknowledgement, or recovery.

A simple feature does not require a separate Domain layer. When pure algorithms/state transitions emerge, extract ordinary C# from Verse/GUI adapters for useful independent verification. Do not duplicate existing host mechanisms just for abstraction. Cross-plugin cooperation uses the provider's public Contracts, not a new host business broker.

Inject the same State into tab/settings providers, as Example does; avoid separate mutable counters that “usually update together.” Decide persistence, mutation ownership, and effective phase before naming classes.

## Every state has a source, scope, and invalidation rule

| State | Decision for a simple DLL |
| --- | --- |
| Persistent settings | Unique host settings prefix; read counters from settings instead of a second authoritative value in State |
| Module runtime state | Plugin scope owns it; Shutdown tolerates incomplete initialization and repetition |
| Connection/save state | Acquire for the current context, distinct from module lifetime; no old game objects after disconnect/save changes |
| Temporary UI state | Selection, scroll, filters; define retention across opening/save changes, without item custody |
| Expensive derived views/caches | Rebuildable from authority; invalidate on data/filter/language/world changes. Read small datasets directly rather than preemptively caching |
| External operation result | Match stable operation IDs; retain dispatched unknown outcomes for reconciliation, without UI/timeout predicting success |

See [composition.md](composition.md) for events, caches, and background-work cleanup. Background I/O/pure calculation must not block the game thread. Add cancellation, timeouts, and bounded queues when tasks are needed, then recheck context inside dispatcher actions. IMGUI may enter Draw repeatedly in one frame. Keep expensive I/O, global scans, and registration outside drawing; simple local actions need no thread just to appear asynchronous. Optimize measured hot spots, not by banning LINQ or every allocation.

## Minimal checks and delivery

Check risk-relevant properties; do not simulate all Verse for simple wrappers:

- Simple tab: compilation/manifest/reference preflight, then sideload the exact ZIP and restart; check text, retained settings, repeated lifecycle, and obsolete callbacks.
- Deterministic rules: test boundaries, invalid transitions, and duplicate operations rather than repeating implementation steps. Text/layout-only changes use actual UI checks.
- External operations: cover missing dependencies, rejection, cancellation, expired context, and unknown outcomes. Item features retain [verification.md](verification.md) acknowledgement/recovery standards.

Use pack.py/tool explicit payload selection. Build into bin/ or an independent output directory, not over installed DLLs. Verify final ZIP manifest/identity/resources/digest, excluding test/stale DLLs and compilation references. Test the exact ZIP to be delivered. After moving files, check Compile Include and pack lookup; do not clean the whole workspace to eliminate output confusion.

Log activation, important transitions, and actionable failures with stable module/operation IDs, without per-frame noise or credentials/full payloads. Repeated-error limits have explicit connection/save/module scopes so an earlier failure cannot suppress later diagnostics permanently. Prefer existing logging and “Copy summary”; no new diagnostics platform is required.

User delivery can be short: completed behavior, exact package/target host, actual verification, unverified items, and next step. Add decisions only when compatibility/data risks justify them; do not require architecture reports, scaffolds, or extra tools for every simple DLL.

## Adaptation evidence

Adapted from fixed external commit `7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e`: [engineering philosophy](https://github.com/HunYuan2333/rimworld-mod-engineering-skills/blob/7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e/rimworld-mod-engineering-cn/references/engineering-philosophy.md), [verification and artifact organization](https://github.com/HunYuan2333/rimworld-mod-engineering-skills/blob/7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e/rimworld-mod-engineering-cn/references/testing-release.md), and performance/UI/observability references. Guidance was rewritten for simple Phinix DLLs and checked against current Example project/DI layout. It does not import About/LoadFolders, XML Def engineering, global Def rewriting, Harmony scoring, or large-mod compatibility matrices. External source is design evidence, not a runtime dependency.
