# Rework Client 错误处理、可观测性与恢复机制评估

日期：2026-11-15。结论：值得建设一套自主定义、内置与第三方插件平等使用的故障机制。现有基础可以复用，但当前实现不满足“故障被隔离后仍保留原始堆栈和原因链”。只增加一个自定义 Exception 基类或替换日志格式无法解决。

本次仅增加评估资料，不修改产品代码、共享层版本或插件合同。

> 2026-11-15 用户后续决定：首版优先“统一错误窗口、一键复制错误详情与堆栈、按原边界保栈传播”，不以通用恢复策略系统为前置条件。具体阶段和验收以[统一错误窗口 Roadmap](../../dev/Client-Error-Reporting-Roadmap-2026-11-15.md)为准；下文第 4–7 节保留为原始架构评估，其中较宽的首版恢复建议已由该路线图收敛。

> 实施进度：[E0 / E1 首切片记录](../../dev/Client-Error-Reporting-2026-11-15/IMPLEMENTATION.md)。本文和 source-inventory 保留为修改前的评估基线；当前生产 dispatcher 的故障记录行为已改进，不应再用原来的缺口探针断言作为修复后的通过标准。

## 1. 基线、范围与可信度

- 主仓库：`Phinix-Rework`，HEAD `939e8e2c27e70432875dc2be306856b5a6621ea2`，以**当前含未提交修改的工作树**为准，尤其包含 SQLite、Inventory、Trade 和 dispatcher 的进行中修改。不是对纯 HEAD 或发布包的审计。
- 实际引用的共享目录：`Dependencies/Phinix.Common`，HEAD `c165f61d749189f700ca896d63d9366d5c71dbf7`。没有用旁边独立的 `Phinix-Rework-Common` 替代它，也没有更新 gitlink。
- 阅读了仓库 AGENTS、Design-Philosophy、Compatibility-Boundaries；其中“游戏 Mod 可靠性”规则优先：保护正常可用性、基本事务和结果语义，避免无边界自动恢复。
- 客户端现有工程为 net472；双运行时构建工具另生成 net10.0 图。About.xml 当前列 1.6。新诊断合同必须兼顾这两套构建，不能据迁移工具存在就宣称 1.7 游戏验收完成。
- 主范围：`Client/**` 和五个内置扩展的手写 C#，包括 Contracts、GameAdapter、LegacyAdapter、PluginStore。补查共享 Common 手写源码中的加载、注册、序列化、网络、托管包管理等路径。
- 排除生成 Proto/compiled、bin/obj、第三方 vendor、历史副本和发布产物；工具/样例单列。未审计所有第三方插件自身源码。未来 Host 能覆盖第三方经过的公共边界，但不能恢复第三方在内部提前丢掉的异常，也不能拦截任意自行创建的后台线程。

静态清单（注释/字符串屏蔽后的词法统计，包含条件编译两侧；不是语法树或构建可达性证明）：

| 范围 | 源文件 | catch 位置 | throw 位置 |
|---|---:|---:|---:|
| Host，含客户端网络/认证、合同、呈现、SQLite | 143 | 159 | 409 |
| Chat | 24 | 23 | 39 |
| Inventory | 24 | 50 | 74 |
| Trade | 52 | 65 | 224 |
| LegacyAdapter | 5 | 23 | 31 |
| PluginStore | 49 | 100 | 479 |
| **Host + 内置扩展合计** | **297** | **420** | **1256** |
| Shared Common 补充范围，包含双端公用逻辑 | 65 | 108 | 514 |
| 工具/样例补充范围 | 6 | 1 | 44 |
| 全部 | 368 | 529 | 1814 |

已逐模块浏览捕获块及关键下游 Report/Fault/Result/日志出口，重点追踪加载、调用、主线程交接、持久化和恢复链路；所有 throw 位置进入清单，并检查具名重抛、换异常不保留 inner、清理覆盖原故障等模式。这里的数量是**位置数，不是漏洞数**；不表示每个 throw 都需要改成自定义异常，也不表示已动态覆盖所有路径。

附件：`source-inventory.json` 保留各位置的源码、行号和文件指纹，`source-inventory.tsv` 用于逐项迁移登记；`inventory.py` 可重新生成。review_hints 只是检索提示，不是自动裁决。

## 2. 可以保留和扩展的基础

1. `FrameworkTypes.cs:461-509` 已有 `HostLogEntry`、`IExtensionLogger`、ExtensionId、CorrelationId 和 Exception；`ToDisplayMessage()` 使用完整异常文本。问题在于许多调用者在到达这里之前就只剩字符串。
2. `LogEventArgs.cs` 只有 Message/LogLevel，老链路依赖字符串传递；用 `+ ex` 的现有路径能保留文本堆栈，不应一律判定为吞栈，但无法作为结构化故障查询。
3. `PhinixExtensionRegistry` 已有依赖排序、部分初始化清理、注册撤销、逆序停止与模块状态。统一故障上报应嵌入这些边界。
4. Task、Lifetime、Presentation、贡献点已有 owner/scope 与局部隔离；主线程队列有 500 容量、每帧 100 上限。可绑定故障归属，不能另建一套不受生命周期管理的执行系统。
5. `ClientDatabaseException` 已保留 InnerException，并区分 Busy/Corrupt/OutcomeUnknown 等；Trade 的 NotSent 包装、部分激活失败 AggregateException、清理失败收集也是可复用实现。
6. Chat 图片下载已有并发、队列与重试上限；Trade 有任务驱动的恢复泵、业务日志和接收/交付记录；PluginStore 有取消、请求 ID、事务和提交后刷新失败区分。不要把这些业务机制搬进 Host。
7. 现有扩展日志有 300 条有界缓存，部分 UI 故障有去重。可以扩展为诊断索引，但当前不能按故障 ID 查看完整记录和恢复历程。

## 3. 已确认的主要缺口与证据

以下优先级针对本项目实施顺序，不是安全漏洞评级。

### A. P1：共享加载/生命周期入口已经丢栈

- `Dependencies/Phinix.Common/Common/Utils/Framework/PhinixExtensionRegistry.cs:162,216,413,426,480,516`：构造、注册、清理、激活和停止异常主要转成 `exception.Message` 写入 StateDetail/Warnings。即便 Host 后续记录所有 warning，也拿不回原栈。
- 同文件 `:662`：`ReflectionTypeLoadException` 返回非空 Types，但没有保留 `LoaderExceptions`，缺失依赖或类型加载失败的重要原因被丢掉。
- 同文件 `:634,648,666,708` 和 `FrameworkSerialization.cs:121,136`：使用 Debug.WriteLine，且只写 Message；不是可靠的 Release 诊断路径。
- `ExtensionAssemblyLoader.cs:47,75,98,196,212,232,252,282`：大量加载/探测错误只剩 Message；LoadFrom 与 Load 双失败只拼两条消息，失去两条原始原因链。

**影响**：第三方在入口阶段失败时，调用方不能补救诊断。完整落地需要共享 Common 的正式变更及客户端 pin 更新，不能只改 Host，也不能直接改 vendored protobuf。

### B. P1：公共运行时隔离保住执行，丢掉了诊断

- `Client/Source/Framework/ClientMainThreadDispatcher.cs:127-134`：任务标 Failed/CallbackFailed，日志只有异常类型，snapshot 没有诊断引用。
- `ClientLifetimeService.cs:38-42`：只记类型；按 owner/stage/type 的去重集合到 256 后直接停止记录新故障，没有抑制计数、摘要或明确重置。
- `ClientExtensionPointService.cs:123-124`：Collect/Context/Execute 的 Report 只保留类型；ActionExecutionResult 只有笼统 code。
- `Client/Source/UI/ClientPresentationService.cs:179-183,1070`：呈现和图像故障只记类型，并有局部次数限制。故障本身被隔离不代表可以丢栈。
- `ClientUserEventStream.cs:29-38`：事件订阅者故障有 owner，但故意不输出任意消息的实现同时删除了堆栈。可保留栈并单独处理敏感消息。
- `ClientLocalizationService.cs:148,160`、`ClientLinkService.cs:44,52` 等把错误缩成固定码/类型，缺少详细诊断关联。

**影响**：内置与第三方共同遇到的 callback 故障无法定位调用点；用户只能看到功能失败，开发者无法沿同一事件追踪。

### C. P1：发送失败存在“记录后正常返回”，错误策略不能以此判定成功

- `PhinixFrameworkClient.cs:1045-1088` 的 `sendPacket`：断线时告警并 return，发送异常只记录 `ex.Message` 后 return，协商拒绝时也 return。`TryHandleOutgoingCommand` 等路径随后可返回“已处理”。已处理不等于已发送，更不等于远端确认。
- `TrySubmitMessage` 的 `:361` 虽记录完整异常文本，但返回 false 混合了无 handler、失败和可能已交接的情形。
- 对照 `Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs:245-247`：本地准备异常包装为 `TradeRequestNotSentException`，`dispatch()` 放在其外，明确后续异常可能为 Unknown。这一语义应保留。

**建议**：发送结果提供明确的本地事实，如 NotSent/Submitted/Unknown，并关联 ErrorId；权威 ACK 继续由业务合同判断。不要仅依据 Exception 类型推断未发送，不要在统一 handler 中重发交易、物品或退款。

### D. P1：业务与存储层存在二次压缩原因链

- `InventoryDatabaseStore.cs:70-77`：数据库分类错误及一般错误都变为 LastError 字符串并返回 false；详细异常在此丢失。
- `BuiltInInventoryClientExtension.cs:310,317,331`：迁移/打开失败部分只显示通知、Message 或类型；不是所有 Inventory 路径都如此，Deposit、Reservation、Materialize 等不少分支已经写完整异常文本。
- `Extensions/Trade/Client/TradeSqliteRecords.cs:112-116`：`Quarantine` 用 `throw error` 抛传入的数据库异常，会重设该次抛出位置；其余隔离只存类型并告警类型。调用者 `TradeCompletionInbox.cs:104`、`TradeIntentJournal.cs:146` 的原始错误在隔离记录后不能完整追溯。具名抛出不能机械替换成 `throw;`，因为这里位于独立方法中；应调整调用方重抛或在确需跨方法传播时保留异常派发信息。
- `TradeOfferOperations.cs:74-79,123,132,136`：未发送、未知结果、本地结算及清理异常大量转为 Message/类型。
- `TradeDeliveryReceipts.cs:202`、`TradeDeliveryRecovery.cs:60`、`TradeIntentSettlementCoordinator.cs:48`、`TradeRecoveryPump.cs:63`：恢复或 ACK 传输异常只记类型。
- `TradeInventoryIntegration.cs:32,128,176`、`TradeItemConverter.cs:112,151,217`、`InventoryDropDelivery.cs:34,61,92`：部分 codec/物理交付失败转成 false/UnknownItem/Reason/类型，原栈未被保留。应保留现有全批拒绝、custody 与 Uncertain 判断，补齐独立诊断。
- `InventoryJournal.cs:111,260,310,332` 及兼容历史链路有 Fault/Reject/Report，但主传递内容仍是摘要字符串。旧迁移/兼容路径也在清单内，不能只迁移当前 SQLite 热路径。

### E. P1：认证与传输诊断过度收缩，取消过滤范围过宽

- `PasswordlessIdentityClient.cs:160,207`：认证/续期错误只调用固定文本 Fail，原异常丢失。
- `ServerIdentityCredentials.cs:185-187`：捕获 Json/Argument/Format 后创建新 InvalidDataException，不传 inner。`TlsNetClient.cs:43` 的 pin 解析也有类似问题。
- `TlsNetClient.cs:55`：Report 只有类型；`:127,132` 在 Stop 已取消时捕获任意 Exception，可能把同时发生的非取消错误当正常关闭忽略。应以实际 token/阶段区分预期取消和关闭期间的其他故障。
- `Client/Common/Connections.Client/NetClient.cs:300-309`：poll 线程入口无最终故障边界。PollEvents 及其调用链若抛出未被内部捕获的异常，可能逃出工作线程；这是静态发现的风险，未在游戏中触发验证。

认证错误不能改为把 secret、token、证书细节或完整响应正文直接输出。原因链和调用栈需在受控诊断中保留，展示/导出脱敏分别处理。

### F. P2：内置插件的覆盖不一致

- Chat：`BuiltInChatClientExtension.cs:218`、`PhinixFrameworkChatService.cs:104`、`ChatMessageList.cs:143-144,1064,1083`，分别存在只写 Message、直接 Unsupported、图片回调摘要等情况。图片下载队列本身可传 Exception，但注入出口又把它降为 Message。
- LegacyAdapter：`LegacyChatProtocolAdapter.cs:86,127,145,157` 只写 Message；相对而言 LegacyTrade 多处使用完整异常和带 inner 的 Unknown 包装，不能一起重写成笼统失败。
- PluginStore：`RepositoryDiagnostics.cs:11,37-44` 明确设计为不包含 exception stack 的固定字段事件；Failure 虽接收 Exception，但只留下异常类型、inner 类型、code/context。它是有价值的脱敏审计流，却不能独自承担开发故障诊断。
- Store 的 `ThemeStoreController.cs:81`、`LocalPackageFileWindow.cs:56,78`、`LocalThemePackagesWindow.cs:42` 等还存在只展示 code/message 的分支。缓存读取返回 miss、HTTP 错误体降级、超时后观察任务等应有轻量诊断/计数，不宜全提升为 Error。
- Host managed runtime 的详细错误在 `Client.cs:418,440,449` 及 `ManagedExtensionManagerView.cs:16` 受 DevMode 限制。正常玩家发生故障时，之后打开 DevMode 无法找回先前被丢弃的异常。
- 主题与昵称：`UiTheme.cs:152,174,186,280,351`、`NicknameEditState.cs:39,46`、`ThemePackageExportWindow.cs:60,63`、`ThemeSettingsWindow.cs:138` 等用摘要/提示替代诊断。可以维持友好提示，但要绑定故障记录。

### G. P2：清理异常与日志异常可能遮住主故障，部分探测回调未纳入隔离

- 不少 `catch { Dispose/Close/Release(); throw; }` 本身是合理补偿；但补偿若再抛异常，后面的 bare rethrow 不会执行。例：`ClientExtensionRuntime.cs:47-51`、`ClientCompositionFactory.cs:47-51`、多个 presentation 构造/发布清理分支。需要按清理实现核验，不应把每个 bare catch 都自动判为吞栈。
- 现有 BuiltInTrade/LegacyAdapter/Store 激活失败使用 AggregateException 保留主异常及 cleanup 异常，是可推广模式；生命周期 observer 的停止聚合也应保留。
- Registry Register 失败时 rollback、部分诊断 callback 未统一防护；异常路径中先 Log 再 throw 的实现，若 Log 本身抛出，也可能覆盖原始错误。
- `PhinixFrameworkClient.cs:255,407,796,835,878,933` 的部分 CanHandle/CanRender 在 handler 的 try 外执行。CanHandleOutgoingItem 已有单独隔离，两者不一致。候选筛选、Priority/元数据属性、枚举器也要纳入调用边界审查。
- `RaiseLogEntry` (`:28-35`) 直接调用 multicast event；`ExtensionLogger.Log` 直接调用 sink。单个订阅者异常可能影响后续订阅者或反向影响业务。日志系统要有自身的隔离与递归保护。

## 4. 推荐的最小体系

### 4.1 一个故障记录中心，两个输入方式

共享层放置无游戏依赖的稳定故障描述和采集入口；客户端 Host 实现记录、查询、策略执行与界面。通过已有 `ExtensionHostContext` 服务注册和模块生命周期绑定提供给插件。具体命名在实施时确定，以下为建议：

- `ClientFaultException`：只在异常传播确有意义时使用，携带命名空间化 Code、操作/阶段和 InnerException。正常拒绝、能力缺失、取消仍可用现有 Result；不要求所有 BCL 异常继承它。
- `IClientFaultReporter` / `ForModule(module)`：捕获已有异常或报告无异常的业务失败；在来源处生成 ErrorId，返回可查询引用。内置与第三方用相同注册、归属、额度和降级规则。
- `FaultRecord`：ErrorId、UTC/单调耗时、OwnerId/版本、Code、Category、Boundary/Operation、Severity、CorrelationId/父故障、task/activation/game scope、Outcome、恢复状态、异常树和调用栈。默认不附带 packet、SQL 参数、存档对象或任意对象 dump。
- `FaultReference`：允许任务/动作/结果/UI 关联 ErrorId；不把原始 Exception 暴露给任意插件，不在公共 snapshot 保留游戏对象。

Host 自动记录未知第三方异常为标准 CallbackFailed 等 code，保留其真实类型和栈；插件可声明自己的错误 code、用户提示、本地恢复动作及策略。第三方完全不接新接口时，仍覆盖 Host 已包裹的入口；其内部静默失败需要插件自身迁移。

### 4.2 堆栈保留规则

1. 同一 catch 重抛用 bare throw；更换异常必须保留 inner；跨方法/异步重新派发使用保留派发信息的方式。不得以 throw variable、仅存 Message、仅存类型替代。
2. 将异常转换为 Result、UnknownItem、false、用户提示之前，先保存故障记录；Result 携带 ErrorId。若下层已报告则复用同一事件，不每层重复打印同一堆栈。
3. 保存完整的 inner/aggregate 树；ReflectionTypeLoadException 还要保存 LoaderExceptions。清理失败是关联次级故障，不能替换主故障。
4. 在边界及时冻结异常信息，避免长期强引用 Exception.Data、插件实例、闭包或 Game。仅短期确需重新抛出时保留派发对象，生命周期结束释放。
5. “保留栈”和“将原始异常文本公开打印”分开。受控本地诊断保留调用帧与原因树，消息/路径按策略处理；玩家提示只显示安全文本与 ErrorId，导出进行脱敏。敏感字段过滤不能删除整个堆栈。
6. 正常取消可不输出警告；仍保留有界取消事实。预期输入校验/缓存 miss 可以低级别或聚合采样。真正捕获到的异常按契约记录首次栈及计数，不能无痕消失。
7. 重复同一指纹错误保留首条及最近样本、累计次数、首次/末次时间、抑制数量；到达全局容量时有明确 dropped 指标与摘要。不能为了“不吞栈”承诺无限保存每次相同异常。

### 4.3 可自定义，但恢复权限归原业务

建议将 handler 输出限制为处置计划（建议动作/恢复能力），由 Host 按边界约束执行，而不是允许任意错误处理器修改全局状态或简单“返回 handled 就算成功”。

| 故障/事实 | 默认处置 | 恢复条件 |
|---|---|---|
| 正常取消、scope 已停止 | 取消，清理，保留必要事实 | 不自动恢复过期 scope |
| 输入无效、协议不匹配、缺能力 | 可理解地拒绝 | 修改输入/升级/重新连接 |
| 只读下载、刷新等暂态失败 | 有上限的重试/人工重试 | 操作明确无副作用，deadline/次数/退避受控 |
| 任务 QueueFull、尚未执行 | Rejected/NotStarted | 队列释放后按原有效 scope 重新提交 |
| SQLite Busy、事务未提交 | 仅 owner 可申请有限重试 | 已证明无外部副作用，重新读取 revision |
| 插件 callback 异常 | 隔离本次，必要时降级该能力 | 当前插件/activation/Game 仍有效，无重复提交 |
| 已交接传输/游戏对象，结果未知 | Unknown/需核对，保留证据 | 原业务查询、已有 receipt 或显式人工处置 |
| 数据损坏、迁移失败 | 隔离受影响记录/能力，保留数据 | owner 提供修复/重新打开/导出诊断 |
| 关闭清理失败 | 尽力继续释放，记录次级故障 | 默认不自动重激活 |

恢复计划至少校验 owner、原 activation/Game/scope、操作是否已开始、幂等性依据、次数与截止时间、同一操作是否正在恢复。game/ledger 修改通过现有主线程任务服务；后台 IO 不放在主线程 handler 内执行。用户关闭窗口不消除 Unknown，handler 成功返回也不创造远端 ACK。

运行时模块 Stop 是终态；启用/禁用仍有重启要求。因此第一版提供重试当前安全操作、重新打开视图、刷新/重连和重启提示，不承诺插件热重载。熔断优先隔离具体能力/操作，不长期冻结无关库存或全客户端。

### 4.4 诊断服务自身也要可失败

- 先写入有界本地记录，再扇出到现有 Host log/UI；每个 sink、observer、custom handler 单独隔离。
- Report 必须线程安全、避免持业务锁调用第三方 handler；恢复动作离开记录锁执行。
- handler/sink 出错以关联次级故障记入独立保底槽，设重入保护，不递归调用同一坏 handler；保底失败保留计数。不可因记录错误导致业务重复执行。
- 首版使用有界内存故障索引、现有日志桥接和手动脱敏导出即可；需要跨重启近期错误时再加有界轮转文件。诊断写入不要依赖可能正故障的业务 SQLite。
- 增加模块状态、fault/error rate、队列拒绝、抑制次数、恢复尝试/结果和操作耗时；不是每 Tick 写日志。不需要首版引入远端采集、重型遥测 SDK 或全局异常“继续运行”机制。
- 扩展管理界面展示故障详情、影响范围、ErrorId、安全动作、恢复中/已恢复/需核对。已恢复必须由执行结果/owner 事实确认；不因为提示消失就算恢复。

## 5. 实施切片与边界

| 切片 | 内容 | 完成标准 |
|---|---|---|
| 1：保真与根因修复 | 共享 loader/registry 详细上报；修复 throw error、无 inner 包装；清理/日志不遮主异常 | 构造/注册/激活/关闭失败保留原栈和全部次级原因，正常玩家也有记录 |
| 2：中立采集合同与 Host 服务 | 故障描述、模块 scope、ErrorId、有界记录、已有 logger 适配 | 内置/第三方相同路径；sink 坏掉也可查原故障；不改业务权威 |
| 3：覆盖公共调用边界 | task/lifetime/event/contribution/presentation/localization/network；CanHandle/枚举/属性探测 | 一个参与者失败不阻断无关参与者；snapshot 能关联详细故障 |
| 4：逐模块迁移 | 五个内置插件、认证、主题/昵称、SQLite/历史迁移，按清单闭合 | 每个捕获点有明确处置：传播、转换并关联、预期取消/拒绝、降级、清理或保底 |
| 5：安全恢复与用户诊断 | 插件自定义 code/handler、限定恢复动作、查询/导出、计数/限流摘要 | 安全操作可重试；Unknown 不盲重放；失效 scope 不复活 |
| 6：验收与插件指南 | 故障注入、两套构建与第三方样例、文档约束 | 入口到提示到诊断到恢复都可追踪，发布产物含所需诊断能力 |

切片 1/2 可作为同一垂直切片推进，先让 registry 的真实异常能进入记录中心，避免先设计一套无人调用的大接口。共享变更须先在 Common 仓库形成可审阅实现，再由客户端固定引用；不能通过临时改 vendor 或旁路 SDK 分发。

新增中立服务可通过 TryGetService 渐进接入；若修改公开 TaskSnapshot/Result 或迁移合同所在程序集，应单独处理 ABI、精确 host pin、联合插件构建与包范围。不能默认为 API 3.0 迁移已授权任意后续 ABI 破坏。

## 6. 验证计划与本次已执行验证

必须覆盖：

1. 原始调用帧、inner/aggregate、多 LoaderExceptions 在转换/重抛后可查询；清理再次失败不会覆盖根故障。
2. 内置与第三方样例分别在构造/Register/Activate/CanHandle/Handle/Render/event/Dispose 处抛错：无关插件继续，失败与依赖状态正确。
3. 主线程任务 Failed 能关联 ErrorId；scope dispose/模块停止释放回调；QueueFull 是尚未执行而不是业务失败。
4. 一个 logger/observer/handler 抛错，其他出口继续；重入、全部出口失败和容量饱和不递归、不改变业务结果，并留下计数/保底证据。
5. 取消、超时、读取失败、认证失败、数据库 Busy/Corrupt/OutcomeUnknown 分开；关闭期间真正非取消异常仍有记录。
6. NotSent、交接后异常、成功 ACK、提交后刷新失败分别注入：没有二次发送/物化/退款，日志不能将 Unknown 改为成功。
7. 恢复动作重复点击、插件停止、切存档、断线/换会话、deadline 到期，旧 scope 永远不能写新 Game。
8. 重复故障不刷屏但有次数和抑制摘要；普通 Release 用户也能拿到此前故障的栈。凭据、payload、SQL 参数、完整存档/路径不出现在共享导出中。
9. net472/net10 构建与实际游戏中的程序集加载、主线程 UI；有栈不代表一定有源码行号，需核验符号文件构建/分发策略。

本次完成：静态源码清单生成、上述边界追踪；另外用 .NET SDK 10.0.114 编译**生产 dispatcher 源码和生产 Task 合同**，以最小替身补足外围接口，执行了 4 项白盒检查，全部通过：

- 失败任务被标 Failed，后续任务仍执行；
- 确认其当前日志没有原始 callback 帧及原始 cause，仅有异常类型；
- 确认 TaskSnapshot 没有 ErrorId 或 Exception 引用；
- 日志出口抛错不会阻止后续任务，但诊断消失。

这证明的是当前缺口，不是新机制已经实现或通过。探针材料位于 `probes/`。本次没有运行完整解决方案、现有全部测试、两版游戏或真实第三方包故障注入；没有宣称这些已通过。

## 7. 建议决策

按上述切片建设自主定义的小型故障合同和 Host 服务，先保证原始故障保真，再接安全恢复。默认本地诊断、玩家可理解提示、插件自行声明业务恢复能力；首版不做通用自动重启插件、全操作重试或远端遥测。

当前没有必须向用户补问才能完成本次评估的阻塞项。后续实现时，日志保留额度、是否需要跨重启轮转、具体 UI 位置可按实际内存/支持需求确定；业务协议、存档/权威或公开 ABI 发生变化时另列明确契约。
