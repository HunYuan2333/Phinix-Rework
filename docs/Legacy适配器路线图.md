# Legacy 适配器路线图

> 新客户端 → 老服务端兼容层。实现代码：`Extensions/LegacyAdapter/`。

---

## 实施状态

| Phase | 内容 | 状态 |
|-------|------|------|
| 1 | 框架接口：`ILegacyModuleTransport` + `IDisplayMessageSink` + `NetClientLegacyTransportAdapter` | ✅ |
| 2 | Chat 协议完善（反馈、fallthrough 修复、Keep-alive） | ✅ |
| 3 | Trade 协议翻译（`LegacyTradeProtocolAdapter` + 状态机 + 旧版 proto） | ✅ |
| 4 | Auth/UserManagement 兼容（wire-compatible，无需适配） | ✅ |
| 5 | 稳定性：错误隔离、反压、资源清理、离线保护 | ✅ |
| 6 | E2E 运行测试 — 连接原版 Phinix 服务器 | ⏳ |

---

## 架构

```
LegacyAdapter (Priority=500, 标准 IPhinixExtensionModule)
  ├─ IClientMessageHandler        → 劫持出站文本 → legacy ChatMessagePacket
  ├─ IClientCommandHandler         → 占位（legacy 入站不走 Framework）
  ├─ IClientOutgoingCommandHandler → 劫持出站 Trade 命令
  ├─ ILegacyModuleTransport        → 直接操作 NetClient 原始模块
  └─ CompatibilityModeChanged      → 自动注册/注销 legacy handlers
```

Host 改动（2 个接口，通用平台能力，非插件专用）：
- `ILegacyModuleTransport`（`IClientExtensionAbstractions.cs`）
- `IDisplayMessageSink`（同上）

---

## DLL 加载顺序

```
10-LegacyAdapter.Client.dll    (P=500)
11-ChatExtension.Client.dll    (P=1000)
12-TradeExtension.Client.dll   (P=1100)
```

---

## 出站管线（已修复）

所有出站走 `IFrameworkClientTransport.TryHandleOutgoingMessage` / `TryHandleOutgoingCommand`，不直连 `SendFrameworkPacket`。

| 模式 | Adapter(P=500) | Chat/Trade handler | 最终发送 |
|------|----------------|-------------------|---------|
| FrameworkV2 | 不匹配，跳过 | 原样返回 FrameworkPacket | host sendPacket → "PhinixFramework" |
| Legacy | 翻译为 Legacy Proto | 不执行（被拦截） | `ILegacyModuleTransport.Send` |

---

## 关键文件

| 文件 | 说明 |
|------|------|
| `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs` | `ILegacyModuleTransport` + `IDisplayMessageSink` 接口 |
| `Client/Source/Framework/NetClientLegacyTransportAdapter.cs` | NetClient→`ILegacyModuleTransport` 适配 |
| `Client/Source/Framework/PhinixFrameworkClient.cs` | `IDisplayMessageSink` 实现 + `IDisposable` |
| `Client/Source/Client.cs` | `ILegacyModuleTransport` 服务注册 |
| `Extensions/LegacyAdapter/Client/` | Legacy Chat/Trade 协议翻译（19 文件） |
| `Extensions/LegacyAdapter/Contracts/Proto/PhinixLegacy/` | 旧版 proto 类型 |

---

## 相关文档

- [设计哲学.md](设计哲学.md) — §3.7 通信管线约束
- [Legacy适配器路线图.md](Legacy适配器路线图.md)
