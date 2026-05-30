# Legacy Trade 出站修复方案

> 2026-05-31，针对问题：交易列表能显示，但状态不同步（接受/取消对方看不见）且物品不可见（双方都看不见对方上传的物品）。

---

## 1. 症状与根因

### 症状

1. **状态不同步**：点击接受/取消后对方完全无感知
2. **物品不可见**：上传的物品双方在意向清单中都看不见

### 根因

[`FrameworkClientTradeServiceAdapter.SendTradePacket()`](Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs) 在 Legacy 模式下**静默丢弃所有出站包**：

```csharp
// 现状
private void SendTradePacket(FrameworkPacket packet)
{
    if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
        frameworkClient.SendFrameworkPacket(packet);
    else
        log?.Invoke("dropped...", WARNING);  // ← 全丢了
}
```

四个出站方法 `CreateTrade`、`CancelTrade`、`UpdateTradeItems`、`UpdateTradeStatus` 在 Legacy 模式下全部无效。服务器收不到任何指令，自然无法转发给另一方。

---

## 2. 当前出站链路

```
FrameworkV2:  UI → tradeFacade → tradeService.Create*Request() → FrameworkPacket
                → frameworkClient.SendFrameworkPacket("PhinixFramework") → server ✅

Legacy:       UI → tradeFacade → tradeService.Create*Request() → FrameworkPacket
                → SendTradePacket() → [丢弃] ❌
```

问题本质：Trade 插件已经通过 `Create*Request()` 构造了 FrameworkPacket，但那个 Packet 的 payload 是 JSON 格式的 `FrameworkTradeCreateRequest` 等类型——旧服务器不认识。必须转译为旧版 Protobuf 格式后通过 `"Trading"` 模块发送。

---

## 3. 设计哲学约束

| 条目 | 约束 | 影响 |
|------|------|------|
| §3.7 | 出站命令通过 `CompatibilityMode` 判断后决定路由策略 | Trade 插件自己判断模式，不能依赖外部"有人会拦截" |
| §1.3 | `ILegacyModuleTransport` 是 host 通用服务 | Trade 可以直接用它发包，不违反层次化原则 |
| §3.3 | 插件间定义接口并直接调用，框架不充当中介 | Trade 定义接口，LegacyAdapter 实现，通过 API registry 解析 |
| §4.1 | Trade 不引用 LegacyAdapter | 通过 API registry 动态解析，编译期不依赖 |

---

## 4. 方案

### 4.1 新增接口：`ILegacyTradeOutboundApi`

定义在 [`TradeExtension.Contracts`](Extensions/Trade/Contracts/TradeDomainContracts.cs)（`#if NET472` 块内）：

```csharp
namespace PhinixClient.Framework
{
    /// <summary>
    /// Legacy 出站交易 API。由 LegacyAdapter 注册，
    /// Trade 插件在 Legacy 模式下通过 API registry 解析并调用。   
    /// 设计哲学 §3.3：插件间交互由插件自行定义接口。
    /// </summary>
    public interface ILegacyTradeOutboundApi
    {
        void SendCreateTrade(string otherPartyUuid);
        void SendUpdateItems(string tradeId, IEnumerable<TradeItemSnapshot> items);
        void SendUpdateStatus(string tradeId, bool accepted, bool cancelled);
    }
}
```

### 4.2 新增实现：`LegacyTradeOutboundAdapter`

新文件 [`Extensions/LegacyAdapter/Client/LegacyTradeOutboundAdapter.cs`](Extensions/LegacyAdapter/Client/LegacyTradeOutboundAdapter.cs)：

**职责**：
- 实现 `ILegacyTradeOutboundApi`
- 持有 `ILegacyModuleTransport` + `IClientSessionContext`
- `TradeItemSnapshot` → `Trading.ProtoThing` 转换
- 构造旧版 Protobuf 包 → Pack → `Send("Trading")`

**物品转换**：

```csharp
private static Trading.ProtoThing ConvertToProtoThing(TradeItemSnapshot item)
{
    return new Trading.ProtoThing
    {
        DefName     = item.DefName ?? "",
        StackCount  = item.StackCount,
        HitPoints   = item.HitPoints,
        StuffDefName = item.StuffDefName ?? "",
        Quality     = (Trading.Quality)(int)item.Quality,  // 枚举值一一对应可直接 cast
        InnerProtoThing = item.InnerItem != null
            ? ConvertToProtoThing(item.InnerItem)
            : null
    };
}
```

### 4.3 调用方修改：`FrameworkClientTradeServiceAdapter`

Legacy 模式下不再调用 `tradeService.Create*Request()` 构造无用的 FrameworkPacket，改为直接调用 `ILegacyTradeOutboundApi`：

```csharp
// 改后
public void UpdateTradeStatus(string tradeId, bool? accepted, bool? cancelled)
{
    if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
        frameworkClient.SendFrameworkPacket(
            tradeService.CreateStatusUpdateRequest(tradeId, accepted, cancelled, createContext()));
    else
        legacyOutboundApi?.SendUpdateStatus(tradeId, accepted ?? false, cancelled ?? false);
}
```

`CreateTrade`、`CancelTrade`、`UpdateTradeItems` 同理。

### 4.4 注册方修改

**LegacyAdapter**（[`BuiltInLegacyAdapterClientExtension.Register()`](Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs)）：

```csharp
builder.RegisterApi<ILegacyTradeOutboundApi>(
    new LegacyTradeOutboundAdapter(legacyTransport, sessionContext));
```

**Trade**（[`BuiltInTradeClientExtension.Register()`](Extensions/Trade/Client/BuiltInTradeClientExtension.cs)）：

```csharp
builder.HostContext.ApiRegistry.TryResolve<ILegacyTradeOutboundApi>(out var legacyOutboundApi);
tradeFacade = new FrameworkClientTradeServiceAdapter(
    tradeApi, frameworkClient, lifecycle, sessionContext,
    legacyOutboundApi,  // V2 模式下可能为 null
    builder.HostContext.Log);
```

---

## 5. 修改文件清单

| # | 文件 | 改动 | 说明 |
|---|------|------|------|
| 1 | `TradeDomainContracts.cs` | 新增 | `ILegacyTradeOutboundApi` 接口 |
| 2 | `LegacyTradeOutboundAdapter.cs` | **新文件** | 协议转换 + 发包 |
| 3 | `BuiltInLegacyAdapterClientExtension.cs` | 修改 | Register() 中注册 ILegacyTradeOutboundApi |
| 4 | `PhinixLegacyAdapter.Client.csproj` | 修改 | 加入新 .cs |
| 5 | `FrameworkClientTradeServiceAdapter.cs` | 修改 | 注入 ILegacyTradeOutboundApi，Legacy 模式下走新路径 |
| 6 | `BuiltInTradeClientExtension.cs` | 修改 | 解析 ILegacyTradeOutboundApi 并注入到 Adapter |
| 7 | `LegacyTradeProtocolAdapter.cs` | 删减 | 删除 SendCreateTrade/SendUpdateItems/SendUpdateStatus（移入新 Adapter） |

---

## 6. 职责边界

```
TradeExtension.Contracts
  └─ ILegacyTradeOutboundApi         ← 接口定义（Trade 知道调用什么）

LegacyAdapter (实现)
  ├─ LegacyTradeOutboundAdapter      ← TradeItemSnapshot→ProtoThing + 发包
  └─ LegacyTradeProtocolAdapter      ← 入站翻译（Proto→FrameworkStateSnapshot，不变）

TradeExtension.Client (调用)
  └─ FrameworkClientTradeServiceAdapter ← CompatibilityMode 路由
```

编译期引用：

```
TradeExtension.Client → TradeExtension.Contracts  ← 已有
LegacyAdapter.Client  → TradeExtension.Contracts  ← 已有
TradeExtension.Client → LegacyAdapter.Client      ← 不需要，API registry 运行时解析
```

---

## 7. 修复后出站链路

```
FrameworkV2:  UI → tradeFacade → tradeService.Create*Request() → FrameworkPacket
                → frameworkClient.SendFrameworkPacket("PhinixFramework") → server ✅

Legacy:       UI → tradeFacade → legacyOutboundApi.Send*()
                → LegacyTradeOutboundAdapter
                  ├─ ConvertToProtoThing()  ← TradeItemSnapshot → ProtoThing
                  ├─ new UpdateTradeStatusPacket { ... }
                  ├─ ProtobufPacketHelper.Pack()
                  └─ ILegacyModuleTransport.Send("Trading", bytes) → server ✅
```

---

## 8. 与入站的对称性

| | 方向 | 格式转换 | 载体 |
|------|------|----------|------|
| `LegacyTradeProtocolAdapter` | 入站 (server→client) | Legacy Proto → FrameworkStateSnapshot → UpsertTrade | `ILegacyModuleTransport.RegisterHandler` |
| `LegacyTradeOutboundAdapter` | 出站 (client→server) | TradeItemSnapshot → Legacy Proto → Send | `ILegacyModuleTransport.Send` |

两个 Adapter 互相独立，职责单一。
