# 框架 Protobuf 协议设计

> 三流模型（message / command / item）的设计基础。Schema 定义见 [框架Protobuf协议Schema定义.md](框架Protobuf协议Schema定义.md)。

---

## 1. 核心方向

框架遵循 **开放/封闭原则**——对扩展开放，对修改封闭。新功能通过实现框架扩展点接入，不应修改框架核心。

三条一流管道取代旧有的业务域协议根：

| 管道 | 标识 | 职责 |
|------|------|------|
| `message` | `KindMessage = "message"` | 用户可见的展示内容 |
| `command` | `KindCommand = "command"` | 请求/响应、状态同步、控制指令 |
| `item` | `KindItem = "item"` | 可传输物品载荷（编解码器模型） |

旧架构：`Chat 协议根 → Trade 协议根 → User/Auth 协议根`
新架构：`框架流协议 → Chat(Trade等)作为消费者实现于其上`

---

## 2. 三条管道详细职责

### 2.1 Message 管道 — 展示流

- 用户可见通信（聊天消息、系统通知）
- 唯一应经过 `IMessageRenderer` 的管道
- 进出站管线均已就绪：`IClientMessageHandler` + `TryHandleOutgoingMessage`

### 2.2 Command 管道 — 控制流

- 客户端请求、服务端响应、状态同步、内部事件
- 不经过 Renderer，Command payloads 不是展示内容
- 入站：`IClientCommandHandler`、服务端 interceptor→handler→observer 五段链
- 出站：`IClientOutgoingCommandHandler` + `TryHandleOutgoingCommand`（已完整实现）

### 2.3 Item 管道 — 物品载荷流

- 物品编码/解码，不由 Trade 独占
- 采用 codec 模型：`codec_id` + payload bytes
- 内置 `FrameworkVanillaItemData`：def_name、stack_count、stuff_def_name、quality、hit_points、inner_item
- `IItemCodec` 接口已就绪，`IExtensionBuilder.AddItemCodec()` 已可用
- 客户端 `TradeClientItemPipeline` 已支持 `extensionCodecs` 参数注入
- 服务端 `ProcessIncomingItem` 已升级为三阶段链（interceptor → handler → observer + 内置 codec 兜底）

---

## 3. 当前实现状态

| 项目 | 状态 |
|------|------|
| 三流常量定义 | ✅ `FrameworkProtocol.KindMessage/KindCommand/KindItem` |
| Message 管线 (Client) | ✅ 进出站完整 |
| Message 管线 (Server) | ✅ 五段 interceptor→handler→observer 链 |
| Command 管线 (Client 入站) | ✅ `IClientCommandHandler` |
| Command 管线 (Client 出站) | ✅ `IClientOutgoingCommandHandler` + `TryHandleOutgoingCommand` |
| Command 管线 (Server) | ✅ 五段链 |
| Item 管线 codec 注册 | ✅ `IExtensionBuilder.AddItemCodec()` + `IItemCodec` |
| Item 管线 (Client 入站) | ✅ `packetHandler` `KindItem` 分支 + `handleItem` + `IClientIncomingItemHandler` |
| Item 管线 (Client 出站) | ✅ `TryHandleOutgoingItem` + `IClientOutgoingItemHandler` |
| Item 管线 (Server) | ✅ 三段链 `IServerInboundItemInterceptor` → `IServerDefaultItemHandler` → `IServerItemObserver` + 内置 codec 兜底 |
| Trade 消费 Submod codec | ✅ `IItemCodecProvider` + `SetExtensionCodecs` Activate 阶段注入 |
| Proto 骨架 | ✅ `Common/Utils/Framework/Proto/` 包含 Shared/Message/Command/Item |
| JSON→Protobuf 迁移 | ⬜ 远期目标 |

---

## 4. 适配器策略

- Legacy 适配器（`Extensions/LegacyAdapter/`）通过 `ILegacyModuleTransport` + `IDisplayMessageSink` 接入
- 适配器是迁移工具，框架核心不吸收 legacy 分支为永久设计约束
- 删除适配器只能失去 legacy 兼容性，不能破坏框架架构

---

## 5. 关联文档

- [设计哲学.md](设计哲学.md) — §3.2 通信管道三分类
- [框架Protobuf协议Schema定义.md](框架Protobuf协议Schema定义.md)
- [Phase6-Core级宿主与动态扩展架构.md](Phase6-Core级宿主与动态扩展架构.md)
- [Legacy适配器路线图.md](Legacy适配器路线图.md)
