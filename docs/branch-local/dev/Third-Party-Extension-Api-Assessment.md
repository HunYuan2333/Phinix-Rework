# 第三方扩展 API 盘点与接续

日期：2026-10-11。用户要求同时审视插件和 Host 接口，特别是右键注入。右键只是一个例子；后续全范围与优先级以 [插件 SDK 全面评估](Plugin-Sdk-Comprehensive-Assessment.md) 为准。本文保留菜单细节。本文基于当前源码；表中“建议”不是已经发布的接口。UI/UX 重构不由本任务负责，在线发行延期，本地 API 实现仍可接续。

## 1. 职责与兼容边界

Host 负责生命周期、通用 UI 外壳、主线程、资源、会话和用户身份；插件拥有自己的业务模型、命令与扩展点。Chat/Trade/Inventory/Store 与第三方具有同等注册地位。Host 不引用插件 Contracts，不增加红包/人才贸易专用服务。

普通插件消费当前契约，不能知道历史客户端版本、wire/schema、兼容模式或旧 DTO。维护插件支持旧 1.9 的接口绑定只留在内部 adapter；主题文件历史输入只留 ThemeInputAdapter。已经 resolve 到 API 不代表 provider 已激活。借用服务不由消费者释放，注册/订阅/菜单回调必须随所属模块停止而失效。

## 2. 当前能力与缺口

| 能力 | 源码证据 / 当前状态 | 接续判断 |
| --- | --- | --- |
| Tab、侧栏、横幅、徽标、接受键 | ClientExtensionAbstractions/UI 的 IMainTabProvider、IServerSidebarProvider、INoticeBannerProvider、IBadgeProvider、IUiAcceptKeyHandler；有响应式几何 | 已有，补真实作者例子；不再创建另一套导航注册 |
| 主题与公共控件 | IClientTheme、Registry、Catalog、WindowService、ClientWidgets；T1–T4 已有消费者 | 已有；继续真实消费和生命周期核对 |
| 设置与快捷设置 | IClientSettingsContext、IClientSettingsPanelProvider、IClientQuickSettingsPanelProvider | 已有；缺少模块命名空间/类型验证的帮助器，不重做存储 |
| 会话与用户 | IClientSessionContext、IClientUserDirectory、IClientUserEventStream | 已有；增加操作执行前身份/会话失效约束，不长期保留世界实体 |
| 窗口、链接、声音、翻译 | IClientWindowService、IClientLinkService、IClientSoundService、IClientLocalizationService | 已有；统一链接实际消费，ChatMessageList 仍直接 Application.OpenURL，是待迁移点 |
| 右键用户/消息菜单 | UserList.drawContextMenu、ChatMessageList.drawNameContextMenu/drawMessageContextMenu 私有构造 FloatMenu | **缺公开注入点，P0**；第三方当前只能补丁或替换 UI，不能据已有菜单称有扩展 API |
| 交易物品菜单 | TradeWindow.DrawAvailableItemContextMenu/DrawItemContextMenu 私有构造 | **缺公开注入点，P1**；保留原选择操作，以只读上下文追加插件操作 |
| 库存条目操作 | 有独立 read/deposit/extraction/reservation API | 菜单注入 P1；通过库存操作 API 执行，不向作者公开 ledger 内部写入口 |
| 包资源读取 | 有托管资源声明、Localization 验证及存储基础 | **缺通用模块绑定资源读服务，P0**；不公开其他包目录/任意可写路径 |
| API 能力状态 | 模块管理状态、服务解析与 ICapabilityProvider.GetCapabilities 存在，后者只声明能力名称，缺统一作者激活/失败状态查询 | **P0**；区分未安装、声明、激活、失败、不可用，返回当前语义 |
| 主线程任务 | IClientMainThreadDispatcher.Enqueue 是 void；有 PendingCount/DroppedCount | **P1**；作者无法直接观察拒绝/取消/失效，新增有结果的提交契约 |
| 通知、确认、文件交互 | 有显示消息、窗口、商店本地 ZIP 选择 | P1；可复用有限结果/取消模型，三端原生文件选择器不作为前置 |
| Chat 操作/读取 | IChatMessageSender 已接收普通 ChatMessageIntent；IFrameworkChatClientApi 仍公开 Packet/ClientFrameworkContext | 发送优先复用；补只读消息与回复/提及入口，协议构包接口限制在 adapter/处理器 |
| Trade 操作/读取 | facade、快照和更新结果接口已有；IFrameworkTradeClientApi 同时暴露构包/Handle* | 补高层操作结果与可用状态；将 adapter 入站和普通作者出站分开，不制造本地预测确认 |
| Inventory 操作/读取 | Read/Deposit/Extraction/Reservation/Registration 已分开；部分模型可变，仍有聚合 IInventoryApi | 补不可变读取快照与结果约束，迁移聚合消费者后删除；不得跳过 custody 或自动返还未知结果 |
| Store 管理 | 扩展注册 Tab/settings/banner；ManagedStoreController 是内部实现 | 需要独立 Store Contracts：只读包状态、用户确认后的管理意图，不公开 receipt/journal 写权限 |
| 原始通信 / 兼容模式 | ILegacyModuleTransport、IFrameworkClientLifecycle 与 FrameworkPacket 相关接口仍公开 | 不是普通作者便利 API；限定 adapter 消费并审计调用后删除可移除部分，不能用它们实现菜单业务 |
| 原版游戏右键 | 当前框架没有完整的原版地图/人类命令菜单扩展契约 | 与 Phinix 面板菜单分开评估。先确定真实场景，再考虑窄 Harmony 适配器；不抢占全局 FloatMenu、不承诺所有 Mod 的菜单 |

以上属于当前检查范围，不把所有 RimWorld API 包装进 Host。网络 handler、存储与 DI 的现有基础继续复用。

## 3. 右键扩展设计

### 3.1 所属与类型

- Host 通用层定义菜单操作描述、执行/异常隔离与呈现机制；不能定义 ChatMessage/TradeItem 等业务类型。
- Chat Contracts 定义用户/消息上下文及 provider 接口；Chat 在用户名、用户列表和消息正文三个入口采集 provider。Trade/Inventory 后续定义各自类型明确的上下文，不用 object payload 或任意字符串 mount point 表替代业务契约。
- 注册复用模块 RegisterApi/provider 发现，不创建第二个插件注册中心。当前 ResolveAll 不自行验证 Active，新增消费前须从同一模块状态事实建立激活过滤与失效检查。自定义短命菜单不是持久化模块注册，不提前暴露尚未激活 provider。
- 上下文只含必要的稳定 ID、显示快照和会话代次。消息全文按实际菜单用途有界提供，不给原始传输包、可写列表、Thing/Map 长期引用或 provider 内部服务。

### 3.2 必须保持的行为

1. 原有菜单项与原顺序保留；第三方只能追加自己的命名空间操作，不删改原有复制、回复、交易或数量选择动作。
2. 操作有稳定 ID、文本、排序与禁用原因；同模块重复 ID 拒绝，跨模块按模块 ID + 操作 ID 排序，数量与文本有界。provider 收集/枚举异常单独隔离，不吞业务失败，也不阻断其他菜单项。
3. 收集只计算可见性/可用性，不发消息、不发交易、不改库存。GUI 状态恢复，只有菜单打开时构建，不每帧反射或扫描模块。
4. 点击时重新核对模块激活、当前会话和目标是否仍存在；断线、切档、停用或用户/消息已删除时明确失效。已打开菜单不能保留一个可在 Shutdown 后调用的裸业务 delegate。
5. provider 和动作主线程调用；异常只影响该项，日志带模块/操作 ID，正文和凭据不写入诊断。动作发起仅代表派发，确认/结果未知仍由对应业务主体处理。
6. provider 借用 Chat/Trade/Inventory API，停止时不释放 provider 的服务。生命周期结束回收自己的订阅和缓存，不持有旧世界。
7. 普通作者示例必须展示有条件追加、禁用原因、会话失效和失败反馈；实际内置菜单通过同一机制消费，不能为示例新增 Host 特权。

原生 FloatMenu 只作为呈现 adapter。菜单描述与普通业务不返回/接收 FloatMenuOption，不要求第三方补丁 ChatMessageList 私有方法。原生项可以暂由业务原有路径构建，注册协议保持一套。

## 4. 实施顺序

用户进一步明确右键只是例子，范围扩大为完整作者流程。以 Plugin-Sdk-Comprehensive-Assessment.md 的 A0–A4 为准：先 owner/激活/失效/结果/事件/资源基础，再组合 UI，随后各插件读取/操作/贡献/订阅。菜单不再独占第一批；原版游戏挂点按真实需求再设计。T5 本地主题包事务继续独立接续，线上发布延期。

## 5. 本批限制

本批新增 API 盘点与设计，没有交付右键注入代码，也没有自动化或游戏测试；T5 首批代码见 Theme-Package-T5-Implementation.md。具体拟议接口名与签名在对应实现切片确定，不据本文声明 API 已冻结。
