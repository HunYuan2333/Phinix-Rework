# Phase 6 Step 4 Implementation Log

## Summary
本次实现落地了 `chat contract ownership migration + core chat constant removal + shared chat contract project`。

这一步的重点不是继续改 framework 主链行为，而是把 `chat` 从 “虽然已经 extension 化，但契约仍挂在 core 周边” 再往前推一层，真正对齐到和 `trade` 一样的 shared contract ownership。

换句话说，`Step 3` 解决的是：

- module-first discovery
- legacy fallback 降级
- extension 以 module 作为正式注册边界

而 `Step 4` 解决的是：

- chat 协议常量不再归 `Utils.Framework` 持有
- chat proto / payload 不再归 `Common/Utils` 持有
- client/server chat 实现改为依赖独立 chat extension contract
- core 不再继续保留 `BuiltInChat*` 这类 chat 专属协议 ownership

## What Changed
### 1. Introduced a shared ChatExtension contract project
本次新增了：

- `Common/ChatExtension/ChatExtension.csproj`
- `Common/ChatExtension/ChatContracts.cs`
- `Common/ChatExtension/Proto/Message/BuiltInChat.proto`
- `Common/ChatExtension/Proto/Message/compiled/BuiltInChat.cs`

这使得 chat 现在和 `TradeExtension` 一样，拥有自己的 shared contract project，而不再继续把共享契约塞在 `Common/Utils` 里。

其中 `FrameworkChatProtocol` 现在持有 chat extension 的协议常量，包括：

- `Capability`
- `MessageType`
- `HistoryRequestType`
- `HistorySyncCompleteType`

这里保留了原有 wire values，不改字符串值，因此不会因为 `Step 4` 本身引入协议不兼容。

### 2. Core stopped owning BuiltInChat protocol constants
`Common/Utils/Framework/FrameworkTypes.cs` 中原本定义在 `FrameworkProtocol` 下的：

- `BuiltInChatMessageType`
- `BuiltInChatHistoryRequestType`
- `BuiltInChatHistorySyncCompleteType`

已在本次移除。

这意味着：

- `FrameworkProtocol` 重新收口为 core 级协议常量
- core 不再直接表达 chat 的领域 type
- chat 不再拥有高于其他 extension 的协议地位

这一步是 `Phase 6` 边界里“core 不理解具体业务”的直接落实。

### 3. Client/server chat code switched to extension-owned contracts
本次已把 client/server 的 chat 调用点统一切到 `Common/ChatExtension`：

- `Client/Source/Extensions/BuiltInChatClientExtension.cs`
- `Client/Source/Framework/PhinixFrameworkChatService.cs`
- `Server/Extensions/BuiltInChatServerExtension.cs`
- `Server/Framework/PhinixFrameworkChatBroadcast.cs`

这些代码现在通过：

- `FrameworkChatProtocol`
- `BuiltInChatMessagePayload`
- `BuiltInChatHistoryStore`

来访问 chat 契约，而不再依赖 `Utils.Framework.FrameworkProtocol.BuiltInChat*`。

这一步的意义不是行为变化，而是契约归属变化：

- chat 现在消费的是 chat extension contract
- 而不是 core 私有常量

### 4. Chat proto/generate ownership moved out of Utils
本次对 `Common/Utils` 做了明确收口：

- `Utils.csproj` 不再编译 `BuiltInChat.cs`
- `Utils.csproj` 不再把 `BuiltInChat.proto` 当成自身 content
- `Common/Utils/compile-proto.bat`
- `Common/Utils/compile-proto.sh`

都不再负责生成 chat message proto

同时，以下旧文件已从 `Common/Utils` ownership 中移出：

- `Common/Utils/Framework/Proto/Message/BuiltInChat.proto`
- `Common/Utils/Framework/Proto/Message/compiled/BuiltInChat.cs`

这说明：

- `Utils` 已不再拥有 chat payload/schema
- chat payload 不再以“framework message proto 的一部分”存在
- `BuiltInChat.proto` 现在是 chat extension 自己的契约，而不是 core message bucket 的内容

### 5. Solution/project references were updated for the new ownership
为了让新的 shared contract 真正成为正式依赖链，本次同步更新了：

- `Client/Source/Client.csproj`
- `Server/Server.csproj`
- `Phinix.sln`

client/server 现在显式引用 `Common/ChatExtension/ChatExtension.csproj`。

这使得 chat contract project 不是“逻辑上存在”，而是已经进入真实构建图。

## Boundary Impact
`Step 4` 真正推进的边界变化是：

- core 不再拥有 chat 专属协议 type
- `Common/Utils` 不再拥有 chat proto / payload schema
- chat extension 的 shared contract 边界第一次和 trade extension 对齐
- client/server 代码开始以“extension contract consumer”身份使用 chat，而不是继续从 core 取 chat 常量

也就是说，这一步之后：

- chat 虽然仍然是 official built-in extension
- 但它已经不再以 core 协议特例的方式存在

这正是 `Phase 6` 里 “chat must also be pluginized” 的一部分实质落地。

## Why This Counts As Step 4 Done
路线图中的 `Step 4` 目标是：

- 把 built-in chat / trade 常量迁到各自 extension contract 项目
- 停止在 core 中保留 chat 专属命名与协议 ownership

本次已经满足其中针对 chat 的核心判断标准：

1. `FrameworkProtocol.BuiltInChat*` 已移除
2. chat 协议常量已有独立 contract owner
3. chat proto / payload 已移出 `Common/Utils`
4. client/server chat 代码已切到新 contract 引用

因此从 `chat contract migration` 这个角度，`Step 4` 可以视为完成。

这里之所以说是 `Step 4`，而不是直接说整个 `Phase 6` 快结束，是因为这一步只处理了：

- chat 契约 ownership

还没有处理：

- `message pipeline -> content pipeline` 命名收口
- `FrameworkDisplayMessage` 从 core 迁出
- host 进一步去业务装配

## Transitional Debt Still Left
本次仍然故意保留了几处过渡态：

- `FrameworkDisplayMessage` 仍留在 core
- `IClientMessageHandler` / `IServerMessageHandler` 仍保留旧命名
- `message` / `command` / `item` 语义清理还没有开始全量收口
- chat / trade 官方 extension 仍然是宿主项目中的 built-in 代码边界，而不是外部 plugin package
- `Step 5` 所需的 host business composition 收口还未开始

这些都属于 `Step 4` 之后仍可见的技术债，但不构成这一步的未完成项。

## Recommended Next Step
建议下一步直接进入 `Step 5`：

1. 继续削薄 host 对 chat/trade 的业务装配认知
2. 用 core-level host services 替换 business-specific composition 入口
3. 明确 extension 之间只能通过 API registry 暴露的 contract 协作
4. 防止 host 以“另一种形式”重新把 chat/trade 装回自己体内

如果 `Step 4` 之后不继续接 `Step 5`，当前虽然已经把 chat 契约 ownership 从 core 挪开，但 host 侧仍可能继续保留过厚的业务组合职责。

## Verification Note
本次改动后的验收依据包括：

- 最新本地编译已确认没有问题
- chat 相关测试已完成
- 服务端启动与 chat history 加载正常
- 当前没有观察到明显 chat 回归

因此当前 `Step 4` 的完成判断基于：

- 代码迁移已落地
- 契约 ownership 已完成收口
- 构建与测试已确认可通过

当前仍未在这份文档中展开的是完整黑盒测试矩阵，而不是 `Step 4` 本身的实现闭环。
