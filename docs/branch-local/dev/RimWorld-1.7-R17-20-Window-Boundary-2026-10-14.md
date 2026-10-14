# R17-20 窗口入口边界（2026-10-14）

本切片按设计哲学 §1.1～1.3、§2.2 和 Compatibility-Boundaries 的业务权威/主线程约束实施。普通设置入口与插件拥有的原生游戏窗口分开；没有新窗口业务模型、专用 renderer、动作系统或自动返还机制。

## 实现与依据

| 范围 | 实际源码与调用依据 | 结果 |
| --- | --- | --- |
| 普通设置 | `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs` 的 `IClientSettingsWindowService.OpenSettingsWindow()`；Chat 激活借用此服务 | 保留中立无参数入口 |
| 原生窗口 | 新 `Client/ClientExtensionAbstractions/Game/IClientGameWindowService.cs` 的 `Open(Verse.Window)`；删除混合的 `IClientWindowService` | 引擎参数明确属于 Game 边界；尚在原 Abstractions 程序集中，不宣称物理解耦 |
| 同一 Host 服务 | `Client/Source/Framework/ClientWindowService.cs`；`Client.cs` 注册 game/settings/management 服务与原 Action 设置入口 | 同一实现与 WindowStack，无官方私有入口；非空 Open 与设置入口先验证 UnityData.IsInMainThread，设置窗在验证后构造 |
| Trade | `Extensions/Trade/Client/{BuiltInTradeClientExtension,ClientTradeUiHostContext,PhinixDefaultTradeBehaviour}.cs` | 原窗口与 Dialog_MessageBox 保留；原主线程队列/派发路径不改 |
| Inventory | `Extensions/Inventory/Client/BuiltInInventoryClientExtension.cs` | 原 delivery choice 窗口仍归 Inventory；ServerTab.PostOpen → NotifyMainWindowOpened → shell event 的 UI 路径保留 |
| 商店 | `Extensions/PluginStore/Client/{PluginStoreClientExtension,PluginStoreSettingsPanel,StoreActivationServices}.cs` | 原窗口 owner、排队开窗、释放路径保留；设置按钮原 GUI 路径保留 |
| 第三方 | 红包、人才、Example 本轮无 IClientWindowService 消费 | 本轮无需改业务；其已有原生 Window/Find.WindowStack 依赖仍待 Game adapter/程序集布局整理 |

Host 只把传入窗口交给栈，不接管资源、交付、报价、Pawn、协议或关闭语义；null 保留 no-op，真实栈异常向调用方传播。主线程 guard 不自动排队，不把错误线程调用报告为成功。普通业务 callback/task 完成仍不代表业务确认。设置与 management 窗口同样使用既有服务，不新增第二窗口系统。

## DLL 和版本证据

当前安装 `Version.txt` 为 **1.6.4871 rev598**。本轮编译引用的 1.6 是原 `GameDlls` **1.6.4633 rev1261**（Assembly-CSharp 1.6.9438.37837）；1.7 使用 `/tmp/phinix-r17-11/repro-1.7/run` 保存的 **1.7.4977 rev926**（Assembly-CSharp 1.7.9782.27799）。精确 SHA/MVID 继承总 roadmap 与每轮 input-fingerprints，不把当前安装当成 1.7。

两版真实 DLL 均接受本轮生产代码对 `Verse.Window`、`Verse.Window.IsOpen`、`Verse.Find.WindowStack.Add(window)` 与 `Verse.UnityData.IsInMainThread` 的编译绑定。这是类型/调用签名证据，未新做上述方法 IL 行为等价证明，也不能证明原生栈运行行为。旧 DLL 对照记录显示 Window/WindowStack 基本延续，不能因截图中的 IMGUI 异常推导必须重写所有插件 Window。

## 验证

输出根 `/tmp/phinix-r17-20-window`，原引用、Output、正式 bin/obj 与正式存档未覆盖。

| 检查 | 状态 | 范围与限制 |
| --- | --- | --- |
| 依赖与调用路径 | 源码检查 | 混合 interface 的生产消费全部迁移；原 UI/main-thread dispatch 路径保留 |
| 两版选定工程 | 编译通过 | 每版 22 项；Host、Chat/Trade/Inventory/Store、红包、人才、Example 同步；固定基础设施从 Output seed，非完整发布构建 |
| 实际合同 | 白盒通过 | 每版 57 项；设置签名中立、Game entry 显式 Verse.Window、旧混合契约不存在；其余数值/图像/菜单/context 检查继续通过 |
| 六套非 GUI 回归 | 白盒通过 | Trade 204、Inventory game adapter 19、Inventory 原 19 场景、Inventory composition 84、Example lifecycle 45，以及上述合同 57；系统 .NET 10 进程执行 |
| 窗口源码/stub | 白盒通过 | 12 项；原对象传栈、null、错误线程拒绝及设置构造无副作用、management 防重复/关闭后重开、栈异常传播；不执行真实游戏 WindowStack |
| 商店组合 | 编译通过、白盒通过 | 两版单独编译实际测试源码，每版 109 项，含激活/释放及依赖借用；不执行原生商店窗口 |
| 原生 WindowStack/焦点/输入/生命周期 | 待测 | 游戏通过：无；关闭/重开、切档、modal、IME、热应用/插件停用均待实机 |

可复跑两版编译与六套检查：`Tests/TradeCompositionRuntimeTests/run-isolated.py`，按总 roadmap 的真实 game/harmony/autofac 路径指定新 output；窗口专项：`python3 Tests/WindowBoundaryRuntimeTests/run-isolated.py --output /tmp/<fresh>`。本轮商店补充生成工程与运行脚本为 `/tmp/phinix-r17-20-window/check-store.py`，引用本轮隔离 run，输出 `store-1.6`、`store-1.7`。原 renderer/主题本轮未改，上一切片 308/73 项结果不冒充本轮重新运行结果。

## 下一切片与测试门槛

本轮是接口归属和线程约束整理，**不是 1.7 UI 后端实现，也不是完整 1.6 adapter**。API 3.0 联合迁移中删除旧混合入口，不新增旧客户端桥接。正式 CLR/API 身份、Host pin、包兼容范围、产品版本继续未变；此中间源码不能与正式 API 2.0 包混装。

R17-23 下一步先核对源文件依赖图，划分中立契约、共享 Game bridge、版本实现，并同步 Host、内置插件、三个正式插件和 Example 的程序集引用/绑定；仅按真实依赖拆分，不引入无消费者的窗口描述模型。随后窗口/输入与两版 backend 闭合。存在 Game 文件夹并不代表程序集无 Unity/Verse 引用；现有 shell/settings panels/sound/widgets 等泄漏仍须明确归属。

用户要求适合游戏测试时主动通知：继续在 R17-33 首个可运行联合候选提供准确游戏/Harmony/Host/插件组合、hash、隔离安装与最小测试清单。当前不要求安装中间 DLL，也没有游戏通过结论。
