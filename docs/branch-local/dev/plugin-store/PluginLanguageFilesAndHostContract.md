# Plugin language files and host application contract

2026-10-05 catalog v3 extraction, store fallback/display and staging publication delivered; [validation](CatalogV3Implementation.md). This supersedes the pending catalog/remote-publication status below.

[中文](插件语言文件与宿主应用契约.md). 2026-10-05, `dev`. This fixes the author layout/host route and supersedes the optional old-format compatibility plan. The user explicitly permits breaking development changes: replace v2 directly, without legacy reading/cache migration/dual-format publication. The first host batch is implemented; see [implementation and validation](LocalizationImplementationAndValidation.md). Catalog v3 and remote publication remain in the next batch.

## Original host gap (closed by the first batch)

Previously ClientExtensionAbstractions had no generic localization API. ManagedExtensionRuntime verifies declared Resources but does not register language dictionaries. Previously Playtest embedded an English/Chinese dictionary; 1.3.0 now uses package JSON. Bundled native Languages support does not mean RimWorld scans managed packages installed under SaveData.

Workshop entries provide store display metadata/links only. RimWorld and the author manage Workshop Mod language files/loading/UI; Phinix does not import those translations.

## Author layout: one JSON per language

```text
my-plugin/
  manifest.json
  Assemblies/My.Plugin.dll
  Resources/Localization/zh-CN.json
  Resources/Localization/en-US.json
  Resources/Localization/ja-JP.json
```

Download the complete ZIP and retain this layout when installed. Any one language is valid; multiple languages are optional. Use UTF-8 JSON only and canonical locale filenames, normalized by the packager. Each file has schemaVersion=1, locale, display (name/summary/optional changelog) and strings (UI key/text). See the [Chinese companion](插件语言文件与宿主应用契约.md) for the full example.

Individual display fields/UI keys may be missing and fall back; the complete set must supply name and summary. Changelog may be absent everywhere; show an unavailable message. Empty strings maps are valid for metadata-only packages. Reject duplicate fields/keys/normalized locales. Locale subset: language[-Script][-REGION], with 2–3-letter language, 4-letter script and 2-letter/3-digit region; do not require game folder names.

The new manifest explicitly lists language files under localization with optional defaultLocale; each file also has a Resources length/digest declaration. Declared defaults must exist; a single file needs no default. The packager generates declarations. Load owned declared files only, without arbitrary directory scanning.

Limits: 16 locales/package, 128 KiB/file, 1 MiB combined language files; display name 160, summary 1024, changelog 8192 UTF-16 units, 32768 combined display units; 2048 distinct UI keys and 8192 units/value. Keep existing catalog/ZIP limits. Allow newlines/tabs in summary/changelog/UI, reject other controls, whitespace-only text, malformed surrogates and scripts; render plain text. Use numbered {0} placeholders and {{ / }} escapes; supplied translations for each key must have the same placeholder set, while missing keys may fall back.

Catalog v3 uses localization.translations[locale] display fields and optional defaultLocale. The publisher extracts display from language files; UI strings stay in the ZIP. Browsing does not fetch complete packages/separate language files. Do not map legacy top-level name/summary. Stable IDs retain discovery/dependency/ownership meaning. Managed manifest schemaVersion remains 1 with a strictly parsed optional localization field; omission means no language resources for this service, without legacy mapping. Language file schemaVersion is 1. The declaration is synchronized with the trusted validator; catalog v3 remains pending.

## Host loading and plugin use

1. Validate ZIP/manifest/declared resource lengths/digests before installation, without executing DLLs.
2. Before activation, load localization through verified module/package ownership, validate data/bounds/keys/placeholders, and build isolated read-only dictionaries. Diagnose failures with package/file/stage/stable code without breaking unrelated plugins.
3. Provide IClientLocalizationService in ClientExtensionAbstractions before Activate. ForModule(this) returns an IClientLocalizer and binds through registered module/assembly ownership, without arbitrary caller-provided paths.
4. The plugin calls localizer.Text("silver.give") or localizer.Format("counter.value", count) when drawing; providers retain the localizer and TabLabel resolves dynamically. Hardcoded DLL text is not automatically translated; authors must use keys.
5. Map game language on the main thread; file parsing can run in the background. Text/Format use immutable published locale snapshots, without background Unity/Verse translation calls.
6. Exact locale → suitable same-language variant/base → available English → author default → actual available content in stable order, per key. Specify Chinese script matching. Single-language packages always display supplied content. Missing keys use caller fallback/visible key and throttled diagnostics, never blanks.
7. Publish LanguageChanged on the main thread so plugins invalidate layout/measurement caches. Shutdown releases localizers/subscriptions; package removal releases dictionaries without deleting settings/saves. Language changes need no reinstall/restart.

Bundled, official and third-party plugins share the same service and ownership resolution. It works with the store disabled, without global game translation injection or cross-package key overrides. Isolation provides correct lookup, not a security sandbox for CLR plugins.

## Revised batches

1. Shared locale core and host service first: parser, matching/fallback, parameters, ownership binding/events. Migrate bundled/managed Playtest and test single/multiple languages, identical-key isolation, missing text, switching, shutdown and store-disabled behavior. Pure/runtime checks precede game acceptance.
2. Packager and catalog v3: package author files/declarations, extract display metadata, share the locale core with store rendering, update client/CF/trusted bot/examples. Only the new format; no v2 compatibility. This can proceed independently once format/core are fixed.
3. Store interaction and publishing bot: grouped versions, one install action, dependency confirmation, progress/changelog/startup notifications; A2 → controlled A4 → unattended A3. Actual upgrades retain separate transaction/data acceptance.
4. Remaining save prerequisites before official RedPacket/TalentTrade separation. Workshop-internal localization remains outside this service.

The first batch delivers the shared core, host service, install/startup validation, Playtest 1.3.0, packaging and regressions. Game acceptance, catalog v3, CF/bot and store presentation remain pending. No new remote assets were published.
