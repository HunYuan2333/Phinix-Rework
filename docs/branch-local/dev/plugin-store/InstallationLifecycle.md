# ZIP installation, ownership, recovery and uninstallation

> 2026-10-04 route change: this document records the earlier local-mod ZIP candidate and Playtest 1.1.0. Its game steps apply only to those old packages. The user selected [managed DLL extensions](ManagedDllRoutes.md), controlled by Phinix outside the RimWorld mod list; that route is not implemented or accepted yet.

[中文](安装卸载与游戏测试.md). Updated 2026-10-04. Branch-local P4 ZIP implementation; not a production rollout or an in-game acceptance claim.

The user reported the preceding game browsing, dependency preview, ZIP check, cancellation/retry and management entry working. The next candidate now adds explicit **Install new packages in this plan** confirmation and a **Packages installed by this store** list with uninstall buttons. It does not enable mods, edit ModsConfig, load candidate assemblies or upgrade/overwrite packages.

## Filesystem boundaries

`ManagedInstallation` is part of the shop extension, not the host. Planning can read and hash managed records without needing writable Mods. A managed package is recognized only after its source, complete locked record, manifest, actual file set and every file SHA-256 are reverified. Manual and Workshop copies remain unowned and block duplicate installation. A record or directory name alone proves nothing.

- Targets: `<LocalModsRoot>/phinix-store-<32-hex source/package hash>`; names in About and the shop remain human-readable.
- Held network downloads: save-data extension directory, outside Mods, exclusive delete-on-close handles.
- Staging/quarantine: sibling `<Mods parent>/.phinix-store-<16-hex Mods-root hash>/<transaction ID>`, outside the scanned Mods tree. Directory moves require the same filesystem; there is no cross-volume copy fallback.
- Records/journals: `<SaveData>/Phinix/ExtensionData/phinix.plugin-store/installation-v1/{installed,journals}`. Never delete this directory to make a conflict disappear.
- Per-Mods-root exclusive lease, writable-root probes, canonical relative paths, no symbolic links/junctions, CreateNew extraction and no overwrite. File and directory additions, missing or edited markers, changed bytes or corrupt/trailing journal JSON stop automatic deletion.

All new dependency ZIPs are downloaded, statically checked and extracted before any visible target move. Extraction consumes the same held file and checks each extracted length and digest against the validator report. The complete online chain is checked before download and again before commit. A changed snapshot, withdrawal, identity change or changed local dependency plan requires a new reviewed plan. Offline caches cannot authorize installation.

A flushed intent journal precedes directory moves. Cancellation is accepted during download/extraction/freshness checking. Once directory commit starts it is a short non-cancellable phase; IO is outside the GUI lock. Multi-directory installation is **not atomic**. Installation finishes with complete ownership records and a manual enable/restart instruction. The stage tree is removed before the journal is retired.

## Recovery and removal

Opening the store starts managed-package inspection/recovery; the explicit refresh/recovery button repeats it. This occurs after RimWorld has scanned its mods, not as an early host loader hook. A fully moved, verified install is completed; a partial install is rolled back only for unused owned targets. Before rollback deletion, a durable `install-rollback` intent permits a second interruption to resume checking the remaining owned subset. Loaded or enabled partial targets, corrupt records and user edits stop recovery and preserve evidence.

Uninstall supports **one selected store-owned package**, without cascading dependency removal. Required managed package/module/external dependencies and declared local/Workshop About dependencies block removal. Enabled mods or any matching loaded CLR assembly block it: disable the RimWorld mod, restart, then use uninstall. Disabling a Phinix extension alone does not unload its DLL.

The directory is moved to quarantine after a flushed uninstall intent. Its complete content is checked again, owned files are deleted individually, and the marker is last. An interrupted deletion resumes only if every remaining file and recorded directory still agrees. An interruption before the move aborts removal and keeps the installed record. Saves, settings and already-generated in-game silver are retained. User additions or edits are not automatically erased.

Power loss and filesystem faults cannot be advertised as zero-risk: parent-directory fsync is not portable in this net472 implementation, write-failure coverage uses injected IOException; real disk-full acceptance remains pending, and startup scanning precedes recovery. Damaged ownership data requires manual review. Large-package memory/CPU and Windows filesystem/game behavior still need acceptance. DLL-with-companion-manifest installation, updates and hot unload remain outside this ZIP candidate.

## Published playtest

Endpoint `https://plugins-staging.hunyuan2333.com`, source `phinix.poc`. Select **Phinix Store Playtest 1.1.0**, ID `phinix.poc.playtest`. It is separate from the inert marker; the marker and old published snapshot remain immutable.

- Source/input snapshot: `0af87fe9aacb9a0cbbd1018db1febeb40d6f318c`.
- Package release `v1.1.0`, numeric release `402842454`, asset `609129967`.
- ZIP: `8105` bytes, SHA-256 `d98aec35437f832c03737d1dc5b13dd79b950449a3aba05563916512c7d6d53b`, seven checked files.
- Two-package catalog release `catalog-0af87fe`, numeric release `402843424`, asset `609134486`, `2152` bytes, SHA-256 `055b52a43efb6ed3eeac1402198346217220dd3c5e80a72bffc9fa59f3880170`.
- Test stable/publication commit: `6ea7fef`; production index and Worker configuration were not changed. Both test endpoints following this source see its new stable pointer.

[Release](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.1.0). Source is also in `Extensions/PluginStore/Samples/Playtest`; the template project is intentionally outside the shipping solution and main Mod package.

The sample uses the public module attribute, RegisterApi<IMainTabProvider>, Activate and Shutdown. No host-specific branch was added. It supplies a click counter and a confirmed action placing 100 silver near the current map center. Use a disposable test save. No network handler or persistent save component is registered.

## Validation and game handoff

Commands run (ordinary build, without the mixed-target Rebuild failure):

```sh
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -m:1
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.poc /tmp/choose-a-new-isolated-test-directory --install-check
```

The live check explicitly disables system proxies and uses newly allocated isolated test Mods with a fake host metadata fixture. It validates real public metadata, conditional revalidation, transfer size/hash, seven-file static checking, held-byte extraction, filesystem commit, ownership and uninstall without loading the sample. It does not test Unity, game Mod settings, GUI clicks or map placement. Mono generic-registry smoke separately loaded the actual compiled sample, discovered/registered one provider, activated and shut it down, without drawing or invoking game actions. Baseline NU1900 and obsolete-target warnings are recorded, not treated as game evidence.

Audit events include `clientRequestId`, `transactionId`, source/package/version/digest, bounded reasons and HTTP request IDs. Key stages: `install.preparation_started`, `install.package_staged`, `install.journal_prepared`, `install.freshness_verified`, `install.commit_started`, `install.package_committed`, `install.committed`, `uninstall.journal_prepared`, `uninstall.quarantined`, `uninstall.committed`, `recovery.*`. The normal extension log also shows `Playtest: activated`, click count, silver placement and shutdown.

1. Exit the game and replace both refreshed main Mod and shop preview output folders. Enable both and restart.
2. Refresh staging online, choose Playtest 1.1.0, preview its dependency plan, then confirm installation. Expect installed/restart status and an owned package entry.
3. In RimWorld's Mods list enable **Phinix Store Playtest**, restart, and open Phinix **Store test**. Check the extension manager shows `phinix.poc.playtest` active and the activation log.
4. Count clicks; with a loaded test map, confirm 100 silver and look near its center. Cancel the confirmation once and verify no silver is created by cancellation.
5. Try uninstalling while loaded: expect `PackageInUse` and no removal. Disable the extension and restart once to verify provider activation policy/tab disappearance; it still cannot be uninstalled while its mod DLL remains loaded.
6. Disable the **RimWorld mod**, restart, open the store's owned list, and confirm uninstall. Expect complete removal and no discovery/Tab on the next restart. Saves, settings and created silver remain.
7. Repeat installation after removal. Separately test cancellation, changed files and interrupted commits on disposable directories; preserve any `RecoveryRequired` journal and report its transaction ID.

Only game testing can accept steps 2–7. Windows/Steam, large ZIP memory/CPU, multi-provider connectivity and production cache/accounting are still pending.

### Final evidence for this candidate

Runtime harness: **585 assertions passed**. Final full solution: **0 errors, 4 legacy SDK warnings**. Main Mod and shop output contents and matching English/Chinese keys were checked with Python because `pwsh` is unavailable; the PowerShell artifact script itself was not run. No game/reference DLL or bundled playtest DLL is present in either shipping output.

Final direct Mono command actually run:

```sh
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.poc /tmp/phinix-playtest-mono-install-20261004-audit --install-check
```

Six HTTP responses were correlated to **98 structured Worker records**, each with one matching 200/304 terminal outcome. Client correlation `90613719a0754d909adff888a98934c5`, install transaction `cf5fc3ea5da848fdac58b9f4b949f845`, uninstall transaction `de99d66ec8d34d12bd73191c159868d8`. Worker tail transport used the local CLI proxy; the Mono client explicitly used direct networking. Raw platform tail logs and client traces stay in private `/tmp` files and are not committed. An initial empty tail capture was not counted as correlation evidence.

Generic-registry smoke commands run from the repository root:

```sh
mcs -out:/tmp/phinix-playtest-registry-check.exe -r:Common/Utils/bin/Release/net472/Utils.dll -r:Client/ClientExtensionAbstractions/bin/Release/ClientExtensionAbstractions.dll /tmp/phinix-playtest-registry-check.cs
MONO_PATH=Common/Utils/bin/Release/net472:Client/ClientExtensionAbstractions/bin/Release:GameDlls mono /tmp/phinix-playtest-registry-check.exe Extensions/PluginStore/Samples/Playtest/bin/Release/net472/Phinix.Store.Playtest.dll
```

The reusable source for that smoke harness is `Extensions/PluginStore/Samples/Playtest/tests/RegistryCheck.cs`; it is excluded from the package assembly. CI/game compilation and this isolated registry check do not replace real game actions, enabled-mod settings or startup crash recovery acceptance.
