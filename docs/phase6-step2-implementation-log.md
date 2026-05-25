# Phase 6 Step 2 Implementation Log

## Summary
本次实现落地了 `builder + API registry + 第一刀边界收口`。

这一步没有提前做 `Step 4` 的 chat 协议常量迁移，也没有在本轮强行完成 `message -> content` 全量命名重构；但已经把 extension 注册面、API 暴露面、以及 host 对 chat/trade 的装配边界向前推进了一大步。

本次重点不是“多加几个接口”，而是停止继续依赖 `BuiltIn*HostServices` 这类业务 wrapper，让 official extension 开始通过显式 API contract 与 framework / host 协作。

## What Changed
### 1. Core registration surface moved to builder
`IPhinixExtensionModule.Register(...)` 已从：

- `Register(IExtensionComponentSink sink, ExtensionHostContext hostContext)`

切到：

- `Register(IExtensionBuilder builder)`

新增了：

- `IExtensionBuilder`
- `IExtensionApiRegistry`
- `ExtensionApiRegistry`

builder 现在负责：

- 注册 capability provider
- 注册 handler / renderer / codec
- 注册 extension-owned API
- 通过 `TryResolveApi<T>()` / `ResolveApis<T>()` 查询已注册 API

### 2. Registry now has a shared API registry
`PhinixExtensionRegistry` 在 discover 阶段创建单个 `ExtensionApiRegistry`，并把它同时挂到：

- `ExtensionHostContext.ApiRegistry`
- `DiscoveredPhinixExtensions.ApiRegistry`

module 注册过程中，API 注册也会留下 diagnostics / warnings，包括：

- API 注册成功
- 同一 API 已重复注册
- 同一 API 出现多个 provider，解析将优先第一个 provider

### 3. Host context no longer carries business wrappers
`ExtensionHostContext` 现在直接暴露：

- `ApiRegistry`
- `TryResolveApi<T>()`
- `ResolveApis<T>()`

本次已删除：

- `Client/Source/Framework/BuiltInChatClientHostServices.cs`
- `Client/Source/Framework/BuiltInTradeClientHostServices.cs`
- `Server/Framework/BuiltInChatServerHostServices.cs`
- `Server/Framework/BuiltInTradeServerHostServices.cs`

同时 client/server 项目文件里的对应编译项也已移除。

### 4. chat/trade switched to extension-owned API contracts
client/server 两侧都引入了 extension-owned API contract，并让 built-in module 在注册时显式暴露这些 API：

- client chat: `IFrameworkChatClientApi`
- client trade: `IFrameworkTradeClientApi`
- server chat: `IFrameworkChatServerApi`
- server trade: `IFrameworkTradeServerApi`

当前策略是：

- host 仍可创建 official extension service 实例
- 但 host 只通过 extension-owned API interface 暴露它们
- module 通过 `builder.HostContext.GetRequiredService<IFramework...Api>()` 获取并 `RegisterApi(...)`
- host 后续再通过 framework runtime 的 `TryResolveExtensionApi<T>()` 取回注册后的 API

这意味着：

- host 不再依赖 `BuiltIn*HostServices`
- `frameworkClient.ConfigureChatService(...)` 这类 chat 特例入口已删除
- client/server 不再需要私有 wrapper 才能把 chat/trade 接进 framework

### 5. Client/server runtime now resolve APIs from registry
新增了：

- `PhinixFrameworkClient.TryResolveExtensionApi<T>()`
- `PhinixFrameworkClient.ResolveExtensionApis<T>()`
- `PhinixFrameworkServer.TryResolveExtensionApi<T>()`
- `PhinixFrameworkServer.ResolveExtensionApis<T>()`

client 现在通过 runtime 解析 chat/trade API，而不是保留 chat 专属 setter。

server 现在通过 runtime 解析 chat/trade API，然后继续挂接：

- log
- load/save
- trade login completion replay

虽然这些生命周期挂接仍在 host 层，但依赖的已经是 extension API contract，而不是 host 私有业务 wrapper。

## Boundary Impact
本次真正砍掉的不是功能，而是这些旧边界：

- host 不再通过 `BuiltIn*HostServices` 把业务服务重新塞回 module
- chat 不再拥有 `ConfigureChatService(...)` 这种 runtime 私有后门
- extension 注册不再只有 handler/component 级别，而是上升到了 API 级别

这一步之后，official extension 至少已经开始以“显式 API provider”的方式存在，而不只是“宿主里的一段内部模块代码”。

## Transitional Debt Still Left
本次故意保留了几处过渡态：

- `FrameworkProtocol.BuiltInChat*` 常量仍在 core，等待 `Step 4`
- `message` / `command` / `item` 的旧命名仍保留，等待后续语义清理
- host 仍直接 new 了 official extension service 实现，只是暴露面已缩到 API contract
- 散装 handler 自动发现仍保留兼容，尚未彻底强制 module-only

这些都不是本次遗漏，而是为了把 `Step 2` 和 `Step 4` 分开，避免把协议迁移、命名迁移、模块化收口混成一次高风险大爆炸。

## Why Step 4 Was Not Pulled Forward
这次如果顺手把 chat 协议常量一起迁掉，会把三件事情耦在一起：

- builder / API registry 引入
- host composition 收口
- chat 协议常量与 payload 迁移

那样出问题时会很难区分是：

- registry 设计有问题
- host runtime 注入有问题
- 还是协议迁移导致兼容性回归

所以本次选择先把组合边界切开，再在下一步把业务常量从 core 继续移出去。

## Recommended Next Step
建议下一步直接接：

1. `Step 3`
   - 把主发现对象进一步从散装类型推进到 module-first
   - 降低非 module 自动发现的架构地位

2. `Step 4`
   - 把 `BuiltInChat*` 常量与契约迁出 core
   - 停止让 core 继续持有 chat 领域语义

3. 命名清理
   - 逐步把 `message pipeline` 的长期语义收口为 `content pipeline`

如果这三步不继续跟上，当前这次收口仍可能被后续新增 feature 再次侵蚀回 host / core。

## Verification Note
本次尝试过用 `dotnet build` / `dotnet msbuild` 验证项目，但当前环境会先在 `Dependencies/protobuf/csharp/src/Google.Protobuf/Google.Protobuf.csproj` 的 SDK resolver 阶段失败：

- `MSB4276`
- `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator`
- `Microsoft.NET.SDK.WorkloadManifestTargetsLocator`

因此本次没有拿到一份可信的全量成功编译结果。
这属于当前机器上的构建环境问题，不是本次 Step 2 改动本身提供出来的 C# 编译错误列表。
