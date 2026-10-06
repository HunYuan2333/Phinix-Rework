# Playtest 1.3.0 remote test publication

[中文](Playtest130远端发布验证.md). 2026-10-05. User explicitly authorized this version's remote test publication. This record supersedes earlier “local candidate only” status; catalog v3 and direct GitHub client access remain pending.

Published to public HunYuan2333/Phinix-PluginStore-PoC, branch `codex/managed-publication`. Main and unrelated host sources were not changed. Eight sample/tool/readme/language files were published; no credentials, game/framework DLLs or local logs were included.

| Object | Identity |
| --- | --- |
| Source / snapshot commit | `002715878af86191b361d6ff642a686bb81ad550` |
| Test pointer commit | `313f1eb56d4ce830965b4865e18a436973f08b4c` |
| [Plugin release](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.3.0) | Release 403580117; asset 612183754; phinix-managed-playtest-1.3.0.zip |
| ZIP | 6822 bytes; SHA-256 `5a8e14105639e0180b10cbf82923d91fe64e2e6a5a14ae6728a6851ba7575f88` |
| Manifest | SHA-256 `1249fe3864ef05f3c28e69bf6698e4ec5053c794051e7c513647f945d4b07d3f` |
| [Catalog release](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/managed-catalog-0027158) | Release 403581372; asset 612187685; catalog.json |
| Catalog | Schema 2; 5266 bytes; SHA-256 `d22dcd114f7d901c1019189fb580043f23a0512e86209e4158d931242ec772f8` |
| Published descriptor | 407 bytes; SHA-256 `f83eaca0ee1f2e5f72a36fea5551c007f49ea673aa61a609205af38ee71b7a0f` |

Release ZIP and catalog were read back and compared to their validated local bytes before the stable pointer was advanced. Fixed GitHub REST asset IDs were used, not GraphQL node IDs. Prior immutable asset/declaration identities were retained. Current 1.2.x selection is withdrawn because those DLLs reference ClientExtensionAbstractions 1.6.0 while this host/sample requires 1.7.0. The installer does not replace an installed version in place: use normal owned uninstall/restart before testing a new version, retaining settings/save data.

Staging already allows `phinix.managed` on this branch. Only test publication content/pointers changed; no Worker code/config deployment or production-domain change was needed. The real endpoint returned the new snapshot and ZIP digest. The new manifest and language resources work through the current v2 catalog test chain; this does not implement v3 multilingual store metadata.

## Exact validation

Executed from `/home/hunyuan2333/Phinix/Phinix-Rework`:

```sh
dotnet Extensions/PluginStore/RepositoryAutomation/Validator/bin/Release/net10.0/Validator.dll payload phinix.managed /tmp/phinix-managed-130-publication/catalog.json /tmp/phinix-managed-130-publication/asset-check/phinix-managed-playtest-1.3.0.zip phinix.poc.playtest 1.3.0 /tmp/phinix-managed-130-publication/payload-validation.json

dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-managed-130-live-net10 --managed

dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-managed-130-live-mono --managed

git diff --check
```

All passed. net472 build had one NU1900 vulnerability-feed warning and no errors. Both live checks explicitly disabled proxies and exercised actual client code: stable/published/catalog, cache round trip, pre-cancellation, held ZIP length/SHA-256, PE/declared language validation, fresh pre/post-transfer chain and temporary cleanup. Both reported four ZIP files, the expected snapshot and exact digest. The downloaded DLL was not loaded/executed or installed. Main host/store had already been compiled in the localization batch; no production source changed in this publication/audit batch.

For repeated checks use new isolated state-directory paths. These results do not prove game UI/silver/managed restart behavior, all geographic network accessibility, or a not-yet-implemented direct GitHub client adapter.

## Game handoff

Remove the whole manually copied Playtest bundle and restart before remote installation, avoiding duplicate modules. Remove an earlier managed version through extension management and restart if present; retain settings/save data. Refresh `phinix.managed` at [staging](https://plugins-staging.hunyuan2333.com), select 1.3.0, install with the current dependency/download workflow, then restart. Check the package-owned DLL and two JSON languages, Chinese/English/fallback, counter retention, translations with the store disabled, and enable/disable/uninstall. See [sample steps](../../../../Extensions/PluginStore/Samples/Playtest/README.md) and [next access architecture](RepositoryAccessAdapters.md).
