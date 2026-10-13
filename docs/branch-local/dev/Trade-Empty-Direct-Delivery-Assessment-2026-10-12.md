# Trade 旧版直接交付错误评估（2026-10-12，未修复）

用户提供主题重构前客户端日志：Trade ID `7ec52560-d25d-4471-83ec-4508b18a120d`，`Delivery failed ... completion remains pending: There are no valid items to deliver.` 没有完整操作记录、准确构建 digest 或完成回执 payload。当前只读核对，不改变任何交付/确认状态。

## 源码证据

- PhinixDefaultTradeBehaviour.tryDepositPending：只有 Deposit/DeliverDirect.Succeeded 才移除本地 pending 并调用 acknowledgeCompletion；失败保留记录。日志不是“服务端交易未成交”的证明，而是本地交付/返回环节未完成。
- TradeInventoryDelivery.Deposit：解析/整批检查后 `items.Count == 0` 返回 AlreadyCommitted，没有物品需要入库；属于无需交付的完成结果。
- TradeInventoryDelivery.DeliverDirect：同样从 completion payloads 或 snapshots 构造列表，但缺少空集合分支，仍解析落地点并调用 InventoryDropDelivery.DeliverThings。
- InventoryDropDelivery.DeliverThings：空集合或任一 Thing/def 为 null 都返回这条精确错误。DecodeItemExactly 已拒绝 null/缺失 def/UnknownItem；坏 payload/缺 codec 走“Trade items could not be restored without loss”错误，不静默跳过。因此当前最符合日志的路径是 completion 的集合为空，仍需回执物品数量确定该实例。
- DeliverDirect 与 DeliverThings 方法体均与 `Phinix-Rework-unsplit-backup-20261008` 相同，此缺口早于本次主题/UI 重构，不是 B18 配色引入。
- 原 Phinix/Common/Trading/ServerTrading 的完成时交付对方报价、取消时返还本方报价；一方可为空。Legacy adapter ConvertProtoThings 对坏项抛异常，不过滤成空列表。合法空报价、单向赠送及无报价取消都可产生没有物品需要交付的一方。
- TradeComposition ActivationLifetimeTests 已测“空完成→入库→ACK”，未覆盖空完成直接空投，不能由那条测试推导 direct 通过。

## 未确定与修复边界

当前未知：报错方是否本来应收到物品，事件 completed/cancelled，入站 items/payloads 数量、是否有第三方修改和精确旧 DLL。若用户应收到物品，必须先查上游回执是否丢失项目；不能统一把错误认成无需交付或直接清 pending。

若确认权威 completion 合法为空，最小修复应在 Trade delivery 的业务 adapter 处理“无需交付”，与入库语义对齐，避免请求地图/drop target；保持一般 DropDelivery 对空/坏项的拒绝。缺少 Trade ID、非空坏 payload、decode/投放失败/不确定仍拒绝并保留 pending。只依真实完成事件和本地无需交付成功进入原 ACK 链，不能把普通报价为空当成成交回执。

后续测试应覆盖 completed/cancelled 两种空交付（direct/inventory）、非空坏 codec/坏项、地图不存在、重复/过期完成事件、ACK 前 main-thread 生命周期与正常真实投放。当前没有改 Trade 源码，没有发布修复，也没有实机复现。


## 用户场景确认

用户已明确回答“单向赠送，报错方不收物品”。该实例与上述合法空交付分支缺陷吻合：赠送方没有应投放物品，直接空投路径仍请求 DeliverThings(empty)，因拒绝空集合而无法完成本地 ACK。该日志不是赠送方还应收到物品的证据；收礼方真实交付结果另行依据该方记录，不从本日志推定。当前仅评估，未改交付或 ACK 实现。


## B19 修复更新（2026-10-12）

用户确认“单向赠送，报错方不收物品”，已授权修复。合法空 completion 在 Trade DeliverDirect 中完成无需交付分支，不要求地图/空 pod，继续原 completion ACK；缺 ID/非空 decode 错误与一般 DropDelivery 空项拒绝保留。无 Map 的成功不发无效投放通知。124 隔离 Trade assertions 通过，真实双端赠送仍待游戏；细节 roadmap 16.27 和 B19 manifest。上文是修复前历史评估，当前状态以本节为准。
