# 实现：Legacy Trade 出站管线修复

## 背景

Phinix-Rework 项目中，Legacy 模式下交易存在 bug：物品不可见（双方上传的物品在意向清单中看不见）。

根因分两层：
1. **直接原因**：`FrameworkClientTradeServiceAdapter.SendLegacyPacket()` 的 `OfferUpdateRequestType` 分支构造 `UpdateTradeItemsPacket` 时遗漏了 `Items` 字段（payload.Items 被丢弃），服务端收到空列表后执行 `TryClearItemsOnOffer()` 清空了物品
2. **架构原因**：`IClientCommandHandler` 只有入站没有出站，`SendFrameworkPacket()` 直连 `NetClient` 绕过所有 handler，Trade 插件被迫内联手写 Legacy Proto 翻译

## 必须先阅读的文档

1. **`docs/设计哲学.md`** — 架构基准，所有改动必须遵守。重点看：
   - §1.1 插件平权
   - §3.7 插件不得绕过通信管线直连底层传输
   - §3.8 日志与可观测性标准
   - §4.5 Common 源文件共享模式
   - §5.3 版本化与 API 兼容
   - §6 渐进式迁移原则（含 Host/Core 增量更新规则）
   - §7 提交与审查 check-list

2. **`docs/legacy-trade-outbound-fix.md`** — 本次修复的完整方案文档，包含管线设计、接口定义、修改清单、合规评估

## 你的任务

按照 `docs/legacy-trade-outbound-fix.md` 中的方案实现代码修改。核心原则：

### 不可做的事
- **不修改** `IClientCommandHandler` — 这是入站接口，出站与它正交
- **不修改** `IExtensionBuilder` — 不新增注册方法，通过运行时 `is` 检测
- **不修改** `PhinixExtensionRegistry` — 不改发现逻辑
- **不删除** 任何现有公开接口或方法签名 — 违反 §6 增量更新规则
- **不在** `FrameworkClientTradeServiceAdapter` 里保留内联的 Legacy Proto 翻译 — 那是旁路

### 要做的事

1. **`Common/Utils/Framework/FrameworkTypes.cs`** — 新增 `IClientOutgoingCommandHandler` 接口（继承 `ICommandHandler`）和 `ClientOutgoingCommandResult` 类。参照已有的 `IClientCommandHandler` 和 `ClientIncomingCommandResult` 的模式

2. **`Client/Source/Framework/PhinixFrameworkClient.cs`** — 新增 `TryHandleOutgoingCommand(FrameworkPacket command)` 方法。参照已有的 `TryHandleOutgoingMessage()`（第 126-183 行）的模式：遍历 `discoveredExtensions.Extensions`，`is IClientOutgoingCommandHandler` 筛选，按 Priority 排序，try-catch 隔离

3. **`Extensions/Trade/Client/BuiltInTradeClientExtension.cs`** — 新增一个内部类实现 `IClientOutgoingCommandHandler`：仅处理 `MessageType.StartsWith("Phinix.Trade.")` 的命令，原样返回 FrameworkPacket 供框架发送。Priority 设为一个中间值如 50

4. **`Extensions/LegacyAdapter/Client/LegacyTradeProtocolAdapter.cs`** — 实现 `IClientOutgoingCommandHandler`：
   - Priority 低于 Trade handler（如 10），在 Legacy 模式下抢在 Trade handler 前面拦截
   - `CanHandleOutgoingCommand`：仅在 `CompatibilityMode == Legacy` 时拦截 Trade 命令
   - `HandleOutgoingCommand`：翻译 FrameworkPacket → Legacy Proto → `ILegacyModuleTransport.Send("Trading")`，返回 `Handled` 且 `Command=null`（框架不再发 FrameworkPacket）
   - **关键**：`SendUpdateItems` 时把 `payload.Items`（`List<FrameworkItemPayload>`）转换为 `Trading.ProtoThing` 并填入 `UpdateTradeItemsPacket.Items`
   - 需新增构造函数参数 `CompatibilityMode` 或 `IFrameworkClientLifecycle`
   - 所有异常必须 try-catch 隔离，日志通过 `ILoggable` 上报

5. **`Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs`** — 出站方法改为调用 `TryHandleOutgoingCommand()`：
   - `UpdateTradeItems`、`UpdateTradeStatus`、`CreateTrade`、`CancelTrade` 的 else 分支（Legacy 模式）统一改为 `frameworkClient.TryHandleOutgoingCommand(packet)`
   - 删除内联的 `SendLegacyPacket()` 方法（约 50 行）
   - 删除 `PackProtobuf()` helper
   - 删除 `ILegacyModuleTransport legacyTransport` 字段（不再需要直连）
   - 删除 `using Phinix.LegacyAdapter.Client` 引用

6. **`Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs`** — 注入 `lifecycle` 到 `LegacyTradeProtocolAdapter` 构造函数

### 实现注意
- `SendLegacyPacket` 中的 `ProtobufPacketHelper.Pack()` **不用** `Any.Pack()`（前者带 `"Phinix"` 前缀），跟 `LegacyTradeProtocolAdapter` 现有的出站方法保持一致
- 框架中的 `sendPacket()` 已经设置 `KindCommand`、`SessionId`、`SenderUuid`，handler 返回的 command 不需要设
- `LegacyTradeProtocolAdapter` 是 `internal sealed`，不需要暴露为公开 API
- Priority 值：LegacyAdapter=10, Trade handler=50。数字越小越优先（跟 `ICommandHandler.Priority` 语义一致）

### 验证方式
- 项目在 Visual Studio 或 `dotnet build` 下编译通过
- 对照 `docs/设计哲学.md` §7 的 check-list 逐项检查
- 确认 `IClientCommandHandler`、`IExtensionBuilder`、`PhinixExtensionRegistry` 未被修改
- 确认 `FrameworkClientTradeServiceAdapter` 中 `SendLegacyPacket` 和 `PackProtobuf` 已删除
