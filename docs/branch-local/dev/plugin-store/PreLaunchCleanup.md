# Pre-launch cleanup

[中文](正式上线前清理.md). 2026-10-05, dev. This is a separate release gate before automatic version intake and feature extraction. Players must not encounter test sources. Keep only the latest Playtest in its standalone repository; exclude it from the official index.

## UI / UX assessment

The installation-to-uninstallation route is accepted, but the existing screens expose development tools. Translation alone does not make that a player experience.

| Content | Player experience | Maintenance location |
| --- | --- | --- |
| Official catalog | Fixed default, GitHub direct | Trusted client configuration |
| GitHub / CF | Quick switch to the same catalog | Shared source/ownership/cache identity |
| Test source, source IDs, endpoint URLs, local JSON | Remove from player controls; ignore obsolete development source settings | Standalone CLI checks and regression fixtures |
| Manual plan and download-only actions | One Install action | Mandatory internal planning, validation, origin rechecks and atomic commit |
| Additional dependencies | Confirm names, versions, status and download size | Strict complete closure validation |
| Versions | One entry per package, selector, latest active default | Immutable assets/digests/compatibility per version |
| Package/module IDs, hashes, CLR references, request IDs and paths | Remove from ordinary status/details | Error details, structured logs and management diagnostics |
| Compatibility, summary, author, license, changelog, actual installed version/restart | Keep for meaningful decisions | Language-aware validated metadata |
| Legacy Mod cleanup/preview | Remove player entry, never delete unknown files | Source/tests may remain, old preview UI excluded from the normal store build |
| Enable/disable/uninstall | Extension manager; same-owner catalog entries | Preserve data/settings/saves; never adopt another source's installation |
| Byte/stage progress | Still required before release | Throttled callbacks, no per-frame network/logging |
| Updates and version replacement | Not delivered; do not expose a working Update button | Replacement/recovery first, then notification-only checks |

## Ordered implementation

1. Fix the player entry to phinix.official; remove test selection, developer input fields and preview UI. Do not migrate old development source settings or alter existing installations and recovery.
2. Exclude Playtest with a maintainer-owned reasoned listing policy. Publish a new immutable catalog and update stable last; retain original approved records, locks and historical snapshots. Validate the visible dependency closure, including failure when a remaining plugin needs an excluded package. Future publication must not resurrect Playtest.
3. Clean the standalone Playtest repository: latest source, packager, localization and documentation on its default branch; retain only release/tag v1.3.0 and its unchanged asset/sourceCommit. Remove obsolete plugin/catalog releases and obsolete test-source service. Do not copy fixture source into the official index.
4. Retain official workflows, trusted validators, regressions, approvals and historical snapshots as maintenance infrastructure. Closing an unapproved Issue rejects it; withdrawing a published listing uses an auditable exclusion.
5. Serve only the official index through CF. The current gateway still uses plugins-staging.hunyuan2333.com; move to the final domain before release and review secrets, privileges, quotas and environment naming. Verify byte/hash/source parity with GitHub.
6. Connect grouping and one-click installation; guard stale confirmation, canceled planning, source switching and retries. Validate installation with a real publishable plugin. Exercise Playtest as a developer-only folder bundle outside the official index.
7. Complete progress, friendly error messages, empty states, narrow-window layouts and button states; perform manual acceptance and artifact checks. Compilation is not in-game validation.

## Manual game checkpoints

- Fresh/old development settings both show only the official catalog. Distinguish an empty catalog from a search with no matches.
- GitHub/CF show the same content and remember the selected access method across restart; no test-source/address/path/legacy cleanup controls.
- An existing Playtest remains manageable/uninstallable in Extension manager but absent from the store. Settings, counters, saves and unknown files remain untouched.
- English/Chinese, narrow windows, scrolling, failed operations and error details have no missing keys, out-of-bounds controls or leaked GUI state.
- Once a real plugin is listed: one-click install, dependency confirmation/cancellation, version selection, withdrawn read-only behavior, restart discovery and uninstall. An empty catalog does not prove this acceptance.
- Byte progress and replacement are separate pending deliveries; retest those after implementation, not the previously accepted three-step fixture flow.

## Batch status and validation

Record implementation and live evidence here after completion. Final domain, progress, replacement and manual game acceptance remain release gates. Do not commit the main project's pre-existing dirty tree as part of this batch.

2026-10-05 local delivery: official default/access switch, grouped versions, one-click install/dependency confirmation, separate error details and removal of preview UI/standalone package target. Index cleanup code PR #14 merged at 098e5c3b30299860b775ebc5199afe8b01d7b6a4; the cleaned catalog is not published yet. Playtest cleanup PR #1 is open; ten obsolete releases/tags and the retired publication branch have not been deleted. Manifest: /tmp/phinix-prelaunch-cleanup-manifest.json. Automatic approval review required explicit authorization for the empty official catalog publication; requested and pending.

CF is deployed with the official source only, version feb3bcce-e77d-48ea-90f0-952f7a813eba. The gateway domain/Worker still use staging names; final migration is pending. Old PoC Worker/storage resources were not deleted and require an independent inventory/retirement step.

Validation: store Release build passed; .NET 10/Mono managed runtime each passed 697 assertions; responsive geometry harness passed; trusted Validator and 58 bot regressions passed locally and in the independent deployment checkout; 141 Worker tests and cleanup config dry-run/deploy passed. English/Chinese key sets match with no duplicates and all literal view keys present. No new in-game acceptance. PowerShell is unavailable, so the standard artifact checker cannot run here; verify contents separately. Initial parallel store builds failed without diagnostics, serial -m:1 passed. Existing NuGet audit-service access and protobuf net50 trimming warnings remain environmental limitations.

### Exact validation commands

```sh
dotnet build Extensions/PluginStore/Client/PluginStore.Client.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj -c Release --no-restore -p:BuildInParallel=false
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj -c Release --no-restore
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
```

```sh
cd Extensions/PluginStore/RepositoryWorker
npm test
WRANGLER_LOG_PATH=/tmp/phinix-cleanup-wrangler.log ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc --dry-run --outdir .wrangler/cleanup-dry-run
WRANGLER_LOG_PATH=/tmp/phinix-cleanup-wrangler.log ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc
```

Remote read-only preflight [37332368226](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37332368226) passed: 58 tests, fresh artifact verification and empty-catalog validation; publish/notify skipped, live stable unchanged. The PowerShell artifact checker was unavailable; an equivalent contents check passed required files, LoadFolders, current store bytes and absence of game reference DLLs.

The user explicitly approved the cleanup and requested a separate official example derived from Playtest without silver generation. Empty-catalog publication 37333360253 succeeded; Playtest PR #1 merged at bed97144e65528851d14b0afe765e2e19f261b48; ten obsolete releases/tags and codex/managed-publication were deleted, keeping original v1.3.0 only. The earlier authorization-pending note is closed. The next official listing is the new example, not Playtest.

Official example: phinix.example.basic 1.0.0 in HunYuan2333/Phinix-Example-Plugin, source commit beginning d9227d8. Localized tab/counter plus live settings, confirmed reset and stale-lifecycle callback protection; no map/colony/item API, only package-prefixed profile settings shared across saves. Fixed ZIP: 7250 bytes, SHA-256 122b172c931f60df685397614ce8927eead1b64c9ca8863fe2335be531d52f7f; trusted payload/publication validators passed. Normal Issue #15 is submitted; admission/publication acceptance is in progress.

### 2026-10-06: cleanup and official example completed

Normal [Issue #15](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/15) passed, the bot merged [PR #16](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/16), [publication 37335979507](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37335979507) succeeded, and the Issue closed without plugin-error. The current catalog contains only phinix.example.basic 1.0.0, with no Playtest listing. Snapshot f35a51796178b11a0c8e098d76c14c9eecb8a92f; catalog SHA-256 7abf24929ea6e2227dcb17c9fa26e2b399023468c1b63e3e8994a4a332d8cb59.

Real .NET/GitHub and Mono/CF client checks passed with identical catalog and 7250-byte ZIP/hash: metadata/cache, pre-cancellation, fresh pre/post-transfer verification, ZIP/PE validation and temporary cleanup. These checks do not install or execute DLLs and are not game acceptance. [Guide PR #17](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/17) merged at 61e2b19bb0312a2f2ca1e0f1ebccf938c44d9bf6: the author template uses the official example, historical Playtest input moves to tests/fixtures, and all 58 regressions pass. Example documentation commit bd75f97 adds evidence only; the v1.0.0 source tag and ZIP are immutable.

Mod settings now hides untranslated SectionId identities while preserving existing Verse translation-key headings. Plugins draw their own localized title; both developer guides explain this. Direct host Release 1.6 build failed because the abstraction project lacks that configuration; the solution's configuration mapping builds successfully, with 16 existing warnings and zero errors. PowerShell remains unavailable; equivalent Python artifact checks passed required files, LoadFolders, current host/store output equality and absence of game reference DLLs.

Manual acceptance: copy the current host output, install the official Example Plugin and restart. Check its localized tab and settings section; click the counter; change Show explanations and observe the tab immediately; cancel reset then confirm it; restart to verify count/options persistence; switch English/Chinese and check titles/formatting; switch saves and verify shared profile settings (intentional); disable/enable/uninstall and restart to verify providers vanish or return. Remove a manual folder copy before store installation to avoid duplicate identity. Old Playtest counts do not migrate.

No trade protocol, item ownership or save format changes. The example writes only its own retained profile settings; CLR unloading requires restart, as do installation/disable/uninstall. Production domain/environment migration, old PoC Worker/storage inventory, byte progress, version replacement and release monitoring remain outstanding gates.

```sh
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
python3 -m unittest discover -s /tmp/phinix-prelaunch-index/tests -v
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-example-github-final-check --official-github
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.official /tmp/phinix-example-cf-final-check --official-cf
```


### 2026-10-06: progress, upgrades and approved-source updates

These three previous gaps are implemented and automatically verified; [current evidence and human checkpoint](StoreFinishingAcceptance.md). Example 1.0.1 was automatically published without another approval label. New game acceptance, final domain migration and old Worker/storage retirement remain pending.

2026-10-06 production origin migration is deployed/verified; permanent PoC retirement awaits concrete approval. [Current deployment/inventory](ProductionCfMigration.md).
