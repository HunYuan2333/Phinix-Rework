# R44 Trade 呈现与业务语义门槛（2026-10-12，B20）

## 当前结论

列表/选中状态/打开原编辑器/取消请求可以独立迁移。报价编辑整批门槛未通过：Reset 先本地返还再发送报价清空；物品托管仍由 TradeWindow 持有。B20 不把这两个问题包装成 UI 已完成，不改变返还/恢复规则。报告依据实际源码，不是实机复现或服务端联机验收。

## 权威与生命周期

| 表面/状态 | 当前所有者与真实依据 | 呈现的允许行为 |
|---|---|---|
| 活跃列表和双方 Accepted | IClientTradeService.GetTrades / sync/create/update/completed/cancelled 事件，Trade repository/state input | 只投影快照；Accepted 是报价确认，不是成交/到账 |
| UI 选择、检索、等待提示 | TradeList；无 Things/预留/账本 | public page/list/action/status；关页只释放视图/动作 |
| 添加地图报价 | TradeWindow.UpdateTradeItems：PopSelectedPhysical/DeSpawn → token/PendingThings → UpdateTradeItems | 未迁移前保留原路径；不能在 Draw/Measure 触发托管或提交 |
| 添加库存报价 | InventoryReservationApi.TryReserve/Materialize/ResolveReservation；Trade 持 token 关联 | Host 不持 reservation/Thing，不能把回调完成当 Commit |
| Update 成功/失败 | TradeWindow.applyTradeUpdated 匹配 Trade ID + token，成功 Commit/Destroy，失败 Restore/返还 | 原规则保留；关闭窗口但仍有 pending 时保留更新 listener，回执后才卸载 |
| 成交/取消的本地交付 | PhinixDefaultTradeBehaviour + TradeInventoryDelivery，真实完成事件、完整 decode/交付成功后原 ACK | B19 合法空 direct 无需物品交付；非空失败保留 pending，业务确认不由 UI 产生 |
| 断线/模块终止 | activation generation、main-thread dispatcher、停止注册/订阅 | 列表清投影无自动返还；不创设超时/断线退款规则 |

## 本批修复与迁移

- TradeWindow.OnTradeFinished 原来收到任意 Trade 完成事件便设置 shouldClose。现在先检查非空、大小写不敏感的 Trade ID 匹配，在原 main-thread callback 检查 closed 后才设置。不同交易完成不再关闭本窗口；未改变本交易完成/取消交付规则。
- TradeList 使用公共双栏、固定/对齐头、列表、search、badge/status 与原 owned action sets。公开 TradePresentationActionContext 位于 Trade Contracts，只有 page/revision、选中 Trade ID、Accepted/堆数等事实，无 mutable snapshot 或托管权威。
- 取消增加公共 UI 确认，仍调用同一 CancelTrade(ID)。回调成功只显示等待；真实 completion 才移行，本地交付是独立流程。异常显示提交失败或不确定，不假定取消、不重试/返还；关页重开保留本会话等待提示。原接口没有独立 cancel rejection 结果，这批不虚构该状态/协议。
- 更新 epoch、theme/point/language/result、选择和视图寿命使旧 action/confirmation 失效；projection 不根据行索引重定位 ID。更新无 Trade 安全忽略；默认 ImmutableUser 的 UUID/名称缺失时显示真实 Trade ID 并禁用报价编辑，不虚构用户资料。
- 回调只更新 owner 数据/epoch；翻译、采集与渲染在 view update/draw 中。未迁移的报价编辑/设置 Draw provider 保持可达。

## 仍需完成的业务门槛

1. `ResetTradeItems` 目前 `DropPods(trade.ItemsOnOffer...)` 在前，`UpdateTradeItems(empty)` 在后，没有 token/确认后返还边界；拒绝、发送异常、重复点击可能使本地与服务器报价不一致。不能只把按钮换成公共 confirmation 便宣称安全。
2. `pendingItemStacks`、物理托管和库存预留仍依赖 Window 的关闭后 listener；下一切片需要独立 Trade operation owner 保持原真实回执/恢复语义，窗口只投影/意图。必须覆盖 close、stop、未知结果、重复/迟到、混合地图/库存整批投放，不能将 owner 放进 Host。
3. 当前 applyTradeUpdated 在获取 token 后移除 pending，再调用 ResolveInventoryReservation；失败返回只日志。该路径的失败恢复/账本关联需要独立故障检查，不能由视觉重构推导安全。
4. 下一批具体方案需以现 wire/adapter 的 request token 和明确结果为依据：先提交报价变更意图；匹配真实结果后处理一次返还/commit；未知结果保留托管和可观察状态。不因关窗/超时/断线返还，不新增协议或自动退款规则。若现结果不足以提供安全证据，需单独评审业务/协议范围，不能在 renderer 私有补偿。

## 证据

B20 Release 1.6 编译通过；147 Presentation stub checks（取消提交与回执区别、关页/重开、stale、异常、缺用户资料、回调无 UI 翻译、Dispose）；127 actual Trade DLL checks（含完成 ID 过滤及 B19 空 direct 修复）。这些没有原生 WindowStack/Thing/地图投放或真实联机证据。完整验收清单 UI-UX-B20-Game-Check-2026-10-12.md；构建 digest UI-UX-B20-Build-2026-10-12.json。


## B21 更新：会话 owner 与确认后 Reset（2026-10-12）

上文是 B20 原始门槛证据；最新实现见 roadmap 16.29。Window 的 pending 字典及闭窗 escrow listener 已移除，TradeOfferOperations/TradeOfferEscrow 由 Trade 模块持有；Reset 不再发请求前返还，只匹配真实 Trade ID/token 且空报价结果后处理冻结 payload。库存 Commit/Restore 失败先停，持记录不操作 physical；已知依据的本地处理可显式重试，未知/异常无盲重试。公共状态/confirmation 均使用原 Host。公共 facade 三个提交口阻止已持 operation 的冲突请求，不仅是官方按钮禁用。

180 stub 与 134 actual DLL assertions、编译通过，原生游戏仍待合批。**完整 R44 仍未通过**：跨存档/切游戏/重启的物理 escrow/journal 未实现持久恢复；不改变原保存格式，不承诺 stop 后自动返还。不能由会话内 owner 的 source/tests 推导这些安全。报价 renderer 仍原 Draw provider；下一批先核对现 Inventory ledger 能否承担持久托管及明确结果的本地恢复，若需新增保存/业务范围单独说明，不迁 Host、不伪造服务端确认。


## B26 关联证据修复

2026-10-12：源 service 曾将任意同 Trade 快照用于已成功响应的全部 pending tokens，full sync 也能误归因。已改 explicit correlation response + same Trade exact correlated delta，仅消费匹配等待；full/unrelated/missing metadata 不造 confirmation，status 与 LegacyAdapter 显式结果路径保留。177 actual DLL/191 stub 通过，实际联机待测；乱序安全保持等待，不实现自动重放/跨存档 escrow。详见 Trade-R44-Correlation-Gate-2026-10-12.md 与 roadmap §16.34。

B27 更新：已有报价编辑的显示路径已迁入公共 Host，普通 Trade owner、ACK/托管/返还边界保留。旧“editor 仍原 Draw”的描述由 roadmap §16.35 替代；完整 R44 与持久恢复门槛仍未通过。203 stub / 189 actual DLL 不是游戏或重启恢复证据。
