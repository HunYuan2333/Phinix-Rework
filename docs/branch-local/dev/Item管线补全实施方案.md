# Item 管线补全 — 实施方案

> 2026-06-21，基于《设计哲学.md》《三条Pipeline职责辨析与Item管线补全分析.md》《框架Protobuf协议设计.md》《Talent-Trade迁移框架需求分析.md》与代码库实际审计编写。
>
> **2026-06-21 更新：本文档中描述的 P0 全部阶段均已实现。** 接口定义、服务端三段链、客户端双端路由、codec 消费、Trade 兼容路径均已在代码库中落地。
> **P1/P2 待排期。** P1：PayloadBytes 直通路径（性能优化）。P2：Trade Item payload 从 Command 嵌套迁至 KindItem（可选，远期的）。
>
> **本文档是方案文档，不是已实施记录。** 所有"现状"行号均为代码库实测（截至 P0 实现前），"目标"为待实施设计。

---

## 0. TL;DR

Item 管线目前是**接口已就绪、链路未接通**的半成品：

- `IItemCodec` / `AddItemCodec()` / `DiscoveredPhinixExtensions.ItemCodecs` / `FrameworkFlow.Item` / `FrameworkItemPacket` proto 骨架 **均已就绪**
- 客户端 `packetHandler` **无 `KindItem` 分支**（`PhinixFrameworkClient.cs:434-470`）——Item 永远不独立到达客户端
- 服务端 `packetHandler` **有 `KindItem` 分支**（`PhinixFrameworkServer.cs:159`）但 `ProcessIncomingItem` 仅 **decode + round-trip 验证 + 丢弃结果**（`ServerPipelineRunner.cs:241-297`）——**无 interceptor / handler / observer 三段链**
- 客户端 `discoveredExtensions.ItemCodecs` 已收集但**未被任何管线消费**；`TradeClientItemPipeline` 构造时未传 `extensionCodecs`（`BuiltInTradeClientExtension.cs:40`）
- 实际 Item 传输路径：**寄生在 Command 管线内**，三层 JSON 序列化 + 一次 base64 膨胀

**补全方案核心：** 与 Message / Command 管线**同构**地补齐 Item 的三阶段链与两端路由，**不破坏** Trade 当前嵌套路径（P2 才迁移），新增接口而非修改既有接口（增量更新原则，设计哲学 §6）。

---

## 1. 背景与目标

### 1.1 触发因素

两位 Submod 作者的迁移需求（Talent-Trade 为代表）暴露 Item 管线半成品阻塞：

- Pawn 数据（10-100KB GZip XML）需要独立路由，不应嵌套在 Command JSON 内经历三层序列化
- Submod 需要注册 `PawnItemCodec : IItemCodec` 后让管线**真正消费**它
- 服务端需要 `IServerItemObserver` 做审计/验证/转发，不能解码后直接丢弃
- 新增"邮件附件 / 蓝图 / 基因 / 意识形态"等物品类型，应等于新增一个 codec，而不是重写整套 Command handler

### 1.2 目标（按优先级）

| 优先级 | 目标 | 衡量标准 |
|--------|------|----------|
| **P0** | Item 管线两端可独立路由，服务端有三阶段链，Submod 可注册 codec 被管线消费 | Submod 注册 `IItemCodec` + `IServerItemObserver` 后，独立发送 `KindItem` 包能被服务端拦截/处理/观察；客户端能收到独立 `KindItem` 包并分发到 `IClientIncomingItemHandler` |
| **P0** | Trade 现有 Command 嵌套 Item 路径完全不受影响 | Trade 协议消息（offer.update.request 等）行为字节级一致 |
| **P1** | `PayloadBytes` 直通路径，消除中间两层 JSON 序列化与一层 base64 膨胀 | 100KB Pawn 数据 wire 体积从 ~140KB 降至 ~100KB |
| **P2** | Trade 的 Item payload 从 Command 嵌套迁至独立 KindItem 路由（可选） | 旧格式兼容保留一个 MINOR 版本 |

### 1.3 非目标

- **不重写 `IItemCodec` 接口签名** —— 已稳定，且 `DefaultLegacyTradeItemCodec` 已实现
- **不重构 Trade 状态机** —— Trade 在 P0/P1 阶段维持现状
- **不引入新序列化框架** —— 仍用 DataContractJsonSerializer + protobuf-net
- **不做热重载** —— 扩展热重载推迟到 2.0（《扩展开发体验增强-设计方案.md》§1.2）

---

## 2. 设计哲学合规要点（必须遵守）

本方案所有设计决策均以《设计哲学.md》为基准。下列条款在实施过程中**不得违反**：

| 哲学条目 | 对本方案的约束 |
|----------|---------------|
| **§1.1 插件平权** | Item 三段链接口（`IServerItemInterceptor` / `IServerDefaultItemHandler` / `IServerItemObserver`）与 Message/Command 同构，无"官方超级公民"。`DefaultLegacyTradeItemCodec` 不得在管线中获得优先特权——它只是 registry 中的一个 codec |
| **§1.2 host 不依赖插件** | 新增的 Item handler 接口必须定义在 `Common/Utils/Framework/FrameworkTypes.cs` 或 `ClientExtensionAbstractions`，**不得**在 host 工程引用 `TradeExtension` 或任何插件 Contracts |
| **§1.3 host 只做通用服务** | host 仅提供路由、三段链执行器、错误隔离；**Item 业务语义（什么算 Pawn、什么算蓝图）由插件 codec 决定** |
| **§2.1 松耦合** | Submod 通过 `IItemCodec` + Item handler 接口接入，不直接引用 host 内部类型 |
| **§2.2 层次化** | 新增接口放在共享契约层（`FrameworkTypes.cs`），实现放在插件层或 host 层，禁止反向依赖 |
| **§2.3 减少硬编码** | host 不得硬编码"vanilla item / pawn item"等具体 codec id；codec 路由通过 `IItemCodec.CodecId` 动态匹配 |
| **§3.2 通信管道三分类** | Item 是独立管道，**不应**让 Submod 继续寄生在 Command 内（P2 阶段消除）；P0/P1 期间允许 Trade 保留寄生路径作为兼容 |
| **§3.5 错误隔离与重试** | 单个 codec `Decode` 抛异常 → 管线 try-catch 隔离，记录日志，**继续尝试下一个 codec** 或返回失败；单个 interceptor/handler/observer 异常不得中断链 |
| **§3.6 反压与资源边界** | Item 包通常较大（10-100KB+），`ProcessIncomingItem` 路径上不得有无界累积；若引入入站缓冲队列，必须有容量上限（参考 `displayMessages` 1000 / `pendingActions` 500 模式） |
| **§3.7 插件不得绕过通信管线直连底层传输** | Submod 发送 Item 必须**通过 `TryHandleOutgoingItem` 管线**，不得直接调 `IFrameworkClientTransport.SendFrameworkPacket()`。这是 P0 必须实现 `TryHandleOutgoingItem` 的哲学依据 |
| **§3.8 日志与可观测性** | 所有 Item 管线日志通过 `hostContext.Log` / `ILoggable` 上报；异常日志必须附带 `Exception` 对象；不得 `Console.WriteLine` |
| **§5.3 版本化与 API 兼容** | 新增接口 = `MINOR` 版本升级；`IItemCodec` / `AddItemCodec` 不改签名；`FrameworkItemPacket` proto 字段编号不得重用 |
| **§6 渐进式迁移** | P0 / P1 / P2 各阶段**必须独立可编译、可运行、可验证**；不一次性大迁徙；`Host/Core 仅做增量更新` —— 新增方法而非修改既有方法 |

---

## 3. 现状精确定位（代码库实测）

### 3.1 已就绪（无需改动）

| 项 | 位置 |
|----|------|
| `FrameworkProtocol.KindItem = "item"` | `Common/Utils/Framework/FrameworkTypes.cs:16` |
| `IItemCodec` 接口（5 成员） | `Common/Utils/Framework/FrameworkTypes.cs:530-541` |
| `ItemCodecContext` | `Common/Utils/Framework/FrameworkTypes.cs:663-668` |
| `IExtensionBuilder.AddItemCodec(IItemCodec)` | `Common/Utils/Framework/FrameworkTypes.cs:129` |
| `AddItemCodec` 实现 | `Common/Utils/Framework/PhinixExtensionRegistry.cs:415` |
| Legacy 自动发现路径（`instance is IItemCodec`） | `Common/Utils/Framework/PhinixExtensionRegistry.cs:192-196` |
| `DiscoveredPhinixExtensions.ItemCodecs` | `Common/Utils/Framework/FrameworkTypes.cs:790` |
| `FrameworkFlow.Item = 3` | `Common/Utils/Framework/Proto/Shared/FrameworkShared.proto:16` |
| `FrameworkItemPacket` proto | `Common/Utils/Framework/Proto/Item/FrameworkItemPacket.proto:7-11` |
| `FrameworkVanillaItemData` proto | `Common/Utils/Framework/Proto/Item/FrameworkItemPacket.proto:25-32` |
| `FrameworkItemCollection` proto | `Common/Utils/Framework/Proto/Item/FrameworkItemPacket.proto:34-36` |
| `FrameworkItemQuality` enum proto | `Common/Utils/Framework/Proto/Item/FrameworkItemPacket.proto:13-23` |
| `DefaultLegacyTradeItemCodec` (`core.item.vanilla`) | `Extensions/Trade/Client/DefaultLegacyTradeItemCodec.cs:9` |
| `FrameworkItemPayload`（`CodecId`/`PayloadJson`/`Metadata`/`PayloadBytes`） | `Common/Utils/Framework/FrameworkPacket.cs:93-107` |
| `FrameworkPacket.PayloadBytes` 字段 | `Common/Utils/Framework/FrameworkPacket.cs:54` |
| 客户端 `sendPacket` 已能归一化 `Flow=Item` | `Client/Source/Framework/PhinixFrameworkClient.cs:675-679` |
| 服务端 `packetHandler` `KindItem` 分支 | `Server/Framework/PhinixFrameworkServer.cs:159-164` |
| 服务端 `handleItem` 鉴权+capability 检查 | `Server/Framework/PhinixFrameworkServer.cs:249-275` |
| 服务端 `sendPacketDirect` 已能归一化 `KindItem` | `Server/Framework/PhinixFrameworkServer.cs:442-446` |
| 服务端 `discoveredExtensions.ItemCodecs` → `ServerPipelineRunner` 构造 | `Server/Framework/PhinixFrameworkServer.cs:345,355` |

### 3.2 缺失（本方案待补齐）

| 项 | 当前状态 | 影响 |
|----|----------|------|
| `IServerItemInterceptor` 接口 | 未定义 | 服务端无入站拦截段 |
| `IServerDefaultItemHandler` 接口 | 未定义 | 服务端无默认处理段 |
| `IServerItemObserver` 接口 | 未定义 | 服务端无观察段（无法审计） |
| `IClientIncomingItemHandler` 接口 | 未定义 | 客户端无入站处理 |
| `IClientOutgoingItemHandler` 接口 | 未定义 | 客户端无出站拦截（违反 §3.7） |
| `ServerIncomingItemResult` 类型 | 未定义 | 三段链无法回传结果 |
| `ItemHandlingResultAction` 枚举 | 未定义（当前 Item 复用 `MessageHandlingResultAction`） | 需 Item 专用语义 |
| `IExtensionBuilder.AddClientItemHandler()` | 未定义 | 无法注册客户端入站 handler |
| `IExtensionBuilder.AddClientOutgoingItemHandler()` | 未定义 | 无法注册客户端出站 handler |
| `IExtensionBuilder.AddServerItemInterceptor()` | 未定义 | 无法注册服务端拦截器 |
| `IExtensionBuilder.AddServerDefaultItemHandler()` | 未定义 | 无法注册服务端默认处理 |
| `IExtensionBuilder.AddServerItemObserver()` | 未定义 | 无法注册服务端观察器 |
| `DiscoveredPhinixExtensions.ClientIncomingItemHandlers` 等集合 | 未定义 | registry 无法收集 |
| 客户端 `packetHandler` `KindItem` case | `PhinixFrameworkClient.cs:434-470` 无此分支 | Item 包落入 `default` 仅 DEBUG 日志后丢弃 |
| 客户端 `handleItem()` 方法 | 不存在 | — |
| 客户端 `TryHandleOutgoingItem()` 方法 | 不存在 | Submod 被迫绕过管线（违反 §3.7） |
| `ServerPipelineRunner.ProcessIncomingItem` 三段链 | `ServerPipelineRunner.cs:241-297` 仅 decode+round-trip+丢弃 | 无 interceptor/handler/observer |
| `ServerPipelineRunner` 构造函数缺少 Item 三段列表 | `ServerPipelineRunner.cs:27-45` 仅 `itemCodecs` 一个 Item 参数 | — |
| `PhinixFrameworkServer.buildPipelineRunner` 未传入 Item 三段列表 | `PhinixFrameworkServer.cs:309-356` 仅传 `itemCodecs` | — |
| 客户端 `discoveredExtensions.ItemCodecs` 未被消费 | `PhinixFrameworkClient.cs:87` 仅日志计数 | Submod 注册 codec 无效 |
| `TradeClientItemPipeline` 未从 registry 收集 codec | `BuiltInTradeClientExtension.cs:40` 构造时不传 `extensionCodecs` | 即使内部支持注入，外部未传 |
| `ProcessIncomingItem` 仍读 `PayloadJson` | `ServerPipelineRunner.cs:248-257` | P1 直通路径未实现 |
| `FrameworkSerialization.TrySendItemPacket()` | 不存在（仅有 `ToItemPacket`/`FromItemPacket` 辅助） | P1 直通发送未实现 |

---

## 4. 目标架构

### 4.1 三段链同构对照

Item 管线补全后，三条管道在服务端的结构完全同构：

```
                     Message 管线          Command 管线         Item 管线（目标）
                     ─────────────         ──────────────       ───────────────
入站路由              packetHandler         packetHandler        packetHandler
                      KindMessage           KindCommand          KindItem
                          ↓                     ↓                   ↓
鉴权/capability       handleMessage         handleCommand        handleItem（已存在）
                          ↓                     ↓                   ↓
InboundInterception   IServerInbound        IServerInbound       IServerItem
                      MessageInterceptor    CommandInterceptor   Interceptor
                          ↓                     ↓                   ↓
DefaultProcess        IServerDefault        IServerDefault       IServerDefault
                      MessageHandler        CommandHandler       ItemHandler
                          ↓                     ↓                   ↓
Observation           IServerMessage        IServerCommand       IServerItem
                      Observer              Observer             Observer
                          ↓                     ↓                   ↓
（出站统一）           DispatchOutbound → OutboundPacketInterceptor → SendPacket
```

### 4.2 客户端路由对照

```
                     Message 管线          Command 管线         Item 管线（目标）
                     ─────────────         ──────────────       ───────────────
入站路由              packetHandler         packetHandler        packetHandler
                      KindMessage           KindCommand          KindItem（待新增）
                          ↓                     ↓                   ↓
分发                  handleMessage         handleCommand        handleItem（待新增）
                          ↓                     ↓                   ↓
Handler 链            IClientMessage        IClientCommand       IClientIncoming
                      Handler               Handler              ItemHandler（待新增）

出站                  TryHandleOutgoing     TryHandleOutgoing    TryHandleOutgoing
                      Message               Command              Item（待新增）
                          ↓                     ↓                   ↓
出站拦截              IClientMessage        IClientOutgoing      IClientOutgoing
                      Handler.Handle        CommandHandler       ItemHandler
                      OutgoingText()        （已存在）            （待新增）
                          ↓                     ↓                   ↓
                     sendPacket（Flow/Kind 归一化，已存在）
```

### 4.3 数据流（P0 目标）

**入站（Server）：**
```
NetServer → packetHandler(KindItem) → handleItem(鉴权/capability) 
  → ProcessIncomingItem:
      1. InboundInterception: IServerItemInterceptor.CanIntercept → InterceptItem()
      2. DefaultProcess:      IServerDefaultItemHandler.CanHandle → HandleItem()
         └─ 内部：遍历 ItemCodecs，按 CodecId 匹配，CanDecode → Decode → 产出 decoded object
      3. Observation:         IServerItemObserver.ObserveItem()
```

**入站（Client）：**
```
NetClient → packetHandler(KindItem) → handleItem(packet)
  → 遍历 IClientIncomingItemHandler 链:
      CanHandle(packet) → HandleItem(packet, context)
```

**出站（Client，Submod 视角）：**
```
Submod → TryHandleOutgoingItem(itemPayload, context)
  → 遍历 IClientOutgoingItemHandler 链:
      HandleOutgoingItem() → 返回 FrameworkPacket
  → sendPacket(Flow=Item, Kind=item, PayloadBytes=protobuf)
```

### 4.4 数据流（P1 直通路径）

P0 仍走 `PayloadJson` 嵌套 `FrameworkItemPayload` 的 JSON 路径，**与 Trade 兼容**。
P1 引入 `PayloadBytes` 直通：

```
Submod → TryHandleOutgoingItem → 直接构造 FrameworkPacket {
    Kind = "item",
    Flow = FrameworkFlow.Item,
    PayloadBytes = protobuf(FrameworkItemPacket {
        header, codec_id, payload_bytes = protobuf(codec-specific data)
    })
}
```

服务端 `ProcessIncomingItem` 检测 `PayloadBytes != empty`：
- 有 → 从 `PayloadBytes` 反序列化 `FrameworkItemPacket`，取 `codec_id` + `payload_bytes`
- 无 → 退回 `PayloadJson` 路径（兼容 Trade 现有嵌套格式）

---

## 5. 接口设计（完整签名）

所有新增接口定义在 `Common/Utils/Framework/FrameworkTypes.cs`，与既有 Message/Command 接口同层。

### 5.1 枚举与结果类型

```csharp
/// <summary>
/// Item 管线 handler/interceptor 的处理动作语义。
/// 与 MessageHandlingResultAction 同构但语义独立，避免 Item 复用 Message 枚举导致语义混淆。
/// </summary>
public enum ItemHandlingResultAction
{
    /// <summary>继续传递给链中下一个候选者（默认）。</summary>
    Continue = 0,
    /// <summary>本 handler 已处理，跳过后续 default handler 但仍通知 observer。</summary>
    Handled = 1,
    /// <summary>替换 Item payload，链继续。</summary>
    ReplacePayload = 2,
    /// <summary>抑制默认 handler，直接跳到 observer。</summary>
    SuppressDefault = 3,
    /// <summary>停止整条链（含 observer）。</summary>
    StopPropagation = 4,
    /// <summary>回退到 legacy 路径（用于适配器场景）。</summary>
    LegacyFallback = 5
}

/// <summary>
/// 服务端入站 Item 三段链的最终结果。
/// 与 ServerIncomingMessageResult / ServerIncomingCommandResult 同构。
/// </summary>
public sealed class ServerIncomingItemResult
{
    public ItemHandlingResultAction Action { get; set; } = ItemHandlingResultAction.Continue;
    public FrameworkItemPayload ReplacedPayload { get; set; }  // 仅 Action=ReplacePayload 时有意义
    public object DecodedItem { get; set; }                    // 解码后的业务对象（如 Thing、PawnSnapshot）
    public string HandledByHandlerId { get; set; }             // 哪个 default handler 处理了
    public string FailureReason { get; set; }                  // 失败原因（不抛异常，记录在此）
    public Exception FailureException { get; set; }            // 失败异常（用于带堆栈的日志）
}
```

### 5.2 服务端三段链接口

```csharp
/// <summary>
/// 服务端入站 Item 拦截器。在 default handler 之前执行，可修改/拒绝 Item。
/// 与 IServerInboundMessageInterceptor / IServerInboundCommandInterceptor 同构。
/// </summary>
public interface IServerItemInterceptor
{
    string InterceptorId { get; }
    int Priority { get; }  // 升序排序，与 Message/Command interceptor 一致
    bool CanIntercept(FrameworkPacket itemPacket, ServerFrameworkContext context);
    ServerIncomingItemResult InterceptItem(FrameworkPacket itemPacket, ServerFrameworkContext context);
}

/// <summary>
/// 服务端默认 Item 处理器。在 interceptor 之后执行，负责调用 codec 解码并落地业务。
/// 与 IServerDefaultMessageHandler / IServerDefaultCommandHandler 同构。
/// </summary>
public interface IServerDefaultItemHandler
{
    string HandlerId { get; }
    int Priority { get; }
    bool CanHandle(FrameworkPacket itemPacket, ServerFrameworkContext context);
    ServerIncomingItemResult HandleItem(FrameworkPacket itemPacket, ServerFrameworkContext context,
                                        IReadOnlyList<IItemCodec> codecs);
}

/// <summary>
/// 服务端 Item 观察器。在 default handler 之后执行，只读，不可修改。
/// 用于审计、日志、统计、转发等。
/// 与 IServerMessageObserver / IServerCommandObserver 同构。
/// </summary>
public interface IServerItemObserver
{
    string ObserverId { get; }
    int Priority { get; }
    void ObserveItem(FrameworkPacket itemPacket, ServerFrameworkContext context, ServerIncomingItemResult result);
}
```

### 5.3 客户端接口

```csharp
/// <summary>
/// 客户端入站 Item 处理器。packetHandler 收到 KindItem 包后遍历此链。
/// 与 IClientMessageHandler / IClientCommandHandler 同构。
/// </summary>
public interface IClientIncomingItemHandler
{
    string HandlerId { get; }
    int Priority { get; }
    bool CanHandle(FrameworkPacket itemPacket, ClientFrameworkContext context);
    void HandleItem(FrameworkPacket itemPacket, ClientFrameworkContext context);
}

/// <summary>
/// 客户端出站 Item 处理器。TryHandleOutgoingItem 遍历此链。
/// 与 IClientMessageHandler.HandleOutgoingText / IClientOutgoingCommandHandler 同构。
/// 设计哲学 §3.7 要求：Submod 不得绕过此管线直连底层传输。
/// </summary>
public interface IClientOutgoingItemHandler
{
    string HandlerId { get; }
    int Priority { get; }
    /// <summary>
    /// 处理出站 Item。返回构造好的 FrameworkPacket（Flow=Item, Kind=item）由框架统一发送，
    /// 返回 null 表示本 handler 不处理，框架继续询问下一个 handler。
    /// </summary>
    FrameworkPacket HandleOutgoingItem(FrameworkItemPayload itemPayload, ClientFrameworkContext context);
}
```

### 5.4 `IExtensionBuilder` 新增方法

**严格增量**：只在 `IExtensionBuilder` 接口追加方法，不修改既有方法签名（设计哲学 §6）。

```csharp
// Common/Utils/Framework/FrameworkTypes.cs : IExtensionBuilder 内
void AddClientItemHandler(IClientIncomingItemHandler handler);
void AddClientOutgoingItemHandler(IClientOutgoingItemHandler handler);
void AddServerItemInterceptor(IServerItemInterceptor interceptor);
void AddServerDefaultItemHandler(IServerDefaultItemHandler handler);
void AddServerItemObserver(IServerItemObserver observer);
```

> **兼容性说明：** 新增接口方法会让所有 `IExtensionBuilder` 实现者必须实现这些方法。当前唯一实现是 `PhinixExtensionRegistry.ExtensionBuilder`（私有嵌套类），host 同步更新即可。**外部 Submod 不实现 `IExtensionBuilder`**（它们消费 builder，不实现 builder），因此无下游破坏。如未来出现外部 `IExtensionBuilder` 实现，需提供默认实现的抽象类作为兼容垫片——但当前不存在此情况。

### 5.5 `DiscoveredPhinixExtensions` 新增集合

```csharp
// Common/Utils/Framework/FrameworkTypes.cs : DiscoveredPhinixExtensions 内
public List<IClientIncomingItemHandler> ClientIncomingItemHandlers { get; } = new();
public List<IClientOutgoingItemHandler> ClientOutgoingItemHandlers { get; } = new();
public List<IServerItemInterceptor> ServerItemInterceptors { get; } = new();
public List<IServerDefaultItemHandler> ServerDefaultItemHandlers { get; } = new();
public List<IServerItemObserver> ServerItemObservers { get; } = new();
```

---

## 6. 实施路线（P0 / P1 / P2）

### 6.1 P0 — 补全 Item 管线（解除 Submod 阻塞）

**目标：** Item 能独立路由，服务端有三段链，Submod 注册的 codec 与 handler 能被管线消费。Trade 当前路径不变。

| 步骤 | 文件 | 改动 | 依赖 |
|------|------|------|------|
| P0-1 | `Common/Utils/Framework/FrameworkTypes.cs` | 新增 `ItemHandlingResultAction`、`ServerIncomingItemResult`、`IServerItemInterceptor`、`IServerDefaultItemHandler`、`IServerItemObserver`、`IClientIncomingItemHandler`、`IClientOutgoingItemHandler` 接口 | — |
| P0-2 | `Common/Utils/Framework/FrameworkTypes.cs` | `IExtensionBuilder` 追加 5 个 `Add*Item*` 方法 | P0-1 |
| P0-3 | `Common/Utils/Framework/FrameworkTypes.cs` | `DiscoveredPhinixExtensions` 追加 5 个 `List<I*Item*>` 集合 | P0-1 |
| P0-4 | `Common/Utils/Framework/PhinixExtensionRegistry.cs` | `ExtensionBuilder` 实现 5 个新 `Add*Item*` 方法（模式同既有 `addIfMissing`） | P0-2, P0-3 |
| P0-5 | `Server/ServerRuntime/ServerPipelineRunner.cs` | 构造函数追加 3 个参数：`IReadOnlyList<IServerItemInterceptor>`、`IReadOnlyList<IServerDefaultItemHandler>`、`IReadOnlyList<IServerItemObserver>`；存为字段 | P0-1 |
| P0-6 | `Server/ServerRuntime/ServerPipelineRunner.cs` | 重写 `ProcessIncomingItem`：三段链 `RunItemInterceptors → RunDefaultItemHandlers → RunItemObservers`，每段 try-catch 隔离（参照 `ProcessIncomingMessage:47-142` 模式） | P0-5 |
| P0-7 | `Server/Framework/PhinixFrameworkServer.cs` | `buildPipelineRunner` 从 `discoveredExtensions` 收集 3 个新列表并传入 `ServerPipelineRunner` 构造函数（参照 `:345` 既有 `itemCodecs` 收集模式） | P0-5 |
| P0-8 | `Client/Source/Framework/PhinixFrameworkClient.cs` | `packetHandler` switch 新增 `case FrameworkProtocol.KindItem:` → `handleItem(packet)`（参照 `:453` `KindMessage` 模式） | — |
| P0-9 | `Client/Source/Framework/PhinixFrameworkClient.cs` | 新增 `handleItem(FrameworkPacket packet)` 方法：遍历 `clientIncomingItemHandlers`，按 Priority 排序，`CanHandle` 过滤，try-catch 隔离调用 `HandleItem`（参照 `handleMessage:473-551` 模式） | P0-8 |
| P0-10 | `Client/Source/Framework/PhinixFrameworkClient.cs` | 新增 `TryHandleOutgoingItem(FrameworkItemPayload payload, ClientFrameworkContext context)` 方法：遍历 `clientOutgoingItemHandlers`，第一个返回非 null `FrameworkPacket` 者胜出，调 `sendPacket`；无 handler 处理时返回 false 并记 Error 日志（设计哲学 §3.5"离线发送触发可观测告警"） | P0-1 |
| P0-11 | `Client/Source/Framework/PhinixFrameworkClient.cs` | 构造函数从 `discoveredExtensions` 收集 `ClientIncomingItemHandlers` / `ClientOutgoingItemHandlers` / `ItemCodecs` 存为字段（参照 `discoveredExtensions.ItemCodecs` 已有的收集模式） | P0-3 |
| P0-12 | `Extensions/Trade/Client/BuiltInTradeClientExtension.cs` | `:40` 构造 `TradeClientItemPipeline` 时传入 `builder.HostContext.GetRequiredService<...>().ItemCodecs`（或等价方式从 registry 取已收集的 codec 列表）作为 `extensionCodecs` 参数 | P0-11 |
| P0-13 | `Common/Utils/Framework/PhinixExtensionRegistry.cs` | 验证 legacy 自动发现路径 `:192-196`（`instance is IItemCodec`）与新 `AddClientItemHandler` 等不冲突；如需为新接口添加类似的 legacy 自动发现，添加并产出迁移 Warning | P0-4 |
| P0-14 | 单元测试 | 新增 `ServerPipelineRunnerTests` 用例：mock 三段链各节点，验证执行顺序、异常隔离、Priority 排序、`StopPropagation` 语义 | P0-6 |
| P0-15 | 单元测试 | 新增 `PhinixFrameworkClientItemRoutingTests`：mock `IClientIncomingItemHandler`，发送 `KindItem` 包，验证 handler 被调用且异常被隔离 | P0-9 |

**P0 完成标准：**
1. Submod 调 `builder.AddItemCodec(new PawnItemCodec())` + `builder.AddServerItemObserver(new PawnAuditObserver())` 后，独立发送 `KindItem` 包能被服务端解码并通知 observer
2. Submod 调 `builder.AddClientItemHandler(...)` 后，客户端能收到独立 `KindItem` 包并触发 handler
3. Trade 现有 `OfferUpdateRequest` / `OfferUpdateResponse` 等行为**字节级一致**（P0 不改 Trade 协议路径）
4. 所有三段链节点异常被 try-catch 隔离，单节点失败不中断链
5. 编译通过、既有测试全绿

### 6.2 P1 — PayloadBytes 直通（性能优化）

**目标：** 消除中间两层 JSON 序列化与一层 base64 膨胀。

| 步骤 | 文件 | 改动 |
|------|------|------|
| P1-1 | `Common/Utils/Framework/FrameworkSerialization.cs` | 新增 `TrySendItemPacket(FrameworkPacket packet)` 辅助方法：若 `packet.PayloadBytes` 非空且 `packet.Kind == KindItem`，直接以 `PayloadBytes` 作为 wire 主体（不再包一层 `PayloadJson`） |
| P1-2 | `Server/ServerRuntime/ServerPipelineRunner.cs` | `ProcessIncomingItem` 入口增加分支：若 `item.PayloadBytes != empty` 且 `item.PayloadJson == null`，从 `PayloadBytes` 反序列化 `FrameworkItemPacket`（protobuf），取 `codec_id` + `payload_bytes` 构造 `FrameworkItemPayload`；否则走原 `PayloadJson` 路径 |
| P1-3 | `Client/Source/Framework/PhinixFrameworkClient.cs` | `handleItem` 同步支持 `PayloadBytes` 直通分支 |
| P1-4 | `Common/Utils/Framework/FrameworkPacket.cs` | `FrameworkPacket` 文档注释明确：`PayloadBytes` 字段对 `Kind=item` 时承载 `FrameworkItemPacket` protobuf 序列化结果 |
| P1-5 | 集成测试 | 双端对发 100KB Pawn 数据，验证 wire 体积降幅 ~30%，round-trip 正确 |

**P1 完成标准：**
1. Submod 用 `TrySendItemPacket` 直通路径发送 100KB Pawn 数据，wire 体积 ≤ 105KB
2. 旧 `PayloadJson` 嵌套路径仍可用（Trade 不强制迁移）
3. 服务端 `ProcessIncomingItem` 自动识别两种格式

### 6.3 P2 — Trade 迁移（可选，远期）

**目标：** Trade 的 Item payload 从 Command 嵌套迁至独立 KindItem 路由。

**前提：** Talent-Trade 等关键 Submod 已在 P0/P1 基础上完成迁移并稳定运行一个 MINOR 版本。

| 步骤 | 文件 | 改动 |
|------|------|------|
| P2-1 | `Extensions/Trade/Contracts/TradeContracts.cs` | `FrameworkTradeOfferUpdateRequest.Items` 标记 `[Obsolete]`，新增 `ItemPacketRefs : List<string>`（引用独立 Item 包的 packet_id） |
| P2-2 | `Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs` | `CreateOfferUpdateRequest:177-195` 改为：先发独立 `KindItem` 包，收集 packet_id，再发 Command 引用这些 id |
| P2-3 | `Extensions/Trade/Server/PhinixFrameworkTradeServerService.cs` | 入站解析改为：收到 Command 后按 `ItemPacketRefs` 向 `IServerItemObserver` 查询已缓存的 Item 包 |
| P2-4 | 兼容路径 | 旧客户端仍发 `Items` 嵌套格式，服务端识别并兼容；标记 `[Obsolete]`，计划在下一个 MAJOR 移除 |

**P2 不在当前方案必做范围。** 仅在 Trade 团队主动要求或 Talent-Trade 迁移完成后再启动。

---

## 7. 详细改动清单（按文件）

### 7.1 `Common/Utils/Framework/FrameworkTypes.cs`

**新增接口（约 80 行）：**
- `ItemHandlingResultAction` enum
- `ServerIncomingItemResult` class
- `IServerItemInterceptor` interface
- `IServerDefaultItemHandler` interface
- `IServerItemObserver` interface
- `IClientIncomingItemHandler` interface
- `IClientOutgoingItemHandler` interface

**`IExtensionBuilder` 追加方法（约 5 行）：**
- `AddClientItemHandler` / `AddClientOutgoingItemHandler` / `AddServerItemInterceptor` / `AddServerDefaultItemHandler` / `AddServerItemObserver`

**`DiscoveredPhinixExtensions` 追加集合（约 5 行）：**
- 5 个 `List<I*Item*>` 集合

### 7.2 `Common/Utils/Framework/PhinixExtensionRegistry.cs`

- `ExtensionBuilder` 实现 5 个新 `Add*Item*` 方法（每个一行 `addIfMissing` 调用）
- `:192-196` 既有 `instance is IItemCodec` legacy 自动发现路径**保持不变**；如需为 Item handler 接口添加类似 legacy 发现，单独添加并产 Warning

### 7.3 `Server/ServerRuntime/ServerPipelineRunner.cs`

- 构造函数追加 3 个参数（`IServerItemInterceptor` / `IServerDefaultItemHandler` / `IServerItemObserver` 列表）
- 字段追加 3 个 `IReadOnlyList<>`
- 重写 `ProcessIncomingItem:241-297`：三段链，参照 `ProcessIncomingMessage:47-142` 模式
- 新增私有 `runItemInterceptors` / `runDefaultItemHandlers` / `runItemObservers` 方法
- 每个 interceptor/handler/observer 调用包独立 try-catch，异常记入 `ServerIncomingItemResult.FailureException` 并 Log Error，**链继续**

### 7.4 `Server/Framework/PhinixFrameworkServer.cs`

- `buildPipelineRunner:309-356`：从 `discoveredExtensions` 收集 3 个新列表（参照 `:345` 既有 `itemCodecs` 模式），传入 `ServerPipelineRunner` 构造函数

### 7.5 `Client/Source/Framework/PhinixFrameworkClient.cs`

- `packetHandler:434-470` switch 新增 `case FrameworkProtocol.KindItem: handleItem(packet); break;`
- 新增 `handleItem(FrameworkPacket packet)` 方法（参照 `handleMessage:473-551`）
- 新增 `TryHandleOutgoingItem(FrameworkItemPayload payload, ClientFrameworkContext context)` 方法（参照 `TryHandleOutgoingMessage:150` / `TryHandleOutgoingCommand:220`）
- 构造函数 `:48-116`：从 `discoveredExtensions` 收集 `ClientIncomingItemHandlers` / `ClientOutgoingItemHandlers` 存为字段
- `sendPacket:656-682` 已支持 `Flow=Item` 归一化，无需改动

### 7.6 `Extensions/Trade/Client/BuiltInTradeClientExtension.cs`

- `:40` 构造 `TradeClientItemPipeline` 时传入 registry 收集的 codec 列表作为 `extensionCodecs` 参数

### 7.7 文档同步

- `docs/设计哲学.md` §3.2 表格：Item 状态从 "⚠️ 半成品" 改为 "✅ P0 完整可用"
- `docs/branch-local/dev/框架Protobuf协议设计.md` §3 当前实现状态表：Item 相关行更新
- `docs/branch-local/dev/Talent-Trade迁移框架需求分析.md` §4 接口清单：从 ❌ 改为 ✅
- `docs/branch-local/dev/三条Pipeline职责辨析与Item管线补全分析.md` §4 P0/P1/P2 步骤表：标记完成状态
- `docs/Phinix附属Mod开发者指南.md` §6.3：更新 Item 管线使用示例
- `docs/Phinix-Submod-Developer-Guide.md` §6.3：同上英文版

---

## 8. 兼容性与风险

### 8.1 兼容性矩阵

| 变更 | 对既有 Submod 影响 | 对 Trade 影响 | 对 Legacy 适配器影响 |
|------|-------------------|---------------|---------------------|
| 新增 7 个接口 | 无（不实现则不参与） | 无 | 无 |
| `IExtensionBuilder` 追加 5 方法 | 无（Submod 消费 builder，不实现 builder） | 无 | 无 |
| `DiscoveredPhinixExtensions` 追加集合 | 无（只读消费） | 无 | 无 |
| `ServerPipelineRunner` 构造函数签名变更 | 无（host 内部类） | 无 | 无 |
| `ProcessIncomingItem` 重写 | 无（外部不可见） | 无（Trade 不调此方法） | 无 |
| 客户端 `packetHandler` 新增 `KindItem` case | 无 | 无 | 无 |
| `TradeClientItemPipeline` 传入 registry codec | 无 | **正向**：Trade 现在能消费 Submod 注册的 codec | 无 |

**结论：** P0 全部为增量变更，不破坏任何既有行为。唯一运行时行为变化是 `TradeClientItemPipeline` 现在能消费 Submod codec——这是修复缺陷，不是破坏兼容。

### 8.2 风险清单

| 风险 | 严重度 | 缓解措施 |
|------|--------|----------|
| `IExtensionBuilder` 接口追加方法导致外部实现者编译失败 | LOW | 当前唯一实现是 host 私有嵌套类；外部 Submod 不实现此接口。若未来出现外部实现，提供 `ExtensionBuilderBase` 抽象类作为默认实现垫片 |
| 三段链 Priority 排序与 Message/Command 不一致导致语义混淆 | MEDIUM | 严格参照 `ProcessIncomingMessage:47-142` 排序模式，单元测试覆盖排序行为 |
| `ProcessIncomingItem` 重写后 Trade 嵌套路径被误处理 | LOW | Trade 走 `handleCommand` 路径，不进 `handleItem`；`ProcessIncomingItem` 只在 `Kind=item` 时被调用。P0 不改 Trade 协议路径 |
| 大 Item 包（>1MB）阻塞 poll 线程 | MEDIUM | `ProcessIncomingItem` 内禁止 File I/O 等慢操作；如需落盘，handler 应投递到后台线程并立即返回 `Handled`。参考《当前遗留问题与稳定性汇总.md》§6.4 反压要求 |
| 客户端 `discoveredExtensions.ItemCodecs` 消费后，Submod codec 异常导致 Trade 解码失败 | MEDIUM | `TradeClientItemPipeline.encodeTradeItem:69-87` / `decodePayloadOrUnknown:89-107` 已有"找不到 codec 建 UnknownItem"兜底；按 CodecId 严格匹配，不会误用 Submod codec 解 Trade 物品 |
| P1 `PayloadBytes` 直通路径与 P0 `PayloadJson` 路径服务端识别不一致 | LOW | P1 中 `ProcessIncomingItem` 入口先判 `PayloadBytes != empty`，二选一，互斥 |
| Submod 通过 `IClientOutgoingItemHandler` 发送的包绕过了能力协商 | MEDIUM | `TryHandleOutgoingItem` 内部仍走 `sendPacket`，能力协商由 `sendPacket` 统一处理（已有逻辑） |

### 8.3 设计哲学合规自审

实施完成后，对照《设计哲学.md》§7 提交 check-list 逐项验证：

- [ ] host 工程是否新增了对插件的引用？**否** —— 新接口在 `Common/Utils/Framework`，host 不引用插件
- [ ] Common 是否新增了 client-only 或 server-only 的代码？**否** —— 接口在共享契约层，实现在端侧
- [ ] 是否硬编码了具体业务类型？**否** —— 按 `IItemCodec.CodecId` 动态匹配，host 不知道"pawn"或"vanilla"
- [ ] 新增的扩展入口是否通过现有通用挂载点接入？**是** —— 通过 `IExtensionBuilder.Add*Item*` 接入，与 Message/Command 同构
- [ ] 插件间交互是否直接在插件间完成？**是** —— Submod 通过 codec 和 handler 接入，host 不中转业务
- [ ] 新增的网络处理是否有 try-catch 隔离？**是** —— 三段链每段独立 try-catch，参照 `ProcessIncomingMessage` 模式
- [ ] 新增的 `IDisposable` 资源持有者？**无新增** —— Item 管线不持有资源
- [ ] 新增的队列/缓冲区是否有容量上限？**N/A** —— P0 不引入新队列；如 P2 引入 Item 包缓存，必须设容量上限
- [ ] 对 Host/Core 的改动是否为增量式？**是** —— 仅新增接口与方法，不删除/修改既有签名
- [ ] 日志调用是否通过 `ILoggable` / `hostContext.Log` 上报？**是** —— 参照 `handleItem:251` 既有 `Logger` 模式
- [ ] 是否新增了公开 API？版本号是否需要升级为 `MINOR`？**是** —— 升级 `MINOR`
- [ ] Protobuf 字段变更是否兼容？**N/A** —— P0 不改 proto；P1 仅用既有 `FrameworkItemPacket` 字段，不新增字段编号

---

## 9. 验证方案

### 9.1 单元测试

**`ServerPipelineRunnerTests`（新增）：**
- `ProcessIncomingItem_NoInterceptor_CallsDefaultHandler` — 无 interceptor 时直接进 default handler
- `ProcessIncomingItem_InterceptorStopPropagation_SkipsDefaultHandler` — interceptor 返回 `StopPropagation` 时 default handler 不被调用
- `ProcessIncomingItem_DefaultHandlerThrows_IsolatesException_AndNotifiesObserver` — default handler 抛异常时链不中断，observer 仍被通知且 `result.FailureException` 非空
- `ProcessIncomingItem_ObserverThrows_IsolatesException` — observer 异常不影响主链
- `ProcessIncomingItem_PriorityOrdering` — 多个 interceptor 按 `Priority` 升序执行
- `ProcessIncomingItem_CodecMatch_DecodesSuccessfully` — 注册 mock codec，`CodecId` 匹配时 `DecodedItem` 非空
- `ProcessIncomingItem_NoCodecMatch_ReturnsFailure` — 无 codec 匹配时 `FailureReason` 非空

**`PhinixFrameworkClientItemRoutingTests`（新增）：**
- `PacketHandler_KindItem_DispatchesToHandleItem` — 发送 `Kind=item` 包，`IClientIncomingItemHandler.HandleItem` 被调用
- `HandleItem_HandlerThrows_IsolatesException_AndContinuesChain` — 第一个 handler 抛异常，第二个 handler 仍被调用
- `TryHandleOutgoingItem_NoHandler_ReturnsFalse_AndLogsError` — 无 handler 时返回 false 并记 Error
- `TryHandleOutgoingItem_FirstHandlerReturnsPacket_SendsImmediately` — 第一个返回非 null packet 者胜出

### 9.2 集成测试

**E2E-1：Submod 独立 Item 收发**
1. 编写 mock Submod：注册 `PawnItemCodec` + `IServerItemObserver` + `IClientIncomingItemHandler`
2. 客户端 `TryHandleOutgoingItem` 发送 mock Pawn payload
3. 验证服务端三段链全部触发（interceptor → handler → observer 顺序）
4. 验证客户端 `IClientIncomingItemHandler.HandleItem` 被调用

**E2E-2：Trade 回归**
1. P0 改动后跑完整 Trade 流程（创建/更新/接受/取消/完成）
2. 验证 `OfferUpdateRequest.Items` 嵌套路径行为字节级一致
3. 验证 `TradeClientItemPipeline` 现在能消费 Submod 注册的 codec（但不误用）

**E2E-3：P1 性能基准**
1. 100KB Pawn 数据通过 P1 直通路径发送
2. 对比 P0 路径 wire 体积
3. 验证降幅 ≥ 25%

### 9.3 黑盒验证

参照《当前遗留问题与稳定性汇总.md》§4：
- LegacyAdapter E2E 不受影响（Legacy 入站不走 Framework 管线）
- TradeWindow 回归必须通过
- 高并发测试：10 客户端同时发送 `KindItem` 包，无 `InvalidOperationException`、无崩溃

---

## 10. 实施顺序建议

```
Day 1: P0-1 ~ P0-4（接口定义 + Builder 实现 + Registry 集合）
       → 编译通过，无运行时变化
Day 2: P0-5 ~ P0-7（服务端三段链）
       → 服务端独立 Item 包能被三段链处理
Day 3: P0-8 ~ P0-11（客户端路由 + codec 消费）
       → 客户端能收发独立 Item 包
Day 4: P0-12 ~ P0-13（Trade codec 注入 + legacy 发现验证）
       → Trade 现在能消费 Submod codec
Day 5: P0-14 ~ P0-15（单元测试）+ E2E-1/E2E-2
       → 全部测试通过，P0 完成

P1（性能优化）单独排期，约 2-3 天
P2（Trade 迁移）远期，需 Trade 团队协同
```

---

## 11. 相关文档

- [设计哲学.md](../../设计哲学.md) — §1.1 插件平权、§3.2 三管道、§3.5 错误隔离、§3.6 反压、§3.7 管线不可绕过、§5.3 版本化、§6 渐进式迁移
- [三条Pipeline职责辨析与Item管线补全分析.md](三条Pipeline职责辨析与Item管线补全分析.md) — P0/P1/P2 步骤原始提出
- [框架Protobuf协议设计.md](框架Protobuf协议设计.md) — 三流模型与当前实现状态
- [框架Protobuf协议Schema定义.md](框架Protobuf协议Schema定义.md) — `FrameworkItemPacket` 等 proto schema
- [Talent-Trade迁移框架需求分析.md](Talent-Trade迁移框架需求分析.md) — 接口缺失清单与 P0/P1 优先级
- [架构耦合度与内聚度评估.md](架构耦合度与内聚度评估.md) — §2.3 Phase 4 Pipeline 接口现状
- [当前遗留问题与稳定性汇总.md](当前遗留问题与稳定性汇总.md) — §6.4 反压与 poll 线程阻塞约束
- [扩展开发体验增强-设计方案.md](扩展开发体验增强-设计方案.md) — §7 hook 点稳定性承诺（`IExtensionBuilder` 只会新增方法）

---

## 附录 A：与 Message/Command 管线的同构对照表

| 维度 | Message | Command | Item（本方案目标） |
|------|---------|---------|-------------------|
| Kind 常量 | `KindMessage = "message"` | `KindCommand = "command"` | `KindItem = "item"` |
| Flow 枚举 | `FrameworkFlow.Message = 1` | `FrameworkFlow.Command = 2` | `FrameworkFlow.Item = 3` |
| 客户端入站方法 | `handleMessage` | `handleCommand` | `handleItem`（新增） |
| 客户端入站 handler | `IClientMessageHandler` | `IClientCommandHandler` | `IClientIncomingItemHandler`（新增） |
| 客户端出站方法 | `TryHandleOutgoingMessage` | `TryHandleOutgoingCommand` | `TryHandleOutgoingItem`（新增） |
| 客户端出站 handler | `IClientMessageHandler.HandleOutgoingText` | `IClientOutgoingCommandHandler` | `IClientOutgoingItemHandler`（新增） |
| 服务端入站方法 | `handleMessage` | `handleCommand` | `handleItem`（已存在） |
| 服务端 interceptor | `IServerInboundMessageInterceptor` | `IServerInboundCommandInterceptor` | `IServerItemInterceptor`（新增） |
| 服务端 default handler | `IServerDefaultMessageHandler` | `IServerDefaultCommandHandler` | `IServerDefaultItemHandler`（新增） |
| 服务端 observer | `IServerMessageObserver` | `IServerCommandObserver` | `IServerItemObserver`（新增） |
| 结果类型 | `ServerIncomingMessageResult` | `ServerIncomingCommandResult` | `ServerIncomingItemResult`（新增） |
| 动作枚举 | `MessageHandlingResultAction` | `MessageHandlingResultAction`（复用） | `ItemHandlingResultAction`（新增） |
| Builder 注册方法 | `AddClientMessageHandler` 等 | `AddClientCommandHandler` 等 | `AddClientItemHandler` 等（新增） |
| 出站统一 | `DispatchOutbound` → `OutboundPacketInterceptor` → `SendPacket`（与 Kind 无关） |

## 附录 B：术语表

| 术语 | 含义 |
|------|------|
| 三段链 | InboundInterception → DefaultProcess → Observation 的服务端入站处理结构 |
| Codec | `IItemCodec` 实现，负责 Item 业务对象与 `FrameworkItemPayload` 互转 |
| 寄生 | Item 数据嵌套在 Command `PayloadJson` 内传输，无独立路由 |
| 直通 | Item 数据通过 `FrameworkPacket.PayloadBytes` 直接承载 protobuf，不经 JSON 嵌套 |
| 增量更新 | 设计哲学 §6 规定：Host/Core 只新增接口与方法，不删除/修改既有签名 |
