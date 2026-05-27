# Phase 5 架构迁移 — 当前状态

> 2026-05-27，可直接给新对话接上下文

## 目标

**Chat/Trade 从"宿主内建功能"变成"与第三方平权的插件"。**

### 核心原则

- **插件可以依赖 host，host 不依赖插件**。宿主只引用 `ClientExtensionAbstractions`（共享契约层），不引用任何插件的 Contracts 工程。
- **插件平等**。Chat 和 Trade 与第三方 submod 走完全一样的发现→注册→激活路径。
- **host 只做通用服务**：网络层、扩展加载、生命周期、基础壳 UI。业务全部在插件里。
- **通信已有动态分发的模板**（module string → Kind → MessageType → handler），UI 层用同样的思路：`IMainTabProvider` / `IServerSidebarProvider`。
- **插件间交互**：插件自己定义接口、自己调用，框架不负责中介。

### 最终形态

```
host/Client （PhinixClient.dll）
  ├─ 网络层 (NetClient)
  ├─ 扩展发现 (PhinixExtensionRegistry)
  ├─ 通用服务 (IClientSessionContext, IClientSettingsContext, IClientUserDirectory, IClientUserEventStream, IClientMainThreadDispatcher, IClientWindowService, IFrameworkClientTransport...)
  ├─ ServerTab（纯壳，收集 IMainTabProvider / IServerSidebarProvider 动态渲染）
  └─ 基础 UI (SettingsWindow, CredentialsWindow, 基础 Widget)

插件 (独立编译、独立落位、运行时发现)
  ├─ ChatExtension.Client.dll → IMainTabProvider + IBadgeProvider + IServerSidebarProvider
  ├─ TradeExtension.Client.dll → IMainTabProvider + 默认 trade 行为
  └─ SomeSubMod.dll → IMainTabProvider / IServerSidebarProvider（同等路径）
```

### 发现路径（和通信层同一模式）

| | 通信层 | UI 层 |
|---|---|---|
| 接口定义 | `Common/Utils/Framework/FrameworkTypes.cs` | `ClientExtensionAbstractions/UI/IMainTabProvider.cs` |
| 插件注册 | `builder.AddClientMessageHandler(this)` | `builder.RegisterApi<IMainTabProvider>(this)` |
| host 收集 | `foreach handler in ClientMessageHandlers` | `foreach provider in MainTabProviders` |
| 路由依据 | `CanHandleIncomingMessage(message)` | `TabOrder` 排序，用户点击切换 |

补充：

- 右栏已用同一思路插件化：`IServerSidebarProvider`
- Host 事件桥接不再使用插件专用 sink，而是通过通用 `IClientUserEventStream`

---

## 已完成

### 1. 动态 Tab 机制（核心架构）

- `IMainTabProvider` + `IBadgeProvider` 定义在 `ClientExtensionAbstractions/UI/`
- `IServerSidebarProvider` 定义在 `ClientExtensionAbstractions/UI/`
- `ServerTab` 不再是硬编码 Chat/Trade，改为收集所有 `IMainTabProvider`，按 `TabOrder` 排序动态生成 `TabRecord`
- `ServerTab` 右栏不再硬编码 Chat，改为动态收集 `IServerSidebarProvider`
- `ServerTabButtonWorker` 聚合所有 `IBadgeProvider`（不再只读 Chat 未读数）
- `PhinixFrameworkClient` 新增 `ResolveExtensionApis<T>()` 方法
- `Client` 新增 `MainTabProviders` / `SidebarProviders` 属性

### 2. Chat 插件

- `ChatMainTabProvider : IMainTabProvider, IBadgeProvider` — 包含消息列表 + 输入框 + 发送按钮 + 未读角标
- `ChatSidebarProvider : IServerSidebarProvider` — 包含设置按钮 + 用户搜索 + 用户列表
- `UserList.cs` 已迁到 `Extensions/Chat/Client/`
- 在 `BuiltInChatClientExtension.Activate()` 中创建并注册
- 发送消息通过 `IFrameworkClientTransport.SendFrameworkPacket()` + `IFrameworkChatClientApi.CreateOutgoingMessage()`
- `ChatUiHostContext` 已由 Chat 插件自己创建并注册，不再由 host `new`
- Chat 事件中继改为消费通用 `IClientUserEventStream`，已不再需要 `ClientChatUiHostContext`

### 3. Trade 插件

- `TradeMainTabProvider : IMainTabProvider` — 包装 `TradeList`
- 在 `BuiltInTradeClientExtension.Register()` 中创建并注册
- **完整迁出的文件**：`TradeWindow.cs`, `TradeList.cs`, `StackedThings.cs`, `PendingThings.cs`, `TradeItemConverter.cs`, `ClientTradeUiHostContext.cs`
- `ClientTradeUiHostContext` 改为接口依赖版（`IClientTradeService` + `Func<>` + `Action<>`），不再引用 `Client`
- `ClientTradeUiHostContext` 现在通过 `IClientUserEventStream` / `IClientSettingsContext` 获取断线、改名和设置状态
- Trade 插件自己创建 `ITradeUiHostContext` 并注册为 API
- `PhinixDefaultTradeBehaviour` 已迁到 `Extensions/Trade/Client/`，trade 创建成功立即开窗、完成/取消 letter、更新失败弹窗都在插件侧处理
- Trade 插件通过 `IClientMainThreadDispatcher` + `IClientWindowService` 回到主线程执行 UI/letter

### 4. Host 解耦

- `Client.cs` 不再 `new ClientTradeUiHostContext(this)`
- `Client.cs` 不再 `new TradeWindow(...)`
- `Client.cs` 不再硬性要求 `IChatTabContent` 存在
- Drop pods 回调通过 `ExtensionHostContext.AddService<Func<...>>()` 注入
- `Client.cs` 不再通过 `IChatUiEventSink` / `ITradeUiHostContext` 这样的插件专用桥接接口驱动 UI
- Host 改为提供通用服务：
  - `IClientUserEventStream`
  - `IClientMainThreadDispatcher`
  - `IClientWindowService`
- Chat / Trade 业务事件消费和 UI 决策都回到插件侧

### 5. 共享层迁移

- `GUIUtils.cs` → `ClientExtensionAbstractions/GUI/`（Unity/Verse 通用工具）
- `UnknownItem.cs` → `ClientExtensionAbstractions/`（host 和 Trade 插件都需要）
- `IClientSettingsContext` 扩展：新增 `ChatMessageLimit`, `ShowNameFormatting`, `ShowChatFormatting`, `AllItemsTradable`, `ShowBlockedTrades`, `CollapseBlockedUsers`, `BlockUser`, `UnBlockUser`
- `IClientUserDirectory` 扩展：新增 `GetUsers(bool loggedIn = false)`
- 新增通用 host 服务接口：
  - `IClientUserEventStream`
  - `IClientMainThreadDispatcher`
  - `IClientWindowService`
- 新增通用 host 事件类型：
  - `FrameworkDisplayMessageEventArgs`
  - `UserBlockStateChangedEventArgs`
- Chat / Trade 领域接口和 DTO 已迁回各自 `Contracts`
- `ClientExtensionAbstractions.csproj` 已不再引用 `TradeExtension.csproj`

### 6. 程序集引用补全

- `ClientExtensionAbstractions`: `UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, `UnityEngine.TextRenderingModule`
- `ChatExtension.Client`: `UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, `UnityEngine.TextRenderingModule`
- `TradeExtension.Client`: `Assembly-CSharp`, `UnityEngine`, `UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, `UnityEngine.TextRenderingModule`

---

## 待完成

### 优先级高

1. **Host / plugin 的运行时边界已达到目标形态**：
   - Host 只保留通用能力接口和通用事件
   - Chat / Trade 领域接口、DTO、默认行为、UI 渲染都已回到插件
   - `Client.csproj` 已不再显式引用 Chat / Trade 工程

2. **继续清理残留的历史文件与发布链**：
   - 目前源码树里仍可能保留少量未参与编译的 legacy 文件，用于历史对照
   - 如果希望仓库边界更“物理纯净”，可以继续删除这些未编译的旧副本

### 优先级中

3. **把 `SettingsWindow` 暴露方式进一步通用化**：
   - 当前通过 `IClientWindowService.OpenSettingsWindow()`
   - 若未来还有更多 host 壳窗口，可考虑进一步抽成统一 window routing / window id

### 优先级低

4. **Docker / 发布链对齐**：当前 Dockerfile 还没把 `Extensions/` 纳入构建上下文。

---

## 关键文件索引

| 文件 | 角色 |
|---|---|
| `Client/ClientExtensionAbstractions/UI/IMainTabProvider.cs` | 通用 Tab 注册契约 |
| `Client/ClientExtensionAbstractions/UI/IBadgeProvider.cs` | 通用角标契约 |
| `Client/ClientExtensionAbstractions/UI/IServerSidebarProvider.cs` | 通用右栏注册契约 |
| `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs` | 纯通用 host 服务接口 |
| `Client/Source/GUI/Windows and panels/ServerTab.cs` | 动态 Tab 容器 |
| `Client/Source/Client.cs` | Host 入口，扩展加载编排 |
| `Client/Source/Framework/PhinixFrameworkClient.cs` | 框架客户端，API 解析 |
| `Extensions/Chat/Contracts/ChatDomainContracts.cs` | Chat 领域契约（UI 消息、Chat host context、Chat service） |
| `Extensions/Chat/Client/ChatMainTabProvider.cs` | Chat Tab 实现 |
| `Extensions/Chat/Client/ChatSidebarProvider.cs` | Chat 右栏实现 |
| `Extensions/Chat/Client/UserList.cs` | Chat 用户列表（已迁出 host） |
| `Extensions/Chat/Client/ChatUiHostContext.cs` | Chat 上下文（已由插件自己创建并消费通用事件流） |
| `Extensions/Chat/Client/BuiltInChatClientExtension.cs` | Chat 扩展注册/激活 |
| `Extensions/Trade/Contracts/TradeDomainContracts.cs` | Trade 领域契约（DTO、event args、trade services、trade host context） |
| `Extensions/Trade/Client/TradeMainTabProvider.cs` | Trade Tab 实现 |
| `Extensions/Trade/Client/ClientTradeUiHostContext.cs` | Trade 上下文实现（已迁出 host） |
| `Extensions/Trade/Client/TradeClientItemPipeline.cs` | Trade 物品编码管线（已迁出 host） |
| `Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs` | Trade 默认行为（已迁出 host） |
| `Extensions/Trade/Client/BuiltInTradeClientExtension.cs` | Trade 扩展注册/激活 |
| `Common/Utils/Framework/PhinixExtensionRegistry.cs` | 扩展发现与注册 |
| `Common/Utils/Framework/FrameworkTypes.cs` | 框架类型（handler contract 等） |
