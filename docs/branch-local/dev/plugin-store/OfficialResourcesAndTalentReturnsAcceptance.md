# Official resources and Talent return acceptance

2026-10-06 update: bundled localization/basic Talent feedback and the 800-silver increment were accepted; extraction resumed. [New independently compiled DLLs](OfficialRepositoryExtraction.md) also pass the same local harnesses. Missing-package resave and genuine managed-loading/restart recovery remain unaccepted; the earlier repository deferral below is superseded.

[中文](官方资源与人才返还切片验收.md) · 2026-10-06 · Local implementation/automation passed; human game checks pending.

The user defers both business repositories and independent releases. RedPacket/Talent remain bundled. No new public repository/release/index or Worker deployment occurred. Host APIs, wire formats and existing assembly/module/codec/settings/storage identities are unchanged. No database or business-data migration occurs.

Both clients reuse normal module-scoped host localization. Owner language JSON, companion hashes and real UI/message calls move together; external game/framework/Inventory keys remain Verse translations. Language events/rebinding invalidate caches, and shutdown releases handles/listeners. Old main Keyed XML is removed and only corresponding retired generated files are cleaned. Copy the complete `Output/phinix-rework`, including `Common/Extensions/Resources`, rather than DLLs alone.

Talent preserves pending payloads across queueing, rejected/cancelled callbacks, save backup and preparation failure. Save snapshots merge unresolved loaded recovery with current live listings; withdrawn current backups do not linger. Normal local drop-pod return removes the recovery record. Ownership-boundary exceptions retain raw data with uncertainty and block automatic replay. Cosmetic failures after delivery cannot produce a second return. Missing live-listing cache retains captured data with ownership uncertainty, rather than authorizing replay; missing payload retains its ID and a diagnostic. Callbacks verify activation and exact Game/component ownership; queue pressure rejects new callbacks without evicting accepted work. Structured `talent.pending_return` audits contain bounded/escaped IDs, token, time and outcome, never serialized pawn data. Abnormal outcomes are warnings.

Current output is under `Common/Extensions`: client DLLs `14-LegacyRedPacketExtension.Client.dll` and `16-LegacyTalentTradeExtension.Client.dll`, their `.dll.localization.json` companions, and `Resources/LegacyRedPacket/Localization` / `Resources/LegacyTalentTrade/Localization`, each with `en-US.json` and `zh-CN.json`. This is bundled deployment, not evidence of a published managed package.

The [Chinese companion](官方资源与人才返还切片验收.md) contains every exact command run. Commands execute at repository root with local RimWorld references set to `/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed`; substitute your legitimate 1.6 reference path. New harness restore was completed. Builds use `-p:BuildInParallel=false -m:1` and existing assets, with explicit repository `SolutionDir` for classic projects.

Results: managed metadata/resources/recovery parent 1624 assertions, existing startup children 18/18/16/16/20; actual Talent queue/component snapshots and both client localization facades 31 assertions each under .NET and Mono; RedPacket inventory/custody/history 8 scenarios. Full `Release 1.6` main/store build passed with zero errors and seven existing protobuf/obsolete and related warnings. Harnesses do not execute real Scribe saves or pawn delivery.

PowerShell is unavailable, so `check-artifacts.ps1 -IncludeClient` was not executed. Equivalent Python verification checked its 32 required paths, LoadFolders and excluded game DLLs, plus exact built resource length/hash/owner/source equality and retired XML cleanup. No Docker or in-game validation is claimed.

An additional solution `--no-incremental` attempt failed with 34 CS0006 missing-reference errors: net472 was produced by the client path, then shared-project Rebuild from the server path removed those files. A focused client build restored its chain. The final fresh-build procedure is one solution clean followed by ordinary build; this store slice does not alter multi-target Rebuild semantics. The failed attempt is not reported as passing.

Human checks now, using backed-up test saves and a full Mod copy after exiting the game:

1. Check RedPacket/Talent tabs, settings, details and notifications in English and Chinese. Switch game language/reopen the view or save: cached labels change, age formatting is correct and version notes do not show literal newline escapes.
2. Save an own unsold Talent listing in a test session, exit/reload and confirm one returned pawn via drop pod. Save/reload again: no duplicate pawn. Record identity/count and `Returned` audit.
3. Use another copy of that listing save. Disable Talent/restart, load and resave separately, re-enable/restart and load the new save: retained pending content returns once. This tests module disable with the DLL present, not DLL removal.
4. Another save must not receive the previous save's pawn. Smoke-test RedPacket inventory selection/send/claim/normal return. Online ACK/disconnect recovery remains separate from local delivery testing.

For `Deferred`, `Uncertain`, `uncertain-retained`, `queue-full-retained` or malformed-record outcomes, retain original save copies and provide surrounding logs, operation sequence and game version. Uncertainty deliberately blocks automatic retry; do not delete it to force replay.

The save fields are not a separate durable transaction journal and no authoritative remote delist ACK was added. Legacy market behavior across save rollback is not declared safe. Real Scribe/drop-pod behavior, actual missing-DLL resave retention and late managed-component discovery remain gates. After human feedback, continue the minimum RedPacket recovery and generic loading/save-retention slices; only then prepare separate repositories/formal releases. A working Tab or compile must not justify removing bundled business DLLs.
