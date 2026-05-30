# Legacy Adapter 完整路线图

> 2026-05-30，新客户端 → 老服务端兼容层。

---

## 当前状态（全部完成）

| Phase | 内容 | 状态 |
|-------|------|------|
| Phase 1 | 框架：2 个新接口 + ILegacyModuleTransport + IDisplayMessageSink + NetClientLegacyTransportAdapter | ✅ 完成 |
| Phase 2 | Chat 协议完善：消息反馈、Legacy 模式 fallthrough 修复、Keep-alive(无需改动) | ✅ 完成 |
| Phase 3 | Trade 协议翻译：LegacyTradeProtocolAdapter + 状态机 + 旧版 proto 文件迁移到 Contracts/Proto/PhinixLegacy/ | ✅ 完成 |
| Phase 4 | 连接流程兼容：Auth/UserManagement 两套代码 wire-compatible，无需适配 | ✅ 无需改动 |
| Phase 5 | 稳定性：错误隔离、反压、资源清理、HandleOutgoingText 离线保护 | ✅ 完成 |
| Phase 6 | 运行时端到端测试 — 连接原版 Phinix 服务器验证 | ⏳ 待测试 |

---

## 架构回顾

```
LegacyAdapter (Priority=500, 标准插件)
  ├─ IClientMessageHandler → 劫持出站文本 → 翻译为 legacy ChatMessagePacket
  ├─ IClientCommandHandler → 占位（Legacy 入站不走 Framework pipeline）
  ├─ ILegacyModuleTransport  → 直接操作 NetClient 原始模块通信（Chat + Trading）
  └─ CompatibilityModeChanged → 自动注册/注销 legacy handlers

Host 改动（2 个接口，通用平台能力）：
  ILegacyModuleTransport  ← 任意插件可用它直接操作 NetClient
  IDisplayMessageSink      ← 任意插件可用它注入 FrameworkDisplayMessage
```

### DLL 加载顺序

```
10-LegacyAdapter.Client.dll      ← Priority=500
11-ChatExtension.Client.dll      ← Priority=1000
12-TradeExtension.Client.dll     ← Priority=1100
```

---

## 管线绕过修复（2026-05-30）

> Chat/Trade 插件绕过了 `IClientMessageHandler` 管线，直接调 `SendFrameworkPacket`，
> 导致 LegacyAdapter 通过 Priority 排序劫持消息的机制失效。

| # | 文件 | 问题 | 修复 |
|---|------|------|------|
| 1 | `IFrameworkClientTransport` 接口 | 缺少管线入口 | 新增 `TryHandleOutgoingMessage(string)` 方法 |
| 2 | `BuiltInChatClientExtension.cs` (Register) | ChatMainTabProvider.send 回调直接调 `SendFrameworkPacket` | 改为调 `IFrameworkClientTransport.TryHandleOutgoingMessage`，走完整 handler 管线 |
| 3 | `BuiltInChatClientExtension.cs` (HandleOutgoingText) | 无条件返回 FrameworkPacket | 非 FrameworkV2 模式返回 `LegacyFallback`，让管线继续 |
| 4 | `FrameworkClientTradeServiceAdapter.cs` (4 个方法) | Trade 方法直接调 `SendFrameworkPacket` | 统一走 `SendTradePacket()`，非 FrameworkV2 记录 Warning 丢弃 |
| 5 | `PhinixFrameworkClient.cs` (TryHandleOutgoingMessage) | `null` result 返回 `true` (消费)；`LegacyFallback` 返回 `false` (终止管线) | `null` → `continue`；`LegacyFallback` → `continue` |

### 修复后的流水线逻辑

| 模式 | Adapter(P=500) | Chat(P=1000) | 发送方式 |
|------|----------------|--------------|----------|
| FrameworkV2 | CanHandle=false，跳过 | HandleOutgoingText → FrameworkPacket | host 转调 sendPacket → "PhinixFramework" |
| Legacy | CanHandle=true，拦截翻译 | 不执行（被 Adapter 拦截） | Adapter → ILegacyModuleTransport.Send → Legacy Module |

### 设计原则

- 插件通过接口暴露的管线方法（`IFrameworkClientTransport.TryHandleOutgoingMessage`）发送消息，不直接调 `SendFrameworkPacket`
- 接口依赖遵循 §1.2：Chat/Trade 插件通过 `ClientExtensionAbstractions` 中定义的接口契约与 host 交互
- 设计哲学 §3.7：所有出站通信必须走 handler 管线

---

## Phase 4 结论

Auth (`"Authentication"`) 和 UserManagement (`"UserManagement"`) 在 Rework 和旧版使用：
- **相同的 module names**
- **相同的 protobuf packet schema**
- **都直接走 NetClient，不经过 Framework**

唯一差异是 RNG 实现（`RandomNumberGenerator.Create()` vs `RNGCryptoServiceProvider`），不影响 wire protocol。**无需任何适配器代码。**

---

## Phase 5 目录

- [x] 5.1 错误隔离 — Chat: `SafeUnpackAndHandle` 泛型解包包装，Trade: 所有 Unpack 在 try-catch 内
- [x] 5.2 入站反压 — Chat: `MaxPendingMessages=200` 计数器 + Warning
- [x] 5.3 资源清理 — Shutdown 中注销所有 handlers + 清空状态字典 + 取消事件订阅
- [x] 5.4 离线发送保护 — 后续运行时测试确认
- [x] 5.5 空消息过滤 — HandleChatMessage 跳过空 Text
- [ ] 5.6 运行时端到端测试 — 连接原版 Phinix 服务器验证

---

## 关键文件

| 文件 | 说明 |
|------|------|
| `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs` | ILegacyModuleTransport + IDisplayMessageSink |
| `Client/Source/Framework/NetClientLegacyTransportAdapter.cs` | NetClient 包装 |
| `Client/Source/Framework/PhinixFrameworkClient.cs` | IDisplayMessageSink 实现 |
| `Client/Source/Client.cs` | 服务注册 |
| `Extensions/LegacyAdapter/Client/PhinixLegacyAdapter.Client.csproj` | 插件项目 |
| `Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs` | 入口 |
| `Extensions/LegacyAdapter/Client/LegacyChatProtocolAdapter.cs` | Chat 翻译 |
| `Extensions/LegacyAdapter/Client/LegacyTradeProtocolAdapter.cs` | Trade 翻译 |
| `Extensions/LegacyAdapter/Contracts/Proto/PhinixLegacy/Chat/*.cs` | 旧版 Chat proto 类型 |
| `Extensions/LegacyAdapter/Contracts/Proto/PhinixLegacy/Trading/*.cs` | 旧版 Trading proto 类型 |
