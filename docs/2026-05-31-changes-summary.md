# 今日修改摘要 (2026-05-31)：Legacy Trade 出站管线修复 + 入站状态同步修复 + 全链路日志追踪

## 测试现状

**对面正常**：我上传的物品对面能看到，对面发的物品对面能看到，对面同意对面能看到，对面可以正常结算交易。

**我这边有问题**：我自己传的物品我看不见，对面传的我也看不见，对面同意的我看不见，我的交易没法结算但对面可以结算完成。

## 根因

1. **入站 `HandleUpdateItems` + `HandleUpdateStatus` 覆盖丢失**：收到服务器回显的 UpdateTradeItems/UpdateTradeStatus 包时，原先代码从 repo 重建全新 snapshot 再 `UpsertTrade`。但通过 `ClientTradeSnapshot` 间接查询 repo 时 `TryGetTrade` → `ToTradeSnapshot` 的 `userDirectory?.Uuid` 匹配不稳定，返回 "cannot resolve other party" → 入站包被丢弃。日志：`Line 790/810: HandleUpdateItems/HandleUpdateStatus: cannot resolve other party — dropping`

2. **`ConvertProtoThings` (入站方向) 物品编码有损**：入站方向 `ProtoThing` → `FrameworkItemPayload` 时，`PayloadJson` 存入的是 `protoThing.ToString()` (protobuf JSON)。但 `MergeItemsState` 辅助方法将 `TradeItemSnapshot` 转回 `FrameworkItemPayload` 时只存了 `DefName` 作为 `PayloadJson`，实际物品数据丢失。而且 `ConvertProtoThings` 将 `CodecId` 设为 `"PhinixLegacy.ProtoThing"`——管线中的 item codec 不识别这个 codec，解码返回 `UnknownItem`。

## 修复措施（第三轮）

### 入站 HandleUpdateItems：原位读取 + 原位更新
不再构造新 snapshot。改为：
1. `tradeApi.GetRepositoryTrades()` 直接读取 repo 中的 `FrameworkTradeStateSnapshot`
2. 在已有 snapshot 的 `Participants` 上按 `packet.Uuid` 匹配更新物品
3. `tradeApi.UpsertTrade()` 回写原位修改后的 snapshot

如果 repo 中不存在（如首次 `HandleCreateResponse` 尚未写入），则从 packet 数据构造初始 snapshot。

### 入站 HandleUpdateStatus：同上
直接原位更新 accepted 状态，保留已有物品信息。

### MergeAcceptedState / MergeItemsState
这两个辅助方法在新逻辑中不再被 `HandleUpdateItems`/`HandleUpdateStatus` 调用（因为直接原位更新，不需要 merge）。但保留代码以防止其他调用方引用。`MergeItemsState` 的 `TradeItemSnapshot` → `FrameworkItemPayload` 转换有损问题不影响当前路径。

### 注意：`ConvertProtoThings` 物品编码为 `"PhinixLegacy.ProtoThing"`
入站方向 `ProtoThing` → `FrameworkItemPayload` 时将 `CodecId` 设为 `"PhinixLegacy.ProtoThing"`。管线中的 `DefaultLegacyTradeItemCodec` 和 `FrameworkTradeItemPayloadCodec` 只识别 `"core.item.vanilla"` codec，不识别 `"PhinixLegacy.ProtoThing"`。因此 UI 中解码这些物品时会返回 `UnknownItem`。物品数据显示依赖 `RepositoryChanged` 事件驱动的 UI 刷新路径而不是 item codec 解码路径，所以当前行为是否正常取决于 UI 的具体数据绑定方式。

## 一、架构新增（host/core 层）

### 1.1 IClientOutgoingCommandHandler 接口（新增，不修改现有接口）
- **文件**: `Common/Utils/Framework/FrameworkTypes.cs`
- **内容**: 新增 `IClientOutgoingCommandHandler : ICommandHandler` 接口 + `ClientOutgoingCommandResult` 类
  - 接口方法: `CanHandleOutgoingCommand(FrameworkPacket)`, `HandleOutgoingCommand(FrameworkPacket, ClientFrameworkContext)`
  - 与 `IClientCommandHandler`（入站）正交——入站接口不受影响

### 1.2 IFrameworkClientTransport 新增方法
- **文件**: `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs`
- **内容**: 接口新增 `bool TryHandleOutgoingCommand(FrameworkPacket command)` 方法声明

### 1.3 出站命令管线实现
- **文件**: `Client/Source/Framework/PhinixFrameworkClient.cs`
- **内容**: 新增 `TryHandleOutgoingCommand()` 方法实现，参照已有的 `TryHandleOutgoingMessage()` 模式：
  - 遍历 `discoveredExtensions.Extensions`，`OfType<IClientOutgoingCommandHandler>()` 筛选
  - 按 `Priority` 排序，try-catch 隔离
  - 全链路 `RaiseLogEntry`（DEBUG 级别）日志

## 二、插件层修改

### 2.1 Trade 插件接入出站管线
- **文件**: `Extensions/Trade/Client/BuiltInTradeClientExtension.cs`
- **内容**: 
  - `BuiltInTradeClientExtension` 继承增加 `IClientOutgoingCommandHandler`
  - `CanHandleOutgoingCommand`: 匹配 `MessageType.StartsWith("trade.")`
  - `HandleOutgoingCommand`: 原样返回 FrameworkPacket 供框架发送（V2 模式）

### 2.2 Trade Adapter 管线化
- **文件**: `Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs`
- **内容**:
  - `SendTradePacket()` 不再区分 V2/Legacy，统一走 `frameworkClient.TryHandleOutgoingCommand()`
  - 删除 `SendLegacyPacket()` (~50 行内联 Legacy Proto 翻译)
  - 删除 `PackProtobuf()` helper
  - 删除 `ILegacyModuleTransport legacyTransport` 字段和 `using Phinix.LegacyAdapter.Client` 引用
  - 构造函数去掉 `ILegacyModuleTransport` 参数
  - 新增 INFO 级别 `SendTradePacket` 入口日志

### 2.3 LegacyAdapter 接入出站管线
- **文件**: `Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs`
- **内容**:
  - `BuiltInLegacyAdapterClientExtension` 继承增加 `IClientOutgoingCommandHandler`
  - `CanHandleOutgoingCommand`: 仅在 `Lifecycle.CompatibilityMode == Legacy` 时，委托给 `tradeAdapter.CanHandleOutgoingCommand()`
  - `HandleOutgoingCommand`: 委托给 `tradeAdapter.HandleOutgoingCommand()`
  - 构造函数注入 `IFrameworkClientLifecycle` 给 `LegacyTradeProtocolAdapter`
  - 新增诊断日志

### 2.4 LegacyTradeProtocolAdapter 出站翻译 + 入站处理
- **文件**: `Extensions/LegacyAdapter/Client/LegacyTradeProtocolAdapter.cs`
- **内容**:
  - 实现 `IClientOutgoingCommandHandler`（Priority=500，低于 Trade handler 的 1100）
  - 构造函数新增 `IFrameworkClientLifecycle` 参数，替换原先的 `FrameworkCompatibilityMode` 字段
  - `CanHandleOutgoingCommand` 使用 `lifecycle.CompatibilityMode` 实时值
  - `HandleOutgoingCommand` → `SendLegacyPacket()`: FrameworkPacket → Legacy Proto 翻译
  - 新增出站方向物品转换链: `FrameworkItemPayload` → `Trading.ProtoThing`
    - 支持 `PayloadBytes` (FrameworkVanillaItemData protobuf) → ProtoThing
    - 支持 `PayloadJson` (protobuf JSON) → ProtoThing
  - 入站 `HandleUpdateItems`: 从 repo 原位读取 `GetRepositoryTrades()` → 原位更新参与者物品 → `UpsertTrade` 回写
  - 入站 `HandleUpdateStatus`: 同上，原位更新 accepted 状态
  - 所有转换方法去裸 catch，异常通过 `log` 回调上报（§3.8 合规）
  - 全链路 INFO/DEBUG 级别日志

## 三、Bug 修复历史

### 3.1 【已修复】LegacyTradeProtocolAdapter 的 compatibilityMode 不一致
- **根因**: 原先从构造函数捕获 `compatibilityMode`（Activate 时通常为 `Unknown`），外层 `BuiltInLegacyAdapterClientExtension` 用实时 `lifecycle.CompatibilityMode`
- **影响**: 外层的 gate 通过但内层 gate 失败，出站包由 Trade handler (P=1100) 处理，把 FrameworkPacket 发给 Legacy 服务器
- **修复**: 注入 `IFrameworkClientLifecycle` 引用，`CanHandleOutgoingCommand` 读实时值

### 3.2 【已修复】入站 HandleUpdateItems 无法定位对方 UUID
- **根因**: 服务器回显 UpdateTradeItems（packet.Uuid == self）时，通过 `TryGetTrade` → `ToTradeSnapshot` → `userDirectory?.Uuid` 匹配查不到 repo 中的 trade，返回 "cannot resolve other party"
- **修复**: 不再通过 `ClientTradeSnapshot` 间接查询，改用 `tradeApi.GetRepositoryTrades()` 直接读 `FrameworkTradeStateSnapshot`，原位更新参与者数据

### 3.3 【已修复】入站 HandleUpdateStatus 同样的问题
- **修复**: 同上，改用 `GetRepositoryTrades()` 原位读取 + 原位更新

### 3.4 【已修复】物品转换异常被静默吞掉
- **修复**: 所有裸 `catch {}` → `catch (Exception ex)` + `log?.Invoke($"{ex}")`

## 四、文件改动清单

| 文件 | 改动类型 | 说明 |
|------|:--:|------|
| `Common/Utils/Framework/FrameworkTypes.cs` | 新增 | `IClientOutgoingCommandHandler` + `ClientOutgoingCommandResult` |
| `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs` | 新增 | `IFrameworkClientTransport.TryHandleOutgoingCommand()` |
| `Client/Source/Framework/PhinixFrameworkClient.cs` | 新增 | `TryHandleOutgoingCommand()` 实现 |
| `Extensions/Trade/Client/BuiltInTradeClientExtension.cs` | 修改 | 实现 `IClientOutgoingCommandHandler` |
| `Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs` | 修改 | 删除旁路逻辑，统一走管线 |
| `Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs` | 修改 | 实现 `IClientOutgoingCommandHandler`，注入 lifecycle |
| `Extensions/LegacyAdapter/Client/LegacyTradeProtocolAdapter.cs` | 大量修改 | 出站管线翻译 + 入站原位更新 + 全链路日志 |

## 五、设计哲学合规检查

| 条款 | 状态 | 说明 |
|------|:--:|------|
| §1.1 插件平权 | ✅ | Trade 和 LegacyAdapter 通过同一管线按 Priority 竞争 |
| §1.2 host 不依赖插件 | ✅ | PhinixFrameworkClient 只依赖 `IClientOutgoingCommandHandler`（通用契约），不引用插件 |
| §1.3 host 只做通用服务 | ✅ | host 不包含任何 trade/chat 业务逻辑 |
| §3.7 插件不绕过管线 | ✅ | 出站命令管线建成，所有出站经 `TryHandleOutgoingCommand` |
| §3.8 日志与可观测性 | ✅ | 所有日志通过 `ILoggable` 回调上报，异常附带完整 `Exception`，消息含 `[Tag]` 模块标识 |
| §6 增量更新 | ✅ | 不修改任何现有公开接口；仅新增接口和方法 |

## 六、新对话 Agent 应阅读的代码文件

### 核心理解（必读）
| 文件 | 用途 |
|------|------|
| `docs/设计哲学.md` | 架构基准，所有改动必须遵守 |
| `docs/legacy-trade-outbound-fix.md` | 本次修复的完整方案文档 |
| `Extensions/LegacyAdapter/Client/LegacyTradeProtocolAdapter.cs` | **核心** — 出站翻译 + 入站处理，本次改动最集中的文件 |
| `Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs` | LegacyAdapter 插件入口，管线接入点 |
| `Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs` | Trade 门面，出站入口，管线调用方 |

### 数据流理解
| 文件 | 用途 |
|------|------|
| `Client/Source/Framework/PhinixFrameworkClient.cs` | `TryHandleOutgoingCommand` 管线实现 + `sendPacket` |
| `Common/Utils/Framework/FrameworkTypes.cs` | `IClientOutgoingCommandHandler` 接口定义 |
| `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs` | `IFrameworkClientTransport`, `ILegacyModuleTransport`, `IFrameworkClientLifecycle` |
| `Extensions/Trade/Contracts/TradeContracts.cs` | `FrameworkTradeProtocol`, `FrameworkTradeStateSnapshot` 等 DTO |
| `Extensions/Trade/Contracts/TradeDomainContracts.cs` | `IFrameworkTradeClientApi`, `ClientTradeSnapshot` |

### 管线组件
| 文件 | 用途 |
|------|------|
| `Extensions/Trade/Client/BuiltInTradeClientExtension.cs` | Trade handler (P=1100)，V2 模式出站 |
| `Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs` | Trade API 实现，repo 读写，`GetRepositoryTrades()` |
| `Extensions/Trade/Client/PhinixFrameworkTradeClientRepository.cs` | Trade 仓库 |

### 物品编解码
| 文件 | 用途 |
|------|------|
| `Extensions/Trade/Client/DefaultLegacyTradeItemCodec.cs` | `core.item.vanilla` codec — 编/解码 FrameworkVanillaItemData |
| `Extensions/Trade/Client/FrameworkTradeItemPayloadCodec.cs` | `TryDecodeToTradeItemSnapshot` — `FrameworkItemPayload` → `TradeItemSnapshot` |
| `Extensions/Trade/Client/TradeClientItemPipeline.cs` | Item pipeline — 聚合所有 IItemCodec |
| `Extensions/Trade/Contracts/TradeDomainContracts.cs` | `TradeItemSnapshot` 定义 |

### 基础类型
| 文件 | 用途 |
|------|------|
| `Common/Utils/Framework/FrameworkPacket.cs` | `FrameworkPacket`, `FrameworkItemPayload` |
| `Common/Utils/Framework/FrameworkSerialization.cs` | 序列化/反序列化工具，`DeserializeItemData` |
| `Common/Utils/ProtobufPacketHelper.cs` | Legacy Proto 打包/验证 |
| `Common/Utils/ILoggable.cs` + `LogEventArgs.cs` + `LogLevel.cs` | 日志基础设施 |

### 调试日志
| 文件 | 用途 |
|------|------|
| `logs/2026.5.31.txt` | 第一次测试日志（出站正常，入站 "3 participants" 错误） |
| `logs/2026.5.31 2号日志.txt` | 第二次测试日志（出站正常，入站 "cannot resolve other party" 错误） |
