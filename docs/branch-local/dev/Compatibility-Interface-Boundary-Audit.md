# F8 旧接口适配边界审查与删除评估

日期：2026-10-10。状态：原轮源码审查与设计；同日后续已清理两个维护插件的旧作者入口，见 §1 和维护插件记录。主仓 adapter/旧接口整治仍待实施。

## 1. 本轮范围决定

用户明确：Steam 客户端统一更新，目前没有第三方插件；不采用新旧业务接口混用的方案。旧接口可以通过各自的 adapter 接入新实现，adapter 不限于 `LegacyAdapter` 协议插件。新业务不应知道任何历史接口、旧数据格式、兼容模式或兼容调用顺序。

因此撤回前一版 F8 设计中“业务长期双接口”“新控件自行回退旧主题键”“公开兼容主题选择”“默认保留全部旧插件二进制接口”的安排。内部维护的插件可以协调迁移并重编；已有设置、存档、库存/交易托管记录、安装收据与仍受支持的外部服务器必须继续正确处理。

本审查基于客户端 `e11ca12570b85a431955f4224969cf5530fb3964`、其固定 Common `6c2ece225bba2616d2a68d3b093cc611cb9733b5`、本地 Common/Server/Index 与 Example/红包/人才贸易源码。没有把包中 `Legacy` 名称、旧配置键或版本数字本身判作泄漏；以“业务是否被迫解释或执行旧契约”为判断依据。远端制品和游戏行为未在本轮验证。

后续同日用户要求评估两个维护插件并清理可直接删除的旧入口。已将红包/人才贸易改为 Compose、删除两者旧 Register 实现并通过编译/候选包静态验证；完整逐项使用清单、剩余边界与验证记录见 [维护插件旧接口评估](Maintained-Plugins-Old-Api-Assessment.md)。本文件 B09 表中“仍使用 Register”是原审查基线，当前源码入口已迁移；这不代表完整 DI/生命周期或主仓旧基类删除完成。

## 2. 统一边界

| 层 | 可知道的内容 | 禁止向新业务传播的内容 |
| --- | --- | --- |
| 旧接口 adapter | 旧方法/事件/DTO、默认值、调用顺序和旧返回含义 | 让新业务接收旧类型、选择旧入口、补执行旧生命周期 |
| 数据读取/迁移 adapter | schema、旧字段/键、资源格式、旧收据与账本版本 | 在 UI/领域算法中解析旧字段或切换历史数据分支 |
| 外部系统 adapter | 协议模式、包形状、版本协商、发送关联和已支持外部能力 | `IsLegacy`、`CompatibilityMode`、协议包及按协议判断成功的行为 |
| 组合与安装基础设施 | provider 身份、选择/注入、包类型、运行时/CLR 版本、迁移状态 | 把基础设施检查转交给业务构造器或普通 UI 控件 |
| 新业务/API/UI | 当前领域 DTO、统一操作/事件、能力可用性、请求结果、有效样式 | 旧接口引用、历史 schema、旧键别名、旧协议名、兼容适配开关 |

老调用方 → 老接口 adapter → 新应用契约 → 新业务主体；历史文件 → 读取/迁移 adapter → 当前模型；新业务 → 统一外部端口 → 已选择的具体协议/平台 adapter。新核心不持有自己的旧接口 adapter，也不反向调用旧实现。多个 adapter 可共用同一个核心状态所有者，不能各自建立交易/库存/主题状态。

能力状态是现实功能语义：能否发送、能否请求历史、是否支持某操作、失败/结果未知。不能把 `IsLegacy` 改名 `SupportsModern` 后继续让业务做兼容分流。详细协议诊断只供 adapter/基础设施日志和管理诊断界面，普通业务结果使用中立原因码。

adapter 只能转换已验证含义，不能凭空创造发送确认、delivery custody、默认 codec 或交易完成；无法无损翻译时明确拒绝或保持结果未知。为旧客户端/已不存在的作者 API 建立长期 adapter 没有必要。

## 3. 已发现的接口与语义泄漏

表中“P0”表示新业务 API 发布前应隔离的边界；“P1”表示随后清理的重复或历史机制，不表示已复现游戏故障。

| 编号 | 位置与证据 | 判断 | adapter/删除方向 |
| --- | --- | --- | --- |
| B01 / P0 | `ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs`：`IFrameworkClientLifecycle` 暴露模式和模式变更；Common `FrameworkTypes.cs` 的 `ClientFrameworkContext` / `ItemCodecContext` 带 `CompatibilityMode`，结果枚举有 `LegacyFallback` | 通用接口让业务可直接依赖协议兼容模式，Chat/Trade 已实际消费 | 新业务使用中立会话/能力/操作端口；这些旧上下文限于基础设施和传输 adapter。业务消费者迁移后删除业务侧旧依赖；共享运行时类型的删除须核对服务端/注册表消费者 |
| B02 / P0 | `Extensions/Chat/Client/ChatUiHostContext.cs:266`：检查远端 Framework capability，管线失败后落入直接 `SendFrameworkPacket`；构造 V2 上下文 | UI 上下文承担协议选择和补发策略；回复/提及能力未进入统一发送契约 | 统一 Chat 发送意图，包含正文/提及/回复；旧 `IChatUiHostContext` 调用必要时由短期 facade adapter 翻译。迁移 Chat UI 后删此分流/直发代码，不能仅更名类 |
| B03 / P0 | `ChatMessageHandler.cs:25`、`ChatCommandHandler.cs:39`；`BuiltInChatClientExtension.cs:236` 等：模式判断、`LegacyFallback`、按模式请求历史和发 Legacy 信件 | Handler/插件组合与业务提示知道兼容模式 | 传输 driver 选择及历史可用性由外部 adapter/组合层处理；业务订阅统一连接/消息事件。协议诊断从普通 Chat UI 移至基础设施诊断；移除业务模式事件订阅 |
| B04 / P0 | `FrameworkClientTradeServiceAdapter.cs:145`：Legacy 路径内嵌物品，其他路径 item refs；无 item handler 时直发；上下文携带模式 | 已有名为 Adapter 的类仍同时做业务操作和两种协议打包；边界未封闭 | 提取统一报价操作，主体只产生一次领域请求；具体 driver 决定旧包内嵌/Framework 引用和发送。旧 `IClientTradeService` facade 只转发统一操作，迁移内置调用后删旧 facade 与模式分支 |
| B05 / P0 | `PhinixFrameworkTradeClientService.cs:502/549/564/591` 含 `ApplyLegacyTradeSnapshot/RemoveLegacyTrade/CompleteLegacyTrade*`；`FrameworkLegacyTradeClientAdapter.cs` 直接转发这些方法；`ClientTradeContracts.cs:54` 等公开 Legacy 接口 | 外围 adapter 已存在，但内部核心仍保留来源命名与重复事件路径；仅有 adapter 壳不能隔离语义 | 建统一快照/报价结果/完成事件入口，区分状态通知与真实关联回执；旧方法短期适配到同一入口，再删旧主体方法与 Legacy 契约。支持原版服务端的 protocol adapter 继续转换数据 |
| B06 / P0 | `TradeClientItemPipeline.cs:23/31/48` 接收/设置模式并注册 `DefaultLegacyTradeItemCodec`；`FrameworkTradeItemPayloadCodec.cs:43` 解析 `LegacyProtoThingPayload`；被 Trade 主体和 `TradeInventoryIntegration.cs:40` 调用 | 旧格式解析进入物品领域/库存协作路径；同一旧 JSON 表示有重复解析实现 | 按载荷版本/codec ID 选择读取 adapter，产出当前物品模型；当前业务 codec 不按服务器模式变化。保留确需读取的旧 codec ID，移走旧 JSON 模型/解析并删重复分支；不可解码内容保持 opaque，不丢物品 |
| B07 / P0 | `BuiltInTradeClientExtension.cs:125/126/375`、`TradeClientActivationLifetime.cs`：模式订阅、修改 codec context、按模式请求快照/确认完成 | Trade 组合与生命周期把协议模式继续传入业务 | 组合根选择统一 Trade port；协议会话初始化与 completion ack 归对应 driver。Trade 仅调用统一操作与观察结果，移除其模式 handler |
| B08 / P1 | `ChatSettingsPanelProvider.cs:115` / `TradeSettingsPanelProvider.cs:62` 实现 `IClientLegacySettingsMigrator`；`Settings.cs:331` 调用迁移；Server Chat/Trade config 也实现旧配置迁移接口 | 迁移边界存在，但与运行中设置 UI/config 对象混在一起，旧契约占据常规插件能力面 | 各插件独立迁移 adapter 持有旧键映射，由启动迁移协调器调用；UI 只消费当前设置。删除 panel/config 对旧迁移接口的实现；历史读取按数据支持期限保留 |
| B09 / P0 | `IClientComposition.cs:31` 的 `LegacyClientExtensionModule`；`ClientExtensionRuntime.cs:51` 仅警告旧 Register；红包/人才贸易仍实现 `IPhinixExtensionModule.Register`，Example 工作区已 Compose | 维护中老接口使用者真实存在，但不需要对未知第三方维持双入口 | 红包/人才贸易协调迁移 Compose。确需过渡时客户端 discovery adapter 将旧模块转换为统一生命周期，业务不感知旧入口；迁移后删除客户端旧作者入口与警告路径。Common/Server 的 Register 是共享基础设施契约，不能因客户端迁移直接删除 |
| B10 / P0 | `InventoryContracts.cs:312/330` 同时有旧 void Deliver 与 verified delivery；`BuiltInInventoryClientExtension.cs:622` 在取出/交付主体里 `is IInventoryVerifiedDeliveryCodec`，否则执行旧 Deliver 再验 custody | 旧接口版本分支实际泄漏到库存交付算法；但现有 custody 验证应保留 | 注册入口将旧 codec 包装成当前统一 verified delivery adapter，主体只调用统一契约。旧 void 返回仍需检查 custody，异常归 Unknown/拒绝规则；维护中 codec 迁移后删分支/旧接口，不删 custody 检查 |
| B11 / P0 | 前版 F8 §4/6/8 曾要求兼容选择、普通控件回退旧键、新旧绘制并存及长期保留所有公开接口 | 设计层已把兼容语义扩散到新 UI 与新业务 | 撤回；旧 XML/键映射和首次配置迁移在主题输入 adapter 完成，输出当前主题模型/普通配置。新控件只读规范语义键，维护中 UI 改为单一公共入口；删除旧绘制 wrapper 和重复消费 |
| B12 / P1 | Store 工程仍编译 `PayloadValidator`、`RepositoryPayloadTransport`、`DependencyPlanner`、`CatalogReader` 等；遗留 `ManagedInstallation/StoreBrowserController/PluginStoreView` 文件还存在，其中部分不在当前 Compile 列表；现行 managed 路径使用 `CatalogReader` 的基础校验/限额 | 取消方向的代码残留和工具复用混杂，不能直接按“Legacy/没编译”一键删除 | 提取仍被 managed/Index Validator 消费的中立 JSON/哈希/限额工具；删除确认无调用的整 Mod 下载/安装/UI/旧 schema 路线。收据与 journal 读取另保留 migration adapter，删除前核对历史受管文件记录 |
| B13 / P1 | Common `PhinixExtensionRegistry.cs` 自动发现 legacy component，并包装旧 Server default handler；Common/Server 还有 config migrator / Server 内部 `LegacyServerTradeRuntimeAdapter` | 大部分位于发现/基础设施边界，不能仅凭 legacy 名称判定业务泄漏；有延续旧注册策略的成本 | 盘点在用服务端模块/测试后评估去掉 component auto-discovery；实际需要的 server handler 适配保持在注册层。客户端阶段不擅自删除服务端仍用的基础设施 |

证据均为现有调用或设计规则，不代表已经完成整治。B05 的无 token 状态更新不能直接当成真实报价确认；本轮不因边界命名判定其已有 token 关联保护无效。B06 的 UnknownItem 预览是否影响物品交付需逐调用审查，不能在此直接声称发生数据丢失。

## 4. 未判作业务泄漏的必要兼容边界

- Store/包校验器检查 `phinixRange`、`abstractionsRange`、游戏版本和 CLR 引用，是安装领域的合法准入规则；这些检查留在安装基础设施，不向 Chat/Trade 传“版本兼容选择”。
- Legacy protocol adapter 的 wire 包、协商、原始模块 handler、`InnerProtoThing` 哨兵与 token 映射属于对仍受支持外部服务器的转换。删除老作者接口不等于取消原版服务端支持。
- Settings、收据、库存/存档日志 schema 的有界读取与恢复是数据边界；统一客户端升级仍需正确读取玩家现有数据。读旧 schema 与保留旧业务实现分别评估。
- 红包/人才贸易的 `Legacy` 命名、稳定 ID、配置键和持久类名不构成语义泄漏，不为清理名字而造成存档迁移。
- 人才贸易的 Def/种族/DLC 转移可用性是业务真实性校验，不因带 `Compatibility` 字样删除。游戏集成依赖和 Harmony 行为保持在对应游戏 adapter/集成层。
- GitHub/Cloudflare 等来源 adapter、版本反序列化器和启动组合可以知道真实外部类型。普通业务只使用统一端口；禁止边界检查变成业务算法的兼容分支。

## 5. adapter 引入与代码删除原则

每个 adapter 必须记录旧契约、新契约、实际旧消费者、所有者、方向、状态寿命和删除条件。优先直接迁移能同批修改的调用方；没有消费者的旧接口直接列入删除，不为其新增 adapter。不要形成多个版本 adapter 串联或万能兼容服务。

| 类别 | 首选处理 | 删除门槛 |
| --- | --- | --- |
| 无第三方且维护中调用方可同批迁移的 UI/作者 API | 迁移到单一当前接口，旧入口标记弃用仅作为交付步骤 | 消费方源码、样例、测试、包/CI 固定宿主与发行批次全部更新，可直接移除旧签名和实现 |
| 暂时不能同批发布的维护插件 | 短期老接口 adapter 转发同一新核心 | 明确维护插件版本与迁移完成时间；无旧包启动要求后删除，不无限保留 |
| 旧主题/设置/存档/包记录 | 读取/迁移 adapter 输出当前模型 | 只有明确停止读取该历史数据后才删除；迁移不能造成奖励复制、预留丢失或 recovery 失效 |
| 仍在支持的原版/Framework wire 系统 | 持续的具体外部 adapter，核心只有统一端口 | 用户明确取消外部系统支持且未决状态有处理办法；当前阶段不满足 |
| 已取消的整 Mod 安装及无引用 wrapper | 提取被复用的中立工具，再删除旧流程 | 无生产/工具/测试/校验器引用，不存在需继续恢复的历史安装事务；存在记录则单独保留读取/隔离能力 |
| 注册表/server 共用接口 | 先核对 Shared 两端消费者 | Client/Server/Common 固定依赖与实际使用者全部迁移，不能按客户端 alone 的“无第三方”直接推断 |

本轮用户授权调整 F8 的接口兼容目标；原先“宿主 1.0/抽象 2.0 才移除”的计划不再是必须长期等待的理由。准确破坏性发行版本在迁移清单冻结后确定，并同步维护插件/CI。必要的包 ID、schema 与用户状态保护仍有效。

旧 UI 不适合自动翻译任意 `Draw(Rect)` / `Widgets` 绘制成新控件；若没有可无损适配的高层语义，直接重构维护中 UI。过渡 wrapper 不允许新业务调用；F8 验收不允许仍有新旧 UI 两套通路。机械的 Compose→注册表桥梁可以留在基础设施，因为它不让业务调用两个作者入口；不要为追求名字清理破坏 Shared/Server。

## 6. 整治顺序与边界门禁

1. 固定当前业务操作/事件/结果和样式模型，记录所有维护中的旧接口消费者；类型不仅检查方法参数，还检查 DTO、事件、构造注入和程序集引用。
2. 主题输入/键映射、Inventory 旧 codec 和设置迁移先收口到边界，新增 UI/业务均按当前接口开发；撤回新 UI 的混用与兼容选择设计。
3. Chat 统一带元数据发送入口，移除 UI 直发/补发；Trade 统一报价与入站事件，迁移 item codec 与协议 driver，保留确认/custody/恢复验证。
4. 维护插件迁移 Compose 与公开业务/UI API；移除客户端旧作者入口、薄 wrapper 和无调用接口。同步客户端、工具、独立插件和 Index 的固定输入。
5. 清理整 Mod Store 残留、重复 JSON 解码和旧 component discovery；涉及 Common/Server 的改动单独携带两端回归，不纳入纯主题操作。

边界门禁不只搜索 `Legacy`：新业务禁止依赖旧接口程序集/命名空间、`CompatibilityMode`、旧包/旧 DTO、版本枚举、schema 判断和原始传输 API。限定扫描范围与允许边界，避免把存档键、codec ID、基础设施诊断误报成业务泄漏。禁止通过中立命名的方法/属性隐藏同样的分支。

应设置依赖/源码架构检查：新业务与当前 UI → 当前契约；adapter → 旧契约和当前契约；当前契约不引用旧契约；旧入口没有新调用者；旧、新前端均不得分别拥有业务状态。生产与打包工具/Validator 的引用图一并核对。

| 验收 | 通过标准 |
| --- | --- |
| 新业务零兼容感知 | Chat/Trade/Inventory/主题/新 UI 应用层中没有旧接口调用、模式/schema/版本分支，详细兼容诊断限定边界 |
| adapter 等价 | 相同已验证输入得到相同领域结果，重复/过期事件、拒绝、结果未知和部分失败保持不变量 |
| 单一 UI/API | 维护中的模块与示例只消费当前契约，无两套 API/绘制实现，删除旧入口后仍可独立编译打包 |
| 数据与外部系统 | 旧主题/设置/账本/收据可读取或准确拒绝；原版/Framework 路径保留真实确认与恢复能力 |
| 生命周期 | adapter 注册/撤销对称，旧调用方和新核心不重复订阅/发送/持有状态，失败不会回退再发一次 |
| 删除完整性 | 无 Compile、反射发现、工具快照、样例/文档或打包脚本引用，无待恢复记录被误删 |

本文件原审查环节仅执行源码搜索与阅读、文档编辑和格式检查。同日后续两个维护插件的入口迁移已编译/候选预检，证据见维护插件记录；其余 adapter 实现、完整引用图、已发行维护插件和 RimWorld 实机验证仍属于后续整治验收，不能把本审查当作已完成边界隔离。

## English summary

No third-party plugins currently require indefinite old API support. New business and UI use one current contract and never interpret old interfaces, schemas or protocol modes. Adapters cover all historical interfaces, not only the LegacyAdapter plugin: caller facades, data readers/migrations, codec adapters, discovery adapters and external protocol drivers. Add adapters only for real remaining consumers, migrate maintained consumers, then remove obsolete interfaces and duplicate code.

Confirmed leaks include Chat UI protocol routing/direct fallback, Trade offer routing and legacy item decoding, public mode-bearing contexts, inventory delivery interface-version branching and settings UI migration responsibilities. Existing adapter class names alone do not prove isolation. Preserve authority/custody/recovery and supported external servers; package compatibility validation and historical data readers remain valid boundary responsibilities. This is a source audit and removal plan, not completed runtime remediation.


## 首批实施进展（2026-10-10）

主题当前契约已替换并删除 `IUiTheme`，旧主题键和 XML 格式仅由 `ThemeInputAdapter` 读取。Chat、Trade 设置面板已删除 `IClientLegacySettingsMigrator` 实现；注册独立 `ChatSettingsInputAdapter`、`TradeSettingsInputAdapter`，继续读取原有持久化键。协议模式、交易桥梁、库存旧交付分支和旧作者入口等其余条目尚未完成，不能据此宣称 F8-B 已通过。完整记录见 [实施进展](F8-Implementation-Progress.md)。


第二批已实现 Chat 当前意图入口、Trade 当前状态输入、库存单一显式交付契约及客户端 Compose 独占发现。对应旧接口与转发类已删除；抽象源码版本 2.0.0。其余模式/物品 JSON/人才贸易边界仍未完成，详见实施记录。
