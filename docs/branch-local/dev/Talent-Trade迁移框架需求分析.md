# Talent-Trade 迁移框架需求分析

> 基于对 [Talent-Trade](https://steamcommunity.com/sharedfiles/filedetails/?id=3685250832) 的完整代码审计，评估从原 Phinix 迁移到 Phinix-Rework 时框架侧需补齐的能力。
> 2026-05-31，修订：2026-06-01

---

## 1. 背景

Talent-Trade 是跨存档 Pawn 交易模组（~4400 行 C#），支持直接交易、公共市场、出租系统。当前实现依赖原 Phinix 的 `Client.Instance` 单例、Harmony 注入 `ServerTab`、HTTP Relay 传输、自建文本协议管道。

迁移目标：转为标准 `IPhinixExtensionModule`，通过 Command 管线传输协议、Item 管线传输 Pawn 数据、`IMainTabProvider` 挂载 UI。

---

## 2. 当前管线能力评估（经验证）

| 管线 | 状态 | 说明 |
|------|------|------|
| Command | ✅ 已就绪 | 完整的 interceptor→handler→observer 链，`IClientOutgoingCommandHandler` 出站已实现 |
| Message | ✅ 已就绪 | 完整三阶段链 |
| Item | ⚠️ 部分就绪 | codec 注册已有（`IExtensionBuilder.AddItemCodec()`），客户端 `TradeClientItemPipeline` 已支持 `extensionCodecs` 参数。但服务端 `ProcessIncomingItem` 无三阶段链 |
| UI | ✅ 已就绪 | `IMainTabProvider/IServerSidebarProvider/IBadgeProvider` 完整 |
| 用户目录 | ✅ 已就绪 | `IClientUserDirectory` 接口已定义：`GetUsers(bool loggedIn)`、`TryGetUser(uuid)` |
| 会话/事件 | ✅ 已就绪 | `IClientSessionContext`、`IClientUserEventStream`、`IClientMainThreadDispatcher` |

---

## 3. 需要补齐的功能点

### P0 — `IExtensionBuilder.AddItemCodec()` ✅ 已可用
- 接口已定义（`FrameworkTypes.cs:129`），registry 已收集 `ItemCodecs`
- `TradeClientItemPipeline` 构造函数已支持 `IEnumerable<IItemCodec> extensionCodecs` 注入
- **P0 完成**

### P0 — `IClientUserDirectory` ✅ 已可用
- 接口已定义：`GetUsers(bool loggedIn)` 返回 `ImmutableUser[]`
- `TryGetUser(string uuid, out ImmutableUser user)` 可用
- **P0 完成**

### P1 — Item 管线服务端三阶段处理器链 ✅ 已实现（2026-06-21）
- `IServerItemInterceptor`、`IServerDefaultItemHandler`、`IServerItemObserver` 均已定义
- `ProcessIncomingItem` 重写为三段链 + 内置 codec 兜底
- Client 端 `KindItem` 独立路由 + `IClientIncomingItemHandler` / `IClientOutgoingItemHandler` / `TryHandleOutgoingItem` 就绪

### P2 — PayloadBytes 二进制直通（未实现）
- 当前仍然经过两次 JSON 序列化（内层 bytes→base64 1.37x 膨胀）

---

## 4. 新增接口清单

| 接口 | 代码状态 |
|------|---------|
| `IServerItemInterceptor` | ✅ 已实现（P0 补全） |
| `IServerDefaultItemHandler` | ✅ 已实现（P0 补全） |
| `IServerItemObserver` | ✅ 已实现（P0 补全） |
| `ServerIncomingItemResult` | ✅ 已实现（P0 补全） |
| `ItemHandlingResultAction` | ✅ 已实现（P0 补全） |
| `IExtensionBuilder.AddItemCodec()` | ✅ 已存在 |
| `IClientIncomingItemHandler` | ✅ 已实现（P0 补全） |
| `IClientOutgoingItemHandler` | ✅ 已实现（P0 补全） |
| `IItemCodecProvider` | ✅ 已实现（P0 补全） |

**Client 侧也已补全**——`IClientIncomingItemHandler` / `IClientOutgoingItemHandler` / `IItemCodecProvider` 均已就绪。

---

## 5. 实施优先级

| 优先级 | 项目 | 当前状态 |
|--------|------|---------|
| P0 | `AddItemCodec` + registry 收集 | ✅ 完成 |
| P0 | `IClientUserDirectory` | ✅ 完成 |
| P0 | `TradeClientItemPipeline` 动态收集 codec | ✅ Activate 阶段通过 `IItemCodecProvider` 注入完整 codec 列表 |
| P0 | Item 管线服务端三段链 + Client 双端路由 | ✅ 完成（2026-06-21） |
| P1 | PayloadBytes 二进制直通路径 | ❌ 待实现 |
| P2 | Trade 的 Item payload 从 Command 嵌套迁至 KindItem | ❌ 远期 |

---

## 6. 目标架构

```
TalentTradeExtension (IPhinixExtensionModule)
├── Register(builder)
│   ├── builder.AddItemCodec(new PawnItemCodec())       ← P0 已可用
│   ├── builder.AddClientCommandHandler(this)            ← 已有
│   ├── builder.RegisterApi<IMainTabProvider>(this)     ← 已有
│   └── builder.RegisterApi<ITalentTradeApi>(this)      ← 已有
├── PawnItemCodec : IItemCodec                         ← 新增
├── TalentTradeCommandHandler                          ← 新增（替代 HTTP Relay）
└── TalentTradeMainTab : IMainTabProvider               ← 新增（替代 Harmony 注入）
```

---

## 7. 相关文档

- [设计哲学.md](设计哲学.md)
- [框架Protobuf协议设计.md](框架Protobuf协议设计.md)
- [架构耦合度与内聚度评估.md](架构耦合度与内聚度评估.md)
