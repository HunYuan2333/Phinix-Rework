# R0–R4 完成记录

日期为 2026-11-09。R0–R4 的实现、组件验证与示例已完成。R5 白盒新增测试已经编写并执行，黑盒测试与人工验收由用户执行，步骤见 [R5 测试与验收交接](Command-Capability-Relay-R5-Tests.md)。

## 源码基线与共享交付方式

[关键源码基线](Command-Capability-Relay-Source-Baseline.json) 记录两端主仓库和两个 Common 工作副本的 HEAD、分支、工作树状态摘要与本次关键源文件 SHA-256。两端均位于 codex/command-capability-relay。基线使用实际工作树内容，包含已授权保留的先前修改。

两个 Common 基线分别为客户端 c165f61d749189f700ca896d63d9366d5c71dbf7、服务端 67f243d9edced77dc3fe669f8a435b1f14b59199。两个本地对象库未同时具备对方提交，比较采用各自 git ls-files 和实际文件内容。差异主要涉及 ManagedExtensions、主题和 FrameworkTypes 的既有版本演进，文件清单已写入基线。

本次新增 CommandRelay.cs、ClientCommandRelay.cs、PinnedUdpReceiveBudget.cs，以及修改后的 FrameworkPacket.cs、PhinixExtensionRegistry.cs，在两端内容一致。FrameworkTypes.cs 保留各自既有差异，均增加相同的 ServerFrameworkContext.CommandRelayResult 属性。

R0 已确定共享交付方式。R5 以基线和包指纹确定候选，将 Relay 改动形成共享提交，审查既有 Common 差异后更新两端精确 gitlink。当前实现采用两份工作树中的一致协议定义，正式共享提交与消费者引用更新继续作为 R5 原定交付项。现有用户修改保持在工作树。

## 旧业务完整调用链核对

| 链路 | 已核对入口、状态所有者与反馈 |
| --- | --- |
| 普通 Chat | PhinixFrameworkChatService 构建消息，经客户端 Message Handler 出站，服务端 BuiltInChatServerExtension 及聊天服务保存历史并广播，客户端消息 Handler 处理。继续使用 Message Pipeline |
| Chat 历史 | 客户端历史请求通过 TryHandleOutgoingCommand 和 ChatCommandHandler，服务端 BuiltInChatServerExtension 的 Command 默认 Handler 读取 GetHistory，再发送历史及 BuildHistorySyncCompletePacket |
| Trade 创建与状态 | FrameworkClientTradeServiceAdapter 调用交易服务构造请求，经 Command 出站，服务端 BuiltInTradeServerExtension 分发 HandleCreateRequest/HandleStatusUpdateRequest，由服务与 store 维护权威状态，再返回响应与事件 |
| Trade 报价与物品 | adapter 先通过 Item Pipeline 发送完整物品包，再提交物品引用 Command。服务端 HandleOfferUpdateRequest 验证并使用交易存储，客户端 HandleOfferUpdateResponse 与对应 token 处理真实回执 |
| Trade 快照与交付 | 服务端 Ready 后及 HandleSnapshotRequest 产生快照，客户端 HandleSnapshot 更新投影。HandleCompletedEvent、HandleCancelledEvent、CompletionAck 和 delivery receipt 继续使用既有交付机制 |
| Legacy Trade | TryHandleOutgoingCommand 在 Legacy 模式由 LegacyTradeProtocolAdapter 转为 Trading 协议，adapter 消费真实旧回执并调用现有交易状态/结果入口。空报价、整批转换、未知结果和物品完整性由既有回归固定 |
| Legacy Chat 与服务端旧协议 | 旧模块由 LegacyAdapter 的 codec/adapter 翻译后调用同一 Chat 或 Trade 服务。服务端 ABI LegacyFallback 在 PluginResultAdapter 归一化为 Continue |
| 登录与重连 | Client.OnLoginSuccess 调用 BeginNegotiation，服务端认证与登录匹配后绑定 Ready epoch。断线清理索引，客户端保留声明并在新 epoch 下全量同步，令牌轮换保持同一连接 epoch |

普通 Command 的缺省模式为 Server，现有入口的业务能力检查与权威 Handler 保持。未知普通 Command 无 Relay 路由。Relay 模式显式进入能力 Router，继续经过同一套 Command Interceptor、Outbound Interceptor 和 Observer。

## R0–R4 退出条件证据

| 阶段 | 完成结果 | 可复核证据 |
| --- | --- | --- |
| R0 | 基线、调用链、旧行为、旧报文兼容与共享交付方式明确 | 本文、基线 JSON、冻结 Common 67f243d9 报文 fixture、Chat/Trade/Adapter 与 stage 回归 |
| R1 | 独立声明句柄、owner 聚合、数量和大小限制、模块失败清理 | AssertRelayContracts、AssertRelayWhiteBoxLifecycle、实际注册和激活回滚测试 |
| R2 | 全量快照、revision、epoch、ACK、重试、动态更新、失败恢复 | 乱序/重复/冲突/旧 epoch、迟到错误 ACK、动态旧 ACK、发送失败恢复及控制流洪峰测试 |
| R3 | 统一 Command 处理、Router、投递与错误结果、预算和验证 | AssertRelayRouting、AssertRelayPipeline、真实 UDP Relay、共享接收保护与 stage 回归 |
| R4 | 公共服务、异步结果、Handler 异常隔离与独立示例 | 完整客户端构建、Chat Handler 异常隔离、Samples/CommandRelay 独立编译、无配套服务端插件的真实 UDP 测试 |

### 实际执行结果

- 服务端本次关闭回归通过 50 项检查，证据为 /tmp/phinix-relay-r0-r4-closeout/manifest.json。
- Pipeline 白盒通过 13 组、109 条断言，包含 R2 恢复和 R5 并发/回滚用例。
- 源代码链接客户端回归通过 7 组、97 条断言，测试夹具已与当前客户端模块接口对齐。
- 完整客户端按 net472 目标编译通过，最终构建日志为 /tmp/phinix-relay-r0-r4-client-build.log。
- 实际 net35 客户端接收资源保护和线程生命周期测试通过，包含真实 1 MiB 载荷、异常分片/合包、重组、事件数与载荷字节水位、排空、故障和重连。
- 服务端固定 netstandard DLL 的接收预算专项通过。
- 聊天 20 组、旧交易 adapter 12 组及独立示例构建通过。具体日志路径见 R5 测试文件。

同步补测发现同 revision 的迟到错误 ACK 会覆盖已确认状态，已修复为忽略已确认 revision 的重复反馈，并加入回归。网络保护验证仍保留既有固定 DLL 哈希检查，水位包含末次重组及可靠窗口的额外余量。

## R5 移交范围

真实发行版混合部署、游戏内联机、长时负载、慢接收者资源测量及人工验收继续属于 R5。黑盒与验收清单的初始状态为待执行。候选成套提交、Common 精确引用、包指纹和最终发布结论也在 R5 完成。
