2026-10-06: Progress, startup notices, confirmed owned upgrades and A3 automatic source updates are implemented and verified. [Current evidence and required game checkpoint](StoreFinishingAcceptance.md) supersedes pending statements below. Domain migration remains after game acceptance.

2026-10-06: Deferred repository prose cleanup by another user-selected model and a harness-neutral AI author workflow without Git/gh are planned, not implemented. [Finishing order](FinishingAndAiAuthorWorkflow.md).

# Future plugin-store experience improvements

2026-10-05 local player cleanup/grouping/one-click implementation is delivered; progress and replacement remain pending. [Pre-launch gate, UI audit and game checkpoints](PreLaunchCleanup.md) supersedes the historical source/preview notes below.

2026-10-05 access buttons/default and provider separation are implemented locally; [validation/game handoff](RepositoryAccessImplementation.md). This supersedes the pending-access note below. Catalog v3/store experience remains pending.


2026-10-05 access requirement: default GitHub and a manual CF acceleration switch using one repository identity/protocol. Current client access is CF-only. Implement profile/provider separation and the direct adapter first; switching must preserve installed identity and invalidate in-flight plans safely. See [access audit/acceptance](RepositoryAccessAdapters.md).


2026-10-05 user decision: one JSON per language, packaged with DLLs; implement generic host localization before store display/publication integration. Replace development formats directly, without v2 reading/cache migration/dual-format publication. [Language files and host contract](PluginLanguageFilesAndHostContract.md) supersedes earlier compatibility/order statements below.

2026-10-05: [Publication format and implementation order](PublicationAndImplementationOrder.md) supersedes the earlier UI-first batch: freeze multilingual/changelog contracts and compatible readers, then generic UI localization/store experience; complete A2 and controlled A4 before enabling A3 unattended version monitoring.

2026-10-05 localization clarification: multilingual architecture accepts one language, with no mandatory English/Chinese. Select game-language matches for display name/summary/changelog/UI and fall back to English, author default or available content. See [localization plan](LocalizationPlan.md); unimplemented and shared with M6b/author tooling.

[中文](后续体验改进.md). 2026-10-05, `dev`. User-confirmed follow-up requirements from the 1.2.1 game test; **not implemented**. The user reports [the basic install-to-uninstall lifecycle passed](StoreFirstRelease.md); improve the ordinary player experience together next. This document is the shared backlog for UI, versions and updates.

## Group versions into one entry

- Show one list entry per plugin with a version selector in details. Default to the latest valid published version, sorted using version rules.
- Group by source and package ID; preserve each version's fixed asset, digest and compatibility data. Grouping must not mix identities across sources.
- Explain an incompatible latest version and allow selecting a compatible older one; do not silently choose an older version. Withdrawn versions may remain labelled in history but cannot be newly installed.
- Distinguish installed, selected and latest versions and pending-restart state; separate versions must not appear to be separate plugins.

## Check for updates at startup; notify only

- Check installed plugins in the background after startup and indicate newer versions in the store/manager without blocking game startup.
- Fetch catalog metadata only, with cache reuse, request coalescing and throttling. Failure must not affect current plugin loading; never poll every frame.
- Do not automatically download, overwrite or enable an update. Players review versions/changelog and explicitly start an update.
- The current installer does not support in-place upgrades. Before exposing an update action, separately implement and validate version replacement, changed dependencies, ownership checks, restart behavior and interruption recovery. Detection is not completed upgrade support; reverting code does not imply reverting business data.

## Show developer-provided changelog

- Display the developer's release notes for the selected version in details and update prompts; show a clear unavailable message when omitted.
- Bind changelog to validated catalog/publication data, version and fixed snapshot. Update the format, publisher tools, author guides and client together; do not bypass strict format validation.
- Initially use bounded, scrollable plain text. No remote images or extra external-site requests when viewing details.

## One install action for ordinary players

- Replace the regular sequence of manual dependency planning, download-only validation and installation with one Install button.
- Internally resolve dependencies, check compatibility/local conflicts, download, validate and commit. Players need not generate a plan or download an uninstalled package first.
- Proceed directly when no additional dependencies need installation. Otherwise show the extra dependency names/versions, reasons and total download size; confirmation installs the batch, cancellation stops it. Explain missing external Mods, conflicts and incompatibility; the Workshop route remains links for player subscription.
- Planning and validation remain mandatory internal steps, including fresh catalog checks before commit, complete file validation, transaction consistency and audit. Simplify the interaction, not these checks.
- Download-only validation and manual plan inspection may move to developer/diagnostic controls rather than being regular installation prerequisites.

## Progress and unified UI improvements

- Show stages (resolution, download, validation, commit, completion/restart), current package, received/total bytes and overall progress. Long operations need visible feedback.
- Make cancel/retry, error details and restart notices clear. Cancellation after the commit decision retains existing transaction semantics.
- Improve list/details layout, version choice, summary/changelog, button state and error wording together. Throttle progress updates; no per-frame catalog reads/logging or remote images.

## Remove old preview-Mod migration controls before formal release

- The formal UI may remove rollback/migration/cleanup controls for the old 1.1.0 local-Mod route and its receipts, including the old Mod installation records/cleanup button. Ordinary players do not need development-preview leftovers.
- Review related code, docs and artifacts during release cleanup. Keep necessary developer diagnostics separately; do not automatically delete users' old Mods or unknown directories.
- This does not remove normal managed-plugin uninstall, retained settings/saves, interruption recovery or duplicate-assembly checks, nor the historical-save compatibility required for official package separation.

## Order and acceptance

1. The user reports the basic 1.2.1 install-to-uninstall flow passed; do not request the same basic test again. Retained counts after reinstallation, loading with the store disabled and other unconfirmed scenarios retain separate acceptance checks.
2. Complete R1/R2 first: freeze localized name/summary/changelog, fallback/resource contracts; align compatible client/cache readers, publisher, Worker and trusted bot validation. Trial the new format through an isolated source while retaining v2.
3. Move M6b generic resources/localization forward (R3); verify single/multiple-language UI, language changes and lifecycle in Playtest. Save migration retains its pre-separation acceptance.
4. Implement R4 grouped versions, one install action, dependency confirmation, progress/layout, changelog and startup notifications, then test the new interactions in game. Actual upgrades require separately validated replacement/recovery and explicit player action.
5. After format freeze, bot A2 and controlled A4 can proceed independently; enable A3 unattended version monitoring after acceptance. Remove preview migration UI before formal release; retain RedPacket/TalentTrade acceptance prerequisites.

See [the revised order](PublicationAndImplementationOrder.md) for dependencies and transition boundaries.

Acceptance should cover one entry per package, latest default/manual old version, withdrawn/incompatible versions; direct installation without extra dependencies and confirmation/cancellation with them; disconnect/cancel/retry/progress; notifications without automatic downloads and offline startup; version-specific/missing changelog; no old preview-cleanup button while regular removal/recovery still work. This batch records requirements only and changes no code, remote catalog or installation state.
