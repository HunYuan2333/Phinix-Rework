# Legacy 全入口衔接审计与修复（2026-11-05）

## 范围、基线与边界

本轮检查不限于交易：客户端原始模块传输、协商/重连、认证映射、历史设置迁移、聊天、交易、物品编码、操作结果与持久接收，以及服务端对应入口。实现位于 `codex/p3-trade-business`，产品输入基线 `57e6d1c726ce9113db7652c0b6ae29d3e2f0f440` 加本轮变更；未跨分支合并、推送、部署或发布工坊。UI/布局未修改。

- Client Common 固定 `c165f61d749189f700ca896d63d9366d5c71dbf7`，protobuf 固定 `4b0c3aacf0657fbf38253b38918d3358dd4319ec`，未修改 gitlink/vendor/SDK pin。
- Server 本轮核对基线 `360e716c78b1f39143291b2753f6300a2740e04a`，Common `67f243d9edced77dc3fe669f8a435b1f14b59199`；本轮未修改服务端产品。
- 原版协议依据本地 thomotron/Phinix Release0.9.7 固定参考代码；不是用被测 Adapter 自己生成期望。
- 现存 README、设计哲学、插件文档等并行整理改动保留，不纳入本次提交。

兼容规则属于协议、持久数据或外部系统 Adapter。核心接收统一业务状态、真实结果与统一交付事件；Host 仅维护通用派发规则。新增 `ITradeCompletionInput` 是协议无关的延迟接收合同，不含旧包、兼容模式、降级标志。旧包的识别、翻译、暂存与最终协议选择仍属于 Legacy Adapter。

## 已确认问题与修复

### 1. 旧协议消耗现代意图日志结果预留

玩家 `Player (8).log` 在 17:10:29.645 和 17:13:39.532 记录 `TradeOfferSendUncertain: Intent journal has insufficient result evidence headroom.`。本次日志已经存在确认状态更新，不能继续认定为上次的零版本确认问题。

`FrameworkClientTradeServiceAdapter.PrepareIntent` 原先对 Legacy 创建、报价、确认/取消同样建立现代 durable intent。原版真实报价回执通过统一 `IFrameworkTradeUpdateResultApi` 结算，但没有现代 operation/query result，因此不能释放现代结果预留。即使文件实际很小也会耗尽 admission headroom。

修复：已知 Legacy 出站不建立无法闭合的现代 intent；继续使用既有真实 token 回执和库存托管。现代路径仍先持久化、容量不足先拒绝。**没有删除、改写或追认历史 intent；没有自动返还 Unknown 资产。**

### 2. Host 在出站异常后继续派发

`PhinixFrameworkClient` 原先在文本、命令、物品处理器抛异常后继续后续处理器。旧协议可能已经发送，随后却可能进入 Framework 路径，形成结果未知后的再次派发。

修复：这三个通用出站循环在处理失败后向调用方传播异常，不再调用下一处理器。明确返回 Continue 的正常扩展链不变；入站错误隔离不变。规则不识别 Legacy、Chat 或 Trade。结构化消息原来已在异常后返回失败，保持原行为。

### 3. 停用/模式切换后的旧出站回调

`BuiltInLegacyAdapterClientExtension` 的 Handle 方法原先信任先前 CanHandle 的判断。停用后直接调用文本处理器会抛异常，模式变化后陈旧调用也可能被旧处理器截获。

修复：实际 Handle 时再次检查当前资格；停用或不匹配时返回 Continue，不发送、不回显。

### 4. 原版登录早到包丢失及交付范围

原版 `ServerTrading.loginEventHandler` 会立即发送活跃交易和离线完成通知。客户端原先只在 Legacy 协商结束后注册 Trading，存在丢包窗口。

修复：Unknown 阶段注册接收器，按接收顺序有界暂存，最多 256 包/16 MiB；仅 Legacy 选择后派发，FrameworkV2 选择时丢弃，重连重置。重复 Unknown 通知不丢弃已暂存包。超限明确诊断并限制交易发送至重连，不伪造成功。

交付通知特别使用统一 `ITradeCompletionInput.CaptureCompletion`：收到时捕获游戏/库存存档范围、endpoint、主体及独立载荷副本，再延迟进入既有 inbox、去重和交付逻辑。协商期间换游戏不能将物品归到后来的游戏。选择 FrameworkV2 不写原版交付或制造回执。该暂存不是持久网络收件箱，限制见下文。

### 5. 全量交易同步错误地追加及部分应用

`LegacyTradeProtocolAdapter.HandleSyncTrades` 原先逐项 Upsert，遗漏交易不会移除；后项无效时前项已经写入。

修复：Adapter 整批转换并校验 ID 唯一性及双方主体，然后发送一个统一 full snapshot 给现有 HandleSnapshot/ReplaceAll。空全量清空旧状态；无效整批保持旧状态。同步不等于报价操作成功，不结算 token、不物化资产。

### 6. 发送失败伪装为 Handled

Legacy 创建、状态发送及外层命令处理原先吞掉异常；不支持的 trade 命令也只记录后返回 Handled。

修复：发送/无效载荷/不支持命令明确失败，传递至操作 owner；报价确定未连接仍经原 token 拒绝，发送后异常继续 pending，后续真实回执仍能结算。Framework facade 的“没有处理器接受”也明确失败。不会失败后切换协议。

### 7. 聊天衔接

- `LegacyChatProtocolAdapter` 原先用 Unix 秒直接生成 .NET ticks，遗漏纪元偏移；改为 protobuf `Timestamp.ToDateTime().Ticks`。
- 删除协议适配器对 Verse.Log 的直接依赖，使用隔离的 Host 日志回调；诊断异常不能改变发送结果。
- 删除不真正限制队列、仅自增/重置的 pendingMessageCount，展示容量仍由 Host 负责。
- 结构化回复/提及原先静默丢弃；Adapter 明确投影成文本。回复有 snippet 时使用 snippet，提及有用户目录时使用当前显示名，否则保留 UUID。原版没有回复跳转/提及提醒，不能伪造这些能力。
- Legacy 模式提示从 Chat 业务插件移到 Adapter 的普通 system display message，不触碰游戏信件/窗口布局。
- `UserManagement` 现有合同项目引用补入 Adapter 工程，仅用于读取 `IClientUserDirectory` 返回的 ImmutableUser；无新包版本或第三方依赖。

### 8. 现代查询/完成 ACK 的路由边界

操作查询除了远端 capability 还需现代协议资格；原版不得因残留能力信息发送现代查询。完成 ACK 与主动快照请求的协议判断移动至 Framework transport adapter。核心交付仍在既有成功证据形成后请求 ACK；原版不伪造 ACK。

## 其他入口核对

| 入口 | 本轮核对结果 |
| --- | --- |
| NetClientLegacyTransportAdapter | 仅桥接原始模块发送/注册/注销，无业务结算或协议重试 |
| LegacyAdapterSession | 激活/回滚/注销、借用资源、陈旧回调由既有 scope 管理，新增早到接收沿用此生命周期 |
| LegacyClientIdentityAdapter / 服务端 LegacyIdentityMappingAdapter | 旧凭据映射位于命名 Adapter，稳定主体不由业务推测；未引入密码或修改凭据文件 |
| TLS 信任与协商 | 认证/信任失败仍不静默降级；TCP/TLS 实验性范围本轮不改变 |
| ChatSettingsInputAdapter / TradeSettingsInputAdapter | 历史设置在输入迁移边界转换，不给业务增加旧设置字段 |
| DefaultLegacyTradeItemCodec / FrameworkTradeItemPayloadCodec / item pipeline | 旧物品格式属于 codec 边界；原版隧道格式仍仅由 Legacy Adapter 翻译；本轮回归整批拒绝和完整载荷往返 |
| 库存、操作 owner、inbox | 不直接解析旧包，不把普通快照当操作成功，未改库存格式/清理/物化/退款规则 |
| 服务端 OriginalChatAdapter / OriginalTradeAdapter | 翻译后进入同一聊天服务、交易账本；原版条款例外仍在 Adapter/中立条款边界，不增加第二套业务账本 |
| 服务端历史 Rework inline/ref 报价 | LegacyTradeOfferAdapter 整批归一化；引用 owner/session 验证和成功后消费保留 |
| 服务端用户广播 | ToPublicUser 白名单构建快照，广播和历史同步不携带 ClientKey 或 protobuf unknown fields |
| 第三方旧 API 兼容插件 | 原有独立适配器/库存公开 API 继续保留，未改人才交易/红包产品或仓库数据 |

以上是静态核对与下列组件证据，不宣称所有外部服务器变种或第三方二进制组合均实机通过。

## 实际验证

构建/包审计：

```sh
RUN=/home/hunyuan2333/.codex/visualizations/2026/10/15/01a13ec4-18b2-7860-931d-fd14d2747341/p4-work/run
$RUN python3 tools/compatibility/build-dual-host.py \
  --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls \
  --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --output /tmp/phinix-legacy-audit-complete-20261105/build
```

通过：1.6 net472 / 1.7 net10 真实工程构建，完整 Host 包 95 文件布局/程序集目标/ZIP CRC 审计。独立人才交易/红包包不在 Host ZIP 中。

L1/L2 组件：用最终包的两版实际 DLL 闭包和对应游戏引用，分别执行：

```sh
$RUN python3 tools/quality/run-client-component-probes.py --suite legacy-adapter \
  --assemblies /tmp/phinix-legacy-audit-complete-20261105/dll17 \
  --game /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --output /tmp/phinix-legacy-audit-complete-20261105/legacy-runner17
```

1.6 同命令替换为 dll16、GameDlls、legacy-runner16；`--suite trade-composition` 同样两版执行，输出 composition16/composition17。

- Legacy 两版各 **87** 项断言：真实 protobuf/adapter/service、早到同步/交付、换游戏 token、模式选择/重连/停用、整批/空报价、token、真实回执、未知发送结果、物品隧道、聊天时间/文本投影、Host 三条出站循环失败停止。
- Trade composition 两版各 **332** 项断言：实际 journal 填至预留容量上限后 Legacy 四类操作不再写现代 intent，旧文件逐字不变；现代仍先持久化拒绝；原版不查询/ACK，现代 ACK 保留；既有库存范围、恢复、交付组件回归。
- 负对照：新全量同步用例在修复前 DLL 失败（旧状态未移除）；Host 管线回归在修改管线前失败（异常未传播/继续后续处理器）。不是把原错误改成期望。
- 新增可重复入口 `--suite legacy-adapter`，不依赖手写临时工程才能复验。

服务端执行：

```sh
$RUN python3 tools/quality/check.py --suite quick --isolated \
  --output /tmp/phinix-server-legacy-audit-network-20261105
```

**47 个检查步骤通过**，manifest seed `[20261015,20261017]`、faultIds `[]`。含原版聊天/报价、golden、身份、原管线、模型、架构、真实 SQLite 持久化及生命周期检查。首次沙箱运行只有三组套接字测试因 SocketException(13) 失败；开放本机套接字后重新执行上述完整入口通过，未跳过测试。没有云端/游戏验收或漏洞扫描声明。

先前中间构建发现缺少 UserManagement 编译引用，以及 Any.ParseFrom 重载引入 net472 的 System.Memory 编译要求；分别通过现有合同引用和既有 CodedInputStream 解析方式解决。上述最终构建和测试通过，未以失败构建出包。

## 剩余限制与人工验收

1. 原版协议无持久交付 ACK、稳定 operation query 和真正的结果未知核对；不能把发送成功或组件测试当作游戏保存到账。旧服务器在登录发送后如何保留 pending 由其原实现决定；协商暂存是内存有界缓冲，不保证进程崩溃后的接收，也不能补救旧服务器已经遗失的物品。
2. 旧 Unknown/attempt/receipt 和现代 intent 继续保留；本轮不清理、不自动退款、不自动重投。历史 intent 占满时，现代路径仍需权威结果核对/后续安全归档策略，不能无依据删除。
3. 原版服务端只有存在活跃交易时才发 SyncTrades；它根本不发空同步时，客户端不能凭“没收到”推断全部交易已经取消。
4. 聊天本地发送回显不是服务端持久成功；原版 ACK 可能改 message ID，跨重连历史与本地回显的完整 ID 对账尚未建立。回复/提及仅文字展示，不能证明原版提醒、跳转或历史用户名快照能力。
5. 缓冲超限分支已静态核对，未做进程强杀或专门的超限故障压力测试。Windows/Waylith、最新安装游戏 DLL、多人实机、真实空投落地/库存持久化本轮未执行。
6. 工坊热修复分支没有被覆盖；本轮只在业务分支实现。不要单独混用旧 Contracts、LegacyAdapter 和新 Trade DLL，使用完整双版本包测试。

建议用隔离测试存档验证：新端连接原版服务器连续创建/报价/取消/确认（超过此前约数十次操作阈值）；重连恢复活跃交易、离线完成通知；聊天发送/回复/提及/历史；原版与 Rework 混合、双 Rework 两种组合；接收前后换档、保存退出重启。资产结果未知时保留记录和完整日志，不反复提交或手工删除 journal。
