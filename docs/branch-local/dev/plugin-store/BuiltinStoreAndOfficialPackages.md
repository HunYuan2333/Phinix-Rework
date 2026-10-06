# Bundled store and downloadable official packages

2026-10-06 latest: private business repositories and CI are complete. [Source privacy and binary distribution](LegacyPrivateSourceDistribution.md) are separate; public catalog/release work is still gated.

2026-10-06 update: the latest user instruction resumes both repository extractions. [Independent candidates](OfficialRepositoryExtraction.md) are prepared; bundled products remain through recovery/save acceptance. Earlier postponement statements below are superseded.

2026-10-06 latest scope: defer both business repositories and independent releases; retain bundled DLLs. Scoped localization and pending-return repairs are implemented; game acceptance and late-load/missing-package save gates remain. See [batch acceptance](OfficialResourcesAndTalentReturnsAcceptance.md).

2026-10-06 priorities follow [store completion](StoreCompletionOrder.md). Gateway extraction, M6b resources/save prerequisites, RedPacket, Talent and M6e production finishing all belong to current store work, before main splitting, broad DI/libraries or SQLite. Implement only required generic host capabilities and business safety fixes; full framework restructuring is not a prerequisite.

2026-10-06 [current S2 prerequisite audit](OfficialPackagePrerequisiteAudit.md): reuse host localization but adapt actual main-XML calls; Talent clears pending returns before asynchronous materialization and needs repair/evidence. Neither package is extracted; CF completion does not prove save/recovery safety.

2026-10-05 user decision: one JSON per language, packaged with DLLs; implement generic host localization before store display/publication integration. Replace development formats directly, without v2 reading/cache migration/dual-format publication. [Language files and host contract](PluginLanguageFilesAndHostContract.md) supersedes earlier compatibility/order statements below.

2026-10-05: [Publication format and implementation order](PublicationAndImplementationOrder.md) supersedes the earlier UI-first batch: freeze multilingual/changelog contracts and compatible readers, then generic UI localization/store experience; complete A2 and controlled A4 before enabling A3 unattended version monitoring.

2026-10-05 M6b clarification: generic localization accepts single-language resources, selects game-language matches and falls back to English/default/available content. Catalog names/summaries are multilingual too. Preserve official existing bilingual resources without requiring third-party packages to provide two languages. See [plan](LocalizationPlan.md).

Latest user feedback, 2026-10-05: the basic 1.2.1 install-to-uninstall game lifecycle passed. Prioritize [store experience improvements](FutureImprovements.md) next, then M6b resources/save prerequisites before separating RedPacket and TalentTrade. Neither business package is independently accepted yet; keep them bundled until then. Extended checks remain; see [acceptance](StoreFirstRelease.md).

[中文](内置商店与官方插件拆分计划.md). 2026-10-04, branch `dev`. The user confirmed that the production store ships with the main mod and RedPacket/TalentTrade become downloadable plugins. This is a planning-only change: no build-copy, runtime or business-state changes. Follow [managed implementation batches](ManagedDllImplementation.md), [design philosophy](../../../Design-Philosophy.md) and [compatibility boundaries](../../../Compatibility-Boundaries.md). The managed route supersedes historical local-mod packaging proposals.

2026-10-05: to prioritize finishing the store, M6a bundling is implemented locally alongside M4b/M4c. The new package/source is published/deployed after explicit authorization, with passing live download checks; game acceptance remains pending. RedPacket/TalentTrade distribution is unchanged in this batch. See [current delivery](StoreFirstRelease.md); this update supersedes earlier pending-bundling status below.

## Final distribution

| Feature | Distribution and management |
| --- | --- |
| Store | Independent PluginStore.Client.dll in the main mod's Common/Extensions, with its translations bundled. Default module phinix.plugin-store; preserve existing user-disabled settings. Same discovery/Register/Activate/Shutdown, disable/restart policy and settings recovery entry. No host dependency on the store project or business types |
| Chat, Trade, Inventory, LegacyAdapter | Remain bundled for now, with ordinary module management |
| RedPacket | Managed ZIP with its own Contracts/Client DLLs, manifest and declared resources. Index/gateway download into SaveData/Phinix/ManagedExtensions. Preserve builtin.legacy-redpacket, assembly/codec/settings/storage identities |
| TalentTrade | Same managed route. Preserve builtin.legacy-talent-trade, assembly/type/Harmony identities, Scribe fields and settings |
| Full RimWorld mods | Workshop index/links; user subscription and RimWorld management |

Bundling changes distribution/default entry, not registration privileges. Downloaded business packages contain no About, go into neither the main mod nor Mods, and include no duplicate framework, Trade/Inventory, game/Unity/Harmony assemblies already supplied by the host. Main-mod updates deliver store updates; no store self-replacement in the first release. Detect old preview/main-store identity collisions explicitly rather than choosing an enumeration winner.

## Order and acceptance

M2b code/regression is complete; game smoke awaits feedback. Continue M3 package management → M4 managed installation → M5 real DLL Playtest lifecycle. Then split M6 below. Resource/persistence audit can start earlier, but remove bundled business packages only after independently downloadable packages pass acceptance.

| Batch | Delivery | Acceptance |
| --- | --- | --- |
| M6a Bundled store | Main-pack copy and bilingual resources, artifact checks/CI; preview becomes an explicit development option. Preserve identity/settings. Production list/details/install/status UI, with test paths under advanced diagnostics and responsive layout comparable to Trade | Clean main mod shows the store; disable/restart/settings recovery; explicit preview conflicts; installed plugins work with store disabled; no host business dependency |
| M6b Resources/migration prerequisites | Audit resources, persistence, actual CLR references and native type discovery. Implement needed generic managed resource/localization access, language changes/collisions/cleanup. Verify late-loaded TalentTrade component discovery/Scribe restoration; use a generic game lifecycle/persistence contract if necessary | Both languages, no missing keys/resources; inactive code has no patches/timer business actions; new/old games create/save the component exactly once; no unconditional global type-cache clearing; preserve original save and pending data |
| M6c RedPacket first | Change Client/Contracts output copying; package owned files only; declare host, Trade/Inventory modules and precise CLR compatibility. Publish a new immutable version/hash, then index | Install/restart/tab; explain disabled prerequisites; real relay confirmation before inventory transfer; disconnect/unknown result/pending send/old template recovery; removal preserves send records/raw inventory payload |
| M6d TalentTrade second | Change both projects' copying, publish separate package; retain actual dependencies without inventing a Trade dependency. Verify component/save and Harmony/timer lifecycles | Old listing IDs/pawn data/saveToken/pending returns; save switching/restart/disable/removal/reinstallation neither duplicates delivery nor loses pawns; verified preservation when an old save is opened and saved without the plugin |
| M6e Production migration | After both packages pass, clean main output excludes four business DLLs and exclusive resources. Diagnose old leftovers/identity conflicts; provide offline update steps and migration records. Update index/templates/bilingual docs/platform acceptance | Clean/upgrade paths, Windows/Mono, networks/large packages, independent updates; non-Steam Workshop behavior; no shared/game-reference DLLs in packages |

Each batch reviews diff, relevant harnesses, artifacts and game evidence. Keep current distribution while failures are repaired. M6b resource contracts and missing-plugin save preservation are not frozen or implemented; a requirement is not evidence of support.

## Source audit and migration constraints

- The store project currently copies only into Output/phinix-plugin-store-preview. The current solution command still generates main plus separate preview; M6a changes packaging later.
- All four RedPacket/TalentTrade Client/Contracts projects still copy into the main mod, and artifact checks require those files. Stopping copies does not remove old output: validate clean distribution. Remove only proven old official-owned files, preserve unknown/changed files, and let Steam update Workshop content.
- RedPacket references Trade.Client protocol implementation. Initially keep the compile reference, accurate compatibility and already-loaded host object requirement without bundling its DLL; a Contracts refactor needs separate validation and must not change wire/acknowledgement semantics.
- Both translation sets live under Client/Languages. Managed Resources are not automatically visible to Translate(); a real generic capability/minimal caller adaptation is needed. Resource access is read-only and bound to verified ownership; abstraction 1.5.0 ownership fields are not a resource API.
- TalentTradeGameComponent uses GenTypes.AllSubclassesNonAbstract automatic creation and ExposeData persistence. Late byte-loading can interact with type caches/initialization order; game verification is required. A working tab alone does not prove save safety.
- Existing GetStoragePath uses framework-extensions/client/<extensionId>, separate from SaveData/ExtensionData. Preserve current paths/identities/content initially; any later relocation requires an owned, retryable migration preserving original data.
- Code/module dependencies cannot reveal arbitrary business operations in flight. Establish pending-operation/missing-plugin handling. If dangerous disable/removal needs blocking, add a generic participant contract for plugin-reported blockers, never host checks for RedPacket/pawn-specific state.
- Refuse a parallel managed copy while the old main mod still contains matching DLLs. Exit, back up data/saves, replace with a clean main mod, install required packages, restart and verify. Backup instructions do not replace a technical preservation design for resaving TalentTrade saves without its plugin.

## First production UI scope

2026-10-04 user decision: use bounded text descriptions, feature labels, bundled generic icons and clear states/actions; no remote image index in the first release. Descriptions travel in the versioned catalog with existing validation/cache, without additional README/image fetches. M4 adds metadata and M6a finishes layout. Optional detail screenshots require a later needs/cost review.

## Current build command

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
```

Outputs: Output/phinix-rework and Output/phinix-plugin-store-preview. This planning batch checks links/tables/fences/whitespace/git status only; no build or game run, split/release, data migration or deletion occurred.
