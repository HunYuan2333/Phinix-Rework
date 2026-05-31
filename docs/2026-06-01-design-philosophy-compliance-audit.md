# 设计哲学合规审计报告

> 审计基准: [设计哲学.md](设计哲学.md) (2026-05-31)
> 审计日期: 2026-06-01
> 审计范围: Client、Server、Common、Extensions 全部 .cs 源码及 .csproj 引用关系
> 审计方法: 逐项对照设计哲学的所有条款，检查每一处违反点并给出严重度/证据/建议

---

## 严重度定义

| 级别 | 含义 |
|------|------|
| **CRITICAL** | 直接违反核心原则，继续放任会导致架构边界完全失效。必须优先修复。 |
| **HIGH** | 明确违反设计哲学中的禁止项，且当前已有实际耦合影响。应在当前 Phase 修复。 |
| **MEDIUM** | 耦合存在但已有限制因素，不会立即扩散。可排入下一 Phase。 |
| **LOW** | 轻微偏离，或属于文档标记的过渡态。主要是技术债标记和未来清理。 |

---

## 三条管道实际路由（经代码验证）

```
  Kind                  = "message" | "command" | "item"
  FrameworkPacket.Kind  → 决定进入哪条管道
  FrameworkPacket.Flow  → protobuf 层的语义标记 (FrameworkFlow.Message/Command/Item)
  FrameworkPacket.MessageType → 具体类型标识 (如 "chat.message", "trade.snapshot")
```

| 管道 | Kind | Client 入站 | Client 出站 | Server 入站 | Server 出站 |
|------|------|-------------|-------------|-------------|-------------|
| **Message** | `"message"` | `IClientMessageHandler` → `IMessageRenderer` | `IClientMessageHandler.HandleOutgoingText()` | Interceptor → DefaultHandler → Observer | 统一走 OutboundInterceptor |
| **Command** | `"command"` | `IClientCommandHandler` | `IClientOutgoingCommandHandler` | Interceptor → DefaultHandler → Observer | 统一走 OutboundInterceptor |
| **Item** | `"item"` | **不存在独立路由**（Client 端 packetHandler 中没有 KindItem case，Item 实际上作为 Command 的 payload 传输） | sendPacket 中设置 KindItem | `IItemCodec` 解码（ServerPipelineRunner.ProcessIncomingItem） | 统一走 OutboundInterceptor |

**关键理解:**
- `FrameworkPacket.MessageType` 是 packet 的**类型标识字段**——不只是 message 管道用它，command 和 item 管道也用它。命名残留了 chat-era 语义，但实际职责是通用的类型标签。
- Item 管道当前是**半独立**的——Server 端有完整路由（`KindItem` → `handleItem` → `IItemCodec`），但 Client 端没有 `KindItem` 入站分支。Item 数据在多数场景下作为 Command 的 `FrameworkItemPayload` 嵌套传输（如 Trade 的 `CreateOfferUpdateRequest` 中携带物品列表）。
- `FrameworkDisplayMessage` 是 Message 管道的**产出物**（`ClientIncomingMessageResult.DisplayMessage`），属于 framework 通用数据契约，放在 Common 合理。

---

## 一、Client 侧审计

### C-1 [CRITICAL] Client.cs 持有 Chat 专用的公开属性

**违反: §1.2 host 不依赖插件、§2.3 减少硬编码**

`[Client/Source/Client.cs:61]`
```csharp
public bool CanUseFrameworkChat => frameworkClient != null ...
```

**分析:**
- `CanUseFrameworkChat` 是专为 Chat 业务存在的属性。宿主不应暴露"聊天是否可用"这个业务判断
- 调用方应直接使用 `IFrameworkClientLifecycle.CompatibilityMode` 自行判断

**建议:**
- 移除 `CanUseFrameworkChat`；调用方改为通过 `IFrameworkClientLifecycle.CompatibilityMode` 自行判断

**品质检查:**
- 确认所有引用方已迁移后再移除属性
- 如需过渡，标记 `[Obsolete("Use IFrameworkClientLifecycle.CompatibilityMode instead")]`

---

### C-2 [HIGH] Settings.cs 硬编码 Chat/Trade 业务配置默认值

**违反: §1.3 host 只做通用服务、§2.3 减少硬编码、开放封闭原则**

`[Client/Source/Settings.cs:100-112]`
```csharp
// DefaultExtensionSettings 中包含:
{ "chat.showNameFormatting", true },
{ "chat.messageLimit", 40 },
{ "trade.acceptingTrades", true },
{ "trade.allItemsTradable", false },
{ "trade.dropCurrentMap", false }
```

**分析:**
- 宿主 `Settings.cs` 在为 Chat/Trade 维护**默认值**——新增一个带配置的插件就必须改宿主
- 虽然读写通过通用 `GetExtensionSetting(key, default)` string key 完成（不是硬编码类型），但默认值仍然由宿主集中管理
- 设计哲学 §1.3 要求"插件设置由插件自行声明"

**建议:**
- 插件通过 `Register()` 向 host 注册自己的配置 schema（含默认值），host 只做持久化存储
- 或者各插件实现 `IExtensionConfigSection`，由 `IExtensionConfigProvider` 统一加载，插件自带默认值

**品质检查:**
- 不引入新的配置持久化机制——复用现有 `IExtensionConfigProvider` / `Settings` 体系
- 迁移后确认旧配置 key 的兼容性（配置升级路径）

---

### C-3 [HIGH] WriteSettings() 中硬编码 Trade 业务行为

**违反: §1.3 host 只做通用服务、开放封闭原则**

`[Client/Source/Client.cs:311]`
```csharp
userManager.UpdateSelf(Settings.DisplayName, 
    Settings.GetExtensionSetting("trade.acceptingTrades", true));
```

**分析:**
- 宿主保存设置时主动把 `trade.acceptingTrades` 同步到服务端——这意味着宿主**知道 Trade 的存在**
- 正确做法：Trade 插件监听 `IClientSettingsContext.OnSettingChanged`，检测到自己的 key 变更后自行同步

**建议:**
- 删除这行；Trade 插件在 `Activate` 中订阅 `IClientSettingsContext.OnSettingChanged`，自行处理 `trade.acceptingTrades` 的变更同步

**品质检查:**
- 确认 `IClientSettingsContext.OnSettingChanged` 在 key 变更时正确触发
- 确认 Trade 插件 `Shutdown` 中取消订阅 `-=`

---

### C-4 [HIGH] 认证成功回调中注入了 Trade 业务参数

**违反: §1.3 host 只做通用服务**

`[Client/Source/Client.cs:160-163]`
```csharp
authenticator.OnAuthenticationSuccess += (sender, args) =>
{
    userManager.SendLogin(
        displayName: Settings.DisplayName,
        acceptingTrades: Settings.GetExtensionSetting("trade.acceptingTrades", true)
    );
};
```

**分析:**
- 认证成功后的 `SendLogin` 携带 `acceptingTrades` 参数——纯 Trade 业务字段被提升到了握手层
- 没有 Trade 插件的部署场景下，这个字段毫无意义

**建议:**
- `SendLogin` 移除 `acceptingTrades` 参数
- Trade 插件在 `Activate` 中通过出站命令管线单独发送 `acceptingTrades` 状态

**品质检查:**
- 确认 `SendLogin` 的 protobuf 定义兼容（已废弃字段用 `[Obsolete]` 标记而非删除字段编号）
- 确认服务端不再在 login 阶段校验 `acceptingTrades`

---

### C-5 [MEDIUM] dropPods 方法属于 Trade 业务逻辑

**违反: §1.3 host 只做通用服务**

`[Client/Source/Client.cs:516-522]`
```csharp
private LookTargets dropPods(IEnumerable<Thing> things)
{
    Map map = Settings.GetExtensionSetting("trade.dropCurrentMap", false) 
        ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
    ...
}
```

**分析:**
- 空投逻辑是纯 Trade 行为，内部还读取了 `trade.dropCurrentMap` 配置
- 当前通过 `Func<IEnumerable<Thing>, LookTargets>` 委托注入 host context，让 Trade 插件通过 `GetRequiredService<Func<...>>()` 消费——这种"宿主注入能力"的模式方向正确
- 但方法体本身留在宿主中是多余的——**这个 Func 到底做了什么，宿主不应该需要理解**

**减轻因素:**
- 方法为 private，仅通过委托注入

**建议:**
- 将整个 `dropPods` 实现搬迁到 Trade Client Extension，宿主只保留调用 `Find.CurrentMap` 等 RimWorld API 的能力注入

**品质检查:**
- 搬迁后空投行为不变
- 确认 Trade Extension 中没有引入新的 `UnityEngine` / `Verse` 等其他依赖

---

### C-6 [MEDIUM] `PlayNoiseOnMessageReceived` 偏 Chat 专属

**违反: §3.4 通用事件流 — 通用接口不应渗入单个插件的语义**

`[Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs:86]`
```csharp
bool PlayNoiseOnMessageReceived { get; }
```

**分析:**
- "收到消息时播放提示音" 是 Chat 专用语义，不应出现在通用 `IClientSettingsContext` 中
- 其他插件（Trade、未来邮件等）不需要"消息提示音"

**建议:**
- 将 `PlayNoiseOnMessageReceived` 移到 Chat 插件自己的 `ChatSettingsPanelProvider` 中管理
- `BlockedUsers` / `BlockUser` / `UnBlockUser` 可保留（跨插件语义），但需确认确实有多个插件需要

**品质检查:**
- 确认 Chat 插件中已有对应的设置 key 和 UI
- 移除 `IClientSettingsContext.PlayNoiseOnMessageReceived` 时保留原属性并标记 `[Obsolete]`

---

### C-7 [MEDIUM] Chat Client 编译引用 Trade Contracts

**违反: 不直接违反 §3.3，但需标记为插件间编译耦合**

`[Extensions/Chat/Client/ChatExtension.Client.csproj:102-104]`
```xml
<ProjectReference Include="..\..\Trade\Contracts\TradeExtension.csproj">
```

**分析:**
- §3.3 允许"插件间交互由插件自行负责"——Chat 引用 Trade Contracts 本身符合原则
- 但耦合方式是**编译时类型引用**（Chat 需要 `ITradeRequestApi` 接口定义）
- 更理想的方式是 Chat 仅通过 API registry 解析该接口，不直接 ProjectReference Trade 工程

**建议:**
- 短期可接受，但需标记为"插件间编译引用——如 Trade 未加载则降级"
- 长期可将 `ITradeRequestApi` 这类交叉 API 提取到更中立的 contract 层

---

## 二、Server 侧审计

### S-1 [HIGH] Server.csproj 构建 target 硬编码 Chat/Trade 扩展路径

**违反: §1.1 插件平权、§1.2 host 不依赖插件、开放封闭原则**

`[Server/Server.csproj:51-74]`
```xml
<Target Name="CopyOfficialServerExtensions" AfterTargets="Build">
    <Copy SourceFiles="..\Extensions\Chat\Server\bin\$(Configuration)\net10.0\ChatExtension.Server.dll"
          DestinationFolder="$(OutDir)Extensions" ... />
    <Copy SourceFiles="..\Extensions\Trade\Server\bin\$(Configuration)\net10.0\TradeExtension.Server.dll"
          DestinationFolder="$(OutDir)Extensions" ... />
    ...
</Target>
```

**分析:**
- 构建过程**知道 Chat 和 Trade 的存在**——新增官方扩展必须改这个 target
- 虽然这是 MSBuild target（非编译时类型引用），但仍然违反了"宿主不依赖插件"的开放封闭原则
- 好消息：运行时通过 `ExtensionAssemblyLoader` 动态加载 `Extensions/` 目录，未耦合类型

**建议:**
- 泛化为通配模式：`Extensions/*/Server/bin/$(Configuration)/net10.0/*.Server.dll`
- 或完全移除，由 `scripts/build-extensions.sh -all` 独立负责复制

**品质检查:**
- 修改后确认 Chat/Trade 服务端 DLL 和 Contracts DLL 仍出现在 $(OutDir)Extensions 中
- 确认 ExtensionAssemblyLoader 能正确加载通配复制的结果

---

### S-2 [HIGH] Config.cs 硬编码 Chat/Trade 的 XML 序列化命名空间

**违反: §1.3 host 只做通用服务**

`[Server/Config.cs:230-254]`
```csharp
string chatXml = string.Format(
    "<ChatServerConfig " +
    "xmlns=\"http://schemas.datacontract.org/2004/07/Phinix.ChatExtension.Server\">" + ...);
ExtensionConfigs["builtin.chat"] = chatXml;

string tradeXml = string.Format(
    "<TradeServerConfig " +
    "xmlns=\"http://schemas.datacontract.org/2004/07/Phinix.TradeExtension.Server\">" + ...);
ExtensionConfigs["builtin.trade"] = tradeXml;
```

**分析:**
- 服务端 Config 类**知道** Chat 和 Trade 扩展的内部 XML 序列化命名空间
- 这是旧版配置迁移逻辑，迁移完成后仍然编译在 Server 主程序中
- 违反开放封闭——新增一个有配置迁移需求的扩展就必须改 Config

**建议:**
- 将迁移逻辑抽取为 `LegacyConfigMigrator`，与 chat/trade 扩展一起编译，通过 `IExtensionConfigProvider` 注入迁移后的配置
- 或标记 `// Legacy migration — remove after v1.0` 并在下个 MAJOR 版本删除

**品质检查:**
- 抽取后旧配置文件仍能正确迁移
- 迁移失败不阻止 Server 启动（降级为默认配置）

---

### S-3 [MEDIUM] ServerUserManager 作为具体类型注入 host context

**违反: §1.3 — host 基础服务应通过抽象暴露**

`[Server/Server.cs:59]`
```csharp
extensionHostContext.AddService(UserManager);  // UserManager 是 ServerUserManager 具体类型
```

**分析:**
- Chat/Trade 扩展通过 `builder.HostContext.GetRequiredService<ServerUserManager>()` 获取
- 没有 `IServerUserManager` 抽象——扩展直接依赖具体类型
- 如果未来 ServerUserManager 拆分或重构，所有扩展都需要重新编译

**建议:**
- 引入 `IServerUserManager` 接口，`ServerUserManager` 实现它
- host 注入接口实现，扩展消费接口

**品质检查:**
- 接口定义放在 `UserManagement` shared 程序集
- 确认不新增内存泄漏：`ServerUserManager` 生命周期仍由 host 管理，接口注入不改变所有权

---

## 三、Common 层审计

### CM-1 [LOW] `FrameworkPacket.MessageType` 命名残留 chat-era 语义

`[Common/Utils/Framework/FrameworkPacket.cs:28]`
```csharp
public string MessageType { get; set; }
```

**分析:**
- 这个字段在当前协议中实际承载的是**所有 packet 的类型标识**——不只是 Message 管道，command 和 item 也用它
- 但改名涉及整个 codebase（数百处引用）和 wire format 兼容性
- Phase 6 正式收口

**建议:**
- Phase 6 改为 `TypeId` 或 `ContentType`，wire format 保持不变
- 当前不必动，只记录为远期命名收口任务

---

### CM-2 [LOW] `FrameworkProtocol.KindMessage` 命名建议收口

`[Common/Utils/Framework/FrameworkTypes.cs:14]`
```csharp
public const string KindMessage = "message";
public const string KindCommand = "command";
public const string KindItem = "item";
```

**分析:**
- `KindMessage` 当前承载的不只是"聊天消息"——是所有归属 Message 管道的流量
- Phase 6 建议改为 `KindContent`。但消息管道在设计哲学中正式名称仍是 "Message"，且设计哲学 §3.2 明确"Message 管道承载用户可见消息"
- **当前名称与设计哲学 §3.2 的命名一致，不属于违规**

**判断:** ✅ 当前合规。Phase 6 如需收口可与设计哲学同步更新。

---

### CM-3 [合规 ✅] `FrameworkDisplayMessage` 留在 Common —— 是 Message 管道契约产物

`[Common/Utils/Framework/FrameworkPacket.cs:66-91]`
```csharp
public sealed class FrameworkDisplayMessage { ... }
```

**经过重新分析后更正:**
- `FrameworkDisplayMessage` 是 Message 管道处理后的**产出契约**：Client handler 处理后产出一个 `DisplayMessage`，Client 的 UI 层据此渲染
- 它是 `ClientIncomingMessageResult.DisplayMessage` 和 `ClientIncomingCommandResult.DisplayMessage` 的类型——属于 framework 通用数据流的一部分
- Server 虽然不直接消费它用于 UI，但未来 Server 的 Message Observer（日志/审计）也完全可能构造此类型
- **放在 Common 是合理的，不应迁出**

---

### CM-4 [合规 ✅] Handler 接口在 Common — 是共享抽象的合理范畴

Framework 核心接口（`IClientMessageHandler`、`IServerMessageHandler`、`IClientCommandHandler`、`IItemCodec` 等）全部在 `Common/Utils/Framework/FrameworkTypes.cs`。这是双端都必须共同理解的 framework contract，放在 Common 完全符合 §4.2。

---

## 四、Extension 侧审计

### E-1 [MEDIUM] BuiltInChatClientExtension 单类实现 6 个接口

**违反: §2.1 松耦合 — 单类职责过重**

`[Extensions/Chat/Client/BuiltInChatClientExtension.cs:15]`
```csharp
public class BuiltInChatClientExtension : IPhinixExtensionModule, 
    IActivatablePhinixExtensionModule, ICapabilityProvider, 
    IClientMessageHandler, IClientCommandHandler, 
    IClientOutgoingCommandHandler, IMessageRenderer
```

**分析:**
- `Register()` 里手动组装了 `PhinixFrameworkChatService`、`ChatUiHostContext`、`ChatMessageList`、`ChatMainTabProvider`、`ChatSidebarProvider` 等大量内部组件
- Module 类承担了 module 入口 + 消息 handler + 命令 handler + 渲染器 + capability 声明的全部角色

**减轻因素:**
- 这是 extension 内部问题，不影响 host 边界

**建议:**
- 将 handler 实现拆为独立文件，module 只负责 `Register(builder)` 中组装

---

### E-2 [LOW] TradeService 混入 Legacy 适配器方法

`[Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs:403-491]`
```csharp
// Legacy 适配器专用:
public void UpsertTrade(...) { ... }
public void CompleteTrade(...) { ... }
```

**分析:**
- 已通过 `IFrameworkLegacyTradeCompletionApi` 接口隔离，LegacyAdapter 通过 `tradeApi as IFrameworkLegacyTradeCompletionApi` 使用

**建议:**
- Legacy 方法可进一步抽到独立 adapter class 中

---

## 五、汇总

### 按严重度

| 严重度 | 数量 | 项目 |
|--------|------|------|
| **CRITICAL** | 1 | C-1: `CanUseFrameworkChat` 宿主暴露 Chat 专用属性 |
| **HIGH** | 5 | C-2: Settings 硬编码业务默认值; C-3: WriteSettings 含 Trade 行为; C-4: 认证回调注入 Trade 参数; S-1: Server.csproj 硬编码扩展路径; S-2: Config.cs 硬编码扩展 XML 命名空间 |
| **MEDIUM** | 6 | C-5: dropPods; C-6: PlayNoiseOnMessageReceived; C-7: Chat→Trade 编译引用; S-3: ServerUserManager 具体类型注入; E-1: Chat Extension 单类职责过重; CM-1: MessageType 命名 |
| **LOW** | 3 | E-2: Legacy 方法混入 TradeService; CM-2: KindMessage 命名收口; CM-5/CM-6/CM-7: 过渡态/合规项 |

### 已合规 ✅

- Host 不再 ProjectReference 插件工程
- `ServerPipelineRunner` 在 `ServerRuntime` 程序集
- Server-only 源码通过独立 `.csproj` 编译
- Common 不再含 `BuiltInChat*` 常量
- 插件化设置面板已生效
- `FrameworkDisplayMessage` 留在 Common 合理（Message 管道契约产物）
- Handler 接口在 Common 合理（共享抽象）

---

## 六、修复优先级

### 当前 Phase（立即）

1. **C-1** (CRITICAL) — 移除 `CanUseFrameworkChat`
2. **C-3** (HIGH) — 从 `WriteSettings()` 移除 `trade.acceptingTrades`
3. **C-4** (HIGH) — 从认证回调移除 `acceptingTrades` 注入
4. **S-1** (HIGH) — 泛化 Server.csproj 扩展复制 target

### 下一 Phase

5. **C-2** (HIGH) — 配置默认值由插件自声明
6. **S-2** (HIGH) — Config.cs 迁移逻辑抽取
7. **C-5** (MEDIUM) — dropPods 迁入 Trade Extension
8. **C-6** (MEDIUM) — PlayNoiseOnMessageReceived 迁入 Chat 插件
9. **S-3** (MEDIUM) — ServerUserManager 抽象为接口
10. **E-1** (MEDIUM) — Chat Extension handler 拆分

### Phase 6（命名收口）

11. CM-1 — `MessageType` 重命名为 `TypeId`

---

## 七、修复品质检查要求（每次提交前必须确认）

按照设计哲学 §5-§8 和用户补充要求，每个修复必须遵守以下规则：

### 开放封闭

- 对扩展开放、对修改封闭。host/core 公开接口的变更保留原接口标记 `[Obsolete]`，不直接删除
- 新增插件行为不应要求修改 host/core 代码

### 必要重构不回避

- 如果一段代码已耦合到无法通过增量修改解决（如 C-1 已被多处引用），该重构就重构
- 但确保每步可编译、可运行、可验证（设计哲学 §6 — 渐进式迁移原则）

### 最小化技术债

- 不引入新的临时方案。如果有过渡措施，必须附带明确的移除条件（如 `// TODO: remove after Phase 6`）
- 过渡措施不能成为长期方案

### 内存泄漏

- 新增 `IDisposable` 资源持有者必须实现 `IDisposable` 并在 `Shutdown`/`Dispose` 中释放
- 事件订阅 `+=` 必须在 `Shutdown` 中有配对 `-=`
- 对字典/列表等集合的移除操作需确认键匹配（如 connectionId vs sessionId）

### 并发与锁

- 共享集合的读写必须有 `lock` 保护或使用 `ConcurrentDictionary`/`ConcurrentQueue`
- 网络回调（poll 线程触发）操作 UI/共享状态时封送至主线程
- `Connected` 检查与 `Send` 之间无 TOCTOU 竞态（使用 `TrySend` 代替 `Send`）

### UI 刷新

- Draw 路径（`DoWindowContents`/`Draw`/`DoButton`）上没有新的 per-frame 对象分配
- Regex 必须是 `static readonly` 预编译实例
- 属性 getter 不做实时计算/分配（改为推送式缓存更新）

### 错误处理

- protobuf `ParseFrom`/反序列化有 try-catch，单个损坏消息不中断管线
- 管线中单个 handler 异常被 PipelineRunner 捕获后继续处理下一个候选者
- 离线发送触发 Error 级别日志或回调通知，不静默丢弃
