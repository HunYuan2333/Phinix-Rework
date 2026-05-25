# Phinix Trade Phase 5 Session Context

## Summary
这份文档用于给下一次对话直接续上下文，聚焦 `Phase 5: legacy trade 拆除与插件化边界收口`。

当前总体判断：
- `chat` 已完成 framework-native 收口，可视为 Phase 4 完成。
- `trade` 已开始进入 Phase 5，但目前仍处于“主链已迁移一部分、兼容层仍存在”的状态。
- 当前最重要的原则不是“尽快把所有 trade 相关类型塞进 `Common/Utils`”，而是**先守住 core / extension 的边界，再逐步搬迁真正通用的契约**。

## Important Boundary Decision
关于 `Common/Utils`，当前结论很明确：

- `Common/Utils` 适合承载：
  - framework core contracts
  - extension/module contracts
  - metadata helpers
  - framework-native protobuf contracts
  - 明确与具体玩法无关的共享模型
- `Common/Utils` 不适合直接吸收：
  - legacy `Trading` 的运行时逻辑
  - legacy 网络包处理器
  - legacy 状态机
  - 旧 trade 持久化实现
  - 任何“只是为了搬家而搬家”的业务实现

换句话说：
- **可以迁的是“去业务化后的通用契约”。**
- **不该迁的是“旧交易系统本体”。**

今天在进一步推进时，已经意识到下一步如果开始把 `ImmutableTrade` / `ProtoThing` / event args 等类型迁走，就必须非常克制：
- 只能迁“客户端/UI 现在真实依赖、且可以去业务化”的数据模型；
- 不能把 `Common/Trading` 的 legacy 语义整包平移到 `Common/Utils`，否则会把 core 再次污染。

因此，今天已经**决定暂停继续向 `Common/Utils` 迁类型**，等待下一次对话里在这个边界前提下继续做。

## What Was Implemented
### 1. Framework module/context groundwork
已完成并之前已通过 VS 重建与 chat 黑盒验证：
- framework metadata helpers
- extension module discovery/registration
- host context / host services
- chat built-in extension module 化
- chat 主链已 framework-native

相关核心文件：
- `Common/Utils/Framework/FrameworkTypes.cs`
- `Common/Utils/Framework/FrameworkPacket.cs`
- `Common/Utils/Framework/PhinixExtensionRegistry.cs`
- `Client/Source/Framework/PhinixFrameworkClient.cs`
- `Server/Framework/PhinixFrameworkServer.cs`
- `Client/Source/Extensions/BuiltInChatClientExtension.cs`
- `Server/Extensions/BuiltInChatServerExtension.cs`

### 2. Trade framework-native 主链骨架
已新增：
- framework-native trade contracts
- client/server built-in trade extensions
- client repository / server store / trade services

主要文件：
- `Common/Utils/Framework/FrameworkTradeContracts.cs`
- `Client/Source/Extensions/BuiltInTradeClientExtension.cs`
- `Server/Extensions/BuiltInTradeServerExtension.cs`
- `Client/Source/Framework/PhinixFrameworkTradeClientRepository.cs`
- `Client/Source/Framework/PhinixFrameworkTradeClientService.cs`
- `Server/Framework/PhinixFrameworkTradeServerService.cs`
- `Server/Framework/PhinixFrameworkTradeStore.cs`

### 3. UI compatibility path still works
已做过的兼容层工作：
- framework trade snapshot / response 能投影回旧 UI 需要的形状
- 旧 `TradeWindow` / `TradeList` 暂时仍可用
- 修过一个真实黑盒问题：
  - 上传单件物品后 reset 不会把它放回底部候选列表

相关文件：
- `Client/Source/Framework/DefaultLegacyTradeItemCodec.cs`
- `Client/Source/Framework/PhinixClientItemPipeline.cs`
- `Client/Source/GUI/Windows and panels/TradeWindow.cs`

### 4. Phase 5 的 facade / adapter 支点已落地
这是今天最重要的新增工作。

#### 4.1 Client trade facade
新增：
- `Client/Source/Framework/IClientTradeFacade.cs`

效果：
- 默认 trade 行为与 UI 事件投影不再直接依赖 `ClientTrading`
- `Client` 现在实现了 `IClientTradeFacade`

#### 4.2 Trade UI facade
新增：
- `Client/Source/Framework/ITradeUiFacade.cs`
- `Client/Source/Framework/ClientTradeUiFacade.cs`

效果：
- `TradeWindow`
- `TradeList`

这两个 UI 组件已经不再直接把 `Client.Instance` 当成整个 trade 宿主，而是先经过 trade UI 门面。

#### 4.3 Unified client trade service abstraction
新增：
- `Client/Source/Framework/IClientTradeService.cs`
- `Client/Source/Framework/LegacyClientTradeServiceAdapter.cs`
- `Client/Source/Framework/FrameworkClientTradeServiceAdapter.cs`

效果：
- `Client.cs` 的公开 trade API 基本都改成走 `ActiveTradeService`
- 宿主不再到处写 `if (UseFrameworkTrade) ... else trading...`
- `Client.cs` 已不再把 `ClientTrading` 作为长期成员字段持有
- 现在只是构造时创建 legacy adapter，再由宿主统一通过 `IClientTradeService` 使用

#### 4.4 Legacy server runtime abstraction
新增：
- `Server/Framework/ILegacyTradeRuntime.cs`
- `Server/Framework/LegacyServerTradeRuntimeAdapter.cs`

效果：
- `Server.cs` 不再直接持有 `ServerTrading` 作为宿主字段
- 现在以 `legacyTradeRuntime` 的形式存在
- 这是为后面移除 legacy runtime 做准备

## What Was Not Implemented
以下内容今天**没有做**，而且是有意停下来的：

### 1. 没有开始把 trade 共享类型迁入 `Common/Utils`
还没有落地这些迁移：
- `ImmutableTrade`
- `ProtoThing`
- `TradeFailureReason`
- `CreateTradeEventArgs`
- `TradeUpdateEventArgs`
- `TradesSyncedEventArgs`
- `CompleteTradeEventArgs`

原因不是忘了做，而是已经明确意识到：
- 如果不先把这些类型重新设计成“去业务化的共享模型”，直接平移会把 legacy trade 语义继续污染 `Common/Utils`

### 2. 没有删除 `Common/Trading`
当前仍不能删除，主要原因：
- legacy runtime 仍存在：
  - `ClientTrading`
  - `ServerTrading`
- 客户端大部分 UI-facing trade 数据类型仍来自 `Trading` 命名空间
- `TradingThingConverter` / `StackedThings` / `TradeWindow` / `TradeList` / trade event args 仍依赖旧类型

### 3. 没有完成旧服兼容/自动降级收口
虽然 roadmap 已经把它归到 Phase 5，但今天没有继续实现这部分。

## Current Compile / Verification Status
需要非常诚实地记录：

- 在今天中段之前，多轮 VS 重新生成都已经成功。
- chat 黑盒验证通过过，extension path 已被日志确认。
- trade 黑盒做过一轮，修过一个 reset/候选列表回归。

但是：
- **今天最后一批“服务端 legacy runtime 抽象”和“客户端 unified trade service adapter”改动之后，还没有再拿到新的 VS 重建结果。**
- 所以下一轮开始时，第一件事应该是：
  - 用 VS 重新生成解决方案
  - 先消化最新的第一层编译错误

不要假设今天最后状态已经编译通过。

## Unverified Recent Files
下一次对话优先验证这些文件：

### Client
- `Client/Source/Client.cs`
- `Client/Source/Client.csproj`
- `Client/Source/Framework/IClientTradeService.cs`
- `Client/Source/Framework/LegacyClientTradeServiceAdapter.cs`
- `Client/Source/Framework/FrameworkClientTradeServiceAdapter.cs`
- `Client/Source/Framework/IClientTradeFacade.cs`
- `Client/Source/Framework/ITradeUiFacade.cs`
- `Client/Source/Framework/ClientTradeUiFacade.cs`
- `Client/Source/Framework/PhinixDefaultTradeBehaviour.cs`
- `Client/Source/UICreateTradeEventArgs.cs`
- `Client/Source/UITradeUpdateEventArgs.cs`
- `Client/Source/UITradesSyncedEventArgs.cs`
- `Client/Source/GUI/Compound Widgets/TradeList.cs`
- `Client/Source/GUI/Windows and panels/TradeWindow.cs`

### Server
- `Server/Server.cs`
- `Server/Server.csproj`
- `Server/Framework/ILegacyTradeRuntime.cs`
- `Server/Framework/LegacyServerTradeRuntimeAdapter.cs`

## Recommended Next Steps
下一次对话建议按这个顺序继续：

1. **先 VS 重新生成**
   - 不要直接继续大改
   - 先把最后这批 facade/adapter/runtime 抽象改动修到可编译

2. **继续压缩 `Common/Trading` 对客户端的类型依赖**
   - 但不是直接把旧类型整包挪到 `Common/Utils`
   - 正确方向是：
     - 先设计一个新的、去业务化的 trade model namespace
     - 再逐步把客户端 UI / facade / adapter 改成吃新的模型

3. **优先迁的是“客户端真正依赖的数据形状”，不是 legacy runtime**
   推荐下一步候选：
   - `ImmutableTrade`
   - `ProtoThing`
   - `TradeFailureReason`
   - trade event args

   但要放在新的命名空间和新的边界里，避免把 legacy 语义污染到 `Common/Utils`

4. **等客户端数据形状迁完，再考虑删除 legacy adapter**
   目前 adapter 是权宜之计，最终目标不是保留 adapter，而是：
   - legacy runtime 被彻底删除
   - framework trade 成为唯一实现
   - facade 成为长期稳定边界
   - adapter 只是拆迁过程中的过渡层

## Explicit Caution For Next Conversation
下一次继续时，务必记住：

- 不要被“为了删 `Common/Trading`”这个目标诱导，把旧 trade 业务逻辑塞进 `Common/Utils`
- `Common/Utils` 只能接收：
  - framework-neutral contracts
  - 去业务化的共享数据模型
  - 不带 legacy runtime 语义的桥接契约
- 如果某个类型只是 `Common/Trading` 的业务对象换了个目录，那不是重构成功，是污染扩散

## Related Docs
- `docs/phinix-framework-roadmap.md`
- `docs/phase6-core-only-extension-architecture.md`
- `docs/phinix-core-extension-boundary-assessment.md`

## Short Handoff
一句话交接：

当前已经把 trade 的宿主依赖、默认行为依赖、UI 入口依赖收成了 facade/service/runtime adapter 几层，方向是对的；但真正阻止删除 `Common/Trading` 的最大问题已经从“宿主双轨”变成了“客户端仍然直接依赖 `Trading` 共享类型”。下一步应该先验证最新编译状态，然后谨慎地把这些共享类型迁到新的去业务化模型层，而不是把 legacy trade 本体搬进 `Common/Utils`。
