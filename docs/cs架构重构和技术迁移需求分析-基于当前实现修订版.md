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

---

### Phase 3：服务端迁移到 modern .NET

这一步应当被提升优先级。

真实迁移目标建议分两段：

```text
第一阶段：
  server -> net8.0

第二阶段：
  根据工具链和依赖清理结果继续升到更高版本
```

原因很简单：

```text
当前 Server 仍依赖 net472
Docker 仍使用 mono
线程与宿主模型仍偏旧
```

只要这一层不动，后续真正的插件化、可维护部署、现代诊断工具链都会被持续拖累。

---

### Phase 4：明确服务端消息处理语义

当前 handler action 已经存在，但“默认 relay / 显式接管 / 拦截”的语义还不够清楚。

建议在现有 `MessageHandlingResultAction` 基础上重新定义服务端约定：

```text
Continue:
  继续交给下一个 handler

Handled:
  当前 handler 已消费，结束

ReplacePayload:
  改写后继续

StopPropagation / SuppressDefault:
  需要重新审视是否保留双重语义
```

如果未来确实想引入“默认 relay”，应该在 server framework 层明确补一个 fallback policy，而不是把它写成文档默认前提。

---

### Phase 5：把“动态发现”升级为“外部插件加载”

当前只有程序集内反射发现，没有外部插件目录。

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

> 这是未来阶段目标，当前仓库尚未实现。

而且这一阶段还有一个重要的架构目标：

> 外部插件加载机制一旦建立，就必须同时适用于 `chat` / `trade` 这类官方扩展和未来新增扩展，而不是只给“第三方插件”走另一条弱化路径。

否则会出现两套插件等级：

```text
官方插件 = 半内建
其他插件 = 外挂
```

这会直接破坏前面建立的平权边界。

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
