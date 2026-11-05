# P3 业务实现候选（2026-10-23）

本轮仅在 `codex/p3-trade-business` 继续，未混入原 UI 工作区；冻结 28 UI 和 9 未分类文件 hash 不变。Server/Client Common gitlink 仍分别为 67f243d/c165f61，没有修改公共子模块。

Trade 新增协商 DurableInboxV1：原始完成/取消 payload 先落盘，再绑定 deliveryId/recipient/hash。InboxStored 表示载荷已持久接收；SavedInventory 只有在读取实际保存 XML、核对 SaveId/库存 journal identity/原 deposit 已保存后才生成。两类文件不可覆盖；服务端回执响应按原 correlation、当前 session/endpoint/主体、系统 sender 校验。现有核对视图分别显示本地状态和服务端观察。

库存交付先冻结原 opaque 整批及固定 depositId，绑定原 save/journal，再写 attempt，最后调用真实库存 API。重启只恢复同一幂等请求，已成功后账本缺失不再创建资产。源日志有 marker 与保存身份保护；拥有所有权变化的 future branch 不能直接丢弃。原 intent 的保存查询结果只结算完全匹配的库存 reservation，NotFound/断线不退款。恢复每批 8 条，经普通主线程 dispatcher 调度，停止/失败解绑所有借用事件。

可靠保存回执适用设置 `inventory.deliveryMode=inventory`。已有 `direct` 选择保留：直投仍保存原收件和 attempt，但不能证明实际存档持久到账，不能自动再造 Thing。未绑定、历史无计划或不同 save/journal 的记录保留核对。旧协议无可靠 ACK 时继续 Unknown/尽力语义。

实现清单已收口；真实 RimWorld 联机/物化/库存取出/存读档与现代完整网络故障链待集中测试，不能将自动文件/组件结果等同真实游戏。未安装、打包发布、更新工坊或部署。

通过的本轮自动证据：Inbox 90、Intent 32；1.6/1.7 Trade 组合各 291、真实 wire 各 34；1.7 Inventory 组合 85、运行时 21 场景；两版完整产品与三个维护插件构建。详细命令、源码/DLL/fixture hash、失败修复原因和服务端对应记录见 `/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-P3-Implementation-2026-10-23/manifest.json` 及服务端 `docs/Server-Upgrade-P3-Implementation-2026-10-23.md`。
