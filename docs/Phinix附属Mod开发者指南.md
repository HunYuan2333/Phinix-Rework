# Phinix 附属 Mod 开发者开发指南

> **面向人群**：第三方附属 Mod / Submod 开发者。本文档假定你已经会用 C# 给 RimWorld 写 Mod，但可能是第一次接触 Phinix 框架。
>
> **文档定位**：本文档与 [设计哲学.md](./设计哲学.md) 同为跨分支共享基线文档。前者阐述"为什么这样设计"；本文档告诉你"怎么落地使用"。
>
> **最后更新**：2026-09-13，基于当前最新代码库（dev 分支）编写。框架仍在活跃演进中——本文档中会明确标注每项能力的当前状态：✅ 完整可用、⚠️ 半成品/过渡态、🔮 计划中。

---

## 目录

1. [架构总览](#1-架构总览)
2. [依赖边界](#2-依赖边界)
3. [扩展入口与生命周期](#3-扩展入口与生命周期)
4. [注册表：IExtensionBuilder 能做什么](#4-注册表iextensionbuilder-能做什么)
5. [API 暴露与解析](#5-api-暴露与解析)
6. [三条通信管线](#6-三条通信管线)
7. [接入 UI](#7-接入-ui)
    - [7.1 添加 Tab](#71-添加-tab)
    - [7.2 添加侧栏](#72-添加侧栏)
    - [7.3 响应式布局与适配（ClientExtensionAbstractions 1.1）](#73-响应式布局与适配clientextensionabstractions-11)
        - [7.3.1 响应式布局提示与降级机制](#731-响应式布局提示与降级机制)
        - [7.3.2 宿主侧栏抽屉折叠机制（Sidebar Drawer Collapse）](#732-宿主侧栏抽屉折叠机制sidebar-drawer-collapse)
        - [7.3.3 屏幕安全区与自定义弹窗（UiScreenSafeArea）](#733-屏幕安全区与自定义弹窗uiscreensafearea)
        - [7.3.4 低分配共享布局原语（ClientExtensionAbstractions.UI）](#734-低分配共享布局原语clientextensionabstractionsui)
        - [7.3.5 几何计算与缓存失效原则](#735-几何计算与缓存失效原则)
    - [7.4 添加角标](#74-添加角标)
    - [7.5 添加设置面板](#75-添加设置面板)
    - [7.6 设置迁移（Legacy Settings）](#76-设置迁移legacy-settings)
    - [7.7 推送显示消息](#77-推送显示消息)
    - [7.8 添加顶部横幅通知（INoticeBannerProvider）](#78-添加顶部横幅通知inoticebannerprovider)
    - [7.9 回车键处理（IUiAcceptKeyHandler）](#79-回车键处理iuiacceptkeyhandler)
    - [7.10 UI 主题与配色（IUiTheme）](#710-ui-主题与配色iuitheme)
8. [Host 提供的通用服务](#8-host-提供的通用服务)
9. [插件间协作](#9-插件间协作)
10. [兼容模式与 Legacy](#10-兼容模式与legacy)
11. [常见反模式与踩坑点](#11-常见反模式与踩坑点)
    - [11.1 绕过管线直连传输层](#111-绕过管线直连传输层)
    - [11.2 在 Register() 里调用 hostContext.GetRequiredService](#112-在-register-里调用-hostcontextgetrequiredservice)
    - [11.3 忘记在 Shutdown() 中取消事件订阅](#113-忘记在-shutdown-中取消事件订阅)
    - [11.4 Draw 路径上的对象分配](#114-draw-路径上的对象分配)
    - [11.5 网络回调线程上操作 UI](#115-网络回调线程上操作-ui)
    - [11.6 静默吞异常](#116-静默吞异常)
    - [11.7 未实现 IDisposable](#117-未实现-idisposable)
    - [11.8 依赖 DLL 加载顺序](#118-依赖-dll-加载顺序)
    - [11.9 使用已废弃的旧 GUI 容器（Displayable 系列）](#119-使用已废弃的旧-gui-容器displayable-系列)
    - [11.10 硬编码绝对坐标与忽略屏幕安全区](#1110-硬编码绝对坐标与忽略屏幕安全区)
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

宿主启动时调用 `ExtensionAssemblyLoader.LoadAssemblies()` 扫描探测目录下的 `.dll` 文件（详见 [Client.cs:543-582](Client/Source/Client.cs#L543-L582) 的 `GetExtensionProbeDirectories` 方法）。

当前框架与官方插件的物理发布目录已完成分离（参考 [设计哲学 §5.1 与 §5.2](设计哲学.md#51-命名与排序)）：

```
PhinixMod/
  1.6/
    Assemblies/           ← 客户端专属宿主（13-PhinixClient.dll）
  Common/
    Assemblies/           ← 框架基础 DLL（01-10，包含 LiteNetLib, Protobuf, Utils, Connections, Auth, UserManagement, ClientExtensionAbstractions）
    Extensions/           ← 官方内置插件 DLL（08-16，包含 Chat, Trade, LegacyAdapter, RedPacket, TalentTrade 及其 Client 端实现）
```

#### 第三方 Submod 的两种分发与部署方式

1. **推荐方式：独立 Mod 分发（Steam Workshop / 独立 Mod 文件夹）**
   - 作为标准的 RimWorld Mod 打包分发，无需也不应修改 Phinix Mod 自身的安装目录。
   - 在你的 Mod `About/About.xml` 中将 Phinix 声明为前置依赖（配置 `<modDependencies>` 以及 `<loadAfter><li>hunyuan.phinixrework</li></loadAfter>`）。
   - 编译产物 DLL 直接放在你自己的 Mod 根目录下的 `Assemblies/` 目录中。
   - **原理**：`Client.cs` 中的 `GetExtensionProbeDirectories` 会通过 `ModLister.AllInstalledMods` 自动扫描所有处于激活状态且排在 Phinix 之后的第三方 Mod 的 `Assemblies/` 目录。宿主启动时会自动探测到你的 DLL 并拾取其中的 `[PhinixExtension]` 扩展模块！

2. **一体化/内置方式（直接放入 Phinix 目录）**
   - 将编译产物 DLL 直接复制到 Phinix Mod 的 `Common/Extensions/` 目录中。
   - **注意**：官方内置插件已占用 `08-` 至 `16-` 序号，直接放入 `Common/Extensions/` 的第三方 DLL **必须使用 `17-` 或更大的数字前缀**（如 `17-MySubmod.dll`），否则 RimWorld 的 `ModAssemblyHandler` 可能会先于前置依赖加载你的 DLL 导致类加载异常（详见 [§12.7](#127-加载顺序号解析)）。

ExtensionAssemblyLoader 代码位置：[Common/Utils/Framework/ExtensionAssemblyLoader.cs](Common/Utils/Framework/ExtensionAssemblyLoader.cs)。

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

### 3.3 `[PhinixExtension]` 特性与依赖声明

你的模块类必须标记 `[PhinixExtension("your.id")]`，否则框架的反射扫描找不到你（除非你的类实现了 `IPhinixExtension` 且也被标记为非 abstract，在这种情况下旧的 legacy auto-discovery 路径仍然会拾取它，但框架会输出一条 warning 提示你迁移到 `IPhinixExtensionModule`）。

特性定义于 [FrameworkTypes.cs:495-509](Common/Utils/Framework/FrameworkTypes.cs#L495-L509)，支持显式声明扩展间依赖：

```csharp
[PhinixExtension("mymod.myfeature", DependsOn = new[] { "phinix.chat", "phinix.trade" })]
public class MyExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public string ExtensionId => "mymod.myfeature";
    // ...
}
```

- `ExtensionId`：模块全局唯一标识符（如 `"mymod.myfeature"`）。
- `DependsOn`：可选的字符串数组，声明当前模块依赖的其他扩展 ID。框架内部构建有向无环图（DAG，见 [ExtensionDependencyGraph.cs](Common/Utils/Framework/ExtensionDependencyGraph.cs)）进行拓扑排序，严格保证被依赖项在当前模块之前完成 `Register` 和 `Activate`，并在 `Shutdown` 时逆序执行。
- **循环依赖防护**：若存在相互依赖或环路，DAG 拓扑排序会检测并报错，将环路中的模块标记为 `Failed` 并跳过激活，防止死锁。

### 3.4 完整生命周期与启用策略

框架对扩展的管理分为四个阶段（参考 [PhinixExtensionRegistry.cs](Common/Utils/Framework/PhinixExtensionRegistry.cs) 中 `DiscoverExtensions`、`ActivateExtensions` 和 `ShutdownExtensions` 方法）：

```
1. Discover  ── 反射扫描候选程序集，定位所有标注 [PhinixExtension] 的类
                 ↓
                 咨询 IExtensionActivationPolicy 判定是否启用
                 ├─ 用户主动禁用 ──→ 状态置为 Disabled（跳过后续阶段）
                 └─ 依赖项被禁用 ──→ 状态置为 DependencyDisabled（跳过后续阶段）
                 ↓
2. Register  ── 按 DAG 拓扑排序依次调用已启用模块的 Register(builder)
                 模块在此阶段注册 handler、API、codec、capability
                 注册完成后状态变为 Registered
                 ↓
3. Activate  ── 宿主子系统就绪后，按拓扑序调用 Activate(hostContext)
                 模块在此阶段获取 host 服务、解析依赖 API、订阅事件
                 激活完成后状态变为 Active
                 ↓
4. Shutdown  ── 宿主关闭或重置时，按依赖逆序调用 Shutdown(hostContext)
                 模块在此阶段取消所有事件订阅、释放资源与句柄
                 完成后状态变为 Shutdown
```

- **全量生命周期状态**（定义于 [FrameworkTypes.cs:51-61](Common/Utils/Framework/FrameworkTypes.cs#L51-L61) `ExtensionModuleState` 枚举）：
  - `Discovered`：反射已发现模块类，待实例化与策略检查。
  - `Registered`：已通过实例化并成功执行 `Register(builder)`。
  - `Active`：已成功执行 `Activate(hostContext)`，处于正常运行中。
  - `Failed`：在实例化、`Register`、`Activate` 或 `Shutdown` 中发生未捕获异常，或因依赖成环/缺失而加载失败。
  - `Shutdown`：已安全注销并完成资源释放。
  - `Disabled`：被用户在扩展管理设置中显式禁用，未执行 `Register`。
  - `DependencyDisabled`：本身被启用，但其所依赖的父扩展被禁用，因而连锁跳过。

- **宿主内置管理与可观测性**：
  - 客户端主窗口提供了内置的 `ExtensionManagerTab`（并在 Mod 设置中提供了“扩展管理”面板，Order=50）。
  - 玩家和开发者可实时查看所有扩展的当前状态、版本、来源程序集路径与所属 RimWorld Mod 包 ID，并可直接开关特定扩展。
  - 宿主维护了容量为 300 条的扩展日志环形缓冲区（带 `ExtensionLogVersion` 缓存失效戳），可在该界面中直接查看扩展运行时的诊断日志。

### 3.5 错误隔离

单个模块的 `Register()`、`Activate()`、`Shutdown()` 失败不会阻断无关模块；依赖失败模块的下游会被主动阻断：

- `Register()` 异常被 catch，状态标记为 `Failed`，记录 warning，并撤销该 owner 已写入的注册项
- `Activate()` 异常被 catch，状态标记为 `Failed`，记录 warning，并撤销已注册的 API、handler 和 persistent
- `Shutdown()` 异常同样被隔离
- 必需依赖失败后，下游不会继续注册或激活

这意味着**你的 Submod 不会拖垮整个框架**——但反过来说，框架也不会自动重试你的失败模块。

---

## 4. 注册表：IExtensionBuilder 能做什么

`Register(IExtensionBuilder builder)` 是你与框架交互的核心入口。`builder` 提供以下能力（完整接口定义见 [FrameworkTypes.cs:129-186](Common/Utils/Framework/FrameworkTypes.cs#L129-L186)）：

### 4.1 注册 handler（接入通信管线）

```csharp
// 1. Message 管线（展示消息）
builder.AddClientMessageHandler(this);                  // IClientMessageHandler (入站与出站处理)
builder.AddMessageInterceptor(this);                    // IMessageInterceptor (显示前拦截/修改)
builder.AddMessageRenderer(this);                       // IMessageRenderer (自定义消息渲染转换)

// 2. Command 管线（控制指令）
builder.AddClientCommandHandler(this);                  // IClientCommandHandler (入站处理)
// 若同时实现了 IClientCommandHandler 与 IClientOutgoingCommandHandler，
// AddClientCommandHandler(this) 一次注册即可；出站处理详见 §6.2。

// 3. Item 管线（二进制/物品载荷，✅ 完整可用）
builder.AddItemCodec(this);                             // IItemCodec (编解码器，供 Item 管线解析使用)
builder.AddClientItemHandler(this);                     // IClientIncomingItemHandler (客户端入站处理)
builder.AddClientOutgoingItemHandler(this);             // IClientOutgoingItemHandler (客户端出站处理)

// 4. 其他通用能力声明
builder.AddCapabilityProvider(this);                    // ICapabilityProvider (声明支持的协议特性)

// 5. 服务端扩展专用角色（编写服务端 Submod 时使用）
builder.AddServerMessageHandler(this);                  // IServerMessageHandler
builder.AddServerInboundMessageInterceptor(this);       // IServerInboundMessageInterceptor
builder.AddServerDefaultMessageHandler(this);           // IServerDefaultMessageHandler
builder.AddServerMessageObserver(this);                 // IServerMessageObserver
builder.AddServerCommandHandler(this);                  // IServerCommandHandler
builder.AddServerInboundCommandInterceptor(this);       // IServerInboundCommandInterceptor
builder.AddServerDefaultCommandHandler(this);           // IServerDefaultCommandHandler
builder.AddServerCommandObserver(this);                 // IServerCommandObserver
builder.AddServerItemHandler(this);                     // IServerItemHandler
builder.AddServerInboundItemInterceptor(this);          // IServerInboundItemInterceptor
builder.AddServerDefaultItemHandler(this);              // IServerDefaultItemHandler
builder.AddServerItemObserver(this);                    // IServerItemObserver
builder.AddServerOutboundPacketInterceptor(this);       // IServerOutboundPacketInterceptor
builder.AddConsoleCommandProvider(this);                // IServerConsoleCommandProvider (服务端控制台命令)
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

参考实现：[BuiltInTradeClientExtension.cs:13](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L13) 同时实现了 `IClientCommandHandler` 和 `IClientOutgoingCommandHandler`。

### 6.3 Item 管线 ✅ 完整可用（P0 已完成）

**当前实际状态**（基于 `dev` 分支代码验证，2026-06-21 更新）：

Item 管线**现在是独立可用的**。以下是最新事实：

- ✅ 服务端三段链已就位：`IServerItemInterceptor` → `IServerDefaultItemHandler` → `IServerItemObserver`
- ✅ 客户端 `packetHandler` 已有 `KindItem` 分支——Item 数据可以独立路由
- ✅ `IClientIncomingItemHandler` / `IClientOutgoingItemHandler` 接口已存在（[FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs)）
- ✅ `IItemCodec` 接口已定义（[FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs)）
- ✅ `builder.AddItemCodec()` 注册方法已完整可用，且注册的 codec 被管线直接消费
- ✅ `IFrameworkClientTransport.TryHandleOutgoingItem()` 已存在，用于出站 Item 包统一路由
- ✅ Trade 当前使用的 Command 嵌套路径保持完全兼容

**入站路由**（Server → Client）：

```
NetClient → packetHandler(KindItem) → handleItem(packet)
  → IClientIncomingItemHandler 链（按 Priority 排序）
  → CanHandle(packet) → HandleItem(packet, context)
```

**出站路由**（Client → Server）：

```
Submod → IFrameworkClientTransport.TryHandleOutgoingItem(itemPayload)
  → IClientOutgoingItemHandler 链（按 Priority 排序）
  → HandleOutgoingItem() → FrameworkPacket
  → sendPacket(Flow=Item, Kind=item, PayloadBytes=protobuf)
```

**服务端三段链**：

```
handleItem → ProcessIncomingItem:
  1. IServerItemInterceptor.CanIntercept → InterceptItem()
  2. IServerDefaultItemHandler.CanHandle → HandleItem()
     （遍历 IItemCodecs 按 CodecId 匹配，CanDecode → Decode）
  3. IServerItemObserver.ObserveItem()
```

**当前可用的 Item 传输方式**：

| 方式 | 说明 | 适用场景 |
|--------|-------------|----------|
| 独立 `KindItem` 包 | 走 Item 管线的直接路由 | 新的 Submod、二进制载荷数据（物品、生物等） |
| Command 嵌套 `FrameworkItemPayload` | Item 数据寄生于 Command 管线 | Trade 向后兼容、混合控制+载荷消息 |

**给 Submod 开发者的建议**：

| 你的场景 | 推荐的方案 |
|----------|------------------|
| 传输少量结构化控制指令 | Command 管线——标准、完整可用 |
| 传输带二进制载荷的数据（物品、生物等） | Item 管线——实现 `IItemCodec` + `IClientOutgoingItemHandler` |
| 服务端需要验证/拦截 Item 数据 | Item 管线——注册 `IServerItemInterceptor` / `IServerItemObserver` |
| 想实现一个 codec 就被管线自动路由 | Item 管线——通过 `AddItemCodec()` 注册 |
| 需要与现有 Trade 协议保持兼容 | Command 嵌套 `FrameworkItemPayload`（Trade 当前的做法） |

**未来演进** 🔮：
- **P1**：`PayloadBytes` 直通路径，消除中间 JSON 序列化和 base64 膨胀（性能优化）
- **P2**：将 Trade 的 Item payload 从 Command 嵌套迁移到独立 `KindItem` 路由（可选，当前嵌套路径保持兼容）

详细分析见 `docs/branch-local/dev/三条Pipeline职责辨析与Item管线补全分析.md` 和 `docs/branch-local/dev/Item管线补全实施方案.md`。

### 6.4 入站 / 出站流向对照表

| 方向 | Message | Command | Item |
|------|---------|---------|------|
| Server → Client（入站） | `IClientMessageHandler` → `IMessageRenderer` → UI | `IClientCommandHandler` → 内部状态 | `IClientIncomingItemHandler` → 内部状态 |
| Client → Server（出站） | `TryHandleOutgoingMessage()` → `IClientMessageHandler` 链 | `TryHandleOutgoingCommand()` → `IClientOutgoingCommandHandler` 链 | `TryHandleOutgoingItem()` → `IClientOutgoingItemHandler` 链 |
| 出站管线入口 | `IFrameworkClientTransport` | `IFrameworkClientCommandTransport` | `IFrameworkClientTransport` |
| 出站必须走管线 | ✅ 是（[设计哲学 §3.7]） | ✅ 是 | ✅ 是 |

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

### 7.3 响应式布局与适配（ClientExtensionAbstractions 1.1）

全量 UI 自适应（Phase 9）使 Phinix 能够在各种屏幕分辨率（从 1024×768 到 4K）、不同 RimWorld UI 缩放倍率及多语言长文本场景下稳定运行。框架在保持旧版 `IMainTabProvider` 和 `IServerSidebarProvider` 二进制与源码兼容的前提下，通过新增可选接口与低分配几何原语提供了完整的自适应支持。

#### 7.3.1 响应式布局提示与降级机制

需要声明内容尺寸偏好的实现，可以让同一个 provider 额外实现 `IResponsiveMainTabProvider` 或 `IResponsiveSidebarProvider`（定义于 [UI 抽象层](Client/ClientExtensionAbstractions/UI/)）。无需单独注册可选接口：

```csharp
public sealed class MyTab : IMainTabProvider, IResponsiveMainTabProvider
{
    // 缓存 Hints 静态实例，避免在 Draw 或 getter 中重复创建
    private static readonly UiLayoutHints Hints = new UiLayoutHints(
        minimumContentSize: new Vector2(480f, 320f),
        preferredContentSize: new Vector2(760f, 560f),
        supportsCompactLayout: true);

    public UiLayoutHints LayoutHints => Hints;

    // IMainTabProvider 的其他成员……
}

public sealed class MySidebar : IServerSidebarProvider, IResponsiveSidebarProvider
{
    public float MinimumWidth => 160f;
    public bool CanCollapse => true;

    // IServerSidebarProvider 的其他成员……
}
```

- **`MinimumContentSize`**：主 Tab 在普通布局下的最低建议内容尺寸。这并不是窗口下限——当玩家分辨率极小或将窗口缩得很窄时，Host 提供的 `inRect` 仍可能小于此尺寸。
- **`PreferredContentSize`**：建议的首选尺寸，只参与首次打开或重置尺寸时的初始尺寸决策，不会迫使窗口超过屏幕安全区。
- **`SupportsCompactLayout`**：指示该 Tab 在低于 `MinimumContentSize` 时是否具备专门的紧凑排版分支（例如由双栏自动切换为单栏/子 Tab）。
- **`MinimumWidth`**：侧栏仍有实用价值的最小宽度下限。
- **`CanCollapse`**：指示该侧栏在宿主空间不足时是否允许被自动折叠。
- **默认降级行为**：未实现可选接口的旧版第三方插件 provider 自动采用保守默认值（`UiLayoutHints.Default` 为 480×320 最小、700×560 首选、不支持紧凑布局；侧栏取 `PreferredWidth` 且 `CanCollapse = false`），旧插件无需重构即可安全加载与渲染。

#### 7.3.2 宿主侧栏抽屉折叠机制（Sidebar Drawer Collapse）

在宿主主窗口 `ServerTab` 中，主内容区有保底最小宽度 `MAIN_MIN_WIDTH = 480f`。
- 当用户将窗口拖小，使得窗口可用宽度不足以同时容纳主内容区保底宽度和侧栏时：
  - 如果所有侧栏均实现了 `IResponsiveSidebarProvider` 且 `CanCollapse == true`，Host 将自动把侧栏收起，并进入折叠模式。
  - 在折叠模式下，主内容区右上角会自动显示一个汉堡菜单按钮（`☰`）。
  - 玩家点击 `☰` 按钮会呼出一个全覆盖的半透明侧栏浮动抽屉（Floating Drawer Overlay），玩家可在抽屉中正常查看在线用户或通知列表。再次点击 `☰` 或点击空白处抽屉自动关闭。
- 附属 Mod 开发者**无需**自行实现折叠按钮或浮层逻辑，只需在侧栏 provider 上声明 `IResponsiveSidebarProvider` 并将 `CanCollapse` 置为 `true` 即可免费获得此能力。

#### 7.3.3 屏幕安全区与自定义弹窗（UiScreenSafeArea）

如果你的 Submod 创建了自定义弹窗窗口（继承自 RimWorld 的 `Window`，如交易确认窗、典籍/角色详情窗、红包弹窗等），**严禁写死窗口的绝对坐标或固定全屏像素**。

必须使用 `UiScreenSafeArea.ClampWindow` 将窗口 Rect 约束在当前屏幕安全区内：

```csharp
public class MyCustomDialog : Window
{
    private static readonly Vector2 MinimumDialogSize = new Vector2(400f, 300f);

    public override Vector2 InitialSize => new Vector2(600f, 450f);

    public override void PreOpen()
    {
        base.PreOpen();
        // 限制窗口尺寸与屏幕边界，避免在低分辨率或高 UI 缩放下超出可视范围
        windowRect = UiScreenSafeArea.ClampWindow(windowRect, MinimumDialogSize);
    }

    public override void DoWindowContents(Rect inRect)
    {
        // 绘制内容……
    }
}
```

- `UiScreenSafeArea.Current`：返回 `(0, 0, UI.screenWidth, UI.screenHeight)`，严格遵从当前 RimWorld UI 坐标系。
- `UiScreenSafeArea.Normalize(Rect rect)`：自动将负宽/负高的异常 Rect 转换为规范化的正向 Rect，防止 IMGUI 抛出内部异常。

#### 7.3.4 低分配共享布局原语（ClientExtensionAbstractions.UI）

为避免各插件自行编写脆弱的布局代码，`ClientExtensionAbstractions` 提供了 4 个高效、零托管分配的纯几何辅助类：

##### 1. `ResponsiveSplitLayout`（双栏/分割布局）
在容器空间变化时，自动在左右双栏（Horizontal）、上下双栏（Vertical）与单窗格（SinglePane）之间切换：

```csharp
// 根据容器可用尺寸与两栏的最小/首选尺寸计算分割几何
ResponsiveSplitResult split = ResponsiveSplitLayout.Calculate(
    container: inRect,
    firstMinimum: new Vector2(300f, 200f),
    secondMinimum: new Vector2(240f, 200f),
    firstPreferred: new Vector2(400f, 300f),
    spacing: 10f,
    showFirstInSinglePane: _activeSubTab == 0);

if (split.Mode == ResponsiveSplitMode.SinglePane)
{
    // 空间严重受限，可绘制子 Tab 切换按钮让用户选择查看 Pane 1 还是 Pane 2
}

// 分割结果包含 FirstRect、SecondRect 和 DividerRect
DrawLeftPane(split.FirstRect);
DrawRightPane(split.SecondRect);
```

##### 2. `ResponsiveToolbarLayout`（响应式工具栏与溢出菜单）
当操作按钮数量较多且窗口变窄时，按优先级从前向后放置按钮；无法容纳的按钮自动截断并收纳到右侧的 `⋯` 溢出按钮中，点击弹出 `FloatMenu`：

```csharp
// 建议在类中预先分配好 desiredWidths 与 actionRects 数组复用，避免 Draw 中每帧 new
private static readonly float[] ActionWidths = new float[] { 100f, 90f, 80f, 80f };
private readonly Rect[] _actionRects = new Rect[4];

// 在 Draw 方法中：
ResponsiveToolbarResult toolbar = ResponsiveToolbarLayout.Calculate(
    container: toolbarRect,
    desiredWidths: ActionWidths,
    actionCount: 4,
    primaryActionCount: 1,      // 确保至少保留 1 个最高优先级的核心操作
    rowHeight: 30f,
    spacing: 6f,
    maximumRows: 1,             // 限制最多排几行
    overflowButtonWidth: 32f,   // 溢出按钮宽度
    actionRects: _actionRects);

for (int i = 0; i < toolbar.VisibleActionCount; i++)
{
    if (Widgets.ButtonText(_actionRects[i], _actions[i].Label))
    {
        _actions[i].Execute();
    }
}

if (toolbar.HasOverflow && Widgets.ButtonText(toolbar.OverflowButtonRect, "⋯"))
{
    var options = new List<FloatMenuOption>();
    for (int i = toolbar.VisibleActionCount; i < 4; i++)
    {
        int actionIndex = i;
        options.Add(new FloatMenuOption(_actions[actionIndex].Label, () => _actions[actionIndex].Execute()));
    }
    Find.WindowStack.Add(new FloatMenu(options));
}
```

##### 3. `ResponsiveFormLayout`（响应式表单行）
处理包含“标签文本 + 输入框 + 可选右侧按钮 + 可选底部错误信息”的表单行。宽度充足时采用单行排布（Inline）；宽度受限时自动折行为两行堆叠（Stacked，上行为标签，下行为输入框和操作按钮）：

```csharp
ResponsiveFormResult form = ResponsiveFormLayout.Calculate(
    container: formRowRect,
    labelWidth: 100f,
    minimumInputWidth: 140f,
    actionWidth: 80f,
    rowHeight: 30f,
    spacing: 8f,
    errorHeight: string.IsNullOrEmpty(_errorText) ? 0f : 22f);

Widgets.Label(form.LabelRect, "服务器地址");
_serverAddress = Widgets.TextField(form.InputRect, _serverAddress);
if (Widgets.ButtonText(form.ActionRect, "连接"))
{
    Connect();
}
if (!string.IsNullOrEmpty(_errorText))
{
    GUI.color = Color.red;
    Widgets.Label(form.ErrorRect, _errorText);
    GUI.color = Color.white;
}
```

##### 4. `VirtualListLayout`（大型列表虚拟滚动）
当列表包含数百或数千条数据（如聊天记录、交易货架、玩家列表、日志）时，必须仅绘制视口可见项以避免帧率暴跌。`VirtualListLayout` 基于二分查找计算当前可见索引范围：

- **固定行高列表（Fixed Row Height）**：
  ```csharp
  Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect);
  // overscan: 上下各多渲染 1~2 行作为缓冲区，提升滚动平滑度
  VirtualListRange range = VirtualListLayout.GetFixedRange(
      itemCount: items.Count,
      rowHeight: 32f,
      scrollY: _scrollPosition.y,
      viewportHeight: outRect.height,
      overscan: 1);

  for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
  {
      Rect rowRect = new Rect(0f, i * 32f, viewRect.width, 30f);
      DrawItemRow(rowRect, items[i]);
  }
  Widgets.EndScrollView();
  ```

- **动态行高列表（Dynamic Cached Heights）**：
  若每行高度不同（如包含换行聊天消息），维护一个前缀偏移数组 `prefixOffsets`（长度为 `itemCount + 1`，`prefixOffsets[0] = 0`，`prefixOffsets[i]` 为前 `i` 项高度总和）。使用 `VirtualListLayout.GetDynamicRange(prefixOffsets, itemCount, _scrollPosition.y, outRect.height, overscan: 1)` 计算可见区间，即可达到同样的零每帧分配与极速渲染。

#### 7.3.5 几何计算与缓存失效原则

- `ResponsiveSplitLayout`、`ResponsiveToolbarLayout`、`ResponsiveFormLayout` 和 `VirtualListLayout` 均为**无状态纯数学函数**，其本身不包含任何内部缓存。
- 调用方应自行保存计算出的 Rect 或布局结果，并**仅在**以下时机触发缓存失效与重新计算：
  - 宿主容器 Rect 尺寸改变（`inRect.size` 发生变化）
  - 数据条目数量改变或内容变更
  - 游戏语言切换（`LanguageDatabase.activeLanguage`）
  - 用户配置项变更
- **严禁**在每帧 `Draw` 路径中重复分配临时数组（如每帧 `new float[]`）或调用 `Text.CalcHeight` / LINQ！关于这一要求，详见 [§11.4](#114-draw-路径上的对象分配)。

### 7.4 添加角标

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

### 7.5 添加设置面板

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

### 7.6 设置迁移（Legacy Settings）

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

### 7.7 推送显示消息

如果你的 Submod 需要向消息队列注入通知（不是走 Message 管线从服务端来的消息，而是本地生成的通知），使用 `IDisplayMessageSink`（定义于 [IClientExtensionAbstractions.cs:178-182](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L178-L182)）：

```csharp
public interface IDisplayMessageSink
{
    void Enqueue(FrameworkDisplayMessage message);
}
```

这个服务在 `Activate()` 中通过 `hostContext.GetRequiredService<IDisplayMessageSink>()` 获取。

### 7.8 添加顶部横幅通知（INoticeBannerProvider）

宿主主窗口（`ServerTab`）顶部预留了横幅通知区域。实现 `INoticeBannerProvider`（定义于 [INoticeBannerProvider.cs](Client/ClientExtensionAbstractions/UI/INoticeBannerProvider.cs)）：

```csharp
public interface INoticeBannerProvider
{
    float CurrentHeight { get; }  // 当前横幅期望高度（0 表示不展示）
    void Draw(Rect inRect);       // 绘制横幅内容
}
```

注册方式：

```csharp
builder.RegisterApi<INoticeBannerProvider>(this);
```

- 当 `CurrentHeight > 0` 时，`ServerTab` 会在窗口最顶部动态划分对应高度的矩形区域并调用 `Draw(inRect)`。
- 适合用于全局断网警报、版本更新提醒、待处理强交互任务或严重配置缺失警示。

### 7.9 回车键处理（IUiAcceptKeyHandler）

在 RimWorld 中，默认按下 Enter / KeypadEnter 键可能会触发窗口确认或直接关闭当前窗口。如果你的 Tab 或侧栏中包含文本输入框、搜索框或多行消息编辑控件，且希望在聚焦时按回车触发发送/搜索而非关闭窗口，可以让对应的 `IMainTabProvider` 或 `IServerSidebarProvider` 实现类同时实现 `IUiAcceptKeyHandler`（定义于 [IUiAcceptKeyHandler.cs](Client/ClientExtensionAbstractions/UI/IUiAcceptKeyHandler.cs)）：

```csharp
public interface IUiAcceptKeyHandler
{
    bool WantsAcceptKey { get; }   // 当前活跃控件是否需要拦截并消费回车键
    bool TryHandleAcceptKey();     // 执行回车确认逻辑，返回 true 表示已消费
}
```

- 无需单独注册：只要当前处于激活状态的 Tab 或侧栏实例实现了该接口，`ServerTab` 会在捕获到 Return 键盘事件时优先咨询 `WantsAcceptKey`；若为 `true`，则调用 `TryHandleAcceptKey()` 并阻断 RimWorld 底层窗口关闭逻辑。

### 7.10 UI 主题与配色（IUiTheme）

Phinix 提供了集中的 UI 主题配色服务 `IUiTheme`（定义于 [IUiTheme.cs](Client/ClientExtensionAbstractions/UI/IUiTheme.cs)），消除了硬编码颜色导致的视觉突兀与主题割裂：

```csharp
public interface IUiTheme
{
    Color PrimaryText { get; }      // 主文本颜色
    Color SecondaryText { get; }    // 次要/提示文本颜色
    Color Background { get; }       // 容器背景色
    Color Surface { get; }          // 卡片/面板表面颜色
    Color Separator { get; }        // 分割线颜色
    Color HoverHighlight { get; }   // 悬浮高亮色
    Color Pending { get; }          // 处理中状态色
    Color Error { get; }            // 错误/失败强调色
    Color Success { get; }          // 成功/连接强调色
    Color Warning { get; }          // 警告强调色

    void RegisterColor(string key, Color defaultColor);
    Color GetColor(string key);
    bool TryGetColor(string key, out Color color);
    void RegisterFloat(string key, float defaultValue);
    float GetFloat(string key, float defaultValue = 0f);
    void Reload();
}
```

- **获取方式**：在 `Activate()` 中通过 `hostContext.GetRequiredService<IUiTheme>()` 获取，或者通过 `builder.TryResolveApi<IUiTheme>(out var theme)` 解析。
- **扩展主题**：Submod 可以调用 `theme.RegisterColor("mymod.accent", defaultColor)` 注入模块专属的主题化颜色。
- **自定义主题提供者**：第三方 Mod 还可以实现完整的 `IUiTheme` 并通过 `builder.RegisterApi<IUiTheme>(customTheme)` 暴露给框架。

---

## 8. Host 提供的通用服务

以下服务在 `Activate(ExtensionHostContext hostContext)` 中通过 `hostContext.GetRequiredService<T>()` 获取。

> **关于 Register 阶段使用服务的说明**：即使当前 host 在调用 `DiscoverExtensions` → `Register` 前已经构建了 `ExtensionHostContext`，扩展也不能依赖这一实现细节。官方 Chat/Trade 客户端扩展会在 `Register()` 中注册稳定对象，再在 `Activate()` 中向这些对象注入宿主服务；第三方扩展也应采用同一边界。

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

`OnSettingChanged` 事件可用于实时响应设置变化。参考 [BuiltInTradeClientExtension.cs:133](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L133)。

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

消息与物品管线（出站）的入口。参见 [§6.1](#61-message-管线-完整可用) 与 [§6.3](#63-item-管线-完整可用p0-已完成)：

```csharp
public interface IFrameworkClientTransport
{
    bool HasRemoteCapability(string capability);
    void SendFrameworkPacket(FrameworkPacket packet);          // ⚠️ 受限制：正交通信应走 TryHandle
    bool TryHandleOutgoingMessage(string rawMessage);         // ✅ 推荐的消息出站入口（走 IClientMessageHandler 链）
    bool TryHandleOutgoingItem(FrameworkItemPayload itemPayload); // ✅ 推荐的物品出站入口（走 IClientOutgoingItemHandler 链）
}
```

> **关于 `SendFrameworkPacket`**：此方法直接发送 FrameworkPacket 而不走 handler 管线。根据设计哲学 §3.7，插件不应绕过管线——正交通信使用 `TryHandleOutgoingMessage` / `TryHandleOutgoingCommand` / `TryHandleOutgoingItem`。

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

如果你需要在新消息到达时触发通知（如播放音效），订阅 `DisplayMessageReceived`。Chat 扩展对此的使用见 [FrameworkClientChatServiceAdapter.cs:43](Extensions/Chat/Client/FrameworkClientChatServiceAdapter.cs#L43)。

### 8.13 IExtensionStorageProvider

插件可以获取专属的文件存储路径：

```csharp
hostContext.GetStoragePath("my.extension.id", "settings.json");
// 返回 framework-extensions/client/my.extension.id/ 下的绝对路径
```

实现代码见 [FrameworkTypes.cs:288-318](Common/Utils/Framework/FrameworkTypes.cs#L288-L318)（`FileSystemExtensionStorageProvider`）。

Provider 会规范化两个标识并校验最终绝对路径仍位于配置根目录内。它只分配安全路径；原子写、迁移、备份和按存档划分仍由插件负责。

### 8.14 日志

通过 `hostContext.Log` 回调记录日志：

```csharp
hostContext.Log?.Invoke("Something happened", LogLevel.INFO);
```

- **日志级别与过滤规则**：
  - 支持的日志级别：`DEBUG`, `INFO`, `WARNING`, `ERROR`。
  - **Release sink 过滤**：客户端会在有界管理器缓冲中保留 `DEBUG` 条目；Release 构建的 RimWorld 日志 sink 不转发这些条目，Debug 构建可在开发者模式下转发。
  - **日志环形缓冲区**：宿主会将所有扩展上报的最近 300 条日志缓存在内存环形缓冲区中（带 `ExtensionLogVersion` 版本戳），玩家与开发者可在客户端内置的 `ExtensionManagerTab` 中翻阅、搜索与排查问题。

需要携带来源的诊断时，可使用 `hostContext.GetExtensionLogger(ExtensionId)` 返回的可选 logger。它会绑定扩展 ID，并接受原始 `Exception` 与可选 correlation ID。旧的 `hostContext.Log` 回调继续兼容，并进入同一个客户端缓冲。

### 8.15 IUiTheme

统一 UI 主题与配色服务。定义于 [IUiTheme.cs](Client/ClientExtensionAbstractions/UI/IUiTheme.cs)：

```csharp
IUiTheme theme = hostContext.GetRequiredService<IUiTheme>();
Color primaryColor = theme.PrimaryText;
```

详见 [§7.10 UI 主题与配色](#710-ui-主题与配色iuitheme)。宿主在启动阶段将其同时注入为 Host 服务（`GetRequiredService<IUiTheme>()`）和通用 API（`TryResolveApi<IUiTheme>()`）。

### 8.16 IItemCodecProvider

查询框架已发现并注册的所有物品编解码器。定义于 [FrameworkTypes.cs:607-610](Common/Utils/Framework/FrameworkTypes.cs#L607-L610)：

```csharp
public interface IItemCodecProvider
{
    IReadOnlyList<IItemCodec> ItemCodecs { get; }
}
```

- 供插件在处理复合载荷或需要解析未知 `CodecId` 的 `FrameworkItemPayload` 时，检索当前环境中所有可用的编解码器实现。
- 官方 Trade 插件通过此服务动态解析并加载第三方扩展注册的物品编解码器。

### 8.17 IExtensionActivationPolicy

扩展激活决策与禁用状态查询服务。定义于 [FrameworkTypes.cs:517-532](Common/Utils/Framework/FrameworkTypes.cs#L517-L532)：

```csharp
public interface IExtensionActivationPolicy
{
    bool ShouldActivate(string extensionId, out string reason);
    IReadOnlyCollection<string> DisabledExtensions { get; }
}
```

- 供扩展在运行时查询其他关联扩展是否处于被用户禁用的状态（`DisabledExtensions` 集合），以便执行功能降级或展示提示。

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

**规则**：`Activate()` 中每一个 `+=` 必须在 `Shutdown()` 中有对应的 `-=`。参考 [BuiltInTradeClientExtension.cs:133](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L133) 与 [BuiltInTradeClientExtension.cs:154](Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L154) 的标准写法。

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

### 11.9 使用已废弃的旧 GUI 容器（Displayable 系列）

`PhinixClient.GUI` 命名空间下的一系列旧版 `Displayable` 弹性容器（如 `HorizontalFlexContainer`、`VerticalFlexContainer`、`TabsContainer`、`ConditionalContainer`、`MinimumContainer`、`VerticalPaddedContainer`）**已全部标记为 `[System.Obsolete]`**：

```csharp
// ❌ 错误：在新增 UI 中使用已废弃的旧容器
var flex = new HorizontalFlexContainer();
flex.Add(new TextWidget("Title"), 100f);
flex.Add(new TextFieldWidget(), Displayable.FLUID);
flex.Draw(inRect);
```

**为什么错**：
1. **过度分配与深层树形结构**：旧容器在绘制时创建了大量嵌套对象，难以被 JIT 有效优化，每帧遍历容易产生垃圾回收停顿。
2. **缺乏空间溢出防御**：旧 Flex 容器在固定宽度总和超过容器可用宽度且无 fluid 子项时，极易退化产生负宽度的异常 `Rect`，引发 Unity IMGUI 底层剪裁异常。
3. **不支持现代响应式重排**：旧容器无法实现表单折行（Inline ↔ Stacked）、工具栏溢出 FloatMenu、侧栏抽屉式折叠以及虚拟滚动（Virtual Scrolling）。

```csharp
// ✅ 正确：直接使用 RimWorld 原生 IMGUI + ClientExtensionAbstractions 几何原语
ResponsiveFormResult form = ResponsiveFormLayout.Calculate(inRect, 100f, 140f, 80f, 30f, 8f, 0f);
Widgets.Label(form.LabelRect, "Title");
_text = Widgets.TextField(form.InputRect, _text);
```

### 11.10 硬编码绝对坐标与忽略屏幕安全区

RimWorld 运行在千差万别的玩家硬件环境中：从 Steam Deck / 笔记本的 1280×720，到台式机的 4K 分辨率，再到 1.25x / 1.5x / 2.0x 的 RimWorld UI 缩放倍率。

```csharp
// ❌ 错误：弹窗写死绝对屏幕坐标，或者把窗口撑大到超出屏幕
public override void PreOpen()
{
    base.PreOpen();
    windowRect = new Rect(200f, 200f, 900f, 700f); // 在 720p 屏幕或高缩放下直接飞出屏幕！
}

// ❌ 错误：按钮写死固定像素宽度，忽略多语言长文本
Widgets.ButtonText(new Rect(x, y, 60f, 30f), "MyButtonText".Translate()); // 德文/法文或长文本直接重叠！

// ❌ 错误：固定行高绘制未截断的多行文字，无 Tooltip 兜底
Widgets.Label(new Rect(0f, y, width, 24f), longDescription); // 溢出文字盖在下一行控件上！
```

**正确做法**：
1. **弹窗 Clamp 到安全区**：所有独立窗口必须调用 `windowRect = UiScreenSafeArea.ClampWindow(windowRect, minSize);`。
2. **文本承载三原则**：
   - 固定高度卡片：必须使用单行截断（`Text.WordWrap = false`）并挂载 `TooltipHandler.TipRegion` 保证完整信息可读。
   - 动态长文本：必须基于当前宽度缓存高度计算，并由外层 `Widgets.BeginScrollView` 承载。
   - 工具栏与操作组：必须使用 `ResponsiveToolbarLayout` 在宽度受限时将低优先级操作自动放入 `⋯` 浮动菜单，防止按钮推挤挤出屏幕。

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

  <!-- 构建后复制到 Extensions 目录（适用于放入 Phinix 目录的集成方式） -->
  <Target Name="AfterBuild">
    <MakeDir Directories="$(SolutionDir)\Output\Client\Common\Extensions" />
    <Copy SourceFiles="$(TargetDir)$(AssemblyName).dll"
          DestinationFiles="$(SolutionDir)\Output\Client\Common\Extensions\17-$(AssemblyName).dll" />
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

第三方 Submod 推荐作为**独立 RimWorld Mod** 发布与部署：

#### 方案 A：作为独立 Mod（推荐）
1. 在 Visual Studio 或使用 `dotnet build` 编译你的 Submod 工程；
2. 将编译生成的 DLL（如 `MySubmod.dll`）复制到你自己的 Mod 根目录下的 `Assemblies/` 目录；
3. 在你的 Mod `About/About.xml` 中配置依赖声明，确保排在 Phinix 之后：
   ```xml
   <modDependencies>
     <li>
       <packageId>hunyuan.phinixrework</packageId>
       <displayName>Phinix Rework</displayName>
     </li>
   </modDependencies>
   <loadAfter>
     <li>hunyuan.phinixrework</li>
   </loadAfter>
   ```
4. 启动 RimWorld 并在 Mod 管理列表中同时激活 Phinix 和你的 Submod；宿主启动时会自动探测到你的 Mod 的 `Assemblies/` 目录并加载其中的扩展类！

#### 方案 B：放入 Phinix 扩展目录（一体化集成）
1. 编译生成 DLL；
2. 将 DLL 文件复制到 Phinix Mod 的 `Common/Extensions/` 目录；
3. **确保文件名前缀 ≥ 17-**（如 `17-MySubmod.dll`，以保证在官方 08-16 插件之后加载，见 §12.7）；
4. 启动 RimWorld 并启用 Phinix。

宿主启动时的日志输出可以帮助确认加载状态：
```
[Phinix] Framework module 'mymod.myfeature' registered from 'MyMod.PhinixExtension.MySubmodExtension' ...
[Phinix] Framework module 'mymod.myfeature' activated for host 'client'.
```

### 12.7 加载顺序号解析

RimWorld 的 `ModAssemblyHandler` 按文件名字符串序加载程序集。当前框架基础程序集与官方插件的编号分配如下（参考 [设计哲学 §5.1](设计哲学.md#51-命名与排序)）：

| 前缀 | 程序集 | 物理目录 | 说明 |
|------|--------|----------|------|
| 01-02 | LiteNetLib, Protobuf | `Common/Assemblies/` | 底层第三方网络与序列化库 |
| 03 | Utils | `Common/Assemblies/` | `IPhinixExtensionModule`、Framework 协议核心 |
| 04-05 | Connections, Connections.Client | `Common/Assemblies/` | 基础连接抽象与客户端实现 |
| 06-07 | Authentication, Authentication.Client | `Common/Assemblies/` | 认证模块契约与客户端实现 |
| 08 | ChatExtension | `Common/Extensions/` | 官方 Chat 领域 Contracts |
| 09 | TradeExtension | `Common/Extensions/` | 官方 Trade 领域 Contracts |
| 10 | LegacyAdapter.Client | `Common/Extensions/` | 兼容旧服务器的协议适配器客户端 |
| 11 | ChatExtension.Client | `Common/Extensions/` | 官方 Chat 客户端扩展实现 |
| 12 | TradeExtension.Client | `Common/Extensions/` | 官方 Trade 客户端扩展实现 |
| 13 | LegacyRedPacketExtension | `Common/Extensions/` | 官方红包扩展契约 |
| 14 | LegacyRedPacketExtension.Client | `Common/Extensions/` | 官方红包客户端扩展实现 |
| 15 | LegacyTalentTradeExtension | `Common/Extensions/` | 官方异能/天赋交易扩展契约 |
| 16 | LegacyTalentTradeExtension.Client | `Common/Extensions/` | 官方异能/天赋交易客户端扩展实现 |
| 13 | PhinixClient | `1.6/Assemblies/` | 客户端宿主（按版本隔离） |
| 17+ | 第三方 Submod（放入 Extensions 时） | `Common/Extensions/` | 必须使用 17 或更大前缀，排在所有官方插件之后 |

> **提示**：如果采用**方案 A（独立 Mod）**分发，DLL 位于独立 Mod 的 `Assemblies/` 中，RimWorld 会在加载完 Phinix 的所有程序集后再加载你的 Mod 程序集，因此通常无需在文件名中添加数字前缀；但如果你的 Mod 包含多个有前后依赖关系的 DLL，仍需在自己的文件名间遵循字母序规则。

### 12.8 调试提示

- **加载问题**：检查 RimWorld 控制台日志，搜索 `[Phinix]` 关键词，观察扩展发现/注册/激活的诊断输出。
- **DLL 未发现**：检查 DLL 是否在 `ExtensionAssemblyLoader` 的 probe 目录中，文件名是否以 `.dll` 结尾。
- **类型加载异常**（`ReflectionTypeLoadException`）：通常是依赖的 DLL 不存在或版本不匹配——检查所有 ProjectReference 是否都已放置到对应探测目录。
- **Activate 未调用**：确认模块同时实现了 `IPhinixExtensionModule` 和 `IActivatablePhinixExtensionModule`。
- **UI 不显示**：确认 `RegisterApi<IMainTabProvider>` 在 `Register()` 中调用；检查 `TabOrder` 是否与其他 Tab 冲突。
- **扩展被禁用**：检查游戏内“扩展管理”界面（`ExtensionManagerTab`）或设置面板，确认该扩展未被手动禁用或因前置依赖缺失而处于 `DependencyDisabled` 状态。

---

## 附录 A：IExtensionBuilder 全部注册方法速查表

| 方法 | 参数类型 | 用途 | 当前状态 |
|------|----------|------|----------|
| `AddCapabilityProvider` | `ICapabilityProvider` | 声明支持的能力 | ✅ |
| `AddMessageInterceptor` | `IMessageInterceptor` | 展示消息拦截 | ✅ |
| `AddMessageRenderer` | `IMessageRenderer` | 消息渲染器 | ✅ |
| `AddClientMessageHandler` | `IClientMessageHandler` | 客户端消息处理（入站+出站） | ✅ |
| `AddClientCommandHandler` | `IClientCommandHandler` | 客户端命令处理（入站） | ✅ |
| `AddItemCodec` | `IItemCodec` | 注册物品编解码器（供 Item 管线与默认 handler 消费） | ✅ |
| `AddClientItemHandler` | `IClientIncomingItemHandler` | 客户端入站物品处理 | ✅ |
| `AddClientOutgoingItemHandler` | `IClientOutgoingItemHandler` | 客户端出站物品处理 | ✅ |
| `AddServerMessageHandler` | `IServerMessageHandler` | 服务端消息处理 | ✅（仅服务端） |
| `AddServerInboundMessageInterceptor` | `IServerInboundMessageInterceptor` | 服务端消息拦截 | ✅（仅服务端） |
| `AddServerDefaultMessageHandler` | `IServerDefaultMessageHandler` | 服务端默认消息处理 | ✅（仅服务端） |
| `AddServerMessageObserver` | `IServerMessageObserver` | 服务端消息观察 | ✅（仅服务端） |
| `AddServerCommandHandler` | `IServerCommandHandler` | 服务端命令处理 | ✅（仅服务端） |
| `AddServerInboundCommandInterceptor` | `IServerInboundCommandInterceptor` | 服务端命令拦截 | ✅（仅服务端） |
| `AddServerDefaultCommandHandler` | `IServerDefaultCommandHandler` | 服务端默认命令处理 | ✅（仅服务端） |
| `AddServerCommandObserver` | `IServerCommandObserver` | 服务端命令观察 | ✅（仅服务端） |
| `AddServerItemHandler` | `IServerItemHandler` | 服务端物品处理 | ✅（仅服务端） |
| `AddServerInboundItemInterceptor` | `IServerInboundItemInterceptor` | 服务端物品拦截 | ✅（仅服务端） |
| `AddServerDefaultItemHandler` | `IServerDefaultItemHandler` | 服务端默认物品处理 | ✅（仅服务端） |
| `AddServerItemObserver` | `IServerItemObserver` | 服务端物品观察 | ✅（仅服务端） |
| `AddServerOutboundPacketInterceptor` | `IServerOutboundPacketInterceptor` | 服务端出站拦截 | ✅（仅服务端） |
| `AddConsoleCommandProvider` | `IServerConsoleCommandProvider` | 服务端控制台命令扩展 | ✅（仅服务端） |
| `RegisterApi<T>` | `T` 实现 | 暴露 API | ✅ |
| `TryResolveApi<T>` | out `T` | 解析单个 API | ✅ |
| `ResolveApis<T>` | — | 解析所有 API | ✅ |

> **状态符号**：✅ = 完整可用 | ⚠️ = 半成品/过渡态 | 🔮 = 计划中

## 附录 B：ExtensionHostContext 全部服务速查表

以下服务在 `Activate()` 中通过 `hostContext.GetRequiredService<T>()` 获取：

| 服务接口 | 用途 | 定义位置 |
|----------|------|----------|
| `IFrameworkClientTransport` | 消息与物品管线出站入口（`TryHandleOutgoingMessage` / `TryHandleOutgoingItem`） | [IClientExtensionAbstractions.cs:9-28](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L9-L28) |
| `IFrameworkClientCommandTransport` | 命令管线出站入口（`TryHandleOutgoingCommand`） | [IClientExtensionAbstractions.cs:30-39](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L30-L39) |
| `IClientDisplayMessageStore` | 消息持久化存储 | [IClientExtensionAbstractions.cs:41-50](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L41-L50) |
| `IClientDisplayMessageFeed` | 消息流事件订阅 | [IClientExtensionAbstractions.cs:52-55](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L52-L55) |
| `IFrameworkClientLifecycle` | 兼容模式与协商 | [IClientExtensionAbstractions.cs:67-72](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L67-L72) |
| `IClientSessionContext` | 当前会话状态 | [IClientExtensionAbstractions.cs:74-83](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L74-L83) |
| `IClientSettingsContext` | 读写设置 | [IClientExtensionAbstractions.cs:85-100](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L85-L100) |
| `IClientUserDirectory` | 用户信息查询 | [IClientExtensionAbstractions.cs:102-109](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L102-L109) |
| `IClientUserEventStream` | 用户事件订阅 | [IClientExtensionAbstractions.cs:111-120](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L111-L120) |
| `IClientMainThreadDispatcher` | 主线程封送 | [IClientExtensionAbstractions.cs:122-125](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L122-L125) |
| `IClientWindowService` | 打开窗口 | [IClientExtensionAbstractions.cs:127-132](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L127-L132) |
| `IClientSoundService` | 播放音效 | [IClientExtensionAbstractions.cs:134-137](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L134-L137) |
| `ILegacyModuleTransport` | 原始模块通信 | [IClientExtensionAbstractions.cs:162-173](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L162-L173) |
| `IDisplayMessageSink` | 注入显示消息 | [IClientExtensionAbstractions.cs:178-182](Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs#L178-L182) |
| `IUiTheme` | 统一 UI 主题与配色令牌 | [IUiTheme.cs](Client/ClientExtensionAbstractions/UI/IUiTheme.cs) |
| `IItemCodecProvider` | 物品编解码器查询与解析 | [FrameworkTypes.cs:607-610](Common/Utils/Framework/FrameworkTypes.cs#L607-L610) |
| `IExtensionActivationPolicy` | 扩展启用策略与禁用黑名单 | [FrameworkTypes.cs:517-532](Common/Utils/Framework/FrameworkTypes.cs#L517-L532) |
| `UserManager` | 底层用户管理（通过 `AddService` 注入） | [Client/Source/Client.cs](Client/Source/Client.cs) |
| `Action` | 打开设置窗口（同 `IClientWindowService.OpenSettingsWindow`） | [Client/Source/Client.cs](Client/Source/Client.cs) |
| `Action<bool>` | 同步 acceptingTrades 状态 | [Client/Source/Client.cs](Client/Source/Client.cs) |

> **提示**：UI 扩展契约（`IMainTabProvider`, `IServerSidebarProvider`, `IResponsiveMainTabProvider`, `IResponsiveSidebarProvider`, `IBadgeProvider`, `IClientSettingsPanelProvider`, `IClientLegacySettingsMigrator`, `INoticeBannerProvider`, `IUiAcceptKeyHandler`, `IUiTheme`）通过 `builder.RegisterApi<T>()` 进行声明式注册；`hostContext` 本身还提供 `Log`、`StorageProvider`、`ApiRegistry`（`TryResolveApi` / `ResolveApis`）、`GetStoragePath()` 等方法——见 [FrameworkTypes.cs](Common/Utils/Framework/FrameworkTypes.cs)。
