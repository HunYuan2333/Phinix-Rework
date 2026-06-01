# Phinix 附属 Mod 开发者开发指南

> **面向人群**：第三方附属 Mod / Submod 开发者。本文档假定你已经会用 C# 给 RimWorld 写 Mod，但可能是第一次接触 Phinix 框架。
>
> **文档定位**：本文档与 [设计哲学.md](./设计哲学.md) 同为跨分支共享基线文档。前者阐述"为什么这样设计"；本文档告诉你"怎么落地使用"。
>
> **最后更新**：2026-06-01，基于 `dev` 分支实际代码编写。框架仍在活跃演进中——本文档中会明确标注每项能力的当前状态：✅ 完整可用、⚠️ 半成品/过渡态、🔮 计划中。

---

## 目录

1. [架构总览](#1-架构总览)
2. [依赖边界](#2-依赖边界)
3. [扩展入口与生命周期](#3-扩展入口与生命周期)
4. [注册表：IExtensionBuilder 能做什么](#4-注册表iextensionbuilder-能做什么)
5. [API 暴露与解析](#5-api-暴露与解析)
6. [三条通信管线](#6-三条通信管线)
7. [接入 UI](#7-接入-ui)
8. [Host 提供的通用服务](#8-host-提供的通用服务)
9. [插件间协作](#9-插件间协作)
10. [兼容模式与 Legacy](#10-兼容模式与legacy)
11. [常见反模式与踩坑点](#11-常见反模式与踩坑点)
12. [最小可行示例](#12-最小可行示例)
    - [12.1 环境准备与先决条件](#121-环境准备与先决条件)
    - [12.2 目录结构](#122-目录结构)
    - [12.3 工程配置](#123-工程配置)
    - [12.4 扩展入口类完整代码](#124-扩展入口类完整代码)
    - [12.5 可选：注册领域 Contracts 工程](#125-可选注册领域-contracts-工程)
    - [12.6 构建与部署](#126-构建与部署)
    - [12.7 加载顺序号解析](#127-加载顺序号解析)
    - [12.8 调试提示](#128-调试提示)
- [附录 A：IExtensionBuilder 全部注册方法速查表](#附录-aiextensionbuilder-全部注册方法速查表)
- [附录 B：ExtensionHostContext 全部服务速查表](#附录-bextensionhostcontext-全部服务速查表)

---

## 1. 架构总览

### 1.1 分层架构

Phinix 从下到上分为四层：

```
┌─────────────────────────────────────────┐
│  Plugins (Chat, Trade, 你的 Submod)      │  ← 业务层
├─────────────────────────────────────────┤
│  ClientExtensionAbstractions             │  ← 共享契约层（UI 接口 + host 服务接口）
├─────────────────────────────────────────┤
│  Host (Client / Server)                  │  ← 宿主层（网络、认证、扩展发现、UI 壳）
├─────────────────────────────────────────┤
│  Common (Utils, Connections, etc.)       │  ← 基础设施层（协议、类型、工具）
└─────────────────────────────────────────┘
```

- **上层可以依赖下层**。插件可以引用 `Utils`、`ClientExtensionAbstractions`。
- **下层绝不反向依赖上层**。`Common/Utils/` 不知道任何具体插件存在。
- **同层模块尽量独立**。Chat 和 Trade 之间通过 API registry 互相发现，不通过 host 中转。

相关源文件：
- [Client/ClientExtensionAbstractions/](Client/ClientExtensionAbstractions/) — 共享契约层，定义所有 UI 与 host 服务接口
- [Common/Utils/Framework/FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs) — 所有 handler、builder、context 类型的定义
- [Common/Utils/Framework/PhinixExtensionRegistry.cs](Common/Utils/Framework/PhinixExtensionRegistry.cs) — 扩展发现引擎

### 1.2 插件平权

Chat 和 Trade **不是**特权模块。它们和你写的 Submod 走完全相同的路径：

- 同一套发现路径：反射扫描实现了 `IPhinixExtensionModule` 的类
- 同一套注册路径：`Register(builder)` → 注册 handler / API
- 同一套激活路径：`Activate(hostContext)` → `Shutdown(hostContext)`

**你的 Submod 和 Chat/Trade 唯一的区别是 Priority 数值**：Priority 小的先执行。Chat 的 Priority 是 1000，Trade 是 1100，LegacyAdapter 是 500。你的 Submod 可以选一个合适的 Priority 插在它们之间。

### 1.3 哪些可以碰、哪些不能碰

| 可以引用 | 不能引用 |
|----------|----------|
| `Utils`（Common 层） | `Client.csproj` 宿主工程 |
| `ClientExtensionAbstractions` | `Server.csproj` 宿主工程 |
| `UserManagement` | 其他插件的**内部实现**类 |
| 其他插件的 `Contracts` 工程（如果想调用其 API） | 把代码放在 Common 目录里（Common 只放 runtime-neutral 代码） |

---

## 2. 依赖边界

### 2.1 必须引用的程序集

每个客户端 Submod 至少需要引用以下程序集：

| 程序集 | 提供内容 | 工程路径 |
|--------|----------|----------|
| `Utils` | `IPhinixExtensionModule`、`IExtensionBuilder`、`FrameworkPacket`、`FrameworkTypes` 等核心类型 | [Common/Utils/Utils.csproj](Common/Utils/Utils.csproj) |
| `ClientExtensionAbstractions` | `IMainTabProvider`、`IServerSidebarProvider`、`IBadgeProvider`、`IClientSettingsContext` 等宿主服务接口 | [Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj](Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj) |

常见额外依赖（如果 Submod 需要操作用户数据）：

| 程序集 | 提供内容 | 工程路径 |
|--------|----------|----------|
| `UserManagement` | `ImmutableUser` 等用户类型 | [Common/UserManagement/UserManagement.csproj](Common/UserManagement/UserManagement.csproj) |

此外还需要 RimWorld 标准引用：`Assembly-CSharp`、`UnityEngine`、`UnityEngine.CoreModule`、`UnityEngine.IMGUIModule` 等。

### 2.2 可选引用的程序集

如果你的 Submod 需要调用 Chat 或 Trade 的能力：

| 程序集 | 提供内容 | 工程路径 |
|--------|----------|----------|
| `ChatExtension`（Contracts） | `IFrameworkChatClientApi`、`IChatUiHostContext` 等，供插件间直接调用 | [Extensions/Chat/Contracts/ChatExtension.csproj](Extensions/Chat/Contracts/ChatExtension.csproj) |
| `TradeExtension`（Contracts） | `IFrameworkTradeClientApi`、`ITradeRequestApi` 等 | [Extensions/Trade/Contracts/TradeExtension.csproj](Extensions/Trade/Contracts/TradeExtension.csproj) |

> **注意**：引用 Contracts 工程不会让你依赖 Chat/Trade 的内部实现——Contracts 只包含接口定义和协议常量。这是推荐的插件间协作方式（详见 [§9](#9-插件间协作)）。

### 2.3 绝对不能引用的

- ❌ **Client 宿主工程**：`Client/Source/Client.csproj`。Host 不依赖插件，反之也不能依赖 host。
- ❌ **Server 宿主工程**：客户端插件不需要它。
- ❌ **Common 中端专属的实现类**：如 `Connections.Client`（注意 `.Client` 后缀——它是客户端 Connnections 的子工程，由客户端编译，不是 Common 本体）。

### 2.4 物理部署：DLL 放在哪

宿主启动时调用 `ExtensionAssemblyLoader.LoadAssemblies()` 扫描以下目录下的 `.dll` 文件（详见 [Client.cs:400-429](Client/Source/Client.cs#L400-L429) 的 `GetExtensionProbeDirectories` 方法）：

```
YourMod/
  Common/
    Assemblies/           ← 框架基础 DLL（01-07）+ 当前官方插件 DLL（08-11）
    Extensions/           ← 专用插件目录（当前也扫描，目标态将独立）
```

数字前缀（如 `08-`）不能省略——RimWorld 的 `ModAssemblyHandler` 按文件名字符串序加载 DLL，必须保证依赖项先于被依赖项加载（详见 [§12.7](#127-加载顺序号解析)）。ExtensionAssemblyLoader 代码位置：[Common/Utils/Framework/ExtensionAssemblyLoader.cs](Common/Utils/Framework/ExtensionAssemblyLoader.cs)。

未来目标态（[设计哲学 §5.2](设计哲学.md#52-发布边界目标态)）会将插件 DLL 移至 `Extensions/` 并从 `Assemblies/` 中分离。

---

## 3. 扩展入口与生命周期

### 3.1 最小接口：`IPhinixExtensionModule`

每个 Submod 必须有一个类实现 `IPhinixExtensionModule`（定义于 [FrameworkTypes.cs:56-59](Common/Utils/Framework/FrameworkTypes.cs#L56-L59)）：

```csharp
public interface IPhinixExtensionModule : IPhinixExtension
{
    string ExtensionId { get; }      // 继承自 IPhinixExtension
    void Register(IExtensionBuilder builder);
}
```

- `ExtensionId`：全局唯一标识符。推荐格式 `author.modname`（如 `"myname.myfeature"`）。
- `Register()`：在扩展被发现后调用。核心职责是注册 handler、API、capability 等。关于是否可以在此阶段获取 host 服务，参见 [§8 开头的说明](#8-host-提供的通用服务)。

### 3.2 可选接口：`IActivatablePhinixExtensionModule`

如果你的 Submod 需要在宿主就绪后执行初始化，实现此接口（定义于 [FrameworkTypes.cs:61-66](Common/Utils/Framework/FrameworkTypes.cs#L61-L66)）：

```csharp
public interface IActivatablePhinixExtensionModule : IPhinixExtension
{
    void Activate(ExtensionHostContext hostContext);
    void Shutdown(ExtensionHostContext hostContext);
}
```

- `Activate()`：从 `hostContext` 获取所需服务，订阅事件，开始工作。
- `Shutdown()`：取消事件订阅，释放资源。**必须**把 `Activate()` 中所有的 `+=` 在这里 `-=` 掉。

> **注意**：`IPhinixExtensionModule` 和 `IActivatablePhinixExtensionModule` 是**独立接口**，不互为继承。你的模块需要同时实现两者才能获得完整生命周期。参考官方 Chat 扩展：[BuiltInChatClientExtension.cs:14](Extensions/Chat/Client/BuiltInChatClientExtension.cs#L14) 同时实现了这两个接口。

### 3.3 `[PhinixExtension]` 特性

你的模块类必须标记 `[PhinixExtension("your.id")]`，否则框架的反射扫描找不到你（除非你的类实现了 `IPhinixExtension` 且也被标记为非 abstract，在这种情况下旧的 legacy auto-discovery 路径仍然会拾取它，但框架会输出一条 warning 提示你迁移到 `IPhinixExtensionModule`）。

```csharp
[PhinixExtension("myname.myfeature")]
public class MyExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public string ExtensionId => "myname.myfeature";
    // ...
}
```

### 3.4 完整生命周期

框架对扩展的管理分为四个阶段（参考 [PhinixExtensionRegistry.cs](Common/Utils/Framework/PhinixExtensionRegistry.cs) 中 `DiscoverExtensions` 和 `ActivateExtensions` 方法）：

```
1. Discover  ── 反射扫描程序集，找到所有 IPhinixExtensionModule
                 ↓
2. Register  ── 调用每个模块的 Register(builder)
                模块注册 handler、API、capability
                完成后状态变为 Registered
                 ↓
3. Activate  ── 调用每个模块的 Activate(hostContext)
                模块获取 host 服务、订阅事件
                完成后状态变为 Active
                 ↓
4. Shutdown  ── 调用每个模块的 Shutdown(hostContext)
                模块取消订阅、释放资源
                完成后状态变为 Shutdown
```

### 3.5 错误隔离

单个模块的 `Register()`、`Activate()`、`Shutdown()` 失败**不会**影响其他模块：

- `Register()` 异常被 catch，状态标记为 `Failed`，记录 warning
- `Activate()` 异常被 catch，状态标记为 `Failed`，记录 warning
- `Shutdown()` 异常同样被隔离

这意味着**你的 Submod 不会拖垮整个框架**——但反过来说，框架也不会自动重试你的失败模块。

---

## 4. 注册表：IExtensionBuilder 能做什么

`Register(IExtensionBuilder builder)` 是你与框架交互的核心入口。`builder` 提供以下能力（完整接口定义见 [FrameworkTypes.cs:105-148](Common/Utils/Framework/FrameworkTypes.cs#L105-L148)）：

### 4.1 注册 handler（接入通信管线）

```csharp
// Message 管线
builder.AddClientMessageHandler(this);           // IClientMessageHandler

// Command 管线
builder.AddClientCommandHandler(this);           // IClientCommandHandler (入站)
// 如果你的类同时实现了 IClientCommandHandler 和 IClientOutgoingCommandHandler，
// AddClientCommandHandler(this) 一次注册即可——框架运行时会按 IClientOutgoingCommandHandler 筛选出站 handler。
// 如果只实现 IClientOutgoingCommandHandler（不入站），则需要单独在 builder 注册——
// 当前 AddClientCommandHandler 的参数类型为 IClientCommandHandler。

// 其他管线角色
builder.AddMessageInterceptor(this);             // IMessageInterceptor
builder.AddMessageRenderer(this);                // IMessageRenderer
builder.AddCapabilityProvider(this);             // ICapabilityProvider
builder.AddServerMessageHandler(this);           // IServerMessageHandler（服务端扩展用）
builder.AddItemCodec(this);                      // IItemCodec（⚠️ 半成品—见 §6.3）
```

### 4.2 注册 API（暴露自身能力）

```csharp
builder.RegisterApi<IMyService>(this);           // 以 IMyService 类型注册
builder.RegisterApi<IMainTabProvider>(myTab);    // 注册 UI 贡献
```

### 4.3 解析其他插件的 API

```csharp
// 获取单个 API（如果有多个提供者，返回第一个注册的）
builder.TryResolveApi<ITradeRequestApi>(out var tradeApi);

// 获取所有提供者
IReadOnlyList<IChatUiHostContext> contexts = builder.ResolveApis<IChatUiHostContext>();
```

### 4.4 读取 ExtensionId 和 HostContext

```csharp
string myId = builder.ExtensionId;               // 你自己的 ExtensionId
ExtensionHostContext hostCtx = builder.HostContext; // 宿主上下文
```

---

## 5. API 暴露与解析

### 5.1 RegisterApi<T>：暴露自身能力

在 `Register()` 中调用 `builder.RegisterApi<T>(implementation)`，你的实现就会进入框架的 API registry：

```csharp
public void Register(IExtensionBuilder builder)
{
    var myFeature = new MyFeatureService(/* ... */);
    builder.RegisterApi<IMyFeatureApi>(myFeature);
    builder.RegisterApi<IMainTabProvider>(myFeature); // 同时为 UI 提供 Tab
}
```

框架内部实现代码：[FrameworkTypes.cs:150-255](Common/Utils/Framework/FrameworkTypes.cs#L150-L255)（`ExtensionApiRegistry` 类）。

### 5.2 TryResolveApi<T> / ResolveApis<T>：发现他人能力

- `TryResolveApi<T>()`：返回第一个匹配的 API 实现。适合"只需要一个实现"的场景。
- `ResolveApis<T>()`：返回所有注册的 `T` 类型 API 实现列表。适合"收集所有贡献者"的场景（如 host 收集所有 `IMainTabProvider`）。

```csharp
// 在 Register() 中
if (builder.TryResolveApi<ITradeRequestApi>(out var tradeApi))
{
    // Trade 插件已注册，可以发起交易
    _tradeApi = tradeApi;
}

// host 在收集所有 Tab 时
IReadOnlyList<IMainTabProvider> tabs = builder.ResolveApis<IMainTabProvider>();
```

**解析顺序**：API registry 是顺序性的——先注册先返回（对 `TryResolve`）。如果同一个接口有多个提供者，`TryResolve` 返回第一个，`ResolveAll` 返回全部（按注册顺序）。

### 5.3 在 Activate 中解析

API registry 在所有模块的 `Register()` 执行完后已经填充完毕，因此你也可以在 `Activate()` 中通过 `hostContext` 解析 API：

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    if (hostContext.TryResolveApi<ITradeRequestApi>(out var tradeApi))
    {
        _tradeApi = tradeApi;
    }
}
```

### 5.4 与直接引用 Contracts 程序集的对比

| 方式 | 优点 | 缺点 |
|------|------|------|
| `RegisterApi` + `TryResolveApi` | 松耦合，不依赖对方程序集 | 需要接口定义一致；运行时发现 |  
| 直接引用 Contracts 工程 | 编译时安全；无需 `TryResolve` 判空 | 增加了编译依赖；对方 DLL 必须存在 |

**推荐**：如果对方提供了 Contracts 工程（如 Chat 和 Trade 都提供了），**直接引用 Contracts 工程**。API registry 方式更适合"对方没有提供 Contracts 程序集"或"你只需要弱依赖（对方可能不存在）"的场景。

---

## 6. 三条通信管线

框架定义了三条通信管线。**每条管线的当前可用程度不同**——请仔细阅读本节。

### 6.1 Message 管线 ✅ 完整可用

**职责**：传输"用户应该看到的东西"（聊天消息、系统通知等）。

**入站**（Server → Client）：

```
FrameworkPacket (Kind="message")
  → packetHandler 分支 KindMessage
  → IClientMessageHandler 链（按 Priority 排序）
  → CanHandleIncomingMessage(message) → HandleIncomingMessage(message, context)
  → IMessageRenderer → FrameworkDisplayMessage → UI
```

**出站**（Client → Server）：

```
用户输入文本
  → IFrameworkClientTransport.TryHandleOutgoingMessage(rawMessage)
  → IClientMessageHandler 链（按 Priority 排序）
  → CanHandleOutgoingText(rawMessage) → HandleOutgoingText(rawMessage, context)
  → 返回 FrameworkPacket → 框架发送
```

**你需要实现的接口**：

```csharp
public interface IClientMessageHandler : IMessageHandler
{
    int Priority { get; }                                    // 数值越小越先执行
    bool CanHandleOutgoingText(string rawMessage);           // 出站筛选
    ClientOutgoingMessageResult HandleOutgoingText(          // 出站处理
        string rawMessage, ClientFrameworkContext context);
    bool CanHandleIncomingMessage(FrameworkPacket message);  // 入站筛选
    ClientIncomingMessageResult HandleIncomingMessage(       // 入站处理
        FrameworkPacket message, ClientFrameworkContext context);
}
```

**配套角色**：

| 接口 | 何时执行 | 用途 |
|------|----------|------|
| `IMessageInterceptor` | 消息渲染为 `FrameworkDisplayMessage` 后、显示前 | 过滤/修改展示消息 |
| `IMessageRenderer` | `FrameworkPacket` → `FrameworkDisplayMessage` 转换 | 自定义消息渲染 |

**注册方式**：

```csharp
builder.AddClientMessageHandler(this);
builder.AddMessageInterceptor(this);
builder.AddMessageRenderer(this);
```

### 6.2 Command 管线 ✅ 完整可用

**职责**：传输"系统应该执行的操作"（Trade 状态同步、历史请求等）。Command 与 Message 的关键区别是：Command 不产生展示产物，它修改内部状态，可能间接触发后续 Message。

**入站**（Server → Client）：

```
FrameworkPacket (Kind="command")
  → packetHandler 分支 KindCommand
  → IClientCommandHandler 链（按 Priority 排序）
  → CanHandleIncomingCommand(command) → HandleIncomingCommand(command, context)
```

**出站**（Client → Server）：

```
插件构造 FrameworkPacket
  → IFrameworkClientCommandTransport.TryHandleOutgoingCommand(command)
  → IClientOutgoingCommandHandler 链（按 Priority 排序）
  → CanHandleOutgoingCommand(command) → HandleOutgoingCommand(command, context)
  → 返回 FrameworkPacket → 框架发送
```

**你需要实现的接口**：

入站处理：
```csharp
public interface IClientCommandHandler : ICommandHandler
{
    int Priority { get; }
    bool CanHandleIncomingCommand(FrameworkPacket command);
    ClientIncomingCommandResult HandleIncomingCommand(
        FrameworkPacket command, ClientFrameworkContext context);
}
```

出站处理（接口定义于 [FrameworkTypes.cs:562-572](Common/Utils/Framework/FrameworkTypes.cs#L562-L572)）：
```csharp
public interface IClientOutgoingCommandHandler : ICommandHandler
{
    bool CanHandleOutgoingCommand(FrameworkPacket command);
    ClientOutgoingCommandResult HandleOutgoingCommand(
        FrameworkPacket command, ClientFrameworkContext context);
}
```

**注册方式**：

```csharp
builder.AddClientCommandHandler(this); // 覆盖入站和出站（如果类实现了两个接口）
```

参考实现：[BuiltInTradeClientExtension.cs:60](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L60) 同时实现了 `IClientCommandHandler` 和 `IClientOutgoingCommandHandler`。

### 6.3 Item 管线 ⚠️ 半成品 / 过渡态

**当前实际状态**（基于 `dev` 分支代码验证）：

Item 管线**不是独立可用的**。以下是事实：

- ❌ Client 端 `packetHandler` **没有** `KindItem` 分支——Item 数据不能独立从服务端路由到客户端
- ❌ Client 端**没有** `IClientIncomingItemHandler` / `IClientOutgoingItemHandler` 接口
- ✅ `IItemCodec` 接口已定义（[FrameworkTypes.cs:530-541](Common/Utils/Framework/FrameworkTypes.cs#L530-L541)）
- ✅ `builder.AddItemCodec()` 注册方法已存在（[FrameworkTypes.cs:415](Common/Utils/Framework/FrameworkTypes.cs#L415)）
- ❌ 但注册的 codec **不被管线消费**——`ProcessIncomingItem` 解码后丢弃结果

**当前实际可用的 Item 传输方式**：

Item 数据通过 **Command 管线嵌套 `FrameworkItemPayload`** 传输。以 Trade 为例：

```
FrameworkPacket (Kind="command")
  └─ PayloadJson = JSON(FrameworkTradeOfferUpdateRequest)
       └─ Items = List<FrameworkItemPayload>
            └─ PayloadBytes = protobuf(FrameworkVanillaItemData)
```

这意味着：
- Item 数据**寄生在 Command 管线内部**
- 你需要实现 `IClientCommandHandler` 来处理 Item 数据
- 没有专用的 interceptor/handler/observer 链供 Item 使用

**给 Submod 开发者当前的建议**：

| 你的场景 | 当前应使用的方案 |
|----------|------------------|
| 传输少量结构化控制指令 | Command 管线——标准、完整可用 |
| 传输带二进制载荷的数据（如物品、生物） | Command 管线嵌套 `FrameworkItemPayload`——这就是 Trade 当前的做法 |
| 服务端需要验证/拦截 Item 数据 | ⚠️ 当前做不到——服务端 Item codec 解码后丢弃，没有拦截链 |
| 想实现一个 codec 就被管线自动路由 | ⚠️ 当前做不到——等 P0 补全后才行 |

**未来演进方向** 🔮：

框架计划补全独立的 Item 管线（P0 阶段），届时：
- 新增 `KindItem` 分包独立路由
- `IItemCodec` 注册后被管线消费
- 服务端三阶段链（Interceptor → Handler → Observer）
- **当前 Command 嵌套方案将保持兼容**

详细分析见 `docs/branch-local/dev/三条Pipeline职责辨析与Item管线补全分析.md`（内部设计参考）。

### 6.4 入站 / 出站流向对照表

| 方向 | Message | Command | Item |
|------|---------|---------|------|
| Server → Client（入站） | `IClientMessageHandler` → `IMessageRenderer` → UI | `IClientCommandHandler` → 内部状态 | ❌ 不存在独立路由 |
| Client → Server（出站） | `TryHandleOutgoingMessage()` → `IClientMessageHandler` 链 | `TryHandleOutgoingCommand()` → `IClientOutgoingCommandHandler` 链 | ❌ 不存在独立路由 |
| 出站管线入口 | `IFrameworkClientTransport` | `IFrameworkClientCommandTransport` | — |
| 出站必须走管线 | ✅ 是（[设计哲学 §3.7]） | ✅ 是 | — |

---

## 7. 接入 UI

### 7.1 添加 Tab

实现 `IMainTabProvider`（定义于 [IMainTabProvider.cs](Client/ClientExtensionAbstractions/UI/IMainTabProvider.cs)）：

```csharp
public interface IMainTabProvider
{
    string TabLabel { get; }    // Tab 标签文字
    float TabOrder { get; }     // 排序，数值越小越靠左
    void Draw(Rect inRect);     // 绘制 Tab 内容
}
```

注册方式：

```csharp
builder.RegisterApi<IMainTabProvider>(this);
```

`TabOrder` 参考值：Chat 使用 `0`（[ChatMainTabProvider.cs:22](Extensions/Chat/Client/ChatMainTabProvider.cs#L22)），Trade 使用 `1`（[TradeMainTabProvider.cs:15](Extensions/Trade/Client/TradeMainTabProvider.cs#L15)）。你的 Submod 可以选一个值排在你希望的位置（如 `0.5` 插在两者之间，或 `2` 排在 Trade 之后）。

### 7.2 添加侧栏

实现 `IServerSidebarProvider`（定义于 [IServerSidebarProvider.cs](Client/ClientExtensionAbstractions/UI/IServerSidebarProvider.cs)）：

```csharp
public interface IServerSidebarProvider
{
    float Order { get; }           // 排序，越小越靠上
    float PreferredWidth { get; }  // 建议宽度（像素）
    void Draw(Rect inRect);        // 绘制侧栏内容
}
```

注册方式同上：`builder.RegisterApi<IServerSidebarProvider>(this)`。

### 7.3 添加角标

实现 `IBadgeProvider`（定义于 [IBadgeProvider.cs](Client/ClientExtensionAbstractions/UI/IBadgeProvider.cs)）：

```csharp
public interface IBadgeProvider
{
    string BadgeText { get; }  // 显示在 Tab 按钮上的角标文字
}
```

- 返回 `null` 或空字符串表示不显示。
- **性能警告**：`BadgeText` 是属性 getter，会在每次 UI 刷新时被调用。不要在 getter 里做计算——用字段缓存，在数据变更时更新。详见 [§11.4](#114-draw-路径上的对象分配)。

注册方式同上：`builder.RegisterApi<IBadgeProvider>(this)`。

### 7.4 添加设置面板

实现 `IClientSettingsPanelProvider`（定义于 [IClientExtensionAbstractions.cs:182-195](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L182-L195)）：

```csharp
public interface IClientSettingsPanelProvider
{
    string SectionId { get; }    // 分组标识，推荐 "plugin.category"
    float Order { get; }         // 显示顺序。host 核心设置在 0-100，插件设置在 100+
    void DrawSettings(Listing_Standard listing, IClientSettingsContext settings);
    bool IsVisible(IClientSettingsContext settings);
}
```

注册方式：`builder.RegisterApi<IClientSettingsPanelProvider>(this)`。

完整示例参考 Chat 的实现：[ChatSettingsPanelProvider.cs](Extensions/Chat/Client/ChatSettingsPanelProvider.cs) 和 Trade 的实现：[TradeSettingsPanelProvider.cs](Extensions/Trade/Client/TradeSettingsPanelProvider.cs)。

### 7.5 设置迁移（Legacy Settings）

如果你的 Submod 需要从旧版 Phinix 的扁平 key 迁移设置到新的命名空间 key，同时实现 `IClientLegacySettingsMigrator`（定义于 [IClientExtensionAbstractions.cs:132-135](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L132-L135)）：

```csharp
public interface IClientLegacySettingsMigrator
{
    bool TryMigrateLegacySettings(IClientSettingsContext settings,
        IReadOnlyDictionary<string, string> legacyValues);
}
```

注册方式：`builder.RegisterApi<IClientLegacySettingsMigrator>(this)`。

Host 在设置窗口首次打开时会调用所有注册的 migrator。参考 [ChatSettingsPanelProvider.cs:53-67](Extensions/Chat/Client/ChatSettingsPanelProvider.cs#L53-L67)。

### 7.6 推送显示消息

如果你的 Submod 需要向消息队列注入通知（不是走 Message 管线从服务端来的消息，而是本地生成的通知），使用 `IDisplayMessageSink`（定义于 [IClientExtensionAbstractions.cs:171-175](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L171-L175)）：

```csharp
public interface IDisplayMessageSink
{
    void Enqueue(FrameworkDisplayMessage message);
}
```

这个服务在 `Activate()` 中通过 `hostContext.GetRequiredService<IDisplayMessageSink>()` 获取。

---

## 8. Host 提供的通用服务

以下服务在 `Activate(ExtensionHostContext hostContext)` 中通过 `hostContext.GetRequiredService<T>()` 获取。

> **关于 Register 阶段使用服务的说明**：当前 host（Client.cs）在构建 `ExtensionHostContext` 时将全部服务注入完成后才调用 `DisoverExtensions` → `Register`，因此 `Register()` 阶段服务实际上已就绪。**当前官方扩展（Chat/Trade）在 `Register()` 中大量使用 `builder.HostContext.GetRequiredService<T>()`。** 推荐做法仍是把需要 host 服务的初始化移到 `Activate()` 中——仅注册 handler/API 留在 `Register()`。后续版本将约束此边界。

### 8.1 IClientSessionContext

提供当前会话的认证与登录状态。定义于 [IClientExtensionAbstractions.cs:67-75](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L67-L75)：

```csharp
public interface IClientSessionContext
{
    bool Authenticated { get; }   // 是否已认证
    bool LoggedIn { get; }        // 是否已登录
    string SessionId { get; }     // 当前会话 ID
    string Uuid { get; }          // 当前玩家的 UUID
}
```

### 8.2 IClientSettingsContext

读写客户端设置。定义于 [IClientExtensionAbstractions.cs:78-93](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L78-L93)：

```csharp
public interface IClientSettingsContext
{
    T Get<T>(string key, T defaultValue = default);
    void Set<T>(string key, T value);
    IEnumerable<string> BlockedUsers { get; }
    bool CollapseBlockedUsers { get; set; }
    void BlockUser(string uuid);
    void UnBlockUser(string uuid);
    event Action<string, object> OnSettingChanged;  // key 和 newValue
}
```

**约定**：key 使用 `"plugin.category.settingName"` 格式（如 `"chat.display.showNameFormatting"`），避免与 host 或其他插件冲突。

`OnSettingChanged` 事件可用于实时响应设置变化。参考 [BuiltInTradeClientExtension.cs:129-136](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L129-L136)。

### 8.3 IClientUserDirectory

查询在线和已知用户：

```csharp
public interface IClientUserDirectory
{
    string Uuid { get; }                                  // 当前用户 UUID
    ImmutableUser[] GetUsers(bool loggedIn = false);      // loggedIn=true 仅在线用户
    bool TryGetUser(string uuid, out ImmutableUser user);
}
```

### 8.4 IClientUserEventStream

订阅用户相关事件：

```csharp
public interface IClientUserEventStream
{
    event EventHandler Disconnected;                                    // 连接断开
    event EventHandler UsersChanged;                                    // 用户列表变更
    event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;
    event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;
}
```

**重要**：`Activate()` 中 `+=` 的事件，必须在 `Shutdown()` 中 `-=` 掉。

### 8.5 IClientMainThreadDispatcher

从网络回调线程封送操作到主（UI）线程：

```csharp
public interface IClientMainThreadDispatcher
{
    void Enqueue(Action action);
}
```

**任何操作 UI 或共享状态的代码，如果可能在网络线程上被调用，必须通过此接口封送。** 网络回调（`OnNetworkReceive` 等）在 poll 线程上触发——直接修改 UI 状态会导致竞态和崩溃。

### 8.6 IClientWindowService

打开宿主级窗口：

```csharp
public interface IClientWindowService
{
    void Open(Window window);
    void OpenSettingsWindow();
}
```

`OpenSettingsWindow()` 打开宿主设置窗口——所有 `IClientSettingsPanelProvider` 的绘制在此窗口中聚合。

### 8.7 IClientSoundService

在 UI 线程上播放音效：

```csharp
public interface IClientSoundService
{
    void Enqueue(SoundDef soundDef);
}
```

使用队列模式——这不是立刻播放，而是在下一帧 UI 更新时播放。参考 [BuiltInChatClientExtension.cs:120](Extensions/Chat/Client/BuiltInChatClientExtension.cs#L120)。

### 8.8 IFrameworkClientTransport

消息管线（出站）的入口。参见 [§6.1](#61-message-管线-完整可用)：

```csharp
public interface IFrameworkClientTransport
{
    bool HasRemoteCapability(string capability);
    void SendFrameworkPacket(FrameworkPacket packet);          // ⚠️ 受限制：正交通信应走 TryHandle
    bool TryHandleOutgoingMessage(string rawMessage);         // ✅ 推荐的出站入口
}
```

> **关于 `SendFrameworkPacket`**：此方法直接发送 FrameworkPacket 而不走 handler 管线。根据设计哲学 §3.7，插件不应绕过管线——正交通信使用 `TryHandleOutgoingMessage` / `TryHandleOutgoingCommand`。

### 8.9 IFrameworkClientCommandTransport

命令管线（出站）的入口。参见 [§6.2](#62-command-管线-完整可用)：

```csharp
public interface IFrameworkClientCommandTransport
{
    bool TryHandleOutgoingCommand(FrameworkPacket command);
}
```

### 8.10 IFrameworkClientLifecycle

获取当前兼容模式和订阅模式切换：

```csharp
public interface IFrameworkClientLifecycle
{
    FrameworkCompatibilityMode CompatibilityMode { get; }
    event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged;
}
```

`FrameworkCompatibilityMode` 枚举值为 `FrameworkV2` 或 `Legacy`。如果你的 Submod 需要在 V2 模式下才工作，检查此值即可。如果你的 Submod 需要支持 Legacy 模式，详见 [§10](#10-兼容模式与legacy)。

### 8.11 ILegacyModuleTransport

⚠️ **仅供 Legacy 适配使用。** 新 Submod 不应使用此接口。

```csharp
public interface ILegacyModuleTransport
{
    void Send(string moduleName, byte[] data);
    void RegisterHandler(string moduleName, RawPacketHandlerDelegate handler);
    void UnregisterHandler(string moduleName);
}
```

这是直接操作 `NetClient` 的原始模块通信能力。新 Submod 的正交通信应在 Message/Command 管线内完成。

### 8.12 IClientDisplayMessageFeed / IClientDisplayMessageStore

消息流订阅与持久化存储：

```csharp
public interface IClientDisplayMessageFeed
{
    event EventHandler<FrameworkDisplayMessageEventArgs> DisplayMessageReceived;
}

public interface IClientDisplayMessageStore
{
    int UnreadMessages { get; }
    void MarkAsRead();
    FrameworkDisplayMessage[] GetUnreadDisplayMessages(bool markAsRead = true);
    FrameworkDisplayMessage[] GetDisplayMessages();
}
```

如果你需要在新消息到达时触发通知（如播放音效），订阅 `DisplayMessageReceived`。Chat 扩展对此的使用见 [BuiltInChatClientExtension.cs:110-126](Extensions/Chat/Client/BuiltInChatClientExtension.cs#L110-L126)。

### 8.13 IExtensionStorageProvider

插件可以获取专属的文件存储路径：

```csharp
hostContext.GetStoragePath("my.extension.id", "settings.json");
// 返回类似 "framework-extensions/client/my.extension.id/settings.json"
```

实现代码见 [FrameworkTypes.cs:288-318](Common/Utils/Framework/FrameworkTypes.cs#L288-L318)（`FileSystemExtensionStorageProvider`）。

### 8.14 日志

通过 `hostContext.Log` 回调记录日志：

```csharp
hostContext.Log?.Invoke("Something happened", LogLevel.INFO);
```

> **当前约定**：官方扩展（Chat/Trade）使用 `hostContext.Log`（`Action<string, LogLevel>`）上报日志。`ILoggable` 接口目前是 host 内部组件（`NetClient`、`PhinixFrameworkClient` 等）使用的日志产生端契约，尚未对插件直接暴露。后续将迁移到扩展级别的 `ILoggable` 支持。

---

## 9. 插件间协作

### 9.1 推荐方式：直接引用 Contracts 程序集

Chat 和 Trade 都提供了独立的 Contracts 工程，只包含接口定义和协议常量。你可以直接引用它们：

```csharp
// 你的 Submod 中
using Phinix.TradeExtension;  // 引用 TradeExtension Contracts 程序集

public void Register(IExtensionBuilder builder)
{
    // 在 Activate 中解析
}

public void Activate(ExtensionHostContext hostContext)
{
    if (hostContext.TryResolveApi<ITradeRequestApi>(out var tradeApi))
    {
        // 调用 Trade 的能力
        tradeApi.CreateTrade("some-player-uuid");
    }
}
```

**为什么推荐直接引用而不是纯 API registry 解析？**
- 编译时类型安全——不用维护两份接口定义
- IDE 支持完整（自动补全、跳转定义）
- Contracts 程序集只包含接口，不包含实现，不违反分层原则

### 9.2 API Registry 方式（弱依赖）

如果你的 Submod **可选的**需要其他插件的能力（对方可能未安装），使用 API registry：

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    if (hostContext.TryResolveApi<ITradeRequestApi>(out var tradeApi))
    {
        _tradeApi = tradeApi;  // Trade 存在
    }
    // Trade 不存在则优雅降级
}
```

### 9.3 不要通过 host 中转

**反模式**：

```csharp
// ❌ 错误：要求 host 为你的插件提供专用桥接
// 这不是 host 的职责。host 只做通用服务。
public interface IMyPluginBridge { void DoSomething(); }
// 然后期望 host 注入它
```

**正确做法**：直接在插件间建立引用关系。框架只提供 API registry 这种发现机制——不充当业务中介。

### 9.4 插件间消息协作

如果插件 A 想监听插件 B 的消息：

- A 引用 B 的 Contracts，知道 B 的 `MessageType` 常量
- A 注册自己的 `IClientMessageHandler` 或 `IClientCommandHandler`，Priority 合适（在 B 之前拦截，或在 B 之后观察）
- 在 `CanHandleIncomingMessage` 中检查 `message.MessageType`
- 在 `HandleIncomingMessage` 中处理，返回 `Action = Continue` 让管线继续

---

## 10. 兼容模式与 Legacy

### 10.1 两种兼容模式

Phinix 可以在两种模式下运行：

| 模式 | 值 | 说明 |
|------|-----|------|
| `FrameworkV2` | 1 | 新版 Framework 协议服务器——正常模式 |
| `Legacy` | 2 | 旧版 Phinix 服务器——需要 LegacyAdapter 做协议翻译 |

通过 `IFrameworkClientLifecycle.CompatibilityMode` 获取当前模式。

### 10.2 Legacy Adapter 如何工作

`LegacyAdapter` 在 `Priority=500` 运行，高于 Chat(1000) 和 Trade(1100)。当检测到 `Legacy` 模式时：
- 它注册自己的 `ILegacyModuleTransport` handler
- 拦截出站 Message 和 Command，翻译为旧协议格式
- 入站旧协议消息转换为 `FrameworkDisplayMessage` 后注入 `IDisplayMessageSink`

代码见：[BuiltInLegacyAdapterClientExtension.cs](Extensions/LegacyAdapter/Client/BuiltInLegacyAdapterClientExtension.cs)。

### 10.3 新 Submod 的兼容建议

**如果你的 Submod 只支持 FrameworkV2**（推荐）：

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    _lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
    _lifecycle.CompatibilityModeChanged += OnModeChanged;

    if (_lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
    {
        StartWorking();
    }
}

private void OnModeChanged(object sender, FrameworkCompatibilityModeChangedEventArgs e)
{
    if (e.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
        StartWorking();
    else
        StopWorking();
}
```

**如果你需要支持 Legacy 模式**：
- 研究 `LegacyAdapter` 的做法
- 你的出站数据需要通过 LegacyAdapter 翻译（它会自动拦截 Priority=500 以上的 handler）
- 入站数据可能需要从 `IDisplayMessageSink` 中解析而非从 FrameworkPacket 直接获取

---

## 11. 常见反模式与踩坑点

### 11.1 绕过管线直连传输层

```csharp
// ❌ 错误：直接发送 FrameworkPacket
hostContext.GetRequiredService<IFrameworkClientTransport>()
    .SendFrameworkPacket(myPacket);
```

**为什么错**：`SendFrameworkPacket` 绕过 handler 管线——其他插件的 interceptor、observer、translator 全部失效。详见设计哲学 §3.7。

```csharp
// ✅ 正确：走管线
hostContext.GetRequiredService<IFrameworkClientCommandTransport>()
    .TryHandleOutgoingCommand(myCommand);
```

### 11.2 在 Register() 里调用 hostContext.GetRequiredService

```csharp
public void Register(IExtensionBuilder builder)
{
    // ❌ 错误：Register 阶段 host 服务可能尚未就绪
    var session = builder.HostContext.GetRequiredService<IClientSessionContext>();
}
```

**正确做法**：`Register()` 只做注册；需要 host 服务的初始化放在 `Activate()` 中。

### 11.3 忘记在 Shutdown() 中取消事件订阅

```csharp
public void Activate(ExtensionHostContext hostContext)
{
    _userEvents = hostContext.GetRequiredService<IClientUserEventStream>();
    _userEvents.UsersChanged += OnUsersChanged;  // += 加了
}

public void Shutdown(ExtensionHostContext hostContext)
{
    // ❌ 忘记 -= ！内存泄漏和幽灵回调
}
```

**规则**：`Activate()` 中每一个 `+=` 必须在 `Shutdown()` 中有对应的 `-=`。参考 [BuiltInTradeClientExtension.cs:157-180](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L157-L180) 的标准写法。

### 11.4 Draw 路径上的对象分配

RimWorld 的 IMGUI 每帧调用 `DoWindowContents` / `Draw` / `DoButton`。在这些路径上分配新对象（`new`）会触发 GC，累积导致帧率下降：

```csharp
// ❌ 错误：每帧 new Regex、new GUIContent、new List
public void Draw(Rect inRect)
{
    var regex = new Regex(@"<[^>]+>");        // 每帧分配！
    var content = new GUIContent("hello");    // 每帧分配！
    var items = messages.Where(m => m.IsNew).ToList(); // LINQ 分配！
}

// ✅ 正确：缓存
private static readonly Regex TagRegex = new Regex(@"<[^>]+>",
    RegexOptions.Compiled);  // static readonly 编译一次
private GUIContent _cachedContent;
private bool _dirty = true;  // 脏标记，仅在数据变更时重新计算
```

详见设计哲学 §8.3。

### 11.5 网络回调线程上操作 UI

```csharp
// ❌ 错误：在 IClientCommandHandler.HandleIncomingCommand 中直接操作 UI
// HandleIncomingCommand 在 poll 线程上被调用！
public ClientIncomingCommandResult HandleIncomingCommand(...)
{
    _myWindow.SomeState = newValue;  // 竞态！
}

// ✅ 正确：封送到主线程
public ClientIncomingCommandResult HandleIncomingCommand(...)
{
    _dispatcher.Enqueue(() => _myWindow.SomeState = newValue);
}
```

### 11.6 静默吞异常

```csharp
// ❌ 错误
try { DoSomething(); } catch { }

// ❌ 仍然错误：只记录 Message 丢弃堆栈
try { DoSomething(); } catch (Exception ex) { Log(ex.Message); }

// ✅ 正确：保留堆栈，使用框架日志
try { DoSomething(); } catch (Exception ex) {
    hostContext.Log?.Invoke($"DoSomething failed: {ex}", LogLevel.ERROR);
}
```

### 11.7 未实现 IDisposable

如果你的模块持有 `Timer`、`FileStream`、`Thread` 等需要释放的资源：

```csharp
public sealed class MyExtension : IActivatablePhinixExtensionModule, IDisposable
{
    private Timer _timer;

    public void Activate(ExtensionHostContext ctx) { _timer = new Timer(...); }
    public void Shutdown(ExtensionHostContext ctx) { Dispose(); }
    public void Dispose() { _timer?.Dispose(); _timer = null; }
}
```

### 11.8 依赖 DLL 加载顺序

RimWorld 的 `ModAssemblyHandler` 按文件名字符串序加载 DLL。如果你的 `13-MySubmod.dll` 依赖 `08-ChatExtension.dll` 中的类型，但文件名按字符串序排在 Chat 前面——加载时会失败。

**规则**：你的数字前缀必须大于所有依赖方的数字前缀。详见 §12.7。

---

## 12. 最小可行示例

> **⚠️ 提示**：当前仓库中**没有**第三方 Submod 的完整示例工程。以下骨架代码是本文档作者基于框架代码和官方扩展的实现模式提取的。

### 12.1 环境准备与先决条件

**客户端**：

- 需要 `GameDlls/` 目录中有 RimWorld 1.6 的程序集（`Assembly-CSharp.dll`、`UnityEngine.dll`、`UnityEngine.CoreModule.dll`、`UnityEngine.IMGUIModule.dll`、`UnityEngine.TextRenderingModule.dll`）
- 需要解决方案中的以下工程在你的 `.csproj` 中作为 `ProjectReference`：
  - `Common/Utils/Utils.csproj`
  - `Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj`
  - `Common/UserManagement/UserManagement.csproj`
- 可选：
  - `Extensions/Chat/Contracts/ChatExtension.csproj`（如果需要调用 Chat API）
  - `Extensions/Trade/Contracts/TradeExtension.csproj`（如果需要调用 Trade API）

### 12.2 目录结构

推荐的工程目录结构（如果放在 Phinix 解决方案外部）：

```
MySubmod/
  Source/
    MySubmodExtension.cs      ← 扩展入口
    MySubmodMessageHandler.cs  ← 你的 Message handler
    MySubmodSettingsPanel.cs   ← 设置面板
    ...
  MySubmod.csproj
```

如果放在 Phinix 解决方案内作为工程引用（推荐，便于调试）：

```
Phinix-Rework/
  Extensions/
    MySubmod/
      Client/
        MySubmod.Client.csproj
        MySubmodExtension.cs
        ...
```

### 12.3 工程配置

最小 `.csproj` 骨架（客户端，.NET Framework 4.7.2）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <OutputType>Library</OutputType>
    <RootNamespace>MyMod.PhinixExtension</RootNamespace>
    <AssemblyName>MyMod.PhinixExtension</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>

  <!-- RimWorld 程序集引用（同标准 Mod 工程） -->
  <Choose>
    <When Condition="Exists('$(SolutionDir)\GameDlls\1.6')">
      <PropertyGroup><RimWorldDepDir>$(SolutionDir)\GameDlls\1.6</RimWorldDepDir></PropertyGroup>
    </When>
    <Otherwise>
      <PropertyGroup><RimWorldDepDir>$(SolutionDir)\GameDlls</RimWorldDepDir></PropertyGroup>
    </Otherwise>
  </Choose>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(RimWorldDepDir)\Assembly-CSharp.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(RimWorldDepDir)\UnityEngine.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(RimWorldDepDir)\UnityEngine.CoreModule.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>$(RimWorldDepDir)\UnityEngine.IMGUIModule.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <!-- 框架核心依赖 -->
    <ProjectReference Include="..\..\Common\Utils\Utils.csproj">
      <Name>Utils</Name>
    </ProjectReference>
    <ProjectReference Include="..\..\Client\ClientExtensionAbstractions\ClientExtensionAbstractions.csproj">
      <Name>ClientExtensionAbstractions</Name>
    </ProjectReference>
    <ProjectReference Include="..\..\Common\UserManagement\UserManagement.csproj">
      <Name>UserManagement</Name>
    </ProjectReference>

    <!-- 可选：如果需要调用 Trade API -->
    <!-- <ProjectReference Include="..\Trade\Contracts\TradeExtension.csproj">
      <Name>TradeExtension</Name>
    </ProjectReference> -->
  </ItemGroup>

  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />

  <!-- 构建后复制到 Extensions 目录 -->
  <Target Name="AfterBuild">
    <MakeDir Directories="$(SolutionDir)\Output\Client\Common\Extensions" />
    <Copy SourceFiles="$(TargetDir)$(AssemblyName).dll"
          DestinationFiles="$(SolutionDir)\Output\Client\Common\Extensions\12-$(AssemblyName).dll" />
  </Target>
</Project>
```

> **注意**：如果你的 `.csproj` 引用路径指向 Phinix 解决方案内的工程，相对路径需要根据你的实际目录结构调整。上面的路径假设你的工程放在 `Extensions/MySubmod/Client/` 下。

### 12.4 扩展入口类完整代码

下面是一个最小可行 Submod 的完整代码骨架。它：
- 注册一个 Message handler（打印日志）
- 注册一个设置面板
- 在 Activate 中订阅事件，在 Shutdown 中取消

```csharp
using System;
using PhinixClient;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using Verse;

namespace MyMod.PhinixExtension
{
    [PhinixExtension("mymod.myfeature")]
    public sealed class MySubmodExtension :
        IPhinixExtensionModule,
        IActivatablePhinixExtensionModule,
        IClientMessageHandler
    {
        private IFrameworkClientLifecycle _lifecycle;
        private IClientSettingsContext _settings;
        private IClientUserEventStream _userEvents;
        private IClientMainThreadDispatcher _dispatcher;
        private Action<string, LogLevel> _log;

        // 事件处理器引用——缓存在字段里，保证 -= 时引用匹配
        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> _modeChangedHandler;
        private EventHandler _usersChangedHandler;

        // ===== IPhinixExtension =====

        public string ExtensionId => "mymod.myfeature";

        // ===== IMessageHandler =====

        public int Priority => 1500; // 在 Chat(1000) 和 Trade(1100) 之后

        // ===== IPhinixExtensionModule =====

        public void Register(IExtensionBuilder builder)
        {
            // 只做注册——不获取 host 服务
            builder.AddClientMessageHandler(this);

            // 注册设置面板
            builder.RegisterApi<IClientSettingsPanelProvider>(
                new MySettingsPanelProvider());

            // 注册能力声明
            builder.AddCapabilityProvider(new MyCapabilityProvider());
        }

        // ===== IActivatablePhinixExtensionModule =====

        public void Activate(ExtensionHostContext hostContext)
        {
            // 获取需要的 host 服务
            _lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            _settings = hostContext.GetRequiredService<IClientSettingsContext>();
            _userEvents = hostContext.GetRequiredService<IClientUserEventStream>();
            _dispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            _log = hostContext.Log;

            // 订阅事件——务必缓存 handler 引用
            _modeChangedHandler = (_, args) =>
            {
                if (args.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                {
                    _log?.Invoke("[MySubmod] FrameworkV2 mode active.", LogLevel.INFO);
                }
            };
            _lifecycle.CompatibilityModeChanged += _modeChangedHandler;

            _usersChangedHandler = (_, __) =>
            {
                _log?.Invoke("[MySubmod] Users changed.", LogLevel.DEBUG);
            };
            _userEvents.UsersChanged += _usersChangedHandler;

            _log?.Invoke("[MySubmod] Activated.", LogLevel.INFO);
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            // 取消所有事件订阅
            if (_lifecycle != null && _modeChangedHandler != null)
                _lifecycle.CompatibilityModeChanged -= _modeChangedHandler;

            if (_userEvents != null && _usersChangedHandler != null)
                _userEvents.UsersChanged -= _usersChangedHandler;

            _log?.Invoke("[MySubmod] Shut down.", LogLevel.INFO);
        }

        // ===== IClientMessageHandler =====

        public bool CanHandleOutgoingText(string rawMessage)
        {
            // 不处理出站——交给 Chat
            return false;
        }

        public ClientOutgoingMessageResult HandleOutgoingText(
            string rawMessage, ClientFrameworkContext context)
        {
            return null; // 不会被调用（CanHandle 返回 false）
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            // 观察所有 message 类型的消息（可以按 MessageType 过滤）
            return message != null && message.MessageType != null;
        }

        public ClientIncomingMessageResult HandleIncomingMessage(
            FrameworkPacket message, ClientFrameworkContext context)
        {
            // 只观察，不拦截——返回 Continue 让管线继续
            _log?.Invoke(
                $"[MySubmod] Observed message: type={message.MessageType}, " +
                $"from={context.SenderUuid}",
                LogLevel.DEBUG);

            return new ClientIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Continue
            };
        }
    }

    // ===== 设置面板提供者 =====

    internal sealed class MySettingsPanelProvider : IClientSettingsPanelProvider
    {
        public string SectionId => "mymod.general";
        public float Order => 200f;
        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            bool mySetting = settings.Get("mymod.mySetting", true);
            listing.CheckboxLabeled("My Feature Enabled", ref mySetting);
            settings.Set("mymod.mySetting", mySetting);
        }
    }

    // ===== 能力声明 =====

    internal sealed class MyCapabilityProvider : ICapabilityProvider
    {
        public System.Collections.Generic.IEnumerable<string> GetCapabilities()
        {
            yield return "mymod.myfeature.v1";
        }
    }
}
```

### 12.5 可选：注册领域 Contracts 工程

如果你的 Submod 有对外接口需要供其他 Submod 调用，建议像 Chat 和 Trade 一样拆分一个独立的 Contracts 工程。该工程只包含接口和常量：

```
Extensions/
  MySubmod/
    Contracts/
      MySubmod.csproj          ← 仅接口 + 常量，无实现
      IMyFeatureApi.cs
      MyFeatureProtocol.cs     ← MessageType 常量
    Client/
      MySubmod.Client.csproj   ← 实现层，引用 Contracts
      MySubmodExtension.cs
```

其他 Submod 就可以安全地引用 `MySubmod/Contracts/MySubmod.csproj` 而不依赖你的实现细节。

### 12.6 构建与部署

1. 在 Visual Studio 或 `dotnet build` 中编译
2. DLL 文件放入 `Output/phinix-rework/Common/Extensions/` 目录
3. 确保文件名带有正确的数字前缀（见 §12.7）
4. 启动 RimWorld 并启用 Phinix——你的 Submod 应被自动发现

宿主启动时的日志输出可以帮助确认加载状态：
```
[Phinix] Framework module 'mymod.myfeature' registered from 'MyMod.PhinixExtension.MySubmodExtension' ...
[Phinix] Framework module 'mymod.myfeature' activated for host 'client'.
```

### 12.7 加载顺序号解析

`Extensions/` 目录下 DLL 的文件名前缀（如 `08-`、`11-`）决定 RimWorld 的加载顺序。当前框架基础 DLL 的编号分配如下（参考 [设计哲学 §5.1](设计哲学.md#51-命名与排序)）：

| 前缀 | 程序集 | 内容 |
|------|--------|------|
| 01-02 | LiteNetLib, Protobuf | 第三方库 |
| 03 | Utils | `IPhinixExtensionModule`、Framework 基础 |
| 04 | Connections | 网络层 |
| 05 | Authentication | 认证 |
| 06 | UserManagement | 用户管理 |
| 07 | ClientExtensionAbstractions | UI 接口、host 服务接口 |
| 08 | ChatExtension | Chat 领域 Contracts |
| 09 | TradeExtension | Trade 领域 Contracts |
| 10 | ChatExtension.Client | Chat 插件（依赖 03,07,08） |
| 11 | TradeExtension.Client | Trade 插件（依赖 03,07,09） |

你的 Submod DLL 前缀应**大于它所依赖的所有程序集**。例如：
- 只依赖 03 + 07 → 前缀 ≥ 12
- 依赖 08（Chat Contracts）→ 前缀 ≥ 12（因为 08 已存在，你的 DLL 必须排在 Chat Contracts 之后，但 Chat.Client(10) 在你的 DLL 之前或之后不影响你引用 Chat Contracts）

### 12.8 调试提示

- **加载问题**：检查 RimWorld 控制台日志，搜索 `[Phinix]` 关键词，观察扩展发现/注册/激活的诊断输出。
- **DLL 未发现**：检查 DLL 是否在 `ExtensionAssemblyLoader` 的 probe 目录中，文件名是否以 `.dll` 结尾。
- **类型加载异常**（`ReflectionTypeLoadException`）：通常是依赖的 DLL 不存在或版本不匹配——检查所有 ProjectReference 是否都已放置到 Extensions 目录。
- **Activate 未调用**：确认模块同时实现了 `IPhinixExtensionModule` 和 `IActivatablePhinixExtensionModule`。
- **UI 不显示**：确认 `RegisterApi<IMainTabProvider>` 在 `Register()` 中调用；检查 `TabOrder` 是否与其他 Tab 冲突。

---

## 附录 A：IExtensionBuilder 全部注册方法速查表

| 方法 | 参数类型 | 用途 | 当前状态 |
|------|----------|------|----------|
| `AddCapabilityProvider` | `ICapabilityProvider` | 声明支持的能力 | ✅ |
| `AddMessageInterceptor` | `IMessageInterceptor` | 展示消息拦截 | ✅ |
| `AddMessageRenderer` | `IMessageRenderer` | 消息渲染器 | ✅ |
| `AddClientMessageHandler` | `IClientMessageHandler` | 客户端消息处理（入站+出站） | ✅ |
| `AddClientCommandHandler` | `IClientCommandHandler` | 客户端命令处理（入站） | ✅ |
| `AddServerMessageHandler` | `IServerMessageHandler` | 服务端消息处理 | ✅（仅服务端） |
| `AddServerInboundMessageInterceptor` | `IServerInboundMessageInterceptor` | 服务端消息拦截 | ✅（仅服务端） |
| `AddServerDefaultMessageHandler` | `IServerDefaultMessageHandler` | 服务端默认消息处理 | ✅（仅服务端） |
| `AddServerMessageObserver` | `IServerMessageObserver` | 服务端消息观察 | ✅（仅服务端） |
| `AddItemCodec` | `IItemCodec` | 注册物品编解码器 | ⚠️ 接口已定义，注册有效但管线不消费 |
| `AddServerCommandHandler` | `IServerCommandHandler` | 服务端命令处理 | ✅（仅服务端） |
| `AddServerInboundCommandInterceptor` | `IServerInboundCommandInterceptor` | 服务端命令拦截 | ✅（仅服务端） |
| `AddServerDefaultCommandHandler` | `IServerDefaultCommandHandler` | 服务端默认命令处理 | ✅（仅服务端） |
| `AddServerCommandObserver` | `IServerCommandObserver` | 服务端命令观察 | ✅（仅服务端） |
| `AddServerOutboundPacketInterceptor` | `IServerOutboundPacketInterceptor` | 服务端出站拦截 | ✅（仅服务端） |
| `RegisterApi<T>` | `T` 实现 | 暴露 API | ✅ |
| `TryResolveApi<T>` | out `T` | 解析单个 API | ✅ |
| `ResolveApis<T>` | — | 解析所有 API | ✅ |

> **状态符号**：✅ = 完整可用 | ⚠️ = 半成品/过渡态 | 🔮 = 计划中

## 附录 B：ExtensionHostContext 全部服务速查表

以下服务在 `Activate()` 中通过 `hostContext.GetRequiredService<T>()` 获取：

| 服务接口 | 用途 | 定义位置 |
|----------|------|----------|
| `IFrameworkClientTransport` | 消息管线出站入口 | [IClientExtensionAbstractions.cs:9-21](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L9-L21) |
| `IFrameworkClientCommandTransport` | 命令管线出站入口 | [IClientExtensionAbstractions.cs:23-31](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L23-L31) |
| `IClientDisplayMessageStore` | 消息持久化存储 | [IClientExtensionAbstractions.cs:34-43](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L34-L43) |
| `IClientDisplayMessageFeed` | 消息流事件订阅 | [IClientExtensionAbstractions.cs:45-48](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L45-L48) |
| `IFrameworkClientLifecycle` | 兼容模式与协商 | [IClientExtensionAbstractions.cs:60-65](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L60-L65) |
| `IClientSessionContext` | 当前会话状态 | [IClientExtensionAbstractions.cs:67-75](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L67-L75) |
| `IClientSettingsContext` | 读写设置 | [IClientExtensionAbstractions.cs:78-93](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L78-L93) |
| `IClientUserDirectory` | 用户信息查询 | [IClientExtensionAbstractions.cs:95-103](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L95-L103) |
| `IClientUserEventStream` | 用户事件订阅 | [IClientExtensionAbstractions.cs:105-113](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L105-L113) |
| `IClientMainThreadDispatcher` | 主线程封送 | [IClientExtensionAbstractions.cs:115-118](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L115-L118) |
| `IClientWindowService` | 打开窗口 | [IClientExtensionAbstractions.cs:120-125](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L120-L125) |
| `IClientSoundService` | 播放音效 | [IClientExtensionAbstractions.cs:127-130](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L127-L130) |
| `ILegacyModuleTransport` | 原始模块通信 | [IClientExtensionAbstractions.cs:155-165](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L155-L165) |
| `IDisplayMessageSink` | 注入显示消息 | [IClientExtensionAbstractions.cs:171-175](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L171-L175) |
| `UserManager` | 底层用户管理（通过 `AddService` 注入） | [Client/Source/Client.cs](Client/Source/Client.cs) |
| `Action` | 打开设置窗口（同 `IClientWindowService.OpenSettingsWindow`） | [Client/Source/Client.cs:121](Client/Source/Client.cs#L121) |
| `Action<bool>` | 同步 acceptingTrades 状态 | [Client/Source/Client.cs:122](Client/Source/Client.cs#L122) |

> **另外**：`hostContext` 本身还提供 `Log`、`StorageProvider`、`ApiRegistry`（`TryResolveApi` / `ResolveApis`）、`GetStoragePath()` 等方法——见 [FrameworkTypes.cs:320-439](Common/Utils/Framework/FrameworkTypes.cs#L320-L439)。
