# Managed store catalog v3

[中文](README.zh-CN.md). 2026-10-05. The local examples bind their exact JSON hashes/lengths; their illustrative DLL/ZIP identities do not represent a downloadable package. [Implementation and live validation](../CatalogV3Implementation.md).

The outer stable/published schema remains 1 and requires `catalogSchemaVersion: 3`. The fixed `/v1/sources/<source>/snapshots/<snapshot>/packages/<id>/<version>/<artifactHash>/package` route stays unchanged. Managed readers accept v3 only, without v2 mapping or cache migration. The separate preview-Mod v1 route is not reinterpreted as a managed route. Root fields are exactly schemaVersion, sourceId, snapshotId, packages; limits remain 2 MiB, 1024 entries and 32 versions/package. Unknown/duplicate fields, wrong source/snapshot, malformed JSON and conflicting identities fail.

Every entry requires id, author, license, tags, state, channel, management. Tags are at most eight unique bounded lowercase tokens. State is active/withdrawn/unmaintained. No image/README URLs or extra browsing fetches are allowed.

- Managed GitHub entry: channel github-release, management phinix-dll, manifest, artifact, **localization**. Top-level name/summary and Workshop fields are forbidden. Canonical name remains inside the strict [manifest v1](../managed-extension-protocol-v1/README.md); the package ID must equal entry id. Display text does not participate in CLR/module/package identity. Fixed owner/repository/commit/tag/release/asset IDs, manifest/ZIP hashes and sizes retain the original validation rules.
- Workshop entry: channel steam-workshop, management rimworld-mod, name, summary, rimWorldPackageId, workshopId, rimWorldVersions. Manifest/artifact/localization are forbidden. Phinix indexes a link; RimWorld and the Mod author own native localization and content.

## Display projection

```json
{
  "localization": {
    "defaultLocale": "zh-CN",
    "translations": {
      "en-US": {"name": "Example", "summary": "A small plugin.", "changelog": "Initial release."},
      "zh-CN": {"name": "示例", "summary": "一个小插件。"}
    }
  }
}
```

Use the same language[-Script][-REGION] subset and normalization as [plugin language files](../PluginLanguageFilesAndHostContract.md). One language is valid, with no mandatory English/Chinese. At most 16 locales; reject duplicates after normalization. An optional default must exist. Fields may be absent per language, but the complete set must provide name and summary. Changelog may be absent everywhere. Empty display maps are allowed for UI-only language files.

Name is at most 160 UTF-16 units, summary 1024, changelog 8192; aggregate display limit 32768. Reject empty/whitespace-bordered text, malformed surrogates, controls and markup. Summary/changelog permit line breaks/tabs. No UI strings are copied to catalog. Exact locale → suitable same-language/script → available English → author default → deterministic available locale, independently per field. Store search/list/details rebuild on game-language changes without network requests.

Publisher extracts each language file's display map from verified package Resources and preserves manifest defaultLocale. If a package declares localization, trusted ZIP validation requires the complete catalog projection to exactly match those language files, including missing fields/default/locale set. Tampered catalog text cannot pass payload verification merely because the ZIP hash is valid. A package without language declarations may supply catalog-only metadata; it gains no host UI translation support. This permits new metadata snapshots for immutable packages without rewriting their ZIPs.

## Payload and publication boundaries

The ZIP contains exactly manifest.json, declared Assemblies DLLs and Resources files, plus optional empty ancestor directories. No native Mod shell, undeclared content, links, traversal/device paths or case/path conflicts. Limits remain 128 MiB compressed, 64 MiB/file, 256 MiB expanded, 4096 entries and expansion ratio 200 with the existing 1 MiB allowance. Validate raw manifest/ZIP hashes, declarations, resource digests and static PE/CLI module/reference facts without executing candidate code.

GitHub and CF deliver identical stable/published/catalog/package bytes through a shared logical repository identity. New PCS4 browsing caches invalidate older formats without touching installed receipts, settings or saves. Metadata-only publication keeps installed package ownership/version/digests unchanged. Installation still checks the live chain before/after transfer and uses host-owned transactions.

`RepositoryAutomation/scripts/catalog.py project` extracts and validates package language display; `build` validates a complete draft before creating an immutable local output. Neither approves candidates, writes remote assets or advances stable. The metadata-envelope draft generator also accepts v3. Remote publication must upload/verify an immutable catalog Release, write its immutable published description, and move stable last with a non-forced branch update. Failed draft/validation does not touch prior stable. Admission/policy approval and controlled automated publication remain the following bot batch.
