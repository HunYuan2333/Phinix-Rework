# 红包与人才贸易：旧接口使用评估与清理记录

日期：2026-10-10。范围：两个维护中的独立插件、其实际宿主契约消费者及 F8 删除条件。

## 1. 决定

没有第三方插件时，能协调修改的两个插件直接迁移到当前接口，不为旧作者入口或旧 UI 构造长期兼容 adapter。需要保存的历史数据和真实外部系统转换仍是有效边界职责；`Legacy` 包名/程序集名/存档键本身不是旧接口债务，不据此改名。

两个插件大多数宿主依赖已经是通用 `IClientSessionContext`、`IClientUserDirectory`、`IClientUserEventStream`、`IClientSettingsContext`、dispatcher 与 localizer。并非所有 `IClient*` 或当前 provider 接口都需要更换。明确过时的入口、宽接口依赖、跨插件实现依赖、生命周期缺口分别处理，不能用一个“Legacy 插件”标签代替引用分析。

## 2. 本轮已清理与验证

| 改动 | 红包 | 人才贸易 |
| --- | --- | --- |
| 作者入口 | 继承 `ClientExtensionModule`，覆盖 `Compose`，移除自身 `Register` 实现 | 相同迁移，无旧作者入口 adapter |
| 无效成员 | 删除只赋值、没有读取的 `ExtensionHostContext` 字段及赋值 | 未发现可凭该项直接删除的同类字段 |
| 打包要求 | `pack.py` 显式 `>=1.9.0 <2.0.0` abstractions 范围 | 相同；避免默认 1.7 范围与 Compose 实际要求不一致 |
| 制品验证 | Release 编译 0 warning/0 error；ZIP 静态验证通过 | 相同；Harmony 引用仅参与编译/预检 |

此次是作者入口迁移，不声称已完成两个插件全部 DI、生命周期或 F8 UI/API 改造。Shared 注册表所需的 `ClientExtensionModule` 内部显式 Register→Compose 桥梁属于中立基础设施，不是插件继续公开两套入口。

只生成本地候选 `/tmp/phinix-f8-redpacket-compose.zip` 与 `/tmp/phinix-f8-talent-compose.zip`。未修改发行版本、覆盖既有 Release、更新 Index 或推送。包内只含两个插件程序集、清单与翻译资源；manifest 声明 1.9 抽象下限和原模块 ID。

验证使用本地已构建宿主与 RimWorld 1.6 编译引用。首次预检发现 slim 游戏引用目录缺少 framework 身份；在临时目录补足实际使用的 managed framework 引用后成功，未改变验证器或放宽检查。红包 CI 固定宿主 `403cea6...` 已核对具备 Compose 与 `--abstractions-range`，无需为此变更浮动更新宿主。人才贸易当前源码没有完整的 `ci/build.py`，其云端发布状态不能由本地成功推断。

未执行 RimWorld 游戏验收。后续验证两个模块发现/激活、禁用、切档、主菜单退出、公告与贸易/红包状态；候选预检不证明这些场景已通过。

## 3. 红包实际使用清单

| 接口/路径 | 实际用途与判断 | 处理与删除条件 |
| --- | --- | --- |
| 旧 `IPhinixExtensionModule.Register` 作者入口 | 入口旧用法真实存在 | 本轮直接删除插件自身旧方法，改 Compose；不引入 adapter |
| Session / UserDirectory / Settings / UserEventStream / Dispatcher / Localization | 玩家身份、在线/用户事件、设置与 UI 回调；没有原版/Framework 模式判断 | 当前通用契约继续消费；以后替换 dispatcher 返回结果时同批改调用，不把这些正常依赖一并判为旧债 |
| `IInventoryDepositApi` / `RegistrationApi` / `ReadApi` / `ReservationApi` | 已使用分离 API，存入/预留/来源显示与实际提交确认 | 保留领域职责；维护插件与库存统一新版契约时直接迁移，不重新引入旧聚合 `IInventoryApi` |
| `IFrameworkClientTransport` | `RedPacketTab.TryBroadcastChatAnnouncement` 唯一具体用途是 `TryHandleOutgoingMessage(text)`，不是闲置字段 | 需要公告发送能力，不需要整个 packet/capability transport；待统一 `IChatSendApi` 或中立消息发送端口可用后直接替换，删除 Tab 的宽 transport 注入。不能现在删字段导致公告失效 |
| Trade 契约/客户端程序集依赖 | `TradeItemSnapshot`、`TradeItemConverter`、`StatefulTradeItemProtocol` 被领取、归还、库存 codec、UI 发包实际使用 | 不是无引用程序集。F8 先交付独立当前物品表示/物化/验证端口，红包直接迁移后删对 Trade.Client 的实现依赖；仅为保留旧签名再加 facade 无收益 |
| `trade.allItemsTradable` / `trade.dropCurrentMap` 设置读取 | 红包 UI/归还读取另一个插件的私有设置键 | 真实横向策略耦合。统一物品选择/交付策略端口，或定义红包自有当前设置；确定行为所有者后删除跨插件键读取。旧值迁移只在设置输入层 |
| `RedPacketInventoryCodec : IInventoryCodec, IInventoryVerifiedDeliveryCodec` | 当前 `Deliver` 只转调用 `DeliverVerified`；宿主现行接口仍要求旧 void 方法 | 两端协调统一 verified 契约后直接删 void method/旧接口及库存算法中的类型分流。现有 custody 校验保留，不为只有维护 codec 的场景建立长期 adapter |
| `RedPacketDropHelper` | 状态机领取/归还及 UI 失败恢复实际调用，直接使用游戏空投 API | 不是闲置 helper。统一 delivery 端口并覆盖确认/拒绝/Unknown 后删重复掉落实现；先核对这几条现行路径的 custody 与恢复含义 |
| `IMainTabProvider` / settings provider / responsive helpers | UI 用宿主几何工具，实际绘制自己完成 | F8 公共控件上线时直接迁移；完成态一套 UI，旧 wrapper 无消费者后删除，不增加 UI 模式开关 |

红包没有实际调用 `Client.Instance`，也没有判断 `CompatibilityMode`；相关旧名字主要是身份和历史注释。不要误删已运行的 HTTP relay 或完整状态物品 codec。

## 4. 人才贸易实际使用清单

| 接口/路径 | 实际用途与判断 | 处理与删除条件 |
| --- | --- | --- |
| 旧 `IPhinixExtensionModule.Register` 作者入口 | 注册 Tab/设置，原入口与红包相同 | 本轮改 Compose 并删除自身旧 Register；不保留旧入口 adapter |
| 通用 Session / Users / Dispatcher / Settings / UserEventStream / Localization | 通过 `LegacyTalentTradeRuntime` 供自有业务与 UI 使用 | 正常契约使用，不是宿主 Trade 兼容 API。之后改作用域注入以收紧静态持有寿命，不能只改 `Legacy` 类名宣称完成 |
| `LegacyTalentTradeRuntime` 静态服务集合与 `TalentTradeTab.Instance` | 所有业务/界面借用同一静态上下文；并未直接调用宿主 `Client.Instance` | 在作用域组合与完整清理方案就绪后直接重构消费者、删除静态 service locator。不是旧 API 二进制兼容理由，无需再添加旧 runtime adapter |
| `TalentTradeTransport` / 自有协议 Contracts | 独立 HTTP 中继与人才交易/租赁协议，没有宿主 Framework/Legacy 模式分支 | 保留具体 transport 作为真实外部边界；自有 manager/UI 不直接解析版本化 wire DTO。老 peer 的兼容策略可取消，仍存储/在途的数据须先归一化 |
| `TradeOfferSerializer` | 有历史报价缺少 manifest 尾段的读取，当前输出 manifest 扩展；`DirectTrade` 注释明确旧报价 metadata 可缺失 | 老 peer 支持不作为长期需求。先确认在途/持久报价来源，再将历史格式读取独立 adapter 化；当前模型完整定义缺失 metadata 的领域拒绝/Unknown 行为后删业务层兼容分支，不能直接删读路径丢报价 |
| `TransferCompatibilityUi` | 确认 UI 自行解压/解析 manifest、检查 Def，并按 metadata 缺失决定提示 | 确认 UI 知道传输格式，是新的边界泄漏发现。Def 校验 adapter/应用服务输出规范 `TransferAssessment`（可接收、缺失资源、未知）及确认意图，UI 只展示；随后删除 UI 的解析/旧格式判断。缺种族/DLC 是真实业务限制，不删除检查 |
| `PawnSerializer` / `PawnDeserializer` / `TalentTradeGameComponent` / 待归还记录 | 游戏 Pawn 状态、租赁/挂牌/归还持久化；还有切档和空地图等真实恢复路径 | 游戏 Scribe 集成留在对应 adapter，业务用稳定记录。改 API 不删活跃交易/租赁记录，不重建丢状态 Pawn；旧数据 reader 必须在安全迁移前保留 |
| 自有 `TalentTradeUi` / `TalentForm` / `TalentToolbar` / GUIUtils 调用 | 局部包装使用宿主响应式几何，绘制多数直接调用 Verse | F8 直接迁移公共 UI；自有业务展示仍保留，删除重叠的通用 wrapper 与旧 static 文本格式帮助调用 |
| Harmony / Timer | 挂补丁、驱动更新，现行回调通过 dispatcher | 是运行生命周期成本。整治部分激活清理、已排队回调代次和卸载挂补丁状态；并非可因“无第三方”直接删除的旧接口 |

人才贸易没有引用宿主 `IFrameworkTradeClientApi`、LegacyTrade 注入接口、`ILegacyModuleTransport` 或 `IInventoryApi`；也没有使用 Trade.Client 物品转换类型。不能把宿主 Trade 的接口债务误记到人才贸易头上。

## 5. 主仓旧接口的最终删除安排

两者转为 Compose 后，独立维护插件的旧作者入口消费者已减少，但不自动证明主仓 `LegacyClientExtensionModule` 可以立即删除：`ClientCompositionRuntimeTests` 仍有明确旧 adapter fixture，Common metadata 检查器仍识别该基类；客户端直接 Register 检测、共享注册表/server 消费也须区分。

F8-B 删除旧客户端作者入口时同步删除其支持专用 fixture/弃用警告/metadata 识别，补当前入口的拒绝与生命周期验证；Common `IPhinixExtensionModule.Register` 暂是 Server 与中立注册表当前底层接口，不能按“没有第三方”一起删除。删除全链路支持是一项明确整治切片，不以新增适配壳拖延。

统一库存 codec 后，维护中的 Trade 与 RedPacket codec 同批迁移，直接移除旧 void Deliver 和 `is verifiedCodec` 分支；必要的历史库存数据读取按 codec ID/version 边界继续处理。要删除的是旧调用协议，不是用户库存。

优先直接迁移已有可控调用方。只有短期无法同批发行的旧包有真实需求才建立有期限 adapter；不为未来可能出现的第三方预留多代 API 或额外注册路径。

## 6. 本轮边界与下一切片

本轮已改两个入口、移除无用字段、收紧打包要求并验证真实候选。仍待 F8 当前端口交付的红包公告/物品转换、人才贸易传输检查 UI、完整 DI/回调生命周期以及旧宿主入口删除不计作已解决。

下一切片应先冻结统一物品/消息/交付端口与当前 UI 契约，再直接迁移两个消费者；然后删除旧实现/签名、更新打包/CI 固定输入并游戏验收。不能先删正常功能再用临时 stub 或静默 fallback 弥补。

## English summary

Both maintained plugins now use ClientExtensionModule.Compose; their own Register implementations are removed. An unused RedPacket host-context field is deleted, and package manifests explicitly require abstractions 1.9. Both plugins compile and produce statically validated local candidate ZIPs; no release, Index update or game acceptance is claimed.

Most host dependencies are current generic services. RedPacket still needs announcement sending, inventory operations and real Trade item conversion; migrate these to current narrow ports before deleting obsolete coupling. TalentTrade uses its own relay, Pawn persistence and generic host services, not the host's legacy Trade APIs. Its confirmation UI currently interprets transport manifests and should consume normalized transfer assessments. No third-party callers justify indefinite interface adapters; maintained consumers migrate directly. Persisted data and actual external/game boundaries remain protected.
