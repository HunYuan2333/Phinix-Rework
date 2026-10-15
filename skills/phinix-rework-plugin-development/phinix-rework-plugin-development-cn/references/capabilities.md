# 按功能选择公共能力

这里只说明组合方式；准确调用形状见 [api-cheat-sheet.md](api-cheat-sheet.md)。

## 呈现与受控自绘

通过 `IClientPresentationService.Open(module, pageId)` 创建 session。先消费 `TryDequeueInput(out input)`、更新插件状态，再按变化 Publish 页面投影与动作；Draw 使用当前 bounds。不必在每次 IMGUI Repaint 重建 action set。Publish 会更新输入修订号，关闭或更新后旧输入可能失效；不能把旧输入当成当前业务授权。

公共 renderer 管理布局、主题、动作与确认。受控自绘实现 `IClientPresentationRegion.Measure(float, IClientThemeReader)` 和 `Draw(IClientPresentationDrawContext)`，通过 `ClientPresentationRegion(id, content)` 放入页面。draw context 只在当前绘制内借用，使用中立 Bounds、Theme 和 Progress；它不是原生事件、任意 GUI、输入或动作的后门。

需要原生 Window/IMainTabProvider 时，使用公共 ClientWidgets 和 `ClientGuiScope` 恢复 GUI 状态。原生窗口按 `UiScreenSafeArea.ClampWindow(Rect, Vector2)` 或显式 safeRect 重算范围；不要对每个子控件调用。原生自绘滚动区可在 EndScrollView 后使用 ScrollbarDecoration，公共 renderer 已处理的区域无需重复装饰。新增呈现能力先评估公共 renderer，不为官方插件增加专用 renderer。

## 动作与确认

通过 `IClientExtensionPointService.ForModule(module)` 定义或贡献 typed actions。`ClientPresentationAction` 关联贡献动作，可附 `ClientPresentationConfirmation(title, text, acceptLabel, cancelLabel)`，由 Host 提供统一确认。

`ClientActionExecutionResult.Completed` 表示本地动作执行完成，不代表服务器或对端确认。按钮、确认框和关闭视图都不接管插件的事务、托管或账本。

## 主题与本地化

读取 `IClientTheme` 的语义值，原生绘制才通过 `ToUnityColor()` 转为 Unity Color。自定义键经 `IClientThemeRegistry.RegisterModule` 取得句柄，RegisterColor/RegisterFloat 后 Commit；关闭模块时 Dispose。使用唯一命名空间和通用 fallback，不建立第二套主题系统。

`IClientLocalizationService.ForModule(module)` 返回模块 localizer；使用 Text/Format，响应 LanguageChanged，并释放句柄。不要在 Compose 永久冻结翻译。纯数据主题包见 [theme-package.md](theme-package.md)，编辑、热应用和导出沿用 Host 公共能力。

## 消息、命令和物品

消息通过 `IClientMessageHandler` 与 `builder.AddClientMessageHandler` 注册；发送走 `IFrameworkClientTransport.TryHandleOutgoingMessage`。命令使用发现到的 `IClientOutgoingCommandHandler` 与 `IFrameworkClientCommandTransport.TryHandleOutgoingCommand`；物品走 TryHandleOutgoingItem。不绕过框架 pipeline 操作原始传输。

true/Handled 仅表示已处理或路由，不保证入队、持久化或业务确认；按实际 handler 的结果继续判断。

## 库存边界

公开声明位于 `Extensions/Inventory/Contracts/InventoryContracts.cs`：

| 需求 | 入口与约束 |
| --- | --- |
| 读取库存 | IInventoryReadApi.GetSnapshot/GetStatus/GetCapabilities；监听 InventoryChanged、AvailabilityChanged |
| 存入 | IInventoryDepositApi.CheckDeposit/TryDeposit；稳定 DepositId 支持幂等，不能给重试换 ID |
| 预留与交付 | IInventoryReservationApi.TryReserve、MaterializeReservation、ResolveReservation；由业务权威回执决定 commit/release，未知状态保留核对 |

物品转换与交付整批成功或整批失败。缺 Def、codec 或依赖时保留原始载荷并报告拒绝，不过滤坏项、不制造空白 Thing，也不由 Host 接管插件的报价或人物业务。
