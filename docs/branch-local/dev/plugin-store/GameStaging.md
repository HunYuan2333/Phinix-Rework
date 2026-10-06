# Public read-only game staging

2026-10-04 update: the user accepted the previous game download/cancel/management checks. Staging now has marker 1.0.0 **and Playtest 1.1.0**. See the current [installation/uninstall game handoff](InstallationLifecycle.md); the numerical evidence below describes the earlier marker-only snapshot.

2026-10-04, branch `dev`. [中文](游戏联网Staging.md).

The independent `phinix-plugin-repository-staging` Worker is deployed at `https://plugins-staging.hunyuan2333.com`. Source ID is `phinix.poc`; it serves the existing controlled inert package, not the formal index. Players send no bearer token. The operator uploaded the dedicated read-only `GITHUB_TOKEN` directly through Wrangler. Its value was never read or copied from the earlier Worker.

| Deployment | Purpose | State |
| --- | --- | --- |
| `plugins.hunyuan2333.com` / PoC Worker | Private gated R2/SQLite experiment | Existing expiry, bucket and ledger preserved; not redeployed in this batch |
| `plugins-staging.hunyuan2333.com` / staging Worker | Anonymous game transport check | Independent origin-only reads; no bucket, DO, operations export or PoC expiry |
| Formal production | Approved source and durable delivery | Not deployed; formal index unchanged |

Staging refuses GitHub access without `STAGING_ENABLED=true`, valid origin credentials and its rate-limit binding. It rejects accidental cache bindings/configuration, invalid paths, methods, Range and unknown sources before origin access. Missing credentials were observed as HTTP 503 `OriginCredentialsMissing` before upload; both Python and Mono error diagnosis were correlated. The Cloudflare limiter uses one aggregate key, 30 requests/60 seconds per location. Its eventually consistent local counters are test throttling, not a global cost or GitHub quota bound. All responses use `no-store`. Disable staging by setting `STAGING_ENABLED=false` in the dedicated config and redeploying it.

Cloudflare rate-limit behavior: [official documentation](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/).

## Real streaming issue found and corrected

The initial Python byte checks passed, but the actual net472/Mono transport rejected ZIP headers as `PayloadSizeMismatch`. A direct header probe found 4179 bytes, `Transfer-Encoding: chunked`, and no `Content-Length`. Workers ignores manually supplied lengths for a generic `ReadableStream`, as documented in [Response](https://developers.cloudflare.com/workers/runtime-apis/response/).

The gateway now pipes its verified stream through a native `FixedLengthStream` for response framing. The final-byte digest holdback remains before framing; corrupt/incomplete streams still fail. Client length/hash checks were not relaxed. Unit tests cover valid and corrupt framing, and native workerd asserts the actual length header. The PoC deployment still runs its earlier code until deliberately redeployed; the shared source fix is currently deployed only to staging.

Final staging build: `staging-20261004-v2`. Deploy command returned version `a8bdaf64-ce8c-48a4-bd33-d970e1b2b8d8` after the operator's secret upload.

## Validation evidence

- Worker regression: 117 tests passed; public guard/limiter failures and valid reads, framing/stream failures, existing cache/recovery behavior.
- Native workerd: staging anonymous access, real limiter binding, real `Content-Length`, and existing SQLite/R2/internal-RPC/recovery regression passed.
- Public Python probe: nine no-proxy assertions passed, including stable/published/catalog, two origin ZIP reads, 304, Range 416 and query 400. Both ZIPs had `Content-Length: 4179`, exact expected bytes and SHA-256 `0299999c8d6b14f7759e5d5dd06ce922aa520ef08e74bfc2540898cea68cfb11`.
- Filtered Worker audit: 280 records; nine Python requests correlated. Five Mono responses were also matched by request/client ID and terminal status. Mono package request: `a9b44ddd-2e8d-4dce-819d-3b96a1c0721e`.
- Actual production client sources compiled into a net472/Mono live checker: verified metadata chain, local browse cache/304, pre-cancelled package without HTTP, ZIP length/digest/static validation (three files), held read-only file and disposal cleanup. No partial files remained and no Mods directory or downloaded assembly was loaded. Exit 0.
- Client regression: 500 assertions passed. Client net472 build: zero errors/warnings; preview output refreshed. Live checker builds net472/net10.0; NU1900 vulnerability-feed warning remains. Its net10.0 target was compiled but not used for live acceptance.
- Python probe regression: four tests passed, including no secret file/read/header in public mode and rejection of missing package length.

No in-game claim follows from Mono or compile success. This is one network and a 4179-byte package. In-flight game cancellation, Unity networking, large ZIP memory/CPU, carrier/region reliability and production budgets remain pending. Logs/credentials are ignored private files, not committed artifacts. The bounded tails ended with timeout exit 124 after capture; validation commands exited 0.

Exact commands from the Worker directory:

```sh
npm test
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false timeout 45s npm run test:native
python3 tests/live-probe.test.py
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy python3 tests/live-poc.py --endpoint https://plugins-staging.hunyuan2333.com --directory .wrangler/private-staging-20261004/framed-response --direct --public --origin-only --require-package-length
python3 tests/live-audit.py --directory .wrangler/private-staging-20261004/framed-response
```

The Python live commands require separately prepared private expected fixtures and a concurrent Wrangler tail for audit; public mode reads no secret. Preserve failed evidence when rerunning. Commands from the repository root:

```sh
dotnet restore Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --ignore-failed-sources
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --no-restore -m:1
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.poc /tmp/phinix-staging-mono-20261004-framed
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore
dotnet build Extensions/PluginStore/Client/PluginStore.Client.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

The live checker requires a new absolute state directory for every run. Replace the `/tmp/...` argument on a repeat. It explicitly disables proxies and preserves normal TLS verification; it needs no game DLLs or client bearer token. This explicit live project is not added to automatic CI or the solution build.

## Game checklist and next implementation

Use the rebuilt preview at `Output/phinix-plugin-store-preview` alongside the existing host. Restart the game after replacing assemblies. In the store source settings enter endpoint `https://plugins-staging.hunyuan2333.com`, source `phinix.poc`.

1. With proxies/TUN disabled, refresh online. Expect `phinix.poc.marker` 1.0.0 and `phinix.poc.playtest` 1.1.0 entries and no credential/expiry error.
2. Select the package and view its plan. Expect no dependencies and a valid download plan.
3. Click the download/validate preview action. Expect three validated files and a correlated request ID, with no installed mod entry.
4. Refresh again, try cancelling then retrying, and leave/re-enter the store during work. A late callback must not replace a new operation or leave the page busy. This small package may complete too quickly to test in-flight cancellation; record that limitation.
5. After a successful refresh, disconnect and use the offline browsing cache. Online failure should retain the prior valid browse state and allow retry; offline data does not authorize downloads.

For failure reports retain the action, UI code, request ID and time. Client structured events include `repository.*` and `package.*`; Worker events include `staging.read_admitted`, `origin.*`, `stream.*` and `request.complete`. Keep full platform logs private.

After game feedback, implement P4 new-package staging/ownership/journal/recovery and the confirmation/commit freshness checks. Production R2 requires a separate bucket/ledger and an audited operation-period transition preserving occupied/reserved bytes and uncertain states; the fixed PoC period must not be extended or reset to simulate permanent service. Formal publication, signing decision, large-file/CPU/cost acceptance and network samples remain separate production work. This batch prepared and deployed game staging, not a production configuration ready for public rollout.
