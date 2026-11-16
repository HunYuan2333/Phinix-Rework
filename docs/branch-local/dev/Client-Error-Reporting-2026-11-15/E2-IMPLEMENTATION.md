# E2：共享加载层与公共回调错误出口

日期：2026-11-15。分支：客户端及其实际 Common 子模块均为 `codex/client-error-reporting`。本轮完成实现和定向验证；没有提交、推送、发布或混入第三方 vendor 变更。

本轮验证见 `verification-e2.json`。`IMPLEMENTATION.md`、`verification.json` 保留为 E0/E1 的历史记录，不代表最新源码的验证状态。

## 已接入的路径

| 范围 | 变化 | 保留的处置语义 |
|---|---|---|
| Shared registry | 构造、Register、Activate、Shutdown、注册撤销及清理直接传递 Exception；部分类型加载保留 LoaderExceptions | 无关模块继续；依赖失败阻断其消费者；原异常与清理异常分开 |
| 依赖图、身份与排序 | 元数据失败可查；缓存图中的类型身份和注册时验证过的模块身份；Priority 在参与者边界内读取 | 无效属性不会反复构造并终止整个发现；异常 getter 不阻断其他提供者 |
| Owned loader | 候选、加载、解析和启动/清理故障进入结构化出口 | 仍只加载原先拥有的候选/请求者，失败继续原有隔离或原样重抛 |
| Shared 日志出口 | 独立调用订阅者；坏订阅者的异常作为 DiagnosticObserverFailed 送到健康出口；无可用结构化出口时保留完整文本回退 | 日志异常不覆盖业务异常；坏出口不重复调用；递归最多八层并计数 |
| Lifetime / Composition | 回调、取消、释放、构造失败进入统一记录；构造失败先记录，再防护清理 | 清理继续，原异常不被替换；借用资源仍不由消费者释放 |
| 贡献点 / Presentation | 工厂及惰性枚举、context、执行、绘制、图片和释放保留原异常；Open/Collect 清理有独立记录 | 已执行失败仍是 Invoked/Failed，不授予再次执行或恢复许可 |
| 用户、shell、消息、模式和日志事件 | 逐订阅者隔离，上报完整异常；静态订阅者按方法声明程序集归属 | 一个订阅者不能阻断后续订阅者；消息已经入库不会因观察者失败再次投递 |
| 本地化 / 窗口 / 链接 | 绑定、格式、listener、开窗和浏览器异常进入 Host；敏感边界保留栈而不输出任意原文 | 保留 fallback、主线程约束和原始重抛；浏览器最多一次外部回退 |
| Framework 管道 | CanHandle / CanRender、运行时 Priority、handler、renderer、interceptor、设置元数据及观察者都在边界记录 | 已开始处理的出站 handler 异常仍重抛，不尝试另一个 handler 或重复发送 |
| 通用 item 解码 helper | 原始解析异常作为 WARNING 记录 | 坏 payload 仍被拒绝；不触发自动错误弹窗，不采集正文 |

## 合同和依赖

- 优先复用 `HostLogEntry.Exception`、`StructuredLog`；没有为客户端复制另一套共享错误对象体系。
- `LoadOwnedAssemblies`、`ScanCandidateModuleTypes`、`ExtensionDependencyGraph.Build`、`CollectCapabilities`、`TryExtractItemPayload` 保留旧签名，新增可传递诊断的重载。
- `IExtensionLogger`、既有 TaskSnapshot/业务 Result 的公开签名不变。Host 内部组件使用可选的 typed fault callback；内置与第三方经过相同的注册、调用和记录路径。
- 仅能按程序集确定来源时，报告保留程序集名；同一程序集存在多个模块时不猜测某个模块。已知模块身份时保留模块 ID 和可得版本。
- Common 当前仍是基于原精确 HEAD 的本地工作区修改，父仓库 gitlink 未推进。发行前必须提交共享层并固定客户端引用，再联合出包；不能只推送客户端或把未固定的 Common 当作可重现发行基线。

## 保真、隐私与级别

- 正常路径在转换摘要、Failed 或用户提示之前保存 Exception；反射、Aggregate、inner 原因树及原始方法调用帧沿用 E1 的有界快照。
- 状态摘要使用固定阶段/异常类型，避免异常自身的 Message getter 再抛异常而遮住根因。详细原因仍在错误编号对应的快照中。
- WARNING 保持 WARNING，同时保留可复制详情；统一出口不会将可恢复警告统一提升为 ERROR。相同错误可以提升严重级别并请求一次提醒，但保持相同编号。
- 浏览器和用户事件沿用其敏感边界要求：原因类型、树与帧保留，任意异常消息省略并明确标记。不会通过简单输出 ToString 把链接查询、事件用户内容或原生异常原文带进报告。
- Shared 日志循环的抑制计数进入复制报告。所有出口损坏时只能尽力输出到平台 Trace；不承诺在平台本身无法工作时仍能弹窗，更不会因此重试业务。
- 旧静态 `LoadAssemblies` 当前仍使用完整文本日志，保留两次加载失败的完整异常。当前 Host 使用新的 owned/structured 路径；不宣称旧字符串 API 自动获得弹窗或能还原第三方提前吞掉的异常。

## 验证与限制

- 两套运行时的生产源白盒套件验证错误记录、通知、窗口状态、队列、Lifetime、事件、贡献点、敏感消息、警告级别及日志自身失败。
- 真实 Common SDK 与已编译 Host 的独立测试验证 constructor/Register/Activate/Shutdown、依赖隔离、元数据、坏 DLL、LoaderExceptions、原始调用帧、静态观察者归属、CanHandle/Priority/interceptor、消息去重、本地化、浏览器回退和呈现 Fail 出口。使用纯数据/最小 lifetime，未初始化真实游戏会话。
- 组合与资源清理的既有 net472/Mono 套件通过。新增断言检查“构造拒绝只报告一次并保留栈”，保留清理顺序、借用所有权及仅释放一次等原断言。
- 1.6 Host/内置图与 1.7 的 28 项目联合图编译通过；后者包含维护中的红包和人才交易独立插件。不是实际游戏 UI 或发布产物验收。
- net472 反射游戏引用时出现缺少 Unity.Burst 自定义属性构造器的提示；独立断言仍通过。需要完整、版本对应的游戏运行环境做后续验收，不能混用另一版 DLL 来消除提示。

| 最终检查 | 结果 |
|---|---|
| 生产源白盒：net10.0 | 64 项通过 |
| 生产源白盒：net472/Mono | 64 项通过 |
| 真实 Shared/SDK/Host 边界：net10.0 | 30 项通过 |
| 真实 Shared/SDK/Host 边界：net472/Mono | 30 项通过，含上述游戏引用提示 |
| 既有组合/资源所有权套件：net472/Mono | 66 项通过 |
| 最新 1.6 Host/内置图重建 | 通过，零错误 |
| 最新 1.7 / 28 项目重建 | 通过，零错误 |

未通过且需后续更新的旧夹具：

1. `PresentationRuntimeTests` 的库存替身缺少 `InventorySaveIdentityPatch`、`StorageNotice`、`RetryDatabase`、放弃操作等当前库存接口，无法编译。库存产品源码与评估基线一致，本轮没有修改库存行为；本轮没有为了让该套件通过而编造库存结果。E4 更新夹具后再运行完整呈现回归。
2. `Phase35ClientRuntimeTests` 的源编译清单缺少当前 `IClientTransport` 合同，无法编译。对应 NetClient 产品源码也未在本轮修改；E3 接网络层时同步修复该测试清单。
3. 实际游戏中的窗口绘制、剪贴板、异常交给 RimWorld 的最终处理及发布安装布局仍待验收。独立边界测试不能替代这些检查。

## 迁移登记

`migration-decisions.json` 固定评估清单指纹，逐条登记判断；生成脚本拒绝将这些决定应用到不同基线。当前 2,343 个基线位置中，72 个已实施、17 个已有保留/旧文本路径的审阅结论，其余明确保持待审阅。本轮新增的边界按本表和测试登记，不计入修改前的基线位置数。

E3 明确仍需处理 Framework 的 packet/control 解析和发送结果、网络/认证、SQLite 与设置路径；E4 完成五个内置插件迁移，E5 完成指南、精确 pin 和发行验收。E2 完成不表示“全部 catch 都已迁移”。
