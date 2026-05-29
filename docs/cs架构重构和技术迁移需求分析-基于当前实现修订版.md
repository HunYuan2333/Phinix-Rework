# Phinix C/S 架构重构和技术迁移需求分析（基于当前实现的修订版）

## 0. 文档目的

本文不是重写原始设想，而是基于当前仓库里的真实代码结构、项目拆分、依赖关系和部署方式，对 `docs/cs架构重构和技术迁移需求分析.md` 做一次“现状对齐”的修订。

核心目标是回答三件事：

1. 当前仓库实际上已经走到了哪一步。
2. 原文里哪些判断仍然成立，哪些已经过时。
3. 下一阶段真正需要推进的迁移重点是什么。

---

## 1. 当前真实架构结论

从当前代码看，Phinix 已经不是“Client/Server 直接共用一坨 Common 具体实现”的最初形态，而是处于一个明显的过渡期：

```text
Client / Server 仍然是主程序入口
Common 仍然承担大量共享实现
但 framework v2 已经出现
并且已经具备：
  - capability negotiation
  - extension module discovery
  - message / command / item 三类扩展入口
  - API registry
  - extension host context
  - extension state persistence
```

因此，当前项目更准确的描述应该是：

> 这是一个“已经做出 framework/extension 雏形，但 Common 仍然偏厚、Server 仍运行在 Mono/.NET Framework、官方扩展仍以内置程序集形式跟随主程序发布”的 C/S 过渡架构。

---

## 2. 当前项目结构与技术栈

### 2.1 Solution 真实项目构成

当前 `Phinix.sln` 中的核心项目包括：

```text
Client/Source/Client.csproj
Server/Server.csproj

Common/Connections
Common/Authentication
Common/UserManagement
Common/Utils
Common/ChatExtension
Common/ChatExtension.Client
Common/ChatExtension.Server
Common/TradeExtension
Common/TradeExtension.Client
Common/TradeExtension.Server
Common/ClientExtensionAbstractions

Dependencies/protobuf/Google.Protobuf.csproj
```

这说明当前仓库并没有完成“Common 只保留 protocol + abstraction”的目标。

相反，`Common` 现在实际承载了四类内容：

```text
1. 基础协议与 framework 契约
2. 网络、认证、用户管理等共享基础设施
3. chat / trade 的领域 contract
4. chat / trade 的 client/server 侧官方扩展实现
```

---

### 2.2 当前运行时与目标框架

当前 `Client` 和 `Server` 项目都还是：

```text
.NET Framework 4.7.2
```

不是：

```text
net8.0
net10.0
```

其中：

```text
Client:
  - 依赖 RimWorld / Verse / UnityEngine / Harmony
  - 明确属于 Unity/Mono 生态

Server:
  - 当前仍是 net472 控制台程序
  - Docker 仍然基于 mono 构建和运行
  - 尚未迁移到 modern .NET
```

也就是说，原文里“服务端应该迁移到 modern .NET”这个方向是对的，但它描述的是目标态，不是现状。

---

### 2.3 当前网络与序列化技术

当前真实使用的技术包括：

```text
传输层：
  LiteNetLib

消息格式：
  Google.Protobuf

框架协议封装：
  FrameworkPacket
  FrameworkSerialization
  FrameworkFlow
```

这里有一个很关键的现实约束：

> 当前项目虽然使用 protobuf，但并不是“只有 protobuf contract 在 Common 里”，而是 protobuf、framework contract、连接层、认证层、用户同步层、官方扩展 contract 和一部分实现都还在 Common。

---

## 3. 当前 framework / extension 架构已经实现到什么程度

### 3.1 已经落地的部分

相比原文，当前仓库里以下能力其实已经存在：

#### 1. Extension module 模型已经存在

当前已经有：

```csharp
IPhinixExtensionModule
IActivatablePhinixExtensionModule
IExtensionBuilder
ExtensionHostContext
```

并且 `PhinixExtensionRegistry` 已经支持：

```text
反射发现模块
执行 Register(builder)
执行 Activate(hostContext)
执行 Shutdown(hostContext)
收集 diagnostics / warnings
```

这意味着：

> “需要把 extension 注册从散装 handler 提升到 module/builder 模式”这一点，其实第一版已经做了，不再是纯概念。

#### 2. API registry 已经存在

当前已经有：

```csharp
IExtensionApiRegistry
ExtensionApiRegistry
RegisterApi<T>()
TryResolve<T>()
ResolveAll<T>()
```

Client 和 Server 也都已经暴露：

```csharp
TryResolveExtensionApi<T>()
ResolveExtensionApis<T>()
```

因此，原文里“建议新增服务端/客户端扩展 API 暴露与发现机制”，应修正为：

> API registry 已初步落地，但还没有完全演化成稳定的跨扩展协作规范。

#### 3. 三类扩展入口已经存在

当前 framework 实际已经有三类扩展入口：

```text
message handlers
command handlers
item codecs
```

并且 capability 默认也已经包含：

```text
core.framework.v2
core.message-pipeline
core.command-pipeline
core.item-pipeline
```

所以原文提到的“三管道思想可以保留”这一点是对的，但表述需要更贴近代码：

> 当前代码不是未来要设计三管道，而是已经实现了 message / command / item 三类主链，只是命名、职责和边界还没有完全收口。

#### 4. 官方 chat / trade 已经 extension 化了一部分

当前 chat 和 trade 不再完全硬编码在 `Client`/`Server` 主项目内部，而是已经拆成：

```text
Common/ChatExtension
Common/ChatExtension.Client
Common/ChatExtension.Server

Common/TradeExtension
Common/TradeExtension.Client
Common/TradeExtension.Server
```

并通过 `BuiltInChatClientExtension`、`BuiltInChatServerExtension`、`BuiltInTradeClientExtension`、`BuiltInTradeServerExtension` 注册进入 framework。

这说明原文里“Server Extension 可以覆盖或接管逻辑”的方向已经有落脚点，只是当前内置扩展还主要是官方程序集，而不是独立 Mods 目录插件。

---

### 3.2 仍然没有解决的部分

虽然 framework 已出现，但当前架构离目标态还有明显距离。

#### 1. Common 仍然过厚

当前 `Common` 仍包含：

```text
Connections
Authentication
UserManagement
Utils
ClientExtensionAbstractions
ChatExtension.Client / Server
TradeExtension.Client / Server
```

这意味着 Common 目前不是：

```text
Common.Protocol
Common.Abstractions
```

而是：

```text
“共享契约 + 共享基础设施 + 官方扩展实现”的混合层
```

这是当前最核心的结构性问题。

#### 2. Server 仍未脱离 Mono / net472

当前服务端依然：

```text
TargetFrameworkVersion = v4.7.2
Dockerfile 使用 mono:latest 构建
运行镜像使用 alpine-mono
启动命令仍是 mono PhinixServer.exe
```

所以“服务端迁移 modern .NET”仍然是必要任务，而且优先级很高。

#### 3. Common 里仍然存在运行时污染

原文拿 `Pastel` 举例，这一点在当前代码里仍然成立。

`Common/Utils/Utils.csproj` 当前仍直接引用：

```text
Pastel
System.Drawing
Google.Protobuf
```

其中 `Pastel` 明显偏服务端控制台输出用途，不应停留在 Common。

此外，`Common/ChatExtension.Client` 还直接引用：

```text
Assembly-CSharp
UnityEngine
```

这意味着当前 Common 并没有做到“运行时中立”。

更准确地说：

> 仓库现在已经不是“所有实现都塞在 Common”，但仍然存在“Common 下挂着客户端扩展实现项目，因此 Unity 依赖仍会从 Common 树进入解决方案”的问题。

#### 4. Client/Server 主程序仍然知道官方扩展 API

虽然 extension 通过 registry 被发现，但主程序仍然显式要求官方能力存在。

例如当前 `Client` 启动时会：

```text
TryResolveExtensionApi<IFrameworkChatClientApi>()
TryResolveExtensionApi<IFrameworkTradeClientApi>()
```

如果没拿到就直接抛异常。

这说明当前架构仍然是：

```text
host + official extension 强耦合
```

而不是完全可插拔。

进一步说，当前 `chat` 和 `trade` 在运行时地位上仍然高于“普通扩展”。

它们虽然已经 module 化，但主程序仍把它们当作默认必须存在的官方能力来解析和使用，这会形成一种隐含前提：

```text
chat/trade 是框架默认组成部分
其他扩展只是附加能力
```

这个前提从长期看是危险的。

因为一旦默认接受 `chat` / `trade` 的特殊地位，后续新增：

```text
red packet
market
mail
guild
quest
其他自定义玩法插件
```

就很容易继续复制这种模式，让 host 或 framework 不断为“官方功能”增加专用 API、专用注入和专用分支。

从需求分析角度，这意味着当前项目仍然缺少一个明确的“扩展平权原则”：

> `chat` 和 `trade` 可以是官方随发扩展，但不应在架构地位上高于未来其他插件；只要某项能力属于 extension domain，它就应通过与其他插件相同的注册、发现、协作和降级机制接入系统。

Server 侧虽然耦合更轻，但 `ExtensionHostContext` 仍显式注入了：

```text
ServerUserManager
IFrameworkServerPacketDispatcher
history-capacity option
```

这是合理的 host 基础服务，但说明宿主和扩展的“最小公共服务面”仍在演化中。

#### 5. 默认 relay 覆盖机制并没有按原文那样显式建模

原文提出的理想模型是：

```text
NotHandled -> 默认 relay
Handled    -> 已处理，不 relay
Blocked    -> 拦截
```

但当前真实代码里并没有一个统一的“默认 relay + 覆盖”状态机。

当前框架更接近：

```text
循环匹配 handler
handler 返回 Continue / Handled / ReplacePayload / StopPropagation 等动作
谁消费了，谁结束
没有消费则 warning
```

也就是说，现状不是“server 默认做 relay，extension 覆盖 relay”，而是：

> framework 根据消息类型把包路由给已注册 handler，由具体扩展自行决定如何处理；不存在一个统一的、可回退的默认业务 relay 中枢。

这会影响后续文档中的迁移设计表达，必须修正。

#### 6. 插件目录和外部动态加载尚未实现

当前 `PhinixExtensionRegistry` 的发现方式是：

```text
扫描 AppDomain.CurrentDomain 已加载程序集
反射查找 IPhinixExtensionModule
```

这不是：

```text
扫描 /app/Mods
按目录加载第三方 dll
按 manifest/assembly policy 激活
```

因此当前项目的“动态注册”是：

```text
对已随程序加载的程序集做反射发现
```

而不是：

```text
真正的外部插件装载系统
```

---

## 4. 对原需求文档的修正意见

### 4.1 仍然成立的判断

原文以下判断今天依然成立：

```text
1. Common 需要继续瘦身，最终收敛为共享契约层
2. 服务端应该脱离 Mono/.NET Framework
3. Client 和 Server 的行为实现应继续分离
4. 共享层不应继续吸纳 server-only / client-only 依赖
5. 服务端应逐步成为权威业务执行端
6. Docker 部署和插件化能力值得继续推进
```

---

### 4.2 需要修正的判断

原文有几处已经不够贴近现状：

#### 1. “Common 只是共享实现垃圾桶”

这句话只对了一半。

更准确的说法应改为：

> Common 目前已经出现 framework core 的雏形，但仍然同时承载了共享基础设施和官方扩展实现，边界仍偏厚，尚未收敛为真正的共享契约层。

#### 2. “三管道设计还没落地”

需要改成：

> 三类扩展入口已经落地为 `message / command / item`，当前问题不是有没有，而是命名是否准确、职责是否足够清晰、以及是否继续允许业务语义污染 core。

#### 3. “服务端默认 relay，扩展覆盖 relay”

需要改成：

> 当前框架并没有统一的默认 relay 机制，而是按 capability + handler 路由。后续如果希望支持“默认中继 + 扩展接管”，需要单独设计 server-side fallback 语义，而不能假定它已经存在。

#### 4. “内置扩展还在 Client/Server 主工程里”

这条已经过时。

当前更准确的描述是：

> chat/trade 已经拆成独立的 `Common/*Extension*` 项目，并通过 extension module 注册进入主程序，但它们仍然和主程序同仓、同解、同发布节奏，不是真正的外部插件。

#### 5. “API 注册与扩展发现还没有”

这条也已过时。

需要修正为：

> API registry、module discovery、activation/shutdown、host context 和 extension persistence 都已经有第一版实现，但还没有彻底变成稳定的插件平台边界。

---

## 5. 当前最合理的目标架构

基于现状，下一阶段目标不应再抽象地写成“从零设计一套 C/S 扩展架构”，而应写成：

> 在现有 framework v2 基础上，继续把 Common 从“混合共享层”收缩成“共享协议 + 共享抽象 + 极少量运行时无关基础设施”，同时把服务端从 Mono/net472 迁移到 modern .NET，并为未来外部插件装载预留边界。

建议目标结构如下：

```text
/Client
  RimWorld / Unity / Verse 宿主
  client UI
  client-side adapters
  official client extensions 或扩展加载入口

/Server
  server host
  network/auth/session bootstrap
  extension runtime bootstrap
  modern .NET deployment target

/Common.Protocol
  protobuf contracts
  packet DTO
  shared enums/constants

/Common.Framework.Abstractions
  extension module contracts
  handler contracts
  pipeline contracts
  host context abstractions
  API registry abstractions

/Common.Framework.Runtime
  仅保留确实双端都需要、且不绑定 Unity/Mono 的少量运行时实现

/Extensions.Chat
/Extensions.Trade
/Extensions.RedPacket
  各自持有自己的 contracts + client/server handlers + APIs
```

注意这里的关键不是目录名，而是边界：

```text
Common 不再持有 client-only / server-only 官方扩展实现
extension 成为一等公民
host 只提供最小宿主服务
```

这里还需要补一条边界澄清，避免后续实现者把 “Common 瘦身” 误解成 “Common 完全不允许出现任何业务语义”：

> Common 不应继续承载业务实现、业务流程编排、官方功能特判以及任何 client-only / server-only 运行时代码；但它仍然可以保留“双端必须共同理解”的契约级业务语义，例如共享 packet、DTO、枚举、capability 名称、extension API 抽象和 handler contract。

换句话说，应该被保留在 Common 的是：

```text
协议
抽象
runtime-neutral 基础设施
```

而不应该继续停留在 Common 的是：

```text
具体业务处理器
服务端状态管理器
客户端 UI 模型
host 对官方扩展的特殊假设
```

这里需要进一步明确一个容易被忽略的需求：

> “extension 成为一等公民”不只是指支持更多插件，更是指 `chat`、`trade` 和未来新增扩展在架构地位上必须平等。

也就是说，目标架构不应存在：

```text
核心框架默认高配合支持 chat/trade
其他插件只能走次级扩展路径
```

而应明确为：

```text
chat 是 extension
trade 是 extension
future plugins 也是 extension
它们共享同一套注册、发现、能力协商、API 暴露和降级规则
```

这样做的价值不是“形式上公平”，而是避免 system boundary 再次被官方功能侵蚀。

如果 `chat` / `trade` 被默认视为“半内建能力”，那后续任何重要插件都可能争取相同待遇，最终结果就是：

```text
host 越来越厚
core 越来越懂业务
扩展边界再次失效
```

因此，平权并不是抽象原则，而是继续解耦的必要条件。

---

## 6. 实际迁移重点排序

### Phase 1：先完成“现状收口”，不是马上推倒重来

当前已经有 framework v2，所以第一步不该是重写，而是收口边界：

```text
整理 framework 命名
明确 message / command / item 的职责
确认哪些服务属于 host 基础服务
确认哪些 API 属于扩展间协作 contract
确认 chat / trade 不再享有高于其他 extension 的架构特权
```

这是对现有代码做“边界澄清”，不是另起炉灶。

---

### Phase 2：瘦身 Common

这是当前最重要的结构性任务。

建议优先从以下几类内容开始迁出：

```text
1. Pastel / System.Drawing / 控制台输出相关能力移出 Common
2. ChatExtension.Client / TradeExtension.Client 从 Common 树中剥离
3. ChatExtension.Server / TradeExtension.Server 从 Common 树中剥离
4. ClientExtensionAbstractions 中与 UI 强绑定的类型重新评估边界
```

目标不是一步做到“只剩 proto”，而是先让 Common 不再继续承担明显的 runtime-specific 实现。

这里还要补一条需求判断：

> 把 `chat` / `trade` 从 Common 树中继续剥离，不只是为了目录整洁，而是为了消除“官方扩展天然高一级”的结构暗示。

只要它们继续长期停留在 Common 主树里，团队在认知上就会更容易把它们视为“框架组成部分”，而不是“与其他插件平权的扩展模块”。

另外还需要明确一条容易被误判的边界：

> 把 `server-only` 源码文件从 `Common` 目录物理迁出，只能算 `Phase 2` 的完成条件之一，但不能等同于 `Common` 程序集边界已经彻底瘦身。

更具体地说，可以接受这样的过渡态：

```text
源码路径已经迁到 /Server 或其他更合理的宿主目录
但暂时仍编译进原有 Common 程序集
```

这样做的价值是：

```text
先修正目录边界和团队认知
降低一次性改动的依赖爆炸风险
不给当前 client/server 引用链引入过多破坏
```

但它也意味着：

```text
Common 的“物理目录边界”变干净了
Common 的“程序集边界”还没有完全变干净
```

因此，`Phase 2` 更合适的完成定义应当是：

> 先把明显的 `runtime-specific` 实现和 `client-only / server-only` 源码从 `Common` 主树中迁出，完成目录层面的收口；至于这些类型是否立即拆出原有程序集，不应在这一阶段强行一次做完。

---

### Phase 2.5：拆 Common 的程序集边界

在 `Phase 2` 之后，建议补一个单独的小阶段，而不是把这部分直接混进 `Phase 3`。

原因是这件事虽然和服务端 modern .NET 迁移有关，但它本质上首先是：

```text
编译边界收口
程序集职责重分
引用方向治理
```

而不只是运行时升级。

这一阶段的核心任务应改写为：

```text
1. 把仍然挂在 Common 程序集里的 server-only 类型逐步拆出
2. 评估 Authentication / UserManagement / Utils 是否需要拆成 shared + server 两层
3. 让 Common 最终只保留真正双端共享的 contract、abstraction 和少量 runtime-neutral 实现
```

针对第 2 点，结合当前代码可以先给出一个明确结论：

> 这三个项目都需要完成 shared / server 职责分离，但不应该机械地“一刀切”成完全对称的双层结构。更准确的策略是：`Authentication` 与 `UserManagement` 需要显式拆成 `shared + server` 两层；`Utils` 不需要整体拆成两层，而是把极少数 server-only 运行时能力单独迁出即可。

原因分别如下。

#### Authentication：需要拆成 `shared + server`

当前 `Authentication` 同时包含了：

```text
shared:
  Authenticator
  ClientAuthenticator
  Credential / CredentialStore
  AuthenticatePacket / AuthResponsePacket / HelloPacket / ExtendSessionPacket 等协议

server-only:
  ServerAuthenticator
  Session
```

并且 `ServerAuthenticator` 不是一个轻量包装层，而是明确承载了服务端运行时状态：

```text
NetServer 绑定
session 生命周期管理
credential store 持久化
connection/session/username 映射
服务端认证包处理与响应发送
```

这类能力既不会被客户端复用，也不适合作为 Common 的共享实现长期保留。

因此，`Authentication` 最合适的方向是：

```text
Authentication(shared)
  保留认证协议、共享模型、Authenticator 抽象、ClientAuthenticator

Authentication.Server
  持有 ServerAuthenticator / Session
  依赖 Authentication(shared) + Connections + Utils
```

#### UserManagement：需要拆成 `shared + server`

当前 `UserManagement` 也同时包含了：

```text
shared:
  UserManager
  ClientUserManager
  ImmutableUser
  User / UserStore
  LoginPacket / LoginResponsePacket / UserSyncPacket / UserUpdatePacket
  若干双端共享事件类型

server-only:
  ServerUserManager
  ServerLoginEventArgs
```

其中 `ServerUserManager` 已经明显不只是“共享逻辑的一个具体实现”，而是服务端核心运行时组件：

```text
依赖 ServerAuthenticator 校验 session
维护 connectionId -> uuid 的在线映射
处理登录与掉线登出
广播 UserUpdate / UserSync
对接 Chat / Trade 等服务端扩展
```

另外，当前 `Extensions/Chat/Server` 与 `Extensions/Trade/Server` 已经直接消费 `ServerUserManager` 和 `ServerLoginEventArgs`。这进一步说明：

> 它们已经是稳定的服务端侧 API，而不是 Common 层里的偶然实现细节。

因此，`UserManagement` 最合适的方向是：

```text
UserManagement(shared)
  保留 user model、共享抽象、客户端实现、协议与双端共享事件

UserManagement.Server
  持有 ServerUserManager / ServerLoginEventArgs
  依赖 UserManagement(shared) + Authentication.Server
```

#### Utils：不建议整体拆成 `shared + server`

`Utils` 的情况和前两者不同。

当前 `Utils` 的主体实际上已经是共享基础设施：

```text
Framework proto / serialization / registry
ILoggable / LogEventArgs / LogLevel
IPersistent
ProtobufPacketHelper
TextHelper
TypeUrl
```

这里只有一小块明确属于服务端运行时：

```text
ConsoleHighlighting
HighlightType
```

这部分能力的特征是：

```text
只给服务端控制台日志增强使用
客户端没有依赖
对 framework shared contract 没有贡献
```

所以 `Utils` 更适合采用：

```text
Utils(shared)
  继续保留现有绝大多数 runtime-neutral 内容

ServerRuntime 或 Utils.Server(小型 server-only 程序集)
  仅承载 ConsoleHighlighting / HighlightType 这类控制台运行时能力
```

也就是说，`Utils` 不需要被整体拆成一个与 `Authentication` / `UserManagement` 对称的双层体系；只需要把 server-only 的尾巴切干净即可。

综合起来，这一项评估的最终结论应写成：

```text
Authentication：需要拆成 shared + server
UserManagement：需要拆成 shared + server
Utils：不建议整体双层化，只需把 server-only 运行时能力单独迁出
```

并且这一步在当前仓库里已经不是抽象讨论，而是有非常具体的落点。

当前代码至少已经暴露出三类典型信号：

```text
1. Extensions/Chat 和 Extensions/Trade 已经独立成新的项目树
2. 但 Authentication / UserManagement / Utils 仍然属于 Common 侧程序集
3. 这些 Common 程序集里依旧通过 csproj 直接编译 ../../Server 下的源码文件
```

例如当前真实情况包括：

```text
Common/Authentication/Authentication.csproj
  -> 编译 ../../Server/Authentication/ServerAuthenticator.cs
  -> 编译 ../../Server/Authentication/Session.cs

Common/UserManagement/UserManagement.csproj
  -> 编译 ../../Server/UserManagement/ServerLoginEventArgs.cs
  -> 编译 ../../Server/UserManagement/ServerUserManager.cs

Common/Utils/Utils.csproj
  -> 编译 ../../Server/ConsoleHighlighting.cs
```

这意味着当前仓库虽然已经完成了部分“物理目录迁出”，但还没有完成“编译时依赖方向收口”：

```text
Server 项目在引用 Common
Common 程序集却仍然反向吞入 Server 源码
最终形成的仍然是“目录分开了，但程序集职责还缠在一起”的过渡态
```

这里尤其要避免一个误区：

> 不要因为 `server-only` 代码文件已经放到了 `/Server` 目录，就误以为 `Common` 已经完成了最终瘦身；如果这些类型仍然编译进 `Authentication` / `UserManagement` / `Utils` 等 Common 程序集，它们仍然属于过渡态。

这一阶段更合适的实施策略，不是一次性把所有 Common 都打碎重组，而是按“最小可验证拆分”推进：

```text
第一步：先消除 Common csproj 对 ../../Server/*.cs 的直接 Compile Include
第二步：让 server-only 类型回到 Server 自己拥有的程序集
第三步：再视情况决定是否继续把 shared 部分细拆成更纯粹的 contract / runtime 层
```

如果结合当前项目状态，更稳妥的目标形态可以是：

```text
Utils
  保留 framework proto、序列化、文本工具、扩展注册等 runtime-neutral 能力
  把 ConsoleHighlighting 彻底移回 Server

Authentication
  保留认证协议、共享模型、客户端所需能力
  把 ServerAuthenticator / Session 拆到 Server 自有程序集

UserManagement
  保留 user model、同步协议、双端共享事件或抽象
  把 ServerUserManager / ServerLoginEventArgs 拆到 Server 自有程序集
```

这里的关键不是名字一定怎么取，而是引用方向必须变成：

```text
Server -> Common(shared contracts / abstractions)
而不是
Common -> 编译 Server 源码
```

从风险控制上看，`Phase 2.5` 最好明确排除几件容易一起被打包进来的大事：

```text
不要在这一阶段同步推进 net8/net10 升级
不要在这一阶段强行引入外部插件装载机制
不要在这一阶段重写 message / command / item 三管道语义
```

因为这一步的目标不是“功能升级”，而是：

```text
先把程序集边界拉直
先把引用方向理顺
先让后续 modern .NET 迁移拥有可控前置条件
```

因此，`Phase 2.5` 建议补上更清晰的完成条件：

```text
1. Common 下所有 csproj 不再直接 Compile Include ../../Server 下的源码文件
2. server-only 类型由 Server 或 Server 侧专属程序集显式持有
3. Server 对这些能力的依赖改成正常 ProjectReference，而不是反向编译注入
4. CommonBoundaryTests 这类边界校验继续保留，并用于防止回归
```

如果以上四点做到了，即使 `Authentication`、`UserManagement`、`Utils` 还没有一步拆成最理想的多层结构，也可以认为：

> `Phase 2.5` 已完成“程序集边界收口”的主要目标，并为 `Phase 3` 的服务端 modern .NET 迁移创造了更干净的起跑线。

把这一步单独记成 `Phase 2.5` 的好处是：

```text
既承认 Phase 2 的目录收口价值
又明确告诉后续实现者：程序集解耦还没做完
同时避免和 modern .NET 服务端迁移搅在一起，导致风险叠加
```

---

### Phase 3：服务端迁移到 modern .NET

这一步应当被提升优先级。

但这里必须先明确一个前提：

> Phase 3 不是“整个仓库一起升级到 modern .NET”，而是“在不破坏 RimWorld / Unity / Mono 客户端前提下，优先完成服务端运行时现代化”。

当前 `Client` 明确仍处于 Unity/Mono 生态，不能为了服务端迁移而被动跟随升级。因此，`Phase 3` 的正确目标不是：

```text
Client / Common / Server 一次性全切 modern .NET
```

而应当是：

```text
Client 继续保留 net472
Server 侧项目迁移到 modern .NET
服务端所依赖的 Common 项目改造成同时支持 net472 + net10.0
```

也就是说，这一阶段真正要建立的是：

```text
Client -> 继续消费 Common(net472)
Server -> 消费 Common(net10.0)
Common -> 成为双端兼容的 multi-target 共享层
```

基于当前目标和工具链判断，真实迁移目标建议分两段：

```text
第一阶段：
  先把服务端依赖链改造成可迁移结构
  Common(shared) -> net472 + net10.0
  Server 专属项目 -> net10.0

第二阶段：
  Server host -> net10.0
  Docker 与部署链路切到 .NET 原生容器
```

原因很简单：

```text
当前 Server 仍依赖 net472
Docker 仍使用 mono
线程与宿主模型仍偏旧
Client 又不能脱离 Unity/Mono 生态同步迁移
```

只要这一层不动，后续真正的插件化、可维护部署、现代诊断工具链都会被持续拖累。

这里还要补一条非常关键的实施约束：

> 由于当前 `Server` 并不是只依赖自己目录下的代码，而是直接依赖 `Common\Utils`、`Common\Connections`、`Common\Authentication`、`Common\UserManagement` 以及 `Extensions\Chat\Contracts`、`Extensions\Trade\Contracts` 等共享项目，所以 `Server` 不能孤立地单独切到 `net10.0`；必须先把服务端依赖到的共享程序集迁成可同时面向 `net472` 和 `net10.0` 的双目标项目。

因此，`Phase 3` 更合适的实施顺序应改写为：

```text
1. 先识别 Server 依赖链上的 shared 项目
2. 把这些 shared 项目从 net472 单目标改成 net472 + net10.0 双目标
3. 保证 Client 继续只消费 net472 输出，不改其运行时前提
4. 再把 ServerRuntime / Authentication.Server / UserManagement.Server / ChatExtension.Server / TradeExtension.Server 迁到 net10.0
5. 最后把 Server host 自身迁到 net10.0
6. 收尾 Dockerfile、发布物结构和诊断/运行命令
```

结合当前仓库，优先需要进入双目标或 modern .NET 迁移链的项目大致是：

```text
需要双目标（至少 net472 + net10.0）：
  Common/Utils
  Common/Connections
  Common/Authentication
  Common/UserManagement
  Extensions/Chat/Contracts
  Extensions/Trade/Contracts

继续保持 net472：
  Client
  ClientExtensionAbstractions
  ChatExtension.Client
  TradeExtension.Client

迁移到 net10.0：
  ServerRuntime
  Authentication.Server
  UserManagement.Server
  ChatExtension.Server
  TradeExtension.Server
  Server
```

这里还有一个口径需要明确写进文档，避免后续在实现中再次把 Common 做厚：

> Common 在 `Phase 3` 中的目标不是“继续兼容所以什么都能放”，而是“为了双端兼容，只保留共享契约级业务语义和 runtime-neutral 基础设施”。任何具体业务流程、状态管理、日志宿主行为、UI 行为和 host 特判，都不应借着 multi-target 的名义重新回流到 Common。

从 Visual Studio / 工具链角度，也建议把目标表述修正得更落地一些：

```text
开发机侧：
  Visual Studio 2026 18.x
  .NET 10 SDK
  .NET Framework 4.7.2 targeting pack

原因：
  需要同时构建 net10.0 的 Server 侧项目
  也需要继续构建 net472 的 Client 与 shared 输出
```

因此，`Phase 3` 的完成条件不应只写成“Server 能在 modern .NET 上运行”，而应更具体地定义为：

```text
1. Client 仍可按原有 net472 / Unity 前提编译
2. Server 及其 server-only 依赖可在 net10.0 下编译和运行
3. Server 所依赖的 Common/shared 项目已完成 net472 + net10.0 双目标
4. Common 未因兼容而重新吸纳 server-only / client-only 实现
5. Docker 构建与运行不再依赖 mono
```

---

### Phase 3.5：完成 Phase 3 的运行时与发布链收尾

当 `Phase 3` 的代码迁移主目标完成后，还需要补一个单独的小阶段来处理“已经能在 VS 中编译通过，但运行时与发布链尚未完全跟上”的问题。

这一阶段不再以“目标框架切换”为核心，而是以：

```text
modern .NET 运行时兼容清理
服务端宿主行为收尾
容器与发布链同步更新
迁移后技术债收口
```

为核心。

之所以建议单列 `Phase 3.5`，是因为当前仓库非常容易出现一种误判：

> 只要 `Server`、`Common`、`ChatExtension.Server`、`TradeExtension.Server` 在 Visual Studio 里全部重建通过，就意味着 modern .NET 迁移已经彻底完成。

这在代码迁移层面大体成立，但在运行时与部署层面并不成立。

更准确地说：

```text
Phase 3 完成的是：
  代码结构与目标框架迁移
  VS 构建链打通
  shared/server 边界在 modern .NET 下可编译

Phase 3.5 负责的是：
  modern .NET 下的运行时兼容问题
  过时 API 清理
  Dockerfile 与发布物结构迁移
  对旧版 host / extension 假设的最终收口
```

#### 当前需要进入 Phase 3.5 的原因

结合当前仓库状态，可以明确判断：

```text
1. Server / Common / Chat / Trade 的 modern .NET 编译链已经打通
2. 但仍然存在 Thread.Abort、RNGCryptoServiceProvider 等迁移后遗留警告
3. Google.Protobuf 本地工程仍有 modern SDK 警告噪声
4. Dockerfile 仍然没有跟随 Phase 3 做任何实质更新
```

尤其是最后一点需要单独强调：

> 当前 `Dockerfile` 仍然基本保持旧版本形态，没有体现 `Server -> net10.0` 的发布方式，也没有体现 chat/trade 已经迁移为 framework v2 下的官方扩展服务端实现。

这意味着目前的完成状态应理解为：

```text
代码迁移完成
!=
容器化发布链完成
```

进一步说，`Dockerfile` 当前仍然继承的是：

```text
Mono / net472 时代的服务端运行方式
旧版 PhinixServer.exe 容器启动语义
chat / trade 仍像早期那样随旧服务端形态一起发布的假设
```

而不是当前已经实现的：

```text
Server host = net10.0
ChatExtension.Server / TradeExtension.Server = Server 侧扩展程序集
shared contracts = net472 + net10.0 双目标
```

因此，`Phase 3.5` 需要把“编译通过”补全为“可按当前架构正确部署和运行”。

#### Phase 3.5 的核心任务

建议这一阶段聚焦以下四类任务：

```text
1. 清理 modern .NET 运行时不再安全或不再支持的旧 API
2. 收敛 shared/server 迁移后残留的构建与 SDK 警告
3. 迁移 Dockerfile、容器运行方式和发布物结构
4. 明确迁移完成后的 host / extension 发布边界
```

可以更具体地写成：

```text
API 清理：
  - Connections 中移除或替换 Thread.Abort
  - Authentication 中将 RNGCryptoServiceProvider 替换为 RandomNumberGenerator 新写法
  - 评估其他仅在 modern .NET 下出现的过时 API 或平台兼容警告

构建噪声收口：
  - 评估并清理 Google.Protobuf 本地工程的 NETSDK1212 等 warning
  - 区分“可接受历史兼容 warning”和“会影响未来维护的真实 warning”

服务端发布链迁移：
  - 将 Dockerfile 从 Mono / net472 时代的构建与运行方式迁移到 modern .NET
  - 调整发布物输出结构，使其符合 net10.0 Server host 的运行方式
  - 更新容器入口命令、工作目录和依赖复制方式

架构口径收尾：
  - 明确 chat / trade 虽然仍可作为官方随发扩展存在，但其发布方式应体现为 extension 体系的一部分
  - 避免 Dockerfile、部署脚本或运行目录重新把 chat / trade 写回“半内建直接实现”的旧语义
```

这里尤其要加一条发布边界要求：

> `Phase 3.5` 中对 Dockerfile 和发布链的调整，不能只是把 `mono PhinixServer.exe` 机械替换成 `dotnet` 启动命令；更重要的是让发布物结构与当前 framework v2 + official extensions 的真实架构一致。

也就是说，这一阶段要防止出现一种“表面 modern .NET，内里还是旧架构打包思路”的半迁移状态。

#### Dockerfile 需要特别补充的修订口径

建议在这一阶段把 `Dockerfile` 的状态明确记成：

```text
当前 Dockerfile 未完成迁移
它仍然是旧版部署脚本
并且仍然停留在早期 chat / trade 直接实现版服务端的发布语境
```

这里的“未完成迁移”不仅仅指基础镜像旧，还包括：

```text
没有体现 Server -> net10.0 的发布流程
没有体现 shared/server 项目已经重新分层
没有体现 ChatExtension.Server / TradeExtension.Server 作为服务端扩展程序集的当前结构
没有体现未来继续向 Mods/外部插件方向演进所需要的目录边界
```

因此，文档里应当避免把 `Phase 3` 的完成直接写成“服务端 modern .NET 与 Docker 部署都已完成”，更准确的表达是：

> `Phase 3` 完成的是代码与 VS 构建链迁移；`Phase 3.5` 才负责把 Dockerfile、发布链和运行时技术债补齐。

#### Phase 3.5 的完成条件

建议把这一阶段的完成条件写得比 `Phase 3` 更运行时导向：

```text
1. Server 侧 modern .NET 代码迁移后的主要过时 API 已完成替换或有明确处置策略
2. 共享层与服务端链路只剩可接受的历史兼容 warning，不再存在高风险运行时 warning
3. Dockerfile 已从旧 Mono / net472 方案迁移到 modern .NET 服务端发布方式
4. 容器入口、发布物结构和工作目录与当前 extension 架构一致
5. chat / trade 在发布链中被表述为官方扩展，而不是重新退回“主程序内建直接实现”的旧口径
```

如果以上五点完成，才更适合说：

> 服务端 modern .NET 迁移不仅在代码层完成，也在运行时和部署层真正完成。

---

### Phase 4：引入新的服务端处理流水线，提供 extension 可拦截的统一入口

当前 `Server\Framework\PhinixFrameworkServer.cs` 的真实行为仍然是：

```text
1. 做认证、登录态与 capability 校验
2. 遍历命中的 server message / command handler
3. 命中 handler 后根据 result.Action 决定继续还是返回
4. 没有统一的 default process 节点
5. 没有统一的 outbound interception 节点
```

这意味着当前框架虽然已经具备 handler/action 雏形，但它仍然不是一个“extension 可明确插入、接管、阻断、审计”的服务端流程模型。

因此，`Phase 4` 的目标不应再是继续解释旧的 `handler + action` 语义，而应当升级为：

> 在 server framework 层引入一套显式的处理流水线，把入站消息、入站命令、服务端主动发送与广播统一纳入可拦截流程，为 extension 提供稳定且平权的接入点。

这里要特别强调：

> `Phase 4` 的优先级高于历史实现习惯。原先的业务逻辑如果不符合这套新流程，就不为了兼容它去扭曲 Phase 4 设计，而是按新的服务端边界重写。

换句话说，这一步要建立的是长期有效的 server-side interception boundary，而不是围绕旧实现做最小代价适配。

#### Phase 4 的目标流程

建议把服务端的目标流程明确定义为下面五段，而不是继续隐式依赖“谁先命中 handler 谁就结束”的遍历语义：

```text
IngressValidation
  认证、登录态、capability、基础包合法性校验
  校验失败直接拒绝，不进入 extension 流程

PreHandleInterception
  extension 可读取、改写、阻断、声明接管当前消息或命令

DefaultProcess
  若没有 extension 接管或阻断，则进入默认服务端流程

PostHandleObservation
  用于记录、审计、补充副作用，不再承担主要业务接管职责

OutboundInterception
  所有 SendMessage / BroadcastMessage 都必须先经过出站拦截层，再真正发给连接
```

这五段分别承担不同职责：

#### 1. IngressValidation

这一层只负责宿主必须保证的前置条件，例如：

```text
是否已认证
是否已登录
session 是否有效
capability 是否协商成功
packet 基础结构是否合法
```

这部分不应交给业务扩展决定，因为它属于 host 安全边界。

#### 2. PreHandleInterception

这是 `Phase 4` 真正新增的核心能力。

extension 应在这里获得统一的前置拦截入口，用于：

```text
读取当前 packet / command
改写 payload
基于 sender / capability / content 决定是否放行
直接接管默认流程
完全阻断默认流程
```

这一步的意义不是“再加一层 handler”，而是明确告诉后续实现者：

> extension 的主要接管点应该在 default process 之前，而不是靠宿主或官方扩展内部的零散分支去实现插桩。

#### 3. DefaultProcess

如果前置拦截阶段没有接管也没有阻断，才进入默认服务端流程。

这里需要明确一个过去容易模糊的问题：

> `chat`、`trade` 虽然仍可以是官方随发扩展，但它们在流程地位上不应高于其他 extension。它们属于 `DefaultProcess` 的默认处理能力，而不是绕过统一入口的“半内建特殊模块”。

因此，这一阶段的设计必须满足：

```text
chat 是 default process 的一种
trade 是 default process 的一种
future plugins 也应能以相同方式挂接
任何 extension 都可以在 PreHandleInterception 中改写、阻断或接管这些默认流程
```

同时必须补充一条硬约束：

```text
原先 chat / trade 或其他业务流程如果与新 pipeline 冲突，不保留旧特判
不允许为了迁就历史业务逻辑而让官方扩展继续绕过统一入口
旧业务只要不符合新流程，就按新的服务端边界重写
```

#### 4. PostHandleObservation

这一层不是为了再做一次主业务处理，而是用于：

```text
审计
日志
指标
通知型副作用
非主链的扩展观察
```

它的职责应显式弱于 `PreHandleInterception` 和 `DefaultProcess`。

也就是说，后置阶段应主要承担“看见已经发生的事并做补充”，而不是重新争夺主控制权。

从阶段划分上看，这部分不应被推迟到未来插件化阶段，而应算作 `Phase 4` 本身的完成条件之一。

原因很简单：

```text
没有 PostHandleObservation 的实际使用者
=
服务端虽然声明支持 observation
但 pipeline 仍然缺少真实的后置职责承载点
```

因此，`Phase 4` 更合适的收口口径应补充为：

> 在 framework 层补齐 `IServerMessageObserver` / `IServerCommandObserver` 的真实执行链，并至少提供一个内置的日志观察器作为官方后置职责落点；示例代码不是目标，但“日志/审计类观察器已经实际接入 pipeline”应视为本阶段完成条件之一。

#### 5. OutboundInterception

这也是当前实现里缺失、但为了 extension 可拦截而必须新增的一层。

当前真实情况是：

```text
服务端扩展通过 ServerFrameworkContext.SendMessage / BroadcastMessage 直接发包
当前没有统一的出站扩展点
一旦扩展自己发包，宿主没有机会统一做审计、改写、过滤或按连接裁剪
```

因此，`Phase 4` 之后，所有服务端出站都应先进入 `OutboundInterception`，再真正写入 socket。

这一层至少应支持：

```text
按目标连接、目标 capability、消息类型决定是否放行
改写出站 packet
阻断单个连接或整个广播
把日志、审计、过滤逻辑从业务 handler 中抽离出来
```

这里也要同步更新语义约束：

> `SendMessage` / `BroadcastMessage` 的目标语义应从“直接发 socket”改成“提交到 outbound pipeline”，由 pipeline 决定最终如何发送。

#### 结果语义需要重新定义

`MessageHandlingResultAction` 当前虽然已经存在，但其语义对于新流程来说不够稳定，尤其是：

```text
Handled 更接近“消费完成”
ReplacePayload 更接近“改写后重试”
SuppressDefault / StopPropagation 存在明显重叠
LegacyFallback 带有过渡期味道，不适合作为长期模型
```

因此，文档中的目标态语义应改写为一套更适合显式流水线的动作集合：

```text
Continue:
  不做决定，交给当前阶段下一个 extension

Replace:
  替换当前 packet / command 后继续当前阶段

Handle:
  当前 extension 接管处理，跳过 DefaultProcess

Block:
  终止处理并丢弃，不进入默认流程，也不产生出站

Observe:
  仅用于后置观察阶段，不改变主流程
```

与现有实现的迁移口径可明确写成：

```text
ReplacePayload -> Replace
Handled -> Handle
SuppressDefault + StopPropagation -> Block
LegacyFallback -> 不再作为长期语义保留，只允许作为过渡兼容说明
```

这里要特别防止一种常见误区：

> `Phase 4` 的目标不是“给现有枚举换个名字”，而是让这些动作能够明确地对应到前置拦截、默认处理、后置观察和出站处理的各自职责。

#### 旧业务服从新流程，而不是新流程兼容旧业务

这一节建议作为 `Phase 4` 的显式迁移原则保留在文档中。

核心判断标准不应是：

```text
旧代码还能不能继续跑
```

而应是：

```text
是否符合统一校验
是否符合统一拦截
是否符合统一默认处理
是否符合统一出站
```

如果某段历史业务逻辑仍依赖下面这些旧模式：

```text
主程序直达处理
官方扩展特判
绕开统一出站链路
先有业务结论、后补 framework 包装
```

则它应被视为待重写逻辑，而不是 `Phase 4` 的兼容例外。

换句话说：

> `Phase 4` 要建立的是未来几年都可复用的 server framework 边界，而不是把旧流程原封不动搬进一个新名字下面。

#### 后续实现应围绕哪些接口和上下文设计

这一轮文档修订不要求马上把接口写进代码，但应提前把目标接口层级写清楚，作为后续实现的设计依据：

```text
IServerInboundMessageInterceptor
IServerInboundCommandInterceptor
IServerDefaultMessageHandler
IServerDefaultCommandHandler
IServerMessageObserver
IServerCommandObserver
IServerOutboundPacketInterceptor
```

同时建议把上下文需求也写明，避免实现时再回到“直接把宿主细节塞进 handler”：

```text
入站上下文保留：
  ConnectionId
  SessionId
  SenderUuid
  capability 查询
  日志接口

出站上下文补充：
  目标连接集合
  发送来源
  原始触发扩展标识
```

这里的关键不只是接口数量，而是接口边界：

> extension 应通过统一的 inbound / default / outbound contract 参与流程，而不是继续直接耦合宿主内部发送细节。

#### 当前客户端是否仍能支持这一版服务端

基于当前实现，答案是：

> 只要 `Phase 4` 的改动继续保持在服务端内部处理流程层，而不改变 framework packet kind、messageType、payload 契约和 capability negotiation 基础语义，那么当前 client 仍然可以继续对接这版 server。

原因在于，当前客户端真正依赖的是：

```text
KindHello / KindCapabilities / KindMessage / KindCommand
messageType 与 payload 契约
capability negotiation 结果
render / client handler / display message 语义
```

而不是服务端内部到底是：

```text
直接遍历 handler
还是先经过 pre-handle / default / observation / outbound pipeline
```

这意味着 `Phase 4` 当前这类“服务端内部流程重构”对 client 是相对透明的。

但这里也要明确写出边界条件，防止后续实现者误判：

```text
如果改动只发生在 server 内部 pipeline 分层，client 仍可继续支持 server
如果改动改变了现有 messageType / commandType / payload 结构，client 就需要同步迁移
如果改动引入新的 capability 前提而 client 未声明对应能力，也会出现功能退化或被 server 拒绝
```

因此，这一阶段对客户端兼容性的更准确结论应写成：

> `Phase 4` 当前目标并不要求 client 同步重写；但它要求后续任何服务端 pipeline 重构都不得随意变更既有 wire contract。只要 wire contract 保持稳定，当前 client 仍然可以支持当前 server。

#### 文档中应保留的验收场景

为了让后续实现者知道 `Phase 4` 是否真正落地，建议在文档中至少保留下面这些验收场景：

```text
1. 未命中任何 pre-handle extension 时，消息进入默认 chat / trade 处理
2. 扩展在 pre-handle 阶段把聊天消息改写后，官方 chat 默认流程处理改写后的内容
3. 扩展在 pre-handle 阶段返回 Block 后，官方默认流程不再执行，且无出站发送
4. 扩展在 pre-handle 阶段返回 Handle 并主动广播时，官方默认流程被跳过，但广播仍会进入出站拦截层
5. 出站拦截器可按连接 capability 阻止广播发往部分客户端，而不影响其余目标
6. 若没有默认处理器且也没有 extension 接管，框架记录 warning 并安全丢弃
7. 某个历史业务流程若依赖绕过统一 pipeline 的旧入口，在 Phase 4 设计下应被标记为需重写，而不是继续保留特殊分支
```

如果这些场景无法成立，就说明实现者只是给旧 handler 模式补了几层包装，而没有真正完成 `Phase 4`。

---

### Phase 5：把“动态发现”升级为“外部插件加载”

结合当前代码，`Phase 5` 的起点需要写得更准确一些：

```text
Server:
  已经进入“official extension 独立编译 + 构建复制到输出目录 + 宿主启动时显式预加载程序集”的过渡态

Client:
  仍然直接 ProjectReference ChatExtension.Client / TradeExtension.Client
  启动期仍然要求 built-in chat / trade API 必须存在

Registry:
  仍然只会对“已经加载到 AppDomain 的程序集”做反射发现
  还没有真正的插件目录扫描和外部装载策略
```

因此，当前并不是“client/server 都还停留在纯程序集内发现”，而是：

> 服务端已经部分进入 `Phase 5` 前的运行时装载过渡态；客户端则仍然明显停留在编译期强耦合阶段；两端尚未形成一致的 official extension 装载语义。

这里还需要再把 `Phase 5` 的最终目标写得更严格一些，避免后续实现停留在“宿主不再依赖具体实现类，但仍然写死依赖某几个官方业务接口”的半解耦状态。

> `Phase 5` 的理想完成标准，不是 `Client` / `Server` 从依赖 `ChatExtension.*` / `TradeExtension.*` 具体实现，升级成依赖 `IClientChatService` / `IClientTradeService` 这类固定业务接口；而是宿主只依赖通用扩展契约与通用挂载点，未来新增业务扩展不需要修改 host/core 代码即可被发现、加载、激活并接入系统。

换句话说，判断 `Phase 5` 是否真正完成的更高标准应是：

```text
如果未来新增一个全新的业务扩展（例如某种新的经济/通知/信息面板扩展），
只要它满足统一的 extension contract 并被放入扩展目录，
host 就不需要为了“认识它”而新增专用字段、专用接口解析、专用 UI 入口或专用启动逻辑。
```

这也是“插件平权”是否落到运行时现实中的最直接验证方式：

```text
增加一个新业务扩展
不修改 host/core 代码
扩展仍可被动态发现、激活、协商能力并挂接自己的 UI / 行为入口
```

如果做不到这一点，就说明当前系统仍然停留在：

```text
宿主不再硬编码具体实现类
但仍然硬编码若干官方业务接口
```

这种状态比旧架构更好，但还不能算 `Phase 5` 终态，只能算插件化过渡态。

未来如果要支持 Docker 中的 Mods 目录，至少要补：

```text
1. 插件目录扫描
2. AssemblyLoad / 兼容的程序集装载策略
3. 插件隔离与错误诊断
4. 启动期注册与失败降级
```

建议目标目录再定义为：

```text
/app/Mods
```

但需要明确：

> 这是未来阶段目标，当前仓库尚未实现；当前 registry 仍然只负责发现“已加载程序集中的 extension module”，并不负责从 `Mods` 目录主动装载程序集。

而且这一阶段还有一个重要的架构目标：

> 外部插件加载机制一旦建立，就必须同时适用于 `chat` / `trade` 这类官方扩展和未来新增扩展，而不是只给“第三方插件”走另一条弱化路径。

因此，`Phase 5` 中宿主真正应该直接依赖的，不应再是 `chat` / `trade` 这类业务语义接口本身，而应逐步收敛到更通用的扩展点，例如：

```text
feature / panel / tab / window provider
notification / badge / unread count provider
command / action / menu contribution provider
lifecycle / capability / host service contract
```

而不应长期停留在：

```text
host 直接解析 IClientChatService
host 直接解析 IClientTradeService
host 为每个官方业务扩展保留一组专用字段和专用 UI 流程
```

否则未来每增加一个新业务扩展，仍然需要：

```text
改 host 字段
改 host 初始化
改 host UI
改 host 路由或事件转发
```

这与真正的插件平台目标是冲突的。

这里还需要明确一条阶段边界，避免把不同层次的工作混在一起：

> `Phase 4` 负责的是“服务端内部 pipeline 平台化”；而 `Phase 5` 负责的是“官方扩展与未来插件在装载、发布、协作和运行时地位上的彻底平权化”。

换句话说，下面这些内容更适合放在 `Phase 5`，而不应继续算作 `Phase 4`：

```text
chat / trade 的彻底去特权化收口
官方扩展从“显式预加载 + 构建复制”过渡到真正插件目录装载
宿主不再显式知道某几个官方扩展程序集名
客户端与服务端对 official extensions 采用一致的装载语义
```

这里还需要把“官方扩展如何从当前过渡态进入真正插件化”写得更明确一些。

按当前代码看，服务端和客户端并不处于同一个起跑线：

```text
Server 当前可接受的过渡态：
  不直接 ProjectReference ChatExtension.Server / TradeExtension.Server 实现工程
  构建时单独编译 official server extensions
  将 DLL 复制到宿主输出目录
  启动期用程序集名显式预加载，再交给 registry discovery

Client 当前仍未达到的过渡态：
  仍直接 ProjectReference ChatExtension.Client / TradeExtension.Client
  仍把官方扩展 API 视为宿主启动成功的硬前提
```

> 理想目标不应再是 `Client.csproj` / `Server.csproj` 直接通过 `ProjectReference` 强绑定 `ChatExtension.*` / `TradeExtension.*`，然后把它们当作宿主构建产物的一部分复制到最终目录；更合理的目标是：`chat` / `trade` 先各自独立编译成 official extension DLL，再由客户端和服务端宿主从各自的 extension/plugin 目录扫描、加载并发现它们。

这里还需要补一个过渡期口径，避免实现者在拆除编译期耦合后不小心把当前功能做没：

> 在真正的 extension/plugin 目录扫描与装载体系完成之前，可以暂时保留“宿主显式预加载官方扩展程序集”的启动逻辑，也可以暂时保留“构建时把官方扩展 DLL 复制到宿主输出目录”的打包步骤；但这类过渡措施只能解决“如何把 DLL 放到运行目录”这个部署问题，不能重新引入 `Server.csproj` / `Client.csproj` 对扩展实现工程的编译期类型依赖。

换句话说，过渡期可以接受的是：

```text
宿主不直接引用扩展实现类型
宿主通过程序集名显式加载官方扩展
构建链把 official extension DLL 复制到宿主输出目录
```

而不应重新退回：

```text
Server/Client 主工程重新 ProjectReference 扩展实现工程并直接使用扩展实现类型
```

但如果目标是“完全插件平权”，`Phase 5` 还应继续把过渡措施本身纳入清理范围，而不是长期保留：

```text
Server.cs / Client.cs 中按程序集名显式预加载官方扩展
构建期在宿主 csproj 中写死 official extension DLL 复制步骤
官方扩展继续以“宿主默认知道它存在”为前提组织启动流程
```

这些做法在 `Phase 4` 结束时可以接受，因为它们仍然是“运行时装载过渡措施”。
但如果到了 `Phase 5` 还长期保留，就说明：

```text
官方扩展仍然是特殊公民
宿主仍然知道谁是“内定插件”
第三方插件与官方插件仍不是同一套装载路径
```

这会让“插件平权”停留在口径上，而不是落到运行时现实里。

同理，如果到了 `Phase 5` 宿主仍然需要显式写出：

```text
chat service 字段
trade service 字段
chat tab / trade tab 固定入口
某个业务扩展的专用未读计数或通知逻辑
```

那么这说明“程序集装载”虽然变动态了，但“业务挂载”仍然是静态的，阶段目标也仍未完全达成。

也就是说，到了这一阶段，官方扩展的理想发布与装载路径应逐步变成：

```text
1. host / core 单独编译
2. ChatExtension.Client / TradeExtension.Client 独立编译
3. ChatExtension.Server / TradeExtension.Server 独立编译
4. 客户端从自己的 extension 目录加载官方客户端扩展
5. 服务端从自己的 extension 目录加载官方服务端扩展
6. registry 再对“已加载程序集”执行 module discovery / register / activate
```

这样做的意义不是“把 DLL 换个地方放”，而是明确宿主与官方扩展的真实边界：

```text
host 负责启动与加载
extension 负责 capability / handler / API / 状态
chat/trade 只是 official extensions
而不是宿主内部模块
```

如果进一步追求“完全插件平权”，`Phase 5` 还应补一条更强的目标：

> `chat` / `trade` 不仅要在发布形态上变成 official extensions，还要在代码组织与运行职责上逐步摆脱“默认官方功能思维”，最终与未来插件共享同一套装载、发现、能力协商、默认处理、观察与降级机制。

进一步说，`Phase 5` 后期应把“官方业务功能如何进入 UI/宿主主流程”也统一到通用扩展模型中，而不是继续让 host 直接拥有：

```text
固定 chat 页签
固定 trade 页签
固定 chat/trade 专属入口控件
固定 chat/trade 专属状态聚合逻辑
```

更合理的终态应是：

```text
host 提供通用 UI 挂载点
扩展自己声明要贡献的 tab / panel / window / badge / action
host 只负责容器与生命周期，不负责认识具体业务名字
```

这并不等于要在 `Phase 5` 一开始就推倒重写 `chat` / `trade`，但它要求文档把下面这件事说清楚：

```text
chat / trade 可以在 Phase 4 结束时继续作为 default process 的官方实现存在
但它们在 Phase 5 中应继续去掉剩余特权
最终目标是：它们只是 official extensions，不再是“宿主预设功能”
```

从客户端协同角度，也建议把 `Phase 5` 的边界写得更准确一些：

> `Phase 5` 不是要求 client 先于 server 全量重写，但它要求 client 与 server 最终采用一致的 official extension 装载语义。当前代码里 server 已经先走到“显式预加载 + 构建复制”的过渡态，因此 `Phase 5` 的直接前置任务之一其实是先把 client 也收口到至少同等级的过渡形态，再继续推进真正的插件目录发现。

需要特别强调的是：

> 这一步并不要求 `chat` / `trade` 立刻变成“网络下载式第三方插件”，但它至少要求它们先摆脱“宿主主项目强引用 + 主构建链直接内嵌”的当前路径，转而进入“独立编译、独立落位、由宿主加载”的 official extension 形态。若在过渡期仍需保留显式预加载逻辑，也应把它理解为运行时装载过渡措施，而不是重新接受编译期反向耦合。

否则会出现两套插件等级：

```text
官方插件 = 半内建
其他插件 = 外挂
```

这会直接破坏前面建立的平权边界。
同时需要清理掉之前的显式加载逻辑

另外，结合当前仓库，`Phase 5` 还有一个很现实的配套任务不能省略：

> Docker / 发布链必须先对齐现在的项目结构与扩展产物结构，否则即使本地完成目录扫描，容器镜像和发布目录也无法正确承载 official extensions。当前根目录 `Dockerfile` 仍是 Mono 时代写法，而且还没有把 `Extensions/` 目录纳入构建上下文，这说明部署链本身还停留在更早的阶段。

因此，`Phase 5` 更合适的完成条件还应补充为：

```text
1. 宿主不再通过实现工程引用或显式程序集名特判官方扩展
2. official extensions 与第三方 extensions 走同一套目录发现与装载流程
3. chat / trade 在运行时地位上不再高于未来插件
4. client 与 server 对 official extensions 的装载语义保持一致
5. Phase 4 中保留的“显式预加载 + 构建复制”过渡措施已退出主路径
6. Docker / 发布产物结构已经能够承载 official extensions 与未来 Mods 目录
```

如果按更严格的“完全插件平权”口径来写，建议再追加两条完成条件：

```text
7. host 不再为 chat / trade 保留专用业务接口解析与专用启动分支
8. 新增一个新的业务扩展时，不需要修改 host/core 代码即可完成发现、激活与基础 UI 挂接
```

这两条非常关键，因为它们决定了 `Phase 5` 的工作量判断不能再按“只做目录扫描和加载器改造”来估计，而必须按“运行时装载 + 业务挂载模型 + 宿主职责收缩 + 官方扩展去特权化”这一整组改动来估算。

因此，基于当前仓库现状，更准确的工作量判断应写成：

> `Phase 5` 不是一个“小型加载器重构阶段”，而是一个中到大型架构收口阶段。它既包含扩展目录发现、程序集装载、发布链对齐，也包含 host 对官方业务接口和固定 UI 入口的去特权化收口。如果目标是“未来新增业务扩展不改 host 代码即可接入”，那么 `Phase 5` 的复杂度显著高于单纯的 DLL 扫描加载改造。

### Phase 5.5：按“只保留双端共享实现”的标准继续硬化 Common 边界

在 `Phase 5` 之后，还应补一个专门的小阶段，用来处理一个很容易在插件化推进过程中再次恶化的问题：

> 即使 official extension 的装载方式已经逐步平权，如果 `Common` 里仍长期混放 client-only / server-only 运行时实现，那么宿主边界和编译边界仍然会继续彼此污染。

这一阶段的目标不是把所有代码都机械拆成更多项目，而是按照更严格的工程标准重新判断：

```text
什么可以继续留在 Common
什么应该只保留抽象/契约
什么必须拆成 Client / Server 各自实现
```

这里建议明确采用下面这个判断规则：

> `Common` 里可以保留“协议、抽象、DTO、纯工具逻辑、以及确实双端都会直接复用的 runtime-neutral 实现”；但只要某段代码已经绑定某一端的连接生命周期、宿主事件、状态管理、文件落位策略或业务处理流程，就不应继续以“共享实现”的名义长期停留在 Common。

#### 当前扫描后，仍适合保留在 Common 的部分

结合当前代码，下面这些内容仍然属于合理的共享层范畴：

```text
FrameworkPacket / FrameworkSerialization / protobuf contracts
extension interfaces / API registry / context DTO
ILoggable / IPersistent / LogEventArgs / LogLevel
TypeUrl / ProtobufPacketHelper / 一般性文本或协议辅助工具
FileSystemExtensionStorageProvider
ExtensionHostContext
PhinixExtensionRegistry（至少其 discovery / register / activate 主体逻辑）
```

这些内容虽然有的会触碰文件系统、反射或时间/ID 生成，但它们仍然满足两个关键条件：

```text
1. 不直接包含 client-only 或 server-only 业务流程
2. 两端都可以在不改变语义的前提下直接复用
```

因此，`Common` 并不是要变成“只能放 enum 和 proto”，而是应保留：

```text
共享协议
共享抽象
双端共用且运行时中立的基础实现
```

#### 当前最明确应该拆出不同实现的部分

这次扫描后，有几类代码已经明显越过了“共享实现”边界。

##### 1. Authentication 中的 ClientAuthenticator

当前 `Common/Authentication/ClientAuthenticator.cs` 明确绑定了：

```text
NetClient 生命周期
客户端凭据存储文件
客户端凭据请求回调
客户端会话续期定时器
```

这不是共享抽象，而是典型的 client runtime implementation。

更合适的结构应是：

```text
Authentication(shared):
  Authenticator 抽象
  wire packets / auth enums / shared credential DTO

Authentication.Client:
  ClientAuthenticator
  凭据读取/保存
  凭据请求回调适配
```

如果后续还要进一步解耦，本地凭据落盘模型 `CredentialStore` 其实也更偏客户端本地状态，而不是严格意义上的双端共享协议。

##### 2. UserManagement 中的 ClientUserManager

`Common/UserManagement/ClientUserManager.cs` 当前明确绑定了：

```text
NetClient
ClientAuthenticator
客户端登录成功/失败事件
本地用户缓存同步
断线后本地状态清理
```

这同样不是共享实现，而是客户端会话与用户目录同步逻辑。

更合适的结构应是：

```text
UserManagement(shared):
  UserManager 抽象
  ImmutableUser
  登录/同步/更新协议
  共享事件参数类型

UserManagement.Client:
  ClientUserManager
  客户端用户缓存同步与事件派发
```

也就是说，`UserManagement` 当前的问题不在协议，而在于客户端运行时管理器仍编译进了共享程序集。

##### 3. Connections 中的 NetClient / NetServer

这部分是一个很典型、也很容易因为“反正两边都用 LiteNetLib”而被误判的边界。

从“代码都在同一个网络库之上”这个角度看，它们像是共享实现；但从软件工程职责边界看，它们其实分别承担：

```text
NetClient:
  客户端连接建立、探测、断线事件、客户端轮询线程

NetServer:
  服务端监听、连接表、连接建立/关闭事件、服务端轮询线程
```

这说明它们不是“同一个实现的双端复用”，而是：

```text
同一技术栈下的两套端侧实现
```

因此，更符合长期边界的结构应是：

```text
Connections(shared or abstractions):
  NetCommon
  packet handler delegate
  共享异常/事件参数

Connections.Client:
  NetClient

Connections.Server:
  NetServer
```

这一项的优先级可以略低于 `ClientAuthenticator` / `ClientUserManager`，因为它不直接阻塞当前插件装载工作；但从“Common 只保留双端共享实现”的标准看，它同样属于应拆项。

##### 4. Framework 中的 ServerPipelineRunner

`Common/Utils/Framework/ServerPipelineRunner.cs` 目前已经是一个命名上就非常明确的 server-only runtime component。

它承担的是：

```text
服务端 inbound / default / observation / outbound 执行链编排
```

这不属于共享实现，也不应继续放在 `Common` 运行时层里。

更合适的归位是：

```text
Server/Framework
或单独的 Framework.ServerRuntime
```

这里要强调：

> `ServerPipelineRunner` 可以继续依赖 Common 中的 handler contracts 和 context DTO，但它自己的执行编排实现不应继续作为 shared runtime 的一部分存在。

#### 暂时不建议过度拆分，但应继续观察的部分

有几块逻辑虽然目前还放在 Common，但不建议在这一阶段机械地立刻继续拆碎。

##### 1. PhinixExtensionRegistry

当前 `PhinixExtensionRegistry` 会同时登记：

```text
client handlers
server handlers
server interceptors / observers / outbound interceptors
legacy adapter
```

从“纯粹性”上看，它内部确实已经知道了一部分 server-specific handler shape。  
但从当前工程现实看，它仍然是：

```text
client 和 server 都共同使用的模块发现/注册核心
```

所以更合理的判断不是“现在立刻拆”，而是：

> 可以在 `Phase 5.5` 先保留 registry 主体于 Common，但在后续若要继续严格收口，可再把 server-specific registration glue 从 registry 主体中分离出去，让 Common 保留 discovery + module-first registration core。

##### 2. FileSystemExtensionStorageProvider

这个实现确实依赖文件系统，但它目前并不直接绑定 client-only / server-only 行为，也没有混入业务流程。

因此它更适合被视为：

```text
runtime-neutral 的默认存储适配器
```

只要未来不把客户端落位规则、服务端专用目录策略或容器部署特判继续硬塞进去，它可以继续留在 Common。

#### Phase 5.5 的推荐任务清单

基于当前代码，`Phase 5.5` 更适合定义为下面几项：

```text
1. 把 ClientAuthenticator 从 Common/Authentication 拆到独立的客户端实现项目
2. 把 ClientUserManager 从 Common/UserManagement 拆到独立的客户端实现项目
3. 把 NetClient / NetServer 从 Common/Connections 拆成 shared abstractions + client/server implementations
4. 把 ServerPipelineRunner 从 Common/Utils/Framework 迁到 Server 侧运行时层
5. 保留 Common 中真正双端共享的 framework contract、registry core、DTO 和工具逻辑
6. 对仍暂留 Common 的 runtime 实现逐项标注“保留理由”，防止后续继续混入宿主逻辑
```

#### Phase 5.5 的完成标准

这个阶段不应以“目录看起来更整洁”作为完成定义，而应以更硬的边界标准来判断：

```text
1. Common 程序集不再直接编译 client-only 生命周期管理器
2. Common 程序集不再直接编译 server-only pipeline runner
3. 双端仍可共享同一套 wire contract、framework contract 和基础 DTO
4. Client / Server 的宿主实现通过抽象或共享 contract 协作，而不是继续把端侧实现塞回 Common
5. 新增功能若属于某一端运行时实现，默认不再进入 Common
```

如果 `Phase 5` 解决的是：

```text
official extension 的装载平权
```

那么 `Phase 5.5` 解决的就是：

```text
Common 自身的边界平权
```

也就是让 `Common` 不再因为历史便利而继续充当“任何双端都可能碰到、于是都先塞进来”的中间层。

---

## 7. 对 Docker 与部署的修订结论

原文里“服务端迁移到 modern .NET 并支持 Docker 插件目录加载”的方向依旧成立，但需要按现状改写。

当前真实部署情况是：

```text
Docker build:
  mono:latest

Docker runtime:
  alpine-mono

启动方式:
  mono PhinixServer.exe

volume:
  挂载整个 /server
```

所以修订后的结论应写成：

> 当前 Docker 方案只是把 net472/Mono 版服务端容器化，尚未达到 modern .NET 原生容器部署，也尚未具备独立 Mods 插件目录加载能力。

---

## 8. 修订后的总体判断

如果只用一句话总结当前局面：

> Phinix 已经迈出了从“共享实现型 Common”走向“framework + extension”架构的关键一步，但目前仍停留在过渡态：Common 仍然偏厚，Server 仍是 Mono/net472，官方 chat/trade 扩展仍与主仓同发布，真正的外部插件装载和 modern .NET 服务端还没有完成。

因此，接下来最重要的不是再抽象地讨论“要不要搞扩展架构”，而是按下面顺序持续收口：

```text
1. 收紧 Common 边界
2. 迁移 Server 到 modern .NET
3. 把官方扩展从 Common 树中继续剥离
4. 统一 server-side handler / fallback 语义
5. 最后再做外部 Mods 目录和真正插件化装载
```

这五步背后其实都服务于同一个更高层目标：

> 不让 `chat` / `trade` 继续作为“超级公民”固化在架构中心，而是把它们降回 extension 应有的位置，为未来任何新插件保留同等接入机会。

---

## 9. 最终建议

基于当前实现，建议把这次架构重构的正式需求重写为：

> 在现有 framework v2、能力协商、extension module、API registry 和三类处理链的基础上，继续推进 Phinix 的 C/S 边界重构。重点不是从零发明一套新框架，而是把 Common 收缩为共享协议与抽象层，把 chat/trade 等官方扩展逐步从 Common 中拆出，把 Server 从 Mono/.NET Framework 4.7.2 迁移到 modern .NET，并为未来 Docker 下的外部 Mods 插件装载建立稳定边界。

这份表述会比原文更符合当前仓库真实状态，也更适合作为下一阶段的实际迁移基线。

另外，还需要把下面这条作为显式需求长期保留：

> `chat` 和 `trade` 可以因为历史原因先被官方随程序分发，但它们不应在架构上被定义为高于其他插件的“超级公民”。未来新增插件只要满足协议和扩展契约，就应与 `chat` / `trade` 处于同等地位，并通过同一套解耦边界接入系统。

---

## 10. 工作量评估

从当前仓库状态看，这次工作不属于“小修小补”，但也不是需要推倒重写的等级，更准确地说是：

> 一次中高工作量的渐进式架构迁移。

之所以是“中高工作量”，主要不是因为某一处代码特别难，而是因为它同时涉及：

```text
项目边界调整
Common 拆分
官方扩展重新归位
Server 运行时迁移
Docker 部署链路调整
未来插件加载机制预留
```

如果只看纯文档里最核心的收口任务，工作量大致可以这样理解：

### 低到中等工作量部分

这部分主要是“边界澄清”和“现有结构收口”：

```text
梳理 framework/core/extension 的正式职责
统一 chat/trade 与 future plugins 的平权口径
整理 host 基础服务边界
清理部分命名和文档描述
```

这些工作更多消耗的是设计判断和代码整理时间，技术风险相对可控。

### 中等工作量部分

这部分开始真正触及代码结构：

```text
把 ChatExtension.* / TradeExtension.* 逐步从 Common 主树中剥离
减少 Common 对 server-only / client-only 依赖的承载
收紧主程序对官方扩展 API 的直接假设
统一 handler / fallback / 降级语义
```

这一层的难点不在“能不能改”，而在于：

```text
改完以后不能破坏当前 chat / trade 的可用性
不能让 client/server 协商链路退化
不能把已有 framework v2 主链改散
```

因此它更像“持续重构 + 回归验证”。

### 高工作量部分

真正重的部分主要有两块：

```text
1. Server 从 net472 / Mono 迁移到 modern .NET
2. 建立真正的外部插件目录加载机制
```

这是高工作量，不只是因为编码量，而是因为它会连带影响：

```text
构建方式
运行时兼容性
Dockerfile
依赖清理
宿主生命周期
程序集加载策略
故障诊断与降级行为
```

如果这两项要放在同一阶段一起做，风险会明显升高；更稳妥的做法是拆阶段推进。

### 综合判断

综合来看，比较合理的预期应该是：

```text
短期：
  可以完成边界澄清、文档统一、Common 初步瘦身、官方扩展继续解耦

中期：
  可以完成 chat/trade 平权化收口，以及 host/core/extension 责任重新落位

长期：
  再推进 modern .NET server 和真正的 Mods 外部插件加载
```

因此，这项工作的合理管理方式不应是：

```text
一次性大迁移
```

而应是：

```text
分阶段推进
每阶段都保持可编译、可运行、可验证
优先做边界收口，再做运行时迁移，最后做完整插件化
```

如果只给一个最终判断：

> 这是一个“应该立即开始，但不应该一次做完”的架构迁移任务；工作量实质上偏中高，最适合按阶段持续落地，而不是集中式重写。
