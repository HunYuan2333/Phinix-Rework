# Gateway deployment ownership and acceptance

[中文](CF独立发布接管与验收.md) · 2026-10-06 · S1 / M1–M4 completed.

The explicitly approved 62-file `e31bfcce71bdce4317175b0ccff2275432734d7e` source was published to [Phinix-Plugin-Gateway](https://github.com/HunYuan2333/Phinix-Plugin-Gateway). Initial CI failed before jobs because job env cannot use runner context ([official context table](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts)). Minimal repair `92aea4808c794b4a52aaa21d14830feac18d7076` preserves runtime/configuration.

[CI 37416583678](https://github.com/HunYuan2333/Phinix-Plugin-Gateway/actions/runs/37416583678) passed clean installation, 144 Worker tests, Python 5+5+4 and native workerd/SQLite/R2/RPC/anonymous limiter. Actions defaults are read-only/no approval; main requires authentic Actions app 15368's `check`, denies force pushes/deletion and retains administrator management exceptions. No cloud deployment job/CF credential in GitHub.

## Live delivery and checks

The same `phinix-plugin-repository-staging` now runs version `90d4921e-7704-4296-a07a-198046e96a76`, BUILD_ID `gateway-92aea4808c79`, from clean CI-passing source. Prior active version `7da31ea5-902c-4105-a460-ed459ef4d183` is retained. Domains, official identity, origin secret and limiter are unchanged, with no R2/DO/admin binding. Dry-run normal/takeover JS bundles match: 47745 bytes, SHA-256 `4f59c5e11547d45e3223883df2761a6e047b89b5b8c4039261af4ea747a5c1b0`. Only audit BUILD_ID was overridden by CLI.

Read-only before/after cloud inventory verifies domains/binding types/secret name/public vars and disabled PoC public access without secret values. Private snapshots are ignored under `Output/gateway-migration-20261006` with mode 0600.

Actual proxy-free Mono/CF and .NET/GitHub download paths passed metadata/cache/cancellation/fresh pre-post checks/ZIP-PE/temporary cleanup, without installing/executing downloaded code. Both show snapshot `2514837d84f27475c17cbb5daa5464ebd0f58339`, catalog SHA `65b18df38737a142353719f10f13f45e56eaa651070f45bbc4f1695f70f961eb` and Example 1.0.2 ZIP SHA `b0c8d5f9dc7674f355f2c30fd05967dbfdbd5aca3b7be48949293bdfc2139a80`.

Thirteen fixed-snapshot requests passed formal/alias metadata, 304, published/catalog digests, all three retained version ZIP/CLR checks and rejected operations/source/range/method/query. All 13 response request IDs match 255 structured server records with the new build and terminal completion; all three packages have verified-stream events. Raw tail was not published and has stopped; private audit summary is retained.

The user confirmed the incremental game check: CF refresh, matching GitHub listing, retained Example Tab/counter/settings all normal. This does not certify other platforms/networks/large packages or RedPacket/Talent business behavior.

## Main-source removal and validation

After acceptance, all 52 former main-source files matched reviewed import/migration hashes. A checked tar backup precedes deletion of only 49 known files; bilingual pointers and `.gitignore` remain. Unknown files and ignored local dependency/runtime state were preserved. `.dockerignore` excludes the retired location from server context. Client projects/DLL packaging and unrelated dirty changes were not committed or modified.

Private source tar SHA `f5ef7f6b13d3b9c65336e2cbd22470f855c235d99bfe8f921e495386bb35ee89`; `main-removal-inventory.json` records exact files. Original source tar/bundle remain outside game distribution. They are not a complete cloud DO backup.

Actual commands:

```sh
cd /tmp/phinix-plugin-gateway-20261006
git push -u origin main
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-takeover-dry-run.log ./node_modules/.bin/wrangler deploy -c wrangler.production.jsonc --var BUILD_ID:gateway-92aea4808c79 --dry-run --outdir .wrangler/takeover-dry-run
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-takeover-deploy.log ./node_modules/.bin/wrangler deploy -c wrangler.production.jsonc --var BUILD_ID:gateway-92aea4808c79 --strict
cd /home/hunyuan2333/Phinix/Phinix-Rework
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins.hunyuan2333.com phinix.official /tmp/phinix-gateway-live-cf-20261006 --official-cf
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-gateway-live-github-20261006 --official-github
python3 /tmp/phinix-gateway-protocol-check.py
dotnet build Server/Server.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
```

Server passed with six existing protobuf trim/unreachable NuGet vulnerability-feed warnings and zero errors. Docker image build/full client rebuild/all gameplay tests were not rerun; game packaging behavior did not change.

PowerShell is unavailable, so the artifact script itself was not executed. Equivalent Python checks passed its 26 path matches, load-folder order and server/current-client game-DLL exclusion, plus retired-source/config absence, Docker exclusion and bilingual evidence links.

The prior platform version is verified and the rollback command is prepared, **not exercised**:

```sh
cd /tmp/phinix-plugin-gateway-20261006
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-rollback.log ./node_modules/.bin/wrangler rollback 7da31ea5-902c-4105-a460-ed459ef4d183 -c wrangler.production.jsonc -m "Restore verified production version"
```

Recheck routes/bindings/downloads after rollback; code ownership can be restored from backup/public source without rolling back catalog/player data. Old PoC Worker/R2/DO/secrets remain undeleted and need separate concrete retirement approval. Alias remains. S1 closes; S2 resource/save prerequisites are next. RedPacket/Talent remain bundled.
