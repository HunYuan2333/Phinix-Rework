# Phinix 框架化重构开发模型与路线图

## Summary
本项目不适合使用纯瀑布模型。建议采用 **演化式原型 + 迭代增量开发**：先用小范围原型验证最关键的架构假设，再逐步稳定接口、替换旧实现、补齐兼容层，最后用真实 submod 验证框架可用性。

核心原则：
- 先验证“扩展性是否真实存在”，再做大规模重构。
- 先稳定框架边界，再补默认功能。
- 先保证 `新客户端 -> 旧服务器` 兼容，再决定是否做 `新服务器 -> 旧客户端`。
- 所有阶段都以“红包 submod 能否自然接入”作为检验标准。

## Development Model
采用五段式开发模型：

1. **架构原型阶段**
   - 目标：验证新框架最关键的 3 个风险点。
   - 只做最小可运行骨架，不追求功能完整。
   - 验证点：
     - `v2` 消息信封是否能承载扩展消息
     - 反射式 submod 发现与自动注册是否稳定
     - 默认消息显示是否能被拦截或替换

2. **框架骨架稳定阶段**
   - 目标：把原型中可行的机制固化成正式公共接口。
   - 产出应包括：
     - message pipeline 的正式接口
     - item pipeline 的正式接口
     - capability negotiation 机制
     - legacy adapter 的边界定义
   - 这一阶段不追求新玩法，只追求接口决策完整。

3. **增量替换阶段**
   - 目标：逐步把现有 Phinix 功能迁移到新框架之上。
   - 推荐顺序：
     - 先迁移聊天链路
     - 再迁移交易/物品链路
     - 再迁移默认 UI 行为
     - 最后补 legacy client compatibility
   - 要求旧功能在迁移后只是“默认 handler”，不再是核心架构本体。

4. **built-in chat 收口阶段**
   - 目标：完成 chat 的 framework-native 重写与 legacy 清场。
   - 这一阶段以 chat 为唯一 built-in feature 收口对象。
   - 允许 built-in chat service 暂时偏厚，但必须保证 pipeline、capability、host context、adapter 边界稳定。

5. **legacy 拆除与插件化收口阶段**
   - 目标：把 trade 从“已接入 framework 主链”推进到“可安全移除 legacy 实现”。
   - 需要完成：
     - runtime 不再实例化 legacy 模块
     - UI 不再依赖 legacy domain/net 类型
     - built-in trade 真正收敛成 extension module + adapter/facade
   - 如果删除 legacy 后仍需回头改 core 才能接回默认 trade，说明插件化边界仍未完成。

## Implementation Plan
### Phase 1: 架构原型
- 新建 `v2` 协议草模，只定义最小消息信封、类型标识、元数据与 payload 容器。
- 做反射扫描机制，自动发现扩展程序集中的 handler / renderer / codec。
- 在客户端消息接收链中插入可中断的拦截点，证明“默认全显示”可以被接管。
- 验收标准：
  - 一个自定义消息类型可以不改核心 UI 直接被发现并处理。
  - 一个扩展可以阻止默认聊天窗口渲染该消息。
  - 原型失败时可以明确知道失败点是在协议、发现机制还是 UI 链路。

状态：已完成（按架构原型目标判定）

补充说明：
- `v2` 消息信封、反射式扩展发现、客户端接收链路中的可中断显示接管点都已在代码中落地。
- 样板扩展已能证明“自定义消息类型可被发现、发送、广播并渲染”，说明协议骨架与发现机制有效。
- 目前仓库中的直接样板更偏向 `handler + renderer` 链路验证；拦截点本身已存在，但“独立 interceptor 样板”仍可作为后续补强项。

### Phase 2: 框架接口定型
- 定义公共接口：
  - `IPhinixExtension`
  - `IMessageInterceptor`
  - `IMessageHandler`
  - `IMessageRenderer`
  - `IItemCodec`
  - `ITradeCompletionHandler`
  - `ICapabilityProvider`
- 定义 handler 返回语义：
  - 继续默认流程
  - 替换 payload
  - 抑制默认 UI
  - 中断传播
  - 回退到 legacy/default
- 定义 capability negotiation：
  - 连接建立后声明双方支持的扩展能力
  - 不支持的消息类型必须能优雅降级
- 定义 legacy 模式边界：
  - 旧服连接时只能启用基础聊天/交易能力
  - 扩展能力在 UI 与日志中都要明确提示不可用

状态：已完成

补充说明：
- Phase 2 所列公共接口、handler 返回语义、capability negotiation、legacy 模式边界都已在当前代码中定型并接入。
- 这一阶段的“完成”指接口与边界已经稳定到可以支撑后续迁移与样板扩展，不代表所有内建功能都已经 fully framework-native。

### Phase 3: 迁移现有功能
状态：已完成（2026-05-22）

- 把当前 `ServerChat / ClientChat` 改造成默认消息 handler，而不是唯一消息系统。
- 把当前 `TradingThingConverter` 改造成默认 item codec，实现“核心内建 codec”而不是“唯一 codec”。
- 明确 `item pipeline` 是独立架构层：交易只是它的第一个内建消费者，不是它的定义边界。
- 未来 submod（集市、邮件附件、仓储同步、拍卖等）应可直接复用 item pipeline，不需要核心继续为玩法做硬编码。
- 把 `Client` 中硬编码的提示音、信件、drop pod、blocked 过滤等逻辑拆成默认行为处理器。
- 将现有 UI 组件改为消费 pipeline 产物，而不是直接消费底层状态列表。
- 验收标准：
  - 无扩展时，用户体验与当前 Phinix 基本一致。
  - 有扩展时，默认 UI 可以被部分或完全接管。
  - 核心不需要为新增消息类型或物品载荷继续改代码。

当前进展说明：
- 已开始把默认聊天 UI 的消息消费入口迁入 framework 层。
- `Client` 不再直接负责 legacy/framework 消息拼接与默认 suppression 判定，这部分已下沉到 `PhinixFrameworkClient`。
- 这意味着聊天 UI 已经开始消费 pipeline 产物，而不是自行拼装两套来源。
- 聊天提示音触发条件与 blocked 用户聊天可见性判断，已开始通过 framework helper 统一收口。
- 交易创建成功、交易完成、交易取消、交易更新失败这几类默认 UI/信件/掉舱行为，已开始从 `Client` 宿主代码抽到 framework 默认交易行为类。
- 交易完成链路已开始消费 `ITradeCompletionHandler` contract（默认 handler 保持原有用户体验）。
- 交易、物品 codec 的更多 UI 内部调用点（例如交易窗口缓存与 offer 更新路径）仍有后续迁移空间。
结论：
- Phase 3 验收目标已满足，可进入 Phase 4。
- Phase 3 之后的工作主要是增强项（例如未知 mod 物品更友好的补偿策略、交易 UI 内部更多调用点的进一步解耦），不影响 Phase 3 完成判定。
- 但这里的“完成”应理解为：按照本阶段验收目标，聊天链路、默认 UI 行为抽离、交易完成处理链路已经完成迁移；并不代表交易与物品传输的每一个内部路径都已经彻底 framework-native 化。

Phase 3 实际接入状态（核查结论）：
- message pipeline（新服协商成功时）已真实接入，不是占位：
  - 客户端发送走 `IClientMessageHandler`，按 capability 做发送前约束；
  - 服务端接收走 `IServerMessageHandler`，并在广播边界做 capability 过滤；
  - 客户端显示走 `IMessageRenderer` + `IMessageInterceptor`，不是仅日志层接入。
- item pipeline 已真实接入到“交易完成后物品落地”路径，不是占位：
  - 交易完成事件先进入 `PhinixClientTradeCompletionPipeline`；
  - 默认 `ITradeCompletionHandler` 再通过 `PhinixClientItemPipeline` + 默认 `IItemCodec` 解码后执行掉舱/信件。
- 但当前仍属于“legacy transport + pipeline adapter”阶段：
  - stock trade 的网络传输仍是 legacy `ProtoThing`；
  - item pipeline 目前主要接管“完成后解码与处理”；
  - 尚未把交易更新/offer 全链路改成 framework item payload 原生传输。

对外建议表述：
- 可以宣告 Phase 3 完成。
- 更准确的完整说法应为：**Phase 3 已完成，当前完成形态为 `legacy transport + pipeline adapter`，后续仍有 framework-native 化增强空间。**

### Phase 4: built-in chat 收口
状态：已完成

  - 当前阶段优先级说明：
    - 当前阶段只针对 chat，不再包含 trade。
    - 目标是让 chat 完成 framework-native 主链迁移、UI 契约迁移与 legacy 清场。
    - 优先保证 pipeline 语义和边界稳定；built-in chat service 内部即使暂时偏厚、承担较多业务职责，也可以接受。
    - 也就是说，当前阶段允许先把 legacy chat 逻辑“整体搬进 built-in chat service”，后续再做 service 内部细分；不要求一开始就把 built-in chat 拆成很多小类。
    - 现有 chat 实现不再继续扩展为长期协议根，进入“framework built-in feature 收口完成”状态。
  - 默认 chat 的 framework built-in feature 重写已基本完成：消息发送走 `message`，历史同步走 `command`。
  - 现有 `message` 通道继续收紧为“显示流”。
  - 后续如需承载状态同步、请求/回执、私有控制信息，应新增与 `message` 平行的 `command` 通道，而不是继续混入当前显示流。
  - built-in chat 已验证：
    - framework 历史同步可工作；
    - 重连后历史消息可恢复；
    - 新消息发送/广播/显示链路可工作。
  - 今日已完成的 chat 重写工作：
    - built-in chat 的 canonical payload/store 已迁到 framework 自己的 protobuf 契约：
      - `BuiltInChatMessagePayload`
      - `BuiltInChatHistoryStore`
    - 服务端 framework chat 已不再依赖 `Common/Chat` 的协议与持久化模型；
    - 客户端 framework chat 已不再依赖 `ClientChatMessage` / `ClientChatMessageEventArgs` / `ChatMessageStatus`；
    - chat UI 已切换到独立的 framework-native UI model：
      - `UIChatMessage`
      - `UIChatMessageEventArgs`
      - `UIChatMessageStatus`
    - `Phinix.sln` 中旧 `Chat` 项目已移除；
    - `Client` / `Server` 主运行链路已不再引用 `Common/Chat` 项目；
    - legacy `Common/Chat` 目录已完成物理清理。
  - built-in chat 后续体验项已记录但暂不优先实现：
    - 聊天显示行数限制，并开放为可配置项；
    - 时间显示补全年月日，而不只显示 `HH:mm`；
    - 系统消息与普通用户消息做明显样式区分。
    - 连接恢复或服务端重启检测后，客户端自动重新协商 framework capability，并自动重新请求 chat history / resync，避免必须手动断联重连。
  - 当前 chat 迁移状态更新为：
    - 旧 chat 的客户端/服务端主运行链路已下线；
    - `ClientChat` 的发送 fallback、已读计数、消息缓存读取与事件桥接已从 `Client` 宿主代码中移除；
    - 客户端聊天 UI 现在直接消费 framework built-in chat service 提供的数据与事件；
    - built-in chat 已不再复用 `Common/Chat` 的协议、持久化模型与 UI 类型；
    - 旧 `Common/Chat` 已完成仓库级清理，chat 当前只保留 framework built-in feature 实现。
  - chat 后续拆分原则（当前决策）：
    - `message pipeline` 只负责显示流分发、handler/renderer 调度、通用显示缓冲与通用事件抛出；
    - `command pipeline` 只负责历史请求、同步完成、后续控制语义等 chat 非显示流分发；
    - chat-specific 逻辑应尽可能继续下沉到 built-in chat service，包括：
      - 历史同步；
      - unread/read；
      - feed 构建；
      - blocked user 过滤；
      - 通知触发策略；
      - framework display message 到 chat UI model 的转换；
    - 当前允许 built-in chat service 先保持“单个较厚 service”形态，不把“service 必须很薄”当作第一目标；
    - 该厚 service 形态是当前迁移阶段的权宜之计，不作为最终架构目标；
    - 当前 legacy chat 清理已完成，后续仍需在主链路稳定基础上继续做 service 内部拆分与职责收口。

- 验收标准：
  - 默认 chat 在 framework 主链上可运行，且用户感知基本不变
  - chat UI 不再依赖 legacy chat 类型
  - `Common/Chat` 可从主运行链路与仓库中清理
  - 框架失效时退回默认行为而不是直接崩

### Phase 5: legacy trade 拆除与插件化边界收口
状态：未开始

- 目标：
  - 让 trade 达到与当前 chat 相近的完成形态：
    - legacy runtime 可删除；
    - built-in trade 作为 extension module 挂在 framework 上；
    - core 不再依赖 trade 语义；
    - UI 与默认行为通过 adapter/facade 消费 trade extension，而不是直接依赖 legacy trade 类型。
- 当前阶段定位说明：
  - Phase 4 已经作为 chat 收口阶段完成；
  - Phase 5 专门负责把 trade 从“framework 主链已可运行”推进到“legacy 可安全下线”。
- Phase 5 主要任务：
  - 完成 `trade -> command + item` 的 framework-native built-in extension 重写。
  - 实现 `新客户端 -> 旧服务器` 自动探测与 legacy 降级。
  - 在 legacy 模式下：
    - 维持基础 chat/trade
    - 禁用 `v2` 扩展消息与高级 item payload
    - 给用户明确提示当前连接不支持框架能力
  - 从 `Client` / `Server` 宿主中移除 `ClientTrading` / `ServerTrading` 的运行时实例化与主链依赖。
  - 将 trade UI 依赖的 legacy 契约迁到新的 adapter/facade/view model 层：
    - `ImmutableTrade`
    - `ProtoThing`
    - `CreateTradeEventArgs`
    - `TradeUpdateEventArgs`
    - `TradesSyncedEventArgs`
    - `CompleteTradeEventArgs`
  - 把 `TradeWindow` / `TradeList` 从“直接消费 legacy 业务对象”改成“消费 trade UI facade / projection”。
  - 把 `PhinixDefaultTradeBehaviour` 从 `ClientTrading` 直接依赖中摘掉，改为依赖 trade extension 暴露的统一查询/命令入口。
  - 把 `ProtoThing` 降级为过渡兼容类型，避免继续作为 trade UI 的 canonical item shape。
  - 最终移除 `Trading.csproj` 对 `Client` / `Server` 主运行链路的硬依赖。
- 验收标准：
  - 新客户端连旧服能正常用基础功能
  - 删除 `ClientTrading` / `ServerTrading` 后，默认 trade 仍可正常创建、更新、取消、完成。
  - `TradeWindow` / `TradeList` 不再直接依赖 legacy trade domain/net 类型。
  - trade extension 的主能力可以通过 module + host context + adapter/facade 完成装配，不需要 core 添加 trade-specific 分支。
  - 样板 submod 能在不修改 core 的前提下复用 trade/item 相关扩展能力。

- 编写一个样板 submod：
  - 推荐“红包消息/附件消息/自定义载荷消息”三选一，优先红包
  - 当前暂缓，不作为进入 Phase 5 前的阻塞项

## Test Plan
- 协议测试：
  - `v2` 消息封装/解包正确
  - 不支持的类型能正确拒绝或降级
- 发现机制测试：
  - 反射能找到合法扩展
  - 重复类型 ID / 能力 ID 有清晰错误
  - 坏扩展不会拖垮整体启动
- 消息链路测试：
  - 默认文本消息仍可正常显示
  - 扩展消息可被自定义渲染
  - 被拦截消息不会落入默认 UI
- 物品链路测试：
  - 旧交易物品仍能正确收发
  - 未知载荷能软失败
  - 自定义 codec 可独立工作
- 兼容性测试：
  - 新客户端连接旧服务器自动进入 legacy 模式
  - legacy 模式下扩展能力被正确禁用
- 样板验证：
  - 红包 submod 从注册到收发到展示全程无需修改核心

## Assumptions
- 第一阶段继续使用独立外部服务器，不做游戏内宿主。
- submod 接入以 **反射自动发现** 为主，不要求手工注册启动入口。
- 默认聊天/交易 UI 会保留，但它们只是官方默认实现，不再主导框架设计。
- `新服务器 -> 旧客户端` 兼容不是阶段一硬目标，如代价过高可放弃。
- 重构后的服务端需要继续支持 Docker 打包与部署，但这属于后续阶段约束，当前阶段只在架构设计中保留这一要求，不立即实现。
- 成功标准不是“重构后代码更好看”，而是“新玩法 submod 能不改核心直接接入”。
