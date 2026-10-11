# 插件 SDK 全面评估与接续计划

日期：2026-10-11。范围：端侧 Host、Common 的客户端扩展基础、内置插件 Contracts、维护插件和 Example。依据当前工作区源码，不等于已发布 SDK 的能力清单。用户明确右键注入只是例子，本次按完整作者流程扩大审查；不把右键 API 设为唯一目标。

## 1. 判断

当前框架已能发现、注册、组合、激活、展示和打包插件，缺口主要在能力边界、生命周期、操作结果和组合式扩展。单纯增加入口会让第三方继续依赖 private UI、原始传输或具体业务对象。

实施方向：保留现有注册、DI、Handler、主题与事务基础；补模块身份绑定、激活状态、失效和结果契约，然后逐个开放业务扩展点。用户最新要求改为广泛开放接入点、先统一规范并用 Example 实验。内置消费者是可选推广证据，不再与 Example 一同作为每项新增 API 的硬前置；见 Plugin-Api-Integration-Standard.md。

这是工程 API 的所有权和使用边界，不是安全沙箱。DLL 插件运行在游戏进程中，模块绑定服务不能保证恶意 DLL 无法直接访问文件或游戏对象；不新增强制签名或权限系统。

## 2. 按作者流程的覆盖矩阵

“已有”只表示找到契约/调用方；“补语义”表示已有入口不足以满足新调用，“新增”仍待实现。

| 环节 | 当前证据 | 评估与行动 | 责任 / 顺序 |
| --- | --- | --- | --- |
| 身份、发现与依赖 | ClientExtensionModule.Compose；PhinixExtensionRegistry 的发现、依赖、激活/停用 | 复用；文档区分编译引用、必需模块和可选业务能力，不靠加载顺序/文件名 | Host/Common，基础 |
| API 发布与解析 | IExtensionApiRegistry.RegisterApi/TryResolve/ResolveAll | 补 provider 身份、状态、单/多 provider 语义、诊断；解析不等于激活 | Host/Common，A1 |
| DI 和所有权 | IClientCompositionFactory / Scope；Borrow 与拥有资源区分 | 已有；示例补部分激活失败与可选依赖，不公开容器作为业务服务 | Host，A1 |
| 生命周期和失效 | Registry Shutdown/UnregisterOwner；模块 lifecycle observer；会话/用户事件 | 补模块任务取消与会话/世界代次；回调执行前验证，已有借用对象停止后不能继续操作 | Host，A1 |
| 事件订阅 | IClientUserEventStream 等普通 event；Shell 按订阅者隔离异常 | 统一异常隔离、线程和可释放订阅习惯；不是强制所有事件改万能事件总线 | Host/插件各自，A1 |
| 主线程工作 | Enqueue void，500 项队列、每帧最多 100 项、溢出丢最旧 | 补可观察拒绝/取消/执行异常与失效；已接受业务动作不能静默丢失 | Host，A1 |
| 资源读 | 已声明资源、摘要/长度校验、模块 Localization | 新增模块绑定只读访问，纹理等 Unity 创建留主线程适配器；关闭时释放自己资源 | Host，A1 |
| 配置读写 | IClientSettingsContext.Get/Set/OnSettingChanged | 补模块作用域、类型/范围、持久化失败与迁移归属；不创建第二配置库 | Host，A2 |
| 插件存储 | IExtensionStorageProvider.GetStoragePath(id,name) | 补绑定、严格逻辑名、原子写/失败结果；现有路径清洗有碰撞可能，不能当隔离授权 | Common/Host adapter，A1/A2 |
| 存档/世界数据 | 环境路径快照；业务各自保存 | 区分全局配置、包资源、存档数据、会话缓存；世界代次/取消优先，通用 Scribe 包装需真实需求再做 | Host/各插件，A1/A4 |
| 主导航、侧栏、横幅、徽标 | MainTab/Sidebar/Banner/Badge provider 与响应式几何 | 复用；补稳定身份与导航意图，作者不保存 ServerTab 私有引用 | Host，A2 |
| 快捷设置与普通设置 | 两种 settings provider | 已有；与模块/设置作用域对齐，先写完整作者示例 | Host，A2 |
| 右键菜单 | Chat 用户/消息、Trade 物品私有 FloatMenu | 新增 typed 插件扩展点与通用动作描述，状态/所有权就绪后实现 | Chat/Trade/Inventory，A2/A3 |
| 工具栏/行内操作/溢出 | ResponsiveToolbarLayout 仅提供几何；各视图自己构造按钮/FloatMenu | 评估 provider 型操作集合，复用动作描述；是否开放每个槽位取决于实际消费场景 | 视图所属插件，A2/A3 |
| 列表展示/详情 | VirtualListLayout；业务列表/详情私有 UI | 可开放只读展示描述、详情入口/附加区域，禁止第三方改业务 backing list；几何缓存版本明确 | 所属插件，A3 |
| 消息扩展内容 | IMessageRenderer / Handler / FrameworkDisplayMessage | 基础协议扩展已存在；补 Chat 级 typed 展示贡献及点击/详情，避免作者构造 UIChatMessage 内部布局 | Chat，A3 |
| 主题注册与公共控件 | IClientTheme、Registry/Catalog、ClientWidgets；T1–T4 | 已有真实消费者；缺键/未知键/失效有既定规则，继续覆盖；不再扩写全局 skin | Host + 同等插件 |
| 本地化 | IClientLocalizationService.ForModule，语言变更和声明资源 | 已有身份绑定范例；推广同一 owner 规则，作者不能写全局语言字典 | Host，复用 |
| 窗口、确认与选择 | IClientWindowService；原生 Window；商店本地包窗口 | 基础窗口已有；补通用确认/选择的取消/失效结果。原生三端文件选择暂不强求 | Host，A2/A4 |
| 通知与进度 | DisplayMessageSink、Banner；商店操作进度 | 补有限通知/进度模型，避免进度等于确认；动作 ID、取消含义与限流明确 | Host/业务插件，A2/A3 |
| 键盘与焦点 | IUiAcceptKeyHandler；Chat 独立输入控制 | 先明确焦点/事件消费再考虑快捷键注册；不得抢其他窗口输入或全局重绑键盘 | Host/所属视图，A4 |
| 用户与会话 | SessionContext/UserDirectory/UserEventStream | 复用；只读当前身份快照、断线/重连失效；用户操作按服务执行，不直接改目录 | Host，A1/A2 |
| 聊天高层操作 | IChatMessageSender / ChatMessageIntent；低层 Framework API 同时公开 | 发送已存在；补消息查询/回复/提及/可用性与派发结果，构包留 adapter | Chat Contracts，A3 |
| 交易高层操作 | facade、快照、事件；Framework API 仍混构包/Handle | 补关联操作 ID/状态、可用性、只读快照；不能把本地更新视为权威确认 | Trade Contracts，A3 |
| 库存操作/codec | Read/Deposit/Extraction/Reservation/Registration；presentation codec | 复用分离接口；模型只读和结果补强、归零后删除聚合 API，不加绕过预留发物入口 | Inventory Contracts，A3 |
| 商店查询/管理 | 内部 ManagedStoreController；Host extension control | 新增 Store Contracts，包状态与模块状态不同；管理必须遵循来源/确认/恢复 | Store Contracts，T5/A3 |
| 主菜单/离线可用 | Host settings/theme entry；可选插件依赖 | 每项定义离线/主菜单/无存档/缺 provider 行为，不以隐藏整个插件替代可用状态 | 各层，A1–A4 |
| 扩展游戏 UI | 当前不提供通用 Gizmo/地图/原版菜单注册 | 按实际场景另评估窄游戏适配器；第三方仍可直接用 Verse/Harmony，无须包装整个 RimWorld | 游戏集成，A4 |
| 领域自定义网络 | Message/Command/Item handler 管线、codec 注册 | 已有；保持管线与序列化限额，新增高层 API 不越过 handler；新的远端协议另开协商工作 | Common/协议 adapter |
| 日志、诊断、关联 | IExtensionLogger/HostLogEntry、Store audit/summary | 复用模块日志；补操作关联、结构化安全摘要，避免每帧刷屏或导出完整消息/凭据 | Host/插件，A1/A2 |
| 打包/侧载/退出 | ManagedPackageTool、ZIP 静态校验、local store；Example | 已有；补新 API 的独立候选与真实消费；声明重启生效，不承诺 DLL 热卸载 | 工具/Store，持续 |
| API 与制品版本 | 当前 API/FileVersion 2.0，CLR 1.9；维护包 1.9 adapter | API/CLR/包/数据版本分别说明；协调编译与交付，普通业务不分支旧版本 | 全部，持续 |

高优先级不是把表中所有能力做成首发阻塞。A1 为公共可靠性基础；A2/A3 按一个真实场景加一个独立作者样例交付，A4 为有需求再设计。

## 3. 当前源码证据与实际风险

### 3.1 已注册对象不是已激活服务

Common/Utils/Framework/FrameworkTypes.cs：ExtensionApiRegistry.TryResolve 返回第一个注册对象，ResolveAll 返回所有注册对象，内部 ApiRegistration 保存 owner ID 但 public 返回值不携带身份/状态。PhinixFrameworkClient.ResolveExtensionApis 直接调用 ResolveAll；Registry 的 ActivateExtensions 另行设置 Active，失败会撤销 owner 注册。

因此“所有 provider 在一切解析阶段都已激活”不成立。正常完成启动后的 UI 可能仅剩可用 provider；注册/激活期间和缓存跨停止的引用仍需不同约束。不能给新菜单/资源/业务 API 写“Resolve 成功即运行”的前提，也不能只加一个布尔 CanUse facade 继续返回所有对象。

需要从同一注册与模块状态权威来源投影身份、契约、状态和诊断，激活阶段允许按依赖关系借用先启动 provider；业务运行端只消费当前活跃 provider。singleton 冲突或未指定的多 provider 不能无声偏爱第一项；集合贡献有稳定排序与独立异常边界。停止移除注册不自动令既有 .NET 对象引用不可调用，服务/短命操作仍要有关闭/代次检查。

### 3.2 当前 Dispatcher 无逐动作结果

Client/Source/Framework/ClientMainThreadDispatcher.cs：Enqueue 返回 void；达到 500 项时 Dequeue 丢弃最旧，再接受新动作。DroppedCount 只给累计量。Drain 逐动作 catch/log 后继续，调用者不能直接知道自己被丢弃、未执行或失败。

新结果型任务服务不能用旧 Enqueue 后立即回报 Accepted，或依靠最后一次全局计数猜自己的结果。需要一份真实队列权威来源、明确拒绝新任务/取消已有任务规则、任务 ID 与终态；后台完成后重新验证 owner/会话/世界代次。旧入口如果暂接续，adapter 转发当前服务，不能保留两套互相丢动作的队列。既有消息/物品回调需要逐消费者迁移，不能在此评估中称已解决恢复问题。

### 3.3 事件隔离不一致（本批已修复 Host 用户事件）

ClientUserEventStream 的 RaiseDisconnected/UsersChanged/DisplayNameChanged/BlockedUsersChanged 直接调用 multicast delegate，一个订阅异常可能阻断后续订阅者。ClientShellEventStream.MainWindowOpened 已按 GetInvocationList 单独隔离。Localization 也有模块拥有的 Localizer，可作为绑定/撤销范例。

本批 ClientEventDispatch 已让这四种用户事件与 MainWindowOpened 共用逐订阅者隔离；日志只含事件/订阅者类型/异常类型，日志失败也不阻断后续。回调仍在原发布线程同步调用，没有新增线程切换或订阅 API。其余插件事件、可释放订阅、模块失效和线程约束仍待逐项迁移。不可订阅者隔离异常不能被误判成事件没发生；回调触发业务仍须有独立结果和日志，不在事件处理里自动重发未知结果动作。

### 3.4 存储路径是便捷能力，不是模块绑定

IExtensionStorageProvider 让调用方传 extensionId/logicalName，当前 FileSystem 实现清洗路径字符、创建目录并返回路径；不绑定实际激活模块。不同无效名称可清洗成相同名称，不同系统无效字符集合也不同。

当前新作者 API 优先模块实例绑定与严格可移植逻辑名，而不是继续暴露写路径并声称隔离。对历史存储名/文件保留读取迁移 adapter；不得直接改变已保存红包 ledger 等路径。包只读资源与可写插件存储分开，存档空间与全局空间明确，失败写入不得报告保存完成。

### 3.5 “只读集合”不等于深不可变

InventoryEntry/InventoryItem/Reservation 等 Contracts 有 public setter 与数组；已有 Clone 会复制 payload 等数据，源码有防御性复制，因此不能据 setter 就声称调用者已经能改库存权威账本。当前 InventoryLedger.Snapshot 已调用 entry.Clone，AvailableSnapshot 也使用复制；其余方法/嵌套引用仍需核对，再定义不可变读取 DTO；把作者修改副本和修改权威状态区分。

同样核对 Chat/Trade 的消息、报价/物品快照。不要为只读 UI 暴露入站 Apply/Handle 接口，也不把 FrameworkPacket 当新的通用业务 DTO。

### 3.6 Host/插件接口还存在归属与语义债务

ITradeRequestApi 属于交易业务却声明在 Host abstractions；ILegacyModuleTransport 与 IFrameworkClientLifecycle.CompatibilityMode 也是现有公开面。Chat/Trade Framework APIs 仍公开构包和入站 Handle*。

分别核对实际 consumers 后迁移：普通交易意图属于 Trade Contracts，Host 不新增第三方专用 business API；原始/模式入口限制在协议 adapter，历史存储 schema 限制在输入 adapter。这些是待修复点，不把现有名字含 Framework 全部判定为旧接口，也不删除维持外部协议所需的 adapter。

## 4. 扩展点的组合方式

按四类能力组织，避免一个接口混合绘制、通信和状态写入：

1. **读取**：不可变/防御性复制的快照、版本/代次、查询筛选，缺少 provider 的明确状态。
2. **操作**：提交用户意图，领域相关结果/关联 ID/取消规则；处理、发送、确认、未知各自精确定义。
3. **贡献**：第三方提供菜单/工具栏/行内操作、详情信息、消息展示或 codec；所属模块控制注册与撤销，消费视图负责组合和异常隔离。
4. **订阅**：通知快照失效或操作终态；线程、排序、拥有者和释放规则明确，不把全部消息变成字符串事件总线。

菜单、工具栏、行内按钮可以复用操作描述和执行边界，但业务上下文继续由所属插件强类型定义。展示阶段不提交业务；点击时再核对 owner、目标和代次。可见性、禁用原因、动作文本、排序有界，第三方不能改原有收据/列表/权威状态，不能把伪装内置 owner 的字符串当注册资格。

窗口内容由第三方自己绘制时可以直接使用公共 ClientWidgets 和 Verse；对于跨插件贡献，优先声明式操作/内容描述。不要为了统一所有 UI 强制泛化整个 RimWorld 绘制，主题也不修改全局 GUI.skin。

## 5. 内置插件的交付边界

| 模块 | 普通第三方应得到的能力 | 留在模块内部/adapter | 第一份实际消费者 |
| --- | --- | --- | --- |
| Chat | 高层发送、消息只读查询/订阅、回复/提及意图、用户/消息操作贡献、显示内容描述 | packet 生成、历史协议、UI 私有缓存/输入控制、入站 Handle | 当前 Chat 菜单/回复/发送与普通示例 |
| Trade | 发起/查看/打开交易、报价/接受/取消意图、关联结果、受限物品操作贡献 | 托管/预留权威、wire token 翻译、确认回执、状态载荷、入站 Handle | 当前交易窗口/Chat 发起交易与独立示例 |
| Inventory | 查询/展示、codec/source 注册、deposit/extract/reserve 的清楚结果和生命周期、条目操作贡献 | ledger 写入、恢复 journal、结果未知核对、权威计数 | 当前库存 UI、交易预留、红包，使用现有分离 API |
| Store | 包只读状态/变更、详情/管理窗口意图、经过确认的管理操作和进度 | 来源批准、payload 锁、receipt/journal、物理文件删除、恢复 | 当前商店与 Host 通用扩展管理；无 Host 反向依赖 |
| 无界面 adapter | 协议和数据翻译所需当前输入/输出端口 | 旧类型、模式/schema、协商、能力降级 | 自身 adapter；无需虚构业务展示能力 |

这些表述不是保证所有接口已实现，不因维护插件已能运行就省略普通作者样例。

## 6. 新的实施顺序

### A0：范围与证据（本批）

完成作者流程覆盖矩阵、源码风险、owner/结果/失效规则和归属判断。检查当前公开面，不从名称推断行为；继续保留接口消费者/adapter 清单。

### A1：可靠性与作者基础

从一份权威注册/生命周期事实提供 provider 状态；补主线程结果、模块/会话代次、Host 事件异常隔离；模块绑定资源读及存储入口边界。借用、拥有、取消、失败、provider 缺失一起交付。Host 用户/Shell 事件隔离已先行实现；其余 A1 代码迁移尚未执行。

### A2：组合式 UI 与用户操作

普通导航/设置示例，通用操作描述/执行机制，选择/确认结果；Chat 用户/消息菜单是其中第一个真实扩展点，同时核对工具栏、行内操作和详情。不为本任务承担后续整体 UI/UX 重构，不因布局将来改变而停止通用契约工作。

### A3：内置业务 API 与第三方组合

逐插件完成读取/操作/贡献/订阅，实际内置调用和独立样例同时迁移；兼容原始接口只留 adapter，归零后删除普通面。未知结果不自动返还或重发，数据/网络外部兼容单独保留。

### A4：有需求再扩展

快捷键、原版游戏 UI 挂点、通用存档辅助、复杂 UI 装饰、多媒体资源缓存等。先给真实场景与线程/所有权证据，再选择窄实现；不作为当前主题首发阻塞。

T5 主题包安装与目录闭环继续原顺序，和 API A1 分切片推进；线上发行延期。没有新增 T6 或自行扩成通用游戏引擎项目。

## 7. 每项新增 API 的完成条件

- 当前契约的类型/线程/生命周期/错误/失效/版本与依赖说明。
- 单一权威来源、可观察拒绝/失败/取消；长期对象借用和短命动作拥有规则。
- 可在 Example/独立插件样例实验，并明确默认行为/未验证项，不依赖 private UI/Host 特例；不要求每个新 API 有内置消费者。
- 必需 provider 缺失明确阻断，可选 provider 缺失保有自身功能；注册/激活/停止不同。
- 类型、DTO、依赖和业务分支不泄漏旧 API/版本/协议；历史读者只在 adapter。
- 新旧制品与 API/CLR 身份边界说明；维护插件 1.9 adapter 暂保留。
- 编译和源码检查记录，游戏验收待用户条件具备时集中进行；未执行的测试不宣称通过。

## 8. 本批结果

本批扩大源码评估并修正后续优先级，先行修复 Host 用户/Shell 事件异常隔离；尚未完成 A1，更没有完成 A2–A4。没有运行自动化或游戏测试、部署或发布。原右键评估仍保留详细设计，但不再代表整个 API 阶段。本评估不会自动删除旧存储数据、修改 dispatcher 或在第三方可见流程增加“兼容开关”。


编译记录：RimWorld 1.6 Release 全客户端解决方案通过，公开 event 与 API 版本保持；保留现有依赖审计/protobuf 目标警告。只执行编译与源码核对，没有执行事件故障注入或游戏测试。T5 存储接续设计见 Theme-Package-Storage-Design.md，尚未实现事务。


## A1 主线程结果接续

本批新增 IClientTaskService / 独立 owned scope / 可观察状态，旧 void adapter 共享同一队列，满队列拒绝新动作不再挤掉已接受动作。Example 1.0.4 是当前实际作者消费者，其他内置消费方尚未迁移；provider 状态/资源/世界代次等基础仍待实施。详见 Client-Task-Api-Implementation.md，旧源码风险段保留为实施前证据。

用户要求这一轮有明确结束点，后续以 UI-Evaluation-Handoff-Checklist.md 为准，达到本轮本地闭环、采用接口与 Example 样例交付条件后告知可开始 UI 评估。A4 的潜在能力不无限扩成本轮前置。


最新实施：按用户纠正已实现通用强类型操作接入点与 Example 1.0.5 实验；不等待内置逐项消费。详见 Plugin-Api-Integration-Standard.md。
