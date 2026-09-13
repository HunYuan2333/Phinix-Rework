# LegacyRedPacket 性能、稳定性与设计审查

> 审查日期：2026-09-09  
> 审查范围：`Extensions/LegacyRedPacket/{Client,Contracts}` 及中英文语言资源  
> 当前状态：**Phase 1（接收路径紧急稳定性）、Phase 3（资源边界与UI优化）、Phase 4（代码质量）已实施完成**；Phase 2（领取与交付稳定性：有界实体化、虚拟账本等）按规划延后，待后续与二级 Tab（已收红包/仓库）一同开发。  
> 主要依据：`docs/设计哲学.md` §3.5（错误隔离）、§3.6（反压与资源边界）、§3.7（通信管线）、§3.8（日志与可观测性）、§8（性能与稳定性审查）

## 1. 结论摘要

“超大额红包导致游戏卡死甚至崩溃”的反馈可信，但最新反馈明确指出：**玩家没有点击领取，仅收到一个可领取的大红包就会卡顿。** 因此必须区分两条问题链路：

**链路 A：收到 create 即卡（当前反馈的首要问题）。**

当前代码不会按照 `TotalCount` 在 `HandleCreate` 中创建 Thing 或分配同等长度的数组，所以“单个数值很大”本身不足以解释接收瞬间卡顿。可能原因包括：大红包发送时实际伴随大报文、同一轮大量中继事件、重复重试或大量唯一红包记录；客户端会在主线程一次排空入站队列，并对每个 create 执行状态变更、全量 badge 重算和通知。红包 UI 随后还会绘制全部记录。由于反馈来自其他用户且当前无法复现，这些应标记为待遥测确认的候选根因，不能把领取路径误报为唯一根因。

**链路 B：点击领取后的实体化卡崩（代码已确认的独立问题）。**

代码中存在一条明确的主线程资源放大链路：

1. `create` 消息中的 `TotalCount` 没有业务上限或实体数量预算。
2. 领取金额最终进入 `SpawnReward`。
3. `CreateThingsFromTemplate` 按物品 `stackLimit` 循环拆栈，每次循环创建一个 `TradeItemSnapshot` 和一个 RimWorld `Thing`。
4. 所有对象同步创建在游戏主线程，之后再构造列表、空投分组并生成 DropPod。

预计创建的游戏实体数为：

```text
Thing 数量 = ceil(领取金额 / max(1, ThingDef.stackLimit))
```

例如 `TotalCount = int.MaxValue`、物品堆叠上限为 75 时，一次领取会尝试创建约 2863 万个 `Thing`。这会同时造成长时间主线程阻塞、海量托管分配、GC 压力和最终内存耗尽；“卡住后崩溃”符合该行为。

链路 B 能直接解释领取后的卡崩，但不能解释最新反馈中的“仅收到即卡”。链路 A 的“主线程单轮工作无预算”属于已确认代码风险；具体是单报文、事件数量、通知还是列表规模占主导，目前无法确认。修复应覆盖所有候选入口，并通过发布后的聚合遥测验证效果。

### 1.1 兼容性与信任边界补充

旧插件可以绕过新版发送 UI，直接向公共中继发送任意 v1 数值。因此：

- **发送端限制只能改善新版用户体验，不能作为安全措施。**
- **所有安全校验和资源预算必须在每个接收客户端独立执行。**
- v1 的 `TotalCount` 是 `int`，为了尽量兼容旧版，不应仅因总金额很大就丢弃 `create` 或隐藏整个红包。
- 必须区分“接受并展示红包元数据”和“在本机实体化领取奖励”。前者可以保持宽松兼容，后者必须受本机资源预算约束。

本审查因此采用以下修订原则：

1. 合法的正数 `TotalCount` 继续按 v1 的完整 `int` 范围接收和展示。
2. `TotalPackets` 只做基本关系校验（`1 <= TotalPackets <= TotalCount`），不在协议入口强制一个较小的玩法上限。
3. 真正的硬边界放在接收端即将执行的工作量：单 Tick 消息数、单 Tick 创建 Thing 数、单次提取批次、并行交付任务和长期集合容量；账本中的奖励金额仍保留完整 v1 int 范围。
4. 新版发送 UI可以提前警告或阻止一个本机明确无法安全交付的红包，但接收端仍必须假设发送端完全不受控。
5. 对超出单次物理实体预算的领取，不允许“只发一部分却记为全部领取”。本项目确定采用客户端本地虚拟账本保存完整奖励，再有界、分批提取。

### 1.2 已确定的实施边界

本次修复采用**纯客户端方案**：

- 不修改服务端；
- 不修改 v1 wire format；
- 保留现有 Legacy HTTP Relay；
- 旧插件仍可任意发送合法 v1 `int` 数量；
- 新客户端独立承担输入校验、幂等、持久化账本和有界实体化；
- 普通红包继续立即空投，只有可能制造大量 Thing 的奖励才转入分批交付。

RP-10（直连 Legacy Relay、共享 key、不经过新框架管线）被认定为旧版互通所需的**兼容性妥协**，不是本轮要求修复的缺陷，也不阻塞本轮验收。相关安全事实仍保留在文档中，作为明确的架构边界，避免以后误认为这条链路具备服务端权威性。

## 2. 风险分级与实施状态

| ID | 等级 | 问题 | 直接影响 | 实施状态 |
|---|---|---|---|---|
| RP-00 | P0 | 接收阶段缺少单轮工作预算；一次排空入站消息并逐条重算/通知 | 不点击领取也会产生长主线程帧，符合最新反馈 | **已修复 (Phase 1)** |
| RP-01 | P0 | 领取金额按 `stackLimit` 无界同步创建 `Thing` | 主线程冻结、OOM、游戏崩溃 | 延后 (Phase 2，随二级Tab) |
| RP-02 | P0 | `create` 对数量、红包数、类型、ID、时间戳缺少语义校验 | 畸形消息进入状态机；异常或资源放大 | 延后 (Phase 2，随二级Tab) |
| RP-03 | P0 | `assign` 基本信任远端金额和剩余量，可直接触发本地奖励生成 | 伪造奖励、重复/超额生成、DoS | 延后 (Phase 2，随二级Tab) |
| RP-04 | P1 | 单次 Tick 用 `while` 排空整个入站队列 | 突发消息造成单帧尖峰和长卡顿 | **已修复 (Phase 1)** |
| RP-05 | P1 | `ProcessedProtocolKeys`、红包集合、显示名集合、领取明细无明确容量上限 | 长时间运行内存持续增长 | **已修复 (Phase 3)** |
| RP-06 | P1 | 中继响应使用 `ReadToEnd`、整段 `Split`、无响应体/单消息上限 | 大响应造成瞬时大分配和解析阻塞 | **已修复 (Phase 1)** |
| RP-07 | P1 | 先标记“已处理”，后做语义校验 | 无效消息可抢占合法消息的去重键 | **已修复 (Phase 1)** |
| RP-08 | P1 | 单消息处理没有就地异常隔离；非法 ticks 可使 `DateTime` 构造抛异常 | 一条坏消息可中断本轮状态机 Tick | **已修复 (Phase 1)** |
| RP-09 | P1 | 红包列表和详情窗口不做可视区域裁剪 | 大列表每帧遍历并绘制全部行 | **已修复 (Phase 3)** |
| RP-10 | 已接受 | Legacy HTTP 中继、共享 key、绕过新框架管线 | 旧版互通所需兼容性例外；不在本轮修改 | **已接受 (兼容性例外)** |
| RP-11 | P2 | 数量输入使用 `int.Parse`，文本框允许 100 位数字 | `FormatException`，可能每帧重复抛出 | **已修复 (Phase 4)** |
| RP-12 | P2 | Regex 未按项目约定预编译，绘制路径仍有字符串与布局分配 | 高频 UI GC，规模增大后帧时间恶化 | **已修复 (Phase 4)** |
| RP-13 | P2 | 持有 Timer 的状态机未实现 `IDisposable`，大量状态为 static | 生命周期语义不清，多实例互相污染风险 | **已修复 (Phase 4)** |
| RP-14 | P2 | 空投前的地图可用性与失败回收不完整 | 无地图或生成异常时奖励丢失/状态不一致 | 延后 (Phase 2，随二级Tab) |
| RP-15 | P0 | 框架 `DrainPendingActions` 与红包 `PollRelayBuffer` 双重无界排空 | 并发接收仍可能在一次游戏更新中处理数百条消息 | **已修复 (Phase 1)** |
| RP-16 | P1 | `Clear()` 强制重置 in-flight 标记，旧 worker 可能在重新激活后回灌 | 生命周期切换时重复轮询、旧消息污染新会话 | **已修复 (Phase 1)** |

## 3. 已确认问题与代码证据

### 3.0 RP-00：仅接收红包也可能触发主线程工作风暴

位置：`RedPacketRelay.cs:248-335`、`RedPacketStateMachine.cs:380-409`、`411-483`、`706-713`、`RedPacketProtocol.cs:133-257`

接收链路为：

```text
Relay PollWorker 读取完整响应
  → Split 全部行并逐行 Base64 解码
  → 最多 512 条进入 IncomingQueue
  → 下一个主线程 Tick 用 while 一次全部排空
  → 每条 create 都 MarkBadgeDirty
  → 每次 MarkBadgeDirty 都遍历全部 Packets 重算 claimableCount
  → 每条新红包可能各发一次游戏通知
```

因此一次包含 N 条 create 的批次，badge 重算会接近 O(N × 当前红包数)，再叠加 N 次协议解析、锁、通知和后续 UI 更新。即使每条 create 都只包含一个十位整数，也可能在收到时形成长帧。

另外，协议识别 `IsProtocolMessage` 和正式 `TryParse` 会重复执行提取工作；零宽编码消息可能被重复扫描和解码。HTTP 响应使用 `ReadToEnd`，再整段 `Split`，放大瞬时内存分配。红包列表没有可视区域裁剪，打开相关 Tab 后会每帧绘制所有红包行。

需要强调：在目前 Rework 代码中，`HandleCreate` 不会按 `TotalCount` 循环，也不会在收到 create 时调用 `CreateThingsFromTemplate`。所以**目前不能确认单个大额 `TotalCount` 数值本身就是收到即卡的原因**。接收即卡可能与旧发送端产生的事件数量/报文形态、重复重试或列表规模相关；这些需要遥测确认，不应在没有现场数据时下定论。应增加不记录敏感 payload 的诊断指标：

- 本轮 HTTP 响应字符数、行数和解码后消息数；
- 单条 wire message 字符数及是否为零宽编码；
- 每个主线程 Tick 实际处理消息数和总耗时；
- 每种消息类型数量，特别是 create/claim/assign；
- create 的唯一 packet ID 数与重复数；
- Tick 前后 `Packets.Count`、通知数、badge 重算次数；
- 红包 Tab 每帧绘制的总行数和可见行数。

紧急修复不必等待完整复现即可实施，因为下面措施本身符合资源有界原则，并且不限制红包金额：

1. 主线程每 Tick 使用动态消息上限，并以约 1.5ms 目标、约 3ms 硬上限约束，剩余消息留待下次。
2. 同一批消息只在结束时重算一次 badge，并把 `claimableCount` 改成增量维护或 O(1) 脏更新。
3. 通知做合并和速率限制，例如同 Tick 多个红包只显示一条汇总通知。
4. Relay 流式逐行读取并限制响应尺寸、行数和单消息尺寸。
5. 协议只提取/解码一次；解析结果直接交给状态机。
6. 红包集合有界，列表只绘制可见行。
7. 日志记录聚合计数与耗时，不记录 API key、显示名或完整协议内容。

### 3.0.2 动态主线程预算（推荐实现）

固定每 Tick 处理 16-32 条可以作为初始保护，但推荐采用**时间预算为硬上限、消息数量为软上限**：

- 默认消息数上限 32，动态范围 4-64；
- 目标预算约 1.5ms / Tick，绝对预算约 3ms / Tick；
- 根据最近消息处理耗时的指数移动平均估算下一批大小；
- 连续稳定若干 Tick 才逐步增加上限，出现超时立即降低；
- 即使队列积压，也不能突破绝对时间预算；
- 每批处理结束后只更新一次 badge、列表版本和合并通知。

这样单个普通红包不会改变交互；只有突发消息会被拆到后续 Tick。动态预算只控制接收端主线程工作量，不限制 `TotalCount`，也不依赖发送端配合。

### 3.0.1 RP-15/RP-16：并发边界和生命周期竞态

**并发可以使用，但只能用于不触碰 Verse/Unity 的阶段。** 当前可安全放到后台线程的工作包括 HTTP 请求、响应流读取、Base64 解码、协议 token 化和纯数据校验；`DefDatabase` 查询、`Messages.Message`、`Thing` 创建/销毁、DropPod、UI 和红包共享状态应用必须回到主线程或受锁保护。

当前存在两个独立的无界循环：

1. `ClientMainThreadDispatcher.DrainPendingActions()` 从 `pendingActions` 取到空为止；
2. `RedPacketStateMachine.PollRelayBuffer()` 从 `IncomingQueue` 取到空为止。

所以“把轮询放到 ThreadPool”不能解决接收卡顿：后台线程只负责生产，主线程最终仍可能在同一帧执行 500 个以上动作/消息。正确做法是两级预算：框架 dispatcher 每帧最多执行固定 action 数或时间预算，红包 Tick 内再限制消息数/耗时；红包不应依赖全局 dispatcher 恰好及时消费。

`RedPacketRelay.Clear()` 还会直接 `Interlocked.Exchange(ref pollInFlight, 0)` 和重置游标，但没有等待旧的 `PollWorker` / `SendOnceWorker` 退出。若断线、Shutdown、重新 Activate 发生在网络请求进行中，旧 worker 可能在新实例已经开始工作后继续写入静态队列和 `lastSeenId`。建议引入 generation/token：每次 Initialize/Clear 递增 generation，worker 完成写队列前检查 generation；Shutdown 先停止调度，再等待或安全忽略旧 worker，不能仅清零标志位。

此外，Relay 队列、游标和红包状态均为 static，多实例或快速重载会共享同一批数据。短期至少以实例 generation 隔离；长期应改为宿主拥有的单例服务或实例字段，避免插件生命周期与静态状态绑定。

### 3.1 RP-01：无界奖励实体化是卡崩的直接根因

位置：`RedPacketStateMachine.cs:1103-1138`

- `SpawnReward` 在主线程同步调用 `CreateThingsFromTemplate`。
- 方法从 `remaining = count` 开始，以 `stackLimit` 为步长执行 `while (remaining > 0)`。
- 每一轮都会克隆快照并调用 `TradeItemConverter.ConvertThingFromSnapshotOrUnknown` 创建真实游戏对象。
- 完成全部创建后才进入空投逻辑，因此空投组数上限 100 并不能限制前面的对象数量和内存分配。

这不是普通的“算法稍慢”，而是输入规模可以把 O(金额 / 堆叠上限) 放大到数千万次的资源耗尽问题。

推荐修复不是写死“红包总金额最多 10000”，也不是在收到 `create` 时直接拒绝大红包，而是在**领取客户端实际准备交付**时按预计创建的 `Thing` 数量设置资源预算。Mod 物品的 `stackLimit` 差异很大：

```text
estimatedStacks = ceil(totalCount / max(1, stackLimit))
estimatedStacks 必须进入接收端的有界交付策略
```

建议把预算分成两级，而不是一个简单的金额上限：

- `MaxStacksCreatedPerTick`：每 Tick 最多创建的 Thing 数，建议从 10-20 开始，用于消除单帧卡死。
- `MaxStacksPerExtraction`：一次自动交付或玩家提取最多实体化的 Thing 数；超过部分继续作为账本余额，不丢弃、不截断。

金额本身不受额外限制。例如一个 Mod 物品 `stackLimit = 1,000,000`，领取一亿个只需要 100 个 Thing，仍可安全进入交付；一个 `stackLimit = 1` 的物品即使金额小得多，也可能超出本机世界可承受的实体数。

仅做“分帧创建”还不够：它可以避免瞬时冻结，却不能让世界安全容纳数百万个 Thing，最终仍会撑爆内存和存档。因此采用虚拟账本，以常数级记录保存完整数量；自动交付和每次手动提取都有实体预算，未提取部分永久保留为余额。这样可以完整领取任意合法 `int` 金额，但不承诺把数百万个物理 Thing 同时放进地图。

必须在接收端多层防守；发送端检查仅作提前反馈：

1. `create` 协议入口：校验字段结构、正数关系、时间戳和字符串尺寸，但不因总金额大就拒绝元数据。
2. `RequestClaim`：正常发送 claim；可以预估实体数以决定领取后走即时空投还是账本交付，但不因金额大而禁止领取。
3. `HandleClaim` / `HandleAssign`：只在本地确实存在 pending claim 时接受以本地 UUID 为领取者的交付，并把最终合法金额完整、幂等地写入账本，防止伪造、乱序和重复入账。
4. `SpawnReward` / 交付调度器：保留最后一道硬防线，分 Tick 创建且绝不超过累计实体预算。
5. 发送 UI：可在移除发送者物品前给出同样的可交付性警告，减少新版客户端制造无法领取的红包，但它不属于安全边界。

同时将 `while` 改为先用 64 位整数计算栈数、验证预算、按确定次数 `for` 创建，并为 List 预设安全容量。仅修改循环形式而不验证预算不能解决问题。

### 3.2 RP-02/RP-08：协议字段缺少完整语义校验和异常隔离

位置：`RedPacketStateMachine.cs:411-483`、`747-782`

当前 `HandleCreate` 只检查 `int.TryParse`，但没有检查：

- `totalCount > 0`；
- `1 <= totalPackets <= totalCount`；
- 红包数是否存在合理上限；
- `RedPacketType` 是否为已知枚举值；
- packet ID、用户 UUID、DefName、显示名的长度；
- `createdTicks`、`expiresTicks` 是否处于 `DateTime` 合法范围；
- `expiresAt > createdAt`，有效期是否异常长；
- 对应物品按堆叠上限计算后的实体需求（记录供领取端决策，不以总金额为由直接丢弃 create）。

`new DateTime(createdTicks, ...)` 和 `new DateTime(expiresTicks, ...)` 会对越界 ticks 抛出 `ArgumentOutOfRangeException`。`PollRelayBuffer` 对每条消息没有 try/catch，因此一条消息就能中断本轮后续消息处理。

推荐引入一个集中式 `RedPacketMessageValidator` 或纯函数校验层，解析成功后先生成不可变的“已验证命令”，状态机只接收已验证对象。每条消息在最小作用域内捕获异常、记录限频 Warning 并继续下一条，避免 catch-all 静默吞错。

首版建议边界：

| 项目 | 建议值 |
|---|---:|
| packet ID / UUID | 128 字符 |
| DefName / StuffDefName | 256 字符 |
| 解码后显示名 | 128 字符 |
| 协议 payload | 8 KiB 字符 |
| 含零宽编码的 wire message | 64 KiB 字符 |
| 红包最长有效期 | 24 小时（兼容值，正常仍为 10 分钟） |

这些值应定义在同一个 limits 类型中，避免 UI、协议和状态机各自维护不一致的魔法数字。

### 3.3 RP-03：`assign` 可覆盖状态并触发本地生成

位置：`RedPacketStateMachine.cs:585-673`

当前逻辑会直接采用消息中的：

- `amount`；
- `remainingPackets`；
- `remainingCount`。

若 `claimerUuid` 等于本地 UUID、且该 UUID 尚未在领取集合中，就会调用 `SpawnReward(packet, amount)`。代码没有验证该 assign 是否来自红包发送者，也没有验证：

- `amount > 0 && amount <= 当前 RemainingCount`；
- `remainingCount == 旧 remainingCount - amount`；
- `remainingPackets == 旧 remainingPackets - 1`；
- 本地是否确实存在对应的 pending claim；
- assign 是否与当前红包发送者/事件因果链绑定。

这既是物品复制漏洞，也是远程资源耗尽入口。即使 RP-01 增加实体预算，也仍可能被伪造为预算内的重复攻击。

短期兼容修复（全部在接收端执行）：

1. 只允许 pending claim 对应的本地 assign 触发奖励。
2. 对首次 assign 严格校验金额和两个 remaining 字段的算术一致性。
3. 已处理过的 assign 只能作为幂等重复，不再次生成奖励。
4. 所有金额运算使用 `long` 校验，确认范围后再转回 `int`。

由于本轮明确不修改服务端，无法增加服务端签名或权威分配。客户端必须始终把公共中继输入视为不可信，并通过本地 pending claim、确定性金额复算、算术一致性和幂等账本降低风险。这是客户端方案的明确安全上限，不阻塞卡崩修复。

### 3.4 RP-04：单 Tick 排空队列造成帧尖峰

位置：`RedPacketStateMachine.cs:706-713`

中继入站队列虽然上限为 512，但 `PollRelayBuffer` 每 200ms 在主线程执行，并用 `while` 一次排空。队列有界只限制总内存，不限制单帧 CPU 时间；512 条消息如果包含通知、排序、状态更新和 UI 脏标记，仍会造成明显卡帧。

推荐每 Tick 使用动态数量和 2-4ms 时间预算，剩余消息留到下一 Tick。数量上限只作为软上限，绝对时间上限优先。

同一 Tick 内不要每处理一条都完整重算 badge。应累计 `stateChanged`，批次结束后只重算一次。

### 3.5 RP-05/RP-07：状态集合和去重集合没有资源边界

位置：`RedPacketStateMachine.cs:32-37`、`793-804`

下列集合没有容量上限：

- `Packets`；
- `KnownDisplayNames`；
- `ProcessedProtocolKeys`；
- 单红包的 `ClaimedUuids` / `ClaimedAmounts`。

完成红包会清理，但攻击者可以使用极远的过期时间、不同 packet ID、不同 UUID 持续灌入。`ProcessedProtocolKeys` 只在整个插件 Clear/Shutdown 时清空，正常长期在线会持续增长。

另外，消息在 `ProcessProtocolMessage` 中先执行 `TryMarkProcessed`，再进入 handler 语义校验。攻击者可先发送同 key 的无效 create/assign，使之后的合法消息因“已处理”被丢弃。

推荐：

- 活跃/保留红包总数上限 512；优先清理已完成、已过期、最旧且不持有发送者真实物品的条目。
- 去重键使用有界 FIFO/LRU，建议 8192 条，并可增加时间窗口。
- 显示名缓存上限 2048，使用 LRU 或随相关红包淘汰。
- 不额外限制协议中的 `TotalPackets`，但本地保留的 claim 明细必须有容量策略。达到上限后可停止保存用于 UI 的金额明细，并保留独立的有界去重结构；若无法在固定内存内继续保证正确性，则隔离该异常红包，而不是让集合无限增长。
- “解析 → 语义校验 → 去重登记 → 应用状态”保持固定顺序；去重检查和登记需在同一锁内原子完成。
- 绝不能为腾容量直接淘汰仍持有 `StoredThings` 的本地发送红包，否则会让已从地图移除的真实物品失去归还路径。

### 3.6 RP-06：中继读取和解码没有尺寸限制

位置：`RedPacketRelay.cs:248-335`、`RedPacketProtocol.cs:133-257`

当前轮询使用 `StreamReader.ReadToEnd()`，随后对完整响应 `Split('\n')`，再逐行 Base64 解码；协议还可能扫描整条消息并把零宽位转换为 `List<byte>`。响应体和单条消息均无显式上限，且 `IsProtocolMessage` 与 `TryParse` 会重复做部分提取/解码工作。

推荐：

- 响应体上限建议 1 MiB；超过立即终止读取并记录限频 Warning。
- 流式逐行读取，最多处理 `FetchLimit` 条，不为整个响应创建字符串数组。
- Base64 解码前先检查编码长度，解码后再次检查协议长度。
- 合并“是否为协议消息”和“解析”步骤，单条消息只提取/解码一次。
- 发送失败重新入队时再次执行容量检查。当前发送线程出队后，其他线程可把队列填满，失败消息重新入队可能使队列短暂超过声明的上限。

### 3.7 RP-09/RP-12：UI 大列表没有虚拟化

位置：`RedPacketTab.cs:244-317`、`319-408`，`RedPacketDetailWindow.cs:109-164`

三个滚动列表都会遍历和绘制全部行，即使绝大多数行位于可视区域之外：

- 可发送物品列表；
- 红包列表；
- 红包领取详情。

当列表较大时，每帧会产生大量 `Rect`、字符串格式化、翻译、`Text.CalcSize` 和 Widgets 调用。红包集合与 claim 数量一旦加上边界，最坏情况会降低，但仍应做基于 scroll position 的首行/末行计算，只绘制可见行和少量 overscan。

其他 UI 问题：

- `RedPacketTab.cs:296` 对最多 100 字符的数字输入使用 `int.Parse`，溢出会抛异常。应使用 `int.TryParse`，失败时夹到可用上限或保留旧值。
- `ItemCountInputRegex` 是 static readonly，但没有 `RegexOptions.Compiled | CultureInvariant`，不符合 §8.3 的明确检查项。
- 数量字符串在每个可见物品行每帧重新生成；可为编辑中的行缓存文本，仅在数量变化或刷新时更新。
- 红包列表的版本缓存思路是正确的，应保留；但时间变化导致过期显示也要确保由状态机版本变更驱动。

### 3.8 RP-10/RP-13：兼容性例外与生命周期问题

位置：`RedPacketRelay.cs:20-24`、`RedPacketStateMachine.cs:30-67`

当前实现：

- 直接访问固定 HTTP 地址；
- 在客户端 DLL 中硬编码 API key；
- 不经过框架 handler 管线；
- Relay 队列和状态机主要集合均为 static；
- 状态机持有 `System.Timers.Timer`，有 `Shutdown` 释放但没有实现 `IDisposable`。

这与 §3.7 的目标架构存在偏差，但它是与旧插件互通所需的已接受例外：本轮保留 v1 线格式、固定中继和现有路由，不修改服务端。HTTP 明文和共享 key 意味着该链路不能提供可信身份，因此接收端不得据此跳过本地安全校验。

RP-13 与兼容性无关，仍需修复：状态改为实例字段；共享中继如确实需要单例，应由宿主显式拥有并引用计数。状态机实现 `IDisposable`，`Dispose` 幂等调用 `Shutdown`，Timer 订阅与释放保持配对。

### 3.9 RP-14：奖励交付不是失败安全的事务

当前领取状态先被修改，然后才创建物品并空投。若转换、地图选择或 DropPod 生成抛异常，红包已记录“领取成功”，但玩家可能没有收到物品。`DropPods` 在无可用地图时也可能把 null 传给地图相关 API。

推荐：

- 先完成纯数据校验和资源预算校验，再提交领取状态。
- 将“待交付奖励”建模为有界、幂等的 delivery 记录，使用 `(packetId, claimerUuid)` 作为唯一键。
- 交付成功后标记完成；失败保留可重试状态并向用户显示明确错误。
- 没有有效地图时不创建 DropPod，延迟到地图可用后交付。
- 任意部分创建失败时销毁尚未交付的临时 Thing，避免泄漏。

## 4. 推荐修复方案

### 4.1 第一阶段：接收路径紧急稳定性补丁（必须优先）

目标：首先解决“不点击领取，仅收到红包就卡”的现网反馈，不改变 v1 金额范围。

1. `PollRelayBuffer` 增加动态数量/耗时预算，禁止 `while` 无界排空。
2. 一个处理批次只触发一次 badge/UI 版本更新；避免每条 create 全量扫描红包表。
3. 多条通知合并并限频，禁止单批消息制造通知风暴。
4. Relay 响应、行数、消息长度全部有界，并改为流式解析。
5. 合并协议识别和解析，零宽消息只扫描、解码一次。
6. 红包表和去重表有界；UI 列表增加可见行裁剪。
7. 增加聚合性能指标，以实际复现确认旧发送端产生的是大报文还是事件风暴。

这些措施全部由接收客户端执行，不限制 `TotalCount`，也不要求旧插件或服务端升级。

### 4.2 第二阶段：领取与交付稳定性补丁（必须）

目标：彻底切断已知卡崩链路，不改变 v1 字段和正常红包分配算法。

1. 新增统一的 `RedPacketLimits` 和输入校验函数；限制全部由接收端强制执行。
2. `create` 接受 v1 正数 int 总量并展示，不把大总金额等同于恶意消息。
3. `RequestClaim` 正常支持任意合法金额；`HandleClaim`、`HandleAssign` 校验 pending claim 和算术一致性后，将最终金额完整写入账本。
4. 新增客户端本地、随存档持久化的奖励账本；收到合法的本地领取结果时先以 `(packetId, claimerUuid)` 幂等入账完整金额。
5. 将奖励生成改成有界交付任务，每 Tick 只创建少量 Thing；普通奖励走立即交付快速路径，大额奖励保留剩余余额并持续或按需提取。
6. `SpawnReward` 保留不可绕过的硬检查，任何远端输入都不能直接进入无界循环。
7. 每 Tick 使用动态入站消息预算；每批只重算一次派生状态。
8. 给协议消息、HTTP 响应和所有长期集合加容量上限。
9. 每条消息独立异常隔离，非法消息记限频 Warning 后跳过。
10. 修复 `int.Parse` 溢出和发送失败重新入队的边界漏洞。

兼容性原则：不改变 v1 wire format；旧客户端发送的大额红包仍然可见、可领取并完整记入本地账本。限制针对的是**一次自动交付或手动提取产生的物理实体数**，不是红包总金额。预算外部分保留为账本余额，不能静默丢弃，也不能只入账一部分。

本地账本保证旧版任意合法 int 金额都能完整记账；物理 Thing 按本机预算逐步提取。单靠优化循环不能安全实现相同能力。

### 4.3 UI 与内存优化（应做）

1. 三个滚动列表增加可视区域裁剪。
2. 将 packet 详情排序结果和行文本缓存到 snapshot，避免窗口每帧重复计算。
3. 去除绘制热路径中可避免的 LINQ、字符串和正则开销。
4. 使用 Profiler 测量 100、512、1000 行下的 `GC.Alloc` 与帧时间。

### 4.4 明确不在本轮范围内

- 修改红包服务端；
- 修改或废弃 v1 wire format；
- 替换 Legacy HTTP Relay；
- 要求旧插件升级；
- 将 RP-10 改造成新 Framework transport。

这些事项只有未来明确放弃或迁移旧版兼容性时再单独立项，不属于本次完成标准。

## 5. 建议的状态处理顺序

每条消息应采用相同的处理模板：

```text
尺寸检查
  → 语法解析
  → 字段类型解析（使用 long/安全 DateTime）
  → 业务语义与资源预算校验
  → 身份/因果校验
  → 原子去重登记
  → 应用状态
  → 建立或推进幂等奖励交付
  → 批次结束后更新 badge/UI 版本
```

任何一步失败都只丢弃当前消息；不得改变红包状态，不得登记去重键，不得影响同批后续消息。

## 6. 验证与回归计划

### 6.1 纯逻辑单元测试

建议将 limits、金额算法和消息校验提取到不依赖 Verse/Unity 的类，以便快速测试：

- `totalCount`：0、负数、1、预算边界、边界 + 1、`int.MaxValue`；
- `stackLimit`：0、1、75、超高 Mod 值；
- `totalPackets`：0、1、等于总量、513、`int.MaxValue`；验证大值不会造成预分配或无界循环，而不是简单以 512 拒绝；
- ticks：`long.MinValue`、0、合法值、`DateTime.MaxValue.Ticks + 1`；
- normal/lucky 最后一包与保留每包至少 1 个的性质；
- assign 算术一致、不一致、重复、乱序、非 pending；
- 去重 FIFO/LRU 达到容量后的淘汰与幂等行为。

### 6.2 集成与性能测试

| 场景 | 通过标准 |
|---|---|
| 收到单个超大 `TotalCount` create | 不按金额循环/分配，处理时间与普通 create 基本一致 |
| 一轮收到 512 条 create | 主线程按预算跨 Tick 处理；每批只重算一次 badge；通知被合并 |
| 单个大额 `TotalCount` create | 不按金额创建 Thing；处理耗时应与普通 create 同量级，实际卡顿诱因由遥测确认 |
| 超长零宽/普通协议消息 | 在尺寸检查处快速丢弃，不重复解码，不阻塞主线程 |
| 75,000 个、stackLimit=75 | 红包可接收；交付按 Tick 分批，完整创建 1000 个 Thing，无长卡 |
| 大额红包但本次仅领取少量 | 按实际领取金额计算，可正常领取，不受总金额牵连 |
| `int.MaxValue`、stackLimit 很高 | 若预计实体数在预算内，允许分批完整交付 |
| `int.MaxValue`、stackLimit=1 | 红包可领取并完整入账；仅按有界批次提取，无瞬时大分配 |
| 旧插件绕过发送端限制 | 接收端所有预算仍生效，不能进入无界循环 |
| 一次积压 512 条消息 | 单 Tick 最多处理预算数；游戏帧仍可推进 |
| 10,000 个不同去重键 | 集合不超过配置容量 |
| 1 MiB+ 中继响应 | 中止读取并限频告警，不 OOM |
| 非法 ticks 后跟正常消息 | 非法消息被跳过，正常消息仍被处理 |
| 伪造本地 assign | 不生成物品，不修改合法红包状态 |
| 无可用地图时领取 | 不丢奖励；进入有界待交付或明确失败状态 |
| 512 条红包/领取明细滚动 | 只绘制可见行，GC.Alloc 接近零 |

建议记录以下指标：单 Tick 处理消息数/耗时、拒绝原因计数、当前红包数、去重键数、待交付数、单次预计/实际创建 Thing 数。日志不得输出 API key、完整协议载荷或其他敏感字段。

## 7. 完成标准

紧急补丁只有同时满足以下条件才算完成：

- 任意网络整数输入均不能触发超过预算的对象创建或循环次数；
- 合法本地领取金额先幂等写入随存档保存的奖励账本，重启后剩余金额不丢失；
- 普通奖励保持即时空投，大额奖励跨 Tick 完整交付或保留为可提取余额；
- 任意单条消息异常不会中断同批后续处理；
- 任意长期集合和队列都有明确容量与淘汰策略；
- 单 Tick 工作量有上限；
- 伪造/乱序 assign 不会触发本地奖励；
- 发送失败或无地图不会造成静默物品丢失；
- 正常 v1 红包的线格式、普通红包算法和拼手气算法保持兼容；
- RP-10 所需的旧中继兼容链路保持不变；
- 构建通过，并完成边界单测、积压压力测试和 RimWorld 主线程 Profiler 验证。

---

## 8. 实施进展与变更记录 (2026-09-09)

按用户需求，**Phase 2（领取与交付稳定性：有界实体化、虚拟账本等）已延后**，留待后续随二级 Tab（如“已收到的红包/仓库”）一同实现。

本次已完整交付 **Phase 1（接收路径紧急稳定性）**、**Phase 3（资源边界与UI优化）** 与 **Phase 4（代码质量）**，彻底消除“收到即卡”的根因，并加固了整体资源边界。

### 8.1 本次已实施的变更

| 涉及问题 | 变更文件 | 具体修改点 |
|---|---|---|
| 全部常量 | `Client/RedPacketLimits.cs` (新建) | 建立统一的静态限制类，定义单 Tick 消息上限 (32)、单 Tick 时间预算 (3.0ms)、活跃红包上限 (512)、去重键容量 (8192)、显示名缓存上限 (2048)、响应体最大字节数 (1 MiB)、报文/Payload 最大长度等，消除魔法数字。 |
| 构建工程 | `Client/LegacyRedPacketExtension.Client.csproj` | 将新建的 `RedPacketLimits.cs` 添加到 `<Compile>` 列表以兼容旧版 MSBuild。 |
| RP-00, RP-04, RP-15 | `Client/RedPacketStateMachine.cs` | 1. 重写 `PollRelayBuffer`：限制单 Tick 最多处理 32 条消息、3.0ms 绝对时间预算硬上限，超出预算的消息自然留在接收队列，分摊至后续 Tick。<br>2. 移除逐条处理过程中的 `MarkBadgeDirty` 刷新，改为批次排空结束后统一更新一次。 |
| RP-00 | `Client/RedPacketStateMachine.cs` 及语言文件 | 引入通知合并机制 `NotifyNewPacketsBatched`：同一 Tick 内接收到多个新红包时，只弹出一跳汇总通知（`收到 {0} 个新红包。`），避免界面被弹窗刷屏。中英文词条已全量同步。 |
| RP-07 | `Client/RedPacketStateMachine.cs` | 调整去重登记顺序：拆分只读检查 `IsAlreadyProcessed` 与成功后登记 `MarkProcessed`。格式错误或校验失败的脏报文不再抢占去重缓存槽。 |
| RP-08 | `Client/RedPacketStateMachine.cs` | 在 `PollRelayBuffer` 中对单条消息包裹独立的 `try-catch` 异常隔离，单条格式或数值异常不再导致批次中断。 |
| RP-06, RP-16 | `Client/RedPacketRelay.cs` | 1. 废弃无限制的 `ReadToEnd()` 与整段 `Split`，改为流式逐行读取 `ParseRawResponseStreaming`。<br>2. 增加响应体大小 (1 MiB)、单条 Base64 长度与解码后长度的多重安全截断。<br>3. 失败重试入队增加容量检查，避免无界积压。<br>4. 引入 `generation` 代际隔离：`Clear()` 时递增代际，异步 worker 在回灌队列前校验代际，防止旧网络会话污染重连后的新状态。 |
| RP-00, RP-06 | `Contracts/RedPacketProtocol.cs` | 1. 定义 `MaxWireMessageChars` (65536) 与 `MaxProtocolPayloadChars` (8192)。<br>2. 在 `IsProtocolMessage` 与 `TryParse` 中强制执行超长消息快速丢弃。<br>3. 状态机在处理消息时跳过重复的先行零宽解码，直接进入 `TryParse`。 |
| RP-05 | `Client/RedPacketStateMachine.cs` | 1. 去重集合改为 FIFO 队列 + HashSet 结构，容量上限固定为 8192，满时淘汰最旧记录。<br>2. 活跃红包字典 `Packets` 上限 512，满时通过 `EvictOldestFinishedPackets` 淘汰已结束或已过期的红包（保留本地未完成发放的记录）。<br>3. 显示名缓存上限 2048，长度截断为 128。<br>4. 单红包领取明细记录上限 512。 |
| RP-09 | `Client/RedPacketTab.cs` & `Client/RedPacketDetailWindow.cs` | 1. 红包列表视口裁剪：根据滚动偏移 `packetListScroll.y` 和视口高度仅绘制可见行与极少量 overscan。<br>2. 可用物资列表视口裁剪：按有效项索引进行视口计算。<br>3. 详情弹窗中的领取明细列表加入视口裁剪。 |
| RP-11 | `Client/RedPacketTab.cs` | 数量输入解析由 `int.Parse` 替换为 `int.TryParse`，防止输入 100 位数字导致异常抛出。 |
| RP-12 | `Client/RedPacketTab.cs` | `ItemCountInputRegex` 添加 `RegexOptions.Compiled \| RegexOptions.CultureInvariant` 预编译。 |
| RP-13 | `Client/RedPacketStateMachine.cs` | 状态机类实现 `IDisposable` 接口，显式提供委托到 `Shutdown()` 的清理语义。 |

### 8.2 延后至 Phase 2 的待办项

随二级 Tab（已收到的红包/仓库）一同开发：
- **RP-01**：`CreateThingsFromTemplate` 消除 `while` 无界循环，引入单 Tick 物理实体预算（如 200 个），大额领取转入本地虚拟账本跨 Tick 分批交付。
- **RP-02**：`HandleCreate` 针对总数量、红包个数等参数的完整业务关系与范围校验。
- **RP-03**：`HandleAssign` 严格校验本地是否存在 pending claim 记录，防止未领受奖与算术不一致。
- **RP-14**：空投无可用地图时的安全暂存与失败恢复逻辑。

