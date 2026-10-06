# Plugin store first release and acceptance

[中文](商店首版交付与验收.md). 2026-10-05, `dev`. M4b host installation, M4c client/network UI and M6a bundling are local candidates. The M5 Playtest 1.2.1 package is published and staging is deployed. **Production live download checks passed without proxies; the user reports the basic 1.2.1 in-game install-to-uninstall lifecycle passed. Extended scenarios remain pending.** RedPacket/TalentTrade splitting stays in the later plan.

Ordinary player requirements are consolidated in [Future improvements](FutureImprovements.md): grouped versions/selection, startup update notifications only, developer changelog, one install action/dependency confirmation, progress and removal of preview migration controls before formal release. Basic lifecycle acceptance now has passing user feedback, so experience improvements are next. Extended checks remain; these plans are not implemented capabilities.

The normal `phinix.plugin-store` module is bundled as `Common/Extensions/17-PluginStore.Client.dll`, with bilingual translations. It uses normal discovery/Register/Activate/Shutdown and preserved disable settings; the host has no store implementation reference. Development preview packaging requires `BuildPluginStorePreview=true`. Text descriptions/tags, responsive list/details and state actions add no remote-image/README requests. Full mods open fixed Workshop links; new GitHub packages use catalog v2 and managed DLL ZIPs outside Mods. Legacy v1 cleanup remains accessible, with new legacy installation disabled.

The separate atomic managed browsing cache cannot authorize offline installation. Before and after transfer the client sends `Cache-Control: no-cache`, revalidates stable/published/catalog and refuses changed snapshots. Locked binary routes retain length/hash checks, transfer deadlines, cancellation and temporary-file cleanup. Bounded dependency backtracking checks compatibility, enabled owned providers, external active mods, module/CLR collisions, and inactive legacy mod DLL identities, including custom LoadFolders and renamed assemblies.

`IManagedExtensionInstallationService` uses the existing lifetime host lease, freezing caller inputs and checking PE metadata, current inventory/loaded identities, compatibility and combined dependency graphs. Provenance is not a signature or remote authorization. Maximum 32 new packages / 256 MiB expanded; no overwrite/in-place upgrade. Preparing journals own exact staged bytes; after atomic replacement to committing, cancellation cannot undo the batch. Startup rolls preparation back or durable decisions forward before loading, idempotently. Changed/unknown files, links, malformed journals and conflicts preserve evidence and close managed loading/writes. Package state applies at restart, and uninstall retains settings/business data/saves. This changes no trade protocol or item ownership.

Audit includes stages/codes, package/source/version, snapshot/catalog/artifact hashes, request/transaction IDs; host logs add UTC, startup ID and sequence. Public logs omit paths/credentials/exception bodies; internal client details require DevMode. Fault injection does not establish physical power-loss durability or Windows/macOS/max-package behavior.

## Build and game acceptance

```sh
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

Use updated `Output/phinix-rework` and remove/disable the old standalone store preview first. Duplicate module IDs are rejected by the ordinary dependency graph. Client abstractions are **1.6.0**, adding current disabled-module snapshots while preserving the old constructor; packages require matching actual CLR references and must not bundle framework DLLs. If old Playtest 1.1.0 was installed as a standalone mod, remove that copy through the legacy cleanup tool or relocate it first. Even inactive copies reserve their actual DLL identity and block new installation; no automatic migration/deletion occurs.

1. Now test host/bundled-store startup, existing tabs, manager/settings recovery, languages, scaling/narrow layout and store disable/restart/re-enable. The new source is available for download/install testing in the same round.
2. Now use `https://plugins-staging.hunyuan2333.com`, source `phinix.managed`; refresh/search/details/plan, download verification without installation, cancellation/network failure and offline install refusal. Existing `phinix.poc` remains on its original branch.
3. Install managed Playtest 1.2.1; no new tab appears before restart. After restart, test its tab/click counter and confirmed 100-silver action on a test save. No new RimWorld mod-list entry. The public settings service retains the counter through removal/reinstallation.
4. Disable/restart, re-enable/restart, undo pending removal before restart, then uninstall/restart and reinstall/restart. Verify code/records removed, retained data, restored counter, and installed plugins still loading with the shop disabled.
5. Actual multi-package interruptions, changed catalogs/files, Workshop opening, platforms, networks and physical power loss remain acceptance work. No game/UI/silver operation has been claimed here.

## Exact local validation commands

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet restore Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --ignore-failed-sources -p:NuGetAudit=false
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStorePayloadCheck/PluginStorePayloadCheck.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStorePayloadCheck/bin/Release/net10.0/PluginStorePayloadCheck.dll --managed phinix.managed /tmp/phinix-managed-catalog-review.json /tmp/phinix-managed-playtest-1.2.0.zip
mcs -out:/tmp/phinix-managed-playtest-registry-check.exe -r:Common/Utils/bin/Release/net472/Utils.dll -r:Client/ClientExtensionAbstractions/bin/Release/ClientExtensionAbstractions.dll Extensions/PluginStore/Samples/Playtest/tests/RegistryCheck.cs
MONO_PATH=Common/Utils/bin/Release/net472:Client/ClientExtensionAbstractions/bin/Release:GameDlls mono /tmp/phinix-managed-playtest-registry-check.exe Extensions/PluginStore/Samples/Playtest/bin/Release/net472/Phinix.Store.Playtest.dll
git diff --check
```

Worker commands, from `Extensions/PluginStore/RepositoryWorker`:

```sh
npm test
WRANGLER_LOG_PATH=/tmp/phinix-managed-wrangler-logs ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc --dry-run --outdir .wrangler/managed-staging-check
```

Both .NET 10 and net472/Mono passed **562 parent + 15 + 18 + 16 child assertions = 611** per runtime; the old store passed **677**, framework/layout checks passed, and Worker passed **119** tests. Full build had zero errors; existing target/deprecation/NU1900 warnings remain. PowerShell was unavailable: an equivalent bounded file/LoadFolders/bilingual-key/no-game-DLL check verified **25** unique required artifacts. The reusable real-sample Mono smoke confirms discovery, one tab provider, activation and shutdown, without drawing or performing game actions. The new ZIP contains only its DLL and manifest; production v2 validation passed.

On 2026-10-05 the user explicitly authorized publishing the concrete source/project/pack-tool/README and ZIP, plus staging updates. An earlier approval-review rejection was resolved through that authorization before publication. No main-repository commit/push occurred; existing main/PoC/R2 ledger remain intact. Staging has no R2 bindings.

- [Source branch](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/tree/codex/managed-publication): source commit/snapshot `ded002a6e4ff5bdf20509459dba2c80aca03e519`; pointer commit `964cc71051d9c2a22a502bb9c2e7d4078633a795`.
- [Playtest 1.2.0](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.2.0): release `403323718`, asset `611291637`, 5917 bytes; SHA-256 `af2ab3847475c5801f92d9e2a21b736b11b5051fa37f2eddcd9348f0e67dc671`.
- [Immutable catalog](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/managed-catalog-ded002a): release `403323937`, asset `611292980`; SHA-256 `a535e001e2b9cb81781d94d4a49351197f650f8ba077144b4b0ef3f01b529285`.
- Staging version `0834ef06-7164-41bb-9c86-afdb066a4c53`, build `staging-20261005-managed-v1`: `https://plugins-staging.hunyuan2333.com`, source `phinix.managed`. Only the exact old staging/phinix.poc settings pair auto-migrates; custom sources require manual selection.

The explicit production live checker passed metadata/cache, pre-cancellation, fixed-asset transfer, ZIP/PE validation, fresh post-transfer chain and temporary cleanup on .NET 10 and net472/Mono. It does not install or load the DLL. Direct access is evidence for this machine/network only, not every mainland carrier. The existing phinix.poc marker also passed its direct live checker after deployment. Game UI/silver, restart/disable/removal and retained settings still require the checklist above.

Additional exact commands (packaging ran locally; deployment ran after authorization):

```sh
# Packaging creates a new file only; do not rerun against the existing published ZIP.
dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll --assembly Extensions/PluginStore/Samples/Playtest/bin/Release/net472/Phinix.Store.Playtest.dll --package-id phinix.poc.playtest --name "Phinix Store Playtest" --version 1.2.0 --output /tmp/phinix-managed-playtest-1.2.0.zip
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-managed-live-net10-20261005 --managed
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-managed-live-mono-20261005 --managed
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://plugins-staging.hunyuan2333.com phinix.poc /tmp/phinix-legacy-live-preserved-20261005
# From Extensions/PluginStore/RepositoryWorker:
WRANGLER_LOG_PATH=/tmp/phinix-managed-wrangler-logs ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc
```

Live checks require a new absolute state directory each time; replace the /tmp directory names on reruns.

## Local identity false-positive fix (2026-10-05)

The game reported `LocalIdentityUncertain` while planning. Read-only reproduction against the local Mod/workshop/data containers found 1427 DLL inspection attempts, including three ordinary libraries accepted by the runtime CLR-name reader but rejected by full managed-plugin entry/attribute inspection. The local collision gate incorrectly reused `ManagedExtensionMetadataReader.Read` instead of a name-only inspection.

`ReadIdentity(bytes)` now reads bounded CLR identity without executing code or interpreting plugin attributes/types. The local gate uses it; downloaded ZIPs and host installation still require the original full static validation. Actual same-name DLLs, malformed bytes, unsafe folders/links and read failures remain closed. The failure snapshot/UI adds mod ID, file relative to that mod, and underlying code. Structured audit adds bounded `localMod`, basename-only `localFile`, `localReason` and source; it omits directory paths and exception bodies.

Both .NET 10 and Mono passed **580 + 15 + 18 + 16 = 629** assertions, including foreign framework attributes, renamed identity conflicts, malformed bytes, traversal and failure diagnostics/privacy. The old-store harness still passed **677**. Full build passed with existing warnings. The production local gate passed **619 installed mods** against the published managed Playtest catalog. This is read-only reproduction, not game UI/install acceptance; no third-party files were changed. Update the entire host/store output and restart the game, then retry dependency planning.

Exact additional verification commands from the repository root:

```sh
dotnet build Common/Utils/Utils.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe --local-identities phinix.managed /tmp/phinix-managed-publication/catalog.json /mnt/data/SteamLibrary/steamapps/common/RimWorld/Mods/phinix-rework /mnt/data/SteamLibrary/steamapps/common/RimWorld/Mods /mnt/data/SteamLibrary/steamapps/workshop/content/294100 /mnt/data/SteamLibrary/steamapps/common/RimWorld/Data
```

The `--local-identities source-id catalog-json host-mod-root mod-container [mod-container ...]` mode is explicit, performs no network/writes/assembly loads, and reports only local inspection evidence. Build/runtime regression commands are listed above. The `/tmp` catalog path is the previously generated, validated live publication input; use a locally verified catalog and your own absolute mod container paths elsewhere. No Worker/package asset update is needed for this fix.

## Game CLR reference build fix (2026-10-05)

Game logs show 1.2.0 metadata/download/ZIP checks passed; install preflight refused before transaction writes. It references `Assembly-CSharp, Version=1.6.9438.37837`; the installed game is `1.6.9676.18020`. A RimWorld 1.6 label alone does not satisfy the existing exact CLR rule.

Playtest **1.2.1** was compiled against the actual game Managed directory. ZIP: 5913 bytes, SHA-256 `161181dd89e176293b9d8ad5cec8892240aea6279cebf260758b62ee118234b3`; manifest SHA-256 `0a312d23e766f1a641c38a0cc4ada4ac3fc5ea749306004e558b74b5ca90b621`. It contains only its own DLL/manifest. Optional repeated `--host-assembly` inputs now verify every external full identity before packaging. The old real package is refused against current references before any output ZIP; the new package matches all seven references and passes the production ZIP validator.

Candidate/install results and host/client audit retain referencing assembly, required full identity and available same-name identities; the store shows these details. Exact binding rules remain intact. Other game builds require a matching build or a separately reviewed explicit host compatibility policy later. No protocol/item-ownership/state/save or in-place-upgrade behavior changes.

.NET 10 and Mono each passed **589 + 15 + 18 + 16 = 638** assertions, including exact-version refusal, no writes and async UI diagnostic propagation. Full host/store build passed. Actual game install/restart/tab acceptance remains incomplete.

**Publication status:** the user explicitly authorized publishing 1.2.1 and updating the catalog on 2026-10-05, resolving the earlier approval-review rejection before any upload. Source snapshot `f020d7c6fc5144c66d77b6f1dde8be5d55c13fd2`, pointer commit `2c0f2972c7e1fa4dfa4ec11817a69b6e1f372bcc`. [Playtest 1.2.1](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.2.1): release `403371396`, asset `611507077`. [Fixed catalog](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/managed-catalog-f020d7c): release `403371589`, asset `611508104`, 3293 bytes, SHA-256 `16f91745cc0ab3dde0ddf408ad2a9b23f88abf8cd802767232562fa40d7856c5`. The existing staging Worker now serves this branch pointer without redeployment or new R2 bindings. Version 1.2.0 is withdrawn from new installs but its artifact and identity remain intact.

The initial live check failed with `OriginReleaseMismatch` because the new releases had been marked prerelease; both release flags were corrected to published/non-prerelease, without changing assets. Subsequent no-proxy .NET 10 and net472/Mono checks passed: current 1.2.1 metadata/cache, pre-cancellation, fresh pre/post-transfer chain, actual ZIP transfer/digests/PE validation and temporary cleanup. No DLL execution or game installation is claimed. Rebuilding from the current sample project produced byte-identical DLL content to the published ZIP (zero warnings/errors).

Additional executed commands (packaging requires a new output file):

```sh
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1 -p:GameReferences=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll --assembly Extensions/PluginStore/Samples/Playtest/bin/Release/net472/Phinix.Store.Playtest.dll --package-id phinix.poc.playtest --name 'Phinix Store Playtest' --version 1.2.1 --output /tmp/phinix-managed-playtest-1.2.1.zip --host-assembly /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed/mscorlib.dll --host-assembly /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed/Assembly-CSharp.dll --host-assembly /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed/UnityEngine.CoreModule.dll --host-assembly /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed/UnityEngine.TextRenderingModule.dll --host-assembly /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed/UnityEngine.IMGUIModule.dll --host-assembly Common/Utils/bin/Release/net472/Utils.dll --host-assembly Client/ClientExtensionAbstractions/bin/Release/ClientExtensionAbstractions.dll
dotnet Tests/PluginStorePayloadCheck/bin/Release/net10.0/PluginStorePayloadCheck.dll --managed phinix.managed /tmp/phinix-managed-121-publication/catalog-review.json /tmp/phinix-managed-playtest-1.2.1.zip
```

The previously listed full build, managed .NET/Mono regression, payload/download checker builds and old-store regression were rerun after this change. UI drawing/game actions/Windows/macOS were not run. Do not publish the placeholder catalog. Use the target game's actual paths elsewhere; compile-only game files remain private compiler inputs.

**Deferred UI work:** after game lifecycle acceptance, improve list/details/actions/error text together and add received/total bytes, current package/overall progress, validation/commit stages and cancel/retry feedback. Throttle progress publication; never re-read the catalog or log every frame. No remote image requests. This batch records that work, not an implemented progress bar.


Executed publication verification commands (use a new state directory on reruns):

```sh
dotnet Tests/PluginStorePayloadCheck/bin/Release/net10.0/PluginStorePayloadCheck.dll --managed phinix.managed /tmp/phinix-managed-121-publication/catalog.json /tmp/phinix-managed-playtest-1.2.1.zip /tmp/phinix-managed-121-publication/stable.json /tmp/phinix-managed-121-publication/published/f020d7c6fc5144c66d77b6f1dde8be5d55c13fd2.json
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-managed-live-121-net10-retry-20261005 --managed
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-managed-live-121-mono-20261005 --managed
```

For game acceptance, exit the game and update the entire built `Output/phinix-rework` host package, start the game, refresh `phinix.managed` and select 1.2.1. Plan, install, restart and exercise the test tab on a test save; then disable/re-enable and uninstall/reinstall with restarts and retained counter checks. This fixes the targeted game build, not an assertion of binary compatibility with every RimWorld 1.6 build. If 1.2.0 was actually installed in another environment, use managed uninstall/restart before installing 1.2.1; in-place upgrades remain unsupported. A rejected preflight requires no uninstall.


## User game acceptance and next batch (2026-10-05)

The user explicitly reported that the plugin worked through the full installation-to-uninstallation flow. Record **basic Playtest 1.2.1 lifecycle acceptance on the user's current game/machine** and do not request the same basic test again. This is user-provided game evidence, not agent-operated gameplay or a build result. Earlier pending-game statements retain their historical meaning and are superseded by this update. Do not infer separate acceptance of retained counts across reinstallation, loading with the shop disabled, multi-package dependencies/catalog changes, physical power loss, Windows/macOS, large packages or other networks from that general report.

Next implement [experience improvements](FutureImprovements.md): grouped versions/selection, one install action with additional-dependency confirmation, then transfer/validation/install progress and layout. Follow with version changelog and startup notifications only. Test the changed interactions in game after that batch, rather than repeating unchanged flows. A formal update action still requires separate replacement/recovery support; finish publication/author tooling and extended checks in their planned batches. Resource/save prerequisites (M6b) precede RedPacket and TalentTrade separation; this UI batch moves no business data. Remove old preview migration controls before formal release.

This turn updates acceptance/order only; no runtime, public catalog or installed state changes and no build/game run. Documentation links/whitespace and `git diff --check` passed.
