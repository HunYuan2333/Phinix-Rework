# Production CF origin migration and PoC retirement

2026-10-06 latest: [standalone repository takeover and incremental human game acceptance passed](CfGatewayTakeoverAcceptance.md). Use that record for current version/source ownership; this document retains the preceding domain rollout and still-pending old-resource retirement scope.

[中文](CF正式入口迁移与退役记录.md). 2026-10-06, dev. The user accepted the 1.0.1 → 1.0.2 upgrade and authorized production finishing.

## Delivered

Official origin https://plugins.hunyuan2333.com serves fixed phinix.official through wrangler.production.jsonc / src/read-only.mjs / REPOSITORY_ENABLED. Reuse the existing phinix-plugin-repository-staging service and GITHUB_TOKEN; its infrastructure name does not imply a separate test environment. The former staging origin is a temporary alias of the same service. Both config files match so an old deployment command cannot remove the official domain. Remove the alias separately after human acceptance of the new origin.

Version 7da31ea5-902c-4105-a460-ed459ef4d183, BUILD_ID production-20261006-read-only. R2_ENABLED=false, no bucket/DO/public operations bindings, aggregate 30/60s rate limiting per Cloudflare location rather than a global billing hard limit, full structured-log sampling without per-chunk/per-frame logs.

Client default CF origin now uses the formal domain. Repository identity remains 409040fdc5aeb97524d08c2e10f573b58453fc37c7134be666c143de04c9db93; receipts/paths/data are unchanged. AccessKey changes with origin, so old endpoint ETags are not reused. Added two identity/validator assertions.

Authenticated domain checks show both domains on the official service. Direct production GitHub/.NET and CF/Mono downloads match snapshot 2514837d84f27475c17cbb5daa5464ebd0f58339; catalog SHA-256 65b18df38737a142353719f10f13f45e56eaa651070f45bbc4f1695f70f961eb; Example 1.0.2 ZIP SHA-256 b0c8d5f9dc7674f355f2c30fd05967dbfdbd5aca3b7be48949293bdfc2139a80.

Thirteen supplementary requests passed: metadata/304/published/catalog, all three immutable retained versions with trusted ZIP/CLR checks, alias equality, and rejection of public operations, unknown sources, Range, wrong method/query. Nothing installed/executed downloaded code. Cloudflare tail captured 241 structured records matching response request IDs and the production BUILD_ID, including admission/origin/verified stream/completion/rejection. A default Python User-Agent received a pre-gateway 403; normal product identification and actual clients passed, without assuming that response meant a protocol failure.

## Retained old resources and required deletion approval

Old phinix-plugin-repository-poc now has no public route, workers.dev or previews. API confirms enabled=false / previews_enabled=false. Its local config also disables routes to prevent an accidental deployment taking the official domain. Worker, namespace, bucket and secrets remain undeleted.

Three deployed service bindings were inspected; only the old PoC references its resources. The exact proposed retirement scope is:

- Worker phinix-plugin-repository-poc, original version fb9c678d-307c-481b-9290-702368429ad2.
- SQLite DO namespace a48ff4dbf6684a008e9f663de491e910 / phinix-plugin-repository-poc_CacheCoordinator. Deployed source defines old cache budget/leases/audit, not player settings/installations/saves. Full ledger export hit OperationsRpcTimeout and no complete SQL backup exists.
- phinix-plugin-poc-20261004 bucket with exactly two verified objects: 45-byte epoch marker and 4179-byte phinix.poc.marker 1.0.0 ZIP (SHA-256 0299999c8d6b14f7759e5d5dd06ce922aa520ef08e74bfc2540898cea68cfb11). Both were downloaded/verified into private ignored Output/operations-backup-20261006 outside the distributable mod. Object/config/deployment/domain/reference metadata is saved; this is not a full DO backup.
- Old service copies of GITHUB_TOKEN/POC_ACCESS_TOKEN. The current official service token and account GitHub token are excluded.

Automatic approval review rejected wrangler delete because no trusted user content explicitly approved this concrete irreversible deletion and ledger backup is incomplete. No retry/alternate deletion bypass was attempted. Only reversible public-access disabling was performed. Await explicit approval of these old resources, or retain them.

If approved: recheck official routes/inventory/hashes, delete the old Worker including its namespace/secrets, delete only the two backed-up exact objects, then delete the empty bucket without forcing removal of unknown content. Verify old resources are absent and official downloads remain valid. Old DO deletion is irreversible. GitHub catalog/approval/asset history and client data are excluded.

Official references: [object list](https://developers.cloudflare.com/api/resources/r2/subresources/buckets/subresources/objects/methods/list/), [domains](https://developers.cloudflare.com/api/resources/workers/subresources/domains/methods/list/), [Worker deletion and DO impact](https://developers.cloudflare.com/api/python/resources/workers/subresources/scripts/methods/delete/).

## Exact checks and rollback

Passed Worker 143 tests, native workerd/SQLite/R2/RPC/anonymous limiter, .NET/Mono each 858 parent assertions plus real children 18+18+16+16+20, store 902. Full solution: 16 existing warnings/0 errors; NuGet vulnerability access/old protobuf targets are environment limitations. PowerShell artifact checker is unavailable; equivalent required-files/load-folders/current-host-store-bytes/no-game-reference-DLL checks passed. Private backup is outside the distributable mod.

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework/Extensions/PluginStore/RepositoryWorker
npm test
WRANGLER_LOG_PATH=/tmp/phinix-production-local.log npm run check:production
# The native bundle prerequisites were built by npm run test:native.
# Sandbox EPERM prevented its local listener; this native command passed outside that sandbox.
node tests/native-runtime.mjs
WRANGLER_LOG_PATH=/tmp/phinix-production-deploy.log ./node_modules/.bin/wrangler deploy -c wrangler.production.jsonc
cd /home/hunyuan2333/Phinix/Phinix-Rework
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins.hunyuan2333.com phinix.official /tmp/phinix-production-cf-live-20261006 --official-cf
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-production-github-live-20261006 --official-github
```

Repeated live checks need a new absolute state directory. The Steam reference override may be replaced with correct GameDlls/1.6. Deployment was performed; this prepared rollback command was not exercised:

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework/Extensions/PluginStore/RepositoryWorker
WRANGLER_LOG_PATH=/tmp/phinix-production-rollback.log ./node_modules/.bin/wrangler rollback feb3bcce-e77d-48ea-90f0-952f7a813eba -c wrangler.production.jsonc -m "Restore previously checked official gateway"
```

Restore the previously checked official read-only version/bindings, retaining domain assignments rather than pointing at PoC. Catalog/player data are not rolled back. Recheck both routes, secret names and live download; redeploy production to restore current version. Deleting old PoC cannot recover destroyed DO data and requires explicit approval.

## Human checkpoint

Copy latest Output/phinix-rework, restart and refresh with CF acceleration. Example 1.0.2 must remain installed with normal manager/counter state; GitHub switch shows the same listing and saved access choice. No uninstall is needed for domain migration. This new-origin Unity game check is pending; alias remains during rollout. Permanent-retirement approval is separate.

[Index documentation PR #21](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/21) merged the production origin and enabled A3 facts only. Deferred prose rewriting, approvals and immutable assets are untouched.
