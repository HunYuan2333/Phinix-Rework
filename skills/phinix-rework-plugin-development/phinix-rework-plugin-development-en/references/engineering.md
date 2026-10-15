# Engineering organization for simple managed DLLs

Read after selecting the managed plugin route, when first organizing code, adding a second feature, or refactoring mixed responsibilities. When making only minor modifications, read the relevant subsection. This reference is bundled with the Phinix development skill; simple managed DLLs do not require downloading, installing, or enabling RimWorld Mod engineering skills.

## Define the slice in a few sentences

Before touching code, clearly identify the player-visible outcome, states/dependencies involved, and criteria for completion. Novices can state: "what happens on click, where data is stored, and what appears on failure"; technical users list interfaces, state owners, and invariants. Three sentences suffice for a counter Tab; do not draft heavy architecture proposals.

A vertical slice covers the entire path from user input to result: first verify a minimal Tab/setting/localization, then add error handling and validation, and finally package and sideload. Slices involving messaging or items must account for missing dependencies, rejections, and unknown states from the start rather than leaving recovery for later. Do not extract shared frameworks before completing the feature.

## Start with one project and split by responsibility

The default approach is one project, one assembly. The current Example entry file contains Extension, State, Tab and SettingsPanel, with presentation, themes, actions and service examples in separate files. Small plugins need not copy every split. When splitting files, you can follow Example's `example/` directory layout (these are suggestions, not mandatory files):

```text
example/
  MyPlugin.csproj
  PluginExtension.cs              # Compose, activation, shutdown, and registration
  PluginState.cs                  # Feature state and operation rules
  PluginTab.cs                    # View state, receive user actions
  PluginSettingsPanel.cs          # Only when settings UI is needed
  package-config.json
  Resources/Localization/
pack.py
```

Retaining existing project filenames is fine; if renamed, check `pack.py` project resolution. The current Example specifies `EnableDefaultCompileItems=false` and explicit Compile Include items; when creating or moving source files, sync the project compilation list, otherwise files will exist on disk without being compiled into the DLL. Project and folder names are merely organizational; identity must be updated according to [local-loop.md](local-loop.md).

| Real Change | Appropriate Organization |
| --- | --- |
| One Tab and simple settings | One project, small classes or single file; no layered solution |
| Multiple independent features | Group by feature (e.g. Notifications/, History/); keep state, actions, and UI together, sharing one composition entry |
| External boundaries (network/file/inventory) | Add narrow adapters inside the feature folder; translate external results without deciding domain success |
| Other plugins need to consume APIs | Extract stable public Contracts; whether to split into a separate assembly depends on consumers and versioning |
| Heavy native lifecycles, Defs/assets, or deep Harmony | Return to [routes.md](routes.md) to evaluate the Mod route, selecting additional engineering references if needed |

Organize by "what makes this code change", not dumping everything into generic Utils/Helpers/Managers. Do not split classes solely to shorten files or create an interface for every class. Explicitly reference public contracts for required dependencies; only introduce custom interfaces for multiple implementations, volatile boundaries, or test seams. Neutral DI can directly register concrete State classes without inventing an `IState`.

## Responsibilities and dependency direction

- **Extension is the Composition Entry**: Connects host, builds scope, registers providers, coordinates Start/Stop; do not write UI, messaging logic, or inventory algorithms inside `Compose`.
- **State/Operation Classes Own Rules**: Maintain explicit valid states and transition conditions, rather than guessing across scattered booleans or call order. Use enums/state machines when multi-phase state is needed; simple counters do not need them.
- **Tab/Settings Panels are Views**: Read state, trigger actions, display rejections/waiting states; do not mutate collections of other features directly, and do not trigger authoritative confirmations or repeated persistence in Draw.
- **Adapters Connect Public Services**: Convert external shapes of host, inventory, and transport; never weaken all-or-nothing delivery, confirmation, or recovery invariants.

Simple features do not need an artificial Domain layer. When pure algorithms or state transitions arise, extract plain C# from Verse/GUI adapters so valuable rules can be verified in isolation; do not duplicate existing host mechanisms for the sake of abstraction. Inter-plugin collaboration occurs via exported public Contracts without intermediate host brokers.

Inject the same State into both UI and settings panels, following Example's pattern; do not maintain two separate counters hoping they stay synchronized. Clarify persistence, mutators, and validity before choosing class names.

## State sources, scope and invalidation

| State Category | Decision for Simple DLLs |
| --- | --- |
| Persistent settings | Use host settings with unique key prefix; e.g. read count from settings so State does not hold a second authoritative counter |
| Module running state | Owned by plugin scope; Shutdown must support partial initialization and repeated invocation |
| Connection/Savegame state | Acquired from current context, not tied to module lifetime; switching saves or disconnecting leaves no stale game objects |
| UI transient state | Selected items, scroll positions, filter inputs; define whether preserved across reopen/save switch, never hold item escrow |
| Expensive derived views/cache | Rebuilt from authoritative sources; invalidate on data, filter, language, or world changes; read small datasets directly without premature caching |
| External action results | Matched to stable operation IDs; in-flight actions with unknown results remain pending for reconciliation; never predict success via UI or timeout |

For cleanup rules regarding events, caches, and background tasks, see [composition.md](composition.md). Background I/O and data computation must not block the game thread; when background tasks are necessary, specify cancellation, timeouts, and bounded queues, re-checking context inside dispatcher callbacks. IMGUI can invoke Draw multiple times per frame; expensive I/O, global scans, and registrations must never reside in draw loops. Simple local actions do not need separate threads for "asynchrony". Optimize based on measured hot paths, not dogmatic bans on LINQ or allocations.

## Minimal checks and delivery

Verify against the nature of the risk without mocking Verse for simple wrappers:

- Simple Tab: Verify compilation, manifest, and references; sideload from the exact ZIP, restart, and inspect text, setting persistence, repeated start/stop, and obsolete callback invalidation.
- Deterministic rules: Test boundary values, invalid transitions, and duplicate actions, not just reproducing implementation steps. Use real UI inspection when only copy/layout is involved.
- External actions: Cover missing dependencies, explicit rejections, cancellations, stale contexts, and unknown results; item features must observe confirmation and recovery standards in [verification.md](verification.md).

Use explicit payload selection in `pack.py`/tools. Keep build outputs in `bin/` or separate output directories, never overwriting installed DLLs directly. Verify manifest, identity, resources, and hashes of the final ZIP, excluding test/stale DLLs and build references; test the exact ZIP destined for delivery. When moving files, double-check Compile Include and pack paths rather than wiping entire workspaces to clear confusion.

Log activations, key state transitions, and actionable failures with stable module/operation identifiers, without flooding frames or logging credentials/full payloads. Duplicate error rate-limiting should have clear scopes (connection/save/module) to prevent suppressing future diagnostics; favor existing logs and "Copy Summary" before adding diagnostic platforms.

Delivery instructions to users can be succinct: completed behavior, exact package and target host version, verified items, unverified items, and next steps. Add decision records only when introducing genuine compatibility or data risks, without imposing architectural documents, scaffolding, or extra tools on every simple DLL.

## Upstream design references

Derived from [Engineering Philosophy](https://github.com/HunYuan2333/rimworld-mod-engineering-skills/blob/7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e/rimworld-mod-engineering-cn/references/engineering-philosophy.md), [Testing & Artifacts](https://github.com/HunYuan2333/rimworld-mod-engineering-skills/blob/7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e/rimworld-mod-engineering-cn/references/testing-release.md), and performance/UI references at pinned commit `7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e`, adapted to Phinix managed DLL workflows and verified against the current Example project/DI layout. This reference introduces no About/LoadFolders, XML Def engineering, global Def rewrites, Harmony rankings, or large-scale Mod matrices. External repositories serve as design rationale only; this reference has no runtime dependencies.
