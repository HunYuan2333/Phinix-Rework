# R44 B22 游戏/存档结算边界

本报告更新 B21，不表示完整跨存档恢复通过。

## 源码证据

- BuiltInInventoryClientExtension.SynchronizeGame：另一个 Game 时释放旧 journal、清空当前 ledger display；Attach 根据 InventoryGameComponent.SaveId 与真实保存路径重新绑定 journal。因此“同一个 Inventory API 实例”不等于“同一个存档账本”。
- InventoryLedger.PrepareReservationResolution：在当前 ledger 找 reservation ID，已有目标终态可幂等；Commit/Restore 是有业务依据的写入，不是只读探测。
- MaterializeReservation：由 Reserved/Uncertain 记录重建原库存预留的物品。它不持地图 physical PoppedThing、原始位置、完整 Reset payload 或服务端结果证明。
- PrepareDeposit：向库存记入可消费数量和 deposit receipt。它不是不影响数量的 Trade 操作档案；直接把物理 escrow 再 Deposit 可能在原物体仍存在时造成双重所有权，不能当作持久化捷径。
- B21 Trade escrow 使用持续存在的 UI host context；该 context 在结算时读取当前库存端口/地图。未绑定 Game 的迟到结果可能跨游戏调用它们。切游戏后的报价窗口也不能继续用旧 availableItems 生成新报价。

## B22 实现

TradeOfferGameScope 捕获发起时的 Game 对象和 context activation generation。检查只在主线程资源路径；Game 为空、对象改变或生命周期终止后永久失效。改变当前地图但仍在同一个 Game 不触发失效，沿用原投放偏好。

原报价窗口捕获自己的 scope；游戏失效关闭视图，保留 owner，不取消/返还。预备报价/Reset 在 stage 前检查该 scope，库存刷新回调也检查。operation 持相同 scope，但不持窗口。

Commit、Restore、未发送 Abort、Unknown 的 ledger marking 和 Reset ReturnFrozen 在任何库存写入、physical Destroy/Restore、payload decode/选图前检查 scope。失效返回 Uncertain，owner 保留记录、禁止盲目重试；Unknown 不向新存档写旧 reservation。Reset 的明确拒绝无本地返还动作，仍可安全结束拒绝记录。公共 Host 状态/确认/API 沿用 B21，仅增加原因本地化；无第二 renderer、无 schema/wire 变更。

## 剩余门槛

这只是隔离错误目标，不重建旧 escrow。reserved inventory 的原 journal 仍保留原记录；physical PoppedThing 和 frozen Reset 仍为内存资源。进程重启后的关联结果、物理资源归属及 Reset 返还收据需要 Trade 的持久记录与可信结果证据；不能根据快照缺行/关窗/超时判定退款。单独的 completion delivery 路径不因本批自动获得跨存档绑定保证。

保存范围需另立明确契约：游戏身份、operation ID/token、结果证据、physical 托管资源所有权、发送/存储故障顺序、同一快照重复加载与收据幂等。该门槛满足前不声称 R44 完成，也不提前迁移高风险报价编辑。现有库存 journal 并非通用物理物品档案。

## 检查边界

客户端 Release 1.6 0 errors / 4 existing warnings；180 stub presentation checks；146 actual DLL assertions（包含无游戏、换 Game、回主菜单、生命周期终止且不可复活，及 actual escrow 的 Commit/Restore/Abort/Unknown/Reset 在失效 scope 下不触库存或 sentinel）。假对象代表身份边界，sentinel 验证早停；没有实际加载存档、Spawn/Destroy/空投证明。Example 1.0.28 与同一公共 ABI/Host 重新编译/静态包检查通过，源文件未改。

B27 更新：已有报价编辑的显示路径已迁入公共 Host，普通 Trade owner、ACK/托管/返还边界保留。旧“editor 仍原 Draw”的描述由 roadmap §16.35 替代；完整 R44 与持久恢复门槛仍未通过。203 stub / 189 actual DLL 不是游戏或重启恢复证据。
