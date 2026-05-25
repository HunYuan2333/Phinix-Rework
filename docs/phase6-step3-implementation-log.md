# Phase 6 Step 3 Implementation Log

## Summary
本次实现落地了 `module-first discovery + legacy fallback 降级 + 样板扩展跟进迁移`。

这一步的重点不是新增更多 framework 能力，而是把 extension 注册模型继续从“能发现零散实现类”推进到“以 module 作为正式注册边界”。

换句话说，`Step 2` 解决的是：

- builder / API registry / host API 暴露面

而 `Step 3` 解决的是：

- framework 现在主要发现的是 `module`
- module 自己决定注册哪些 handler / renderer / codec / API
- 非 module 自动发现不再是主路径，只保留为兼容过渡

## What Changed
### 1. Registry is now module-first
`Common/Utils/Framework/PhinixExtensionRegistry.cs` 的 discover 流程已改为两阶段：

1. 先扫描并实例化 `IPhinixExtensionModule`
2. 再把旧的非 module 类型作为 legacy fallback 兼容处理

这意味着当前 framework 的正式发现模型已经从：

- “扫描所有 handler / codec / renderer / provider 并平铺注册”

切到：

- “先发现 module，再由 module 显式注册自己的组成部分”

module 现在是：

- 正式注册边界
- 优先发现对象
- diagnostics / warnings 的主归属单位

### 2. Legacy auto-discovery was demoted to compatibility path
旧的“散装类型自动发现”并没有被一次性硬删除，但架构地位已经明显降低。

当前策略是：

- 非 `IPhinixExtensionModule` 类型不会再作为主注册模型被优先对待
- legacy 类型只在 module 注册之后才进入 fallback 流程
- fallback 发现到的旧扩展会留下明确 warning，提示迁移到 `IPhinixExtensionModule`

也就是说，兼容还在，但 framework 已经开始主动表达：

- 旧模型还能跑
- 但它不再是推荐路径
- 后续新增 extension 不应继续基于它写

### 3. Sample extensions were moved onto the module path
本次已把 red packet 样板从旧的：

- `IPhinixExtension + handler/provider`

迁到新的：

- `IPhinixExtensionModule`

client/server 两侧现在都会通过 `Register(IExtensionBuilder builder)` 显式注册：

- capability provider
- message handler
- renderer

这一步的意义不是功能变化，而是防止官方样板继续示范旧架构。

如果样板还停在 legacy 自动发现模式，后续 submod 作者会自然把旧模式当成正式 public model。
本次迁移之后，至少样板代码已经开始和 `Phase 6` 目标边界一致。

### 4. Item codec bootstrap stopped doing a second hidden discover pass
`Client/Source/Framework/PhinixClientItemPipeline.cs` 原本会在自身初始化时再次调用一次：

- `PhinixExtensionRegistry.DiscoverExtensions()`

这会带来几个问题：

- item pipeline 会脱离 host context 单独 discover
- 不相关 module 也会被重复初始化 / 重复 warning
- host service 依赖容易在错误的 discover 时机暴露出来

本次改为：

- item pipeline 只接收显式传入的 extension codecs

这使得 item codec 接入重新回到受控装配边界里，而不是继续保留一条“框架内部偷偷再 discover 一次”的旧路径。

## Boundary Impact
`Step 3` 真正推进的边界变化是：

- framework 发现的核心对象从“散装实现类”变成了 “module”
- extension 组成关系开始由 module 显式表达，而不是由反射副作用隐式拼出来
- 样板扩展不再继续示范 legacy 注册方式
- 某些内部基础设施不再绕开 host/runtime 自己重跑发现流程

这一步之后，Phinix 已经更接近：

- `extension module host`

而不只是：

- `handler host`

## Why This Counts As Step 3 Done
路线图中的 `Step 3` 目标是：

- 把 registry 的主发现对象从散装 handler 类切到 module
- 降低非 module 自动发现的架构地位

本次实现已经满足了这两个判断标准：

1. discover 过程已经先处理 `IPhinixExtensionModule`
2. legacy 自动发现已进入 fallback 分支，并附带迁移 warning
3. 官方样板已跟进到 module 注册模型

因此这一步虽然没有彻底删除 legacy fallback，但已经足以视为 `Step 3` 完成。

保留 fallback 是迁移策略，不是目标未完成。

## Transitional Debt Still Left
本次仍然故意保留了一些过渡态：

- legacy fallback 仍存在，尚未彻底强制 module-only
- `docs/submod-framework-api.md` 仍是旧 Phase 2 口径，还没有同步到 module-first + API registry
- chat/trade 协议常量仍在 core，等待 `Step 4`
- `message pipeline` 的长期命名清理仍未完成

这些都属于本轮之后仍然可见的技术债，但它们不构成 `Step 3` 的未完成项。

## Recommended Next Step
建议下一步直接进入 `Step 4`：

1. 把 chat / trade 协议常量与业务语义迁回各自 extension contract
2. 停止让 core 持有 `BuiltInChat*` 这类业务专属命名
3. 继续把 `message pipeline` 的正式长期语义收口到 `content pipeline`

如果 `Step 3` 做完后不继续接 `Step 4`，当前 module-first discovery 仍会和 core 中残留的 chat/trade 业务语义并存，边界虽然更清楚，但还没有彻底收口。

## Verification Note
本次改动已在本地代码层面完成以下检查：

- registry discover 流程已经切成 module-first + legacy fallback
- red packet client/server 样板已经迁到 `IPhinixExtensionModule`
- item pipeline 不再自行触发第二次 discover

同时，用户已在本地 Visual Studio 环境确认编译通过。

因此当前 `Step 3` 的完成判断基于：

- 代码改动已落地
- 路线图目标已满足
- 本地编译已通过

当前未补充的是系统化测试矩阵，而不是 `Step 3` 本身的实现闭环。
