# Same-major upgrades for host assembly references

[中文](宿主引用向上兼容验收.md) · 2026-10-06

The user requested accepting library upgrades within one major instead of rejecting Harmony 2.3.6 references when the game already provides 2.4.1. This is a generic host-library policy, available equally to all managed plugins, not a TalentTrade or Harmony exception.

## Rule and implementation

| Required → actual host | Result |
| --- | --- |
| `2.3.6.0` → `2.3.6.0` | Exact match preferred |
| `2.3.6.0` → `2.4.1.0` | Allow |
| `2.3.6.0` → `2.3.5.0` | Reject downgrade |
| `2.x` → `3.x` | Reject major change |
| Changed name/culture/public-key token | Reject |
| Multiple compatible upgrades, without an exact match | Reject ambiguity |

`ManagedAssemblyIdentity` defines the shared selection rule. Candidate installation/startup planning and the packager use it only for external host references. Package-owned and declared dependency-package assemblies still require exact CLR identities; hashes, manifest identity, explicit package versions and host/game compatibility ranges are unchanged.

Startup independently checks real assemblies against declared host facts. It freezes old-reference → actual-assembly aliases using assemblies loaded before startup. The managed resolver returns that existing object only for the owning plugin's inspected, declared reference; it never probes a directory, loads an extra library, handles another caller or adopts a later-loaded version. `ManagedHostReferenceUpgraded` records requested and selected identities through existing bounded audit fields. Payload bytes and declarations are not rewritten. Same-major acceptance is a compatibility policy, not proof that every API remains compatible; genuine runtime load/type failures retain their existing error paths.

The two affected trusted-validator snapshots and provenance hashes were refreshed locally. No index, gateway or published Talent ZIP was changed; existing 1.0.1 bytes can be reused after updating the main host package.

## Validation performed

```sh
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 -p:BuildInParallel=false -m:1
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-build
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll --package-id test.host-upgrade --name 'Host upgrade fixture' --version 1.0.0 --assembly Tests/ManagedExtensionRuntimeTests/Fixtures/Plugin/bin/Release/net472/Fixture.Managed.Plugin.dll --host-assembly Tests/ManagedExtensionRuntimeTests/Fixtures/HostUpgrade/bin/Release/net472/Fixture.Managed.Helper.dll --host-assembly Common/Utils/bin/Release/net472/Utils.dll --host-assembly /usr/lib/mono/4.5/mscorlib.dll --output /tmp/phinix-host-reference-fixture-20261006.zip
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:GameReferenceDirectory=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
git diff --check
```

.NET 10 and net472/Mono each passed 1661 parent assertions and six actual startup/registration children (18, 18, 18, 16, 16, 20). New coverage includes the exact screenshot's Harmony identities, major/downgrade/culture/token/name rejection, exact preference, ambiguous versions, candidate planning and rejection of an upgraded *owned* helper. A fresh child loads helper 1.3.0.0 as a host library while the plugin still references 1.2.3.4; actual plugin registration calls the helper, resolver identity checks and upgrade audit pass on both runtimes. No game/Scribe/legacy relay validation is inferred.

77 repository-automation regressions passed. Validator and packager builds passed; the real fixture packager accepted a newer host helper while preserving the plugin's original reference metadata. The first net10 restore/build had cached-source `NU1900` warnings because NuGet's vulnerability feed was unavailable, plus existing protobuf trimming warnings; no source/SDK workarounds or vendored edits were made. Logs are `/tmp/phinix-host-reference-policy-tests-net10.log`, `/tmp/phinix-host-reference-policy-tests-mono.log`, `/tmp/phinix-host-reference-automation-tests.log` and `/tmp/phinix-host-reference-main-build.log`. Full main/store build succeeded with 0 errors and seven cached NuGet/protobuf/obsolete-API warnings. Packaged Utils bytes match the rebuilt net472 binary, and the equivalent artifact scan found no game DLLs or retired optional DLLs. Native PowerShell remains unavailable; main package checks use that equivalent scan.

## Player verification

Exit RimWorld and replace the complete main package from `Output/phinix-rework`, including `Common/Assemblies/03-Utils.dll`; replacing only the store DLL cannot update this host policy. Start with the installed Harmony 2.4.1, refresh the store and retry Talent 1.0.1 installation. No new Talent asset is needed. Restart to load the installed plugin, check one Talent Tab and settings/localization, and report any actual load failure with its audit code. Retain the separate Talent save/ownership test gates and use backed-up test saves; this policy change does not solve purchase persistence or missing-package resaving.
