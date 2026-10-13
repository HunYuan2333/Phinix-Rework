# B25 恢复可见性游戏验收（待测）

未改存档格式/协议/业务结果。读取 Inventory 原 journal 中已有 Trade Reserved/Uncertain，不等于实现物理托管重建或退款。

- Trade inventory offer pending：公共交易页分别显示会话 operation 与库存 journal record，明确为不同事实，不能算两笔活跃交易。
- 选择库存记录：核对 reservation ID、原 operation ID、Reserved/Uncertain、ledger evidence 与 codec 单位数量；同 Trade ID 的活跃交易也不能让该记录页出现官方 cancel/open/retry。
- 关页/移除 trade/重开：inventory record 仍可达。只有原 ledger 变更后状态更新，不因为回调/关闭自动解锁。
- 已持久 Trade reservation 重载：待 journal 真正绑定当前 Game 后显示；未绑定/未保存 Game 不显示旧账本。绑定的只读故障允许核对，不允许从此 UI 处理物品。
- 改变 ledger/availability 后显示刷新，重复开启/停用插件订阅配对，无后台 Draw/翻译；大于 100 行可通过公共上一页/下一页访问末页，筛选重置页码。
- 普通第三方通过 ITradeInventoryRecoveryApi 获取不可变 facts；没有 Resolve/Restore/Materialize/Retry 写入端口。Example 1.0.30 的普通 readonly/control/point directory 仍可用。

191 stub 与 163 actual DLL checks、客户端/Example 编译/包通过；没有实际保存、重载、地图 Spawn/Destroy 或服务端证据，以上均待游戏。B21/B22 业务专项、B24 报价摘要、T5 清单继续保留。完整 R44 持久恢复和编辑迁移仍未完成。
