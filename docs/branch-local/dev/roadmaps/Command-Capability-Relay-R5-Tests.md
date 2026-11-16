# R5 白盒测试与人工验收交接

日期为 2026-11-09。R0–R4 完成记录见同目录 Command-Capability-Relay-R0-R4-Closeout.md。白盒测试由实现方编写和执行，黑盒测试与最终验收由用户执行。2026-11-10 用户反馈黑盒全部通过，已登记于第 5 节；其他人工项目保留各自状态。

本次重跑结果见 [白盒测试报告](Command-Capability-Relay-R5-WhiteBox-Report.md)。

## 1. 测试基线与证据

客户端和服务端分支均为 codex/command-capability-relay。实际候选以 Command-Capability-Relay-Source-Baseline.json 的 HEAD、Common HEAD 和源文件 SHA-256 为准。存在未提交修改，因此仅记录分支名或 HEAD 无法确定本次候选内容。

正式打包时记录客户端包、服务端包、示例插件及关键程序集 SHA-256，确认两端编入同一份 Relay 协议定义。Common 共享提交与精确 gitlink 更新是 R5 交付步骤。已有 Common 版本差异及处理方式见关闭记录。

每项结果记录测试编号、候选指纹、执行时间、环境、步骤、预期、实际结果、日志位置和结论。路由结果与业务接收记录分别保存，关联同一请求 ID 或测试载荷中的唯一 caseId。日志隐藏会话令牌、凭据和实际玩家业务载荷。

## 2. 已编写的白盒测试

白盒使用真实编译组件、可控时钟、故障注入和本机网络。结果仅适用于对应组件与测试场景。

| 编号 | 测试入口 | 核心断言 |
| --- | --- | --- |
| W01 | Phase35ServerRuntimeTests 的旧 Command characterization | 未知普通 Command 不转发，Replace、Continue、Blocked、Handled 与旧 Observer 行为兼容 |
| W02 | AssertRelayContracts | 重复声明聚合、多所有者独立释放、重复释放、撤销不存在 owner、保留命名空间与数量限制 |
| W03 | AssertRelayWhiteBoxLifecycle | 4 个并发工作者共 1000 次声明与释放后无残留，真实注册失败和激活失败撤销声明 |
| W04 | AssertRelayContracts | 首次同步、ACK 丢失重试、发送失败后恢复、重复 ACK、迟到错误 ACK、动态更新旧 ACK、旧 epoch ACK |
| W05 | AssertRelayRouting | 快照 revision 乱序与内容冲突、无效版本及超量能力拒绝、失败后保留有效状态、重连排除旧声明 |
| W06 | AssertRelayRouting | 广播仅匹配能力并排除发送连接，玩家所有合格连接各一次，版本精确匹配 |
| W07 | AssertRelayRouting | NoCapability、TargetOffline、UnsupportedRelay、NoRecipients、Forbidden、NotAuthenticated |
| W08 | AssertRelayPipeline | Relay 复用 Interceptor 与 Observer，跳过业务 DefaultHandler，Replace 与原地修改重新校验，异常关闭当前请求 |
| W09 | AssertRelayContracts / AssertRelayRouting | 64 KiB 编码限制、非法目标、能力预算、扇出预算、十万次业务及控制流洪峰限流与恢复 |
| W10 | AssertRelayContracts / AssertRelayRouting | 请求关联、部分投递及 Unknown 保留、结果丢失超时、断线未知结果且无业务自动重发 |
| W11 | Fixtures/LegacyFrameworkPacket.cs | 冻结的 Common 67f243d9 外层 JSON 定义双向读写，缺省 Server 模式，内层 protobuf 往返 |
| W12 | ServerLifecycleTests --relay-network | 真实 UDP 登录、协商、快照、广播、可信身份、每接收者 SessionId、旧协议连接排除与单播错误 |
| W13 | ServerLifecycleTests --udp-receive-budget | 固定 netstandard DLL 下 1 MiB 分片、异常合包、重组及事件/字节水位、排空与故障边界 |
| W14 | ClientTransportLifetimeTests/ReceiveBudgetTests | 实际 net35 DLL 下 1 MiB 分片、恶意分片、事件和字节水位、排空、轮询故障和重连 |
| W15 | ChatRegressionTests | 真实客户端 Handler 谓词及执行异常隔离，既有聊天行为 |
| W16 | LegacyTradeRuntimeTests | 真实 adapter、报价回执、空报价、物品完整性、发送失败和结果未知恢复 |
| W17 | Server stage | 身份、TLS、Trade 持久化、Chat、LegacyAdapter、模型、架构与进程恢复 |
| W18 | Phase35ClientRuntimeTests / 示例构建 | 客户端模块生命周期、主线程边界及独立插件公共 API 编译 |

### 执行命令

服务端仓库执行以下命令。证据目录必须使用新的路径。

```sh
dotnet build Tests/Phase35ServerRuntimeTests/Phase35ServerRuntimeTests.csproj -c Release
dotnet Tests/Phase35ServerRuntimeTests/bin/Release/net10.0/Phase35ServerRuntimeTests.dll
python3 tools/quality/check.py --suite stage --isolated --output /tmp/phinix-relay-r5-whitebox-new --timeout 120
dotnet Tests/ServerLifecycleTests/bin/Release/net10.0/ServerLifecycleTests.dll --udp-receive-budget
```

客户端仓库执行源代码链接回归与隔离构建。

```sh
dotnet build Tests/Phase35ClientRuntimeTests/Phase35ClientRuntimeTests.csproj -c Release
dotnet Tests/Phase35ClientRuntimeTests/bin/Release/net10.0/Phase35ClientRuntimeTests.dll
python3 tools/compatibility/build-graph-isolated.py --output /tmp/phinix-relay-r5-client-new --game /absolute/path/to/GameDlls
```

完整图中的客户端仍以实际 net472 目标构建。需要恢复缺少的引用包时使用固定依赖恢复，保持 vendored SDK 文件原样。成功构建后把实际依赖闭包放入单一程序集目录，使用已有组件测试工具执行以下套件。

```sh
python3 tools/quality/run-client-component-probes.py --assemblies /absolute/path/to/assemblies --game /absolute/path/to/GameDlls --output /tmp/phinix-relay-r5-transport-new --suite transport-lifetime
python3 tools/quality/run-client-component-probes.py --assemblies /absolute/path/to/assemblies --game /absolute/path/to/GameDlls --output /tmp/phinix-relay-r5-chat-new --suite chat-regression
python3 tools/quality/run-client-component-probes.py --assemblies /absolute/path/to/assemblies --game /absolute/path/to/GameDlls --output /tmp/phinix-relay-r5-trade-new --suite legacy-trade
dotnet build Samples/CommandRelay/CommandRelay.Sample.csproj -c Release -p:PhinixPackage=/absolute/path/to/phinix-rework
```

### 本次执行状态

- Pipeline 白盒通过 13 组、109 条断言。
- 客户端源代码链接回归通过 7 组、97 条断言。
- 上次共享收包预算阶段回归通过 50 项，路径为 /tmp/phinix-relay-shared-budget-stage/manifest.json。本次关闭回归路径为 /tmp/phinix-relay-r0-r4-closeout/manifest.json，最终状态写入关闭记录。
- 实际 net35 接收预算及线程生命周期通过，路径为 /tmp/phinix-relay-client-receive-budget-final/manifest.json。
- 聊天 20 组与旧交易 12 组通过，证据分别为 /tmp/phinix-relay-guard-chat、/tmp/phinix-relay-guard-legacy。
- 完整客户端与独立示例编译通过。构建仍有现有 Protobuf 裁剪、SourceLink 和过时成员警告。

## 3. 用户执行的黑盒测试

### 环境准备

准备同一测试服务器及 A、B、C 三个独立客户端。A、B 安装候选版和示例插件，C 用于不声明能力、版本不匹配及旧客户端组合。另备发布前客户端和服务端包用于交叉部署。使用测试账号与可恢复存档。

当前示例提供 API，人工操作需要另行接入测试驱动或测试面板。候选 Framework Server 提供基础 Relay，只有 B13/B14 的拦截器专项需要临时服务端测试插件。

示例公开 IResearchRelaySample.RequestPlayer、Announce 和 Received。将这些入口接入测试驱动或现有测试面板，记录 SyncState、SyncError、路由 Status/Error/Deliveries 和接收方 Received。动态声明测试通过 IClientCommandCapabilities 的句柄执行。当前模块管理界面要求重启的启停操作仍按其现有方式执行，动态声明与释放直接使用测试驱动。

每个载荷写入唯一 caseId，例如 {"caseId":"B01-001"}。接收记录增加玩家、连接和时间，保留请求与接收之间的关联。单播玩家 ID 从在线玩家目录取得。

| 编号 | 操作步骤 | 预期结果 |
| --- | --- | --- |
| B01 | A、B 登录，等待 SyncState 为 Synced。A 向 B 调用 RequestPlayer | B 收到对应 caseId 一次，发送者为 A。A 得到 Submitted，接收方业务观察单独记录 |
| B02 | A 调用 Announce，B 声明 announcement，C 保持未声明 | B 收到一次，A 的发送连接与 C 均无接收记录 |
| B03 | 释放 B 的 announcement 句柄，等待同步后再次广播 | B 不再收到，零匹配时返回 NoRecipients |
| B04 | B 在线但释放 request 声明，A 单播 B | NoCapability，B 无接收记录 |
| B05 | B 断线，A 单播 B | TargetOffline，服务端无旧连接投递 |
| B06 | B 同账号建立两个允许的在线连接，两个连接均声明 request | 每个合格连接收到一次，Deliveries 与接收连接数量相符 |
| B07 | B 的两个连接只有一个声明 request | 仅该连接收到一次 |
| B08 | B 只声明 request v2，A 发送 v1 | NoCapability，B 不收到 v1 |
| B09 | 同一能力由两个 owner 各声明，依次释放 | 释放第一个后仍接收，最后一个释放并同步后停止接收 |
| B10 | A、B 断线重连并重新登录后广播和单播 | 声明自动恢复，旧 epoch、旧 ACK 和旧回调不污染新会话 |
| B11 | 声明能力但停用对应业务 Handler，再发送 | 路由仍按声明执行，Framework 不自动撤销能力。接收业务未处理与网络 Submitted 分别记录 |
| B12 | 接收 Handler 抛错后发送另一条正常消息 | 失败受隔离，后续消息继续可处理 |
| B13 | 使用测试服务端插件分别返回 Continue、Replace、Blocked、Handled | Continue 可路由，合法 Replace 使用新目标与能力，Blocked 明确拒绝，Handled 无客户端转发 |
| B14 | Replace 改成保留命名空间、无能力目标或禁止的目标 | 明确拒绝，目标无接收记录 |
| B15 | 连续发送超出预算，随后停止并等待预算恢复 | 出现 RateLimited，恢复后可再次正常发送 |
| B16 | 发送超大 JSON、非法目标 ID、尝试保留命名空间 | 明确拒绝，其他客户端继续正常运行 |
| B17 | 发送期间断开网络，等待结果 | 明确未发送或 Unknown/逐连接结果，Framework 无自动业务重发 |
| B18 | 普通未知 Command 未指定 Relay，观察 B/C | 无转发，既有普通 Command 未处理语义保持 |

### 新旧版本组合

每个组合均执行普通聊天、聊天历史、交易创建、空报价、物品报价、确认、取消和断线恢复。确认真实回执才能改变物品所有权，记录操作 token 与库存变化。

| 编号 | 客户端 / 服务端 | Relay 预期 |
| --- | --- | --- |
| C01 | 候选 / 候选 | B01–B18 按契约通过 |
| C02 | 发布前 / 候选 | 旧业务正常，旧客户端无 Relay 投递 |
| C03 | 候选 / 发布前 | 旧业务正常，Relay API 返回 UnsupportedRelay |
| C04 | 发布前 / 发布前 | 既有业务基线正常 |

### 负载与资源观察

| 编号 | 场景 | 观察与通过标准 |
| --- | --- | --- |
| P01 | 连续 30 分钟正常单播和广播 | 记录进程内存、发送/接收计数及延迟，消息行为符合声明与逐连接结果 |
| P02 | 接收方暂停处理或限速，同时持续发送 | Relay 队列和预算产生可观察拒绝，其他连接继续可用。恢复后能够继续发送 |
| P03 | 多个发送者同时广播大载荷 | 发送者与全局扇出预算生效，无静默截断接收集合 |
| P04 | 重复连接、断开和动态声明 100 轮 | 无重复 Handler 或旧连接投递，排空与清理后内存能够回落 |
| P05 | 分片及事件洪峰 | 结合白盒水位证据观察进程峰值与恢复。队列水位含可靠窗口和末次重组余量 |

P01–P05 记录机器配置、在线连接数、载荷字节、持续时间、内存峰值和排空后的稳定值。具体延迟与进程内存容许值由验收环境先行登记，测试结果不得事后改变阈值。

## 4. 用户最终验收

- [ ] 候选包、服务器、示例插件和共享协议指纹已登记。
- [ ] B01–B18 已记录实际结果，关键路由与权限场景全部通过。
- [ ] C01–C04 使用真实包执行，旧 Chat、Trade、Adapter 行为通过。
- [ ] P01–P05 有完整资源与恢复记录，超限结果符合契约。
- [ ] 游戏内收发与操作无跨存档、重复物品、提前确认或失联后自动返还问题。
- [ ] Submitted 与插件业务 ACK 的含义在测试记录中明确区分。
- [ ] 发现的问题已修复并重跑相应用例，未验证项目保持待执行状态。
- [ ] R5 共享提交、精确 gitlink、产物清单与发布说明已核对。
- [ ] 用户填写最终结论、验收日期与候选指纹。

最终结论可填写通过、需修复后复验、待补证据。黑盒和人工验收完成前，R5 保持进行中。

## 5. 2026-11-10 用户黑盒结果

- 执行方：用户。
- 反馈日期：2026-11-10（Asia/Singapore）。
- 原始反馈：“黑盒测试all pass”。
- 黑盒结论：全部通过（用户报告）。未附逐项日志，不补写具体执行步骤、连接数或实际运行的游戏版本。
- 关联交付：2026-11-09 提供的 Relay 候选客户端和 Example 测试插件，均包含 1.6/1.7；实际测试版本组合未单独登记。
- 客户端 ZIP SHA-256：cc9496ab62e327d0edf102bb85bc4e8912e369b2dc69d7f4afccac56dc74b83e。
- Example ZIP SHA-256：dc721470dcced10a9e25e85892d5f97b5ea3a57f4f15e03d6a0a50618d723028。
- 关联开发服务：47.93.172.62:16200/UDP，部署镜像 phinix-relay-dev:20261109；部署详情见服务端 docs/Server-Relay-Dev-Deployment-2026-11-09.md。

本记录接受用户的黑盒通过结论。第 4 节要求的逐项证据、C01–C04 真实包交叉组合、P01–P05 压力资源记录和最终验收签收尚未单独确认，因此未代为勾选。共享提交、精确 gitlink、成套产物与发布说明仍需核对；R5 保持进行中。

## 6. 2026-11-10 验收标准调整与完成结果

用户明确授权：“压测和交叉全部做白盒，通过就行，推送到 github 远端分支”。本次采用最终核对报告中的组件矩阵和 P01–P05 白盒替代场景作为验收标准，第 4 节原始真实包/实机压力清单保留为历史计划，不再是本次交付门槛。

- [x] 黑盒全部通过（用户反馈）。
- [x] C01–C04 对应的新旧组件组合、JSON/protobuf 白盒通过。
- [x] P01–P05 对应的确定性压力、资源水位与恢复白盒通过。
- [x] 实际 UDP 出站与收包预算、TLS 生产队列白盒通过。
- [x] 两端 Common 精确引用统一，API、协议示例及交付范围核对完成。
- [x] 精确暂存客户端的双版本构建、生命周期、聊天和实际接收保护回归通过。

最终结果、执行边界及证据见 [最终核对报告](Command-Capability-Relay-R5-Final-Verification.md)。本次 R5 验证通过，分支交付需再核对远端引用；没有把虚拟时钟、注入边界或冻结组件描述为真实游戏长时运行及旧发行进程混部。
