# 按功能选择公开能力

仅实现对应功能时读相应小节。完整手册和源码通过 [basis.md](basis.md) 定位；以下是导航与关键语义，不是全量 API。

## UI / 设置 / 翻译

`PhinixClient.IMainTabProvider`：`TabLabel`、`TabOrder`、`Draw(Rect)`；通过 builder.RegisterApi 发布。响应布局另实现 `IResponsiveMainTabProvider.LayoutHints`。设置用 `PhinixClient.Framework.IClientSettingsPanelProvider`：SectionId、Order、IsVisible、DrawSettings，签名按目标接口核对。需侧栏/徽标/横幅/Enter 时分别查 `IServerSidebarProvider`、`IBadgeProvider`、`INoticeBannerProvider`、`IUiAcceptKeyHandler`。

布局使用公开 `UiScreenSafeArea.ClampWindow`、`ResponsiveFormLayout`、`ResponsiveSplitLayout`、`ResponsiveToolbarLayout`、`VirtualListLayout`；Normalize 是内部成员，不能调用。Toolbar 计算几何，调用方负责绘制与溢出菜单。Draw 不创建大量 Thing，不留旧世界缓存；finally 恢复字体、对齐、换行、GUI.color/GUI.enabled 等共享状态。验收窄窗口、滚动、语言切换与设置保留。

包作用域 `Resources/Localization/*.json` 随工具生成清单；沿用目标 Example 的键结构与 `--language-file`。从 `IClientLocalizationService.ForModule(this)` 取 localizer，按 [所有权](composition.md) 清理。

## 消息 / 命令 / 物品管线

Common `Utils.Framework.IClientMessageHandler` 实现出入站文本/消息处理；用 `IExtensionBuilder.AddClientMessageHandler` 注册。物品对应 AddClientItemHandler/`IClientIncomingItemHandler` 和 AddClientOutgoingItemHandler/`IClientOutgoingItemHandler`。入站命令 `IClientCommandHandler`，出站命令 `IClientOutgoingCommandHandler` 正交，入站用 AddClientCommandHandler。当前宿主从 discoveredExtensions.Extensions 筛选 IClientOutgoingCommandHandler，因此出站 handler 由被发现模块实现（如 Trade）；没有 AddClientOutgoingCommandHandler，单独注册服务/API 不保证进入该管线。按目标版本核对发现逻辑。

宿主公开出站入口：

- `IFrameworkClientTransport.TryHandleOutgoingMessage(string)`；
- `IFrameworkClientTransport.TryHandleOutgoingItem(FrameworkItemPayload)`；
- `IFrameworkClientCommandTransport.TryHandleOutgoingCommand(FrameworkPacket)`。

返回 handled/true 不等于领域权威确认；核对实际处理结果、发送失败传播与请求回执。不要直接 `SendFrameworkPacket` 或 raw Legacy transport 绕过 Priority、拦截、替换和回退管线。原版适配的特殊接收入口属于 Legacy adapter，不是普通业务插件模板。连接兼容模式与远端能力分别查公开接口，不给 `IClientSessionContext` 编造服务器地址字段。新增服务端协议时才读服务端源码与固定 Common 契约。

## 库存

契约位于客户端 `Extensions/Inventory/Contracts/InventoryContracts.cs`，文档 `docs/Inventory.md`。模块依赖 `builtin.inventory` 与托管 packageId 依赖是不同标识层次，按实际 manifest、模块特性声明；不要把模块 ID 随意填入包依赖。获取 API 后还检查扩展激活与 `GetStatus()/GetCapabilities()`，不因 TryResolveApi 成功就认定可写。

| 目的 | 公开 API / 调用 |
| --- | --- |
| 读状态 | `IInventoryReadApi.GetSnapshot/GetStatus/GetCapabilities`；订阅 InventoryChanged/AvailabilityChanged 成对取消 |
| codec/来源展示注册 | `IInventoryRegistrationApi.RegisterCodecScoped/RegisterSourcePresenter`，保存并释放返回句柄 |
| 整批入库 | `IInventoryDepositApi.CheckDeposit/TryDeposit(InventoryDeposit)` |
| 出库、定时计划 | `IInventoryExtractionApi.TryExtract/SetDailySchedule/RemoveDailySchedule` |
| 出站业务托管 | `IInventoryReservationApi.GetAvailableSnapshot/TryReserve/MaterializeReservation/ResolveReservation`；预览 `CreatePreview` |

稳定 DepositId 与原始内容相同重试返回 AlreadyCommitted；同 ID 不同所有权内容是 Conflict。仅 Committed/AlreadyCommitted 后生产者释放自己的托管；CheckDeposit 不提交。新游戏须先保存，遇到旧快照/日志冲突等状态要按公开能力拒绝写入，不能绕过只读保护。

出站预留整批原子化、持久化稳定操作 ID：匹配成功回执才提交，证明拒绝才恢复，派发后结果未知继续锁定并核对。物化临时副本必须整批成功、转换后销毁，不落地；不把它们当作新的所有权。缺 codec/Def/Mod、payload 损坏或 state 无法无损恢复时整批拒绝并保留数据，不能重造白板 Thing。不可分状态条目以整条目处理，不以显示分组重写 payload。`IInventoryItemPresentationCodec` v5 仅展示能力，缺失时退回独立行，不升级持久化或协议。

真实业务参考：红包的中继发布接受/历史匹配确认不是 Trade-server ack；HTTP 排队不等于接受。其断线/切档/重启未决历史自动核对尚未实现，不自动退还或重发。人才贸易独立 Pawn 路径，无 Inventory 依赖，内存态未结算购买不能自动崩溃恢复。具体版本与已确认游戏验收见 [basis.md](basis.md)，传统入口与局限不作为新方案推荐。
