# Legacy Adapter 插件架构方案

> 2026-05-30，基于 Phinix-Rework Phase 5 架构 与 原 Phinix 协议差异分析。

---

## 1. 问题分析

### 1.1 核心矛盾

原 Phinix 与 Phinix-Rework 使用的是**两套完全不兼容的通信协议**：

| 维度 | 原 Phinix | Phinix-Rework |
|------|-----------|---------------|
| 路由方式 | `NetClient.Send(moduleName, bytes)` 模块名分发 | `PhinixFrameworkClient.SendFrameworkPacket(FrameworkPacket)` 统一框架路由 |
| 序列化 | Protobuf `Any.Pack()` → 模块名是 namespace（`"Chat"`, `"Trading"`） | `FrameworkSerialization.SerializePacket()` → 模块名固定 `"PhinixFramework"` |
| 聊天消息 | `ChatMessagePacket` / `ChatHistoryPacket` | `BuiltInChatMessagePayload` 包在 `FrameworkPacket` 里 |
| 交易消息 | `CreateTradePacket` / `CompleteTradePacket` / `SyncTradesPacket` 等 | `FrameworkTradeCreateRequest` / `FrameworkTradeStateSnapshot` 等 JSON 序列化 |
| 能力协商 | 无 | `KindHello` → `KindCapabilities` 握手 |
| 兼容模式 | N/A | `FrameworkCompatibilityMode.Legacy` 占位但**无实际处理** |

当前 Rework 的 `PhinixFrameworkClient` 已有一个"兼容模式检测"机制：连接服务器后发送 Hello，3 秒超时未收到 Capabilities 回包则进入 `Legacy` 模式。但是 Chat/Trade 的 handler 在 Legacy 模式下**只显示"此扩展不可用"的系统消息**，不产生任何实际协议回退。

### 1.2 当前协议的精确差异

**Chat 出站（客户端发送）：**
- 原版：构造 `ChatMessagePacket`（sessionId, uuid, messageId, message） → `ProtobufPacketHelper.Pack()` → `netClient.Send("Chat", bytes)`
- Rework：`chatApi.CreateOutgoingMessage()` → `BuiltInChatMessagePayload`（proto3）→ `FrameworkPacket`（Flow=Message, MessageType="builtin.chat.message"）→ `frameworkClient.SendFrameworkPacket()`

**Chat 入站（客户端接收）：**
- 原版：`netClient` 回调（module="Chat"）→ `ProtobufPacketHelper.ValidatePacket()` → `ChatMessagePacket` / `ChatHistoryPacket` / `ChatMessageResponsePacket` → `ClientChatMessage` → `OnChatMessageReceived`
- Rework：`netClient` 回调（module="PhinixFramework"）→ `FrameworkSerialization.DeserializePacket()` → 走 `handleMessage()` → `IClientMessageHandler.CanHandleIncomingMessage()` 匹配 `MessageType == "builtin.chat.message"` → `chatApi.RenderMessage()` → `FrameworkDisplayMessage`

**Trade 出站：**
- 原版：`CreateTradePacket` / `UpdateTradeItemsPacket` / `UpdateTradeStatusPacket` → `netClient.Send("Trading", bytes)`
- Rework：`FrameworkTradeCreateRequest`（JSON）包在 `FrameworkPacket`（Flow=Command, MessageType="trade.create.request"）→ `frameworkClient.SendFrameworkPacket()`

**Trade 入站：**
- 原版：`netClient` 回调（module="Trading"）→ 6 种 packet 类型 switch → 各种 `EventArgs`
- Rework：`netClient` 回调（module="PhinixFramework"）→ `handleCommand()` → `IClientCommandHandler.CanHandleIncomingCommand()` 匹配 6 种 `FrameworkTradeProtocol.*` → 对应的 `tradeApi.Handle*()`

---

## 2. 方案总览：Legacy Adapter 插件

### 2.1 核心思路

遵循设计哲学 **插件平权**（§1.1）和 **host 不依赖插件**（§1.2），创建 `PhinixLegacyAdapter` 插件：

- **它自己就是一个标准 `IPhinixExtensionModule`**，和 Chat/Trade 地位完全相同
- 当框架检测到服务器是 Legacy 模式时，Adapter 劫持（intercept）Chat 和 Trade 的通信，做协议翻译
- 当服务器是 FrameworkV2 模式时，Adapter 完全透明，消息照常交给 BuiltInChat/BuiltInTrade 处理
- **不修改** BuiltInChat 和 BuiltInTrade 的任何代码

### 2.2 劫持机制

Adapter 通过 **Priority 排序** 实现劫持：

```
优先级链（数字越小越先执行）：

  Priority 500  → LegacyAdapter（拦截 Legacy，Continue 给后续）
  Priority 1000 → BuiltInChat（处理 FrameworkV2 消息）
  Priority 1100 → BuiltInTrade（处理 FrameworkV2 命令）
```

当 CompatibilityMode == FrameworkV2：
- Adapter 的 `CanHandle*` 返回 false，所有流量正常流入 Chat/Trade

当 CompatibilityMode == Legacy：
- Adapter 的 `CanHandle*` 返回 true，拦截并翻译协议
- Adapter 返回 `Action = Handled`，Chat/Trade 的 handler 不会被调用

---

## 3. 需要修改 Host/Core 的地方

### 3.1 新增 `ILegacyModuleTransport` 接口

**位置：** `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs`（或新文件）

```csharp
namespace PhinixClient.Framework
{
    /// <summary>
    /// 给需要直接操作 NetClient 原始模块通信的插件使用。
    /// 主要用于 Legacy 协议适配 —— Legacy 协议不经过 Framework 统一路由，
    /// 而是直接用 NetClient 的 module-based 分发。
    /// </summary>
    public interface ILegacyModuleTransport
    {
        /// <summary>向指定模块发送原始字节（对应原版 NetClient.Send）</summary>
        void Send(string moduleName, byte[] data);

        /// <summary>注册原始模块的数据包处理器（对应原版 RegisterPacketHandler）</summary>
        void RegisterHandler(string moduleName, Action<string, string, byte[]> handler);

        /// <summary>取消注册</summary>
        void UnregisterHandler(string moduleName);
    }
}
```

### 3.2 在 Host 中注册 `ILegacyModuleTransport`

**位置：** `Client/Source/Client.cs`，在 `ExtensionHostContext` 初始化处

```csharp
// 在构造 extensionHostContext 之后、new PhinixFrameworkClient 之前
extensionHostContext.AddService<ILegacyModuleTransport>(
    new NetClientLegacyTransportAdapter(netClient));
```

`NetClientLegacyTransportAdapter` 是一个简单的包装类：

```csharp
internal sealed class NetClientLegacyTransportAdapter : ILegacyModuleTransport
{
    private readonly NetClient netClient;

    public NetClientLegacyTransportAdapter(NetClient netClient)
    {
        this.netClient = netClient;
    }

    public void Send(string moduleName, byte[] data)
        => netClient.Send(moduleName, data);

    public void RegisterHandler(string moduleName, Action<string, string, byte[]> handler)
        => netClient.RegisterPacketHandler(moduleName, handler);

    public void UnregisterHandler(string moduleName)
        => netClient.UnregisterPacketHandler(moduleName);
}
```

注意：需要在 `NetClient` 上新增 `UnregisterPacketHandler` 方法。

### 3.3 为什么只需要改这些

| 需要的集成点 | 当前状态 | 是否已满足 |
|-------------|---------|-----------|
| 知道当前 CompatibilityMode | `IFrameworkClientLifecycle.CompatibilityMode` + `CompatibilityModeChanged` 事件 | ✅ 已满足 |
| 注册 IClientMessageHandler（劫持出站）| `IExtensionBuilder.AddClientMessageHandler()` | ✅ 已满足 |
| 注册 IClientCommandHandler（劫持交易命令）| `IExtensionBuilder.AddClientCommandHandler()` | ✅ 已满足 |
| 产生 FrameworkDisplayMessage（入站消息）| `IClientDisplayMessageFeed` + `IClientDisplayMessageStore` | ✅ 已满足 |
| 获取 session/user 上下文 | `IClientSessionContext`, `IClientUserDirectory` | ✅ 已满足 |
| **发送原始协议包到 NetClient** | ❌ 缺失 — `NetClient` 是 private | **需新增 `ILegacyModuleTransport`** |
| **接收原始协议包从 NetClient** | ❌ 缺失 — `RegisterPacketHandler` 不可达 | **需新增 `ILegacyModuleTransport`** |

所以 Host 改动是：1 个新接口 + 1 个适配器类 + 1 行注册 + NetClient 加 1 个 Unregister 方法。非常干净。

---

## 4. Adapter 插件设计

### 4.1 目录结构

```
Extensions/
  LegacyAdapter/
    Client/
      PhinixLegacyAdapter.Client.csproj
      BuiltInLegacyAdapterClientExtension.cs   ← IPhinixExtensionModule 入口
      LegacyChatProtocolAdapter.cs              ← Chat 协议翻译器
      LegacyTradeProtocolAdapter.cs             ← Trade 协议翻译器
    Contracts/
      LegacyAdapterContracts.cs                 ← 如有需要暴露的 API
```

### 4.2 入口类：`BuiltInLegacyAdapterClientExtension`

```
[PhinixExtension("builtin.legacy-adapter")]
class BuiltInLegacyAdapterClientExtension :
    IPhinixExtensionModule,
    IActivatablePhinixExtensionModule,
    IClientMessageHandler,    // 劫持 chat 消息的出入站
    IClientCommandHandler     // 劫持 trade 命令的出入站
{
    Priority = 500;  // 高于 Chat(1000) 和 Trade(1100)

    Register(builder):
        - 获取 ILegacyModuleTransport
        - 获取 IFrameworkClientLifecycle
        - 获取 IClientDisplayMessageStore
        - 获取 IClientUserDirectory
        - 创建 LegacyChatProtocolAdapter
        - 创建 LegacyTradeProtocolAdapter
        - 注册自己为 IClientMessageHandler（优先级500）
        - 注册自己为 IClientCommandHandler（优先级500）

    Activate(hostContext):
        - 监听 CompatibilityModeChanged
        - 当模式变为 Legacy 时，注册 legacy 模块处理器
        - 当模式变为 FrameworkV2 时，注销 legacy 模块处理器

    Shutdown(hostContext):
        - 注销所有 legacy 模块处理器
        - 取消事件订阅
}
```

### 4.3 Chat 协议翻译：`LegacyChatProtocolAdapter`

**出站（用户输入 → 服务器）：**

```
用户输入 "hello"
  → Adapter.CanHandleOutgoingText("hello") → true（仅 Legacy 模式）
  → Adapter.HandleOutgoingText("hello", context)
  → 构造 ChatMessagePacket {
        SessionId = context.SessionId,
        Uuid = context.SenderUuid,
        MessageId = Guid.NewGuid().ToString(),
        Message = "hello"
    }
  → ProtobufPacketHelper.Pack(packet).ToByteArray()
  → legacyTransport.Send("Chat", bytes)
  → 返回 MessageHandlingResultAction.Handled
```

**入站（服务器 → 用户显示）：**

```
legacyTransport.RegisterHandler("Chat", legacyPacketHandler)
  → 收到 ChatMessagePacket
  → 解析 ChatMessage 字段
  → 构造 FrameworkDisplayMessage {
        MessageId = packet.MessageId,
        SenderUuid = packet.Uuid,
        TimestampUtcTicks = packet.Timestamp,
        Source = "builtin_chat",  // 让 chat 的 ToUiMessage 能识别
        Text = packet.Message
    }
  → 调 displayMessageStore 的方法或直接触发 OnDisplayMessageReceived
```

这里有**两种入站路由策略**：

**策略 A：通过 DisplayMessageFeed 注入**
- Adapter 拿到 `IClientDisplayMessageFeed`（即 `PhinixFrameworkClient`）
- 直接调用某种方式把 `FrameworkDisplayMessage` 注入到消息队列
- 问题：`PhinixFrameworkClient` 没有公开的 `AddDisplayMessage` 方法 — `addDisplayMessage` 是 private

**策略 B（推荐）：Adapter 也有自己的 DisplayMessageStore 桥接**
- Adapter 不依赖 `PhinixFrameworkClient` 的内部方法
- 而是直接把 legacy 消息转换为 `FrameworkDisplayMessage`，然后通过 `IClientDisplayMessageStore` 的接口... 等等，让我再看看。

实际上 `IClientDisplayMessageStore` 只有读接口（`GetDisplayMessages`, `GetUnreadDisplayMessages`, `MarkAsRead`），没有写接口。写操作是通过 `IClientDisplayMessageFeed.DisplayMessageReceived` 事件和 `PhinixFrameworkClient` 的 private `addDisplayMessage` 方法。

所以需要在 `ClientExtensionAbstractions` 中新增一个小的写入接口，或者让 Adapter 通过其他方式注入消息。

### 4.4 需要新增的接口

**`IDisplayMessageSink`（可选方案）：**

```csharp
public interface IDisplayMessageSink
{
    void EnqueueDisplayMessage(FrameworkDisplayMessage message);
}
```

`PhinixFrameworkClient` 实现此接口，并在 `ExtensionHostContext` 中注册。这样 Adapter 就可以把从 legacy 协议翻译过来的消息注入到统一的消息显示管道中。

或者**更简单的方案**：让 Adapter 直接触发 `IClientDisplayMessageFeed.DisplayMessageReceived` 事件。但事件源是 `PhinixFrameworkClient`... 

另一个简洁替代：**让 Adapter 持有一个 `IClientChatService` 的等价实现**，把 legacy 消息直接暴露为 chat 消息。但这又绕回去了。

**最佳方案：新增 `IDisplayMessageSink`**

这是最小侵入的改动 — 只加一个接口和一行实现：

```csharp
// 在 IClientExtensionAbstractions 中
public interface IDisplayMessageSink
{
    void Enqueue(FrameworkDisplayMessage message);
}

// PhinixFrameworkClient 实现
public void Enqueue(FrameworkDisplayMessage message)
{
    addDisplayMessage(message);  // 已有 private 方法，改为 public 即可
}

// 在 Client.cs 注册
extensionHostContext.AddService<IDisplayMessageSink>(frameworkClient);
```

### 4.5 Trade 协议翻译：`LegacyTradeProtocolAdapter`

Trade 比 Chat 复杂，有 6 种命令类型：

**出站：**
| 用户操作 | Legacy 协议包 | FrameworkV2 等价 |
|---------|---------------|-------------------|
| CreateTrade(uuid) | `CreateTradePacket` → `netClient.Send("Trading", ...)` | `FrameworkTradeCreateRequest` JSON |
| UpdateTradeItems(tradeId, items) | `UpdateTradeItemsPacket` | `FrameworkTradeOfferUpdateRequest` |
| UpdateTradeStatus(tradeId, accepted, cancelled) | `UpdateTradeStatusPacket` | `FrameworkTradeStatusUpdateRequest` |
| CancelTrade(tradeId) | `UpdateTradeStatusPacket{Cancelled=true}` | `FrameworkTradeStatusUpdateRequest{Cancelled=true}` |

**入站：**
| Legacy 协议包 | 操作 |
|--------------|------|
| `CreateTradeResponsePacket` | → 触发 trade created 事件 + 开 trade window |
| `CompleteTradePacket` | → 触发 completed/cancelled 事件 + drop pods |
| `UpdateTradeItemsPacket` | → 更新 trade 物品 + 触发 trade update |
| `UpdateTradeStatusPacket` | → 更新 accept 状态 + 触发 trade update |
| `SyncTradesPacket` | → 全量同步 trades 列表 |

Trade 的入站处理需要维护**本地 trade 状态**（类似原版 `ClientTrading` 中的 `activeTrades` 字典），并在状态变更时触发对应的 UI 事件。

这意味着 Adapter 内部需要维护一个简化的 trade 状态机，把 legacy 协议的事件映射为 Rework trade 插件能理解的事件。

但这里有个**架构问题**：Rework 的 Trade 插件通过 `IClientTradeService` / `ITradeRequestApi` 暴露 API，通过事件通知 UI。Adapter 如果要触发 Trade UI 更新，需要通过 Trade 插件的 API 来注入事件。如果 Adapter 直接操作 Trade 内部状态，就违反了插件平权原则。

**更解耦的方案：Adapter 只做协议翻译，Trade 事件通过现有 API 注入**

即 Adapter 把 legacy trade 包翻译成 `FrameworkTradeStateSnapshot` 等格式，然后通过 `ITradeRequestApi` 或直接调用 `IFrameworkTradeClientApi.HandleSnapshot()` 等方法注入。

但这要求 Adapter 引用 Trade 的 Contracts 程序集。根据设计哲学 §3.3：
> 插件之间的协作由插件自行负责

所以 Adapter 引用 Trade.Contracts 是**完全合法**的。

### 4.6 更新后的目录和引用关系

```
Extensions/
  LegacyAdapter/
    Client/
      PhinixLegacyAdapter.Client.csproj
        → ClientExtensionAbstractions  (通用契约)
        → ChatExtension.Contracts      (Chat 领域契约，用于消息格式)
        → TradeExtension.Contracts     (Trade 领域契约，用于交易 API)
        → Common.Utils                 (FrameworkPacket, Protobuf 等)
      BuiltInLegacyAdapterClientExtension.cs
      LegacyChatProtocolAdapter.cs
      LegacyTradeProtocolAdapter.cs
```

引用方向符合设计哲学：
- `Client.csproj` **不引用** LegacyAdapter
- Adapter 通过 `IExtensionBuilder` 注册自己的能力
- Adapter 引用 Chat/Trade Contracts 是插件间协作的正常行为

---

## 5. Host/Core 改动清单

### 5.1 `ClientExtensionAbstractions` 新增接口

**文件：** `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs`

1. `ILegacyModuleTransport` — 3 个方法
2. `IDisplayMessageSink` — 1 个方法 `Enqueue(FrameworkDisplayMessage)`

### 5.2 `Common/Connections/NetClient.cs` 新增方法

```csharp
public void UnregisterPacketHandler(string module)
{
    // 从 packetHandlers 字典中移除指定 module 的 handler
}
```

### 5.3 `Client/Source/Client.cs` 注册新服务

- 实例化 `NetClientLegacyTransportAdapter` 并注册为 `ILegacyModuleTransport`
- frameworkClient 实例化后，注册为 `IDisplayMessageSink`

### 5.4 `Client/Source/Framework/PhinixFrameworkClient.cs`

- 实现 `IDisplayMessageSink` 接口（将 private `addDisplayMessage` 改为 public `Enqueue`）

### 5.5 DLL 加载顺序

```
...
07-ClientExtensionAbstractions.dll    ← 新增接口
08-ChatExtension.dll                  ← Chat 领域契约（不变）
09-TradeExtension.dll                 ← Trade 领域契约（不变）
10-LegacyAdapter.Client.dll           ← 新增
11-ChatExtension.Client.dll
12-TradeExtension.Client.dll
```

*注意：LegacyAdapter 需要比 Chat/Trade 的 Client 实现更早加载，因为它引用它们的 Contracts。在字符串序中 "LegacyAdapter" < "ChatExtension.Client" 和 "TradeExtension.Client"，所以放在 10 号位。*

---

## 6. 工作分解

### Phase 1: Host/Core 改动（估计 1-2h）
- [ ] 在 `ClientExtensionAbstractions` 中新增 `ILegacyModuleTransport` 接口
- [ ] 在 `ClientExtensionAbstractions` 中新增 `IDisplayMessageSink` 接口  
- [ ] 在 `NetClient` 中新增 `UnregisterPacketHandler` 方法
- [ ] 在 `Client.cs` 中创建 `NetClientLegacyTransportAdapter` 并注册服务
- [ ] `PhinixFrameworkClient` 实现 `IDisplayMessageSink`
- [ ] 编译验证

### Phase 2: LegacyAdapter 插件骨架（估计 1h）
- [ ] 创建项目结构（csproj、目录）
- [ ] 实现 `BuiltInLegacyAdapterClientExtension` 基本骨架（Register/Activate/Shutdown）
- [ ] 验证插件被发现和加载

### Phase 3: Chat 协议翻译（估计 2-3h）
- [ ] 实现 `LegacyChatProtocolAdapter`
  - 出站：`ChatMessagePacket` 构造 + 发送
  - 入站：legacy handler 注册 + `ChatMessagePacket` → `FrameworkDisplayMessage`
  - 入站：`ChatHistoryPacket` → 批量 `FrameworkDisplayMessage`
  - 入站：`ChatMessageResponsePacket` → 消息确认/拒绝
- [ ] 集成测试：连接 legacy 服务器，发送/接收聊天消息

### Phase 4: Trade 协议翻译（估计 3-4h）
- [ ] 实现 `LegacyTradeProtocolAdapter`
  - 本地 trade 状态机
  - 出站：4 种 trade 请求的协议转换
  - 入站：6 种 trade 响应的协议转换 + 事件注入
- [ ] 集成测试：创建交易、更新物品、接受/取消、完成

### Phase 5: 端到端测试（估计 2h）
- [ ] 连接原版 Phinix 服务器
- [ ] 聊天消息往返
- [ ] 交易创建→物品更新→接受→完成
- [ ] 断开重连→状态恢复
- [ ] 切回 FrameworkV2 服务器验证 Adapter 透明

---

## 7. 检查清单（对照设计哲学）

### 7.1 插件平权（§1.1）
- [x] LegacyAdapter 通过同样的 `IPhinixExtensionModule` 发现路径
- [x] 通过 `Register(builder)` → `builder.AddClientMessageHandler(this)` 注册
- [x] 通过 `Activate(hostContext)` → `Shutdown(hostContext)` 激活
- [x] 没有在 Client.cs 中为 LegacyAdapter 开专用分支

### 7.2 host 不依赖插件（§1.2）
- [x] `Client.csproj` 不引用 LegacyAdapter 工程
- [x] Host 通过 `ILegacyModuleTransport` 提供通用传输能力，不绑定具体协议
- [x] Host 新增的接口是通用的（legacy transport、display sink），不是插件专用的

### 7.3 host 只做通用服务（§1.3）
- [x] 新增的 `ILegacyModuleTransport` 是通用网络能力（"可以向任意模块发送原始数据"）
- [x] 新增的 `IDisplayMessageSink` 是通用消息管道（"可以向消息显示系统注入消息"）
- [x] 业务逻辑（Chat/Trade 协议翻译）全在插件里

### 7.4 错误隔离（§3.5）
- [x] Adapter 的协议解析失败不影响 FrameworkV2 管线
- [x] Adapter 的 handler 抛异常被 PipelineRunner 捕获，不中断整体管线

### 7.5 事件订阅清理
- [x] `Shutdown` 中取消所有 `CompatibilityModeChanged` 订阅
- [x] `Shutdown` 中通过 `ILegacyModuleTransport.UnregisterHandler` 清理所有 legacy handler

---

## 8. 风险和备选方案

### 8.1 风险：Protobuf 程序集版本冲突
原版和 Rework 可能引用不同版本的 Google.Protobuf。如果 LegacyAdapter 需要同时引用原版 protobuf 消息类型和 Rework 的 FrameworkPacket，需要注意程序集版本兼容性。

**缓解：** LegacyAdapter 只引用 Rework 的依赖，不直接引用原版程序集。Legacy 的 protobuf 消息类型可以由 Adapter 自己定义（copy 原版的 .proto 生成），或者通过 `Dependencies/protobuf/` 中已有的 proto 定义生成。

### 8.2 风险：`PhinixFrameworkClient.addDisplayMessage` 改为 public 的影响
这个方法改名/改可见性后，需要注意所有内部调用点。实际上可以直接保留 private 方法 `addDisplayMessage`，新增 public 的 `Enqueue` 方法转调用它。

### 8.3 备选：Adapter 不通过 DisplayMessageSink，而是直接通过 IClientMessageHandler pipeline
如果不想新增 `IDisplayMessageSink`，可以让 LegacyAdapter 收到的消息走一个迂回路径：在 `PhinixFrameworkClient` 中手动触发 `handleMessage()` 的调用。但这更 hack。

### 8.4 备选：Adapter 完整嵌入 Chat/Trade 插件中
一种看起来更"简单"的做法是在 `BuiltInChatClientExtension` 的 `HandleOutgoingText` 中加上 Legacy 分支。但这违反了关注点分离——Chat 插件不应该知道 Legacy 协议的存在。

---

## 9. 结论

**最小 Host 改动：2 个新接口 + 1 个适配器类 + 1 个 NetClient.Unregister 方法**

所有协议翻译逻辑自包含在 `LegacyAdapter` 插件中，符合 Phinix 设计哲学的每一条核心原则。Adaptor 可以被干净地删除而对其他功能零影响（只是失去与 legacy 服务器的互操作能力）。
