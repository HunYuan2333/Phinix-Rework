# Optional plugin removal from the main package

[中文](可选插件退出内置包验收.md) · 2026-10-06

The user explicitly requested removing RedPacket and TalentTrade from the bundled main product now. This supersedes earlier plans to retain bundled copies until standalone acceptance. It does not assert that missing-package save recovery, durable purchases or relay security passed.

## Delivered boundary

- Removed the four Contracts/Client projects from `Phinix.sln`, including configuration and nesting entries. Kept source/projects for independent publication and focused regressions.
- Removed their four copy-to-main targets and unused distribution-resource items. Building either optional project, or a harness referencing them, no longer publishes its DLLs/resources into the main package.
- Main host and bundled store builds import `Client/Packaging/ClientPackageLayout.targets`. Its post-build cleanup removes only former `13`–`16` flat DLL families, companions/PDBs, owner-specific resources and retired XML from generated output. It never changes game installations, player settings, saves or managed-package storage.
- Artifact checking now rejects those optional DLLs/resources in a main distribution. The new package contains exactly eight extension DLLs: Chat Contracts/Client, Trade Contracts/Client, Inventory Contracts/Client, LegacyAdapter Client and PluginStore Client. Existing prefixes remain; no shared dependency or store was removed.
- No host/business runtime change, special plugin branch, namespace/module/type rename, protocol change, relay probing or credential upload. Historical `builtin`/`BuiltIn` identities are preserved for existing settings and manifests; identity spelling does not imply bundled distribution. No automatic replacement of installed player packages.

## Validation performed

```sh
dotnet sln Phinix.sln list
python3 .github/scripts/test-client-package-layout.py -v
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:GameReferenceDirectory=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
dotnet build Tests/TalentReturnRuntimeTests/TalentReturnRuntimeTests.csproj --configuration Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/TalentReturnRuntimeTests/runtimeconfig.json Tests/TalentReturnRuntimeTests/bin/Release/TalentReturnRuntimeTests.exe
mono Tests/TalentReturnRuntimeTests/bin/Release/TalentReturnRuntimeTests.exe
python3 .github/scripts/update-bundled-localization.py --check
git diff --check
```

The solution lists 25 projects, with neither optional plugin included. The two layout scenarios run the real imported MSBuild target on isolated fixtures: stale outputs are removed, other plugin/shared files and save/storage sentinels survive, a second build is idempotent, and empty cleanup creates no package. Full main/server/store build succeeded with 0 errors and four existing protobuf/obsolete-API warnings. Focused optional build succeeded with 0 warnings/errors; .NET and Mono each passed 68 assertions. A subsequent scan verified it did not repopulate the main package.

PowerShell is unavailable, so `.github/scripts/check-artifacts.ps1 -IncludeClient` could not run natively. An equivalent Python scan read the required literal paths from that script and checked all 21 unique client/server artifacts, `/`, `Common`, `1.6` load folders, absent retired artifacts and no Assembly-CSharp/Unity/mscorlib DLLs. Evidence: `/tmp/phinix-unbundle-legacy-artifacts-20261006.json`; build logs: `/tmp/phinix-unbundle-legacy-build-20261006.log` and `/tmp/phinix-unbundle-legacy-focused-build-20261006.log`. Run the PowerShell check on a host with PowerShell before packaging a final release. No native game validation is claimed.

## Player upgrade and next acceptance

Exit RimWorld before deploying the generated `Output/phinix-rework` package. Replacing the main Mod folder with a clean copy is preferable to overwriting: copying newer files does not delete removed old DLLs. Preserve any manually added third-party extensions before replacing that folder. Do not delete player settings, saves or the separately managed installation directory.

For an existing overlaid copy, the retired files in `Common/Extensions` are:

- `13-LegacyRedPacketExtension.dll`
- `14-LegacyRedPacketExtension.Client.dll`
- `15-LegacyTalentTradeExtension.dll`
- `16-LegacyTalentTradeExtension.Client.dll`
- Their adjacent localization companions/PDBs, `Resources/LegacyRedPacket` and `Resources/LegacyTalentTrade`, and old owner XML under `Languages/*/Keyed`.

This removes their Tabs until the corresponding independent package is installed and enabled after restart. Talent [Issue #22](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/22) has passed static intake and awaits the user's approval label; RedPacket has no published/admitted asset. Main removal does not silently index either plugin or bypass the earlier credential-publication rejection.

Use disposable saves first: clean main must show Chat/Trade/Inventory/Store without either optional Tab; install one package and restart, verify exactly one Tab and package-owned languages/settings, then disable/uninstall/reinstall. Preserve untouched backups of old Talent saves. Missing-package resaving is an unresolved concrete persistence risk: reinstall Talent before opening/saving a save containing its listings, rental pawns or pending returns. New purchase intent is still process-local; main removal does not add cross-restart recovery. Detailed business tests remain in [Talent safeguards acceptance](TalentTradeInputGuardsAcceptance.md).
