# Multilingual metadata and plugin UI localization plan

2026-10-05 catalog v3/store display/publication-format batch delivered. [Implementation](CatalogV3Implementation.md). This supersedes the older pending-v3 status below; A2/A4 and formal interactions are still pending.

2026-10-05 implementation status: shared language core, host service, install/startup validation, Playtest 1.3.0, language packaging and trusted validation are delivered locally; host/store are built. Game language acceptance and catalog v3/client/CF/bot presentation/publication remain in the next batch. See [implementation and validation](LocalizationImplementationAndValidation.md); this status supersedes earlier unimplemented-service notes below.

2026-10-05 user decision: one JSON per language, packaged with DLLs; implement generic host localization before store display/publication integration. Replace development formats directly, without v2 reading/cache migration/dual-format publication. [Language files and host contract](PluginLanguageFilesAndHostContract.md) supersedes earlier compatibility/order statements below.

2026-10-05: [Publication format and implementation order](PublicationAndImplementationOrder.md) supersedes the earlier UI-first batch: freeze multilingual/changelog contracts and compatible readers, then generic UI localization/store experience; complete A2 and controlled A4 before enabling A3 unattended version monitoring.

[中文](多语言元数据与插件UI本地化计划.md). 2026-10-05, `dev`. Require **multilingual architecture with single-language author packages allowed**, not mandatory English/Chinese. These contracts remain unimplemented.

## Current state

Store controls have bundled English/Chinese translations. Catalog v2 `name`/`summary` are single strings; the canonical name is tied to the manifest. Playtest embeds dictionaries, not a generic service. Declared Resources bytes are verified/installed but not automatically native Languages/Translate registrations. M6b planned resources/localization; multilingual metadata and fallback needed this explicit follow-up.

## One language is valid

Only `en-US`, only `zh-CN`, or another supported language is valid. Missing English/Chinese cannot reject admission/loading. Multiple languages are optional; no automatic translation calls or downloads. A single language displays as supplied even in a differently configured game. Normalize identifiers, including `en-us`/`zh-cn`, and map game identities in the host rather than requiring game folder names. An optional default must exist in the supplied set; infer it for one language and use deterministic ordering otherwise.

## Selection and missing translations

For display name, summary, changelog and UI strings: exact game locale → suitable same-language variant/base with defined region/script rules → supplied English (`en` or a suitable variant) → declared default → any supplied language in stable order.

Only `zh-CN` therefore displays Chinese even in an English game. With Chinese/English, select the matching language; unsupported game languages use available English, otherwise author default/available content. Fallback is per field/key. Missing one button does not discard other matching text. A key absent everywhere uses caller fallback or a visible key with throttled diagnostics, never a blank. Missing the current game language is normal fallback, not an error.

## Catalog and UI contracts

Keep package/module IDs, CLR names, canonical manifest name, versions and fixed asset identities unlocalized. Add localized display names/summaries and the same rules for changelog. Existing text becomes default content during migration without immediate author changes. Text stays within validated bounded snapshots/cache; escape display and fetch no extra README/images/language files. Refresh list/details/search/layout caches on language changes. Strict v2 rejects new fields: freeze explicit format version/migration and update client, publisher, Worker and trusted bot together first.

Declare package-owned language data under verified resources; one file is valid. Expose generic scoped read-only resources/localization through client abstractions for bundled/third-party packages alike, independent of store activation. Tabs/buttons/settings/dialogs/tooltips/notifications/errors use stable keys, never translated persistence/protocol/audit identifiers. Centralize selection, per-key fallback and placeholders. Refresh on language changes rather than fixing text at Register. Game language/translation/UI operations respect the main thread. Release registrations/caches on shutdown; keys cannot collide across packages or override host content. Retain settings/data on uninstall. Language files are bounded data, not scripts, global translator injection or native Mod scanning; freeze format/service signatures during implementation.

## Bot, author tooling and acceptance

In A2–A4/format upgrades, validate locale/default IDs, duplicates, bounds, owned resource hashes, placeholders and declared UI-key coverage. **Never require two files or mandatory English/Chinese.** Single valid language passes; absent game languages fall back. Static checks cannot prove every Draw string uses localization, so guides/templates/manual/game evidence supplement them. A1 currently accepts v2 and does not check multilingual semantics. Preserve official RedPacket/TalentTrade's existing two languages in migration tests without making that a third-party minimum rule.

Follow [the revised order](PublicationAndImplementationOrder.md): freeze format/resource contracts (R1), compatible readers/publication validation (R2), early M6b generic localization with Playtest acceptance (R3), then store UI (R4). After format freeze, implement A2 and controlled A4 before enabling A3 unattended publication. Update bot validation before accepting new-format submissions. Test single English/Chinese/other languages, exact/variant matches, no-English fallback, missing keys, long text/placeholders, language changes, invalid defaults/duplicates/tampered resources, shutdown cleanup and localization with the store disabled. No format/runtime/public-asset changes in this planning turn.
