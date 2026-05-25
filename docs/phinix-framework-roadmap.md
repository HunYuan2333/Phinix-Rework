# Phinix 框架化重构开发模型与路线图

## Summary
本项目继续采用 **演化式原型 + 迭代增量开发**，但当前阶段的重点已经从“继续搬功能”切换为“先把 core / host / extension 的边界钉死”。

当前总路线分两段理解：

- `Phase 1` 到 `Phase 4` 负责把 framework 主链做出来，并完成 chat 的 framework-native 收口。
- `Phase 5` 到 `Phase 6` 负责把 trade 从 legacy 过渡态继续往前推，同时把 server host 收口成真正的 `core-only host`，避免以后每加一个 official extension 都继续污染 core。

当前最重要的原则：

- 先守住 framework 边界，再继续搬业务代码。
- `core` 只保留通用协议、注册、发现、生命周期、pipeline 调度与基础 host context。
- `chat`、`trade`、后续 `red packet`、`market`、`mail attachment` 都必须作为 extension/plugin 存在，而不是继续长回 host 或 core。
- extension 之间的协作走最小 API registry，不引入复杂 DI / IoC / dependency solver。

## Current Focus
当前开发主线已经进入 `Phase 6`。

当前优先级不是继续扩展 trade 功能，而是先完成 `Phase 6 / Step 1`：

- 先把文档边界定死。
- 明确 server host 只保留 core 能力。
- 明确 extension 只能通过 `content / command / item` 三条 pipeline 接入。
- 明确 chat 也必须被视为 official extension/plugin，而不是 built-in 特例。

Phase 6 设计基线见：

- `docs/phase6-core-only-extension-architecture.md`

## Development Model
采用六段式开发模型：

1. **架构原型阶段**
   - 验证 `v2` 信封、扩展发现、默认显示可被接管这三个最高风险点。

2. **框架骨架稳定阶段**
   - 把协议、pipeline、capability negotiation、legacy adapter 边界定型。

3. **增量替换阶段**
   - 逐步把聊天、交易、默认 UI 行为迁入 framework 主链。

4. **built-in chat 收口阶段**
   - 完成 chat 的 framework-native 重写与 legacy chat 清场。

5. **legacy trade 拆除与插件化边界收口阶段**
   - 把 trade 从“主链已可跑”推进到“legacy 可安全下线”。

6. **core-only host 与动态 extension 架构收口阶段**
   - 把 core / host / extension 的职责彻底收口。
   - 引入 extension module + API registry 的最小平台能力。
   - 停止让官方功能以 built-in 特例的形式继续污染 core。

## Implementation Plan
### Phase 1: 架构原型
状态：已完成

- 落地 `v2` 协议草模与最小消息信封。
- 验证反射式扩展发现可行。
- 验证客户端显示链可被扩展拦截或替换。

完成判断：

- 自定义消息类型已可不改核心 UI 被发现、收发、渲染。
- 扩展可以接管默认显示链。

### Phase 2: 框架接口定型
状态：已完成

- 公共接口、handler 返回语义、capability negotiation、legacy 模式边界已定型。
- 当前代码已经具备继续承载 built-in extension 和样板扩展的基础能力。

### Phase 3: 迁移现有功能
状态：已完成（当前完成形态为 `legacy transport + pipeline adapter`）

- chat、trade、默认 UI 行为已迁入 framework 主链。
- item pipeline 已从“trade 附属工具”开始转向“独立基础设施”。
- 默认行为逐步从宿主代码下沉到 framework/extension 层。

完成判断：

- 无扩展时体验基本保持一致。
- 有扩展时默认 UI 可以被部分或全部接管。
- 新增消息类型或物品载荷不再要求 core 继续加业务分支。

### Phase 4: built-in chat 收口
状态：已完成

- chat 已完成 framework-native 主链迁移。
- 客户端 chat UI 已切换到 framework-native UI model。
- `Common/Chat` 已退出主运行链路并完成仓库级清理。

完成判断：

- 默认 chat 已不再依赖 legacy chat 类型。
- chat 运行在 framework 主链上，但当前仍允许 built-in chat service 暂时偏厚。

### Phase 5: legacy trade 拆除与插件化边界收口
状态：进行中

当前已完成：

- client/server built-in trade extension 主链骨架已落地。
- repository / service / facade / adapter 这几层支点已建立。
- 宿主对 `ClientTrading` / `ServerTrading` 的直接依赖已开始收缩。

当前未完成：

- `Common/Trading` 仍不能删除。
- trade UI 仍有一部分直接依赖 legacy 共享类型。
- 最新一批 facade / adapter / runtime 抽象改动之后，还需要重新验证编译状态。
- 旧服兼容与自动降级仍未在本轮收口完成。

当前已知问题追踪：

- `TradeWindow` 的本地物品回滚体验仍有缺陷。
  - 现象：当某类物品只剩一件时，点击 `upload/update` 后，下方候选物品列表会立即消失；随后点击 `reset`，候选列表不会把该物品稳定加回。
  - 当前影响：本地 UI / 本地物品回滚体验不可靠，但交易结束后物品通常仍会通过现有返还链路回到玩家。
  - 初步判断：`reset` 的本地回滚与 `pending item` / `drop pod` / 可交易候选列表刷新之间存在时序或落点不一致。
  - 建议优先级：`P2`，先记录追踪，不阻塞当前 `Phase 6 / Step 4` 收口。

当前规则：

- 不为了“删掉 `Common/Trading`”而把 legacy trade 本体搬进 `Common/Utils`。
- 只迁移真正去业务化、可复用的共享契约。

### Phase 6: core-only host 与动态 extension 架构收口
状态：进行中，`Step 1` 到 `Step 4` 代码迁移已基本完成，待最终验收

目标：

- server host 只保留 core 级能力，不再内嵌 chat/trade 业务装配。
- extension 只能通过 `content / command / item` 三条 pipeline 接入。
- chat 必须与 trade 一样，被当成 official extension/plugin 处理。
- 引入最小 `API registry`，支持 `RegisterApi<T>()` / `TryResolve<T>()` / `ResolveAll<T>()`。
- 把 extension 注册入口从“散装 handler 发现”收敛为 “module + builder”。

当前已完成：

- 更新 roadmap，明确 `Phase 6` 是当前主线。
- 以 `docs/phase6-core-only-extension-architecture.md` 作为新的边界基线。
- 清理已被新设计文档覆盖的旧草稿，避免后续继续按过时边界推进。
- 已抽出 `IExtensionBuilder` 与 `IExtensionApiRegistry`。
- registry 主发现对象已从散装 handler 类推进到 module-first。
- 非 module 自动发现已降级为兼容路径，不再作为主注册模型。
- chat 协议常量已从 `Utils.Framework.FrameworkProtocol` 迁出，改由独立 `Common/ChatExtension` 契约承载。
- chat proto / generated payload ownership 已从 `Common/Utils` 转移到 `Common/ChatExtension`。
- client/server chat 代码已切到 chat extension contract 引用，不再依赖 core 私有 `BuiltInChat*` 常量。

后续实现步骤：

1. 完成 `Step 4` 最终验收：
   - 补齐编译验证
   - 补齐 chat capability / send / history sync 轻量黑盒验证
   - 确认仓库里不再残留 `FrameworkProtocol.BuiltInChat*` 有效引用
2. 逐步把 `message pipeline` 正式收口为 `content pipeline` 语义。
3. 用 core 级 host services 替换 `BuiltInChat*HostServices` / `BuiltInTrade*HostServices` 这类业务专用装配。
4. 在 `Step 5` 中只引入轻量 lifecycle 约束：
   - extension 自己负责初始化、持久化、清理
   - 这些行为挂在通用 lifecycle phase 上
   - host 不再为具体业务扩展补专用时序钩子

release 后可考虑的增强，但不属于当前 `Phase 6` 的收口目标：

- server 侧用户/权限/操作审计等通用平台能力
- 基于 SQLite 的通用 user/platform storage API
- 面向 extension 的更完整 server platform service 层
- 更明确、统一的 extension lifecycle phase 语义与扩展约束
- activate / shutdown / save 等时机的统一 diagnostics、ordering、failure handling

这些能力属于开发增强，不应在首个 `Phase 6` release 前继续扩大范围。

## Test Plan
- 协议测试：
  - packet / envelope 编解码正确
  - 不支持的类型能正确拒绝或降级
- 发现机制测试：
  - 反射能发现合法 module
  - 重复 capability / type / API 注册有清晰错误
  - 坏扩展不会拖垮整体启动
- pipeline 测试：
  - `content / command / item` 三条主链分发行为清晰
  - 未知 item payload 能软失败
  - 扩展消息可被自定义渲染并可抑制默认 UI
- 兼容性测试：
  - 新客户端连接旧服务器可自动进入 legacy 模式
  - legacy 模式下 framework 扩展能力被正确禁用
- 架构回归测试：
  - 新增一个 official extension 不需要修改 core pipeline 结构
  - 移除某个 extension 后 host 仍可启动，其余 extension 以可预期方式 degrade

## Explicitly Postpone To 2.0
- dependency graph
- extension versioning
- hot reload
- sandboxing
- remote extension download
- complex plugin manifests
- dependency solver
- graph-based startup ordering
- multi-version API coexistence
- server-side user/permission/platform API layer
- SQLite-backed core platform storage services

这些能力都不是当前阶段的目标。当前优先保证的是：

- 边界简单
- 宿主变薄
- 扩展可注册、可发现、可协作
- 后续继续演化时不需要反复改 core

## Related Docs
- `docs/phase6-core-only-extension-architecture.md`
- `docs/phinix-core-extension-boundary-assessment.md`
- `docs/phinix-trade-phase5-session-context.md`
