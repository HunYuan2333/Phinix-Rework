# Talent-Trade 迁移适配需求 —— 框架需补齐的功能点

> 基于对 [Talent-Trade](https://steamcommunity.com/sharedfiles/filedetails/?id=3685250832)（CA 小队成员交易模组，v2.0）的完整代码审计，评估其从原 Phinix 迁移到 Phinix-Rework 时，**框架侧需要补齐的接口、管线能力和基础设施**。
> 2026-05-31

---

## 1. 背景

### 1.1 Talent-Trade 做什么

Talent-Trade 是一个跨存档 Pawn（殖民者/动物/机械体/囚犯）交易模组，支持三种交易模式：

| 模式 | 说明 |
|------|------|
| 直接交易 | 双人在线实时协商，各自添加 Pawn + 白银，双方锁定后原子交换 |
| 公共市场 | 挂牌售卖，固定价格，买家浏览购买，空投交付 |
| 出租系统 | 殖民者出租，押金 + 日租金，"快照"模式（出租期间变更不保留） |

### 1.2 技术特征

- 约 4,400 行 C#（13 个源文件）
- 自建文本协议 `PHXTT|v1|<type>|field1|...`（pipe 分隔 + Base64 编码字段）
- 独立 HTTP Relay 服务器（`http://114.55.115.143:8080`，room `talent-trade`）作为主要传输通道
- **同时复用 Phinix 聊天通道**注入协议消息（副作用：需要 4 个 Harmony Patch 过滤聊天 UI）
- Pawn 数据序列化：Scribe XML → GZip → Base64，单条可达 10-100KB，大消息自动分片
- 身份/UI 强依赖原 Phinix 的 `Client.Instance` 单例 + `ServerTab` Harmony 注入

### 1.3 核心迁移诉求

将 Talent-Trade 从"通过 Harmony 寄生原 Phinix 的模组"转变为"Phinix-Rework 框架下的标准插件"，使其：

- 通过 `IPhinixExtensionModule.Register()` 注册所有能力
- 通过 `IMainTabProvider` 挂载 UI（不再 Harmony Patch `ServerTab`）
- 通过 Command 管线传输交易协议（不再注入聊天通道）
- 通过 Item 管线传输 Pawn 数据（不再走 HTTP Relay）
- 通过 `IClientSessionContext` / `IClientUserEventStream` 获取会话状态（不再调用 `Client.Instance` 单例）

---

## 2. 当前 Rework 管线能力评估

### 2.1 Command 管线 — ✅ 已就绪

Command 管线具备完整的 interceptor → handler → observer 链，支持 Request/Response/State/Event 四种语义。Talent-Trade 的 22 种协议消息类型可以直接映射为 Command 管线的 `MessageType`：

```
PHXTT|v1|mlist  → command MessageType "talent-trade.market.list"
PHXTT|v1|mbuy   → command MessageType "talent-trade.market.buy"
PHXTT|v1|trequest → command MessageType "talent-trade.trade.request"
...
```

现有 Trade 扩展的 Command 使用方式（`FrameworkTradeCreateRequest` 等 DataContract 嵌入 Command payload）为 Talent-Trade 提供了直接参考。

**无需框架改动。**

### 2.2 Message 管线 — ✅ 已就绪

Message 管线完整的 interceptor → handler → observer 链，面向用户可见消息。Talent-Trade 的交易结果通知（"交易成功""收到 Pawn"）可走此管线。

**无需框架改动。**

### 2.3 Item 管线 — ⚠️ 部分就绪，需补齐三处

当前 Item 管线存在的问题：

| 问题 | 影响 |
|------|------|
| 服务端 `ProcessIncomingItem` 解码后立即丢弃结果 | 无法在服务端验证 Pawn 数据合法性、检查 Def 兼容性 |
| `IItemCodec` 无法通过 `IExtensionBuilder` 注册 | Talent-Trade 的 `PawnItemCodec` 无法让 Trade 扩展或其他扩展发现和使用 |
| 客户端 `TradeClientItemPipeline` 硬编码 codec 列表 | 第三方 codec 无法注入 |

详见 §3.1–§3.3。

### 2.4 UI 扩展 — ✅ 已就绪

`IMainTabProvider` / `IServerSidebarProvider` / `IBadgeProvider` 接口完整。Talent-Trade 的三个子 Tab（DirectTrade / Market / Rental）和角标（未读交易请求数）可直接通过这些接口挂载。

当前的 `ServerTabPatches.cs`（Harmony 注入 Tab）→ 改为 `builder.RegisterApi<IMainTabProvider>(this)`。现有的 Chat/Trade 扩展已提供参考实现。

**无需框架改动。**

### 2.5 会话与事件 — ⚠️ 需确认一项

Talent-Trade 需要获取**在线玩家列表**用于直接交易面板（选择交易对象）。原代码调 `Client.Instance.GetUserUuids(bool onlineOnly)`。

需要确认 `IClientSessionContext` 或 `IClientUserDirectory` 是否已暴露此能力。如果没有，需新增一个方法。详见 §3.5。

---

## 3. 需要补齐的功能点

### 3.1 Item 管线服务端处理器链（重要）

**现状：**

```csharp
// ServerPipelineRunner.ProcessIncomingItem —— 当前实现
public bool ProcessIncomingItem(FrameworkPacket item, ServerFrameworkContext context)
{
    // ... deserialize payload ...
    foreach (IItemCodec codec in ItemCodecs)
    {
        if (!codec.CanDecode(payload, codecContext)) continue;
        object decoded = codec.Decode(payload, codecContext);
        // round-trip 验证后丢弃
        if (codec.CanEncode(decoded, codecContext))
            codec.Encode(decoded, codecContext);
        return true;
    }
    return false;
}
```

**问题：** Item 在服务端解码后立即丢弃，没有拦截/处理/观察链。对比 Command 管线的三层处理（interceptor → handler → observer），Item 管线在服务端是"只解码不处理"的。

**为什么需要补齐：**

- **交易场景**：服务端在转发 Pawn 数据前需验证数据完整性（如 Base64 解码成功、Def 兼容性检查）
- **审计/日志**：需要观察器记录物品传输事件（谁给谁传了什么 Pawn）
- **内容过滤**：拦截器可以阻止违规内容（如检测到特定禁止交易的 Pawn 类型）
- **插件平权**（§1.1）：Item 管线应和 Command/Message 管线一样，支持插件通过相同的 interceptor/handler/observer 模式参与处理

**新增接口：**

```csharp
// 放在 Common/Utils/Framework/FrameworkTypes.cs

public interface IServerItemInterceptor : IItemHandler
{
    bool CanInterceptIncomingItem(FrameworkPacket item);
    ServerIncomingItemResult InterceptIncomingItem(FrameworkPacket item, ServerFrameworkContext context);
}

public interface IServerDefaultItemHandler : IItemHandler
{
    bool CanHandleIncomingItem(FrameworkPacket item);
    ServerIncomingItemResult HandleIncomingItem(FrameworkPacket item, ServerFrameworkContext context);
}

public interface IServerItemObserver : IItemHandler
{
    bool CanObserveIncomingItem(FrameworkPacket item);
    void ObserveIncomingItem(FrameworkPacket item, ServerFrameworkContext context, ItemHandlingResultAction terminalAction);
}
```

**新增结果类型：**

```csharp
public enum ItemHandlingResultAction
{
    Continue,   // 继续下一个处理器
    Handled,    // 已处理，停止
    Block       // 阻止（不转发）
}

public sealed class ServerIncomingItemResult
{
    public ItemHandlingResultAction Action { get; set; }
    public FrameworkPacket ModifiedItem { get; set; }  // 拦截器可修改
    public string Reason { get; set; }                 // Block 原因
}
```

**PipelineRunner 改动：**

`ProcessIncomingItem` 改为三阶段执行：interceptors → default handlers → observers，与 `ProcessIncomingCommand` 同构。

### 3.2 IItemCodec 的扩展注册与发现（重要）

**现状：** `IItemCodec` 不在 `IExtensionBuilder` 的注册方法中。目前 `TradeClientItemPipeline` 自己维护 `List<IItemCodec>` 并在构造函数中硬编码 `DefaultLegacyTradeItemCodec`。

**为什么需要补齐：**

Talent-Trade 需要注册一个 `PawnItemCodec`（CodecId `"talent-trade.pawn"`）来序列化/反序列化 Pawn 数据。如果 Trade 扩展或 Talent-Trade 自己无法通过框架注册和发现 codec，就会退化为各自维护私有 codec 列表，违背插件平权原则。

**方案选择：**

| 方案 | 做法 | 优缺点 |
|------|------|--------|
| A. `builder.AddItemCodec(codec)` | 在 `IExtensionBuilder` 上加新方法 | 与 `AddServerMessageObserver` 风格一致，简单直接 |
| B. `builder.RegisterApi<IItemCodec>(codec)` | 复用 API Registry，调用方 `ResolveAll<IItemCodec>()` 收集 | 更灵活，允许 codec 作为 API 被其他扩展发现；但 codec 天然是多实例的（每种物品类型一个），API Registry 的 `TryResolve<T>` 语义偏向单例 |

**建议：方案 A**。Codec 不是"服务 API"，而是"可插拔的编解码器集合"。语义上更接近 handler 注册（多个实现共存、按顺序匹配），而非 API 解析（通常一个接口一个实现）。

**新增方法：**

```csharp
// IExtensionBuilder 新增
public interface IExtensionBuilder
{
    // ... 已有方法 ...

    /// <summary>
    /// 注册一个 Item Codec。Codec 按 Priority 排序，
    /// 解码时按顺序匹配第一个 CanDecode 返回 true 的 codec。
    /// </summary>
    void AddItemCodec(IItemCodec codec, int priority = 0);
}
```

**PhinixExtensionRegistry 改动：**

- `DiscoveredPhinixExtensions` 新增 `List<PrioritizedComponent<IItemCodec>> ItemCodecs` 字段
- `BuildFromModules()` 中收集 codec 并按 priority 排序
- `ServerPipelineRunner` 和 `TradeClientItemPipeline` 从 registry 获取 codec 列表（而非各自硬编码）

### 3.3 客户端 Item Codec 聚合（配套 3.2）

**现状：** `TradeClientItemPipeline` 在构造函数中创建 `List<IItemCodec>`，手动 `Add(new DefaultLegacyTradeItemCodec())`。不具备从框架收集所有已注册 codec 的能力。

**改动：**

`TradeClientItemPipeline` 改为从 `IExtensionApiRegistry` 或 `DiscoveredPhinixExtensions` 中获取所有 `IItemCodec` 实例：

```csharp
// Before（硬编码）
_itemCodecs = new List<IItemCodec> { new DefaultLegacyTradeItemCodec() };

// After（动态收集）
_itemCodecs = extensionRegistry.GetItemCodecs(); // 按 priority 排序
```

注意：此改动影响 `TradeClientItemPipeline` 和 `TradeItemConverter` 的初始化方式，但不改变其公共 API。

### 3.4 大数据块传输优化（建议）

**现状：** Item 管线的数据流是：

```
byte[] PayloadBytes
  → FrameworkItemPayload (DataContract, PayloadBytes 字段)
    → JSON 序列化（byte[] → Base64 字符串, ~1.37x 膨胀）
      → 放入 FrameworkPacket.PayloadJson（string）
        → FrameworkPacket 整体 JSON 序列化
          → 网络发送
```

Pawn 数据 GZip 后约 10-100KB，经 Base64 膨胀后约 14-137KB，加上两次 JSON 包装的开销。

**问题不在于绝对大小**（100KB 对网络传输来说不是问题），而在于：

1. 两次全量 JSON 序列化/反序列化的 CPU 开销
2. 中间层 Base64 字符串在堆上分配，增加 GC 压力
3. 无法流式处理或分片（当前无 chunking 机制）

**建议：**

短期（不影响 Talent-Trade 迁移）：框架的 `FrameworkPacket` 已有 `PayloadBytes` 字段（`byte[]`，序号 11），可以将二进制数据直接放在外层 `PayloadBytes` 中，`PayloadJson` 仅放 codecId 等元数据，跳过内层 JSON 的 Base64 膨胀。这需要在 `FrameworkSerialization` 中增加一条"二进制直通"路径。

中长期：参考 Talent-Trade 现有的 blob 分片机制（`PHXTT|v1|blob_part|...`），在 Item 管线层实现通用的分片/重组，对 codec 透明。

**此项不是阻塞项**——Talent-Trade 迁移可以先用现有 Item 管线跑通，性能优化作为后续迭代。

### 3.5 在线用户列表 API（需确认）

**现状：** Talent-Trade 的 Direct Trade 面板需要展示在线玩家列表，供发起交易选择。原代码调：

```csharp
Client.Instance.GetUserUuids(onlineOnly: true)
Client.Instance.TryGetDisplayName(uuid, out name)
```

**需确认：** `IClientSessionContext` 或 `IClientUserDirectory` 是否已暴露等价方法？如果有，此项无需改动。如果没有，需要新增。

**如果需要新增：**

```csharp
// 在 ClientExtensionAbstractions 中
public interface IClientUserDirectory
{
    /// <summary>获取所有已知用户的 UUID 列表</summary>
    IReadOnlyList<string> GetUserUuids(bool onlineOnly = false);

    /// <summary>通过 UUID 获取显示名称</summary>
    bool TryGetDisplayName(string uuid, out string displayName);

    /// <summary>获取当前本地用户的 UUID</summary>
    string LocalUuid { get; }
}
```

在 `ExtensionHostContext` 中作为 host 服务注入：`hostContext.AddService<IClientUserDirectory>(clientUserDirectory)`。

### 3.6 扩展加载目录确认（配套）

Talent-Trade 作为第三方插件，其 DLL 需要被 `ExtensionAssemblyLoader` 发现和加载。

确认点：

- `ExtensionAssemblyLoader.LoadAssemblies()` 的探测目录是否包含用户扩展目录（如 `Client/Common/Extensions/` 或 `UserExtensions/`）？
- 如果不是，需要新增一个用户扩展加载路径

**此项为部署/构建链问题**，不影响 API 设计，但必须在迁移前确认。

---

## 4. 新增接口汇总

### 4.1 新增接口清单

| 接口 | 位置 | 用途 |
|------|------|------|
| `IServerItemInterceptor` | `Common/Utils/Framework/FrameworkTypes.cs` | 服务端物品拦截器 |
| `IServerDefaultItemHandler` | 同上 | 服务端物品默认处理器 |
| `IServerItemObserver` | 同上 | 服务端物品观察器 |
| `ServerIncomingItemResult` | 同上 | 服务端物品处理结果 |
| `ItemHandlingResultAction` | 同上 | 物品处理动作枚举 |
| `IExtensionBuilder.AddItemCodec()` | 同上 | 注册 Item Codec |
| `IClientUserDirectory` | `Client/ClientExtensionAbstractions/` | 用户目录查询接口（如缺失） |

### 4.2 变更文件清单

| 文件 | 变更类型 | 说明 |
|------|----------|------|
| `Common/Utils/Framework/FrameworkTypes.cs` | 新增接口 + 新类型 | 新增 §4.1 中的接口和结果类型 |
| `Common/Utils/Framework/PhinixExtensionRegistry.cs` | 修改 | `DiscoveredPhinixExtensions` 新增 `ItemCodecs` 字段；`BuildFromModules` 收集 codec |
| `Server/ServerRuntime/ServerPipelineRunner.cs` | 修改 | `ProcessIncomingItem` 改为三阶段链 |
| `Extensions/Trade/Client/TradeClientItemPipeline.cs` | 修改 | codec 列表从硬编码改为从 registry 获取 |
| `Client/ClientExtensionAbstractions/` | 可能新增 | `IClientUserDirectory` 接口（如需） |
| `Server/Framework/PhinixFrameworkServer.cs` | 修改 | Item 管线初始化适配新的 codec 注册方式 |

### 4.3 不需要变更的部分

| 项目 | 原因 |
|------|------|
| Command 管线接口 | 已完整，Talent-Trade 协议消息直接映射为 Command MessageType |
| Message 管线接口 | 已完整 |
| `IMainTabProvider` / `IServerSidebarProvider` / `IBadgeProvider` | 已完整，Talent-Trade UI 直接实现即可 |
| `IClientMainThreadDispatcher` | 已完整，替代 `Root.Update` Harmony Patch |
| `IClientUserEventStream` | 已完整，替代 `Client.Instance.OnDisconnect` |
| `FrameworkPacket` 结构 | 已有 `Kind = "item"` 和 `PayloadBytes`，无需新增字段 |
| `FrameworkItemPayload` | 已有 `CodecId` + `PayloadBytes`，足够承载 Pawn 数据 |
| `IItemCodec` 接口 | 现有定义无需改动 |
| 分片/重组机制 | 非阻塞项，Talent-Trade 自带分片逻辑，可后续框架化 |

---

## 5. 实施优先级

### P0 — 阻塞迁移，必须先做

| 序号 | 项目 | 预估工作量 |
|------|------|-----------|
| P0-1 | `IExtensionBuilder.AddItemCodec()` + Registry 收集 | 0.5 天 |
| P0-2 | `TradeClientItemPipeline` 动态收集 codec | 0.5 天 |
| P0-3 | 确认/新增 `IClientUserDirectory` | 0.5-1 天 |

**P0 完成后**，Talent-Trade 可以在框架中注册 `PawnItemCodec`、获取在线用户列表，开始插件化改造。

### P1 — 提升架构完整性

| 序号 | 项目 | 预估工作量 |
|------|------|-----------|
| P1-1 | Item 管线服务端三阶段处理器链 | 1-2 天 |
| P1-2 | `ServerPipelineRunner.ProcessIncomingItem` 重构 | 1 天 |

**P1 完成后**，Item 管线与 Command/Message 管线在服务端具备同等的可扩展性，满足插件平权原则。

### P2 — 优化（非阻塞）

| 序号 | 项目 | 预估工作量 |
|------|------|-----------|
| P2-1 | `PayloadBytes` 二进制直通路径（跳过 JSON 内层 Base64） | 1-2 天 |
| P2-2 | 通用分片/重组机制 | 2-3 天 |

---

## 6. 与设计哲学的对齐检查

对照 [`设计哲学.md`](./设计哲学.md) 逐项检查：

| 原则 | 检查结果 |
|------|----------|
| **插件平权（§1.1）** | ✅ 补齐后 Item 管线与 Message/Command 管线具备同等的 interceptor/handler/observer 链，新 codec 与内置 codec 通过同一 `AddItemCodec()` 注册 |
| **host 不依赖插件（§1.2）** | ✅ 所有新增接口在 `Common` 或 `ClientExtensionAbstractions` 中定义，host 不引用 Talent-Trade 工程 |
| **host 只做通用服务（§1.3）** | ✅ `IClientUserDirectory`（如新增）是通用用户查询，不绑定交易业务 |
| **松耦合（§2.1）** | ✅ PawnItemCodec 通过 `IItemCodec` 接口被管线消费，管线不依赖具体 Pawn 类型 |
| **层次化（§2.2）** | ✅ 新增接口在 Common 层（基础设施/契约），实现在插件层 |
| **减少硬编码（§2.3）** | ✅ `AddItemCodec` 替代 `TradeClientItemPipeline` 硬编码 codec 列表 |
| **三管道（§3.2）** | ✅ 交易协议 → Command；用户通知 → Message；Pawn 数据 → Item。三条管线各司其职 |
| **渐进式迁移（§6）** | ✅ P0 → P1 → P2 分层推进，每阶段可编译可运行 |
| **插件不得绕过通信管线（§3.7）** | ✅ 迁移后 Talent-Trade 通过 Command/Item handler 通信，不再直接操作 HTTP Relay 或聊天通道注入 |

---

## 7. 附：Talent-Trade 迁移后的目标架构

```
TalentTradeExtension (IPhinixExtensionModule)
├── Register(builder)
│   ├── builder.AddItemCodec(new PawnItemCodec())         ← P0-1 新增
│   ├── builder.AddClientCommandHandler(this)              ← 已有
│   ├── builder.AddServerDefaultCommandHandler(this)       ← 已有
│   ├── builder.RegisterApi<IMainTabProvider>(this)        ← 已有
│   └── builder.RegisterApi<ITalentTradeApi>(this)         ← 已有（供其他扩展调用）
│
├── PawnItemCodec : IItemCodec
│   ├── CodecId = "talent-trade.pawn"
│   ├── Encode(Pawn) → FrameworkItemPayload { PayloadBytes: GZip(Pawn XML) }
│   └── Decode(FrameworkItemPayload) → Pawn
│
├── TalentTradeCommandHandler : IClientCommandHandler, IServerDefaultCommandHandler
│   ├── 替代 HTTP Relay —— 协议消息走 Command 管线
│   └── 替代聊天通道注入 —— 不再发 PHXTT 文本到聊天
│
└── TalentTradeMainTab : IMainTabProvider
    ├── 替代 ServerTabPatches（Harmony 注入）
    └── 三个子 Tab：DirectTrade / Market / Rental
```

---

> **文档维护约定**：当框架侧 P0/P1 项目完成、或 Talent-Trade 迁移实际启动后，更新本文档标记完成状态。涉及接口变更时同步更新 `设计哲学.md` 中的管线描述。
