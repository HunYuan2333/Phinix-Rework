---
name: phase5-plugin-migration
description: Phase 5 插件架构迁移 — 核心原则、当前状态、已完成/待完成清单。每个插件平等：Chat 和 Trade 与第三方 submod 同权。
metadata:
  type: project
---

## 核心原则

- 插件可以依赖 host，host 不依赖插件（只引用 `ClientExtensionAbstractions`）
- 插件平等：Chat/Trade 和第三方 submod 走同一套发现→注册→激活
- host 只做通用服务（网络、扩展加载、生命周期、基础壳 UI）
- 插件间交互由插件自己定义接口调用，框架不负责中介

## 当前落点

参见 `docs/phase5-current-state-2026-05-27.md`。

### 已完成
- `IMainTabProvider` + `IBadgeProvider` 动态 Tab 机制（ServerTab 纯容器，插件动态注册）
- Chat 插件：`ChatMainTabProvider`（消息列表+输入框+发送+角标）
- Trade 插件：`TradeMainTabProvider`（完整 TradeList）
- Trade 完整迁出：TradeWindow/TradeList/StackedThings/PendingThings/ClientTradeUiHostContext
- 共享层：GUIUtils、UnknownItem → ClientExtensionAbstractions
- IClientSettingsContext 扩展（ChatMessageLimit、BlockUser 等）
- Host 解耦：不再 new TradeWindow、不再 hardcode IChatTabContent

### 待完成（按优先级）
1. Chat HostContext 完整迁移（ChatUiHostContext 已建，事件中继待接）
2. UserList → Chat 插件
3. PhinixDefaultTradeBehaviour → Trade 插件
4. 断掉 host→plugin ProjectReference
5. 域名类型迁到插件 Contracts（需先解决双目标 net472/net10.0 兼容）
6. ServerTab 右栏迁到 Chat 插件
