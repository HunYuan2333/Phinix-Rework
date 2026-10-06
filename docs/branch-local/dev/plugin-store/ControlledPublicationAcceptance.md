# First controlled publication accepted

[中文](首次受控发布验收.md). 2026-10-05, `dev`.

The human maintainer ran exact-fingerprint admission [37315639331](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37315639331) for Issue #5 and personally merged [PR #6](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/6). Input/approval merge: `9c2bbdced6f94d74b4d7035069cd8ce7b40d74eb`. [Read-only publication preflight](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37319631889) passed; [controlled publication](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37319875981) passed both jobs. A real rerun (attempt 2) returned `publication.already_complete` in both jobs, wrote no new pointer and left main at `9c2c2f8df9d9b9bd1ce97657f873d2d74d1dc079`.

## Published identity

| Field | Verified value |
| --- | --- |
| Source/repository | `phinix.official`, `HunYuan2333/Phinix-Plugin-Index` |
| Repository/owner IDs | `1402564805` / `64630568` |
| Input snapshot | `9c2bbdced6f94d74b4d7035069cd8ce7b40d74eb` |
| Catalog | schema 3, 2364 bytes, SHA-256 `e40ef11aeb304c84a278f983889941d33e81c835480ceb369160af67196311ec` |
| Published description | 406 bytes, SHA-256 `dc2ecf9e77c6ae2bdce112da0fb9728985ef75b5099654e6f3f3218991ba40a8` |
| Catalog Release/asset | `403764254` / `612714227`, `catalog.json` |
| Plugin | `phinix.poc.playtest` 1.3.0, four ZIP files |
| Plugin ZIP | 6822 bytes, SHA-256 `5a8e14105639e0180b10cbf82923d91fe64e2e6a5a14ae6728a6851ba7575f88` |

[Fixed catalog Release](https://github.com/HunYuan2333/Phinix-Plugin-Index/releases/tag/catalog-v3-9c2bbdced6f94d74b4d7035069cd8ce7b40d74eb). Package ZIP bytes and version remain the already published author asset. Official catalog and client source ownership are distinct from the old `phinix.managed` test profile.

## CF and actual client acceptance

Added only the fixed `phinix.official` index/main identity to the independent staging source list. Both old PoC sources remain. Worker `phinix-plugin-repository-staging` version `0304c261-3dbc-484f-b3d3-6f8b8a846c54` serves `https://plugins-staging.hunyuan2333.com`; build `staging-20261005-official-index`, metadata 30 seconds, limiter 30 requests/60 seconds, R2 disabled. Existing read-only origin credential unchanged; no production gateway deployment.

The explicit download checker gained `--official-github` / `--official-cf` with fixed official repository IDs/branch; production game profile/defaults were not changed. Four actual tests (.NET 10 and net472/Mono, each GitHub and CF) passed the linked production client readers/transports/cache/ZIP/PE/localization validators. All returned the same snapshot, payload digest and provider-independent repository identity `409040fdc5aeb97524d08c2e10f573b58453fc37c7134be666c143de04c9db93`. Checks covered anonymous requests with system proxy use disabled, Chinese/English display and fallback, cache round trip/revalidation, pre-cancellation, fresh chain before/after transfer and temporary cleanup. No installation directory was created and no downloaded assembly was executed. This is client-code network validation, not in-game validation or proof of connectivity from every region.

## Exact validation commands

From the Phinix workspace (use new temporary directories on repetition):

```sh
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-official-live-github-net10 --official-github
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://plugins-staging.hunyuan2333.com phinix.official /tmp/phinix-official-live-cf-net10 --official-cf
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://api.github.com phinix.official /tmp/phinix-official-live-github-mono --official-github
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.official /tmp/phinix-official-live-cf-mono --official-cf
```

Build: zero errors, two NU1900 warnings for unavailable local vulnerability-feed data. From `Extensions/PluginStore/RepositoryWorker`:

```sh
npm test
WRANGLER_LOG_PATH=/tmp/phinix-official-wrangler.log ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc --dry-run --outdir .wrangler/official-source-dry-run
WRANGLER_LOG_PATH=/tmp/phinix-official-wrangler.log ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc
```

Worker: **141 tests passed**, dry-run and deploy succeeded. The log path avoids the sandbox's read-only global Wrangler log directory. Four live-check logs are `/tmp/phinix-official-live-{github,cf}-{net10,mono}.log`; retry evidence is `/tmp/phinix-official-publication-retry.log`. Remote workflow commands were `gh workflow run plugin-publish.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f check_only=true`, then the same with `false`, followed by `gh run rerun 37319875981 --repo HunYuan2333/Phinix-Plugin-Index`. Remote build and 34 bot regressions passed in publication verification.

## Next and human testing

No additional game rebuild/test is required for this publication batch. Current game source is still `phinix.managed`. Next implement the proposed maintainer Issue-label approval path, with exact-body/actor proof, fresh validation, permanent records and automatic metadata PR handling/publication; it is not active yet. Human acceptance then consists of adding the designated label and checking the resulting record/publication. A3 normal-version monitoring remains separate and disabled.

Before exposing the official source in the game, register the same trusted profile for both access methods and test source switching. An installed test package owned by `phinix.managed` must not silently transfer ownership to `phinix.official`; use an isolated test environment or explicitly uninstall the old package before installing from the new source. Save data and other installed packages were not changed. Existing pilot bounds and lack of a host-module dependency allowlist remain as recorded in the [operations guide](../../../../Extensions/PluginStore/RepositoryAutomation/ControlledPublication.md).
