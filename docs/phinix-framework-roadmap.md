# Phinix 框架化重构开发模型与路线图

## Summary
本项目不适合使用纯瀑布模型。建议采用 **演化式原型 + 迭代增量开发**：先用小范围原型验证最关键的架构假设，再逐步稳定接口、替换旧实现、补齐兼容层，最后用真实 submod 验证框架可用性。

核心原则：
- 先验证“扩展性是否真实存在”，再做大规模重构。
- 先稳定框架边界，再补默认功能。
- 先保证 `新客户端 -> 旧服务器` 兼容，再决定是否做 `新服务器 -> 旧客户端`。
- 所有阶段都以“红包 submod 能否自然接入”作为检验标准。

## Development Model
采用四段式开发模型：

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

4. **试点扩展阶段**
   - 目标：用至少一个真实 submod 验证框架。
   - 推荐直接做“红包/特殊消息载荷”作为样板扩展。
   - 如果样板扩展仍需改核心代码，说明框架抽象失败，需要回退修正接口。

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

### Phase 4: 兼容与样板验证
状态：进行中

- 当前阶段优先级说明：
  - 先完成兼容性与降级行为的收口。
  - 样板 submod 验证仍属于 Phase 4 范围，但不是当前最优先事项。
  - 现有 `message` 通道继续收紧为“显示流”。
  - 后续如需承载状态同步、请求/回执、私有控制信息，应新增与 `message` 平行的 `command` 通道，而不是继续混入当前显示流。

- 实现 `新客户端 -> 旧服务器` 自动探测与 legacy 降级。
- 在 legacy 模式下：
  - 维持基础 chat/trade
  - 禁用 `v2` 扩展消息与高级 item payload
  - 给用户明确提示当前连接不支持框架能力
- 编写一个样板 submod：
  - 推荐“红包消息/附件消息/自定义载荷消息”三选一，优先红包
  - 当前暂缓，不作为进入 Phase 4 后的第一优先级任务
- 验收标准：
  - 新客户端连旧服能正常用基础功能
  - 样板 submod 能独立注册、收发、渲染，不改核心代码
  - 框架失效时退回默认行为而不是直接崩

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
