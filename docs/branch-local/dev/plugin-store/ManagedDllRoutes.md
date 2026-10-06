# Workshop mods and managed DLL extensions

> Implementation update: M1–M3 host loading/recovery/management and M4a catalog v2/managed ZIP validation are implemented locally; M4b/M4c installation/network integration and game acceptance remain pending. See [implementation batches](ManagedDllImplementation.md).

[中文](双路线与托管DLL扩展评估.md). Updated 2026-10-04, branch `dev`. The user selected the product direction; this preserves the initial code assessment and plan; the implementation update above and linked batch records describe current progress.

## Product decision

| Entry | Store responsibility | Installation and management |
| --- | --- | --- |
| Complete RimWorld mod | Index, description and Workshop link | User subscribes; Steam downloads; RimWorld handles activation and native content |
| Phinix DLL extension package | Index, dependency planning, download, verification and installation | Phinix loads from its managed directory and provides enable, disable and uninstall controls |

Managed DLL packages have no generated About, ModsConfig entry or RimWorld mod identity. A ZIP remains a suitable transport container for DLLs and manifests. Native Def/Patch XML content belongs on the Workshop route; managed files do not automatically participate in the game's language/texture content discovery. The first DLL sample can implement a tab, counter and confirmed silver action using code and package-owned text.

Distribution source does not determine installation mode. Explicit package format/management semantics prevent interpreting every GitHub item as a downloadable mod. Alternate distributions of the same package/module/assembly must not load together.

This decision replaces the earlier standalone-local-mod and generated-mod-shell recommendations. Existing ZIP transaction code and Playtest 1.1.0 remain historical evidence and reusable infrastructure.

## Existing support and required changes

`Client.GetExtensionProbeDirectories` already includes the main package's `Common/Extensions` and active mods' `Assemblies`. Discovery, dependency ordering, registration and activation are generic; downloaded modules need no privileged business startup branch.

The current `ExtensionAssemblyLoader` loads before installing its resolver, indexes filenames, and scans late files on resolution misses. Managed loading requires a static inventory, verified file/assembly identities, explicit conflict handling, and a resolver installed before loading. Disabled, pending-removal, invalid or conflicting managed packages must never enter Phinix's resolution candidates. Also account for CLR LoadFrom automatic adjacent-directory dependency probing: allowed directories must not contain unverified DLLs, and hidden references/cross-package resolution require target-Mono tests beyond resolver-callback tests. This is loader policy, not a security sandbox against other in-process code calling CLR load APIs directly.

Current activation policy runs after assembly loading. Preserve module activation semantics while adding package loading state. A package can contain multiple DLLs/modules. The manager needs inventory rows for disabled/unloaded, newly installed, failed and pending-removal packages; reflection results alone are insufficient.

`ClientEnvironmentCapture` currently assigns ownership through RimWorld mod roots; unmanaged roots cause `ModuleOwnershipUnknown`. Extend generic package origin/management facts without fabricating mod ownership. Host/framework services read inventory and recover startup transactions; the store handles networking and installation requests through contracts. Already installed packages must load and remain manageable when the shop is disabled or absent.

## Storage and lifecycle proposal

Use `<SaveData>/Phinix/ManagedExtensions/{packages,state,transactions}` with deterministic source/package directory keys. Separate download/staging/quarantine from loadable packages and preserve same-filesystem directory move constraints. This is a proposal, not a frozen path API. Keep existing runtime data identities/storage contracts; uninstall never deletes settings, saves or business recovery data. Do not write downloaded packages into the main mod's `Common/Extensions`.

Startup: read state and recover intents → verify candidates/dependencies → register identity-based resolver → load allowed assemblies → discover modules → dependency order → Register/Activate. Recovery failures exclude affected packages, preserve evidence and allow unrelated modules to start. Game/environment capture still follows main-thread rules.

Installation confirms the plan and authorizes its new packages' next-start activation. Download and commit do not execute the new DLL in the current session. Enable/disable records desired state and displays pending restart. Disabled managed packages are excluded before loading, while built-ins retain their existing activation policy.

Uninstall verifies ownership and reverse dependencies. An unloaded unused package can be removed transactionally. A loaded package receives a durable pending-removal intent; on next startup, before loading, the host rechecks facts, quarantines and removes it. Failed removal remains excluded and visibly recoverable. Users need no RimWorld mod-list step. Package loading and individual module activation remain separate when multiple modules share assemblies.

The existing host shares its AppDomain with game objects and offers no assembly-unload lifecycle. Use restart boundaries, then validate on Unity/Mono. Microsoft documents unloading through the containing AppDomain in [assembly loading and unloading](https://learn.microsoft.com/en-us/dotnet/standard/assembly/load-unload); an [AssemblyResolve handler](https://learn.microsoft.com/en-us/dotnet/standard/assembly/resolve-loads) should handle only known identities and avoid recursive name-based loading.

## Dependencies, protocol and compatibility

Package dependencies, CLR references and module DependsOn remain distinct. Built-in/host/framework DLLs are provided dependencies, never duplicate downloads. External full mods are checked against game facts and linked for subscription. Reverse checks cover managed packages, known built-ins and identifiable active mod extensions; legacy native mods do not automatically declare dependencies using the new contract.

Current catalog v1 requires `rimWorldPackageId`; `dll-with-manifest` expects a `.dll` asset and accompanied the old mod-shell proposal. Define a versioned managed contract with explicit route/capabilities instead of silently changing these meanings. Reuse the fixed metadata identity/hash transport and CF distribution. Unsupported new schemas/routes must fail on older clients; add managed-layout/declaration validation.

Keep `installation-v1` receipts separate. Do not move or delete old installed mods automatically. Duplicate copies require explicit old-route removal before managed reinstallation. Publish a new immutable Playtest version/asset; never overwrite the 1.1.0 release or historical catalogs.

## Implementation sequence and acceptance

1. Freeze managed manifests, origin, desired state, receipt/journal format and generic inventory/path contracts.
2. Implement host startup recovery and verified candidate/resolver behavior; test disabled-package exclusion, conflicts, missing references, damaged records and shop-disabled startup.
3. Connect manager inventory and pending/failed/restart states while preserving built-in management and settings recovery.
4. Reuse download/verification/transaction mechanisms with the managed target and recovery path; retain old receipt recognition/removal compatibility.
5. Release the new DLL Playtest: install/restart → tab/counter/confirmed 100 silver; disable/restart → tab gone but package manageable; uninstall/restart → files/entry removed and data preserved; reinstall → registration works. Verify Workshop links separately.

Audit package/source/version/hash, transaction and startup correlation, candidate/resolve/discover/register/activate/remove stages and stable outcomes; preserve detailed internal failure diagnostics without exposing credentials.

This assessment changed documentation only. No runtime implementation, publication, Worker deployment or new-feature validation occurred. Earlier 585 assertions and ZIP evidence establish reusable foundations, not acceptance of this managed route.
