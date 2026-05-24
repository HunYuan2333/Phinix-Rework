# Phinix Core / Extension Boundary Assessment

## Summary
Phinix 当前的 framework 已经具备一个可工作的扩展骨架，但它更像“可注册 handler 的协议宿主”，还不是“可承载独立领域子系统的扩展平台”。

现阶段最重要的边界判断是：

- `core` 应负责通用协议、协商、分发、编解码入口与宿主能力。
- `extension` 应负责具体领域能力、状态模型、默认行为与 UI 适配。
- `core` 不应因为某个具体功能的复杂度持续吸收该功能的领域语义。
- `extension` 也不应退化成“通过静态全局对象拼接出来的伪扩展”。

这意味着后续无论是 built-in chat 还是 future trade，都应被视为“运行在 framework 上的功能模块”，而不是继续把功能本体堆进协议宿主。

## Core Responsibilities
`core` 的职责应严格限制在所有扩展都会复用的横切能力上：

- framework packet / flow / metadata 的基础协议定义
- capability negotiation 与 capability gating
- message / command / item 三态 pipeline
- extension discovery / registration / activation
- handler / renderer / codec 的调度与优先级规则
- 通用 host services：
  - logging
  - send / broadcast
  - remote capability lookup
  - persistence root 或持久化能力入口
  - clock / id generation 等基础运行时能力
- legacy mode 的检测与降级边界

`core` 不应承担的职责：

- 具体玩法的生命周期状态机
- 某个 built-in feature 的领域数据结构
- 某个领域特有的持久化模型
- 某个 UI 的直接数据供给逻辑
- 因单一功能需要而引入的大量专用 hook

## Extension Responsibilities
`extension` 应承载一个完整功能域，而不只是若干零散 handler：

- capability 声明
- 领域协议类型与 payload
- client-side repository / state projection
- server-side aggregate / store
- 领域 service
- pipeline handlers / renderers / codecs
- 默认行为策略
- optional UI adapter

一个合理的 extension 内部应至少能分出以下角色：

- registration / descriptor
- protocol handlers
- domain service
- repository 或 aggregate store
- adapter / presentation layer

其中 handler 只负责协议入口与出口，不应同时承担协议解析、状态流转、持久化和 UI 拼装。

## Current Boundary Assessment
当前 framework 已经正确建立了几条重要边界：

- chat 已经从 legacy 协议根迁到 framework-native message / command 路径
- item pipeline 已经从“交易附属工具”开始向“独立基础设施”转向
- capability negotiation 已经把“是否可用某扩展能力”从功能逻辑中分离出来
- renderer / interceptor / handler 已经初步把显示流与协议处理流拆开

但当前实现仍有几处边界混杂：

### 1. Discovery 和 composition 混在一起
当前 registry 直接反射扫描并实例化所有实现类，然后把实例平铺塞进不同列表。

这说明 framework 目前做到了“发现实现”，但还没有做成“发现 extension module，再由 module 注册其组成部分”。

结果是：

- extension 的内部对象图由反射副作用决定
- 初始化顺序与依赖关系不显式
- 很难为复杂 extension 提供受控生命周期

### 2. Host context 仍偏薄
当前 handler context 已提供 send / broadcast / capability / log 等基础能力，这对 message-style 扩展已经够用。

但对于重状态扩展，context 还缺少更明确的宿主装配语义，例如：

- 持久化入口
- 生命周期阶段
- state service 的注册/获取方式
- 受控的 host service 注入边界

### 3. Built-in extension 仍依赖全局静态宿主
当前 built-in chat extension 通过 `Client.Instance`、`Server.FrameworkChat`、`Server.UserManager` 等静态入口获取运行时对象。

这说明当前实现做到了“逻辑被搬进 extension 类”，但尚未做到“extension 只通过受控 context 与 host 协作”。

这类依赖在简单功能上问题不大，但会让复杂功能重新长回宿主耦合。

### 4. Extension contract 仍以 handler 为中心
现在的 public contracts 更偏：

- message handler
- command handler
- renderer
- codec

而不是：

- extension module
- extension lifecycle
- extension-owned domain services
- extension-owned state repository

这意味着 framework 当前更擅长承载“协议拦截/转发/显示型扩展”，对“完整子系统型扩展”的表达力还不够完整。

## Recommended Loading Model
建议的扩展加载模型应分成四层：

### 1. Discovery
发现的是 extension module，而不是零散实现类。

module 应至少能声明：

- extension id
- capabilities
- 要注册的 handlers / renderers / codecs
- optional domain services
- optional UI adapters

### 2. Composition
由 host 创建受控的 extension context，并把运行时能力注入 module。

module 在此阶段完成自身内部装配，而不是依赖静态单例到处抓对象。

### 3. Activation
module 被激活后，才把它提供的 handlers / codecs / renderers 接入 pipeline。

这样可以把：

- extension 是否可用
- extension 依赖是否满足
- extension 初始化是否成功

都变成显式决策，而不是扫描时的副作用。

### 4. Runtime
运行时由 pipeline 与 host services 驱动 extension，对外暴露的是受控 contract，不是宿主内部实现细节。

## Boundary Rules Going Forward
后续演进建议遵守以下规则：

- 新功能优先新增 extension module，而不是继续扩大 `Client` / `Server` 宿主职责
- `core` 只新增可被多个扩展复用的 contract，不为单一功能注入专用语义
- 复杂功能必须有自己的 repository / aggregate，而不是把状态散落在 handler 中
- extension 不直接依赖宿主静态单例；如确有过渡期需要，应标注为临时技术债
- UI 不直接消费 transport 或 protocol 类型，应尽量消费 extension adapter 暴露的 view model
- built-in feature 可以暂时“较厚”，但厚的是 feature service，不应是 framework host

## Conclusion
Phinix 当前 framework 已经证明：

- 反射式扩展发现可行
- capability negotiation 可行
- 三态 pipeline 方向正确
- built-in chat 的 framework-native 化是成功样板

但它尚未完全证明：

- 重状态功能可以在不依赖宿主静态对象的前提下成为真正松耦合 extension
- extension 可以拥有清晰生命周期、状态仓与装配边界

因此，下一阶段的重点不应只是“继续搬运功能”，而应是把 framework 从“handler 宿主”推进成“extension module 宿主”。
