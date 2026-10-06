# Managed DLL implementation batches

2026-10-06 policy update: host-provided references now permit a unique same-major upgrade with unchanged name/culture/token, preferring exact identity. Actual startup freezes the binding to a declared, already loaded host assembly and logs the selected identity. Package-owned/dependency identities, hashes and compatibility ranges stay strict. This supersedes earlier blanket exact-host-reference statements; see [acceptance](HostReferenceUpgradeAcceptance.md).

[中文](托管DLL分步实现.md). 2026-10-04, branch `dev`; based on [the route assessment](ManagedDllRoutes.md). Workshop entries link full mods; GitHub packages are managed by Phinix without a RimWorld mod shell.

| Batch | Delivery | Acceptance | Status |
| --- | --- | --- | --- |
| M1 | Generic static manifest v1, stable paths and read-only ownership inventory | Strict parsing; disabled/pending-removal rows; changed bytes/extra files/corrupt records/links rejected; no DLL execution | Static contracts/inventory complete; not wired into loading |
| M2 | Preload recovery, identity/reference validation, candidate resolver and environment ownership | Disabled/removing/bad packages excluded; conflicts/missing references diagnosed; independent of shop activation | M2a/M2b code and runtime regression complete; game/platform acceptance pending |
| M3 | Generic manager inventory, package desired/current states, enable/disable/removal intent | Built-ins preserved; unloaded rows manageable; reverse dependencies explained | Code/local regression complete; game acceptance pending |
| M4 | New catalog version, managed ZIP validation and download/transaction integration | Fail closed across formats; pre/post freshness; cancellation, crash and repeated recovery | M4a–c code/regression, staging and live download complete; game pending |
| M5 | New immutable DLL Playtest release and game acceptance | No mod-list entry; tab/counter/100 silver; disable/removal/reinstallation and data preservation | 1.2.1 published; user reports basic install-to-uninstall passed; extended checks remain |
| M6a–e | Bundled store, resource/save prerequisites, downloadable RedPacket/TalentTrade and Workshop/production | Store recovery entry, old-data/missing-plugin preservation, clean upgrades, Windows/Mono/networks | M6a locally bundled; remaining batches later |

2026-10-04 distribution decision: bundle the production store and distribute RedPacket/TalentTrade as downloadable managed packages. M3–M5 order stays unchanged. M6 now has independent batches; resource access and TalentTrade save-component compatibility precede splitting. See [the distribution plan](BuiltinStoreAndOfficialPackages.md) / [中文](内置商店与官方插件拆分计划.md).

M1 uses a separate managed manifest schema v1 with fixed `management: phinix-dll`. It does not reinterpret the old shop v1 catalog/manifest. M4 adds a versioned catalog; outer stable/published transport can be reused with explicit supported catalog versions. M1 changes no online source/Worker.

Under `<SaveData>/Phinix/ManagedExtensions/`, `packages/pkg-<SHA256(sourceId + LF + packageId)>/` contains `manifest.json`, declared `Assemblies/*.dll` and optional `Resources/*`. No About, Defs, Patches or automatic game language discovery. `state/installed/pkg-<hash>.json` retains source/endpoint hash/package/version, manifest hash, fixed catalog snapshot/hash, artifact hash and installation transaction and exact path/length/hash inventory including the manifest. `state/desired/pkg-<hash>.json` binds Enabled/Disabled/PendingRemoval to that source/package/manifest. Missing/corrupt desired state never enables a package. `transactions/` is reserved for future journals/staging/quarantine outside load candidates.

M1 allocation/inspection creates or mutates no files and executes no DLLs. Receipt enumeration is the ownership entry; a directory/manifest alone establishes nothing. `ContentVerified` means exact owned bytes, not PE identity, compatibility, dependencies or successful activation; M2 establishes loadable candidates separately. Manifest contracts retain package vs CLR versions, compatibility, package dependencies, modules and entry assembly/type, external mod requirements, full assembly identity/path/hash and declared resources. Multiple modules/DLLs per package are supported.

Per-package failures preserve diagnostic rows and unrelated entries. Unknown directories are neither loaded nor deleted. Reject symlinks/junctions, aliases, duplicate case-insensitive paths, escapes, extra/missing/edited files. These generic contracts do not depend on the shop plugin. Module activation stays a separate lifecycle fact from desired package loading state.

Later installation confirmation authorizes next-start activation, not execution in the current session. Loaded-package removal records intent; next startup rechecks dependencies/ownership before transactional removal and loading. Failed recovery stays excluded and visible. Never delete settings, business recovery records or saves.

Audit source/package/version/hash, transaction/startup IDs and validation/resolution/discovery/activation/removal outcomes without credentials, remote bodies or public local paths. Keep detailed internal diagnostics and safe UI error codes. Preserve old local-mod `installation-v1` receipts and Playtest 1.1.0; do not automatically move them. Red-packet/talent-trade persistence migration is outside M1.

## M1 delivery and validation (2026-10-04)

Implemented generic immutable static contracts/strict parsing, stable paths and read-only receipt/content/desired-state inspection in Common/Utils. `ClientEnvironmentPaths.ManagedExtensions` is additive; client abstraction assembly/compatibility is 1.4.0, documented in both developer guides §8.20. See [format examples](managed-extension-protocol-v1/README.md). No inventory service or preload input is registered by the host yet; M2 wires these contracts into startup.

Unknown directories remain unowned. Damaged/identity-mismatched records remain diagnostic rows. Local source/endpoint/catalog/artifact/transaction provenance is neither a signature nor new remote approval. Root-level diagnostics, including inventory limits, require the future loader to exclude the managed candidate domain. Inspection describes observed bytes; it does not replace pre-load verification or transaction concurrency control.

Exact commands run from the repository root:

```sh
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mcs -langversion:7.2 -out:/tmp/phinix-managed-extension-mono-smoke.exe -r:Common/Utils/bin/Release/net472/Utils.dll Tests/PluginStoreRuntimeTests/Fixtures/ManagedExtensionMonoSmoke/Program.cs
MONO_PATH=Common/Utils/bin/Release/net472 mono /tmp/phinix-managed-extension-mono-smoke.exe Tests/PluginStoreRuntimeTests/Fixtures/ManagedPayload/bin/Release/net10.0/Fixture.Plugin.dll
git diff --check
```

Results: **675 store assertions passed** (585 baseline + 90 new), framework regression passed, final solution build **0 errors/6 existing warnings**, main/shop outputs refreshed. The net472 download-check tool compiled. Production Utils on Mono passed manifest/inventory/desired-state/tamper/actual and dangling symlink/no-execution checks. Candidate bytes were not loaded/executed. An earlier narrow net472 Utils build also passed with no errors/warnings. Ordinary Build is retained; the earlier full-solution Rebuild issue is outside this batch.

Existing warnings concern obsolete members/older SDK frameworks; tests/download-check also reported NU1900 because restricted networking prevents NuGet vulnerability lookup. Document links/whitespace and example manifest/receipt/state digest consistency were checked. No game, Windows junction or disk-failure acceptance, remote asset/catalog change, Worker deployment or main-repository commit/push occurred. Preload, state mutation and install/remove recovery remain M2–M4 work.

## M2a static preflight delivery and validation (2026-10-04)

M2 is split into reviewable steps. **M2a** is complete; **M2b** still needs the startup lease, transaction recovery, scoped AssemblyResolve/frozen-byte loading, generic discovery filtering and client ownership capture. M2 as a whole is incomplete. No new managed-route game acceptance is requested yet.

`ManagedExtensionMetadataReader` reads bounded PE/CLI/ECMA-335 data for full identity, AssemblyRef, target framework, entry types and PhinixExtension ID/DependsOn, without loading assemblies or constructing attributes/modules. `ManagedExtensionPayloadInspector` freezes and rechecks length/hash, matches actual metadata against the manifest, rejects hidden modules or forged entry/ID/dependencies/identity/framework and exposes only copies of inspected bytes.

`ManagedExtensionCandidatePlanner` uses the exact inventory manifest that supplied inspected bytes and trusted facts supplied by the host. Invalid/disabled/removing/missing-intent/uninspected rows are excluded; root uncertainty excludes the entire managed domain. Game/Phinix/abstraction compatibility, active external mods, package version dependencies, CLR references and module dependencies are independent gates. Source/package, assembly and module collisions have no enumeration winner. Package cycles fail; later provider rejection propagates. Cross-package CLR references require explicit package dependencies.

Each result includes immutable `ManagedExtensionCandidateAudit`: startup ID/stage/stable code, source/package/record/version, manifest/catalog/artifact hashes and installation/state-operation IDs. Refused results expose no executable payload. This batch returns audit data; M2b must connect host log output and inspection/resolution/discovery/activation/recovery events. No public local paths, credentials or exception bodies are added.

Current boundaries: actual TargetFramework must be `.NETFramework,Version=v4.7.2`; CLR full identities match exactly, without implicit binding redirects from compatibility ranges. Entries must be public, concrete, non-generic IPhinixExtensionModule types with public parameterless constructors; same-assembly inheritance is supported, external-base inference is not yet supported. Multi-module assemblies, pointer/Edit-and-Continue tables, mixed-mode/native-entry/32BITREQUIRED are rejected. Tables 0–44 are bounded to 200000 rows per table/1000000 total, 20000 types/256 references/64 attributed modules, 2 MiB combined type names, 16 KiB read blobs and inheritance/nesting depth 64. This is not complete IL verification, signature trust or a code sandbox. Internal module cycles, loaded-assembly discovery scope and transaction concurrency/recovery remain final M2b startup gates; a non-null payload does not authorize execution.

`Tests/ManagedExtensionRuntimeTests` references production Utils directly. Real net472 plugin/helper/provider DLLs are copied as data, with a static-initializer sentinel. CI now runs the .NET 10 harness; remote CI has not run. Exact validation commands:

```sh
dotnet restore Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --ignore-failed-sources -p:NuGetAudit=false -p:BuildInParallel=false
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

**230 new assertions passed separately on .NET 10 and production net472/Mono**, including 128 deterministic single-byte mutations, truncation/PE corruption, actual entries/references, frozen bytes, compatibility/dependencies/collisions/cycles, inventory uncertainty and audit correlation. Plugin/helper assemblies remained absent from AppDomain and the initializer sentinel did not execute. Existing **675 store assertions** and framework regression passed. Full main/shop Build had **0 errors/7 existing obsolete/old-SDK warnings**; net472 download-check compiled with existing NU1900.

PowerShell is unavailable, so `.github/scripts/check-artifacts.ps1` could not run directly. Equivalent Python checks passed for its declared required files, preview sole DLL/identity, LoadFolders and exclusion of game reference/test DLLs. No new runtime libraries. No game, Windows, remote CI or actual managed loading/recovery/removal acceptance; no remote catalog/assets/Worker changes. Business protocols, item ownership and plugin data were unchanged.

## M2b host startup and removal recovery (2026-10-04)

Code and runtime regression are complete for the host gates left pending in the historical M2a record above. **Next: M3** package operations in the generic extension manager, followed by M4 managed downloads. The shop still downloads the older local-mod format; a new DLL Playtest with tab/100-silver game acceptance belongs to M5. No remote asset/catalog/Worker changes or automatic legacy migration occurred.

`Utils.Framework.ManagedExtensionRuntime` depends on neither the shop nor game assemblies. After mod loading, the client invokes it through the existing main-thread dispatcher, then uses generic discovery/Register/Activate/Shutdown; automatic connection follows framework initialization. Read-only `IManagedExtensionInventoryService` reports startup inventory, diagnostics and loaded-byte facts, separately from module lifecycle. Client abstractions are **1.5.0**, preserving old constructors and adding explicit managed source/package/root ownership. Actual Assembly objects establish byte-loaded ownership without fabricated RimWorld mod rows or SourceModRoot. Host loading is independent of shop activation.

A lifetime exclusive `state/runtime.lock` lease serializes startup and future M3/M4 writes through the same host coordinator; real cross-process Mono locking passed. Recovery precedes loading. Exact file trees/frozen bytes, the combined host/managed module graph (maximum 4096 declarations) and actual CLR references are checked. Fully module-disabled packages are not loaded. Missing providers, conflicts, cycles and later rejection propagate. Aggregate frozen DLL bytes are limited to 256 MiB; CLR reference cycles fail.

Only approved frozen byte copies reach Assembly.Load. The scoped resolver accepts owned requesting assemblies and their declared references, returning exact already-loaded host objects or approved managed objects without probing unknown files. A generic guard prevents the older Phinix file resolver from serving owned requests; the managed root is never a legacy probe directory. Actual entry types are rechecked; failed packages/partially loaded rejected assemblies are excluded from generic discovery. Loaded bytes cannot unload. This is not a code sandbox and does not control other mods' resolvers.

Removal durably records original receipt/desired state/manifest in `transactions/rm-<operationId>.json`, atomically moves the complete package into same-root quarantine, then rechecks and deletes only owned files. Replay accepts canonical journals, unchanged ownership and either the complete original tree or the remaining quarantine subset. Replay is idempotent. Reverse checks include disabled packages, host modules and loaded/referenced assembly names; pending dependency chains remove dependents first. Changed/extra files or directories, state/journal changes and uncertainty preserve evidence; unknown transactions close the managed load domain. Only package code and its ownership/desired-state records are removed, never ExtensionData, settings, business recovery records or saves.

Public JSON audit includes UTC time, startup ID/sequence, stage/stable code, source/package/record/version, manifest/catalog/artifact hashes, installation/state-operation IDs and assembly/module correlation, without absolute paths, credentials or exception bodies. Recovery exceptions reach the internal diagnostic callback; the client prints details under DevMode. Lifecycle records come from actual generic results. Inventory remains a startup snapshot; manager writes/UI are not delivered yet.

Exact commands run from the repository root:

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

Each **.NET 10 and production net472/Mono** run passed **307 parent assertions plus 15 real-load/registration child assertions** (322 per runtime): six interruption checkpoints, replay, tamper/unknown contents, data preservation, real cross-process locks, combined module cycles/valid chains, generic lifecycle, scoped resolution, ownership and post-disposal exclusion. **677 store assertions** and framework regression passed. Full main/shop Build: **0 errors/7 existing obsolete-API/old-framework warnings**. net472 download-check compiled with existing NU1900 network warnings. PowerShell was unavailable; equivalent Python artifact checks passed required files, preview sole DLL/identity, LoadFolders and game/test DLL exclusions. Diff and current-batch document links passed; pre-existing guide-link issues outside §8.20 were not repaired. An earlier standalone Client build using Release 1.6 failed legacy referenced-project configuration mapping; the complete solution mapping passed.

Actual game, Windows/macOS, remote CI and physical power-loss acceptance were not run. .NET 10/macOS explicitly refuses unsupported FileStream.Lock; other production net472 platforms remain M6 work. Business protocols, item ownership and legacy plugin data were unchanged. Startup/automatic-connection timing changed, so a small game smoke is appropriate: restart, verify connection and Chat/Trade/Shop tabs, extension manager and mod-settings entry, and inspect ManagedStartupCompleted/final module states. Screenshots/logs are needed for game evidence. M3 adds unloaded-package enable/disable/removal acceptance; M4/M5 add complete managed installation and 100-silver acceptance.

## M3 package management and asynchronous host UI (2026-10-04)

M3 code and local regression are complete; game acceptance is pending. **Next: M4** versioned catalog, managed ZIP validation and download/install transactions. M5 publishes the new DLL Playtest with tab/counter/100-silver actions. The first formal store uses text summaries, labels and bundled generic icons, without remote images or README fetches; see [the distribution plan](BuiltinStoreAndOfficialPackages.md).

The host registers Common/Utils `IManagedExtensionManagementService`. `Refresh` rereads ownership/content/desired state while retaining immutable startup facts; unloaded and failed rows remain visible. Extension management adds Modules / Managed packages pages, preserving built-in controls and logs. Cards show desired state, byte-loaded facts, diagnostics and restart requirements. The package Modules menu can restore unloaded modules; colliding declarations cannot change another module's settings.

Enabling rechecks bytes, PE metadata, compatibility, host/package dependencies, the combined module graph and rejection propagation. Disabling protects enabled consumers. Removal also protects disabled consumers; explicitly marking dependents for removal first permits the entire chain to recover in dependency order at the next startup. Host module dependencies and unmanaged CLR references conservatively block removal. Package and module switches remain independent intents. Neither changes the current session's Activate/Shutdown results or deletes loaded code/business data.

The same lifetime-lease coordinator serializes writes. Each operation rereads inventory and compares source/catalog/artifact/install/manifest/state-operation identity, refusing stale views. A complete same-directory JSON is flushed and atomically committed with `File.Replace`, without delete/move fallback. Original state, receipt, temporary bytes and the complete tree are rechecked before commit; cancellation applies before that point. Audit events correlate startup/stage/code with the new operation ID. Replacement exceptions report `ManagedStateWriteUncertain` and require refreshed inspection rather than claiming rollback. Unknown transactions/ownership block writes and changed evidence is preserved.

The UI thread captures existing module settings, draws, saves module switches and consumes completion. Inventory scans, hashes/metadata and desired-state writes run through a background controller. Rows are virtualized and cached by version. Closing cancels work; saved-intent/failed-refresh outcomes are distinct. Detailed errors remain internal. Deterministic geometry assertions cover tiny and normal windows.

Exact commands run from the repository root:

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release --no-restore
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

Each **.NET 10 and production net472/Mono** run passed **355 parent + 15 normal real-load child + 18 loaded-package management child assertions** (**388 per runtime**, 66 added since M2b). Coverage includes unloaded rows, stale state/provenance, reverse dependency chains, cancellation, malformed PE/changed files, unknown transactions, pre/post-commit interruptions, unchanged loaded-code/lifecycle behavior, the asynchronous controller and successful commit followed by refresh failure. Existing **677 store assertions**, framework and responsive-layout regression passed. Full main/store Build and net472 download-tool compilation passed with existing warnings. PowerShell was unavailable; equivalent artifact checks, translation-key parity and diff checks passed.

Actual game, Windows/macOS, remote CI and physical power-loss acceptance were not run. Fault injection cannot prove platform-specific replacement/flush durability. No new managed test package is published: an empty Managed packages page is expected until M4/M5; smoke-test navigation, settings recovery and existing module controls first. Complete install/restart/actions/disable/removal game acceptance follows M4/M5. The old store still uses local-mod packages, with no automatic receipt migration. In-flight business and TalentTrade save removal protection remain M6 prerequisites. No remote catalog/Worker changes or commit/push occurred.

## M4a versioned catalog and managed ZIP static validation (2026-10-04)

M4 now has three independently verifiable batches. **M4a code/local regression are complete; M4 as a whole is incomplete.**

| Batch | Scope | Status |
| --- | --- | --- |
| M4a | Catalog v2, explicit routes, stable/published schema binding, managed ZIP/real metadata checks | Code/regression complete |
| M4b | Generic host installation under the lifetime lease; combined pre-install dependency/current-inventory checks, commit/cancel/startup recovery/replay | Local implementation/regression complete |
| M4c | Generator/Worker/transport/cache/planner/UI integration, pre/post-confirmation freshness, budgets/cancel/audit, legacy receipt removal compatibility | Implementation/publication/deployment/live download complete; game pending |

Separate ManagedStoreCatalogSnapshot / ManagedStoreRecord types do not fabricate RimWorld Mod IDs or feed the old local-Mod installer. Catalog schema 2 distinguishes GitHub phinix-dll / managed-dll-zip from Workshop rimworld-mod listings. Embedded managed manifest v1 binds complete declarations to listing identity. Author/license, a 1024-character summary and up to 8 tags come from the fixed catalog, without image/README URL fields. Old catalog/local-Mod manifest semantics and installation-v1 receipts remain separate.

Outer stable/published schema 1 explicitly declares catalogSchemaVersion 2. Source/snapshot/length/hash and fixed catalog.json asset identity are bound. New parsing entry points remain separate: old readers reject version 2. Fixed hashed resource URLs are reused; arbitrary download URLs and Workshop downloads stay unavailable. M4a invokes no new networking/cache/Worker path and changes no online source.

ManagedStorePayloadValidator freezes and hashes compressed bytes, rejecting excess size, traversal, links, aliases, undeclared contents/directories and mod shells. Only manifest.json, declared Assemblies DLLs / Resources and their ancestor directory entries are accepted. ZIP/raw manifest hashes, complete parsed declarations and every file's length/hash are checked independently. Production static PE/CLI inspection verifies full identities, net472, modules/entry types/DependsOn. Nothing loads or executes, including attributes. Frozen manifest/inspection reports do not authorize different/subsequently changed input. Verified package Resources do not introduce store image indexing or native game content discovery.

Compatibility, combined host/package graphs, currently loaded identities, inventory and transaction authority remain M4b checks; static validation is not successful installation. Catalog/ZIP/file/expanded/entry/ratio limits are bounded; large-package CPU/memory and Unity platform acceptance remain pending. See [protocol examples](managed-store-protocol-v3/README.md) / [中文](managed-store-protocol-v3/README.zh-CN.md). Artifact/DLL digest examples are placeholders, not releases.

Exact commands run from the repository root:

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStorePayloadCheck/PluginStorePayloadCheck.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

Each **.NET 10 and production net472/Mono** run passed **427 parent + 15 + 18 real-load child assertions** (**460 per runtime**, 72 added). Coverage includes routes, old/new refusal, metadata hash/schema mismatch, identity conflicts, invalid text/tags/formats, fixed resources/nonselected rows, real multi-DLL input, changed resources, native Mod/undeclared code/directories, paths/links/aliases, duplicate/missing files, compression bombs, forged modules/bad PE, double manifest binding, property ordering and protocol examples. Existing **677 store assertions** passed. Full main/store Build: **0 errors/2 existing SDK warnings**; net472 download/net10 payload tools compiled with existing NU1900 network warnings. PowerShell was unavailable; equivalent artifacts and current-batch links/hashes/diff checks passed.

Game, Windows/macOS, large-package performance, remote CI, real download and physical interruption acceptance were not run. No new GitHub assets/catalog, Worker deployment or commit/push occurred. Current store UI/downloads still use local-Mod packages; game clicks cannot prove new managed installation. Next is M4b, then M4c; complete managed Playtest game acceptance is M5. Business protocols, item ownership and existing plugin data were unchanged.

## 2026-10-05: local first-release candidate

M4b host transactions and M4c client networking/UI are locally implemented/regression-tested. The user requested finishing the store first, so M6a bundling is integrated ahead of game acceptance. Current validation: 562 + 15 + 18 + 16 = 611 assertions per .NET 10/Mono runtime, 677 old-store assertions, 119 Worker tests. M5 Playtest 1.2.0 is built/verified locally. After explicit authorization, publication and staging deployment completed; no-proxy production download passed on .NET 10/Mono. Game acceptance is incomplete. See [current delivery/checklist](StoreFirstRelease.md).
