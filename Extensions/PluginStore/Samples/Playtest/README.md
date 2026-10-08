# DLL language-file Playtest 1.4.0 (published test release)

Pre-launch cleanup: Playtest is now a standalone developer fixture, excluded from the official index. Retired test-source instructions below are historical. Use only the immediate-child folder bundle method for new developer testing; existing managed installations remain manageable through Extension manager.

Bundle layout: `Common/Extensions/<plugin folder>/DLL + companion + Resources` is supported. Copy the whole sample folder; only immediate plugin folders are probed, without recursive DLL loading from Resources/deep folders. Existing package-scoped resource paths remain intact. Do not keep a previous loose copy too.

[中文](README.zh-CN.md). This sample now uses host `IClientLocalizationService`, replacing embedded bilingual dictionaries. The ZIP contains its DLL and `Resources/Localization/en-US.json` / `zh-CN.json`. Their display fields are ready for the next publisher batch; strings already drive the tab, buttons, confirmation, counter and results. Any one language is valid; missing keys use host fallback. Existing tabs follow game-language changes without reinstalling.

1.4.0 is publicly released and the phinix.managed staging pointer is updated. GitHub ZIP bytes were read back and verified; see the publication record for live-chain evidence. The user accepted the previous 1.2.1 install-to-uninstall game flow; 1.4.0 language acceptance remains pending. Client abstractions are now 1.7.0: rebuild the complete host and bundled plugins. Old 1.2.x CLR references to 1.6.0 are not rewritten. Language files have manifest lengths/SHA-256 and are verified during installation/startup; editing installed JSON alone is refused.

## Build and package

These are this machine's actual game reference paths; change them elsewhere. Use new output names if they already exist; the packager refuses overwrites.

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework
PHINIX_LOCALIZATION_GAME_REFS=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed

dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:BuildInParallel=false -m:1 -p:RimWorldDepDir="$PHINIX_LOCALIZATION_GAME_REFS" -p:GameReferenceDirectory="$PHINIX_LOCALIZATION_GAME_REFS"
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1 -p:GameReferences="$PHINIX_LOCALIZATION_GAME_REFS"
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1

dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll \
  --assembly Extensions/PluginStore/Samples/Playtest/bin/Release/net472/Phinix.Store.Playtest.dll \
  --package-id phinix.poc.playtest --name 'Phinix Store Playtest' --version 1.4.0 --abstractions-range ">=1.9.0 <2.0.0" \
  --language-file Extensions/PluginStore/Samples/Playtest/Resources/Localization/en-US.json \
  --language-file Extensions/PluginStore/Samples/Playtest/Resources/Localization/zh-CN.json \
  --output /tmp/phinix-managed-playtest-1.4.0.zip \
  --bundle-output /tmp/phinix-playtest-localization-bundle-1.4.0 \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/mscorlib.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/Assembly-CSharp.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/UnityEngine.CoreModule.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/UnityEngine.TextRenderingModule.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/UnityEngine.IMGUIModule.dll" \
  --host-assembly Common/Utils/bin/Release/net472/Utils.dll \
  --host-assembly Client/ClientExtensionAbstractions/bin/Release/ClientExtensionAbstractions.dll
```

Managed manifest schemaVersion remains 1 with optional localization; files also belong to resources. --default-locale is optional. The ZIP contains only manifest, plugin DLL and two languages, with no game/framework DLLs. --bundle-output additionally emits a normally discovered DLL, Phinix.Store.Playtest.dll.localization.json and resources scoped by package ID. All authors use the same host service without a store dependency. Historical Package/ and pack.py describe 1.1.0's local-Mod route and are not used for this candidate.

## Local game acceptance first

Exit the game completely. Remove the old managed Playtest through extension management and restart to complete removal; take out earlier local-Mod/bundled copies. Avoid two copies of the same module. Keep settings/counter/saves. The remote phinix.managed source now provides 1.4.0. Remove the whole manually copied bundle folder and restart before installing remotely; do not retain duplicate DLLs.

Deploy the entire new Output/phinix-rework to the actual Mod directory. If the game directly uses that Output, copy the prepared bundle below; existing files stop the command:

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework
test ! -e Output/phinix-rework/Common/Extensions/Phinix.Store.Playtest.dll && \
  test ! -e Output/phinix-rework/Common/Extensions/phinix-playtest-localization-bundle-1.4.0 && \
  cp -R /tmp/phinix-playtest-localization-bundle-1.4.0 Output/phinix-rework/Common/Extensions/
```

1. Start in Chinese: tab 商店测试 and UI/counter/confirmation use Chinese; counting and 100 silver work on a test save.
2. Switch to English: existing UI changes to Store test and English text while keeping count; unavailable Japanese/French falls back to English, without keys/blanks.
3. Disable the store and restart: Playtest still registers and translates through the host.
4. Disable Playtest and restart: tab disappears; enable/restart restores it and retained count. No callbacks remain after shutdown. Remove only this candidate DLL, companion and Resources/phinix.poc.playtest when taking out the bundled test; retain settings/saves.

This checks bundled discovery and host localization. Managed installation/removal is covered by runtime regressions; repeat actual remote managed game acceptance for the new language resources.

## Mono registration check

```sh
mkdir -p /tmp/phinix-playtest-localization-test-extensions
cp -R /tmp/phinix-playtest-localization-bundle-1.4.0 /tmp/phinix-playtest-localization-test-extensions/
dotnet build Extensions/PluginStore/Samples/Playtest/tests/RegistryCheck.csproj --configuration Release -p:BuildInParallel=false -p:NuGetAudit=false -m:1
MONO_PATH=/usr/lib/mono/4.5:/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed \
  mono Extensions/PluginStore/Samples/Playtest/tests/bin/Release/net472/RegistryCheck.exe \
  /tmp/phinix-playtest-localization-test-extensions
```

System Mono must find its own BCL first; do not prioritize the game's mscorlib. This actually discovers/registers/activates the sample, reads JSON, switches/falls back and cleans up; it invokes no game UI or silver action and does not replace game acceptance. See [the validation record](../../../../docs/branch-local/dev/plugin-store/LocalizationImplementationAndValidation.md).

## Remote 1.4.0 test

[Release](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.4.0). Endpoint https://plugins-staging.hunyuan2333.com, sourceId phinix.managed; refresh and select 1.4.0. This test still uses catalog v2; catalog v3 store-localized descriptions/changelog are pending. Package UI localization is delivered through the host. Check the installed per-plugin folder/DLL/two languages, restart and check Chinese/English/fallback, translations with the store disabled, enable/disable/uninstall.

## F4-B Compose candidate

This source now derives from ClientExtensionModule and overrides Compose. A module-owned scope registers ordinary services; constructors remain passive, Activate starts localization and Shutdown disposes the scope. Host settings/log services are borrowed. Minimum client abstractions: 1.9; update the complete matching host. Existing settings keys, callback guards and gameplay actions remain unchanged. Candidate versions: Example 1.0.3 / Playtest 1.4.0, not yet published. Historical Package/ files and fixed releases are unchanged. The packager accepts --abstractions-range; specify >=1.9.0 <2.0.0 for these new author entries.
