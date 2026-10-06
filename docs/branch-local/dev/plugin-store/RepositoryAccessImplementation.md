# Repository access adapters: implementation and validation

[中文](仓库访问适配实现与验证.md). 2026-10-05, `dev`. Implements the access batch from [the adapter plan](RepositoryAccessAdapters.md). No public asset, repository pointer or Worker deployment changed in this batch. Catalog v3 display/publishing remains the next step.

## Delivered

- `RepositoryProfile` pins source ID, public index repository/owner IDs and publication branch, separately from the selected access method/gateway address. Its identity hash binds source/IDs/branch and omits transport. Gateway-only custom sources remain explicitly tied to their configured gateway; they do not gain an inferred GitHub mapping.
- `IManagedRepositoryAccess` returns one stable/published/catalog byte chain and validated package report. `CloudflareRepositoryAccess` preserves the strict CF HTTP policy. `GitHubRepositoryAccess` uses anonymous public API requests, pins the branch commit before reading stable/descriptor, verifies public repository identity, release state, asset membership/name/length/digest and package tag → commit (at most five annotated tags). It never treats target_commitish or a download URL as proof.
- GitHub allows at most five asset redirects to the exact approved HTTPS release-assets.githubusercontent.com authority/path; no cookies or token are sent. API metadata does not redirect. Compressed/ranged/unexpected responses, over-limit/truncated/changed bytes and wrong identities fail. Total metadata budget is 30 seconds with bounded individual headers/body reads and at most 24 requests per operation. Package transfer respects the existing caller header/idle/total budgets.
- Both adapters use `ManagedPayloadTransfer`: exclusive delete-on-close held file, bounded streaming hash/length checks and existing ZIP/PE/manifest/language validators. Neither executes downloaded code. Installation still requires the shared fresh pre/post-transfer chain checks and normal all-or-nothing host transaction.
- Managed cache PCS3 is keyed by profile identity. It retains provider provenance and strips ETags when reading via another provider; snapshot/package continuity is shared across switches. Old browsing caches are invalid, with no migration reader. Offline content cannot authorize installation.
- Host-owned installation input/receipt/inventory now uses `RepositoryIdentitySha256` / JSON `repositoryIdentitySha256`, replacing the endpoint field in this unreleased development contract. Package paths remain source/package-owned; no native RimWorld Mod is generated. Known fixed-profile GitHub/CF changes do not affect installation ownership. A true repository/profile change cannot silently adopt a package.
- The built-in managed test source defaults to GitHub. Store sources has GitHub direct / CF acceleration buttons and current-method feedback; the chosen method is saved. Busy operations disable switching; cancel/finish first, then switch. Refresh through a different access/profile clears prior catalog/repository/plan state before work, and a failed switch cannot leave the former install plan actionable. Confirmation callbacks still reject replaced plans. There is no automatic fallback or plugin update.
- Audits retain client correlation, access method/profile hash, bounded GitHub request ID/rate-limit/reset/retry metadata and explicit rejection codes. Signed URLs, raw response bodies, credentials and local absolute paths are omitted. Trusted bot validator snapshots/provenance are synchronized locally (24 files); this does not update its remote running workflow.

The repository profile is currently the controlled `phinix.managed` test repository/branch. UI does not pretend that arbitrary user-entered gateway origins have an approved GitHub mapping. This is the access implementation for the current catalog v2 test chain; v3 readers/display/publisher/CF/bot integration remain pending.

## Validation

Executed from repository root:

```sh
dotnet restore Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --ignore-failed-sources -p:NuGetAudit=false -p:BuildInParallel=false
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false

dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe

dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:BuildInParallel=false -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:GameReferenceDirectory=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -m:1
dotnet build Extensions/PluginStore/Client/PluginStore.Client.csproj --configuration Release --no-restore -p:BuildInParallel=false -p:GameReferenceDirectory=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1

dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.managed /tmp/phinix-github-adapter-net10-01 --managed-github
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-cf-adapter-net10-01 --managed-cf
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://api.github.com phinix.managed /tmp/phinix-github-adapter-mono-final --managed-github
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-cf-adapter-mono-final --managed-cf
```

The live tools explicitly disable proxies and use new isolated directories. Use new output-directory names when repeating. No token is required. CLI logs for the final Mono checks are under the same /tmp basename plus .log. These checks do not install or execute the downloaded DLL.

Results: store harness 896 assertions; host managed harness 649 assertions on .NET 10 and Mono, plus actual registry/startup child checks 18 + 18 + 16. Adapter simulations verify equal publication/ZIP bytes, cross-provider ETag isolation, actual verified inventory/planner recognition, pre-cancellation, shared continuity, invalid cache and 304 rejection, bounded stalled requests, identity/tag/commit/asset/redirect/digest/length/encoding/rate-limit/missing/duplicate failures and URL-free audits. A dedicated real net472 module/initializer fixture is inspected without execution; framework/module checks were preserved.

Full solution passed with seven existing warnings; final standalone store build passed without warnings. CLI/validator builds retain NU1900 feed-access warnings. A standalone `Release 1.6` store invocation was rejected because the legacy abstraction project needs the solution's configuration mapping; the successful standalone build uses Release. No vendored protobuf files were changed. Artifact script requires unavailable PowerShell; check expected package contents separately, without claiming that script ran.

Live anonymous, proxy-disabled GitHub and CF checks passed on both .NET 10 and Mono. Both returned snapshot `002715878af86191b361d6ff642a686bb81ad550`, catalog SHA-256 `d22dcd114f7d901c1019189fb580043f23a0512e86209e4158d931242ec772f8`, and the four-file 1.3.0 package SHA-256 `5a8e14105639e0180b10cbf82923d91fe64e2e6a5a14ae6728a6851ba7575f88`.

Final review: `git diff --check` passed; working-tree changes were reviewed without committing generated outputs. The equivalent filesystem artifact check found all 25 unique required server/client paths, the expected LoadFolders entries, and no game/Unity reference DLLs in distributable output. Both built language XML files parsed with unique keys and all new access labels. All 24 bot production snapshots matched source bytes and recorded hashes; new documentation links resolved. PowerShell was unavailable, so the artifact script itself was not run. Automated checks do not establish in-game UI/network acceptance or accessibility from every geography.

## Before deploying and game acceptance

This changes a development persistence contract deliberately, without compatibility/adoption/deletion logic. **On the previous game build, finish pending operations and uninstall old managed test packages, then restart, before replacing the complete host/package.** Keep settings and saves. Do not edit old receipt hashes to force adoption. Remove the whole manually copied Playtest bundle and restart before a managed install, avoiding duplicate loaded modules.

Deploy the entire Output/phinix-rework produced by the full solution, including host Utils and store DLL/languages. For phinix.managed, open Store sources: check default GitHub, switch to CF and back, retry after blocked/rate-limited access, and restart to check selection retention. Existing profiles with an explicit saved choice keep it.

Install 1.3.0 through one method and restart. Switch methods: the same package must still show as installed, with normal enable/disable/uninstall controls and retained counter. Verify installed DLL/language resources remain together in the owned package directory, translations/fallback still work, and cancel/finish is required before switching during an operation. No hot unload/reload is introduced. A custom gateway source stays CF-only until an explicit repository profile is configured.
