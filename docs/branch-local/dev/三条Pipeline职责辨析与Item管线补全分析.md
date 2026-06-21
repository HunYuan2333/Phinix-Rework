# 三条 Pipeline 职责辨析与 Item 管线补全分析

> 2026-06-01，基于实际代码 + Talent-Trade submod 迁移需求编写。

---

## 1. 三条 Pipeline 的实际职责（经验证）

### 1.1 Message — 展示流

**一句话：** 传输"用户应该看到的东西"。

**路由机制（完整）：**

| 端 | 入站 | 出站 |
|----|------|------|
| Client | `packetHandler → KindMessage → handleMessage()` → `IClientMessageHandler` 链 → `IMessageRenderer` → `FrameworkDisplayMessage` → UI | `IClientMessageHandler.HandleOutgoingText()` → `TryHandleOutgoingMessage()` → `sendPacket()` |
| Server | `handleMessage()` → `IServerInboundMessageInterceptor` → `IServerDefaultMessageHandler` → `IServerMessageObserver` → `DispatchOutbound` | `DispatchOutbound` → `SendPacket` |

**产出物：** `FrameworkDisplayMessage`（展示用，可渲染到 UI）

**典型使用者：** 聊天消息、系统通知、任何需要被"看到"的内容

---

### 1.2 Command — 控制流

**一句话：** 传输"系统应该执行的操作"。

**路由机制（完整）：**

| 端 | 入站 | 出站 |
|----|------|------|
| Client | `packetHandler → KindCommand → handleCommand()` → `IClientCommandHandler` 链 | `IClientOutgoingCommandHandler` → `TryHandleOutgoingCommand()` → `sendPacket()` |
| Server | `handleCommand()` → `IServerInboundCommandInterceptor` → `IServerDefaultCommandHandler` → `IServerCommandObserver` → `DispatchOutbound` | `DispatchOutbound` → `SendPacket` |

**产出物：** 无展示产物（handler 修改内部状态，可间接触发后续 Message）

**典型使用者：** Trade 状态同步（创建交易/更新物品/接受/取消）、历史同步请求、能力协商

---

### 1.3 Item — 物品载荷流（半成品）

**一句话：** 传输"可编解码的物品/实体数据"。

**当前路由机制：**

| 端 | 入站 | 出站 |
|----|------|------|
| Client | ❌ **不存在**。`packetHandler` 没有 `KindItem` 分支 | ❌ 不存在。无 `TryHandleOutgoingItem` |
| Server | `handleItem()` → `ProcessIncomingItem()` → codec decode → **丢弃** | 无独立出站 |

**当前实际的 Item 传输方式：** Item 数据（`FrameworkItemPayload`）作为 **Command 的嵌套载荷** 传输：

```
FrameworkPacket (Kind=command)
  └─ PayloadJson = JSON(FrameworkTradeOfferUpdateRequest)
       └─ Items = List<FrameworkItemPayload>
            └─ PayloadBytes = protobuf(FrameworkVanillaItemData)
```

**这意味着是什么：**
- Item 不是独立路由的——它寄生在 Command 管线内部
- 服务端 `ProcessIncomingItem` 解码后**丢弃结果**（只做 round-trip 验证）
- 没有 interceptor/handler/observer 链——submod 无法插手 Item 处理
- 客户端没有 Item 路由——Item 数据从不独立到达客户端，总是嵌套在 Command 中
- **三层 JSON 序列化**：FrameworkVanillaItemData(protobuf) → FrameworkItemPayload(JSON) → FrameworkTradeOfferUpdateRequest(JSON) → FrameworkPacket(JSON)，加上 `byte[]` 字段的 33% base64 膨胀

---

## 2. 为什么 Item 管线需要补全（以 Talent-Trade 为例）

### 2.1 Talent-Trade 的活体交易数据结构

```
Pawn 序列化流程:
  Pawn → Scribe XML (Scribe.saver.DebugOutputFor)
       → UTF-8 字节
       → GZip 压缩
       → Base64 字符串
       
  单条 Pawn: 10-100KB (压缩后)
  带 blob 分片: 任意大小
```

**三种交易模式的数据需求：**

| 模式 | 元数据 | 物品载荷 | 特点 |
|------|--------|---------|------|
| 直接交易 | `PawnSummary` (文本) | `PawnData` (b64 GZip XML, 10-100KB) | 双人实时协商，各自添加 Pawn + 白银 |
| 公共市场 | `PawnSummary` (文本) | `PawnData` (同上) | 挂牌售卖，买家浏览购买 |
| 出租系统 | `PawnSummary` (文本) | `PawnData` (同上) | 快照模式，出租期间变更不保留 |

**共同特征：** 所有三种模式都有"轻量元数据 + 重量二进制载荷"的结构

### 2.2 如果让 Talent-Trade 用当前架构实现（纯 Command 管线）

**现状路径：**
```
TalentTradeCommandHandler (IClientCommandHandler/IServerDefaultCommandHandler)
  → 22 种协议消息通过 Command MessageType 分发
  → Pawn 数据作为 JSON 字符串嵌入 Command PayloadJson
  → 自建 blob 分片协议（PHXTT|v1|blob|...）处理大消息
  → 通过 HTTP Relay 发送（旁路框架网络层！）
```

**问题：**
1. **无法服务端验证** — `ProcessIncomingItem` 解码后丢弃，Talent-Trade 需要自己解析 Pawn 数据的 `FrameworkItemPayload` 来做 Def 兼容性检查
2. **无法注册 codec** — `IItemCodec` 接口和 `AddItemCodec()` 已存在，但没有管线消费它。Talent-Trade 的 `PawnItemCodec` 注册了也用不上
3. **三层 JSON 膨胀** — Pawn 数据本来可以直走 `PayloadBytes`(protobuf)，现在要经历三次 JSON 序列化
4. **开放封闭违反** — 新增一个"邮件附件"或"蓝图"物品类型，需要自己实现整个 Command handler，而不是实现一个 codec 就完成
5. **HTTP Relay 旁路** — 原模组用独立的 HTTP Relay 服务器，不经过 Phinix 框架网络层。迁移到 Rework 后应该走框架管线

### 2.3 理想路径（Item 管线完整化后）

```
TalentTrade:
  ├─ Command 管线: 协议消息（trade request/accept/offer/lock/execute/cancel, market list/buy/sell, rental list/rent/return 等）
  │   → IClientCommandHandler / IServerDefaultCommandHandler
  │   → 轻量文本负载（UUID、tradeId、价格等元数据）
  │
  └─ Item 管线: Pawn 数据载荷
      → PawnItemCodec : IItemCodec
      → CodecId = "talent-trade.pawn"
      → Encode: Pawn → FrameworkItemPayload { PayloadBytes: protobuf(GZip Pawn XML) }
      → 独立路由: Kind=item, PayloadBytes=<二进制>
      → 服务端三阶段链: Filter → Handler → Observer
```

**收益：**
- Pawn 数据独立路由，不嵌套在 Command JSON 里——去掉一层 JSON 序列化
- 可以用 protobuf 的 `FrameworkItemPacket` 做 wire format，去掉 JSON 外封套的 33% 膨胀
- 服务端可以拦截/验证/存储/转发 Item，Submod 可以注册 `IServerItemObserver` 做日志
- `PawnItemCodec` 通过 `AddItemCodec()` 注册，其他扩展也能发现和使用
- **新增物品类型（蓝图、附件、基因、意识形态...）= 新增一个 IItemCodec 实现，不动管线**

---

## 3. 多角度评估

### 3.1 架构与开放封闭原则

**当前状态：违反开放封闭。** 新增一个携带二进制载荷的功能（如 Talent-Trade 的 Pawn 数据），需要：
- 实现完整的 `IClientCommandHandler` + `IServerDefaultCommandHandler`
- 手动管理 JSON 序列化/反序列化
- 手动管理数据分片（如果数据 > ~1MB）
- 无法复用框架的 codec 机制

**补全后：符合开放封闭。** 新增物品类型 = 实现 `IItemCodec` + 注册 `AddItemCodec()`。管线负责路由、序列化、错误隔离。

### 3.2 软件工程组织

| 维度 | 当前（寄生 Command） | 补全后（独立 Item） |
|------|-------------------|-------------------|
| Submod 接入门槛 | 高（需理解 Command 管线 + JSON 序列化 + 分片） | 低（实现 IItemCodec 两个方法） |
| 代码复用 | Trade 硬编码 codec 列表 | Registry 动态收集 |
| 测试隔离 | Item 逻辑与 Trade 状态机耦合 | Item 管线独立可测 |
| 错误影响面 | Item 解析失败可能影响 Trade 状态机 | Item codec 异常隔离在管线内 |

### 3.3 网络性能

当前三层 JSON 序列化路径（以 100KB Pawn 数据为例）：

```
1. FrameworkVanillaItemData → protobuf bytes: ~100KB (无膨胀)
2. FrameworkItemPayload → JSON: PayloadBytes 字段被 base64 编码 → ~133KB (+33%)
3. FrameworkTradeOfferUpdateRequest → JSON: 包含 Items 列表 → ~135KB
4. FrameworkPacket → JSON: PayloadJson 包含整个 Request → ~140KB (含信封字段)
```

补全后的 Item 直通路径（`FrameworkPacket.PayloadBytes` 直接承载 protobuf）：

```
1. FrameworkVanillaItemData → protobuf bytes: ~100KB (无膨胀)
2. FrameworkItemPacket.PayloadBytes → 二进制直通: ~100KB
3. FrameworkPacket.PayloadBytes → 二进制直通: ~100KB (外层 JSON 的 byte[] → base64 仍存在，但只膨胀一次)
```

**收益：** 消除中间两层 JSON 序列化和一层 base64 编码。实际降幅约 30-40KB/消息。

### 3.4 并发

当前不存在 Item 管线的并发问题——Item 根本没有独立路由。补全后需要：
- `ItemCodecs` 列表的线程安全读取（当前 `IReadOnlyList<IItemCodec>`，已只读，安全）
- 入站 Item 的 `FrameworkPacket` 处理与 Command/Message 同构（已有 try-catch 模式可复用）

---

## 4. 实施建议

> **2026-06-21 更新：P0 已全部实现，P1/P2 待排期。实施细节见 [Item管线补全实施方案.md](Item管线补全实施方案.md)。**

### P0 — 补全 Item 管线 ✅ 已完成

| 步骤 | 内容 | 影响范围 | 状态 |
|------|------|---------|------|
| 1 | Client `packetHandler` 新增 `KindItem` 分支 + `handleItem()` | `PhinixFrameworkClient.cs` | ✅ |
| 2 | Client 新增 `IClientIncomingItemHandler` 接口 | `FrameworkTypes.cs` | ✅ |
| 3 | Server 新增 `IServerInboundItemInterceptor`/`IServerDefaultItemHandler`/`IServerItemObserver` | `FrameworkTypes.cs` | ✅ |
| 4 | `ProcessIncomingItem` 升级为三阶段链 | `ServerPipelineRunner.cs` | ✅ |
| 5 | `IExtensionBuilder` 新增 `AddClientItemHandler()`/`AddServerItemHandler()` 等 | `FrameworkTypes.cs` | ✅ |
| 6 | `PhinixFrameworkClient` 从 Registry 收集 `IItemCodec` 并暴露为 `IItemCodecProvider` | `PhinixFrameworkClient.cs` | ✅ |

**P0 不改变 Trade 的现有 Item 传输方式**——Trade 继续走 Command 嵌套 Item payload。新 Submod 可以选择使用新的独立 Item 路由。

### P1 — 性能优化（待实施）

| 步骤 | 内容 |
|------|------|
| 7 | `FrameworkPacket.PayloadBytes` 直通路径——Item 数据跳过 `PayloadJson`，直接用 protobuf `PayloadBytes` |
| 8 | 添加 `FrameworkSerialization.TrySendItemPacket()` 直接构造 protobuf `FrameworkItemPacket` |

### P2 — Trade 迁移（可选，远期）

| 步骤 | 内容 |
|------|------|
| 9 | Trade 的 Item payload 从 Command 嵌套改为独立 KindItem 路由 |
| 10 | 旧格式兼容（legacy 路径保留一个 MINOR 版本） |

---

## 5. 结论（2026-06-21 更新）

**Item 管线 P0 补全已完成。** 当前状态：

- 接口全量就绪（`IItemCodec`、`IClientIncomingItemHandler`、`IClientOutgoingItemHandler`、`IServerInboundItemInterceptor`、`IServerDefaultItemHandler`、`IServerItemObserver`、`IItemCodecProvider`）
- 服务端三段链就绪（interceptor → default handler → observer + 内置 codec 兜底）
- Client 端双端路由就绪（`KindItem` 独立入站 + `TryHandleOutgoingItem` 出站）
- `IItemCodec` 注册后即被管线消费，Submod 可注册 `IServerItemObserver` 做审计/日志
- Trade 现有 Command 嵌套路径兼容不变（P2 远期迁移）
- P1 PayloadBytes 直通待排期

**相关文档：**
- [Item管线补全实施方案.md](Item管线补全实施方案.md)
- [设计哲学.md](设计哲学.md) — §3.2 三管道, §3.7 管线约束
