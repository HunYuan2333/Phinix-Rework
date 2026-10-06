# Catalog v3 implementation and validation

[中文](目录v3实现与验证.md). 2026-10-05, dev. This completes the next catalog/display/publication-format batch after the accepted GitHub/CF game flow. It does not deliver A2 admission, automatic publication, upgrade transactions, formal store interaction or official-plugin separation.

## Delivered

- Managed catalog v3 replaces v2 directly. `localization.translations[locale]` supplies name/summary/optional changelog; top-level managed name/summary are forbidden. Workshop metadata/link behavior remains outside plugin language imports.
- Common `ExtensionDisplayLocalization` shares language normalization, script-aware per-field fallback and text bounds with package UI localization. Single-language/no-English packages are valid. Unknown/duplicate/normalized-duplicate locales, absent defaults, markup/control/surrogate/text/aggregate limits fail. UI-only language files can have an empty display map.
- Store lists, details, search, sorting and plan confirmations use the requested game locale supplied by the generic host localizer. Language/selection/search/snapshot changes invalidate cached text/measurement. Changelog is rendered as plain text with an explicit absent message. There are no extra browsing requests for language data.
- ZIP validation compares the complete display projection against declared, length/hash-verified language Resources. Catalog text changes cannot bypass this check. Canonical IDs, manifest/assembly names, dependency identifiers and installed receipts are unchanged. Catalog-only languages remain available to packages without a localization declaration.
- Managed browsing cache PCS4 accepts the new chain only; older caches are invalid, without migration or deletion of installed packages. The logical repository identity and provider ETag boundaries are unchanged.
- Packager `--display-output` emits display metadata. `RepositoryAutomation/scripts/catalog.py project` validates an existing ZIP and extracts its owned language display. `build` validates a complete catalog before writing a create-only local draft. Drafts grant no admission authority and perform no remote writes. Outer metadata draft generation accepts v3.
- Worker accepts managed v3 and routes `manifest.version` / managed-dll-zip correctly; strict managed v2 rejection retains the independent preview v1 route. It validates localized display using the same fixtures as Common and the bot, including aligned Unicode whitespace handling.
- The trusted validator's 24 production snapshots/provenance, bot v3 candidate generation, example and isolated Python tests are synchronized locally. Index draft [PR #3](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/3) contains this format update. Its existing intake [self-check](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37309425129) passed; issue reporting was skipped (issue_number=0). Workflow permissions and official main/catalog were not changed; merging remains separate from A2 implementation.

## Staging publication

Existing Playtest 1.3.0 ZIP stayed byte-identical: 6822 bytes, SHA-256 `5a8e14105639e0180b10cbf82923d91fe64e2e6a5a14ae6728a6851ba7575f88`, raw manifest hash `1249fe3864ef05f3c28e69bf6698e4ec5053c794051e7c513647f945d4b07d3f`. Re-running the packager also reproduced these exact hashes; its display output equals ZIP projection. Older withdrawn packages gained catalog-only metadata without asset replacement.

The new input snapshot is `d84b6b7a4cc002dc3508aa07319aa83b3903bae8`. Validated v3 catalog is 5630 bytes, SHA-256 `b3d337f4e09e2f741db182ea40b8bf8022e45e6dcb79bb9050d8fc1b7decb5c9`; catalog Release ID 403681509 and asset ID 612497872. The draft asset was read back and compared byte-for-byte, then published. Immutable published description and stable were committed last; the controlled branch advanced without force to `561a3296e63240afe20cfc85e0a012a2fed91a58`. Prior snapshots/assets remain. [Catalog Release](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/catalog-v3-d84b6b7a4cc002dc3508aa07319aa83b3903bae8).

Staging Worker build label is staging-20261005-catalog-v3. Final deployed version `84185643-8596-4f11-ae32-a5b0d3b9c20c`. Domain, source mappings, secret, limiter and disabled R2 configuration remain as configured. No production Worker or official index pointer was changed.

## Validation

Commands run from the main repository root unless indicated:

```sh
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:BuildInParallel=false -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:GameReferenceDirectory=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
# From Extensions/PluginStore/RepositoryWorker:
npm test
npm run check:staging
./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc
```

Results: store 896 assertions; host .NET 10/Mono 677 each, with actual startup/registry children 18+18+16; Worker 141 tests; bot/publisher 16 tests and remote intake CI passed. Shared cases cover single-language, missing fields, script/default/English fallback, absent/duplicate locales, markup/control/surrogates and individual/aggregate limits. Projection mismatch also rejected an actual unchanged 1.3.0 ZIP with only its catalog Chinese name forged (CatalogLocalizationMismatch, no output report). Immutable draft writes/prior-pointer preservation, corrupt ZIP hash and legacy-format rejection are covered. Full solution passed with seven existing warnings; validator/CLI retain NU1900 feed-access warnings. PowerShell was unavailable: an equivalent filesystem check verified all 25 unique required output paths, LoadFolders, new XML labels, no game/Unity reference DLLs, 24 source/provenance pairs and copied fixture equality. The PowerShell artifact script itself was not run. Final git status/diff review and git diff --check passed; pre-existing unrelated work was preserved and no generated output or game reference was committed.

Live checks use new isolated directories, explicitly disabled proxies and anonymous requests; they neither install nor execute downloaded code:

```sh
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-v3-live-cf-mono --managed-cf
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://plugins-staging.hunyuan2333.com phinix.managed /tmp/phinix-v3-live-cf-net10 --managed-cf
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://api.github.com phinix.managed /tmp/phinix-v3-live-github-mono-retry --managed-github
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.managed /tmp/phinix-v3-live-github-net10 --managed-github
```

Both CF live checks passed metadata/cache/localized name/changelog/English fallback, fresh transfer chain, four-file package inspection and cleanup. GitHub read the same new catalog but the package step encountered an anonymous API limit; diagnostics recorded RepositoryRateLimited and remaining=0. This is not a successful full direct-download check. Direct adapter simulations passed; the unchanged package previously passed direct live checks. After the quota reset, the anonymous proxy-disabled Mono retry succeeded against the same new snapshot and four-file ZIP. Its directory/log basename is /tmp/phinix-v3-live-github-mono-retry; no token, installation or DLL execution was involved. The .NET 10 direct check also passed; all four provider/runtime combinations returned the same snapshot and package digest. The initial failure remains recorded as rate-limit evidence. Use new output-directory names when repeating checks. No automated check establishes in-game UI acceptance or accessibility in every geography.

## Game handoff and next batch

Deploy the complete newly built Output/phinix-rework (host Utils, store DLL and language XML), then restart. Earlier v2 shop builds intentionally reject the new staging directory; use the new build. Installed 1.3.0 ownership/receipts/settings/save data are unchanged: no uninstall/reinstall is required for this metadata upgrade.

Check store name/summary/changelog in Chinese and English, then an unsupported language for available-English fallback. Switch GitHub/CF and confirm equal entries and the same installed package; if GitHub is rate-limited, the manual CF switch should work. Check the installed Playtest tab still translates independently. Browsing should not download ZIP/language files just to change language. Current version grouping/three-step installation UI remains unchanged until the following formal interaction batch.

Next implement A2 exact-candidate approval/persistent policy records and controlled A4 publishing before unattended A3; then version grouping, one-action install/dependency confirmation/progress/update notices, actual replacement transactions and official-plugin save prerequisites/separation. Main-thread/resource/ownership boundaries remain mandatory.
