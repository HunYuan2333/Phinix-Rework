# R17-20：公共动作菜单与游戏转换边界

日期：2026-10-14。承接 Region context，先关闭 session 的原生菜单类型泄漏；窗口合同和物理程序集布局仍待下一切片。用户要求适合游戏测试时主动通知，当前不部署中间 DLL。

## 源码检查与实现

`Client/ClientExtensionAbstractions/UI/IClientPresentationService.cs:43` 原返回 `List<Verse.FloatMenuOption>` 的 CreateActionMenuOptions 移除，改为 `IReadOnlyList<ClientPresentationMenuItem> CreateActionMenuItems()`。中立 item 只有 Label 和可空 Request；Request 是 UI 请求，不是业务回执。返回 ReadOnlyCollection，外部不能经集合 cast 修改 session 的菜单快照。该普通合同文件已不出现 Unity/Verse 类型；不等于整个 Abstractions 已独立于引擎。

`Client/Source/UI/ClientPresentationService.cs:621` 使用原 Bindings(page)、原动作集与原 generation 创建请求闭包。旧快照保留自己的 captured binding/ticket，不在点击时按 ID 重新查当前页面，因此同 ID 重绑定不会执行新业务。菜单不另行注册动作、复制 owner 权威或生成成功回执。

新增 `Client/ClientExtensionAbstractions/Game/ClientPresentationMenus.cs` 只把中立 items 转成当前游戏 FloatMenuOption，逐项保留 label 与同一个 request delegate，不做业务 lookup。Game bridge 与合同暂在同一物理 DLL；它是明确的引擎相关接入代码，不能冒充新的 1.7 backend。

当前消费者同步：

| 入口 | 修改与保留 |
|---|---|
| ChatMessageActions:152 | 原生 FloatMenu 子类继续管理 Refresh/PostClose；Options 经同一 bridge 生成，session/current/point 生命周期不变 |
| ChatSidebarProvider:214 | 用户菜单保留同样的权限、阻止/交易动作与 provider context guard；只改菜单数据转换 |
| ExamplePresentation:186 | 显式入口用普通 session items + 同一 Game bridge，不需要官方能力 |
| Host 分组菜单 | selected label 的勾选前缀、ActionIds 顺序与同 generation 的 Request 保留；转换集中到同一 bridge |
| Host composer overflow | 保留原动作集合、标签与 callback；走同一转换 bridge |
| 红包/人才/Inventory/Trade/主题/商店 | 无新增 session 菜单生产调用；随选定构建与既有回归核验 |

FloatMenu 弹出和更新仍由当前 Verse/game 接入处理，没有重写窗口栈、业务 owner 或确认系统。

## 白盒发现的公共 guard 缺口

已有代码在生成菜单时检查 DisabledReason，却未在 Request/Execute 中重新检查。新增 fixture 的 action 在快照后变成 disabled，保留的 callback 仍能调用 TryExecute，白盒明确复现。实际 owner 通常还有自己的 current/业务校验；这不证明任何实际交易绕过了业务校验。

本轮最小补齐：Request 与 Execute 在读取 GUI/业务路径前检查主线程；Current/ticket/待处理输入检查保留，调用前重新读公共 DisabledReason。禁用请求不调用 owner、不标 Completed、不消耗 later legitimate attempt；确认弹窗打开后状态变禁用也不能执行。业务判断和结果权威仍是 owner 的 TryExecute，Host 的禁用检查不能替代它。

原 Request → ConfirmationWindow → Execute 路径继续使用。callback Completed 仍只表示本地回调完成，菜单点击、关闭视图或打开确认都不构成业务确认/取消/返还。

## 验证矩阵

| 检查 | 状态 | 范围 / 限制 |
|---|---|---|
| 菜单生产调用、原生泄漏与 guard 路径 | 源码检查 | Host、Chat 与 Example；没有官方类型判断或第二动作注册 |
| 1.6 / 缓存 1.7 各 22 项选定构建 | 编译通过 | Host、内置、三个插件、Example、六测试工程；基础通信/protobuf 来自保留 Output，不是完整发行图 |
| 实际合同两版各 54 断言 | 白盒通过 | 中立 IReadOnlyList/items、旧 native 方法移除、普通 session 所有直接/泛型签名无 engine 类型；不调用 native 菜单 |
| 两版其他五套回归 | 白盒通过 | Trade 204、Inventory composition 84、game adapter 19、Example 45 断言，原 Inventory 19 组场景 |
| 生产 renderer + stub GUI，308 检查 | 白盒通过 | readonly snapshot、bridge callback identity、native list 与 snapshot 独立、Layout/Repaint/off-thread 不执行、动态禁用、旧 ticket/同 ID 重绑定、关闭、单次尝试、原确认与确认后的禁用 |
| source-manifest / 四仓库 diff | 检查通过 | 两插件本轮源码未改，原摘要匹配 |
| 游戏菜单尺寸/位置、遮挡/焦点、确认与 WindowStack、native 输入 | 待测 | 游戏通过：无；stub constructors/GUI 不证明真实游戏菜单行为 |

引用保持原 1.6 GameDlls 4633/Harmony 2.3.6，与 SHA 再核验的 1.7.4977 rev926 缓存/Harmony 3。当前安装 Version.txt 仍是 1.6.4871 rev598，没有切换安装、运行或改正式存档。非 GUI suite 使用系统 .NET 10，不等同游戏 CLR。

最终结果：`/tmp/phinix-r17-20-menu/closed-1.6`、`closed-1.7`、`closed-presentation`。final-1.x 是 Host 分组/composer 菜单转换集中前的结果，以 closed 为准。原始玩家日志和分析 DLL 不进入仓库。

## 尚未闭合

ABI 变化继续按 API 3.0 联合管理；正式 API/CLR 身份、包范围、Host pin、产品版本未更新。普通模型的 menu 已中立，但 IClientWindowService.Open(Window)、公共 shell/widgets、Game bridge 的物理程序集以及 native WindowStack/input/backend 未完成。不要因为 session 签名中立就宣称整个 SDK 或 1.7 游戏兼容。

下一切片审查窗口入口的普通 UI 与明确游戏接入层，再决定 R17-23 联合程序集布局，不因存在 Window 参数就把插件业务迁入 Host 或预设重写所有窗口。测试通知门槛保持 R17-33：可运行联合候选、准确版本/匹配包、同进程单一合同与 adapter、最小视觉/主题/菜单/Region 测试步骤；届时主动通知用户。当前不用安装中间 DLL。
