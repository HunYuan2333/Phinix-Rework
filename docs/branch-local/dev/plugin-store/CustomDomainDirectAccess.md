# Custom-domain direct-access acceptance

Date: 2026-10-04. Branch: `dev`. [中文](自定义域名直连验证.md).

The user has a domain and reports that TUN is disabled. The existing public test service at `https://cf-test.hunyuan2333.com` was tested from the current host with all six uppercase/lowercase HTTP, HTTPS and ALL proxy variables removed, and curl proxy use explicitly disabled. Normal certificate verification remained enabled; redirects were not followed. No DNS, route, Worker deployment or existing test service was modified.

| Request | Result |
| --- | --- |
| Custom-domain homepage, two requests | HTTP 200; 0.783 s and 0.714 s; TLS verification passed |
| `/test.js` and `/files.json` | HTTP 200; existing test-file protocol inspected |
| `/downloads/5m.bin`, one download | HTTP 200; 5,242,880 bytes; 3.061 s; 1,712,938 bytes/s |
| File integrity | Size and published SHA-256 matched: `b367a479527806f00633fda745e6a1546bef92cde170da24b631c5b4c8b0f4ef` |
| Existing PoC `workers.dev/diagnostic` under the same no-proxy settings | Connection timeout after 5.005 s; HTTP 000 |

This supports continuing the custom-domain Worker + private R2 design without first adding a separate server. It demonstrates one host/network and the existing static test service; it does not prove the plugin gateway, Unity/Mono transport, mainland-wide reachability, sustained reliability, cold GitHub origin, large ZIPs or billing budgets. A hostname bound to the real plugin Worker is still needed. The existing test service should remain available for comparison.

Production clients should access the selected repository custom domain for metadata and package bytes. GitHub release redirects remain inside the gateway; players must not need direct GitHub/R2 access or the internal Wrangler operations bridge. Current package validation, fixed source/snapshot/hash, HTTPS and redirect refusal remain intact. The temporary authenticated PoC is not a public game endpoint; its token and expiry must not be removed simply to claim game acceptance.

Next: select a dedicated repository hostname in the user's zone; bind the real gateway and verify TLS without altering cache history; test a controlled real catalog and ZIP with proxies/TUN off; collect independent mainland carrier/region samples; then validate game networking and realistic payload sizes. Add a different regional delivery path only if measurements justify it. Custom-domain success is evidence to test further, not a universal availability guarantee.

Exact homepage command (file download used the same flags plus a 6 MiB size bound and 30-second total timeout):

```sh
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy \
  curl --noproxy '*' --connect-timeout 5 --max-time 12 --silent --show-error \
  --output /dev/null --write-out 'http=%{http_code} time=%{time_total}\n' \
  https://cf-test.hunyuan2333.com/
```

Cloudflare supports using a Worker as the origin of a Custom Domain and handles its DNS/certificate configuration: [official documentation](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/). The initial static-test batch created no binding; the authorized real-gateway follow-up below records the later binding.

Exact 5 MiB transfer command:

```sh
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy \
  curl --noproxy '*' --connect-timeout 5 --max-time 30 --max-filesize 6291456 \
  --silent --show-error --dump-header /tmp/phinix-custom-domain-5m.headers \
  --output /tmp/phinix-custom-domain-5m.bin \
  --write-out 'http=%{http_code} bytes=%{size_download} speed=%{speed_download} remote_ip=%{remote_ip} tls_verify=%{ssl_verify_result} time=%{time_total}\n' \
  https://cf-test.hunyuan2333.com/downloads/5m.bin
```

## Authorized real-gateway follow-up

The user authorized the trial. `wrangler.poc.jsonc` now binds `plugins.hunyuan2333.com` to the existing isolated `phinix-plugin-repository-poc` via a Custom Domain in `hunyuan2333.com`. Initial DNS returned NXDOMAIN; public DNS then returned the zone's Cloudflare addresses, and `resolvectl query --cache=no` refreshed normal system resolution without changing resolver configuration. The first TLS/route check used curl `--resolve`; later protocol requests used ordinary DNS and no proxy. The existing `cf-test` service was not changed.

Standard Wrangler `triggers deploy` added the route without uploading different Worker code. The lost local PoC access token was replaced using `wrangler secret put POC_ACCESS_TOKEN`; the new random value is held only in ignored `.wrangler/private-domain-poc` files (directory 0700, secret files 0600). The normal PoC expiry, private R2 bucket, singleton DO and accounting configuration were retained. No personal gh/OAuth token was extracted or uploaded, and no recovery confirmation or counter reset occurred. At that stage the dedicated `GITHUB_TOKEN` secret was absent; the authenticated-origin follow-up below supersedes that blocker.

Direct acceptance removed all HTTP/HTTPS/ALL proxy variables and used the probe's explicit `--direct` handler. Unauthenticated requests returned the expected correlated 401. One authenticated stable request returned HTTP 200 in 1532 ms; all 342 bytes matched the published fixture (SHA-256 `6ccbe44d8ff6be3a1143fce901704c1664a7038f4677c296fbfaa73232115db2`, request `34c1e846-fc82-4220-b9d5-106b40e1746a`). The next published-descriptor request returned 503 `OriginRateLimited` in 649 ms (`81bca457-1e0e-41eb-b176-0c55c2912b50`). Filtered Worker audit recorded GitHub HTTP 403 and `rateLimitRemaining=0`; its reset was 2026-10-04 11:35:46 Singapore time. A single bounded repeat failed at stable with the same error (`872affd8-f6b8-4384-a4af-c992f88cc8eb`). Initial evidence was retained separately; retries were not continued.

`tests/live-audit.py` accepted error/request correlations for both captures (27 and 10 filtered records). This does not establish package transfer or cache-hit acceptance. The domain's real ZIP was not reached: the metadata chain failed before the package request. The separate .NET payload checker accepted the GitHub-fetched expected catalog/stable/published/ZIP baseline (three files); it is not a direct-domain or game-network result. It exited 0 with the existing NU1900 vulnerability-feed warning.

Commands from the Worker directory:

```sh
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false ./node_modules/.bin/wrangler triggers deploy -c wrangler.poc.jsonc --dry-run
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false ./node_modules/.bin/wrangler triggers deploy -c wrangler.poc.jsonc
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false ./node_modules/.bin/wrangler secret put POC_ACCESS_TOKEN -c wrangler.poc.jsonc < .wrangler/private-domain-poc/access-token.txt
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy python3 tests/live-poc.py --endpoint https://plugins.hunyuan2333.com --directory .wrangler/private-domain-poc --direct --existing-cache
python3 tests/live-audit.py --directory .wrangler/private-domain-poc
```

The direct live probe returned exit 1 on both attempts; no full protocol or ZIP success is claimed. The audit command returned exit 0 for both captures. The probe now refuses redirects, can explicitly disable proxies, reports request durations and labels the pre-existing cache test accurately. Its 2 MiB response bound is for this small controlled fixture, not large-package acceptance.

Local baseline command from the repository root:

```sh
dotnet run --project Tests/PluginStorePayloadCheck/PluginStorePayloadCheck.csproj --configuration Release --no-restore -- phinix.poc Extensions/PluginStore/RepositoryWorker/.wrangler/private-domain-poc/catalog.json Extensions/PluginStore/RepositoryWorker/.wrangler/private-domain-poc/phinix-poc-marker-1.0.0.zip Extensions/PluginStore/RepositoryWorker/.wrangler/private-domain-poc/metadata/stable.json Extensions/PluginStore/RepositoryWorker/.wrangler/private-domain-poc/metadata/published/e3c57856b3a68c1d25e18dbe2b4f90e4e8618779.json
```

The initial next dependency was to configure a separate short-lived fine-grained GitHub origin token, selecting the controlled `Phinix-PluginStore-PoC` repository with Contents read-only (Metadata read-only), as Worker secret `GITHUB_TOKEN`. Enter it using standard Wrangler on the operator terminal, not in chat or Git. [GitHub token instructions](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens). Then repeat direct frozen metadata/ZIP checks with audit. Keep the temporary PoC access gate and expiry; the game transport still does not send that gate token. Player connectivity to the custom domain and Worker-to-GitHub authentication are separate requirements. Multi-network reliability, formal publication, game networking, actual package sizes and cost limits remain pending.

## Authenticated-origin direct acceptance

The operator uploaded `GITHUB_TOKEN` using standard Wrangler. Its value was not read or displayed. The subsequent no-proxy check passed all nine protocol assertions on `https://plugins.hunyuan2333.com`, using normal DNS, certificate verification and the temporary PoC access gate. This host initially returned a DNS lookup failure; `resolvectl query --cache=no plugins.hunyuan2333.com` obtained fresh answers without changing resolver configuration, then the full check succeeded.

| Request | HTTP | Bytes | Duration |
| --- | --- | --- | --- |
| Unauthenticated stable | 401 | 95 | 585 ms |
| Stable | 200 | 342 | 1603 ms |
| Published descriptor | 200 | 404 | 1647 ms |
| Catalog | 200 | 1047 | 2570 ms |
| ZIP, existing cache | 200 | 4179 | 3113 ms |
| ZIP, repeat | 200 | 4179 | 2571 ms |
| Conditional stable | 304 | 0 | 1496 ms |
| Range request | 416 | 97 | 608 ms |
| Query-string request | 400 | 91 | 713 ms |

Metadata and both ZIP responses matched the expected raw bytes exactly. The ZIP SHA-256 was `0299999c8d6b14f7759e5d5dd06ce922aa520ef08e74bfc2540898cea68cfb11`. Filtered audit accepted all request/client correlations and terminal status checks (151 records), with `cache.r2_hit`, `cache.hit` and `stream.verified` for both package requests and no new fill reservation. Package request IDs were `0837047d-d0ec-4816-ad8a-11bacdbfe4b0` and `37be9da0-10de-4abe-bc5a-b72d82169184`. GitHub API audit showed positive remaining quota (4938–4997 in captured responses), replacing the prior zero-quota failures. This verifies usable authenticated origin access, not the token's precise permission configuration or sustained quota sufficiency.

The live and audit commands below both exited 0. Raw platform logs and local credentials remain only in the ignored private evidence directory; earlier failed attempts were retained. The bounded 55-second Wrangler tail ended with timeout exit 124 after capture, which is separate from the successful validation commands.

```sh
# From Extensions/PluginStore/RepositoryWorker; private fixtures are prepared separately.
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy python3 tests/live-poc.py --endpoint https://plugins.hunyuan2333.com --directory .wrangler/private-domain-poc/authenticated-origin --direct --existing-cache
python3 tests/live-audit.py --directory .wrangler/private-domain-poc/authenticated-origin
```

This establishes the controlled metadata/ZIP chain on this host/network without endpoint proxies. No code deployment, formal index publication, cache reset, recovery confirmation or item-ownership change was performed in this follow-up. The existing cached small ZIP does not establish a new cold package fill, large-file performance, game networking, multi-carrier mainland reliability or production cost bounds. Origin checks still occur and the small ZIP took 2.6–3.1 seconds; edge caching is not enabled, so this is correctness evidence, not a production performance result.

Next: arrange a separate game-accessible read-only staging endpoint/source, preserving the isolated PoC's gate and expiry (2026-10-05 16:10:49.973 UTC), then validate the actual net472/Unity metadata and bounded download path with cancellation, failures and logs. The current game transport does not send the PoC gate token. Collect other mainland carrier/region samples and realistic payload measurements before public rollout; continue with CF Worker + private R2 on current evidence.

## Credential replacement follow-up, 2026-10-04 03:24 UTC

After the operator reported replacing the origin token, the same nine no-proxy protocol checks passed again. Stable, published, catalog and both ZIP responses were byte-exact; both package requests hit R2 and verified their streams. All request/terminal associations passed against 151 filtered audit records, with positive GitHub quota. Package requests: `7339f1e1-3630-476c-bf8b-ff3e869de773` (2603 ms) and `c259d421-e94b-4037-9822-a133515f84de` (3954 ms). Both validation commands exited 0; no build was needed for this credential-only service check. The token value and its GitHub expiry/permission settings were not inspected. PoC serving expiry and accounting period remain unchanged; token replacement alone does not convert this deployment to production. Multi-network, large-file and game acceptance remain pending. Commands from the Worker directory:

```sh
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy python3 tests/live-poc.py --endpoint https://plugins.hunyuan2333.com --directory .wrangler/private-domain-poc/token-rotation-20261004T032410Z --direct --existing-cache
python3 tests/live-audit.py --directory .wrangler/private-domain-poc/token-rotation-20261004T032410Z
```
