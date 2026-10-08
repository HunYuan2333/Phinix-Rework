# RedPacket independent repair plan / 红包独立修复计划

2026-10-08. Planning only: no production code, contracts, localization resources, package manifests or tests changed; no build/game test run. Keep this independent defect batch deferred until the planned infrastructure F stages are finished. Implementation belongs to the independent `Phinix-Legacy-RedPacket` repository, not Store UI or the host DI migration. The user explicitly requests a separate RedPacket plugin update containing both defects.

仅制定计划，未改生产代码、契约、本地化资源、包清单或测试，未构建/运行游戏；保留当前基础设施 F 阶段，完成后另行实施。实现属于独立红包仓库，不放进商店界面或宿主 DI 迁移；用户明确要求把两项缺陷合在同一红包修复批次，单独推送插件更新。


## Added report: completion-summary null reference / 新增反馈：领取完成通知空引用

2026-10-08. User reported `[builtin.legacy-redpacket] [RedPacket] Message processing failed; cursor held for retry`, `System.NullReferenceException`, reference `21196259`. Stack path: `QueueSenderSummary` → `HandleClaimSafe` → `ProcessProtocolMessageSafe` → `PollRelayBuffer`; callback runs through the client main-thread dispatcher, and the game stack ends in `Verse.Root_Entry.Update`.

Latest independent source: `Client/RedPacketStateMachine.cs`, sender completion-summary call in `HandleClaimSafe` and the `QueueSenderSummary` method. The method guards null packet and empty local UUID, and the claim-summary helper handles a null dictionary; it nevertheless directly calls `Find.LetterStack.ReceiveLetter`. Main-menu/world-unload/read-save availability of the letter stack is a concrete investigation lead, not a confirmed root cause. The installed plugin version and exact failing expression are not yet known; the supplied stack alone does not prove a missing letter stack or a reward-delivery failure.

领取完成汇总通知抛空引用，外层把消息游标留在原位等待重试。日志出现主菜单 `Root_Entry`，源码直接使用 `Find.LetterStack`；优先核对主菜单、退出世界、切档时通知上下文，但尚未复现，不能把此线索当成已确定根因。这条红字也不能直接证明领取物品失败或需要退款。

### Planned repair / 后续修复范围

1. Reproduce claim completion while a game is loaded, at the main menu, during save switching and on shutdown. Inspect all notification dependencies and record bounded plugin-version/event/context diagnostics; do not dump relay payloads or player information.
2. Audit ordering of authoritative claim application, item consumption/deposit, `MarkProcessed`, terminal packet cleanup and cursor advancement. A notification failure must not undo committed item work or hold an already-applied business event indefinitely. Preserve cursor hold/recovery for real protocol/item transaction failures; do not globally skip every failed relay message.
3. Make completion/expiry notification scheduling independent of successful domain finalization. If game notification context is unavailable, use a bounded, deduplicated pending summary with safe value snapshots and lifecycle-aware main-thread delivery. Key completion/expiry independently by packet/event; never retain old world/Thing references or reuse disposed session services. Do not turn an uncommitted claim into success merely because a notification was queued.
4. Cover duplicate/replayed claim messages and notification retries: no repeated reward, consume, return or letter; no packet left half-finalized after the first notification exception. Keep useful error diagnostics and suppress repetitive identical notification failures only with bounded context/rate control.
5. Test claim completion and expiry/refund summaries together with the existing stack-selection batch. Changing summaries must not weaken server confirmation, all-or-nothing ownership, original refund records or uncertain-result handling.

复现通知上下文并检查已应用领取、去重、完成清理与游标推进顺序；通知失败和物品/协议失败分别处理，不能全局吞错或跳过消息。通知只能在可靠提交之后排队，具备容量、去重和生命周期清理；重放不得重复入库、消耗、退款或通知。完成通知与过期退款通知一起验证，保持所有权和恢复规则。

### Joint acceptance and publication / 与堆叠问题共同验收、独立发布

- Preserve the 200-steel stack-selection acceptance criteria below.
- Completion/expiry notifications work in an active game; absent world context does not flood errors, leave a completed packet stuck or block later relay events. Re-entering a save delivers or explicitly resolves any deferred summary according to the documented policy.
- Deterministic checks cover duplicate/replay, completion cleanup, unavailable/disposed context and genuine item failure. Game checks cover normal final claim, return to menu, load/switch saves and expiry/refund; verify actual item counts and notification count.
- Build/package in `Phinix-Legacy-RedPacket`; increment the plugin version and update bilingual resources/hash declarations as needed. Publish a new immutable plugin release, then submit/update its Index record through the normal reviewed route; do not overwrite an existing version or bundle it into the client host. Verify the Store downloads the exact new ZIP and records the expected version/hash.
- Keep defect repair and eventual DI entry migration separately reviewable. Neither fixing these bugs nor merely moving to DI proves the other has been completed.

两项缺陷在红包仓单独验收并发布一个新插件版本；同步必要的资源、哈希和正规 Index 准入记录，核对商店实际下载的新产物。不覆盖旧 ZIP、不重新塞回主包；红包 DI 迁移另列，不把换入口等同于业务修复。

## Observed mismatch / 已查到的规则差异

The user reports selecting 200 steel and receiving the localized incompatible-stacks exception. `RedPacketTab.RefreshAvailableItems` uses Trade's `StackedThings.GroupThings` (game stackability), while sending pops/despawns physical sources before `RedPacketStackTemplate.Capture` enforces known Thing/ThingComp types, bidirectional stackability and full comparable Scribe state. Multiple physical stacks can therefore appear as one selectable row but fail the stricter send check. Inventory-materialized sources share this template checker and must remain covered.

用户发送 200 个钢铁触发不兼容堆异常。列表使用 Trade 的游戏堆叠归组，发送却在弹出/移除物品后执行更严格的模板检查：已知 Thing/组件、双向堆叠及完整 Scribe 状态一致。列表合并并不代表可以安全共用一个发送模板；库存物化来源也使用同一检查。

The same localized exception covers unsupported types/components, stackability failure, incompatible snapshot/count/codec, and state differences. The supplied stack trace does not identify which branch caused this user's failure, whether the source was map or inventory, or which DLL release was installed. Do not label ordinary steel unsupported, claim a specific XML field caused it, or assume rollback actually restored every item solely from the current log message.

同一个提示覆盖多个拒绝原因，现有日志无法确认本次具体分支、来源及插件版本；不能直接断言钢铁不支持、某个 XML 字段导致错误，或仅凭日志文案确认全部物品恢复成功。

## Batch 1: Diagnosis and predictable rejection / 第一批：诊断与可预期拒绝

1. Record plugin version and source kind; reproduce equivalent vanilla steel across multiple physical stacks, with and without other mods. Inspect actual serialized states and component types. Also inspect the split stack case: partial extraction may change runtime identity/placement independently of transferable state.
2. Define one structured eligibility result shared by selection and send validation. Reasons distinguish unsupported Thing type, unsupported component, bidirectional stackability mismatch, transferable-state difference, invalid snapshot and count/selection changes. Keep bounded safe fields: Def, selected count, source-stack count, type/component names, reason and differing field paths. Do not log full Scribe XML, payloads, credentials or player information.
3. Add a read-only physical selection plan and validate before SplitOff/DeSpawn or relay publication. Revalidate selected identities/counts/state at execution to handle game changes; preflight cannot replace the final invariant. Preserve the existing rollback after mutation. Inventory preflight checks metadata where possible; if actual materialization is needed, use existing atomic reservation/restore and destroy only transient previews.
4. Expected incompatibility becomes a concise localized rejection rather than a full ERROR stack trace. Unexpected serializer/transport/restore faults retain error logs. Report actual restoration outcome; do not say “restored” when restoration is incomplete or the relay outcome is uncertain.

先确认版本、来源与真实状态，补有界诊断并引入统一可发送性结果。物理来源先制定只读选取方案并校验，再拆堆/移除；执行时重新核对，保留变更后的回滚。库存沿既有预留、物化、恢复规则。预期拒绝使用普通提示，意外故障保留错误堆栈；返还文案须反映真实结果，不能将未知发送结果自动返还。

## Batch 2: Align selection and aggregation / 第二批：统一选择与合并

1. Use the same eligibility/state policy in RedPacket-specific grouping and send validation. Display separately the groups that can safely share one template; selectable quantity is the usable quantity of that group. Retain Trade's existing grouping behavior unless a separately reviewed shared abstraction is required. Do not add a Steel-specific path or fork host item conversion.
2. For a requested quantity that fits a single compatible physical stack, prefer that stack rather than needlessly selecting several smaller sources. Unsupported custom-state objects retain a single-source choice with a clear quantity limit. Never silently choose a different state group or reduce the requested quantity.
3. Allow equivalent ordinary stacks to aggregate after checking count conservation, bidirectional stackability and complete transferable state. Diagnose differences in canonical state comparison before changing exclusions. Ignore only fields proven to be instance identity or placement/lifecycle metadata, with focused tests; do not blanket-ignore tickDelta, component state, damage, quality, ingredients, quest tags or styles. Custom components require an explicit state-preserving contract/codec before multi-stack support; do not grow a list of per-mod exceptions.
4. Cache expensive inspection only within a bounded UI refresh/selection lifetime, with invalidation for game changes. Final validation must use current state. Bound XML size, stack count and work per refresh; avoid serializing every map item every GUI frame.
5. Keep one-template legacy protocol. If heterogeneous states genuinely cannot share a template, reject or offer explicit separate selections. Multiple state templates would require a distinct protocol/recovery plan; do not silently split into several independently published packets.

红包内部统一归组与发送规则，显示可安全发送的状态组及实际可选数量；不顺带改变 Trade。能从单个堆满足数量时优先单堆，未知自定义状态仍提供明确的单堆选项，不暗改数量或状态。普通等价堆继续合并，只有经证据和测试确认的身份/位置字段才可从比较中排除；自定义组件通过保留完整状态的契约支持，不加逐模组例外。预检缓存按刷新寿命限定并失效，最终校验使用当前状态。保持现有单模板协议，不自动拆成多个红包。

## Batch 3: Presentation, regression and delivery / 第三批：提示、回归与发布

- Bilingual UI shows source, usable count, and why a group is limited. Example: “这些钢铁来自不同状态的物品堆，无法放入同一个红包。请选择一个状态组。” Unsupported component and real state mismatch receive distinct messages; selection changes prompt refresh/reselection.
- Automated cases: equivalent steel totaling 200, different source order, sufficient single large stack, partial-stack extraction, prior 800-silver behavior, identical/different known components, unknown components with single/multiple sources, malformed serializer result, selection changes, map/inventory sources and large-count bounds.
- Ownership cases: preflight rejection leaves physical objects unchanged; partial extraction failure restores all captured originals; template serialization restores temporary count in finally; inventory reservation restores on definite unpublished failure; uncertain publication stays pending; receiving and expiry preserve claimed + returned quantities with no duplicate refund. Check restore-failure diagnostics.
- Game acceptance: send 200 steel from equivalent stacks, receive all 200, repeat from inventory, reject genuinely different states without item loss, disconnect/reconnect, and verify counts after partial claim/expiry. Include the affected user's original mod list; console tests do not prove game behavior.
- Build/package the independent RedPacket plugin; update bilingual resources, declared resource hashes/version and supported host requirements together. Confirm the store catalog refers to the new artifact before claiming the store-distributed plugin is repaired. Release the independently versioned plugin after this repair batch is accepted; do not bundle RedPacket back into the host.

中英提示区分状态不同、自定义组件未支持、数量变化。自动回归覆盖 200 钢铁、单大堆优先、部分拆堆、原 800 白银、多种组件及库存路径，并核查变更/回滚/未决/退款所有权。游戏验证发送、接收、部分领取、过期与原用户模组环境。独立插件构建后同步资源哈希、版本与商店产物锁；修复源码不等于商店已发布新版，不重新内置到宿主。

## Acceptance / 完成条件

Equivalent ordinary steel stacks can send the requested 200 without losing state or items. Incompatible groups are identifiable and selectable restrictions match final validation. Expected rejection does not flood ERROR logs. Original snapshots, reservations, relay authority, idempotency and rollback remain valid. The actual store artifact is verified and the user reports game acceptance. Root-cause attribution remains provisional until real state/branch evidence is collected.

等价普通钢铁多堆可发送 200，完整状态和数量不丢失；不兼容组可识别且列表限制与最终校验一致；预期拒绝不刷 ERROR。模板、预留、中继确认、幂等与回滚语义保留，实际商店产物核对并经游戏验收。收集真实分支/状态证据前，根因保持待确认。
