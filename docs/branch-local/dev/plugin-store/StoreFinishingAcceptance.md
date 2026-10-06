# Store finishing implementation and game upgrade acceptance

[中文](商店收尾实现与游戏升级验收.md). 2026-10-06, dev. The accepted basic install/uninstall path remains. This batch adds byte progress, startup notices, owned version replacement/recovery and approved-source release monitoring. The user has now accepted the requested 1.0.1 → 1.0.2 game upgrade.

## Delivered behavior

GitHub and CF use the same real package/cumulative byte progress and validation/commit stages. Updates are throttled; canceled, completed or superseded callbacks cannot rewrite the UI. A full download bar is not successful installation.

Startup checks official metadata in the background, without network calls when no official plugin is installed. Identity/compatibility checks select localized newer-version notices and changelogs. No automatic package download or replacement occurs, and failed metadata reads do not prevent existing plugin loading.

Explicit confirmation upgrades only an owned, enabled package with the same source/repository/package identity to a newer version. There is no downgrade or takeover of external files. Dependencies and affected assemblies/modules are checked before replacement. The old session remains active until restart. Existing installation journals perform whole-batch recovery: cancellation before the durable decision retains old files; after it, completion/recovery preserves the new decision. Old backups are removed only after all new content and receipts are verified. Unknown/changed files stop recovery and retain evidence. User data survives; authors remain responsible for business-data migrations.

Approved-source monitoring now records an explicit same-major scope, discovers fixed releases, statically verifies ZIP/PE, automatically merges exact evidence PRs and performs controlled publication. Changed scope/failures preserve the current catalog and create a tracking error; success closes it. Player upgrades remain voluntary. [Operations guide](../../../../Extensions/PluginStore/RepositoryAutomation/SourceUpdates.md).

## Remote evidence

[Index PR #18](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/18) merged as ed9c3fb. Read-only [run 37402682501](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37402682501) passed with proposal/report skipped, downstream publication no-op and unchanged stable.

[Example 1.0.1](https://github.com/HunYuan2333/Phinix-Example-Plugin/releases/tag/v1.0.1) is built from source 139cbcd0e7388ee2e582a4628b9a4b965f49e432, changing only project-version packaging and bilingual changelogs. No gameplay operation. Release 404238483, asset 614187421, ZIP 7324 bytes, SHA-256 a570d501100f2c5ef1866544cb38c2484fc487cf0ceb5eb9aa87e39b458fcc38; manifest SHA-256 2a959a3ba3042cf2859c04926bb5ae97a74a8ec20d3c32b3ce2f8a2807713514. Original 1.0.0 remains immutable.

[Discovery 37402889939](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37402889939) → automatic [PR #19](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/19) → [publication 37402959753](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37402959753) all succeeded without another human label. Snapshot 360753b8b0c8581fb2921ae2dc4e40210368360f; catalog 5071 bytes / SHA-256 76464e1b67a667bb8317f9bf9eb70baec72074cfc45c9b8e01fc4294cfbacf32. That publication listed only Example 1.0.0/1.0.1; no Playtest.

Real production .NET/GitHub and net472/Mono/CF direct downloads passed with identical catalog/new ZIP, pre-cancellation, cache, fresh pre/post-transfer checks, ZIP/PE and temporary cleanup. Neither installed nor executed downloaded code.

## Human checkpoint now

1. The user now has a fresh Example 1.0.1 installation. Keep it and the latest host/bundled store, record its counter/settings, and use new release 1.0.2 as the upgrade target. No uninstall or older-version reinstall is needed.
2. Restart and confirm the 1.0.2 notice opens the store, one grouped entry defaults to latest, and Chinese/English display/changelog work. Startup/browsing must not download or replace automatically.
3. Cancel an upgrade confirmation and verify old state remains. Confirm again and observe stages/bytes/completion/restart. The ZIP is only ~7 KiB, so its download stage may pass too quickly to read.
4. Before restart, manager shows pending restart and the current example still works; reopening the store does not advertise the already saved version. After restart, 1.0.2 is loaded with counter/options/language retained. Verify live settings, counter and canceled/confirmed reset.
5. GitHub/CF switching preserves package identity without duplicates. Disable/restart hides the example; enable/restart restores it; uninstall/restart removes the entry while allowing reinstallation.
6. Brief offline startup retains installed plugins with no blocking/error popup. Online refresh recovers. Failures need operation/version/error details and transaction/request IDs, never tokens.

Unity/Verse behavior requires human acceptance. Chunked larger-download progress and failure/recovery paths have isolated automated coverage; players need not intentionally corrupt files.

## Exact validation

Exact commands run are below. Build sequentially; the Steam reference path is this machine’s compiler input. Omit that override when GameDlls/1.6 already has correct references. Repeated live checks require a new absolute state directory.

```sh
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
dotnet build Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ResponsiveUiGeometryTests/bin/Release/net10.0/ResponsiveUiGeometryTests.dll
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-example101-github-20261006 --official-github
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.official /tmp/phinix-example101-cf-20261006 --official-cf
```

Passed: full Release 1.6 solution with real Steam references, 19 existing warnings/0 errors; .NET/Mono each 856 parent assertions plus real assembly child processes 18+18+16+16+20; store 902; responsive layout; local/remote bot 77; trusted Validator build. NuGet vulnerability-service access and old protobuf targets produce existing warnings. PowerShell artifact checker is unavailable; equivalent required-files/load-folders/current-host-and-store-bytes/no-game-reference-DLL checks passed, together with bilingual key parity. Initial concurrent dotnet run attempts reported build failure; sequential builds/tests passed. The main dirty working tree was not bulk-committed.

## Remaining after game acceptance

Production CF domain migration is pending. Read-only inspection: current official adapter phinix-plugin-repository-staging / plugins-staging.hunyuan2333.com has only GITHUB_TOKEN, R2_ENABLED=false and no R2/DO bindings. The old phinix-plugin-repository-poc configuration binds plugins.hunyuan2333.com; its inspected deployment is version fb9c678d-307c-481b-9290-702368429ad2, with GITHUB_TOKEN/POC_ACCESS_TOKEN, CacheCoordinator SQLite DO and phinix-plugin-poc-20261004 bucket; its fixed PoC expiry is past.

Do not use that old domain as the official adapter or delete Worker/storage/secrets before ownership/storage inspection and a confirmed migration. After game acceptance, move the final domain, check identity/all retained versions and logs, then follow a separate retirement inventory while preserving rollback configuration and immutable packages. Read-only inventory is not completed retirement.

No trade protocol, item ownership or save format changed. RedPacket/TalentTrade separation retains resource/save prerequisites; AI author tooling and public prose cleanup remain [deferred plans](FinishingAndAiAuthorWorkflow.md).


### 2026-10-06: 1.0.1 → 1.0.2 upgrade target

The user removed their old 1.0.0 and freshly installed 1.0.1, then explicitly requested 1.0.2. The human checkpoint above now keeps 1.0.1. Version 1.0.2 changes only the version and bilingual changelog; functionality/settings keys are unchanged. Source 9afaa2fc0bf27cfc5275f853341da19af53bfcbf built successfully and passed trusted ZIP/CLR/language projection without executing plugin code.

[Release v1.0.2](https://github.com/HunYuan2333/Phinix-Example-Plugin/releases/tag/v1.0.2): releaseId 404247369, assetId 614223915, 7359 bytes; SHA-256 b0c8d5f9dc7674f355f2c30fd05967dbfdbd5aca3b7be48949293bdfc2139a80; manifest SHA-256 f7cf9d4486779a546dbc8387082ea423a0172ce52bb389ab1ea33d7627014f4d. [Admission 37404125661](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37404125661) passed and automatically merged [PR #20](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/20), candidate snapshot 2514837d84f27475c17cbb5daa5464ebd0f58339. [Publication 37404187389](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37404187389) passed verify/publish/notify_updates. Official snapshot 2514837d84f27475c17cbb5daa5464ebd0f58339; catalog 7662 bytes / SHA-256 65b18df38737a142353719f10f13f45e56eaa651070f45bbc4f1695f70f961eb. Real GitHub/.NET and CF/Mono downloads verified that exact 1.0.2 ZIP and snapshot, pre-cancellation/cache/fresh pre-post checks/static payload/temporary cleanup. The user subsequently accepted this game upgrade; see the record below.


```sh
python3 /tmp/phinix-example-update-source/pack.py --phinix-root /home/hunyuan2333/Phinix/Phinix-Rework --game-references /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed --output /tmp/phinix-example-release-1.0.2/phinix-example-basic-1.0.2.zip
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-example102-github-20261006 --official-github
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.official /tmp/phinix-example102-cf-20261006 --official-cf
```


### 2026-10-06: user-accepted game upgrade

The user reports this requested 1.0.1 → 1.0.2 game upgrade works. Record human feedback acceptance for the update-notice/confirmation/restart/settings-retention checkpoint above; do not extend it to multi-package faults, Windows or broad network coverage. Next is production CF domain/environment migration, retained-version/log/rollback checks, then separate owned PoC retirement inventory. AI author tools/public prose cleanup remain future work; official RedPacket/TalentTrade separation retains resource/save prerequisites.
