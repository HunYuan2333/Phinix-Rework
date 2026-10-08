# Client infrastructure finishing and three-repository split

Updated: 2026-10-08. Branch: dev. [中文](后续实施计划.md).

This is the current schedule. It supersedes the earlier P0–P6 sequence that waited for store completion and a server compatibility prototype. Detailed older proposals remain references; current code and this schedule resolve conflicts. This is a plan, not evidence that DI, Stateless or repository extraction is implemented.

## F4 closure by user decision (2026-10-08)

F4 is closed at the user's requested scope checkpoint. This decision supersedes earlier wording that required whole-mod/theme work before F4 closure. F5 remains on hold until the user explicitly authorizes it; no extraction, new phase, commit or publication is implied.

- Cancel the whole-mod ZIP installation direction / F4-F2c whole-mod narrowing proposal. This turn changes documentation only: existing legacy installer code was not removed or disabled. Do not claim a completed code removal or the repair of its known metadata-scan boundary.
- Move third-party theme selection and store theme downloads to a later enhancement evaluation. Retain current theme behavior for now; no selector, payload kind, catalog support or download capability has been implemented. See [theme evaluation](Theme-Store-Future-Evaluation.md).
- Official client modules and maintained samples use Compose. F4-H adds an Obsolete client source compatibility adapter and one startup migration warning for registered old modules, without deprecating shared server Register. Author guides/templates and the trusted metadata/validator slice were aligned. See [F4 closure evidence](F4-H-Closure-Handoff.md).
- Client composition contracts remain 1.9; the first stable deprecation release is planned as host 0.9.8, conditional removal as host 1.0 / abstractions 2.0. No release/version publication occurred. Independent RedPacket/TalentTrade entry migrations and hard-removal gates remain explicitly future work; RedPacket business optimization stays deferred.
- Closure is a scope/phase decision, not evidence of every manual game test passing. F4-G/F4-H manual checks have no new supplied result. The H slice passed its narrow tests; the later dev checkpoint full build, 31 artifact checks and 14-source validator check also pass. Game validation is still not implied. Existing commits/parallel changes are preserved.


## 1. Scope and baseline

The store is delivered: managed DLL and Workshop routes, GitHub/CF adapters, localization, versions, upgrades, installation/removal, the redesigned UI and maintainer/distribution badges. The user accepted the new UI. The known handoff baseline is `f312c38940c8026f3f680089cf99b039402dc025`, including the one-time extracted-plugin announcement; its actual dialog and restart behavior still need game acceptance. This identifies delivered work, not a revision to reset to. Recheck the latest HEAD, remotes and concurrent documentation commits when starting.

Finish client lifecycle design, DI, a useful Stateless slice, limited testing/HTTP improvements and then extract Shared, Client and Server. Consume Phinix shared source through a Git submodule pinned to a commit and ProjectReference; do not add a parallel Phinix NuGet SDK channel. Ordinary third-party NuGet dependencies are allowed.

Defer server features, server DI and legacy-client compatibility. Repository extraction includes necessary server project paths, shared references, Docker context and publication wiring, with existing behavior retained. Independent RedPacket/TalentTrade business problems no longer gate this client infrastructure work.

## 2. Items recovered from older proposals

| Item | Current treatment |
| --- | --- |
| Construction versus startup, ownership and shutdown | Required before container integration |
| DI | Required: prove Chat first, then migrate relevant consumers in dependency order |
| Stateless | Required useful single-operation pilot; expand only where behavior and benefit are clear |
| HTTP/Polly | Inspect and address demonstrated sync/cancellation/duplicate-retry gaps; do not rewrite working async HttpClient merely to change libraries |
| NUnit/NUnitLite | Limited migration of affected scenarios; preserve existing coverage and executable failure reporting |
| Main thread and connection/save generations | Required; stale callbacks cannot mutate a new session/save |
| Shared versus game contracts and linked endpoint source | Required before extraction |
| Dependency versions, Mono loading and output ownership | Required; compilation alone is not deployment evidence |
| Example, index validator and host-module configuration | Update when contracts, real artifacts or source roots change |
| Display naming | Separate prose/display work; do not bundle assembly/module/package/storage renaming |
| SQLite, wholesale JSON/logging replacement | Defer as independent decisions requiring recovery and cost evidence |
| Server features and old-client compatibility | Defer |
| AI author tools, AGENTS and Skill | After author-facing composition contracts and source/build consumption stabilize |
| DLL hot replacement, new client CI, chat images/WebP | Defer; retain manual client checks and the existing server-image CI division |

## 3. Implementation order

| Stage | Work | Exit condition | Human acceptance |
| --- | --- | --- | --- |
| F0 Baseline | Pin input, existing failures, references/linked sources, candidate dependencies, output ownership and test backups | Existing client/server builds and scenario coverage are accounted for | Existing store/example/chat smoke |
| F1 Lifecycle and contracts | Separate construction, registration, activation and stopping using current assembly; define host/plugin/connection/save/operation ownership and neutral composition contract | Disabled plugins are not constructed; failed startup is cleaned; dependents stop first; behavior is testable without a container | Startup/shutdown, reconnect and save switching |
| F2 DI and Chat | Verify an Autofac candidate's net472/Mono loading, disposal and distribution; constructor-inject Chat services | Cancellation, subscription cleanup, borrowed API ownership and rollback work for official and third-party modules alike | Chat enable/disable, reconnect and repeated entry/exit |
| F3 Stateless pilot | Freeze current transitions/failure traces; migrate store single asynchronous-operation rules without changing installation transactions | Cancellation, errors, duplicate/stale events and stop/rebuild are deterministic; no false success | Cancel transfer, disconnect/retry, adapter switching and installation commit |
| F4 Adoption, old-entry deprecation and contract freeze | DI across Chat → Inventory → Trade → Store/LegacyAdapter; useful internal state slices; necessary HTTP/tests; update example and affected validators | Author-facing lifecycle/composition APIs and runtime distribution are stable; old client author entry is Deprecated with a declared removal version (section 4.1); superseded duplicate paths removed | Incremental store/example/inventory/trade checks; not a claim that historical recovery defects are repaired |
| F5 Shared-source rehearsal | Extract in independent temporary checkouts; both consumers pin the same initial gitlink; separate mixed tests; verify nested protobuf and Directory.Build behavior | Fresh recursive acquisition builds without neighboring old directories or duplicate runtime assets | New main package loads example; existing server image builds/starts |
| F6 Physical repositories | Extract Client/Server, history/branches as needed, tests, package paths, Docker workflow and developer entry points; pin a matching version set | Independent checkouts validate; remote shared commits and rollback are available; release ownership is clear | Main package/store/example and existing connection/chat/trade smoke |
| F6-M Management/Store state synchronization | Share package/module state calculation and management commands; invalidate both views by change version or notification; retain restart application | Distinguish package/module and current/next-start states; show partial disable, restart pending and load failure; preserve other module choices | Bidirectional toggles, single/multiple modules, repeated operations, failure recovery and post-restart parity |
| F7 Author experience | Refresh example/templates/guides, environment/build/publication tooling, then thin AGENTS/Skill | A new author can build/package/apply against one stable workflow; no-Git/gh path honors fixed gitlinks | Real example creation and normal submission |

Default order: F0 → F1 → F2 → F3 → F4 → F5 → F6 → F6-M → F7. Pure library environment probes can run independently. Keep DI, state rules, HTTP, persistent formats and physical source moves in reviewable separate changes.

User explicitly canceled F6-S (mandatory plugin signing/trusted-key admission) on 2026-10-08. Do not implement it or require it before F7. Retain existing hash, payload integrity, compatibility and installation transaction checks.

Implement F6-M as a separate batch after F6 and accept it before starting F7. Define package/module command semantics first; reuse shared management snapshots and change versions, with no direct calls between views. Marshal callback-driven UI updates through the existing main-thread dispatcher. For commands touching package intent and module settings together, define validation, commit/partial-failure recovery and stale concurrent-snapshot handling. Never display saved next-start intent as current activation. Preserve existing configuration/state readability and dependency checks. Cover operations initiated in either view, single-module recovery, partial multi-module disable, overlapping package/module disable, tab refresh, restart, operation failure and state-read failure. This is future implementation scope; this turn changes documentation only.

If container deployment fails, retain the useful lifecycle work and record evidence before selecting an alternative. HTTP and external RedPacket work do not block extraction. Changed contracts and runtime dependencies cannot be omitted from consumers to make extraction appear complete.

## 4. DI deliverables

Autofac is the initial candidate, not a preselected untested version. Microsoft DI remains a smaller alternative if local-registration/ownership benefits are insufficient. Verify current official requirements, actual net472 assets and real game/Mono loading before pinning versions and transitive dependencies.

- Keep the implementation in Client composition. Shared registry and public plugin APIs remain neutral and do not expose Autofac DSL/container types.
- Required dependencies are constructor parameters for ordinary services. Service location is limited to composition or justified transitional adapters, never per-frame UI or hidden domain dependencies.
- Host, plugin, connection, save and operation have explicit owners. Connection and save changes are independent; borrowed game objects and cross-plugin APIs are not disposed by the consuming container.
- Construct dependencies without running modules. Register/activate after host services are complete and in dependency order. Scanning cannot instantiate disabled plugins. Stop work, cancel old generations, revoke capabilities/subscriptions and then dispose on the correct thread.
- Official and third-party modules use the same path. Design the author-facing composition interface in F1/F2 and freeze it in F4.
- Development APIs may be coordinated across consumers rather than preserved as permanent debt. Breaking changes need declared abstraction versions and maintained example/plugin/index updates. Never overwrite fixed release assets or clear player settings/saves as migration.

### 4.1 Deprecation of the original client registration entry (2026-10-07 addition)

The user explicitly requires the original plugin registration entry to be Deprecated, rather than retained as a permanent second author path. New plugins, examples, templates and author documentation use the new neutral DI composition entry. Existing client plugins need a migration plan; do not continue promising that migration is unnecessary or that the old entry remains available indefinitely.

- **F2 is transitional**: Chat currently calls its new DI composition from `Register(IExtensionBuilder)`. This request updates the plan only. Marking that shared method Obsolete immediately would incorrectly label both new DI and unmigrated modules.
- **F4 distinguishes author entries and applies deprecation**: establish a neutral client contract/adapter boundary that clearly distinguishes new composition from old registration, then add `[Obsolete]` and replacement guidance to the old client author API/adapter entry. If necessary, emit one migration diagnostic for enabled modules actually using the old path, without constructing disabled modules or changing activation policy. Keep the neutral Common registry and discovery/register/activate/stop semantics unified. Server registration is not deprecated as part of this client DI rollout.
- **Migration coverage**: migrate official client Chat → Inventory → Trade → Store/LegacyAdapter and maintained third-party examples, templates, author documentation and affected index/validator configuration. Independent RedPacket/TalentTrade repositories schedule their own migrations; their old entry is not promised permanent compatibility either.
- **Temporary compatibility and removal gate**: retain the old entry only as a migration adapter; new features must not depend on it. F4 declares the deprecation version, replacement entry and intended removal version. After maintained modules/examples migrate and rollback/ownership regression and game acceptance pass, explicitly remove it or record remaining migration work. F5/F6 must not claim contract freeze and completed extraction while concealing retained dual entries.
- **Versions and rollback**: breaking removal upgrades the applicable abstractions/host, publishes migration instructions and synchronizes plugins/examples/validator configuration. Roll back a matching version set. Old plugins require upgrades to load after removal; immutable historical assets, player settings and saves stay intact.

### 4.2 F4 batches and acceptance gates (2026-10-07)

The user reported “没问题了”, accepting the extension-management blocker repair in game. No full per-step log was supplied; do not infer that every F3 fault timing was reproduced in game. Retain existing automated F3/repair evidence. Before F4, establish an explicit dev rollback baseline for accepted F3/repair changes, excluding parallel edits; no push/new branch. This update only splits the plan: no commit or F4 implementation was performed.

Current entry inspection: Chat uses a scope through old Register; Inventory implements six API families plus registries, ledger/journal, GameComponent binding and Harmony; Trade manually builds pipeline/services/adapters/UI and borrows Inventory APIs; Store constructs controller/UI during Activate; LegacyAdapter owns endpoint/connection subscriptions. Use these boundaries for small batches.

| Batch | Deliverable | Validation and game focus |
| --- | --- | --- |
| F4-A New author entry + Chat | Establish neutral client composition/legacy adapter boundaries; migrate Chat to the new entry and declare deprecation/removal version proposal. Keep ordinary discovery/activation/stop and server Register unchanged; do not freeze yet | New/old entry once, disabled zero-instance, partial failure and cleanup; chat, reconnect, disable/restore |
| F4-B Example and Playtest | Migrate both maintained client samples and minimal author guidance; update necessary build/validator slices | Actual sample build/manifest/contracts; install, disable, restore, uninstall/reinstall; no historical release rewrites |
| F4-C Inventory composition | Scope ordinary dependencies and UI/settings, retaining necessary module/game façade, API identity and codec registration behavior | Partial construction, borrowed API ownership, single instances; inventory open/deposit/extraction/disable |
| F4-D Inventory save lifetime | Make ledger/journal/save attachment ownership and cleanup explicit; retain game ownership of game objects and existing persistent/domain formats | Two test saves, menu/reentry, stale callbacks/subscriptions; reservation/rollback/restart consistency |
| F4-E Trade composition | Scope pipeline/services/legacy façades/UI; borrow Inventory APIs and retain explicit connection Start/Stop | Framework/legacy trade regression; offer/refusal/cancel/ACK/reconnect; unchanged conversion/ownership |
| F4-F Store composition | Scope controller/services/views while retaining F3 transitions and original transaction service | Cancel/retry/repository switch/install/module recovery/uninstall/reinstall/exit; unchanged protocols/recovery |
| F4-F2 Narrow local mod checks | Separate batch after Store composition acceptance: prioritize loaded assemblies/effective folders; distinguish proven collision from inability to inspect, rather than automatically vetoing on unrelated folder errors | Active/inactive mods, version/conditional folders, true collisions, unreadable/malformed XML, links/traversal and precommit recheck; explicit unknown-case policy |
| F4-G LegacyAdapter composition | Scope endpoint construction and connection subscription ownership | Legacy/framework endpoint selection/reconnect/disable/stop; full legacy trade regression |
| F4-H Deprecation and freeze | Apply section 4.1 Obsolete to the distinct old client author entry; diagnostics only for enabled old modules. Align guides/templates/affected validators and declare external repository migration/version gates | Official/third-party parity, no maintained internal old callers, server unaffected; full build/distribution and combined game smoke |

Each batch: inspect latest/parallel edits, implement, narrow regression/affected build, report game steps, obtain acceptance, then commit independently on dev. Split further when useful without committing half of a nonbuilding contract/consumer update. Do not bundle the next plugin into a failing batch. Propose versions in F4-A; freeze only after F4-H migration/distribution checks. Explicitly track unmet old-entry removal gates and independent RedPacket/TalentTrade migrations.

Additional HTTP/state-machine work requires a concrete benefit/defect and its own batch. Persistent formats, physical moves and F5/F6 extraction retain their original stage boundaries.

## 5. Stateless target

The old first target, `Extensions/LegacyRedPacket/Client/RedPacketInventorySending.cs`, has been removed from the main repository and belongs to the independent RedPacket project. Do not retain that stale path as the main implementation prerequisite.

Start with a single asynchronous operation in [ManagedStoreController](../../../Extensions/PluginStore/Client/ManagedStoreController.cs): it has stages, cancellation, snapshots and real production-source regression coverage, and can be verified without a server change or external-plugin publication. Verify that an explicit transition table removes real duplication before replacing internal rules. Preserve refresh/planning/dependency confirmation/download/verification/install/manage/stop behavior.

Stateless expresses transitions; the controller still serializes work, verifies generations/cancellation and protects snapshot publication. Progress must not create costly transitions or per-buffer logs. Existing installation journals, hashes, file ownership, recovery and commit remain in the installation service. A transition does not make multiple files/plugins atomic, and Installed cannot precede durable commit. Recovery cannot rerun side-effecting entry actions.

Consider Trade/Inventory operation slices only with clear rules and failure evidence. Actual ACK, uncertain sent operations, duplicate results and persistence-before-publication retain their meaning. Historical recovery defects stay separate. RedPacket adoption is a future decision in its own repository.

## 6. Repository layout

Propose retaining the existing `Phinix-Rework` remote identity for Client, limiting disruption to current links/Workshop author entry points. Add Shared and Server repositories; confirm their exact names before F6. This plan does not create remote repositories.

| Repository | Owns | Excludes |
| --- | --- | --- |
| Shared | Game-independent contracts/protocols/tools and genuinely shared neutral runtime; business contracts stay organized by plugin | Concrete endpoints, Verse/Unity, Autofac, plugin UI |
| Client | RimWorld host, client/game/UI contracts, endpoint implementations, bundled client modules, game-related tests and packaging | Linked server endpoint source and copies of extracted plugin/gateway source |
| Server | Existing server/endpoints/modules/tests, Docker and server image workflow | Client/game/UI dependencies and linked client endpoint source |

Shared is not necessarily interface-only, and current Common directories cannot be copied without ownership review. Classify NetClient/NetServer/authentication/user-management endpoints. Split game/client portions of Chat/Trade Contracts from shared protocol before relocating them. Hosts still do not reference concrete business plugin contracts.

Each consumer pins a Shared commit at the proposed `Dependencies/Phinix.Common` path and uses ProjectReference. No floating branch acquisition; consumers may later pin different compatible versions. Preserve the vendor protobuf submodule, initializing it recursively when nested under Shared; do not patch its source for repository SDK issues.

Validate the new Server image workflow before retiring the old repository publisher; keep one publication owner and do not add a client build workflow. Future no-Git author tools must retrieve nested source at gitlink commits, not current floating Shared branches.

F5 checks SolutionDir/MSBuildThisFileDirectory, Directory.Build import boundaries, explicit/conditional Compile items, legal game references, copy paths, numbered client DLLs, protobuf paths, mixed Phase35-style linked tests, Docker contexts and validator source roots. The new Server must build without a neighboring Rework directory. Keep deployment/image names and connection behavior stable; provision workflow credentials separately without committing them.

Index/Gateway/Example/RedPacket/TalentTrade remain independent. DI does not change repository access protocols. Update affected fixed validator snapshots/host manifests from real artifacts and specified input commits when required; retain immutable release/catalog history.

## 7. Validation and completion

Record source commits, public API/data/protocol impacts, actual commands/results, output dependencies and human evidence by stage. Preserve current Phase35, ManagedExtensionRuntimeTests, ResponsiveUiGeometryTests, LegacyTradeRuntimeTests, packaging and plugin-specific scenario coverage; move tests by responsibility rather than testing stale production copies.

Inject construction/registration/activation failure, provider shutdown, duplicate subscriptions, cancellation/stale callbacks, save switches, failed persistence, duplicate ACK, missing references, logger/cleanup failure and wrong-thread resource handling. Library compile tests do not certify actual game loading or recovery. Human checks are incremental, not a replay of the whole accepted store route.

F6 requires stable shared/client composition APIs, independently obtainable/buildable pinned consumers, correct example/store loading, runtime assets and executable publication/rollback instructions. F7 does not await SQLite, Server DI, legacy-client new features or blanket HTTP/test-runner migration.

Rollback endpoint implementation, matching Shared gitlink and runtime dependencies as a set. Persistent-format changes require a separate compatibility/migration design; source rollback alone is not data rollback.

## 8. Status and references

- [x] Store and redesigned UI accepted by the user.
- [x] New ordering, missing items and deferred-server boundary recorded.
- [ ] Actual one-time upgrade-dialog/restart acceptance.
- [x] F0 first-batch framework baseline/ownership (section 11.1; live remote verified during F2 preparation).
- [x] F1 first batch accepted by the user, who authorized F2 (section 11.2; individual game steps were not separately confirmed).
- [x] F2 Autofac candidate environment probe.
- [x] F2 Chat production composition and regression coverage (section 11.4).
- [x] F2 game acceptance: user reported “F2 pass” (section 11.5).
- [ ] F3 Stateless and F4 adoption, old-entry deprecation and contract freeze.
- [x] F5 local rehearsal and user acceptance: “F5-C passed” on 2026-10-08.
- [ ] F6 physical repositories and publication cutover.
- [ ] F6-M Management/Store state synchronization, with bidirectional and restart acceptance before F7.
- [x] F6-S canceled by user; mandatory signing admission is excluded.
- [ ] F7 AI author tools and Skill.

References: [older four-part migration](客户端四项基础设施迁移方案.md), [library evaluation](客户端框架与库选型评估.md), [repository extraction details](Repo-Split-Plan.md). Deferred: [SQLite](客户端持久化与SQLite评估.md), [old-client server compatibility](老客户端服务端兼容插件评估.md), [naming](Naming-Consistency-Audit.md), [AI workflow](plugin-store/收尾顺序与AI作者工作流计划.md). Stable constraints: [design philosophy](../../Design-Philosophy.md), [compatibility/recovery](../../Compatibility-Boundaries.md).

Older stage numbers and pre-delivery store states do not impose new prerequisites. Recheck actual code before implementing details, especially extracted-plugin paths. Independent business or server unresolved items do not silently become gates for this client work.

## 9. Start directly in a new conversation

**The first delivery is F0 baseline verification plus a minimal F1 lifecycle change with regression coverage.** Do not substitute another plan, introduce containers/state libraries, extract repositories or redesign accepted store UI in this batch. Proceed to F2 container/Chat integration after this batch and its game acceptance. Another task owns repository documentation cleanup; read its latest work, check it against code and preserve its changes rather than rewriting those READMEs.

1. In the original workspace, read AGENTS.md, this plan and the design philosophy. Read compatibility boundaries before touching trade, recovery or ownership. Inspect branch, HEAD, remotes, recent commits and tracked/untracked changes. Distinguish pushed work, concurrent edits and local drafts; do not reset, clean or stage everything to manufacture a baseline.
2. Check the actual entry points below and record the small F0 ownership/lifecycle inventory, existing failures and missing prerequisites. Earlier user acceptance is existing evidence, not a new test performed by the incoming agent.
3. Implement production F1 code and matching tests in one reviewable batch. Ask only for missing decisions, external permissions or real blockers; resolve ordinary implementation choices autonomously.
4. Prefer a suitable existing isolated workspace; otherwise create a `codex/` branch from the verified development baseline. At this handoff these plan files are untracked in the original workspace: read and explicitly carry them, or include them in a later commit. A fresh worktree/clone does not automatically receive them, game references, legacy NuGet caches or concurrent uncommitted code.
5. Preserve parallel changes and compare actual overlapping diffs before editing. Unfinished unrelated work, IDE files, generated fixture DLLs and outputs are outside this batch. Remote creation/naming, credentials and repository migration belong to later stages, not F0/F1.

Announcement game acceptance can run independently and does not gate F0/F1. F1 lifecycle acceptance is still required before expanding integrated F2 changes. Give the user short steps, rebuild scope and expected outcomes; continue independent ownership or library-environment preparation while waiting.

### 9.1 Source entry points

| Entry point | Inspect |
| --- | --- |
| [Client.cs](../../../Client/Source/Client.cs), `InitializeExtensions` | Startup already waits for LongEvent completion and the main-thread dispatcher. Check service readiness, dependency policy and every framework construction call rather than adding another arbitrary delay |
| [PhinixFrameworkClient.cs](../../../Client/Source/Framework/PhinixFrameworkClient.cs) | Construction now has no discovery/activation effects; explicit Start performs composition/startup and ClientExtensionRuntime owns module lifecycle. Recheck shutdown/failure cleanup |
| [FrameworkTypes.cs](../../../Common/Utils/Framework/FrameworkTypes.cs), `ExtensionHostContext` | Current service dictionary/capability access, not an already integrated DI container |
| [PhinixExtensionRegistry.cs](../../../Common/Utils/Framework/PhinixExtensionRegistry.cs) | Discovery, registration rollback, dependency ordering, activation/shutdown and parameterless-constructor checks. Keep official/third-party parity; do not assume constructor injection already exists |
| [Client abstractions](../../../Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs) | Public services/events/lifecycle; add only needed neutral contracts, never container-specific author APIs |
| [Dispatcher](../../../Client/Source/Framework/ClientMainThreadDispatcher.cs) | Queue limits and real game-thread handling. Construction on a loading thread does not identify the game thread; a bounded queue cannot be assumed to accept every critical callback |
| [Settings adapter](../../../Client/Source/Framework/ClientSettingsContextAdapter.cs) | Set triggers persistence. Global settings do not belong to one save scope; failed saves are not success |
| [Chat entry](../../../Extensions/Chat/Client/BuiltInChatClientExtension.cs) | F2 pilot dependency access, subscriptions and activation/shutdown; preserve behavior in F1 |
| [Inventory entry](../../../Extensions/Inventory/Client/BuiltInInventoryClientExtension.cs), [InventoryGameComponent](../../../Extensions/Inventory/Client/InventoryGameComponent.cs), [Trade entry](../../../Extensions/Trade/Client/BuiltInTradeClientExtension.cs) | Independent connection/save lifetimes; defer domain adoption to F4 |
| [ManagedStoreController](../../../Extensions/PluginStore/Client/ManagedStoreController.cs) | F3 operation serialization, snapshots, cancellation and stale callbacks; retain transaction services |
| [StoreReleaseNotice](../../../Extensions/PluginStore/Client/StoreReleaseNotice.cs) | Announcement already implemented; remaining work is actual game acceptance |
| [Package layout](../../../Client/Packaging/ClientPackageLayout.targets), [artifact check](../../../.github/scripts/check-artifacts.ps1) | Output ownership, excluded game DLLs/optional legacy plugins, languages and LoadFolders |
| [Validator snapshot](../../../Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py) | Pinned production inputs; coordinate only affected changes, never refresh blindly to hide inconsistency |

These paths are navigation at handoff time; inspect current callers and files before changing them. Removed RedPacket/TalentTrade directories are not F1/F3 prerequisites.

### 9.2 First batch and exit conditions

Record F0 results in section 11, linking a necessary audit artifact if useful; avoid proliferating duplicate plans:

- Project inventory: target frameworks, shared/endpoint ownership, game dependencies, linked sources, reference directions and output/distribution responsibility. Cover F1 callers first, then extend to extraction candidates.
- Lifecycle inventory: creator/stopper/disposer for host, plugin, connection, save and operation; borrowed services; thread/generation requirements.
- Verification baseline: commands actually run, existing failures and environment gaps. Do not repair unrelated business bugs merely to make every check green.

Minimal F1 implementation:

- Move discovery/registration/activation out of framework construction into explicit startup after composition is complete. Trace every caller, readiness and failure path; retain manual composition at this stage.
- Define repeated Start/Stop/Dispose and failed-start semantics. Clean owned resources after partial registration/activation, dispose once, never destroy borrowed services, and avoid constructing disabled plugins during discovery.
- Preserve dependent-first stopping, capability/subscription revocation, cancellation and stale callback isolation. Handle game resources through actual main-thread rules; no background Verse/Unity/translation calls.
- Leave a neutral creation/lifecycle boundary for DI, introducing only interfaces used by current production code. Record actual public API/version and maintained-plugin impacts; do not add speculative extension points.
- Extend the closest existing runtime harness with production-path coverage: construction without activation, explicit one-time startup, disabled construction prevention, partial failure, repeated Start/Stop, provider shutdown and cleanup exceptions. Do not verify only a separate imitation of the implementation.

Exit with production code/tests, reviewed diff, build/regression evidence, ownership conclusions, concise game steps and the next F2 slice. Another plan alone is not delivery. Do not bundle persistent format, trade ACK/ownership, installation transaction, access protocol or accepted UI changes.

F1 human smoke uses test saves: restart → chat/store/installed example → disconnect/reconnect → main menu and another test save → exit. Check errors, duplicate tabs/subscriptions/messages and stale callbacks affecting the new save. No real-asset trade is required; changes to trade paths need targeted compatibility acceptance.

## 10. Current monorepository validation entry points

Run from the repository root. Check .NET 10, net472 targeting support, classic NuGet dependencies, protobuf submodule and legally available RimWorld 1.6 references. Restore missing dependencies first; do not assume `--no-restore` is valid in a fresh environment or that one SDK restore resolves every packages.config dependency. Report exact missing prerequisites instead of borrowing output DLLs to fake source dependencies.

The commands below are current starting points, to be updated after extraction. Record actual complete commands and exit results. Do not run builds concurrently when they write the same output folders.

```sh
phinix_repo_root="$(pwd)/"
phinix_game_references="${phinix_repo_root}GameDlls/1.6"

# Full build only when legal game references are available.
dotnet build Phinix.sln --configuration "Release 1.6" \
  -p:SolutionDir="$phinix_repo_root" \
  -p:RimWorldDepDir="$phinix_game_references" \
  -p:GameReferenceDirectory="$phinix_game_references" \
  -p:BuildInParallel=false -m:1

# Existing server behavior/references, not server feature development.
dotnet build Server/Server.csproj --configuration Release \
  -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1

dotnet build Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj \
  --configuration Release -p:SolutionDir="$phinix_repo_root" \
  -p:BuildInParallel=false -m:1
dotnet Tests/Phase35RuntimeTests/bin/Release/net10.0/Phase35RuntimeTests.dll

dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj \
  --configuration Release -p:SolutionDir="$phinix_repo_root" \
  -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
# If Mono is available; still not a real RimWorld loading/GUI test.
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
```

Choose an existing reference directory containing all required Unity modules; store additions also require UnityEngine.ImageConversionModule.dll and com.rlabrecque.steamworks.net.dll. Some older configurations use GameDlls/ rather than GameDlls/1.6; check both path properties and actual files, or use the game's Managed directory. Never commit/package these game references.

For geometry/layout changes only, add deterministic assertions and run:

```sh
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj \
  --configuration Release
```

After building the corresponding artifacts:

```sh
pwsh -File .github/scripts/check-artifacts.ps1 -IncludeClient
```

If pwsh is unavailable, report that script as not run. An equivalent read-only inspection must follow its current required files, `/,Common,1.6` load folders, languages, excluded game DLLs and excluded bundled RedPacket/TalentTrade artifacts. State substituted checks and limits; do not claim the PowerShell script passed.

When modifying pinned validator production inputs, first run:

```sh
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py \
  check --source-root .
```

Investigate drift before updating trusted inputs/provenance and the independent index validator; refresh is not unconditional. For LegacyAdapter/trade changes use the documented MSBuild/runtimeconfig method in [compatibility boundaries](../../Compatibility-Boundaries.md), not an assumption that dotnet test executes console harnesses. Do not commit build artifacts, fixture DLLs or logs.

## 11. Handoff record and maintenance

Known at handoff: f312c38 is pushed. The announcement's store build and ManagedExtensionRuntimeTests on .NET 10 and net472/Mono passed; the main harness then had 937 assertions including nine announcement assertions. This is prior evidence, not F1 verification. DI, Stateless and repository extraction remain unimplemented. Recheck the latest HEAD for concurrent documentation commits.

The announcement uses global configuration key `plugin-store.notice.optional-plugins-20261007`, not per-save state. Pending human checks: first startup shows it, the store action opens the store, and another restart does not repeat it. Use an isolated test configuration for repeated acceptance, never clear real player settings. No further announcement framework is required.

Append one short record per batch and update section 8 only with actual evidence:

```text
Stage/date:
Input: branch/HEAD, relevant remote/shared SHAs, concurrent edits
Delivery: production files/entry points, public API/dependency versions, ownership changes
Impact: settings/saves/protocol/items/packaging, including explicit no-change conclusions
Validation: actual commands/results/evidence; checks not run and why
Human: actual user feedback, pending short steps; build success is not game acceptance
Commit: batch commit/PR or local-only status; no unrelated files
Next: one actionable slice, exit conditions and required external inputs
```

New-conversation prompt:

> Read AGENTS.md and docs/branch-local/dev/后续实施计划.md. Verify current code and concurrent documentation commits, then implement F0 plus F1 production code and regression coverage following section 9. Preserve existing edits; do not redo accepted store behavior, extract repositories or introduce a container yet. Deliver one verified batch, report game acceptance steps and update the handoff record.


### 11.1 F0 + F1 first batch (2026-10-07)

Input: original workspace, `dev`, HEAD and cached `origin/dev` at `f312c38940c8026f3f680089cf99b039402dc025`; protobuf gitlink `4b0c3aacf0657fbf38253b38918d3358dd4319ec`. `git ls-remote origin refs/heads/dev` failed because the configured proxy could not reach GitHub; live remote state is unverified. The pre-existing abstraction-file working-tree state, other untracked plans/audits, IDE files, Inventory output and fixture DLLs were retained. Only related status and handoff entries were added to these two plans.

F0 ownership for this batch; expand the full extraction inventory in F5:

| Project/source | Target, dependency direction and ownership | Game/linked source | Distribution |
| --- | --- | --- | --- |
| Client/Source | net472 host → abstractions/Common/client endpoints; no business Contracts reference | Verse/Unity; explicit Compile includes the new runtime | Client main package, 13-PhinixClient.dll |
| ClientExtensionAbstractions | net472 client service/UI contracts → Utils/UserManagement | Game APIs; not wholly Shared; existing parallel interface file untouched | Client main package, 10-ClientExtensionAbstractions.dll |
| Common/Utils | net472/net10.0 neutral registry/context/protocol/managed runtime → protobuf | No game dependency; automatic source inclusion | Client 03-Utils.dll and server Utils.dll; each endpoint owns distribution |
| Common Connections/Authentication/UserManagement | net472/net10.0 shared primitives/protocol | Endpoint implementations excluded from compilation; no Client back-reference | Client 04/06/08 and server equivalents |
| Client/Common endpoints | net472 endpoint implementations → Common | Link NetClient, ClientAuthenticator and ClientUserManager still physically under Common; relocate by ownership before F5 | Client 05/07/09 |
| Chat/Trade Contracts | net472/net10.0 plugin contracts | net472 still conditionally references client/game contracts; not yet entirely neutral | Client Extensions 08/09; separate server Extensions |
| Inventory and PluginStore.Client | net472 client plugins; ordinary module path | Game/UI types; Store → client abstractions and Utils; no new infrastructure libraries | Client Inventory 10/11, Store 17 |
| Phase35 / Managed tests | net10.0 / net472+net10.0 console harnesses | Link actual runtime/endpoints or store/localization source, no duplicated implementation | Test outputs only; fixture DLLs excluded from distribution |
| Server | net10.0 existing host/endpoints | No game dependencies; consumes the shared registry cleanup change | Existing server output/publication path |

Existing LiteNetLib, protobuf and Memory/Unsafe/Vectors ownership remains with the main package; no third-party dependency or version was added. Autofac, Stateless, Polly and NUnit versions/real Mono distribution are not selected. Compile references came from existing `GameDlls/`, since `GameDlls/1.6` is absent.

| Lifetime | Owner and stop boundary | Borrowed resources/callbacks |
| --- | --- | --- |
| Host | Client prepares services, explicitly starts framework, handles startup failure and Unity main-thread quitting | Framework owns timer/network registration/service slots; Client owns localization/managed host runtime. ProcessExit handles game-independent host resources only |
| Plugin | ClientExtensionRuntime uses the ordinary registry; partial failure and terminal idempotent Stop/Dispose clean consumers before providers | Registrations revoked; plugin cancels work/unsubscribes; borrowed services/APIs are not disposed. Constructors must be exception-safe before an instance exists |
| Connection | Existing NetClient/authenticator/userManager; reconnect does not reconstruct plugins | Existing dispatcher/disconnect handling retained; stopped framework rejects late send/display/timeout work; no unified connection generation framework introduced |
| Save | InventoryGameComponent and Inventory plugin own save identity/journal | Global settings stay Client-owned; Set still saves; no save-format/scope migration |
| Operation | Store controller/plugin owns task/CTS | Existing running/token/disposed gates and installation transactions retained |

```mermaid
flowchart LR
    A[Construct framework without plugin effects] --> B[Main thread host services ready]
    B --> C[Explicit Start]
    C --> D[Register and activate in dependency order]
    D --> E[Connection and save change independently]
    D --> F[Startup failure or main-thread quit]
    E --> F
    F --> G[Clean consumers first and revoke registrations]
    G --> H[Release framework resources then Client-owned host resources]
```

Delivery: production ClientExtensionRuntime and explicit classic-project Compile inclusion; passive framework construction and explicit pre-connect Start; symmetric failure/quit cleanup; registry cleanup for partial Register/Activate failure, registered-but-not-active modules and dependent rollback. Phase35 links the production runtime and covers passive construction, single startup, disabled construction, constructor/register/activate failure, repeated Start/Stop/Dispose, terminal stop, thread rejection, consumer-before-provider cleanup, module/observer exceptions and borrowed service ownership. Existing registry scenarios remain.

API impact: additive `ExtensionHostContext.RemoveService<T>(ownedInstance)` removes only the matching owner without disposal. Existing author-interface signatures remain unchanged; constructor injection is not supported yet. Utils remains 0.9.7.0, client abstractions 1.8.0.0. Deploy matching 03-Utils.dll and 13-PhinixClient.dll because the new host calls the new Utils method. Third-party Shutdown must now tolerate incomplete Register/Activate; official client/server cleanup was checked for partial initialization, while real third-party loading needs game acceptance. No pinned validator source input changed and no snapshot refresh was performed.

Settings/save formats, wire protocol, trade ACK/item ownership, install transactions, accepted store UI and packaging layout remain unchanged. The shared registry cleanup affects both endpoints; no business recovery claim is made.

Actual checks, all exit 0, from repository root; `phinix_repo_root=/home/hunyuan2333/Phinix/Phinix-Rework/` represents the absolute property values used:

```sh
phinix_repo_root=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet build Phinix.sln --configuration "Release 1.6" -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
dotnet build Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet Tests/Phase35RuntimeTests/bin/Release/net10.0/Phase35RuntimeTests.dll
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
python3 /tmp/phinix-f1-check-artifacts.py
git diff --check
```

Full build: 0 errors, 9 existing protobuf/obsolete warnings; includes client main package and server. Phase35 passed. Managed main harness: 937 assertions plus six child scenarios on each of .NET 10 and Mono; NU1900 audit requests failed due to unavailable NuGet network, but builds/tests passed. No pwsh: the PowerShell checker was not run. A temporary equivalent read-only Python check passed the same 21 required files/languages, load folders and game-DLL/retired-plugin exclusions. Local build logs are `/tmp/phinix-f1-{solution,phase35,managed}-build.log`, not committed.

Known entry limitation: direct Client.csproj build with Release 1.6 and these path/serial properties exited 1 because the classic abstractions project has no OutputPath for that configuration; solution configuration mapping compiled successfully, without a project-configuration workaround. Geometry and LegacyTrade harnesses were not run because their code paths were unchanged. No game/Steam/GUI or server deployment was performed. Harnesses use isolated temporary directories; player settings/saves were not accessed or changed. Copy test configuration and two test saves before human acceptance.

Pending game steps: deploy the full new Output/phinix-rework package or at least the matching Utils/host pair; restart RimWorld 1.6; check chat/store/installed example, send once, disconnect/reconnect and send once again; return to menu, enter another test save, then quit normally. Expect no duplicate tabs/messages, stale save callbacks or plugin shutdown errors. Also disable a regular example in test settings, restart, check Disabled/no tab/no activation log, then restore it. Prior store UI acceptance stays historical evidence; this batch did not repeat it. The announcement remains an independent pending check.

Commit: local changes only, no staging/commit/push and no inclusion of other drafts/generated DLLs. Next: process actual F1 game feedback, then F2 Autofac environment/distribution and Chat composition pilot; F2 integration remains gated by game acceptance.


### 11.2 F1 feedback and F2 preparation (2026-10-07)

The red-packet symptom recovered; the user considered it an intermittent relay problem and explicitly said “没问题，准备F2”. Proceed on that acceptance without inventing separate confirmation of every reconnect/save/quit step. The supplied log shows the host/business/example/managed modules active, but also notice/legacy-chat notification NREs; it is not a clean-log or announcement acceptance claim. Relay HTTP 500 and eventual reward deposit lack a complete causal trace; no red-packet code was changed.

Input remains dev/f312c38940c8026f3f680089cf99b039402dc025 plus preserved F1/local edits. Approved read-only network access with the unavailable proxy cleared let git ls-remote confirm remote dev at the same SHA.

Delivery: independent Tests/ClientCompositionRuntimeTests, net472 binary, exact candidate and lock file; no shipping Client reference. Nineteen assertions cover lazy construction, separate activation, borrowed ownership, subscription/cancellation, partial constructor failure, reverse dependency disposal, cleanup failure and guarded release; actual ActivityContext/ActivitySource operations exercise diagnostic dependencies.

| Candidate | Evidence | Treatment |
| --- | --- | --- |
| Autofac 9.3.4/netstandard2.0, AsyncInterfaces/DiagnosticSource 10.0.12 | Complete Mono probe passes; same net472 binary on .NET 10 fails DiagnosticSource 10.0.0.12 strong-version loading after real activity operations; mixed Mono loads both old/new support assemblies | Comparison only; not evidence that native net10.0 assets or Mono are unsupported |
| Autofac 8.4.0/netstandard2.0 | Mono, .NET 10 and Mono preloaded with current host support DLLs each pass 19 assertions; build has zero warnings/errors | Current trial candidate, pinned only in the probe; shipping version requires actual game acceptance |

Runtime assets: Autofac 8.4.0/netstandard2.0; AsyncInterfaces 8.0.0/net462; DiagnosticSource 8.0.1/net462; Memory 4.5.5/net461; Unsafe 6.0.0/net461; Buffers 4.5.1/net461; Vectors 4.5.0/net46; Tasks.Extensions 4.5.4/net461. ReferenceAssemblies 1.0.3 is compile-only. Memory/Unsafe/Vectors differ from current host files. Ship one host-owned copy of each public assembly after coordinated validation; do not rely on duplicated files or console binding redirects. Mixed Mono loads old and new Memory side by side; .NET 10 uses framework diagnostics/memory, so neither scenario certifies Unity loading.

Both candidates stop default scope disposal when one owned Dispose throws, skipping its remaining dependency. Guarded OnRelease catches/reports each object error and continues cleanup; verified by the probe. Production coordination must cancel/unsubscribe first and use that protected release path. Register borrowed host/cross-plugin APIs ExternallyOwned. Container disposal does not cancel ordinary subscriptions/CTS. Sources: [official package](https://www.nuget.org/packages/Autofac/8.4.0), [Autofac disposal](https://docs.autofac.org/en/latest/lifetime/disposal.html).

Actual 8.4.0 commands, all exit 0, from repository root:

```sh
HTTPS_PROXY= HTTP_PROXY= ALL_PROXY= dotnet restore Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj --packages /tmp/phinix-f2-packages -p:AutofacCandidateVersion=8.4.0 -p:NuGetAudit=false
dotnet restore Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj --packages /tmp/phinix-f2-packages --source /tmp/phinix-f2-packages -p:NuGetAudit=false
dotnet build Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj --configuration Release --no-restore
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
dotnet exec --depsfile Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.deps.json --runtimeconfig Tests/ClientCompositionRuntimeTests/runtimeconfig.json Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe --host-dependencies Output/phinix-rework/Common/Assemblies
git diff --check
```

Online restore used approved network access and /tmp cache. Final exact-version restore reused downloaded packages locally. The narrower initial 9.3.4 probe passed both runtimes; after adding real activity operations .NET 10 exits 1 while Mono exits 0. Logs: /tmp/phinix-f2-{mono,dotnet}.log; comparison lock: /tmp/phinix-f2-autofac-9.3.4.lock.json. Full solution tests were not rerun because shipping sources/dependencies did not change.

Chat inspection: module Register constructs the graph manually. FrameworkClientChatServiceAdapter constructor takes only chatApi and Initialize supplies feed/store/users/settings; ChatUiHostContext similarly initializes required session/settings/events/transport later. Next slice moves required ordinary-service dependencies to constructors and pairs Stop/Dispose, with one composition/module publication entry. Keep the container in Client composition, add only necessary neutral factory/scope contracts, and preserve identical official/third-party discovery/composition. Trade APIs stay borrowed and resolved when needed. These production contracts and Chat changes are not implemented yet.

No new game deployment is required for preparation. After a complete Chat pilot and coordinated host dependency package exist, check actual assembly origins, one chat send/receive, reconnect and repeat, two test saves, normal quit and example disable/enable; expect no duplicate subscriptions/stale callbacks/cleanup errors. Current shipping output stays the accepted F1 package; do not manually copy probe/cache DLLs into the game. F2 integration and real Unity/Mono acceptance remain pending. Local edits only; no commit/push.


### 11.3 F1 commit (2026-10-07)

Committed directly on dev as authorized: `702981b`, refactor(client): separate extension startup and lifecycle cleanup. Parent/rollback baseline: `f312c38940c8026f3f680089cf99b039402dc025`. Twelve files contain production lifecycle changes, Phase35 regression coverage, bilingual lifecycle guidance and the [F1 handoff](F1-Lifecycle-Handoff.md). F2 probes, both unified plans, other parallel drafts, the existing interface working-tree state and generated files were excluded. Not pushed. Staged diff check passed; source was unchanged since section 11.1 validation, so tests were not repeated. Revert this commit when undoing F1 while preserving other work; do not reset/clean parallel changes.


### 11.4 F2 production composition (2026-10-07)

Input: dev/702981b plus retained parallel working-tree state. Implemented neutral client factory/builder/scope, the client-owned Autofac 8.4.0 SDK library and pinned runtime distribution, constructor-injected Chat graph with explicit subscription startup and scoped cleanup, stale module/connection/game notification rejection, image cancellation/texture release, and protected managed-package host-library identities/aliases. Module discovery remains the ordinary parameterless path for official and third-party modules. Host/Utils and abstraction assembly versions stay 0.9.7.0 / 1.8.0.0; the new composition assembly is 1.0.0.0. No settings/save/protocol/ACK/item/installation transaction changes.

Evidence: clean followed by full solution build passes with 0 errors/7 existing warnings; the earlier single --no-incremental command loses already-built references and fails, recorded separately. Actual composition/runtime/Chat-service regressions pass 52 assertions on Mono, .NET 10 and preloaded packaged dependencies. Phase35 passes; Managed passes 943 main assertions plus six children on each runtime; all 16 existing Chat scenarios pass, including actual complete passive graph registration and ordinary host construction. Validator builds, six snapshot tests and 19 mock admission/publication tests pass. Equivalent read-only packaging check passes 30 required files and nine unique composition assets matching real build bytes; pwsh is unavailable, so its script was not executed. Existing release profile digests were inspected and are historical, not the locally rebuilt artifacts; module IDs/dependency graph stay unchanged. No blind profile rewrite, independent index publication or game test.

Full commands, source ownership, scope semantics (including rejection of async-only owned resource types), validation limitations and short game steps: [F2 handoff](F2-Composition-Handoff.md). Both plans and unrelated drafts/fixtures/IDE/output/interface state are retained. Source/tests/lock files/docs remain uncommitted; no branch/push. Deploy the entire matching main package for the game smoke; after acceptance, commit F2 as a separate batch on dev, then begin F3.


### 11.5 F2 user acceptance (2026-10-07)

The user explicitly reported “F2 pass”; F2 game acceptance is recorded as passed. No per-step results or complete log were supplied, so do not invent individually confirmed reconnect/save/quit checks, blanket mod compatibility or fixes to historical business issues. F2 has no remaining game-acceptance gate. Current dev/702981b still has uncommitted F2 production work and preserved parallel edits. This turn records acceptance only; no repeated tests, commit or push. Next: commit F2 as a separate batch on dev, then enter the F3 store-operation baseline and Stateless pilot. F4 retains the old-entry deprecation policy in section 4.1.

### 11.6 F2 commit and F3 implementation (2026-10-07)

Accepted F2 was committed directly on dev as `58c043e` (37 files); no push or new branch. Parallel interface/IDE/fixture/output/draft edits are preserved. F3 implements the Store operation transition pilot, pinned Stateless 5.20.1, generation/cancellation/terminal-stop rules and deterministic controller/transaction-boundary tests. Existing durable transaction and protocol services are unchanged. Full evidence and game checklist: [F3 handoff](F3-Store-State-Handoff.md). F3 game acceptance remains pending; do not proceed to F4 or commit F3 on inferred acceptance. Section 4.1 old-entry deprecation remains an F4 requirement.

### 11.7 Deferred extension-management issues (2026-10-07)

User requested diagnosis and deferred repair only. Track OPEN EM-01 (disabled managed module vanishes from discovery-based settings list; package desired state remains Enabled) and EM-02 (reinstallation planning collapses module-disabled or other candidate conflicts into ManagedDependencyConflict). Source paths, confirmed facts, alternative causes and future acceptance: [known issues](ExtensionManagement-Known-Issues.md). No behavior changes or tests in this audit. F3 game acceptance remains pending; do not count this report as F3 pass. Repair scheduling is deferred pending user instruction.

### Extension management unblock implementation (2026-10-07)

The user authorized fixing EM-01/EM-02 before F3 acceptance. Both management lists now expose missing saved disabled IDs as UI recovery entries; package and module intent are separated. Planner retains the actual candidate rejection and Store provides actionable recovery text. Disable preferences are never silently cleared; explicit restore permits reinstallation after completed removal. Existing transaction/assembly gates remain. Verification and game steps: [issue record](ExtensionManagement-Known-Issues.md). F3 and this fix still await game acceptance; no commit/push.

2026-10-07 priority update: repair EM-03 local LoadFolders path normalization and deliver a complete package before F4-A. Evidence and uncertainty are recorded in ExtensionManagement-Known-Issues.md. F4-A has not started; resume after the package is retested against the actual mod list.

### F4-A implementation record (2026-10-07)

Accepted F3/management repair committed directly on dev as `1be33dd`, no push. Pending LoadFolders code/tests and parallel edits were excluded; stage docs include the pending diagnosis. F4-A introduces neutral client Compose/base with the internal ordinary Register bridge and migrates Chat. Client API/CLR version is 1.9; static inspection recognizes only the trusted bridge. Evidence: F4-A-Author-Entry-Handoff.md. Local mod check narrowing is a separate F4-F2 batch. Do not enter F4-B or claim deprecation complete yet. Version proposal: deprecate old client author adapters in the post-F4 accepted host 0.9.8 release; remove in host 1.0/client abstractions 2.0 only after migration/rollback gates. Confirm/freeze versions in F4-H. Game acceptance and separate F4-A commit remain pending.

### F4-B implementation record (2026-10-07)

Latest package game check accepted; committed EM-03 `c0c8d4e` and F4-A `b339cb6` separately, no push. F4-B migrates both samples to Compose/scopes: Example 1.0.3 and Playtest 1.4.0, minimum abstractions 1.9, preserved settings/gameplay. Actual DLL Mono registry/localization/disable/failure checks and actual ZIP install/removal/recovery/reinstall checks pass. Commands/local test package: [F4-B handoff](F4-B-Samples-Handoff.md). No live candidate publication. Game check and separate commit pending before F4-C.

### Prioritized minimal store/chat repair (2026-10-07)

User requested an immediate working download package. F4-F2 disk-veto removal is implemented ahead of Inventory migration: ordinary mod LoadFolders/DLLs are no longer scanned; actual loaded identity and declared dependency checks remain. Chat callbacks dispatch UI work and isolate subscriber failures. See Store-Chat-Minimal-Fix-Handoff.md and RimWorld-Ownership-Boundary-Audit.md. Game acceptance pending. Full F4-F2 is not complete: fresh environment capture, scoped legacy loading/resolution, and whole-mod installer boundaries remain separate batches. F4-B work is preserved and its samples are excluded from this main repair ZIP.

### F4-F2b owned client loader candidate (2026-10-07)

The store/chat-history repair was accepted by the user. Implemented the next separate boundary batch: client probes only its ModContentPack roots, and a disposable resolver handles owned requesters/declared exact identities. Ordinary game-loaded submods keep the ordinary registry path; legacy server loading remains compatible. 13 loader assertions per runtime, 3108 managed assertions per runtime and the full solution build passed. See F4-F2b-Owned-Loader-Handoff.md; game acceptance pending. Preserve F4-B work, no sample publication. Whole-mod installer metadata and fresh environment capture remain pending.

## Store installation review follow-up (2026-10-07)

Before continuing plugin migration, validate the generic release-family host policy, compact schema 2 installation work paths and readiness diagnostics described in [Store-Install-Flow-Review.md](Store-Install-Flow-Review.md). F4-F2 must then refresh environment facts on the main thread before commit and classify target/dependency failures separately from uncertain global ownership. Unify all shop error presentation and safe diagnostics before F4-F2c; this batch improves key errors but does not complete that broader work.

### Accepted store fixes and environment refresh candidate (2026-10-08)

The user reports successful installation after deployment was corrected. Record acceptance of the preceding store repair; do not imply F4-B sample packages or every manual step were tested. The next F4-F2 batch refreshes environment facts through the main-thread dispatcher before transfer and before installation, with cancellation/deadline handling and immutable host/path checks. See [F4-F2-Environment-Refresh-Handoff.md](F4-F2-Environment-Refresh-Handoff.md). Game validation of this new batch is pending; scope classification and unified shop error presentation remain next. Preserve F4-B and unrelated parallel work.

### Store failure classification candidate (2026-10-08)

The temporary F4-F2 failure-presentation work is implemented and regression-tested; see [F4-F2-Store-Failures-Handoff.md](F4-F2-Store-Failures-Handoff.md). The two shop controllers/views share safe diagnostics and translation classification. Uncertain owned records still block mutation; no per-mod exceptions or ownership bypasses. Game validation remains pending. Return to the F4 composition track, checking F4-B sample acceptance before F4-C Inventory; retain F4-F2c whole-mod and theme boundary work as separate batches.

### F4-C Inventory composition candidate (2026-10-08)

The user accepts the preceding batch and explicitly requests behavior parity before/after DI. Inventory now uses Compose/scope for its ordinary dependencies and UI; six facade APIs, one settings/quick-settings provider, ledger/journal ownership and contract version remain unchanged. The same probe loaded real before/after DLLs: both runtimes produced identical facts, with 24 baseline and 52 new assertions; the unchanged 13 inventory scenarios passed against both DLLs on both runtimes. Details and game steps: [F4-C-Inventory-Composition-Handoff.md](F4-C-Inventory-Composition-Handoff.md). Pending game validation; F4-D save lifecycle remains separate. Existing F4-B/unrelated changes are preserved.

### F4-D1 Inventory lifetime candidate (2026-10-08)

The user accepts F4-C. Split F4-D into D1 attachment/shutdown and D2 menu/reentry lifetime. D1 adds activation generation and current-game component membership checks, and continues cleanup after individual release failures. Actual before/after behavior facts match on both runtimes; 62 candidate assertions and unchanged 13 domain scenarios pass. Full build and distribution checks pass. See [F4-D1 handoff](F4-D1-Inventory-Lifetime-Handoff.md) for exact commands, limits and game steps. Game acceptance remains pending; menu journal release and stale snapshot handling remain D2. Preserve all parallel work.

### F4-D2 menu/reentry candidate (2026-10-08)

User authorized continuation after D1; no detailed game acceptance log supplied. D2 releases journals and clears display state when leaving the current game, supports initialized-component reattachment, resets inherited new-game save paths and guards recovery actions. Persistence/protocol/ownership algorithms unchanged. Candidate 77 assertions, original 13 domain scenarios with before/after actual DLLs on both runtimes, full build and distribution pass; Mono recovery-guard coverage has a reference-assembly limitation. See [F4-D2 handoff](F4-D2-Inventory-Lifetime-Handoff.md). Complete F4-D game checks before F4-E; no commit/push, preserve parallel changes.

### F4-E1 Trade passive composition candidate (2026-10-08)

User authorized continuation after F4-D2; no detailed game acceptance log. Split F4-E into E1 passive composition and E2 activation/connection/Inventory-token lifetime. E1 scopes the seven core/UI implementations and preserves eleven APIs and original activation paths. Adds missing idempotent tab/list eight-event cleanup. Actual baseline/candidate: 23/42 assertions, identical facts on both runtimes; unchanged 10 legacy scenarios pass against both DLLs on both runtimes. Full build and 31 distribution checks pass. See [F4-E1 handoff](F4-E1-Trade-Composition-Handoff.md). E2 and manual F4-D/Trade checks remain; no commit/push, preserve parallel changes.

### F4-E2 Trade lifetime candidate (2026-10-08)

User accepts E1. E2 introduces a private activation scope owning registration/connection leases, delivery helper and default behavior while borrowing Inventory/core/host services. Activation/Start are idempotent; partial failure rolls back, cleanup continues after release errors, queued work and captured module callbacks are invalidated on stop, and stopped facade/UI mutations are rejected. Normal actual-DLL facts match (35 baseline/112 candidate assertions on both runtimes), including startup and completion ACK timing. Original 10 legacy scenarios and full/distribution checks pass. See [F4-E2 handoff](F4-E2-Trade-Lifetime-Handoff.md) for isolated baseline methodology, limits and manual quantity/reconnect steps. F4-E implementation ready for game acceptance; next F4-F Store composition. Preserve parallel work, no commit/push.

### F4-F1 Store providers candidate (2026-10-08)

User accepts E2. Split F4-F into provider composition (this batch) and a later activation/controller/view-factory batch; do not reuse the reserved F4-F2 boundary label. Store uses ordinary Compose/scopes for three providers, with idempotent release and independent shutdown attempts after window/localizer failures. Activate and transaction/state/environment code unchanged. Actual baseline/candidate facts match (6/38 assertions on both runtimes); store 946 and managed 3212 assertions per runtime pass, including 2224 store operations. Full build, 14-source validator snapshot and 31 distribution checks pass. See [F4-F1 handoff](F4-F1-Store-Composition-Handoff.md). Game acceptance and activation migration remain; no commit/push, preserve parallel changes.

### F4-F Store composition implementation complete (2026-10-08)

User requested completing Store before combined game validation. Activation scope now owns localizer/controller leases, icons, independent-view factory and notice lease; endpoint services/providers are borrowed. Repeated activation rebuilds resources with stable APIs; failures roll back and old views/factories/notices become inert. Original controller, F3 flow, environment recapture and install/recovery semantics unchanged. Actual baseline/candidate: 16/93 assertions on both runtimes, identical startup/presentation facts, with an explicitly excluded native-only old icon teardown. Store 946, managed 3212 per runtime, 14-source validator and 31 artifact checks pass. See [Store completion handoff](F4-F-Store-Completion-Handoff.md). F4-F code complete; combined game acceptance pending, then F4-G. No commit/push, preserve parallel work; existing F4-F2 boundary follow-up stays separate.

## F4-G candidate (2026-10-08)

User reported the combined Store test passed; F4-F acceptance is recorded without inventing individual game steps. F4-G uses the ordinary Compose entry and owns adapter construction/subscriptions/registration lifetimes through one private scope; optional Trade APIs and endpoint services remain borrowed. Actual old/new DLL probes pass 58/68 assertions on .NET and Mono, including unchanged early history, mode switches and authoritative trade results. See [F4-G handoff](F4-G-LegacyAdapter-Handoff.md). Game acceptance is pending. No commit/push; preserve parallel edits. Complete F4-H and the tracked F4-F2c whole-mod/theme boundaries before overall F4 completion/F5.

## F5-A local rehearsal authorized and passed (2026-10-08)

User authorized pushing dev and continuing F5 locally. origin/dev is at 328c556. A local Shared core snapshot plus net472 ClientProbe/net10 ServerProbe pin the same gitlink and recursively obtain protobuf from a local mirror; reproduced fresh clones build and run without original source paths. Three runtime runs pass 11 assertions each; 16 isolation/import/ownership checks pass; offline restore uses existing caches. Record original SourceLink local-URL failure and repeated transport Dispose limitation rather than hiding them. See [F5-A evidence](F5-A-Local-Rehearsal-Handoff.md). F5-A complete, full F5 still requires contract separation, full consumer/packaging/mixed test and server-image checks. No new GitHub repositories or main source moves; F6 is not started.

## F5-B contracts/full consumers checkpoint (2026-10-08)

Local Shared owns game-free dual-target Chat/Trade contracts; full client wrappers retain original game interfaces and assembly identities. Fresh complete consumers pin the same Shared commit and build successfully. Four contract pairs match 988 metadata rows and 400 method bodies; 11 isolation and 31 artifact checks pass; isolated actual server startup/exit passes. Acquisition corrections, private legacy NuGet/game references and SourceLink/Docker limits are explicit. See [F5-B evidence](F5-B-Contracts-Full-Consumers-Handoff.md). Mixed test migration, complete acquisition replay, plugin/game acceptance and image gates remain for F5-C; no main source moves/F6.

## F5-C local runtime/acquisition checkpoint (2026-10-08)

Complete generator replay and fresh recursive consumers pass full builds. Phase35 splits by ownership with all 18 original scenarios/117 assertions retained; store 968, managed 3212 per runtime, loader 13 per runtime, client/inventory/trade/store composition 69/77/112/93, legacy 11 scenarios/68 assertions, Chat 17 and Inventory 13 scenarios pass. Actual local server image builds and exits cleanly with isolated data/network. Test-only Phase35 composition-source and classic Chat runtime-copy omissions are corrected in main test projects; production source moves remain local. Contracts/endpoint bytes match, packaging and reference acquisition evidence are recorded in [F5-C handoff](F5-C-Runtime-Acquisition-Handoff.md). Local automatic checkpoint passes; game acceptance, hosted publication and SDK/source-snapshot policy remain explicit gates. No F6 or main commit/push.

## F5 acceptance and F6 naming authorization (2026-10-08)

User reported “F5-C passed” and authorized continuing. Record collective acceptance without inventing separate game-step reports. Exact identities confirmed: retain HunYuan2333/Phinix-Rework for Client; new repositories HunYuan2333/Phinix-Common and HunYuan2333/Phinix-Server. F6 local history/ownership preparation is authorized; local candidates and canonical URLs are not evidence of remote publication or publisher cutover. F6-M follows F6 before F7, and F6-S remains canceled.

## F6-A local history/ownership candidates (2026-10-08)

Prepared confirmed local dev repositories with retained client ancestry and filtered Shared/Server history, exact Shared/protobuf pins and canonical submodule URLs. Fresh full builds, 117 split runtime assertions, Store/managed/DI/legacy regressions and 31 artifact checks pass; 412 source files match accepted F5. Phinix.sln remains available. No remote creation/push or original workspace replacement. Three frozen Index sources are already behind; next batch prepares pinned Client/Shared provenance and validator refresh/tests. Root license policy, independent consumers and publication cutover remain later work. See [F6-A handoff](F6-A-History-Ownership-Handoff.md). Overall F6 is incomplete; F6-M follows and F6-S is canceled.
