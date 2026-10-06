# RedPacket multiple-stack fix

2026-10-06 follow-up: the user accepted the increment and requested continued extraction. [Standalone candidate evidence](OfficialRepositoryExtraction.md) is separate from the remaining pending-send/restart/save gates; the old recheck-pending status below records the initial repair handoff.

[中文](红包多堆发送修复.md) · 2026-10-06 · Local repair/build passed; real game recheck pending.

The user accepted the previous localization/basic Talent checks and requested extraction, then reported a new blocker sending 800 silver. Repair precedes independent publication. Prior smoke feedback does not prove multiple-stack sending. No repository/release/index or bundled-DLL removal occurred.

The list grouped stackable Things while sending rejected any selection with more than one physical stack after popping/despawning. Vanilla silver has a 500 stack limit, so 500+300 failed before publication; the old handler then attempted map restoration.

Physical and inventory materialized sources now share `RedPacketStackTemplate`. It checks complete count, unique sources, bidirectional game stackability and full Scribe state. Equivalent ordinary stacks produce one stateful total-count wire template while original Things remain separately in custody. Serialization changes the first count only temporarily and restores it in finally; no absorption/destruction is used for aggregation.

Comparison excludes only top-level instance identity, quantity and transfer placement/lifecycle fields. Health/stuff/tickDelta/quest tags/graphics and nested component/reference data remain authoritative. Unknown custom Thing/ThingComp implementations stay single-stack. Native Thing/ThingWithComps and known per-unit Forbiddable/Quality/Colorable/Rottable/Ingredients components are permitted only with equal complete state. Different quality/damage/rot cannot be replaced with the first stack. A bilingual explanation is added, increasing RedPacket resources from 70 to 71 keys with updated hashes.

Stateful world delivery also splits by the actual physical limit (800→500+300), bounded to 1000 parts before materialization; failure discards temporary copies before any delivery. Existing inventory splitting remains. `PhysicalSelectionCaptured` logs counts without payload or credentials. Wire/codec/module/assembly/storage identities and confirmation semantics are unchanged. This does not complete cross-restart physical custody, pending-history reconciliation or independent-release gates.

Exact commands are in the [Chinese companion](红包多堆发送修复.md). RedPacket compilation passes with zero errors/warnings; 10 runtime scenarios pass under both .NET and Mono (existing 8 plus equivalent templates/count rollback/state rejection and actual custody consumption/refund-list behavior). Tests inject the game stackability and Scribe boundary, not Unity/drop pods. A native silver ThingWithComps/CompForbiddable layout case is included. Managed metadata/localization parent 1628 assertions and existing startup children pass; resource declarations and built files are checked. Unchanged server/full-solution builds were not repeated. Git diff/status was reviewed without committing unrelated changes.

After exiting, copy the complete `Output/phinix-rework` including resources and restart. Test sending 800 from equivalent stacks, receiving all 800 with valid physical partitions, then another packet's partial claim/expiry with claimed+returned quantity exactly 800 and one refund. For incompatible-state rejection, retain surrounding diagnostics and provide stack damage/forbidden state and Mod list. Do not bypass state checks. Resume RedPacket extraction/recovery gates after incremental acceptance, then Talent release.
