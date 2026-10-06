# Publication format and implementation order

2026-10-05 priority changed: pre-launch cleanup comes first. Players see only the official index with GitHub/CF access; keep latest Playtest separately and remove it from official listings. Hide/remove developer and legacy preview controls, then complete one-click/version UI and progress. [Release gate and manual checkpoints](PreLaunchCleanup.md).

2026-10-05 single-label live acceptance passed: Issue #8 -> automatic PR #9 -> publication 37325481455 -> matching GitHub/.NET 10 and CF/Mono downloads. Success-close/error-label/author-guidance follow-up PR #10 is deployed; 52 regressions and real malformed Issue #11 feedback passed. Real user-approved Issue #12 passed the full new flow: automatic PR #13/publication, error label removal and bot closure. All acceptance complete; next remains official source/UI integration and A3. [Evidence](LabelAdmissionImplementation.md).

2026-10-05 label admission implementation: one maintainer `plugin-approved` event binds the exact Issue body, label event and actor IDs; trusted checks automatically create/merge an evidence PR, then `workflow_run` rechecks and publishes. 47 local regressions pass. PR #7 is merged; deployment `e1d00e51c937c2ef069d138f5ef7ac494ee58e64`, remote self-check and real publication preflight passed; real human-label acceptance is pending and tracked in [label implementation](LabelAdmissionImplementation.md). A3 and game UI/default-source integration remain separate. Older status notes below are historical.

2026-10-05 controlled publication accepted: human admission/PR #6 merge, actual fixed catalog publication and read-only rerun passed; official GitHub/CF downloads passed on .NET 10 and Mono. Game defaults remain the old test source; next is the proposed label-based admission UX, then source/UI integration and A3. [Evidence and exact validation](ControlledPublicationAcceptance.md).


2026-10-05 proposed next admission UX: the user prefers a maintainer-applied Issue label as the approval action. Keep the current PR #6 manual merge and controlled-publication acceptance first. After that acceptance, plan `plugin-approved` on the submission Issue -> trusted labeled-event workflow -> verify admin/maintainer actor and bind the exact event-body candidate fingerprint -> fresh origin/ZIP checks -> permanent review/policy records and bot-managed metadata PR -> serialized publication. PRs remain audit records rather than an additional routine manual step. Edited candidates or label removal before publication invalidate pending approval; reject arbitrary labels, comments, unauthorized actors and stale label events. Published accepted versions remain immutable. This is planned, not active: the current publisher still requires a human-merged admission PR. Update approval-proof validation, workflow permissions, documentation and regression tests together before enabling label-based automation; keep A3 ordinary-version monitoring a separate delivery.


2026-10-05 A2/controlled A4 code is now merged and configured; 34 regressions and remote real intake passed. First positive human approval/publication remains pending. [Evidence and exact manual command](AdmissionControlledPublicationImplementation.md).


2026-10-05 next batch: exact-version A2 records/metadata PR and controlled A4 workflows are implemented; first live human approval/publication acceptance is pending. No A3 scheduling or game UI changes. See [operations and manual acceptance](../../../../Extensions/PluginStore/RepositoryAutomation/ControlledPublication.md).


2026-10-05 R2 catalog v3 delivered: localized display/client/PCS4, shared ZIP projection validation, packager/draft generator, deployed staging Worker and published v3 snapshot. [Implementation/game handoff](CatalogV3Implementation.md). Bot format PR #3 has passed remote intake self-check; A2 admission/controlled A4 and formal interaction remain next.

2026-10-05 access batch implemented: both adapters, shared repository ownership/cache, GitHub default and CF quick switch. [Validation/game handoff](RepositoryAccessImplementation.md). Next implement R2 catalog v3/localized store display and publishing once for both paths.


2026-10-05 access decision: GitHub is the default target, with an explicit CF acceleration switch. Current clients only speak CF; profile/transport identity and a direct GitHub adapter must be implemented before defaults/buttons, and both adapters share the next catalog v3 readers. See [access audit and revised order](RepositoryAccessAdapters.md). Playtest 1.3.0 is now publicly released through the current v2 test chain; v3 is still pending. This supersedes the earlier mandatory-gateway assumption below.


2026-10-05 implementation status: shared language core, host service, install/startup validation, Playtest 1.3.0, language packaging and trusted validation are delivered locally; host/store are built. Game language acceptance and catalog v3/client/CF/bot presentation/publication remain in the next batch. See [implementation and validation](LocalizationImplementationAndValidation.md); this status supersedes earlier unimplemented-service notes below.

2026-10-05 user decision: one JSON per language, packaged with DLLs; implement generic host localization before store display/publication integration. Replace development formats directly, without v2 reading/cache migration/dual-format publication. [Language files and host contract](PluginLanguageFilesAndHostContract.md) supersedes earlier compatibility/order statements below.

[中文](发布格式与实施顺序调整.md). 2026-10-05, `dev`. This supersedes the previous UI-first next batch. The user has accepted the basic game install-to-uninstall flow. New formats, services and bot stages below remain planned.

## Required changes

Keep author GitHub Release assets → validated catalog snapshots → CF fixed-source gateway → client. Localization changes display, not package/module IDs, assembly identity, dependency identifiers, artifact digests or installation ownership.

| Layer | Change and reason |
| --- | --- |
| Catalog | Add localized display names, summaries and per-version changelog, with defaults, per-field fallback and bounds. Strict v2 rejects extra fields, requiring an explicit format upgrade. |
| Client | Update catalog models, stable/published dispatch, network/cache readers and continuity checks, then list/search/details/language-sensitive caches. Accept v3 only, without legacy v2 reading or migration. |
| Publisher | Align author input, catalog generation and pre-publication validation. Update catalogSchemaVersion/digests/sizes together; publish stable last. |
| CF Worker | Accept the explicit new schema and select managed assets correctly. selectedPackage currently recognizes only schemaVersion=2 as managed; widening the whitelist alone misroutes assets. Retain fixed routes and identity validation. |
| GitHub bot | Refresh trusted validator source snapshots/provenance, submission form/examples, locale checks and publisher validation. Live A1 v2 checks do not imply support for the new format. |
| Client abstractions/host | Provide generic package-scoped resources/localization for bundled, official and third-party plugins. Disabling the store must not disable translations in installed plugins. |

Propose catalog v3, freezing its number and fields in R1. Multilingual display alone need not change the outer stable/published v1 envelopes or `/v1/sources/...` routes; update their declared/accepted catalogSchemaVersion. Prefer existing manifest v1 Resources declarations for language files, defining interpretation and host access separately. Consider a manifest upgrade only if existing declarations cannot express necessary information.

## R1: Freeze display and language contracts

- Author one JSON per language with display and UI strings. Extract display into localization.translations[locale] with name/summary/changelog and optional defaultLocale. Keep manifest canonical information distinct; do not map legacy top-level catalog name/summary.
- A single en-US, zh-CN or other supported locale is valid. English/Chinese are not mandatory; a declared default must exist.
- Exact locale → suitable same-language variant/base → available English → author default → available content in stable order. Resolve per field/key, reject duplicates after normalization, and specify region/script rules.
- Define UI resource format, keys/placeholders/bounds, game-language mapping and lifecycle together. Translations never become persistence, protocol, audit or identity fields.
- Deliver bilingual format documentation, single/multiple/invalid-language fixtures and acceptance criteria. Rebuild development inputs under the new structure without compatibility transitions.

## R2: Connect compatible readers and publishing validation

Implement the generic host locale core/application in the next section first, then align client parsing/cache, catalog generation/validation, CF dispatch and trusted bot validation. Accept v3 only; treat old browsing caches as invalid without migration. Preserve asset/digest/resource ownership and transaction checks.

Replace development readers/writers/fixtures/test sources together. Share language fixtures across host/store/Worker/bot and validate single/multiple-language fallback, tampering/bounds, offline new-format caches and publication failure retaining a valid pointer.

Existing immutable ZIPs may gain localized catalog metadata through a new snapshot without rewriting their assets/manifests/digests. Adding UI language files to a package requires a new version and ZIP.

## R3: Generic UI localization and game acceptance

Implement this section before R2 integration. Move the resource/localization portion of M6b forward; TalentTrade save migration remains a prerequisite to separation. Contracts belong in ClientExtensionAbstractions; the host provides generic services independent of the store. Validate bounded declared-resource reads, package isolation, per-key fallback, main-thread game-language access, language-change notifications and shutdown cleanup.

Add an optional service contract rather than members to interfaces existing third-party modules must implement. Packages using it declare the corresponding minimum host version. Existing packages keep their discovery/registration path; do not weaken assembly-reference checks to claim compatibility.

Migrate Playtest to this interface and test single/multiple languages for tabs, buttons, messages, language changes, restart and display with the store disabled. Reuse the accepted basic installation route; focus game testing on the new resource/localization behavior.

## R4: Unified store interaction and UI

Use the stable model/interface for grouped versions/selection, one Install action, dependency confirmation, byte/stage progress, summary/changelog, errors/restart state and layout. Add background startup notifications using validated metadata only, without automatic downloads or upgrades. Game acceptance covers version selection, installation with/without extra dependencies, confirmation/cancel, progress, disconnect/retry, language changes and offline startup.

Notifications and actual version replacement are separate deliveries. The current installer cannot upgrade in place. Before exposing Update, implement and validate replacement transactions, dependency changes, ownership and interruption recovery; code rollback does not prove data compatibility.

## R5: Admission and controlled publication before unattended monitoring

Keep live A1. Once R1/R2 freeze the format, bot work can proceed independently of R3/R4:

1. A2: approve exact candidate fingerprints/source policy, create metadata PRs/permanent review records, configure minimal write permissions and repository protection.
2. Controlled A4: manually invoke the trusted serialized publisher, validate approved inputs, generate the full catalog, upload/verify fixed Releases and update stable last. Validate concurrency/failure recovery and real client downloads through an isolated source. Unattended A3 monitoring is not a prerequisite.
3. A3: monitor ordinary versions within approved policy using the same validator/publisher; identity, same-version bytes or policy changes need review. Enable unattended publication only after controlled acceptance.

Retain A numbers as feature areas; implementation becomes **A2 → controlled A4 → A3 automation**. First admission/policy changes retain human judgment; ordinary versions satisfying approved rules need no per-version approval.

## Formal delivery afterward

Complete actual upgrade transactions and remaining resource/save prerequisites, then separate RedPacket/TalentTrade with retained uninstall data and missing-package save acceptance. Remove preview-Mod migration UI and complete platform/failure/multi-package/network/quota checks. Basic Playtest success does not replace these checks.

Start with language-file contracts/shared locale core/host service, then the new catalog/publication chain. This update changes local plans only, with no runtime, public-asset, remote-catalog or CF deployment changes.
