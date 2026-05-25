# Phase 6: Core-Only Server And Dynamic Extension Architecture

## Summary

Phase 6 的主要目标不是继续搬业务代码，而是重新把 framework / host / extension / plugin 的边界钉死。

本阶段最重要的目标只有四个：

- 服务端宿主只保留 `core` 能力，不再继续内嵌具体业务功能。
- 所有 extension 只能通过三条 pipeline 接入：
  - `content pipeline`
  - `command pipeline`
  - `item pipeline`
- chat 也必须真正插件化，不能再以任何形式停留在 core 或 host 私有实现里。
- extension/plugin 必须可扩展、可动态发现、可动态注册。
- extension 之间的协作必须通过 framework 提供的最小注册/发现机制完成，不能重新长回宿主硬依赖。

换句话说，Phase 6 的成功标准不是“trade 更完整”，而是“以后再加 chat / trade / red packet / market / mail attachment 时，server core 不需要再加新业务分支，也不需要为了某个功能额外安装新的宿主级能力”。

## New Boundary Decision

从 Phase 6 开始，边界明确如下：

- `core` 只负责协议、协商、注册、发现、生命周期、pipeline 调度、基础 host context。
- `host` 只负责启动 runtime、装配 core、提供基础宿主服务。
- `extension/plugin` 负责自己的 capability、payload、handler、状态、默认行为、可选 API。
- `server` 不理解 chat/trade/red packet 的领域语义。
- `client` 不理解某个 extension 的内部状态机，只消费 extension 暴露出来的 projection / facade / view model。

这里的“server 只保留 core 功能”并不意味着服务端进程里不能加载 built-in extension。
真正的含义是：

- server runtime 可以加载 official extension/plugin；
- 但 server host 不应把 chat/trade 当成自己的一部分硬编码进去；
- host 不应因为新增 extension 而继续新增 `BuiltInXxxHostServices`、`FrameworkXxxService` 装配分支或 capability 常量。

这里要额外强调：

- `chat` 不只是 “built-in feature”
- `chat` 必须是一个真正的 official plugin/extension
- `chat` 与 `trade` 在架构地位上应完全一致
- 唯一差别只能是发布方式，而不能是 core/host 边界待遇不同

## Current Architecture Review

结合当前代码，现状可以分成“已经做对的部分”和“仍然耦合的部分”。

### Already In The Right Direction

- `PhinixExtensionRegistry` 已经具备反射发现和注册入口，说明动态发现方向是成立的。
- `PhinixFrameworkClient` / `PhinixFrameworkServer` 已经形成统一协商与分发入口，说明 pipeline 主链已经存在。
- `chat` 和 `trade` 已经不是纯 legacy transport，说明 extension 化迁移路径已被验证。
- `RedPacket` 样板证明了自定义能力可以不改主链就完成收发与渲染。

### Current Coupling Problems

当前架构仍然没有完全达到 “core-only host + pipeline-only extension/plugin”。

#### 1. Core still contains chat-specific protocol constants

当前 `Common/Utils/Framework/FrameworkTypes.cs` 中仍然定义了：

- `BuiltInChatMessageType`
- `BuiltInChatHistoryRequestType`
- `BuiltInChatHistorySyncCompleteType`

这说明 `core` 仍然理解 chat 的具体 capability/type。
这不符合 “core 不理解具体业务” 的 Phase 6 边界。

同时这也说明：

- chat 目前还没有彻底插件化
- chat 仍然拥有高于其他 extension 的特殊地位

这正是本阶段必须修掉的点。

#### 2. Host still performs business-specific composition

当前 `Server/Server.cs` 与 `Client/Source/Client.cs` 仍然显式创建并注入：

- `PhinixFrameworkChatService`
- `PhinixFrameworkTradeServerService`
- `BuiltInChat*HostServices`
- `BuiltInTrade*HostServices`

这说明 extension 虽然通过 registry 被发现，但宿主仍然在做具体业务装配。

这类装配意味着：

- 新增一个 official extension，就要继续修改 host；
- “动态注册”仍然只是 handler 层动态，不是 extension 层动态；
- server 并没有真正退化成只保留 core 的宿主。

#### 3. Built-in extensions are still compiled into host assemblies

当前 `BuiltInChatClientExtension`、`BuiltInTradeClientExtension`、`BuiltInChatServerExtension`、`BuiltInTradeServerExtension` 仍直接存在于 `Client` / `Server` 主项目中。

这导致两个问题：

- extension 的发布边界和 host 的发布边界没有真正分开；
- “built-in extension” 更像“宿主内部模块”，而不是“由宿主加载的 official extension”。

对 chat 来说，这个问题尤其严重，因为它最容易被误认为“天然属于 framework 主链”。
Phase 6 必须明确否定这个前提。

#### 4. Extension registration is dynamic, but API discovery is missing

当前 registry 能收集：

- handlers
- renderers
- codecs
- capabilities

但还不能收集：

- extension 对外暴露的 typed API
- extension 提供的 facade / service contract
- extension 之间的能力协作入口

因此现在还没有真正意义上的：

- `RegisterApi<T>()`
- `TryResolve<T>()`
- `TryResolveAll<T>()`

这意味着 extension 之间一旦需要协作，很容易重新退回到：

- 硬依赖具体实现类
- 宿主提前注入专用 service
- 静态入口

#### 5. Pipeline semantics are not fully normalized yet

虽然当前主链实际上已经接近三条 pipeline，但语义上仍然有历史包袱：

- `FrameworkPacket.MessageType` 命名仍偏向 chat 时代的 message 语义；
- `IClientMessageHandler` 同时承担“处理文本输入”和“处理入站 message”；
- `FrameworkDisplayMessage` 仍然带着明显 chat/UI 影子；
- item pipeline 还偏向“trade 的一个附属能力”，尚未完全成为独立基础设施。

这并不代表当前设计错误，而是说明命名和契约还没有完全完成去业务化。

其中最容易制造误会的一点就是：

- `message pipeline` 这个名字太像 “chat pipeline”

所以 Phase 6 不应只讨论语义整理，而应明确把它改名。

## Target Architecture

Phase 6 的目标架构建议分成四层。

### 1. Core Layer

`core` 只保留所有 extension 都会复用的最小能力：

- packet/envelope/payload 基础协议
- capability negotiation
- extension discovery
- extension registration
- extension activation / shutdown
- content / command / item 三条 pipeline
- 通用 metadata
- 基础 host context
- API registry

`core` 不再包含：

- built-in chat type 常量
- built-in trade type 常量
- business-specific payload
- UI-facing display model
- 某个官方 extension 的 host service 类型

### 2. Host Layer

`host` 只负责：

- 启动网络与认证
- 创建 `ExtensionHostContext`
- 加载 extension assembly
- 启动 `PhinixFrameworkClient` / `PhinixFrameworkServer`
- 提供基础服务

host 可提供的基础服务只应包括：

- logging
- clock
- id generation
- storage root
- network send / broadcast adapter
- authenticated user/session lookup

host 不应继续提供：

- `BuiltInChatServerHostServices`
- `BuiltInTradeServerHostServices`
- `BuiltInChatClientHostServices`
- `BuiltInTradeClientHostServices`

因为这类类型本质上是在把 business composition 放回 host。

### 3. Extension Layer

每个 extension 自己负责：

- protocol constants
- payload contracts
- handlers
- renderers
- codecs
- repository / store
- service / facade
- 对外 API

这意味着：

- `ChatExtension` 持有 chat 的所有 type 和 API；
- `TradeExtension` 持有 trade 的所有 type 和 API；
- `RedPacketExtension` 持有自己的 payload/rendering 逻辑；
- official extension/plugin 只是“随主程序分发”，但架构地位仍然是 extension/plugin，不是 core。

这里再次强调：

- `ChatExtension` 必须是插件化的
- 它不能因为“默认总会有”就继续留在 core
- 它不能因为“UI 依赖多”就继续保留特殊入口
- 它不能因为“历史最早”就继续占用 `message pipeline` 这个名字

### 4. Projection Layer

客户端 UI 不直接吃 transport 契约，而是吃 extension 投影：

- chat feed projection
- trade snapshot projection
- custom view model

transport type 只能停留在 extension 内部或 adapter 边界，不应直接渗透到宿主 UI。

## The Three-Pipeline Rule

Phase 6 之后，extension 的 runtime 接入路径必须被严格限制。

### Content Pipeline

适用于：

- 用户可见的内容流
- 广播型消息
- 可渲染的社交/通知型载荷

职责：

- 接收/发送内容型 envelope
- 调度 `message handlers`
- 调度 `renderers`
- 产出默认 display/projection 所需的中间结果

不负责：

- 复杂状态同步
- 请求/响应控制语义
- 业务依赖解析

选择 `content pipeline` 这个名字，而不是 `message pipeline`，是因为：

- `message` 太容易被理解成 chat domain
- 这条 pipeline 实际承载的是“内容型载荷”
- chat 只是它的一个消费者，不是它的定义者

如果后续觉得 `content` 仍不够准确，可接受的备选名只有这类中性名称：

- `content pipeline`
- `payload pipeline`
- `presentation pipeline`

不建议继续保留 `message pipeline` 作为正式名称。

### Command Pipeline

适用于：

- request / response
- snapshot sync
- state transition
- ack / error / control command

职责：

- 调度 command handlers
- 承载控制流
- 承载 extension 内部状态同步

不负责：

- UI 渲染语义
- 文本展示语义

### Item Pipeline

适用于：

- 任意可编解码 item payload
- trade 物品
- mail attachment
- market listing payload
- 未来任何“物品/对象载荷”传输

职责：

- codec discovery
- codec selection
- encode / decode
- unknown payload soft-fail

不负责：

- trade 状态机
- 聊天显示
- command routing

### Explicit Rule

Phase 6 明确规定：

- extension 不允许新增第四条 runtime pipeline。
- extension 的所有网络接入都必须复用这三条 pipeline 之一。
- 如果某个需求看起来需要第四条 pipeline，应优先重新审视它究竟属于 content / command / item 中哪一类。

这条规则的价值是：

- 保持 runtime 结构稳定；
- 避免 feature-driven core 膨胀；
- 保持 extension 接入模型可预测、可测试。

## Dynamic Registration Design

当前的反射发现方向保留，但收口方式需要升级。

### Desired Registration Model

建议以 “发现 module，再由 module 显式注册组件” 为主，而不是继续直接扫描所有零散 handler 类型。

推荐 contract：

```csharp
public interface IPhinixExtensionModule
{
    string ExtensionId { get; }
    void Register(IExtensionBuilder builder, ExtensionHostContext hostContext);
}
```

其中 `IExtensionBuilder` 负责接收：

- `AddCapability(...)`
- `AddMessageHandler(...)`
- `AddCommandHandler(...)`
- `AddItemCodec(...)`
- `AddRenderer(...)`
- `RegisterApi<T>(...)`

### Why this is better

这样做有几个直接好处：

- extension 的组成部分由 module 自己决定；
- 初始化顺序可控；
- 是否暴露 API 可控；
- extension 内部可以自由拆分类，但对 framework 暴露的仍是一个 module 边界；
- host 不需要知道 extension 内部有哪些 service。

### What should remain dynamic

Phase 6 仍然保留动态发现：

- 通过反射发现 module
- 自动实例化 module
- 自动执行 register/activate

但 framework 发现的对象应主要是 “module”，不是 “所有 handler 实现类”。

这条规则对 chat 也完全生效。
也就是说：

- chat 不是 framework 内建特例
- chat 也是被发现、被注册、被激活的 plugin/module

## API Discovery / Capability Registry

这是 Phase 6 最关键的新能力之一。

当前 capability registry 只能表达“谁支持什么消息类型”，不能表达“谁暴露了什么协作 API”。
因此建议新增一个轻量级 API registry。

### Design Goals

- 不引入完整 DI / IoC。
- 不做复杂生命周期管理。
- 不做自动依赖求解。
- 只解决 “extension A 如何找到 extension B 暴露的最小 contract”。

### Recommended API

```csharp
public interface IExtensionApiRegistry
{
    void RegisterApi<T>(string extensionId, T implementation) where T : class;
    bool TryResolve<T>(out T implementation) where T : class;
    IReadOnlyList<T> ResolveAll<T>() where T : class;
}
```

### Semantics

- `RegisterApi<T>()` 由 extension 在 `Register(...)` 或 `Activate(...)` 阶段显式调用。
- `TryResolve<T>()` 用于查询单个首选 API。
- `ResolveAll<T>()` 用于未来存在多个 provider 的场景。
- framework 不负责决定“哪个 provider 更正确”，只负责注册与查询。

### Important Non-Goals

Phase 6 不做：

- 构造函数自动注入
- 多层作用域
- 自动释放复杂资源
- dependency graph solver
- version range matching

如果 extension 依赖另一个 API，它自己负责：

- 检查 `TryResolve<T>()`
- 缺失时决定 degrade / disable / soft-fail

framework 只提供最小查询能力。

## How Trade Should Call Chat Without Hard Dependency

Trade 不应硬编码依赖 `BuiltInChatClientExtension`、`BuiltInChatServerExtension` 或 `PhinixFrameworkChatService`。

正确方式是让 Chat 暴露一个极小的 capability API contract，例如：

```csharp
public interface IChatApi
{
    void PublishSystemNotice(string translationKey, params string[] args);
}
```

然后：

- `ChatExtension` 在注册阶段 `RegisterApi<IChatApi>(...)`
- `TradeExtension` 在需要时 `TryResolve<IChatApi>(out var chatApi)`
- 如果存在，则调用
- 如果不存在，则 trade 自己降级，例如只记录日志或仅发本地事件

### Why this matters

这样 Trade 依赖的是：

- chat 暴露的稳定 contract

而不是：

- chat 的具体实现类
- chat extension 的程序集布局
- host 是否手工注入 chat service

这正是 “extension 之间通过 framework 查询 API” 的最小实现。

## What Must Move Out Of Core

以下内容应明确从 core 语义中移出：

- built-in chat message types
- built-in chat sync command types
- built-in trade protocol constants
- chat-specific display model
- trade-specific host service wrapper
- official extension 的私有 service 类型
- chat plugin 的任何专属主链命名

### Naming Guidance

建议逐步去掉那些会把 pipeline 理解成“聊天系统”的命名。

优先建议：

- 保留 `FrameworkPacket`
- 把偏展示语义的 `MessageType` 逐步收口为更中性的 `Type`
- 把 `MessagePipeline` 正式重命名为 `ContentPipeline`
- 把 UI 专用的 `FrameworkDisplayMessage` 移到 ChatExtension 或独立 projection contract，而不是继续放在 core

如果连接口一起调整，建议同步收口为：

- `IClientMessageHandler` -> `IClientContentHandler`
- `IServerMessageHandler` -> `IServerContentHandler`
- `KindMessage` -> `KindContent`

这里不要求一次性做完所有 rename，但设计基线必须先改，不再继续把 `message` 当作正式长期名称。

### Recommended Terminology

- `Packet` / `Envelope`：transport 层
- `Payload`：业务载荷
- `Flow`：content / command / item
- `Projection` / `DisplayEntry`：客户端展示层

这里不一定要求 Phase 6 一次性全量改名，但语义上必须先统一，否则后面还会不断把业务塞回 core。

## Minimal Phase 6 Implementation

本轮建议只做最小但关键的收口，不做大平台化。

### Must Do

1. 新增一份明确的 Phase 6 边界设计，并按此执行。
2. 在 core 中移除 built-in chat/trade 业务常量与业务语义。
3. 引入轻量级 `API registry`。
4. 把 extension 注册入口收敛为 module/builder 模式。
5. 把 official chat/trade 从 “host 内建逻辑” 改成 “official extension/plugin package/module”。
6. 让 server host 只装配 core 级基础服务，不再装配业务专用 host service wrapper。
7. 把 `message pipeline` 正式更名为 `content pipeline`，避免 chat 语义污染。
8. 明确三条 pipeline 是唯一接入面。

### Should Do If Cheap

- 把 `FrameworkDisplayMessage` 从 core 中搬到 chat/projection 层。
- 把 item pipeline 语义从 “trade 附属工具” 改成 “独立基础设施”。
- 给 extension 增加 activation diagnostics，便于定位注册失败。

### Explicitly Postpone To 2.0

- dependency graph
- extension versioning
- hot reload
- sandboxing
- remote extension download
- complex plugin manifests
- dependency solver
- automatic startup ordering based on graph analysis
- multi-version API coexistence

这些都不是 Phase 6 的核心目标。
如果现在就做，会把一个“稳定可演化的最小平台”做成“复杂但不稳定的半插件系统”。

## Migration Strategy

建议按下面顺序推进，避免再次耦合。

### Step 1

先把文档边界定死，不继续把新业务塞进 host 或 core。

### Step 2

抽出 `IExtensionBuilder` 和 `IExtensionApiRegistry`。

### Step 3

把 registry 的主发现对象从 “散装 handler 类” 切到 “module”。

### Step 4

把 built-in chat / trade 常量迁到各自 extension contract 项目，并停止在 core 中保留 chat 专属命名。

### Step 5
把 `BuiltInChat*HostServices` / `BuiltInTrade*HostServices` 替换为：

- core-level host services
- extension 内部自己的 composition
- 必要时通过 API registry 获取对外协作能力

同时在这一阶段明确收紧一条硬边界：

- `core` / `host` 不得因为某个具体 extension 的领域语义继续新增业务分支
- `core` / `host` 只负责发现、注册、激活 extension，并提供通用基础服务
- extension 的业务逻辑必须建立在 `content / command / item` 三条 pipeline 之上
- extension 之间的协作只能依赖显式暴露的 API contract，不能依赖宿主注入的业务 wrapper，也不能依赖其他 extension 的具体实现类

换句话说，`Step 5` 完成后应达到的不是“host 以另一种方式继续装配 chat/trade”，而是：

- host 对 extension 的认知只停留在 module lifecycle 与基础 host services
- extension 自己决定如何组织 handler / renderer / codec / repository / service
- extension 如果要调用另一个 extension，只能通过 `RegisterApi<T>()` / `TryResolve<T>()` 这类显式 contract 协作

### Step 6

把 official extension 代码从 host 主项目边界上拆开。

这里未必要求物理上立刻变成外部 DLL 下载式插件，但至少要达到：

- 架构上是 extension
- 组合上不依赖 host 业务分支
- 新增 official extension 不需要改 core 设计

## Acceptance Criteria

Phase 6 完成后，应该满足以下标准：

- server host 不再包含 chat/trade/red packet 的业务常量和业务装配逻辑。
- extension 只能通过 content / command / item 三条 pipeline 接入。
- 新增一个 official extension 时，不需要修改 core pipeline 结构。
- extension 可以动态发现、动态注册、动态激活。
- extension 可以通过 `RegisterApi<T>()` / `TryResolve<T>()` 进行协作。
- trade 可以调用 chat 暴露的 API，但不依赖 chat 的实现类。
- chat 作为 official plugin/extension 被加载，而不是作为 core 私有组成部分存在。
- core 不需要理解任何具体业务 payload 语义。
- 移除某个 extension 后，host 仍能启动，其他 extension 以可预期方式 degrade。

## Final Recommendation

当前架构并不是方向错误，而是还停在 “core 已出现、extension 已接入、但宿主仍然偏厚” 的过渡阶段。

所以 Phase 6 的真正工作重点不应是继续加功能，而应是：

- 把 `server only keeps core` 变成真实结构，而不是口头原则；
- 把 `chat must also be pluginized` 变成真实结构，而不是 built-in 特例；
- 把 `content / command / item only` 变成硬约束；
- 把 `dynamic registration` 从 handler 级能力升级到 extension + API 级能力；
- 把 official chat/trade 从“宿主内部模块”提升为“真正的 official extension/plugin”；
- 把 `message pipeline` 这个会误导边界理解的名字正式淘汰。

如果这一步不先做，后面每增加一个 extension，都会再次要求 host/core 添加专用 service、专用 type 或专用分支，最终又会重新耦合回去。
