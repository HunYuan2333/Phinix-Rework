# Localization first batch: implementation and validation

2026-10-05 follow-up: [Playtest 1.3.0 remote publication and live checks](Playtest130Publication.md) supersedes local-only status below. GitHub assets were read back and staging checks passed on .NET 10/Mono without proxy; v3 store metadata/direct GitHub remain pending.


[中文](本地化实现与验证.md). 2026-10-05, dev. Implements the first batch in [the author/host contract](PluginLanguageFilesAndHostContract.md); catalog v3, metadata rendering, CF and publishing remain pending. Development formats may change without compatibility readers or cache migration. This batch adds no alternate old-format path.

## Delivered

- `ExtensionLocalization` provides strict UTF-8 JSON, bounded locales/files/maps, package display/UI text, canonical locale filenames, per-field/key fallback, Chinese script matching and numbered placeholder consistency. Any single language is valid. Unknown fields, duplicates, markup, malformed text/parameters and changed hashes/lengths are rejected.
- Manifest schemaVersion=1 now has optional `localization: {files:["Resources/Localization/en-US.json"], defaultLocale:"en-US"}`. Each language file must also belong to declared resources with length/SHA-256. Omission means no language resources for this service. defaultLocale is optional but must exist when supplied. Language file schemaVersion=1 is fixed for this batch.
- ClientExtensionAbstractions 1.7.0 exposes `IClientLocalizationService.ForModule(this)` and disposable `IClientLocalizer` with Text/Format/Locale/LanguageChanged. Binding and game-language publication require the main thread; reads use frozen data. Missing keys use explicit fallback/key and throttled diagnostics. Parameters/result are bounded.
- The host registers localization before activation, reads game language on the game thread and follows language changes. Ownership comes from actual registered module/assembly identity; module instance identity cannot be confused by overridden Equals. Managed packages use the verified startup owner; normally discovered bundled DLLs use explicit assembly-bound companions. No store dependency, plugin-specific startup branch or injection into global RimWorld languages.
- Generic lifecycle hooks release handles/events on failed activation and after Shutdown even if the plugin throws. Shutdown/next-start package removal do not erase settings/saves or unload CLR assemblies during the current session. The startup domain releases retained package dictionaries at disposal.
- ZIP payload validation, host installation and startup all validate language data before DLL use. Startup freezes language dictionaries; later file mutation does not change UI lookup. Public managed audits retain package/transaction correlation plus owned relative `resourcePath`; localization events have UTC time, sequence, module, code, relative file and missing/format key, with no absolute path/content dump.
- ManagedPackageTool accepts repeated --language-file, optional --default-locale and --bundle-output. Bundled output is DLL + adjacent `<assembly>.dll.localization.json` + `Resources/<packageId>/Localization/` to isolate plugins sharing locale filenames. The companion has schemaVersion=1, assemblyName, resources and localization. It uses the same declarations/parser as a managed package; a missing companion means no languages. The tool refuses existing outputs and checks exact supplied external CLR identities.
- Playtest 1.3.0 uses en-US/zh-CN JSON for dynamic tab/buttons/confirmation/counter/result. Trusted bot validator snapshots include the shared language parser and strict payload validation (24 declared snapshots verified against provenance hashes). Native Workshop Mod language loading remains the author/RimWorld responsibility.

## Local candidate

`/tmp/phinix-managed-playtest-1.3.0.zip`, 6822 bytes, SHA-256 `5a8e14105639e0180b10cbf82923d91fe64e2e6a5a14ae6728a6851ba7575f88`.

Manifest SHA-256 `1249fe3864ef05f3c28e69bf6698e4ec5053c794051e7c513647f945d4b07d3f`.

`/tmp/phinix-playtest-localization-bundle-1.3.0` contains the ordinary bundled-discovery candidate. ZIP has exactly manifest.json, one plugin DLL and two declared language files; no game/framework DLLs. Actual Assembly-CSharp reference is 1.6.9676.18020, abstractions CLR reference 1.7.0.0. No 1.3.0 remote release/catalog/Worker change was performed. A local structural catalog solely exercised the trusted validator; its historical GitHub IDs are not remote 1.3.0 publication evidence.

Rebuild/deploy the entire mod; earlier 1.6.0 abstractions binaries are not compatible with the new sample. Remove earlier managed/bundled sample copies through their existing ownership route before testing, retaining data. See [sample build, packaging and game steps](../../../../Extensions/PluginStore/Samples/Playtest/README.md).

## Validation commands and results

Executed from repository root:

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe

dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1

dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:BuildInParallel=false -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:GameReferenceDirectory=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -m:1
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj --configuration Release --no-restore -p:BuildInParallel=false -p:GameReferences=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -m:1
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1

mkdir -p /tmp/phinix-playtest-localization-test-extensions
cp -R /tmp/phinix-playtest-localization-bundle-1.3.0 /tmp/phinix-playtest-localization-test-extensions/
dotnet build Extensions/PluginStore/Samples/Playtest/tests/RegistryCheck.csproj --configuration Release -p:BuildInParallel=false -p:NuGetAudit=false -m:1
MONO_PATH=/usr/lib/mono/4.5:/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed mono Extensions/PluginStore/Samples/Playtest/tests/bin/Release/net472/RegistryCheck.exe /tmp/phinix-playtest-localization-test-extensions

dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Extensions/PluginStore/RepositoryAutomation/Validator/bin/Release/net10.0/Validator.dll payload phinix.managed /tmp/phinix-playtest-localization-candidate-catalog.json /tmp/phinix-managed-playtest-1.3.0.zip phinix.poc.playtest 1.3.0 /tmp/phinix-playtest-localization-payload-validation.json
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
```

Managed runtime: 646 main-process assertions on both .NET 10 and Mono, plus actual-loading children reporting 18 + 18 + 16 assertions. Includes English-only/Chinese-only/Japanese-only fallback, identical-key/instance isolation, format/bounds, malformed/tampered files, main-thread restrictions, callback isolation/disposal, failed activation/shutdown, refusal before install/startup DLL load and actual managed owner binding without a store. Framework harness passed; prior store harness passed 677 assertions; bot intake passed 10 tests. Full solution, sample/tool and independent validator built successfully. Actual sample discovery/registration/locale changes/fallback/cleanup passed under Mono; it invoked no game GUI or silver action.

ZIP declarations, hashes, contents and namespaced companion resources were independently checked. PowerShell is unavailable, so `.github/scripts/check-artifacts.ps1 -IncludeClient` was not executed; equivalent Python checked its 26 required files, expected load folders and absence of game reference DLLs in host/server artifacts. Existing protobuf/obsolete warnings and unavailable NuGet vulnerability data remain; these were not addressed here. No in-game validation is claimed.

## Next acceptance and implementation

Game check now targets this host batch: deploy whole new mod and local bundle, switch Chinese↔English and an unavailable language, confirm counter/silver on a test save, disable store/restart and verify Playtest translation still works, disable/re-enable Playtest/restart and keep count. These steps are in the sample README. Do not hand-edit installed JSON without regenerating its signed-by-hash declarations.

Next, implement catalog v3/publisher display extraction and store locale rendering using this same core; update client/CF/trusted bot/examples together without v2 compatibility. Browsing keeps translated metadata in the catalog; UI strings remain in package ZIPs, without separate language-file network fetches. Then repeat remote managed installation/game language acceptance and proceed to grouped versions, one install action, progress/changelog and update notifications. Business plugin separation still follows its save prerequisites.

## Per-plugin folder supplement (same day)

The requested layout is supported: host Common/Extensions probes loose DLLs plus immediate plugin folders containing top-level DLLs. Copy the entire phinix-playtest-localization-bundle-1.3.0 directory, retaining DLL, companion and Resources together. No recursive Resources/deep DLL scan is added. Localization already resolves relative to DLL location, so declarations/package resources need no change. This uses generic bundled discovery; managed store installation paths/ownership/transactions and native active-Mod Assemblies probing are unchanged.

Regression now passes 649 assertions on both .NET 10 and Mono. The actual sample smoke check now uses production directory expansion/assembly loading from `/tmp/phinix-playtest-localization-test-extensions`, then tests registration/localization. Full host/store rebuilt. This temporary root is test preparation, not game deployment. See the updated sample README for preparation and exact commands. Game acceptance remains pending, with existing Game/protobuf obsolete warnings retained.
