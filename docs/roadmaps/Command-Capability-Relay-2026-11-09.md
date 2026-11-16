# Command Pipeline 显式 Capability 路由实施路线图

日期为 2026-11-09。状态更新于 2026-11-10：R0–R4 已完成，R5 白盒已执行，用户反馈黑盒全部通过；交叉兼容及压力白盒通过，共享精确引用已统一；按用户本次标准完成 R5 验证。本文件覆盖 Phinix-Rework、Phinix-Rework-Common、Phinix-Rework-Server 三个仓库，以当前工作树为实施依据。

## 1. 已确认方向与范围

Command 显式区分服务端执行业务与目标客户端执行业务。服务端处理模式执行服务端业务 Handler，Relay 模式由 Framework 根据客户端声明选择接收连接并投递。

Trade 的创建、报价、确认和取消继续由服务端维护权威状态。Chat 历史查询继续由服务端读取历史。服务端执行业务后可以产生面向客户端的结果或事件。

客户端插件显式声明自身支持的能力，并自行兑现声明。Framework 维护声明同步、连接身份、路由权限、资源预算、投递与错误反馈。Capability 与 Handler 注册独立，PluginId 作为业务命名空间，身份由认证连接确定。

本次允许系统性重构，必须保持已有 Chat、Trade、Adapter、LegacyFallback 的可观察行为。旧 Command 缺省进入服务端处理模式。新增 API 显式表达处理模式、PluginId、CommandType、能力版本与 Relay 目标。

第一版包含玩家单播、能力广播、动态声明与撤销、重连恢复、路由结果反馈。Multicast、房间、群组和版本区间匹配留作扩展。业务 ACK、响应、超时及业务去重由插件管理。

## 2. 源码审计基线

| 位置 | 当前行为与实施关注点 |
| --- | --- |
| Client/Source/Framework/PhinixFrameworkClient.cs | 出站通过 Command Handler 决定发送，入站按 Handler 谓词分发。匹配谓词需要纳入异常隔离 |
| Common/Utils/Framework/FrameworkPacket.cs | DataContract 外层信封，包含 MessageType、身份字段、Flow、CommandKind、JSON 与 bytes |
| Common/Utils/Framework/FrameworkSerialization.cs | 外层使用 DataContractJsonSerializer，内部部分载荷使用 protobuf |
| Common/Utils/Framework/PhinixExtensionRegistry.cs | 初始化时收集 Framework 能力字符串，独立于本次动态插件声明 |
| Server/Framework/PhinixFrameworkServer.cs | Ready 身份检查后按 MessageType 检查协商能力，再进入 Pipeline |
| Server/ServerRuntime/ServerPipelineRunner.cs | Interceptor、DefaultHandler、Observer 三段处理，Command 返回 bool，Replace 可替换整个包 |
| Server/Framework/FrameworkServerPacketDispatcher.cs | 同时提供 void 与带结果发送接口，Relay 应消费带结果接口 |
| Server/ServerRuntime/ServerSessionRegistry.cs | 维护 Ready、PrincipalId、会话与 epoch，支持令牌替换和断线清理 |
| Server/UserManagement/ServerUserManager.cs | 现有玩家连接查找使用 Single，需要为 Relay 明确多连接行为 |
| Extensions/LegacyAdapter/Pipeline/PluginResultAdapter.cs | 服务端 LegacyFallback 在兼容边界归一化为 Continue |
| Server/Connections.Server | UDP 使用 ReliableOrdered，已有固定后端分片与收包队列保护 |

首次审计读取的客户端 Common HEAD 为 c165f61d749189f700ca896d63d9366d5c71dbf7，服务端 Common HEAD 为 67f243d9edced77dc3fe669f8a435b1f14b59199。已重新核对差异及工作树，本次 Relay 协议文件在两端保持一致。共享交付提交与精确引用更新归入 R5 原定交付项，具体方式见 R0–R4 完成记录。保留已有未提交改动，禁止自动刷新子模块远端。

已运行服务端 Phase35ServerRuntimeTests，Release 构建通过，执行结果为 8 cases、13 assertions 通过。存在 vendored Protobuf 裁剪配置警告。完整业务链、混合版本序列化和资源压力验收仍待以下阶段完成。

## 3. 目标生命周期

1. 客户端声明服务维护模块持有的能力句柄，形成当前完整快照。
2. 登录后沿用 Hello 协商 Framework 能力，双方协商 core.command-relay.v1 后启动插件能力同步。
3. 客户端发送带 revision 的完整能力快照，服务端校验后原子更新当前连接 epoch 的索引，并返回 ACK。
4. 插件显式提交 Server 或 Relay Command，Framework 验证本地状态并填充认证信封。
5. 服务端从认证连接取得身份，执行入口资源限制及 Command 结构校验。
6. 统一 Command Pipeline 执行 Interceptor，并校验替换后的最终 Command。
7. Server 模式进入业务 DefaultHandler，Relay 模式进入能力 Router。
8. 路由投递复用 Outbound Interceptor 与带结果 Delivery，发送前再次检查接收连接及声明资格。
9. 形成处理与逐连接投递结果，执行 Observer，反馈关联请求的路由结果。
10. 客户端在既有主线程调度约束内分发给插件 Handler，插件负责业务结果与 ACK。

认证身份、连接 epoch、原始请求关联和原始处理模式保存在可信处理上下文。Replace 修改业务内容和 Relay 目标后重新验证。修改处理模式应明确拒绝。Outbound Interceptor 修改路由相关字段后同样重新验证接收集合，避免扩大投递范围。

## 4. 拟采用的实现细节

以下细节作为实施默认方案。API 名称可按现有项目风格调整，影响行为的调整需在本文件记录。

### 4.1 声明与快照

- 路由键为 PluginId、CommandType、Version，第一版精确匹配版本。新标识采用统一、明确的字符与大小写规则，保留现有业务标识的兼容读取。
- Declare 返回可释放的声明句柄，模块卸载或激活失败时成对释放。相同模块重复声明与多个模块共同声明均按独立句柄聚合，释放最后一个句柄后撤销能力。重复释放幂等。
- Framework 不扫描 Handler，不根据 Handler 异常或缺失修改声明。
- 断线保留本地声明，新连接建立新 epoch 后重新发送完整快照。同连接令牌轮换沿用 epoch。
- 每个 epoch 使用递增 revision。旧 revision 忽略，相同 revision 与相同内容重发得到幂等 ACK，相同 revision 内容冲突返回错误。
- 快照完整验证后原子替换，失败保留上一份有效快照。ACK 丢失时重试最新快照。限制并合并动态更新，暴露 Pending、Synced、Failed 状态。
- ACK 必须绑定连接 epoch 与 revision。过期 ACK、定时器及回调不得污染新连接状态。
- 控制消息独立于插件 Handler 分发，但使用现有 Framework 协议与传输入口。

### 4.2 Command 与 Pipeline

- 新 Command API 显式提供 PluginId、CommandType、版本及处理模式，SendRelay 默认目标为能力广播。
- 统一准入规则按处理目的地校验。既有 MessageType 协商检查迁入服务端处理目的地的兼容准入逻辑，并通过 characterization tests 固定原行为。
- 内部处理结果表达最终 Command、终止原因、路由结果与投递结果。旧接口需要的 bool 只在既有调用边界投影。
- Continue 继续拦截链。Replace 更新当前 Command 后继续，并重新验证。Blocked 返回拒绝。Handled 表示服务端插件已消费。异常终止当前服务端请求。
- Relay 进入 Router，Server 模式进入 DefaultHandler。未知普通 Command 保持既有未处理语义。
- 服务端 LegacyFallback 继续在兼容边界归一化，显式 Relay 资格始终由请求模式确定。
- 同 Priority 使用稳定注册顺序。支持按 PluginId、CommandType 匹配拦截。
- Observer 对最终结果执行一次，观察异常隔离。保留旧业务 Observer 被调用时机的兼容投影，并增加结构化结果观察能力。

### 4.3 Router 与结果

- 广播选择当前在线 Ready、协商 Relay 且声明精确能力的连接，默认排除发送连接，零匹配返回 NoRecipients。
- 玩家单播选择该玩家所有合格连接，每连接一次。只排除明确要求排除的连接，并固定向自身玩家单播的测试语义。
- 离线返回 TargetOffline，在线且全部连接未协商 Relay 返回 UnsupportedRelay，有 Relay 连接但均缺少声明返回 NoCapability。
- 路由权限由服务端策略控制，默认允许已认证普通玩家在预算内进行单播与能力广播。保留系统命名空间，部署方可收紧通用权限。
- 投递结果保留逐连接 Submitted、NotSent、Unknown 及原因，部分提交返回部分结果。Framework 不自动重试已提交或结果未知的业务 Command。
- 结果关联原始 MessageId，明确区分本地接受、服务端接受、接收者匹配和网络提交。业务处理成功由插件 ACK 表达。
- 覆盖 UnsupportedRelay、InvalidCapability、NoCapability、TargetOffline、NotAuthenticated、Forbidden、PayloadTooLarge、RateLimited、DeliveryFailed、NoRecipients，并为结构错误和拦截终止提供稳定错误码。
- 未认证请求的响应受反射放大与信息泄露边界约束，可直接拒绝连接上的无效请求。

### 4.4 兼容与资源边界

| 客户端与服务端组合 | 预期 |
| --- | --- |
| 新与新 | 协商 Relay 并同步插件能力 |
| 旧与新 | 原有业务正常，排除 Relay 投递 |
| 新与旧 | 原有业务正常，Relay API 明确返回不可用 |
| 旧与旧 | 原有协议行为保持 |

优先保留 Framework Version 2，以能力协商启用新增字段和控制消息。使用真实旧版本序列化器与端点测试缺省字段、未知字段、枚举、JSON、protobuf 以及超时降级。若实测无法满足兼容矩阵，先修订协商和封装方案，再交付协议。

建议初始预算如下，最终值通过边界与压力测试确定并记录。

| 资源 | 初值 |
| --- | --- |
| 单连接插件能力 | 256 项 |
| PluginId 与 CommandType | 各 128 字符 |
| 完整能力快照 | 序列化后 64 KiB |
| Relay 信封 | 序列化后 64 KiB |
| 单连接发送频率 | 每秒 10 条，突发 20 条 |
| 单次广播接收连接 | 128 个，超限明确拒绝，避免静默截断 |
| 发送者扇出预算 | 每秒 1 MiB，突发 2 MiB |

能力同步另设控制面频率预算，防止频繁全量更新。按最终编码字节及实际接收者数量计算扇出成本，并设置全局预算、有界出站积压与慢接收者处理。超限在交付前返回明确错误。

保留已有 UDP 4096 分片、单连接 48 MiB、全局 96 MiB 重组预算，以及 512 事件、32 MiB 载荷队列水位。核查客户端同方向保护和 TLS 路径。业务层大小检查发生在网络重组之后，需配合收包前保护、反序列化前长度限制及有界队列。固定后端队列水位可能短时超出，进程内存峰值与长时压力需单独验收。

## 5. 实施阶段与退出条件

### R0 基线与完整调用链核对

- [x] 记录三仓库 HEAD、脏文件和 Common 差异，确定共享协议交付方式。
- [x] 完整追踪 Chat、Trade、LegacyAdapter 的请求、回执、快照、物品交付与登录重连路径。
- [x] 固定旧 Command 的 Continue、Replace、Blocked、Handled、LegacyFallback、无 Handler、异常和 Observer 行为。
- [x] 运行既有 Chat、Trade、Adapter、会话和传输相关测试，记录可重现命令及环境限制。
- [x] 建立真实旧端协议 fixtures，验证新增协议字段的实际序列化兼容性。

退出条件为已有业务断言建立、兼容封装可验证、跨仓库基线明确。

### R1 共享协议与声明服务

- [x] 定义处理模式、路由目标、能力键、快照、ACK、路由结果及错误契约。
- [x] 实现独立声明句柄与模块生命周期清理，暴露给插件公共服务。
- [x] 实现本地聚合、上限验证、动态快照与同步状态。
- [x] 完成重复声明、多所有者、撤销、连接前声明及卸载清理测试。

退出条件为公共契约可编译、Handler 与 Capability 完全独立、旧报文解析保持既有行为。

### R2 连接同步与服务端索引

- [x] 增加 Relay 协商及登录后完整同步。
- [x] 实现 epoch、revision、幂等 ACK、重试与快照原子替换。
- [x] 将能力索引与断线、重连、身份切换、令牌轮换配对。
- [x] 验证乱序、重复、失败、失效回调、海量声明与更新洪峰。

退出条件为服务端索引只包含有效连接状态，重连可恢复，失败状态可观察。

### R3 统一 Command 处理与能力 Router

- [x] 引入结构化处理结果，完成普通 Command 的兼容投影。
- [x] 集成显式模式、Interceptor、替换后验证、Router、Delivery 与 Observer。
- [x] 实现玩家单播、能力广播、多连接语义和逐连接结果。
- [x] 实现权限、命名空间、载荷、频率、扇出与队列预算。
- [x] 确保替换前后、出站拦截后与实际发送前均保持身份和接收资格约束。

退出条件为未知普通 Command 无转发，未声明能力的连接无投递，部分失败具有明确反馈。

### R4 客户端 API 与接收分发

- [x] 提供 SendRelay、默认广播、玩家目标和异步路由结果入口。
- [x] 实现 UnsupportedRelay 本地反馈、请求关联及断线后的未知结果处理。
- [x] 将 Handler 匹配和执行纳入异常隔离，保持游戏主线程约束。
- [x] 编写独立第三方插件示例，分别展示声明、Handler 注册、单播、广播、撤销和结果观察。
- [x] 验证服务端无需安装对应业务插件即可中继。

退出条件为插件通过公共服务完成全链路，网络细节封装在 Framework 内。

### R5 联合回归与交付

- [x] 编写并执行白盒测试，准备由用户执行的黑盒与最终验收清单。
- [x] 用户执行黑盒测试并反馈全部通过（2026-11-10；结果来源为用户反馈）。

- [x] 执行完整 Chat、Trade、Adapter、LegacyFallback 回归。
- [x] 按 2026-11-10 用户授权，以冻结旧组件与当前组件执行四种组合及 JSON/protobuf 白盒矩阵。
- [x] 执行真实 UDP Relay、UDP 出站积压及阶段套件的 TLS/身份路径；TLS Relay 队列以生产组件白盒核对。
- [x] 按用户授权执行 P01–P05 白盒替代场景及实际 UDP/TLS 队列、分片、收包预算专项。
- [x] 两端统一 Common 精确引用，更新插件 API 文档、示例及最终验证记录。
- [x] 交付修改清单、生命周期、兼容矩阵、构建测试证据、安全限制和剩余债务。

退出条件为相关构建和测试通过，所有未验收项目有明确说明，发行产物与共享契约成套对应。

## 6. 必测验收矩阵

| 范围 | 必须覆盖 |
| --- | --- |
| 旧业务 | 普通未知 Command、Chat 历史、Trade 权威回执、Adapter 转换、LegacyFallback |
| 声明 | 声明与撤销、重复、多个所有者、卸载、无 Handler 仍保留声明 |
| 同步 | 登录、重连、动态更新、乱序、重复 ACK、冲突 revision、发送失败、旧 epoch 回调 |
| 路由 | 能力广播、默认排除发送连接、零接收者、单播无能力、目标离线、旧客户端、多连接 |
| 管线 | Continue、Replace、Blocked、Handled、同优先级次序、Observer 一次、异常隔离 |
| 安全 | 伪造身份、系统命名空间、未认证、非法目标、替换后越权、能力数量与大小限制 |
| 资源 | 超大载荷、发送频率、控制面频率、广播扇出、全局预算、慢接收者、分片与队列 |
| 结果 | 本地拒绝、服务端拒绝、零匹配、全部提交、部分提交、Unknown、回执丢失 |
| 兼容 | 四种端点组合、缺省模式、未知字段与枚举、JSON/protobuf 往返、原有协商降级 |

测试应通过正常公共入口并包含真实编译后的组件。网络提交与业务 ACK 分别断言。测试日志记录错误码、revision、epoch 与投递数量，避免记录凭据、会话令牌和业务载荷。

## 7. 执行记录

2026-11-09 已确认服务端业务执行与客户端业务执行的处理模式区分，完成本路线图。上述具体同步、版本、多连接和预算选择为实施默认建议，尚待实现与测试验证。当前完成项仅包含架构准备及既有 Phase35ServerRuntimeTests 基线运行。

## 8. 2026-11-09 实施与验证记录

核心实现已覆盖共享协议、独立声明句柄、全量快照与 ACK、epoch 与 revision、客户端发送和结果关联、统一服务端 Command 处理、能力 Router、逐连接投递结果与预算。普通 Command 的入口能力检查及原有服务端业务路径保持兼容，Relay 使用显式协议与授权检查。

服务端阶段回归通过 50 项检查，包含新增真实 UDP Relay 测试。最终 Pipeline 聚焦回归通过 12 组、95 条断言。客户端完整 1.6 依赖图编译通过。旧交易适配器回归通过 12 组、74 条断言。聊天回归通过 20 组，包含真实客户端 Command Handler 谓词及执行异常隔离。冻结旧 JSON 信封的双向读写与既有 protobuf 往返测试已加入服务端 Pipeline 回归。

当前两端分支均为 codex/command-capability-relay。共享协议文件在两个 Common 工作副本同步修改，尚未提交或更新 gitlink。首次客户端离线恢复缺少 .NET 引用包，下载固定依赖后完整编译通过。服务端首次阶段检查因测试数量匹配和新源码职责分类失败，更新验证配置后最终阶段回归通过。

待验收项包含真实旧发行版交叉部署、游戏内联机、长期洪峰与慢接收者压力、客户端收包前内存保护。已有 UDP 服务端预算、Relay 出站水位与扇出预算构成当前资源边界，进程内存峰值仍需实测。API 与行为详情见 ../Command-Capability-Relay.md，服务端对应说明位于服务端仓库 docs/Command-Capability-Relay.md。

## 9. 客户端收包保护与示例闭合

2026-11-09 已将收包预算提取为 Common/Connections 的共享实现，并在客户端启动前校验 net35 固定 DLL 哈希及布局。客户端与服务端使用同一套分片、合包、未完成组、事件数和载荷字节预算。轮询故障关闭当前客户端传输，重新连接先等待旧工作线程退出。

客户端完整 net472 图编译通过。实际 net35 客户端网络测试通过有效 1 MiB 载荷、异常分片及合包、重组预算、512 事件和 32 MiB 字节水位、排空、故障后重连。源代码链接的客户端回归通过 7 组、97 条断言。其原有生命周期夹具仍实现旧模块接口，已迁移到当前 IClientExtensionModule 并保留原断言。服务端共享预算提取后的阶段回归通过 50 项检查。客户端聊天 20 组、旧交易 12 组回归再次通过。

新增 Samples/CommandRelay 独立插件示例已编译通过，包含声明、Handler、玩家单播、能力广播、结果观察、主线程回调和卸载清理。

当前资源保护采用确定性预算和本机合成流量测试。真实发行版交叉部署、长期负载、游戏内联机与峰值内存测量仍待验收。共享修改尚未提交或统一 gitlink，发布前仍需完成三仓库成套提交与引用更新。

## 10. R0–R4 关闭与 R5 测试交接

R0–R4 所有退出条件已核对，证据见 [完成记录](Command-Capability-Relay-R0-R4-Closeout.md) 与 [关键源码基线](Command-Capability-Relay-Source-Baseline.json)。R5 白盒测试已扩充并执行，具体命令、用例、结果和用户负责的黑盒/验收步骤见 [R5 测试与验收交接](Command-Capability-Relay-R5-Tests.md)。真实包交叉部署与人工验收保持待执行，共享提交及 gitlink 更新按原计划在 R5 交付时完成。

## 11. 2026-11-10 黑盒测试反馈

用户反馈“黑盒测试 all pass”，已登记黑盒结论为全部通过。关联候选为 2026-11-09 交付的客户端与 Example 双版本测试包及开发测试服务器。包指纹与结果范围见 R5 测试与验收交接第 5 节。未附逐项日志及实际游戏版本组合；C01–C04 真实版本交叉部署、P01–P05 资源压力和最终发行验收未单独确认，保留原状态。R5 仍需完成共享提交、精确 gitlink 和最终交付核对。

## 12. 2026-11-10 R5 白盒收口

用户授权交叉兼容与压力场景全部以白盒通过作为本次标准。已执行并通过替代矩阵及资源边界验证，两端 Common 统一为 911047cf8a6dd34e661e107e182e9c921b8e9586。最终验证与范围说明见 [最终核对报告](Command-Capability-Relay-R5-Final-Verification.md)。前文记录中的待执行项反映各阶段当时状态，以本节及最终报告为当前结论。
