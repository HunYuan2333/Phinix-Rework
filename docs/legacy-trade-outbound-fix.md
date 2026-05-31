# Legacy Trade 出站修复方案

> 2026-05-31。症状：交易列表能显示，但物品不可见 + 状态不同步。

---

## 1. 根因分析

### 1.1 直接原因：Items 字段丢失

[`FrameworkClientTradeServiceAdapter.SendLegacyPacket()`](Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs#L177-L188) 的 `OfferUpdateRequestType` 分支构造 `UpdateTradeItemsPacket` 时遗漏了 `Items` 字段：

```csharp
// 现状 — 有 bug
case FrameworkTradeProtocol.OfferUpdateRequestType:
    var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(packet.PayloadJson);
    if (payload != null)
        legacyTransport.Send("Trading", PackProtobuf(new Trading.UpdateTradeItemsPacket
        {
            SessionId = sessionContext.SessionId ?? "",
            Uuid = sessionContext.Uuid ?? "",
            TradeId = payload.TradeId ?? "",
            Token = packet.GetCorrelationId() ?? ""
            // BUG: Items 未填充。payload.Items (List<FrameworkItemPayload>) 被丢弃
            //      服务端收到空 Items → TryClearItemsOnOffer() → 物品被清空
        }));
    break;
```

对照原版 `ClientTrading.UpdateItems()`（`C:\Users\zydyo\Desktop\Phinix\Phinix\Common\Trading\ClientTrading.cs`）：

```csharp
// 原版 — 正确
var packet = new UpdateTradeItemsPacket {
    SessionId = authenticator.SessionId,
    Uuid = userManager.Uuid,
    TradeId = tradeId,
    Items = {items},    // ← 必须填充
    Token = token
};
```

**CreateTrade 和 UpdateTradeStatus 分支数据完整，无 bug。**

### 1.2 架构原因：出站命令管线缺失

`FrameworkClientTradeServiceAdapter` 之所以存在内联的 `SendLegacyPacket()` 方法做 Legacy Proto 翻译，根本原因是**框架没有出站命令管线**。

现状对比：

| | 入站 | 出站 |
|------|:--:|:--:|
| Message | `IClientMessageHandler.HandleIncomingMessage` ✅ | `IClientMessageHandler.HandleOutgoingText` ✅ |
| Command | `IClientCommandHandler.HandleIncomingCommand` ✅ | **缺失** ❌ |

`IClientCommandHandler` 只有入站方法，没有出站。`SendFrameworkPacket()` 直连 `NetClient`，绕过了所有 handler。Trade 插件不得不在 `FrameworkClientTradeServiceAdapter` 里手写两种模式的出站路由——V2 走 `SendFrameworkPacket`，Legacy 走 `ILegacyModuleTransport.Send`，两条都是旁路。

设计哲学 §3.7 明确要求通信通过管线。既然管线缺失，就应该建管线。

---

## 2. 架构修复：新建出站命令管线

### 2.1 新增接口（不破坏现有接口）

在 `FrameworkTypes.cs` 中新增 `IClientOutgoingCommandHandler`，与 `IClientCommandHandler` 正交：

```csharp
/// <summary>
/// 客户端出站命令管线接口。Handler 按 Priority 排序依次执行。
/// 与 IClientCommandHandler（入站）正交——插件可以只实现其一或同时实现。
///
/// 设计哲学 §3.7：所有通信必须通过 handler 管线，不得直连传输层。
/// 设计哲学 §6：此接口为增量新增，不修改或删除现有 IClientCommandHandler。
/// </summary>
public interface IClientOutgoingCommandHandler : ICommandHandler
{
    /// <summary>Priority 越小越先执行。默认 int.MaxValue（最低优先级）。</summary>
    int ICommandHandler.Priority => int.MaxValue;

    /// <summary>判断此 handler 是否可以处理该出站命令。</summary>
    bool CanHandleOutgoingCommand(FrameworkPacket command);

    /// <summary>
    /// 处理出站命令。返回 FrameworkPacket 由框架发送。
    /// 返回 null 或 Action == Continue 则传递给下一个 handler。
    /// </summary>
    ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket command, ClientFrameworkContext context);
}

/// <summary>出站命令处理结果。</summary>
public sealed class ClientOutgoingCommandResult
{
    public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Handled;
    public FrameworkPacket Command { get; set; }
}
```

### 2.2 PhinixFrameworkClient 新增出站命令管线方法

参照已有 `TryHandleOutgoingMessage()` 模式：

```csharp
/// <summary>
/// 出站命令管线。按 Priority 顺序遍历所有 IClientOutgoingCommandHandler，
/// 首个 CanHandleOutgoingCommand 返回 true 的 handler 处理该命令。
/// 设计哲学 §3.7：插件不得绕过管线直连传输层。
/// </summary>
public bool TryHandleOutgoingCommand(FrameworkPacket command)
{
    if (command == null) return false;

    var context = new ClientFrameworkContext
    {
        CompatibilityMode = CompatibilityMode,
        SenderUuid = userManager.Uuid,
        SessionId = authenticator.SessionId,
        SendMessage = sendPacket,
        RemoteCapabilities = remoteCapabilities.ToArray(),
        HasRemoteCapability = hasRemoteCapability,
        Log = (msg, level) => RaiseLogEntry(new LogEventArgs(msg, level))
    };

    // 从已发现扩展中筛选实现了 IClientOutgoingCommandHandler 的实例
    foreach (var candidate in discoveredExtensions.Extensions)
    {
        if (!(candidate is IClientOutgoingCommandHandler handler)) continue;
        if (!handler.CanHandleOutgoingCommand(command)) continue;

        ClientOutgoingCommandResult result = null;
        try
        {
            result = handler.HandleOutgoingCommand(command, context);
        }
        catch (Exception ex)
        {
            RaiseLogEntry(new LogEventArgs(
                $"Outgoing command handler {handler.GetType().FullName} threw: {ex}", LogLevel.ERROR));
            continue;
        }

        if (result == null || result.Action == MessageHandlingResultAction.Continue)
            continue;

        FrameworkPacket outgoingCommand = result.Command;
        if (outgoingCommand == null)
        {
            if (result.Action == MessageHandlingResultAction.Handled) return true;
            continue;
        }

        outgoingCommand.Kind = FrameworkProtocol.KindCommand;
        outgoingCommand.SessionId = authenticator.SessionId;
        outgoingCommand.SenderUuid = userManager.Uuid;
        sendPacket(outgoingCommand);

        if (result.Action != MessageHandlingResultAction.Continue) return true;
    }

    return false;
}
```

> **注意**：`IClientOutgoingCommandHandler` 不像 `IClientCommandHandler` 那样在 `PhinixExtensionRegistry.DiscoverExtensions()` 中被自动收集到专用列表。这里通过 `is` 检查在运行时动态筛选——因为出站 handler 必然也是实现了扩展模块接口的实例。也不需要修改 `IExtensionBuilder`——出站能力不需要新的注册方法。

### 2.3 Trade 插件接入管线

`BuiltInTradeClientExtension` 或其内部类实现 `IClientOutgoingCommandHandler`，替代现在 `FrameworkClientTradeServiceAdapter` 中的旁路逻辑：

```csharp
// BuiltInTradeClientExtension 内部的出站 handler
internal sealed class TradeOutgoingCommandHandler : IClientOutgoingCommandHandler
{
    public int Priority => 50; // 在 LegacyAdapter 之前执行（Lower priority runs first? 或者反过来）
    private readonly IFrameworkTradeClientApi tradeService;
    private readonly IClientSessionContext sessionContext;

    public bool CanHandleOutgoingCommand(FrameworkPacket command)
    {
        // 仅处理 Trade 命名空间下的出站命令
        return command?.MessageType?.StartsWith("Phinix.Trade.") == true;
    }

    public ClientOutgoingCommandResult HandleOutgoingCommand(
        FrameworkPacket command, ClientFrameworkContext context)
    {
        // V2 模式下直接透传 FrameworkPacket 供框架发送
        // Legacy 模式下交由 LegacyAdapter（更高 Priority）拦截翻译
        return new ClientOutgoingCommandResult
        {
            Action = MessageHandlingResultAction.Handled,
            Command = command // 原样透传，由框架 sendPacket 发送
        };
    }
}
```

**关键**：V2 模式下 Trade handler 直接返回 FrameworkPacket，由管线统一下游发送。Legacy 模式时，LegacyAdapter（Priority 更高，如 Priority=10）抢先拦截命令并翻译为 Legacy Proto 格式发送——Trade 插件不知道自己运行在 Legacy 模式下。这就是管线"插件平权"的实际意义。

### 2.4 LegacyAdapter 接管 Legacy 出站

`LegacyTradeProtocolAdapter` 实现 `IClientOutgoingCommandHandler`，Priority 高于 Trade 自己的 handler：

```csharp
// LegacyTradeProtocolAdapter 新增出站命令管线实现
internal sealed class LegacyTradeProtocolAdapter : IClientOutgoingCommandHandler
{
    int ICommandHandler.Priority => 10; // 高于 Trade handler (50)，优先拦截

    public bool CanHandleOutgoingCommand(FrameworkPacket command)
    {
        // 仅在 Legacy 模式下拦截 Trade 命令
        return compatibilityMode == FrameworkCompatibilityMode.Legacy
            && command?.MessageType?.StartsWith("Phinix.Trade.") == true;
    }

    public ClientOutgoingCommandResult HandleOutgoingCommand(
        FrameworkPacket command, ClientFrameworkContext context)
    {
        // 翻译 FrameworkPacket → Legacy Proto → ILegacyModuleTransport.Send("Trading")
        // 返回 Handled（不再传递给后续 handler）
        SendLegacyPacket(command);
        return new ClientOutgoingCommandResult
        {
            Action = MessageHandlingResultAction.Handled
            // Command 为 null = 已通过 legacy transport 发送，框架不再发送 FrameworkPacket
        };
    }

    private void SendLegacyPacket(FrameworkPacket packet)
    {
        // ... 现有 SendCreateTrade / SendUpdateItems / SendUpdateStatus 逻辑，
        //     从 LegacyTradeProtocolAdapter 现有出站方法迁移过来 ...
        //     注意：SendUpdateItems 必须填充 Items 字段！
    }
}
```

### 2.5 调用方收敛到管线

`FrameworkClientTradeServiceAdapter` 的出站方法不再自己判断路由，统一走管线：

```csharp
public void UpdateTradeItems(string tradeId, IEnumerable<TradeItemSnapshot> items, string token = "")
{
    var packet = tradeService.CreateOfferUpdateRequest(tradeId, items, createContext());
    // 统一经出站命令管线，无论是 V2 还是 Legacy
    if (!frameworkClient.TryHandleOutgoingCommand(packet))
    {
        log?.Invoke($"[TradeAdapter] No handler for outgoing command {packet.MessageType}", LogLevel.WARNING);
    }
}
```

### 2.6 管线图

```
修复前（旁路）:
  Trade UI → tradeFacade.UpdateTradeItems()
    ├─ V2:    frameworkClient.SendFrameworkPacket() → NetClient → server  ❌ 绕过管线
    └─ Legacy: legacyTransport.Send("Trading") → NetClient → server       ❌ 绕过管线

修复后（通过管线）:
  Trade UI → tradeFacade.UpdateTradeItems()
    → frameworkClient.TryHandleOutgoingCommand(packet)
      ├─ V2 模式:
      │   TradeOutgoingCommandHandler (P=50) → return FrameworkPacket
      │   → PhinixFrameworkClient.sendPacket() → NetClient → server ✅
      │   (LegacyAdapter handler 不匹配 → 跳过)
      │
      └─ Legacy 模式:
          LegacyTradeProtocolAdapter (P=10) 抢先拦截
            → TranslateToLegacyProto() → legacyTransport.Send("Trading")
            → return Handled (Command=null, 框架不再 sendPacket) ✅
          Trade handler (P=50) 不再被调用
```

---

## 3. 修改文件清单

| # | 文件 | 改动 | 说明 |
|---|------|------|------|
| 1 | `FrameworkTypes.cs` | **新增** | `IClientOutgoingCommandHandler` 接口 + `ClientOutgoingCommandResult` 类 |
| 2 | `PhinixFrameworkClient.cs` | **新增** | `TryHandleOutgoingCommand()` 方法 |
| 3 | `BuiltInTradeClientExtension.cs` | **新增** | `TradeOutgoingCommandHandler`（实现 `IClientOutgoingCommandHandler`） |
| 4 | `LegacyTradeProtocolAdapter.cs` | **修改** | 实现 `IClientOutgoingCommandHandler`；补全 Items 填充逻辑；新增 `ConvertToProtoThing()` |
| 5 | `FrameworkClientTradeServiceAdapter.cs` | **修改** | 删除内联 `SendLegacyPacket()`（~50行）；出站改为调用 `TryHandleOutgoingCommand()` |
| 6 | `BuiltInLegacyAdapterClientExtension.cs` | **修改** | 注入 `CompatibilityMode` 到 `LegacyTradeProtocolAdapter`（现有构造函数不含此参数） |

---

## 4. 设计哲学合规评估

| 条款 | 状态 | 说明 |
|------|:--:|------|
| §3.7 管线传播 | ✅ | 出站命令管线新建完成，所有出站命令经 `TryHandleOutgoingCommand` 分发 |
| §1.1 插件平权 | ✅ | Trade 和 LegacyAdapter 通过同一管线按 Priority 竞争，不互相依赖 |
| §1.2 host 不依赖插件 | ✅ | `PhinixFrameworkClient` 只依赖 `IClientOutgoingCommandHandler`（通用契约），不引用插件 |
| §1.3 host 只做通用服务 | ✅ | 新方法属于管线基础设施，与 `TryHandleOutgoingMessage` 对称 |
| §3.3 插件间交互 | ✅ | Trade 不需要引用 LegacyAdapter——LegacyAdapter 通过 Priority 自动拦截 |
| §6 增量更新 | ✅ | 不修改 `IClientCommandHandler`、`IExtensionBuilder`；仅新增接口和方法 |

---

## 5. 管线迁移路线图

| 阶段 | 内容 | 预期效果 |
|------|------|------|
| **本 PR** | 新建 `IClientOutgoingCommandHandler` + `TryHandleOutgoingCommand`；Trade/LegacyAdapter 接入；修正 Items bug | 出站命令管线建成，Legacy 交易物品可见 |
| **后续** | 排查 V2 模式 `SendFrameworkPacket` 的其他调用方，逐步迁移到 `TryHandleOutgoingCommand` | 消除全部旁路 |
| **目标态** | 删除 `IFrameworkClientTransport.SendFrameworkPacket` 的公开暴露（或标记 `[Obsolete]`），强制所有出站走管线 | 完全符合 §3.7 |

每个阶段保持向后兼容。
