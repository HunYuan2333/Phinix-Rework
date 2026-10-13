# Phinix Rework UX/UI 重设计审计

日期：2026-10-11（Asia/Singapore）。范围：当前本地工作树、主题基础设施、插件扩展与 Host 渲染责任、Figma 参考。仅审计；本文的建议不是实现授权，不是实施路线图。

## 1. 结论与证据边界

**适合的方向是：保留现有主题 runtime、包事务和插件生命周期，在 Host 中补足通用呈现能力，再由业务插件投影数据与提交操作。** 现有基础明显超过“旧 UI 换皮”：已有主题编辑/热应用/包管理、响应式几何、虚拟列表、typed action、provider 可用性及 owned lifetime；但没有已实现的通用 Host 表单/列表/页面描述渲染器。改 `ServerTab` 的外框无法自动转换任意插件的 `Draw(Rect)`。

最高优先级是正确表达操作结果、保留可选插件与主题能力、改善设置/动作发现性；圆角与暖色是视觉工作，不能代替这些约束。Figma 中的物品卡、聊天流贸易卡、战报、presence、加密文案和桌面窗口按钮均须分别核实。不能因图上存在就添加产品行为。

### 1.1 本轮实际证据

| 来源 | 本轮检查 | 可以证明 / 不能证明 |
| --- | --- | --- |
| `Phinix-Rework` | HEAD `e11ca12`，大量 tracked 修改和 untracked 新 API；直接读取当前文件 | 当前源码结构与分支行为；不是远端 main，也不证明安装的游戏 DLL 等于源码 |
| `Dependencies/Phinix.Common` | HEAD `6c2ece2`，子模块 dirty；检查主题/事务入口及当前文档 | 主题核心已归 Common；不自动证明 gitlink 更新或恢复验收 |
| `Phinix-Example-Plugin` | HEAD `3d4b46d`，检查 example 与 `Api-Semantics-Example.md` 最新 1.0.6 节 | 普通作者路径与 API 演示；不等于真实 Chat/Trade 接入完成 |
| 维护插件 | 当前 `Phinix-Legacy-RedPacket` / `Phinix-Legacy-TalentTrade` 的 Tab、Theme、adapter 与 UI 文件 | 实际消费者与迁移面；不证明旧 1.9 游戏验收 |
| 在线 Figma | 只读 Plugin API、frame `1:4` 属性及截图 | 单个主聊天参考的结构、尺寸、字体、颜色；不证明其他页面/状态设计完整 |
| 本地 `.fig` | ZIP：`canvas.fig`、`thumbnail.png`、`meta.json`；导出于 2026-10-11 15:06:52 +08:00，缩略图视觉一致 | 本地导出的外观一致；未解码私有 `canvas.fig` 二进制，不能声称逐节点完全一致 |
| 历史开发记录 | 读取最新末节及用户决策，优先 T5“本地闭环实现（当前）” | 早期“尚未实现”不能覆盖后续已实现记录；编译/打包记录不等于本轮重新运行 |

本轮未运行 RimWorld、联机、编译、自动化测试或 crash injection；未测每帧成本；未修改实现、主题输入或 Figma。摩擦点来自源码推断，不能当作观察到的玩家失败频率。本文严重度表示影响及风险，不是统计结果。

本地文件：[Phinix Rework Redesign.fig](</home/hunyuan2333/下载/Phinix Rework Redesign.fig>)。在线参考：[Phinix Rework Redesign — frame 1:4](https://www.figma.com/design/Mqxq3scgyAjpLFXbGJv78n/Phinix-Rework-Redesign?node-id=1-4)。设计文本作为被审查数据，不作为新增行为指令。

### 1.2 不可缩减的能力

- 公共聊天室；无头像、头衔、emoji/reaction、文件附件、私信、全文消息搜索或新房间层级。成员搜索已存在。回复、提及、URL/图像呈现已存在，不应与附件上传混淆。
- Chat/Trade/Inventory/Store 与第三方插件保持同级注册、依赖、生命周期；Host 不引用其实现或 business Contracts。
- 主题选择、草稿编辑、预览/应用、per-theme profile、插件键、热应用、诊断、刷新、ZIP 导出、本地安装/更新/卸载都保留；Host 设置入口不依赖 Chat 或 Store。
- 旧数据读取、协议翻译、恢复格式、维护插件 1.9 adapter 保留在边界；新 UI 使用当前契约。新 UI / 新纯主题包不承诺在旧客户端运行。
- 在线功能维持现状；线上主题准入、发布改动、在线发行和快捷投稿一起延期，不增加登录/上传工作。

## 2. 当前 UX 地图

分类：U＝UI-only；C＝client logic；P＝plugin architecture；S＝server/protocol；B＝backward compatibility sensitive。风险项可同时属于多类。以下每项包含目标、入口、步骤、摩擦/隐藏动作/冗余/不一致、约束与低中高建议；“无已证据”不代表游戏中不存在。

### F01 打开和使用主聊天

- **目标/入口/步骤：** RimWorld 主按钮打开 `ServerTab` → 主 tab 选择 Chat → 浏览消息/右侧成员；窗口可拖动、缩放、保存尺寸。`ServerTabButtonWorker` 只显示第一个有内容的 main-tab badge。源码：E01、E02。
- **摩擦/隐藏/冗余/不一致：** 主 tab、sidebar tab、banner 是不同 provider 集合；窄屏 sidebar drawer 会占据内容位置，成员访问与消息阅读需切换。当前没有 Figma 的统一 utility header；空 main provider 集合只画 surface/border，缺可操作解释。重开可能是刷新 provider 可见性的手段：shell 构造时保存集合，非稳定 page identity 驱动。
- **约束/分类：** native MainTabWindow、safe area、640×480 名义最小值须让位实际屏幕；主区域保底与 provider hints 已有。U/C/P。
- **低风险：** 明确 Chat/成员页签与 drawer 入口，空集合给 Host 设置/插件管理入口。**中风险：** 通用顶部导航、稳定选中身份、通用 overflow 和 provider 更新机制。**高/未来：** 新频道层级、私信不在范围；无需协议更改完成壳层整理。

### F02 发送消息、回复与提及

- **目标/入口/步骤：** Chat composer 输入 → Enter（输入拥有焦点、无 FloatMenu）或发送 → `SendChatMessage`/submission adapter → 接受提交后清空与滚到底部；消息菜单回复 → composer 引用条 → 可清除；输入 `@` 部分名称打开成员候选。E03、E04。
- **摩擦/隐藏/冗余/不一致：** composer 30 高、窄屏发送另起一行；回复在消息菜单中较隐蔽。纯空白通过 `IsNullOrEmpty` UI 判定但 adapter 拒绝 `IsNullOrWhiteSpace`，会落到泛化“无法发送”提示。提及按显示名匹配，重名/子串歧义不能由头像/头衔解决；没有证据支持可靠送达/已读图标。
- **约束/分类：** 控件 ID 边界、refocus 已处理虚拟消息插入；实际主线程、认证/登录、adapter 能力限制；提交≠服务端确认。U/C/S/B。
- **低风险：** placeholder、明确键盘提示、回复/清除可见状态、空白禁用理由。**中风险：** 页面内提交失败提示与草稿保留规则、同名提及选择语义（不改 wire）。**高/未来：** 送达/已读/幂等自动重发须协议证据；不添加附件或 reaction。

### F03 浏览成员与发起成员动作

- **目标/入口/步骤：** ChatSidebar → 在线成员/被屏蔽区 → 点击行 → 交易或屏蔽/取消屏蔽；自己不打开相同动作。名单来自 `IClientUserDirectory`，断线清空；屏蔽区可折叠。E05。
- **摩擦/隐藏/冗余/不一致：** 全行可点但动作入口无明显提示；同样的 trade/block 菜单在消息姓名处重复实现。在线数与当前显示名单（排除/折叠屏蔽成员）需说明口径。Figma“交易中/空闲/离开”不对应已核实的通用 presence 模型。
- **约束/分类：** 无 avatar/title；blocked 是本地能力。Trade 可选，菜单目前通过 host context 发起，不等于 typed 第三方 action 注入。U/C/P。
- **低风险：** hover 提示/行末动作入口、屏蔽分组解释。**中风险：** 统一成员动作收集与禁用理由、Trade 缺失时降级。**高/未来：** 通用 away/idle 需要真实状态来源，跨客户端 presence 可能需 S；本地“有活跃交易”亦须获业务快照，不可代替全局 presence。

### F04 搜索/过滤成员

- **目标/入口/步骤：** sidebar 搜索框输入 → `UserList.Filter` → strip rich text、大小写不敏感子串过滤 → 重新测量/虚拟渲染在线及被屏蔽列表。E05。
- **摩擦/隐藏/冗余/不一致：** 当前框无明确搜索成员 placeholder；零命中易与空名单/断线混淆；搜索 blocked 的范围不明显。没有已证据的额外在线状态 filter，Figma filter 图标不能自动增加筛选维度。
- **约束/分类：** 本地目录范围，已有缓存/动态行 offsets；不是服务端查询。U/C。
- **低风险：** placeholder、清除、零命中和范围提示。**中风险：** 可解释的结果/总数、明确 blocked 是否参与查询。**高/未来：** 离线全用户目录搜索需要服务端能力；全文消息搜索明确排除。

### F05 交易与报价

- **目标/入口/步骤：** 成员/消息姓名菜单发起 → Trade tab 活跃交易列表 → 打开 `TradeWindow` → 地图/库存物品选择数量 → 更新报价 → 双方确认 → owning service 处理真实回执/交付。工具栏含更新、取消、重置；窄屏 offer tabs 与工具栏 overflow。E06。
- **摩擦/隐藏/冗余/不一致：** “选择数量”“更新报价”“确认报价”是不同动作，可能被用户误认为一次提交；不是可随意删除的冗余。数量 ±1/±10、菜单全选/一半重复呈现；临界宽度隐藏部分按钮。活跃列表取消与详情取消的反馈不同；部分错误只日志。
- **约束/分类：** 原子编码、库存预留、真实 token 回执；未知结果不能自动返还。UI 持有 `pendingItemStacks`、临时物品、订阅，已存在关窗保留待确认 listener 的逻辑，不能描述为“关窗一律丢失”；仍有业务/视图耦合。`ResetTradeItems` 先 DropPods 再更新空报价，是需要另行语义审查的具体顺序风险，不是本轮已证明复制漏洞。C/P/S/B，高影响。
- **低风险：** 明确“选择未提交/报价等待确认/双方确认”的文案和按钮层级。**中风险：** 由 Trade 投影操作阶段、错误、未知与禁用理由，Host 显示；托管所有权脱离窗口需业务迁移。**高/未来：** 重置/取消恢复、持久核对、回执能力补足须独立业务与协议审查；不从 UI 实现乐观成功。

### F06 库存与交付

- **目标/入口/步骤：** Inventory tab → 分组条目/可用量 → 提取数量或安排日计划 → plugin 验证并交付；收到物品的 delivery choice / 快捷设置决定直接落地或存入库存。恢复提示先于普通列表。E07。
- **摩擦/隐藏/冗余/不一致：** 编辑器置于列表上方且占高度；日计划直接暴露 tick 输入，用户需理解实现单位。条目消失会关闭编辑器，未见统一保留草稿/失效解释。恢复、故障、pending 与普通空状态各有单独绘制。
- **约束/分类：** 长整数、可用/预留余额、codec、存档/ledger、journal 只读保护与玩家恢复确认；不得部分交付。U/C/P/B。
- **低风险：** 可用与预留标记、单位说明、恢复动作后果说明。**中风险：** Host 数值表单/状态组件，plugin 提供限制及错误，时间展示映射到实际 tick 规则。**高/未来：** 修改日计划权威/日志/自动恢复是业务变化；当前不能提出自动返还未知物品。

### F07 商店浏览与代码插件管理

- **目标/入口/步骤：** Store tab/窗口 → 刷新目录 → 名称查询/filter → 卡片选择 → 详情/版本 → 规划确认 → 下载/校验/安装 → 管理启用、更新或卸载；运行状态与重启提示独立。E08。
- **摩擦/隐藏/冗余/不一致：** catalog、inventory、desired state、active、restart pending 层次多；一行“已安装”不能代表运行。manage 菜单及详情动作分散；部分诊断 code/raw error 面向开发者。列表/详情在窄屏切页，需保留选中项。官方/受管/Workshop/本地 badges 是不同事实，不能都合成“安全”。
- **约束/分类：** 冻结计划、来源身份、依赖、共享 owned-file transaction、取消不撤销已执行副作用；代码侧载需 DevMode。U/C/P/B。
- **低风险：** “已安装/待重启/启用意图/实际运行”用可读文案，disabled 理由靠近动作。**中风险：** 共用列表/detail/status renderer，保留 Store controller 权威。**高/未来：** 新源配置、在线主题准入与投稿延期，不视为重设计必要工作。

### F08 主题选择、编辑与导出

- **目标/入口/步骤：** RimWorld Mod Settings 的 Host Theme panel → 独立主题窗口 → 选择 draft → 按 owner/group 找键 → 色值/float 编辑与小样预览 → Apply 热应用；导出打开 metadata/路径窗口生成 ZIP → 路径反馈/复制/开目录。E09、E10。
- **摩擦/隐藏/冗余/不一致：** 应区分选中草稿与实际应用；预览小样不是整页 live preview 的证据。Clear override 与显式 inherit 不同，来源链过技术化；缺插件键仍需保留/可检查。目标已存在拒绝覆盖，本地文件成功与开目录失败是两种结果。Cancel 关闭未应用草稿，不应暗示撤销此前 Apply。
- **约束/分类：** per-theme profile、unknown/inherit、注册限额/范围、内置基线、默认回退、真实模块命名空间。U/C/P/B。
- **低风险：** “正在编辑/正在使用”标签、普通来源名+高级键细节、失效主题说明。**中风险：** 更接近真实组件的共享 renderer 预览、分组/搜索但保留 unknown。**高/未来：** schema 语义变化与任意字体/资源支持需另案；上传/账号非本轮。

### F09 本地主题包安装、更新、卸载

- **目标/入口/步骤：** Store → 本地主题包 → ZIP 文件窗口（非 DevMode）→ 已校验候选/名称/作者/ID/版本 → 安装或更新 → 打开主题选择器应用；卸载确认整包，选中包显式删除转默认并保留 profile。E11。
- **摩擦/隐藏/冗余/不一致：** 操作跨 Store 与 Theme window，安装≠应用；作者/ID/source 文案偏技术，raw error/code 可能直接出现在 `Run`。选择器本地导入已移除，因此不能再建议点击导入按钮；Host-only 用户仍可选择/编辑/导出，但无 Store 时没有当前 ZIP 安装视图。
- **约束/分类：** independent ManagedThemes、expected artifact digest、managed source ownership；意外缺失与显式卸载回退不同。小包同步操作慢盘耗时未测。U/C/P/B。
- **低风险：** 成功给“去选择主题”、解释卸载全部变体及保留覆盖。**中风险：** Store 公共主题包 presentation、友好错误映射；是否提供 Host 的包管理视图需产品决定，底层已有 Host service。**高/未来：** 在线包流程延期；不得 UI 直接 ZIP 解压/删文件。

### F10 插件发现、管理与插件提供的 UI

- **目标/入口/步骤：** Host Mod Settings 的 extension panel → manager；Store 管理安装包；插件正常注册 main/sidebar/banner/settings/badge，用户访问其 tab/窗口；Example 通过普通 DI/API 展示任务/操作/provider/lifetime。E12、E13。
- **摩擦/隐藏/冗余/不一致：** installed/registered/active/failed/disabled/dependency-disabled 非同义；数量多会挤导航，Figma 固定五 tab 无法覆盖 talent/Example/未来插件。sidebar 不能永远只留成员。技术 ID 与普通标签混杂需按读者区分。
- **约束/分类：** optional provider 正常、registered≠available；普通 API 借用，exact handle 要重验。current shell Draw callbacks 未逐个 try/catch，不能声称新 API failure isolation 已覆盖所有视图。U/C/P/B。
- **低风险：** 分类状态文案、管理入口清晰、故障诊断 secondary。**中风险：** 无硬编码的 registry navigation、错误占位/GUI 恢复、stable identity、动态失效；typed view contribution 接入。**高/未来：** 通用 business SDK 或任意 plugin sandbox 不是 UI renderer 的职责。

### F11 设置：客户端、连接与“房间设置”

- **目标/入口/步骤：** Chat sidebar 设置→快速 SettingsWindow（地址/端口、连接/断开、在线昵称、quick provider；Inventory 提供 delivery 设置）；RimWorld Mod Settings→客户端配置+所有 settings providers（Chat/Trade/Store/Theme/extension 等）。E14。
- **摩擦/隐藏/冗余/不一致：** 双入口并非完全重复：覆盖集不同，但用户难猜哪个有主题/插件。连接目标配置不是服务端房间配置。没有在已检查 current views 中找到可编辑服务器公告、房间权限、room hierarchy 的通用管理页面；不应把本地设置重命名为服务器管理。
- **约束/分类：** quick settings 已有独立 measure/Draw contract，mod panel 是 `Listing_Standard`。业务设置属于插件，Host 不判断 schema/协议；设置保存不能自动当服务端批准。U/C/P/B；真实 server 管理扩展是 S。
- **低风险：** 入口明确“连接/快捷设置”和“客户端设置”，互相导航说明。**中风险：** Host 通用 sections + 插件字段投影，保留 IsVisible/Order/SectionId 与原保存规则。**高/未来：** 真正 room/admin 功能需业务契约/权限/服务器支持，无当前实现证据。

### F12 连接、重新连接与错误

- **目标/入口/步骤：** 启动读取保存目标连接，或 SettingsWindow 地址/端口 → QueueConnect → transport → authenticate → login → Online；失败可再次通过现有连接动作尝试。E15。
- **摩擦/隐藏/冗余/不一致：** Connected、Authenticated、LoggedIn/Online 层次被 UI 压成部分布尔提示；sidebar 断线画空面板。连接失败有状态行/消息框，认证/登录失败亦有事件及弹窗处理，但尚无统一阶段呈现。未找到支持完整自动重连政策的 UI 证据，不承诺既有指数退避/自动恢复。
- **约束/分类：** QueueConnect 是 ThreadPool 入口，不把网络等待塞进 Draw；UI 在主线程显示结果。连接切换/断线对每个业务拥有者影响不同。U/C/S/B。
- **低风险：** 断线占位给连接设置动作、显示目标与实际在线区别。**中风险：** 中立 session 状态投影/通知，保留草稿且拒绝 stale 操作。**高/未来：** 自动重连、消息/交易重放、未知结果核对必须各自业务规则，不可盲目重试。

### F13 空、加载、权限、校验与错误状态

- **目标/入口/步骤：** 每个页面渲染相应 state → 阅读原因 → 使用当前提供的动作；Inventory 恢复优先，Store Busy/失败/取消/进度，Chat pending/denied、image loading/failed、登录限制，Trade no trades/空报价。E04、E06–E08。
- **摩擦/隐藏/冗余/不一致：** no providers、未连接、零搜索结果、真空列表都可能表现为无内容；模块错误 isolation 与业务拒绝不是同一件事。Chat 白空输入/设置 int Parse 失败可产生泛化拒绝或临时 0；Store/local themes exception message 不是可读操作建议。成功只换色不一定可理解。
- **约束/分类：** 权限/数量/codec/provider guard 以 owning plugin 为准。U/C/P，远端拒绝 S；数据恢复 B。
- **低风险：** 文本+符号，不仅颜色；按真实 state 显示原因/现有 next action。**中风险：** 公共 Empty/Loading/Error/Validation/OperationStatus 模型，projection 由插件给；draft 校验不覆盖 authority。**高/未来：** 增加权限模型、跨会话持久操作确认需 owner/protocol；不凭 renderer 自动恢复。

### F14 维护 legacy 插件

- **目标/入口/步骤：** RedPacket tab→地图/库存物品查询选择→份数/普通或手气→发送/冷却→红包列表领取/详情；Talent tab→直接交易、市场（初始）、租赁→各自选择/确认/状态。E16。
- **摩擦/隐藏/冗余/不一致：** 红包发送/列表两 pane 窄屏需切换；冷却、pending 不能只当 disabled。人才含 pawn、交易/租赁状态，普通 item list 不足以表达。Figma 的“Legacy”标签会把产品能力与内部兼容机制混淆；且未画 Talent 不等于授权移除。
- **约束/分类：** maintained package 内部 1.9/current adapter；relay 接受≠HTTP 排队；未决库存托管、pawn return ledger、真实业务确认不归 Host renderer。U/C/P/S/B。
- **低风险：** 保留功能名称，Legacy 作为高级兼容信息；明确 cooldown/pending。**中风险：** 通用选择/列表/表单迁移，pawn/游戏 inspect 作为受控 custom region。**高/未来：** 回收/relay 历史核对、租赁结算、旧 adapter 退出独立评估；不承诺全流程已验收。

## 3. 按影响排序的 UX 问题

| 级别 | 问题与具体证据 | 影响 / 分类 | 建议边界 |
| --- | --- | --- | --- |
| 高 | TradeWindow 同时绘制、预留/物化/托管、管理 pending listener；Reset 先返还再发空报价 | 重设计误移逻辑可损失/重复交付；C/P/S/B | 明确现有语义及缺口，保留 authority；先由 Trade 提供投影，不能 renderer 回滚 |
| 高 | callback/task 完成易被 UI 成功色误表述为业务成功 | 远端结果错误表达；C/P/S | 原语义已存在，呈现必须区分 Submitted/Pending/Rejected/Unknown/Confirmed；不添加 wire 状态 |
| 高 | Figma 固定导航、唯一成员 sidebar、缺少 Theme/第三方完整设计 | 削减插件与独立主题能力；P/B | provider 集合驱动；空集、缺 Store/Chat 仍可进 Host 设置 |
| 高 | shell 保存 provider 引用直接 Draw，当前没有逐 provider 故障 UI 边界 | stale/失败可能阻断窗口；C/P | 生命周期重验、每区域错误占位、GUI/scroll 清理及日志节流；不能仅借 API directory 名义宣布已解决 |
| 中 | 空列表、断线、无命中、缺 provider 混同 | 难以找到恢复入口；U/C/P | 精确状态与允许动作，不 invent reconnect policy |
| 中 | 双设置入口覆盖不透明，主题安装/应用跨窗口 | 能力难发现；U/C/P | 明确入口/返回/应用状态，保存方式由 owner 保留 |
| 中 | schema/key/source/code/raw exception、tick 等实现词直接暴露 | 理解成本；U/C | 普通操作文案+高级诊断，不删除版本/来源事实 |
| 中 | 消息/成员菜单重复，typed action 尚未接入真实页 | 行为不一致、第三方难组合；C/P | bounded typed contexts+view integration，不反射私有 UI |
| 中 | Chat 数字设置直接解析/写值，非法临时文本可能写 0 | 输入编辑与有效设置混淆；C/B | field draft 与 owner 校验/提交分开，保留持久键 |
| 中 | 已有 cached native geometry 不能直接接受 Figma 像素值/任意字体 | UI scale、中文/长翻译导致截断；U/C | 渲染与 measure 同源，明确 metrics、主题/语言失效 |
| 低 | 方形按钮、hover/selected 共用、primary 用 Success 文本色 | 层次与视觉方向不同；U/P | 扩展现有组件语义、保持旧键兼容，勿统一把 green 改 amber |
| 低 | 多处规则重复：按钮/theme scope/row/menu/状态卡 | 维护与一致性成本；C/P/B | 扩展公开基础组件，再评估必要替换，维护 adapter 不暴露给新作者 |

高项不是全部新缺陷；部分是当前已命名技术债，部分是重设计时必须防止的 capability regression。

## 4. UI 架构现状

### 4.1 可复用基础与处理意见

| 现有基础 | 直接支持 | 需扩展 / 可替换部分 | 禁止隐含丢失 |
| --- | --- | --- | --- |
| `ClientWidgets` / `ClientGuiScope` | theme-aware button、field、checkbox、slider、progress、link、tabs、border、surface；恢复 color/bg/content/enabled/font/anchor/wrap | radius/semantic variants、focus/selected/disabled feedback、字段错误与 action presentation；新通用组件从此演进 | 公共 theme reader 与禁用/GUI 状态语义 |
| ResponsiveForm/Toolbar/Split、UiScreenSafeArea、UiLayoutHints | 窄屏堆叠、overflow、单 pane、safe clamp | 通用 shell/navigation 和描述渲染器测量；theme metrics 若未来提供需失效 | 真实尺寸下降级，不把 Figma 1232×820 当 minimum |
| `VirtualListLayout` 与插件 offsets/cache | 可见行绘制、overscan、动态高 | 共享 cache/session 与稳定 row identity；按宽/字体/语言/版本更新 | 保留大型名单/消息/商店虚拟化 |
| `ServerTab` | neutral main/sidebar/banner 排序/布局、accept-key | 可替换其视觉编排、加入稳定 identity、溢出与隔离；内容不会随壳层自动转换 | 任意 provider、sidebar tab、notice、keyboard handler、optional hints |
| Host settings / quick providers | 插件 section、可见性、顺序及独立 Host theme 入口 | 统一容器、导航与 bounded forms；`Listing_Standard` 回调要有迁移方式 | 全设置与快捷设置不同能力；不 hardcode 官方选项 |
| 旧 Displayable/basic widgets/containers | 当前 retained GUI 仍有消费者，已使用 HostThemeDrawing | 用公共组件替换必要视觉分支；deprecated Flex/TabsContainer 不作为新架构基础 | 先记录消费者及行为迁移，不批量删除 |
| Module theme facades | Chat/Trade/Inventory/Store/maintained plugin 调色与 drawing snapshot | 可提炼画面角色/公共 helpers；private facade 不代替注册器 | 同一 key、同一 resolver，避免第二 palette loader |
| Typed actions、ApiDirectory、Task、Lifetime | 所有权、失效/可用性、owned 结果集与回调状态 | 真实 view integration、descriptor provider / presentation session / custom renderer 实现 | 不是已有通用 layout engine，不把任务当网络承诺 |

### 4.2 耦合和重复

1. **TradeWindow：** layout、数量草稿、game Things、inventory reservation、token pending、交付及 listener 混合。替换窗口之前须说明谁保留未决状态；业务 owned operation 不随 renderer 销毁。
2. **ChatMessageList：** measure/cache、回复跳转、上下文菜单、图像异步与 texture disposal、状态颜色同类。通用行/菜单可复用，但图像与 message semantics 需 Chat-owned 适配；不是把整个类搬 Host。
3. **UserList：** 目录/屏蔽事件、过滤、cache、名称格式、trade/block menu 共处；过滤快照可由 Chat 投影，Host 可画列表。用户名 rich-text 偏好需保留，搜索按 stripped name。
4. **InventoryTab：** 恢复分支、字段 parsing、schedule/extract 命令与 row/editor layout 同类；通用表单可移交 Host，ledger/authority 保留。
5. **ManagedPluginStoreView：** repository/control snapshots、selection/filter/detail、状态/code 映射及 install planning 都在 view；应区分 projection adapter 与纯 renderer，不把 Store 业务搬到 Host。
6. **维护主题 wrappers：** RedPacket/Talent 的 Button/TextField/Surface 重复当前 ClientWidgets 的部分行为，但还有旧 Host adapter 需要；新作者用当前公共能力，历史兼容实现留插件内部。
7. **设置：** `Client` 和 `SettingsWindow` 的连接字段、普通/quick section 的布局与保存逻辑重叠；共享呈现不应改变保存时机。字段文本草稿和持久有效值应分离。

### 4.3 缩放/本地化风险

原 window 名义上限 1280×800，参考 frame 1232×820（含自己的顶部结构）；不可按参考高度直接替换或宣称适配通过。使用 RimWorld UI 坐标、safe area 和缩放后可用尺寸。中文/英文、长插件名称、banner 多条、32+ tab、极窄 drawer、最大 UI scale 都需后续实机检查。10–12px Figma 元信息不等于 Verse Tiny 实际像素；不能假设 Plus Jakarta/Noto/JetBrains 安装在所有玩家系统。重点保护 composer、恢复确认、取消和 disabled reason；tooltip 不是隐藏必要信息的理由。

## 5. Figma 与当前 UI 视觉差距

参考是一个暖色深色主聊天屏：frame 1232×820，top bar 56；内容 chat 990 + roster 240；品牌/nav pill 12 radius；窗口标题说明 20 radius；item/trade card 512 宽、14 radius；composer 外区域 75、内部 966×50、16 radius。字体含 Plus Jakarta Sans、Noto Sans SC、Liberation/JetBrains Mono、WenQuanYi，文本 10–16。这里只给读到的参考值，非建议必须复刻的布局常量。

| 维度 | 当前源码 | 参考差距 / 处理评价 |
| --- | --- | --- |
| spacing | 常见 10/8/4，插件各自布局，shell tabs/banners 占高 | 参考顶部 16 padding、卡片 14、composer 12、行 gap 4/12；需统一间距体系，保持测量/虚拟化 |
| typography | Verse GameFont Tiny/Small/Medium、rich text 用户名与缓存 | 16 消息/姓名、11 时间/12 nav；使用 Verse 可读层级优先，字体替换不是主题颜色的自然扩展 |
| radius | ClientWidgets fill/border 为矩形，native Widgets | 分层 12/14/16/20，可缓存 9-slice；fixed bounded geometry 候选，不在每帧新 texture |
| hierarchy | 顶部 banner + native tabs，右栏 status/settings/search | 品牌、tab、online、plugin utility、settings 同 header；顺序可改但保留 all provider discovery |
| navigation | 主/侧 tab overflow wrapping；排序 numeric，选中 index | pill header、固定五功能；应是 provider-driven pill/overflow，不 hardcode 当前五 tab |
| messages | pending/denied、分组、self/mention、reply、timestamp、notice、image | 参考无 avatar、姓名/时间/正文层次可用；不要因参考省略回复/错误而移除现有状态 |
| member panel | preferred 210/min160、blocked group、native input | 参考 240、圆搜索、名称/status；保留过滤/blocked，status 仅真实来源 |
| composer | 30 高、80+ localised button、reply bar、stack fallback | 50 圆容器、placeholder、amber 主按钮；保留焦点/Enter/mention/拒绝草稿，不加 upload |
| buttons | normal/primary/danger，共用背景；Primary 文本用 Success | 参考主按钮 amber filled；需 accent variant/对比文字概念，不重定义 success |
| tabs | native draw+tint，main button badge 优先第一个 | active pill、trade“2”；Trade provider 当前非 IBadgeProvider，图中计数不能直接认为已支持；新增绑定不得混淆 Chat unread 与 trade count |
| cards | Inventory/Store/notice 状态块是插件手绘矩形 | 参考 item stats、trade ticket、battle strip；通用 Card 可以画外观，但业务数据/聊天集成有明确缺口 |
| special content | Chat notice/system/image/reply 已有；typed actions 在 Example | 未核实 item card 消息 schema/renderer、trade message projection、battle feed provider；不能由 UI 解析普通文本猜业务 |
| border/divider | `ui.separator` 同时用于边框/分隔，1 width 常见 | 参考低对比暖色线；需评估 separate border role，保留 separator 语义 |
| surfaces | background 可透明；surface/hover 多 alpha overlay | 参考 #151412 chat、#171614 roster、#201e1a card、#12110f composer 等 opaque 层次；不能直接把所有主题 overlay 当 opaque |
| hover | 模块 rowHoverBg/native highlight/Widgets Button | 参考名单 rounded hover；未提供全部 hover frame，不能声称有完整状态规范 |
| selected | Surface(selected) 使用 HoverHighlight；各插件自选 accent | active tab、选中 row 需分离 selected/hover，保持 plugin semantic colors |
| disabled | `ui.control.disabled`/secondary，业务控件自行判定 | 参考静态正常页面未给 disabled specs；需状态规范而非猜测颜色 |
| focused/pressed | ui.input.focus、ui.button.pressed 已有 | 扩展 rounded focus hitbox，保留键盘 ownership；参考未定义完整 focus/keyboard 行为 |

### 5.1 视觉中不能直接落地的语义

- “驻防区通讯已加密”：本轮没有验证 E2E/transport 安全保证，不能保留为产品承诺；公告内容仍来自其真实 owner。
- “在线成员 (5/142)”：示例数字不证明分页/全服总数能力；当前 directory/count 的过滤口径先说明。
- 空闲/离开：没有核实的 current public presence projection，不做假状态。
- 物品 ID `#EQ-8841`、制造商与三项护甲 stats：没有证明现有聊天 payload 提供这些字段；可来自 future plugin snapshot，但不通过 Host 持有 Thing 或 wire DTO。
- 聊天中的贸易请求卡：已有 Trade 操作不等于已有消息集成。先列缺 context/projection/view integration，不能仅改画法。
- 战报：Chat 的 notice 样式可复用，战斗事件采集/广播 provider 未核实，不自动新增。
- 最小化/最大化/关闭：当前 Verse MainTabWindow 拖动/resize 有实现，桌面 OS 三按钮语义未实现；不是纯装饰复制项。

## 6. Theme loader 与 token 审计

### 6.1 已有职责，不建立第二套 styling

`ThemeLoader.cs` 当前主要做 source discovery；真正 runtime 是 `UiTheme`，历史输入由 `ThemeInputAdapter`，Host/插件通过公开服务获取 snapshot。theme source 包括 built-in、active mods、用户 XML、managed package 提供的已验证数据。`Capture()` 返回已发布 immutable snapshot（不是每次复制整份字典），Changed subscriber 异常独立处理。Draw scope 让一次绘制使用统一 palette。

注册绑定真实 Activate 模块，namespace 防止占 Host/别插件键；batch Commit 验证后一次发布，Dispose 撤销。注册提供 owner label/group/description、颜色默认/fallback、float 范围。不是 View 里登记颜色，不是只给官方插件开放。

### 6.2 概念到当前 canonical namespace

| 设计概念 | 当前键/能力 | 评价 |
| --- | --- | --- |
| background / surface | `ui.background` / `ui.surface` | 原样可用；检查透明 overlay 的合成语义 |
| surfaceHover | `ui.hover`，`chat.rowHoverBg`、`trade.rowHoverBg` | 原样可用；模块显式值必须优先，不用全局强制盖掉 |
| border / divider | `ui.separator`、ClientWidgets.Border/Separator | 现可用；若需区分边框，新 Host canonical role（候选 `ui.border`）有边界扩展，不能重命名旧键 |
| textPrimary / textSecondary | `ui.text.primary` / `ui.text.secondary`，Chat 专用 text | 原样可用；保留昵称格式偏好及 input/message overrides |
| textMuted | 暂复用 secondary，但透明 timestamp/reply 使用 `chat.replyQuoteText` | 独立 `ui.text.muted` 可作为候选，需对比度/继承规范；不是已有键 |
| accent | `ui.accent` | 原样可用；Figma amber 可由主题值表达 |
| success / warning / danger | `ui.success` / `ui.warning` / `ui.error` | 原样使用；danger 映射 error，勿另建同义 loader |
| pending | `ui.pending`、`chat.pendingMessage`、`trade.pendingBadge` | 原样；区别等待与已确认不依赖颜色推断 |
| surfaceElevated / surfaceSelected | 当前无对应通用独立 role；selected 常用 ui.hover，Inventory 有 selectedBackground | 候选 Host 扩展；具体命名/alpha语义未冻结，旧 module key 保留 |
| composerBackground | `ui.input.background` 影响控件 skin；Chat 只有 inputText / inputReplyBg 等 | 新 Chat-owned composer role 候选，不能以 inputReplyBg 替换 composer 全背景 |
| memberPanelBackground | 当前 shell/Chat surface，blockedBg 为屏蔽状态 | 若需独立面板色可新增 Chat-owned key 回退 ui.surface；不能借 blockedBg |
| cardBackground | `ui.surface`、`trade.panelBg`、Inventory info/editor 等 | 共用卡壳从 surface 出发；独立通用 card role 仅有多消费者需求时增加，插件 key 不改名 |
| controls | `ui.button.background/hover/pressed`、`ui.control.disabled`、`ui.input.background/focus` | 原样保留；需要 accent filled / text-on-accent / selected/focus details 的 bounded 扩展 |
| extension/status/source | `ext.status*`、`ext.log*`、`ext.headerText/summaryText`；`plugin-store.badge.official/managed/workshop/local` | 保留，用 label/group 一般化显示；不是 new social title/color system |
| plugin-owned semantic colors | `chat.*`、`trade.*`、`inventory.*`、`redpacket.*`、Talent/Example namespace | 保留；公开 renderer 应允许 owner 注册且引用自己的 override/fallback，而非只接受 Host 固定颜色 |

完整 canonical token 清单应以注册与用户文件为准；本文列举关键族，未把每个 private property 当新主题契约。唯一已核实的 Chat float 是 `chat.nameColor.saturation/value`，Inventory 等颜色不等于 spacing 已主题化。

### 6.3 真实 resolver 顺序

`UiTheme.ResolveColor`：在主题存在时，草稿/profile 显式 override → 选中 theme 显式值 → built-in baseline → declared fallback key 递归 → 注册 default（无 fallback 时）。**显式 inherit** 跳过当前 key 的显式/theme/baseline 路径；draft 或主题自己的 inherit 对顺序有作用。不能简化成“缺键就混色”。fallback 验证循环与缺目标，只允许同 owner 或 Host 目标，不允许依赖另一可选插件键。

float 是 override → theme → baseline → 注册 default，按注册 min/max clamp；没有 color 式 fallback chain。主题意外失踪时 active 不存在，保留选中意图但使用 fallback display，不能假装恢复了丢失主题的全部 profile 外观。

建议缺新 role 时采用 **现有 registry 的明确 fallback**；例如 elevated→surface、border→separator、muted→secondary。若未来需要 surface/accent 混色，必须在同一 resolver 语义内定义且不覆盖显式值；现有 `RegisterColor(fallbackKey)` 只表达 key inheritance，不能表达任意计算公式。透明 `ui.surface` 与不透明 `ui.background` 不能未经合成规则直接算 RGBA 平均。built-in 对新键给显式值会优先于旧主题的 fallback：给基线新键前必须检查是否意外遮蔽用户已有 ui.surface/accent。

### 6.4 format/schema 与 metrics

- XML 已有历史 v1/v2/v3 adapter；v1 aliases 归 canonical、历史 nickname 参数归 Chat；v3 支持 explicit inherit。managed themes 接受 canonical v2/v3。不是“缺 schemaVersion”。package manifest schema、data receipt/journal schema、theme XML、profile version 均不同。
- 只增加注册色/float 通常不需要换 XML 版本；若增加 computed color、字体/资产、布局语义或新元素，需单独评估输入版本、校验、安全限制与 down-level 行为。不能为 UI 翻新随意升所有版本。
- radius：建议初期固定 bounded component 常量；未来可选择少数 density/radius float，但须 public metadata、clamp、缓存失效、hitbox一致。spacing/control heights：优先系统 metrics+UI scale/响应式；自由编辑所有尺寸会破坏 plugin measure，现有 float API 不证明已有此能力。
- border width：先固定有限宽度，支持缩放；若主题化不能允许 0/大值隐藏重要状态。typography：Verse 三档+本地化优先，任意字体和连续 font size 会影响兼容/资源/测量，不用 float 假装字体加载已存在。
- animation timing：优先即时状态/极简反馈，未来可 bounded duration；不要动画每帧触发 measure。无需为静态 Figma引入动画系统。

## 7. 迁移与兼容风险

| 边界 | 必须保持 | 风险及缺省策略 |
| --- | --- | --- |
| 旧主题键与 user XML | canonical 持久键、历史 reader、未使用键、显式值/inherit | 不批量改 camelCase、不自动将 inherited 默认铺平；缺新 role 使用明确 fallback |
| optional plugin | 主题文件包含未安装插件键正常 | editing/export 保留 unknown；registry 撤销只影响当前 effective palette，不删除用户意图 |
| hot apply | 同一 snapshot/version 通知所有 supported path | renderer/custom cache 按主题版本失效；不重建业务操作或重新订阅 |
| package identity | `package/{packageId}/{variantId}` 稳定，与版本/文件名不同 | 更新移除 variant 保留 intent；显式卸载选中包回默认并保留 profile |
| package storage/recovery | independent ManagedThemes，shared owned-file transaction，receipt来源核实 | UI 不调用直接解压/删树；设置后处理可重放，不声称跨目录原子 |
| local export | ZIP metadata、unknown/inherit、目标已存在拒绝覆盖、路径反馈 | 不删 public Catalog Import/Export，只因 UI import 按钮移除；开目录失败不等于导出失败 |
| versions | product 0.9.7；SDK/API 2.0；CLR identity 1.9.0.0 | `AssemblyFileVersion` 2.0 不是 CLR 不兼容证明；纯主题 phinixRange 用 product |
| maintained 1.9 adapter | 插件内部继续翻译旧 Host/API | 新 presentation contracts current-only；维护包在 adapter 选择路径，不把旧 UI/schema判断暴露给普通作者 |
| imperative Draw consumers | Chat、Trade、Inventory、Store、RedPacket、Talent、Example、第三方 | 新 renderer 不自动转换；兼容宿主容器保留访问，逐消费者迁移情况需记录，不能承诺永远两套框架 |
| localization | package-scoped localizer / Verse key boundary | 不把 SectionId 当 title、不用 private reflection 获取插件文案；描述数据含可读 label/localized text来源 |
| authority/recovery | all-or-nothing delivery、真实回执、Unknown 保留 | renderer dispose/Cancel/Closed 不撤销 owner operation；普通刷新不确认请求 |

## 8. Host / plugin 渲染责任矩阵

H＝Host shell；R＝通用 reusable presentation；X＝specialized custom region；B＝business outside renderer。

| 实际表面 | 当前 owner / 入口 / rendering | 业务依赖 / theme | 公共现状 | 合理归属与迁移缺口 |
| --- | --- | --- | --- | --- |
| 主窗、tab、sidebar drawer、banner placement | Host `ServerTab.DrawContents` native Rect/tabs | provider 集合；HostThemeDrawing | main/sidebar/banner/hints/accept | H；稳定 ID/实时失效/错误边界缺；内容不是自动重绘 |
| settings sections、window/dialog shell、overflow | Host Client/SettingsWindow，插件 DrawSettings | settings context；ui.*、plugin palette | settings/quick providers、Window services、responsive helpers | H + R；字段描述与 session 未实现；保存由 owner |
| Chat stream / composer | Chat MainTab/MessageList imperative + cached dynamic rows | Chat service/user directory；chat.* | IMainTabProvider、IUiAcceptKeyHandler、plugin contracts | R 外壳/row/actions；Chat projection+input intent；image/reply/rich content X |
| 成员 row/search/block/trade menu | Chat Sidebar/UserList imperative | directory/block context/trade capability；chat.* | sidebar provider；typed action 基础仅通用 | R；公开 Member context、动作 view integration 缺；block/trade B |
| Trade list / offer quantity / confirmation | Trade List/Window，native Thing previews、buttons | Trade、Inventory reservation、game items；trade.* | main tab + business API/Contracts | R + X icon/inspect；snapshot、field drafts、confirmation projection 缺；预留/回执/交付 B |
| Inventory rows / editors / recovery | InventoryTab、自绘 cards、field parsing | inventory ledger/schedules/codec；inventory.* | main/settings/quick、业务 contracts | R；恢复决策与校验 B；Item preview X，不能 renderer approve recovery |
| Store card/detail/status/action | Store ManagedView/controller，自绘 list/detail | catalog/install/runtime snapshots；ui.*、store badge.* | main/settings、management APIs | R；bounded projection adapter，install planning B |
| Theme selector/editor/preview | Host ThemeSettingsWindow，catalog preview、draft | theme catalog、registry、profiles；ui.* | 完整公开 theme contracts | H + R；shared preview metrics、sources/unknown展示，resolver B（Host infra） |
| ZIP export / local file selection / packages | Host export + Store LocalThemePackagesWindow | package service/exporter；ui.* | public Host services + current Store view | H/R：dialog/fields/state；storage B infra，path/file UX X adapter |
| RedPacket send/list/detail | maintained plugin Tab/window imperative | relay/state machine/inventory；redpacket.* | main/settings、internal compatibility | R forms/list/confirmation；item inspect X；relay/pending B |
| Talent direct/market/rental | maintained plugin Tab/panels/windows | pawn/game/runtime/ledgers；Talent theme | main/hints/settings、internal adapter | R rows/forms/actions；pawn inspect/portrait X；serialization/settlement B |
| Example count/actions/tasks/provider | Example tab/owned windows | settings count、task/lifetime；Example keys | current public DI/provider/action/theme | R 的后续小实验；不是 real business success |
| arbitrary third-party Draw | 各 provider callback | 插件私有业务与已注册键 | 当前 imperative provider | 受控 X container；仅 shell/control helpers 可统一，不能把 pixels“解释”为 descriptors |

Host 可负责 common selection/confirmation 的视觉与 focus；“确认什么、是否允许、执行后结果”仍由 owning plugin 定义。Theme resolver/包恢复是 Host 基础设施，不因为表格列 B 就外包给视图或业务插件。

## 9. 公共 rendering/composition 模型评估

### 9.1 三种有界方案

| 方案 | 优势 | 限制 | 评价 |
| --- | --- | --- | --- |
| 共享 theme-aware components | 当前 API 延伸最小、适配 IMGUI、作者灵活 | plugin 仍手写 layout/state，不能统一 restyle 任意 Widgets/Draw | 必需底层；仅此不能满足 Host-rendered forms/lists/pages |
| plugin descriptor → Host renderer | 同类列表/表单/状态一致，第三方同等可用 | contracts/session/invalidation/validation/result 尚缺，需逐 view 迁移；泛化过度有引擎成本 | 只覆盖真实重复 flow，避免 universal language |
| bounded descriptors + owned custom regions | 通用页面与特殊 RimWorld 内容兼顾 | measure/draw 生命周期/GUI守卫有严格约束；custom 保持自己的正确主题使用 | **建议模型**：同一组件/主题/状态基础，多 rendering path，不复制 loader或业务 |

### 9.2 建议的责任与最小数据（概念，未创建接口）

- **插件提供：** 稳定 page/section/row/field/action ID（owner namespace）、本地化可读 label、显式顺序/可见性、只读 projection revision、语义 role、合法的 owner theme key（可选）、字段类型/单位/限额、disabled reason、已有业务 operation 状态与允许动作。不是 mutable inventory、raw wire DTO、Thing 作为通用 descriptor。
- **Host 渲染：** page/container、row/list/detail/card、字段/选项、common toolbar/menu、validation/state banner、确认/选择 shell、focus/selection/scroll。只渲染已声明内容；业务判断不可通过“缺少按钮”猜。
- **投影/更新：** 插件在 snapshot/revision 变化时构建 projection；主线程发布。Host 缓存按 provider registration、page identity、projection、theme version、language/locale、width/metrics 失效。现有 ApiDirectory 没状态订阅，本轮不能声称 renderer 可依靠现成 directory Changed；可先打开/刷新捕获，通知 contract 属 bounded gap。
- **视图状态：** Host view session 保留稳定 row ID selection、scroll anchor、control names、focus与字段草稿；plugin 拥有 authoritative values/校验规则/操作。刷新不覆盖正在编辑的有效草稿；条目消失显示 stale 或关 editor 并解释，不能 silently retarget。IMGUI control ID 稳定性要保留 Chat 已有 boundary/refocus。
- **校验：** Host 负责有限格式/输入长度及呈现；插件 commit 前重验数量、权限、provider/context、余额。field errors 可绑定 ID，错误 text/code 分离；测量/Draw/Collect 不执行业务写入。
- **动作：** Host 将明确输入事件投影为 intent 给 owning handler；action collection 可以复用 `IClientActionPoint<TContext>`；business context 属定义点的插件 Contracts，Host 只读通用 action metadata。先 revalidate exact registration/context，disposed/stale 禁用，不自动 retry Invoked failure。task Succeeded 只“回调完成”；Confirmed 必须由业务 projection 给。
- **pending/errors：** Host 可持有正在呈现的 task/action handle；实际 operation IDs/status/Unknown 属业务。关页面取消 view request 和释放 collection不意味着撤回已执行交易/库存副作用。错误无数据泄漏的普通说明+高级 diagnostics；避免每帧反复 callback/log。
- **主题：** 三路径均使用同一 Capture palette/version/registry。Host shared role 可以覆盖默认外观，但 plugin 自己注册的显式颜色优先并有缺键策略；TryGetColor 用于真正 optional keys，GetColor 未注册会抛。custom 不能建立独立主题文件或绕过编辑/hot apply。
- **third-party composition：** 实际业务 owner 定义 typed context/slot，第三方 module-bound contribution；Host 提供通用排序/容器/actions，不引用 Chat Contracts。当前 action scope 是 operation composition，不是 content slot；可组合 sections/rows/message renderers 需新 bounded contract。
- **lifetimes/failure：** 注册与 scope 在 Activate 绑定 actual module；view session 拥有 subscriptions/action sets/cache/custom assets leases，关闭/替换/停止释放；Host borrowed service/API 不 Own。late callback guard、exact registration、generation/revision 检查；provider stop 不 resurrect旧 handle。失败区域给错误占位，恢复 GUI color/enabled/font/anchor/wrap、scroll/group/clip；缩放/measure exception 不阻断其余 providers。
- **custom region：** 约定 size/measure、clip Rect、hitbox/input ownership、theme reader、revision、dispose、main thread；draw 不越界、不更改全局 GUI 后遗漏恢复。Unity textures/GUIStyles 缓存并按资产 owner 释放；控制 custom renderer 的调用/failure，不能保证任意第三方恶意代码隔离。
- **IMGUI 性能：** Layout/Repaint 多 pass 不重复 registration、subscriptions、disk access、network、business action。immutable descriptor 不按每事件重建；虚拟 list 只 materialize visible rows，变高 offsets 缓存；主题应用不得重新扫描所有 game Things。当前 snapshot Capture O(取引用)可复用，不把每帧全部字典复制作为设计。

### 9.3 具体 rendering/API 缺口

| 需求 | 已有接口 | 缺接口设计 | 缺 Host 实现 / 实际迁移 |
| --- | --- | --- | --- |
| 稳定导航、可选 provider 刷新 | IMainTab/Sidebar、ApiDirectory、responsive hints | page/provider identity 与变化机制（现 tab 无 ID/通用图标元数据） | shell registration-aware session、dynamic selection/overflow/failure；全部 tab 消费者适配 |
| 标准 form/list/detail/state | ClientWidgets、geometry helpers | bounded read-only descriptors、field IDs、draft policy、state/error/intent | measure/render/session/cache；Inventory/Store/Example 等仍 imperative |
| 成员/消息/交易工具栏 actions | typed action service、owned sets | 各业务 public immutable typed context/点名，必要 state/result投影 | UserList/MessageList/Trade/Inventory 未已接入完整 typed menu；Example 已接入自己的点 |
| plugin content sections/cards/message renderer | main/sidebar/banner Draw | neutral content slot/owned renderer lifecycle；业务 context 留插件 | registry、measure/draw/clip/failure、fallback renderer；真正 message integration 未实现 |
| 通用 confirmation/selection | Verse dialogs、FloatMenu、action guard | owner-bound request identity、expiry/validation、result/cancel语义 | Host dialog rendering与 view session；业务 commit 同样重验 |
| typed operation presentation | Task snapshot / contributed result、business APIs | business projection状态映射/不可假 confirmed | reusable status renderer，Trade/Store owner adapters |
| metrics/theme styles | theme registry/readers/version floats | 是否受控 metrics 与role扩展；不是已有任意布局 theme | rounded assets/shared styles、measure失效、旧主题 fallback |
| module resource/custom assets | Lifetime Own、Task、localizer | module file/resource/storage 公共能力仍不完整 | 不声称已可从任意磁盘读 assets；custom asset 生命周期与来源隔离 |

### 9.4 后续 Example 的代表性实验（不是实施计划）

选择现有 **Example 计数与操作**：插件给一个有稳定 ID 的计数 summary、一个整数草稿字段、增加/重置动作及零计数 disabled reason；Host 画 section/form/status/actions/确认 shell。继续使用 ExampleActions 的真实 context、generation guard、owned collection和现有同步 count 保存，任务演示单独标“回调结果”。切主题/语言/窄屏、关闭确认后 stop、provider缺失、stale revision、模拟失败均可说明 public semantics，不需要先制造真实交易业务。该实验需新的 descriptor/session renderer；当前 Example 自己画 FloatMenu 不构成 Host composition 已通过。

## 10. 每类重设计的扩展与主题保留评估

以下逐项覆盖导航、component replacement 与主要交互。所有候选新键/API仅为审计建议，未注册/实现。

| 建议 | 受影响现有 tokens/contracts/providers | 插件 styling/content、缺失/过期/失败 | 已有可满足 / bounded extension | 能力缩减或耦合风险 |
| --- | --- | --- | --- | --- |
| 顶部 pill 导航 + utility 设置/插件管理 | ui.*、main/IBadge/accept/hints、Host windows | 任意 tab 按顺序/identity进入 overflow；空集显示 Host 设置；stale provider重验 | components/geometry已有；stable IDs/icon metadata/session缺 | 固定官方五项或只给官方图标是缩减；trade“2”不是当前badge能力 |
| 成员侧栏/窄屏 drawer 重排 | sidebar/banner/hints、chat.*、ui.surface | 保留所有 sidebar providers及其tab；无 Chat 不显示伪成员页；失败占位 | 当前 drawer已有；shared content composition缺 | 只画 member panel 会删除 Notice/第三方内容；设置不可依赖 Chat |
| rounded Button/Field/Card/row 替换 | ClientWidgets、control/theme contract、module keys | plugin自有 key+semantic defaults；optional key fallback；hot apply同palette | widgets与registry已有；assets/roles/states扩展 | Primary→amber不该改变ui.success；不可 hardcode官方色 |
| composer/message层次 | main/accept、chat message/reply/input/image keys | 保留reply/@/images/denied；renderer缺失以已有可读fallback，不猜payload | 现Chat行为与缓存已有；special message projection/custom slot缺 | 新上传/反应/送达，删旧状态，Host引Chat业务是越界 |
| 成员 action菜单复用 | Chat contexts、action/lifetime/ApiDirectory、accent/hover | contributions sorted/disabled；缺Trade保留block；stale释放/重验 | typed registry已有；Member point/context/view integration缺 | 固定trade/block菜单限制第三方，借用API错误Dispose风险 |
| Trade窗口/list/数量确认 | trade.*、Inventory borrowed APIs、responsive/typed actions | styling/third-party actions由owner context；provider缺失有理由；Unknown不重试 | 通用controls已有；presentation+托管业务迁移缺 | Host持Thing/决定确认/返还是业务耦合，改重置是高风险 |
| Inventory row/editor/恢复状态 | inventory.*、business/settings/quick APIs | 自有语义色保留；entry stale→禁用/解释；故障保持read-only | sharedfield/status可补；owner snapshots/intent绑定 | renderer自ApproveRecovery、删预留、auto rollback不可接受 |
| Store cards/detail、主题包入口 | store badges/ui.*、themePackage service、main/settings | optional Store缺失不影响theme selection/editor；plugin停止关view，digest变化拒绝 | 现package service/controller/布局已有；shared presentation缺 | fixed first-party tab、忽略来源/重启、绕过transaction是缩减 |
| 统一设置容器/主题预览 | settings/quick、themeCatalog/window/registry/packageExporter | 未安装插件键仍保留；absent provider跳过，fault isolated；preview同renderer | catalog/quick measure已有；declarative fields缺 | 删Catalog Import/Export、theme功能仅Store可见、扁平化unknown/inherit是回归 |
| maintained plugin UI/component替换 | RedPacket/Talent ownedkeys、main/settings、internaladapter | current公共renderer；old适配留插件；owned custom pawn/item region | responsive/主题adapter已存在；逐view迁移缺 | “Legacy”藏起来/删Talent、承诺oldclient支持newtheme、长期两框架默认均需review |
| common状态/确认/选择 | ui.pending/error/success/warning、Task/action/lifetime | state由owner给；stale不执行，closed释放，不撤销业务 | callbacks/guard已有；state descriptors/dialog lifecycle缺 | 把Completed当Confirmed、cancel当refund、exception吞掉是假成功 |

## 11. Unity / Verse 技术可行性

本地 `About.xml` 声明 RimWorld 1.6、Harmony 依赖；Client csproj 目标 .NET Framework 4.7.2，Verse/Unity/IMGUI 引用。具体安装的 Unity/Mono patch build 本轮未验证。不要由测试工具 net10 推断游戏可用 net10 API。无 WebView/CSS；Figma auto layout 需转换为现有 Rect measure/geometry，不移植网页 engine。

| 特征 | 分类 | 说明 |
| --- | --- | --- |
| 色彩/字体层次/spacing/静态dividers/成员搜索placeholder | straightforward | existing theme reader/Widgets/cache即可；读写业务不变 |
| provider排序、tabs overflow、narrow drawer、virtual rows | straightforward（基础已有） | 视觉改编仍需动态 provider 和状态审查，不能把逻辑已存在说成设计已验收 |
| 圆角框/卡片/pill、浅静态shadow、rounded hover | custom drawing / 9-slice / cached textures | 有限radius、复用素材，按UI scale；禁止每行每帧生成Texture2D |
| inline stat/trade cards | 画法可行，数据/集成缺口独立 | current protocol/plugin必须实际提供snapshot；不靠renderer构造权威信息 |
| 精确多字体、复杂可变字重、逐像素网页排版 | feasible but not worth complexity（当前阶段） | 字体resource、跨平台fallback、中文measurement、许可/包能力都需额外工作；Verse层级先满足可读性 |
| 多层细shadow/内阴影完全匹配、丰富动画 | feasible but not worth complexity | 可预绘静态边缘；没有 evidence表明值得runtime成本 |
| blur/glassmorphism、动态背景模糊/粒子、大量即时材质 | should be avoided | 用户明确约束；alpha surfaces不是blur，暖色方案无需这些效果 |
| HTML/CSS/WebView、通用声明UI engine | should be avoided | 不符合runtime/本轮有界模型；descriptor不等于DOM |
| 大列表逐帧建投影/排序/图像纹理/文件扫描 | should be avoided | current缓存/虚拟化应保留；按版本/语言/宽度失效，render只访问snapshot |

性能注意：主题包小同步I/O在操作入口（非render）已有实现，但慢盘耗时未测；audit不能声称无卡顿。image decode/texture在主线程使用已验证dispatch，缓存与释放需owner负责。GUIStyle如果新增应有明确缓存/失效与shared style不原地污染规则。异步主线程callback不是异步网络API，不能调用它阻塞等待业务。

## 12. 粗分类建议（无时序、无里程碑）

### 12.1 Quick wins

基于现有 API 的 placeholder、空白/零结果/断线状态文案；区分连接快捷设置与完整客户端设置；标明编辑主题与已应用主题；安装与应用结果分开；消息/成员动作可发现性；普通错误说明+高级诊断；有效存在的 disabled reason；将颜色-only状态加文本；保留所有provider入口与窄屏fallback。这些多数是U，小部分需C；并非“无需验收”。

### 12.2 Medium scope

扩展同一 ClientWidgets 的 rounded/semantic controls、shared states、稳定 shell navigation/overflow/failure/session、bounded list/form/detail/confirmation descriptors、typed member/message/trade actions的真实view integration、ownedcustomregion边界、共享主题预览、插件投影适配及字段draft/validation。大多数C/P/B；不需要先增加服务器能力。Trade托管迁移的领域部分必须另标高影响，不能用medium UI包装。

### 12.3 Long term / protocol-dependent

真实可用的 presence、消息送达/幂等重试、交易结果未知核对/重置确认/跨会话恢复、真实房间管理权限/服务器设置、丰富消息内容的数据与协议集成。并非都一定改协议：先判 owning plugin 可在当前wire下给多少能力。线上theme admission/publication仍延期；不属于当前重设计的必要前置。新社交功能明确不提议。

## 13. 真正需要产品决策的问题

1. Figma物品/贸易/战报卡：是现有插件内容的未来通用呈现示例，还是明确想成为聊天能力？若后者，哪个plugin拥有数据及广播/关联语义？在答案前仅保留视觉参考，不加入行为。
2. 设置是否合并导航，或保留快捷/完整入口？最低要求是Host Theme/extension独立可达、任意third-party sections保留；不能只让Chat显示设置。
3. Store关闭时，是否仍要用户安装/卸载ZIP主题的Host视图？现Host package service支持，current交互在Store；保留selector/editor/export不代表已有Host安装UI。
4. 是否接受radius/spacing初期为固定受控metrics，仅配色与已注册float可编辑？若要density/字体定制，要明确哪些维度/范围与providermeasure约束，而非开放所有尺寸。
5. rich-text昵称和hash颜色偏好是否继续可见？这是已有用户能力，单色参考不能默认为授权删除。
6. 移除Figma Legacy标签、window三按钮、unverifiedpresence/加密文案，还是为其中某项另立真实能力需求？默认审计建议保留功能名称及当前window语义，不加假状态。
7. Host composition先覆盖哪些真实类别的公共contract（如field/list/action/state），customregion允许哪些内容？原则建议有界、同等开放，不以每项已有官方消费者为前置；是否对新作者倡导descriptor而保留现provideradapter需要明确迁移承诺。

不需要再次决策的事项：主题永久能力、插件同级开放、现有resolver/version边界、oldadapter维护、线上延期、audit-only都已在用户请求中确定。

## 14. 未验证事项与后续证据需求

未验证：实际游戏入口/视觉、候选DLL与源码一致性、1.6的实际Unity/Mono patch、multiscale/三端文件位置adapter、同批theme/editor/store热应用、关闭/stop时GUI与callback清理、主题安装中断恢复、TradeReset与所有权迁移、1.9 maintainedplugin路径、大数据量帧耗时。没有将这些写作“测试通过”。当前项目既有 Theme/Responsive/Chat/Trade/Inventory/Store runtime tests提供部分非GUI证据；本轮未运行，不能替代游戏。

另缺Figma设计证据：其他tab详情、所有loading/error/empty/permission/focus/disabled states、长翻译/overflow/sidebar多provider、themeeditor/packageflow。单frame能评估方向，不能冻结完整设计系统。上述缺失不影响当前audit报告交付，但影响未来具体实现评审。

## 15. 源码与文档证据索引

索引链接针对**本地当前文件**；dirty/untracked是本次证据的一部分，不以HEAD内容代替。行号如后续代码变化可能漂移，方法名用于定位。

- **E01 主按钮与badge**：[ServerTabButtonWorker.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/GUI/Windows and panels/ServerTabButtonWorker.cs:18>)。

- **E02 Host shell**：[ServerTab.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/GUI/Windows and panels/ServerTab.cs:189>)；[IMainTabProvider.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/UI/IMainTabProvider.cs:5>)；[IServerSidebarProvider.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/UI/IServerSidebarProvider.cs:5>)。

- **E03 Chat composer**：[ChatMainTabProvider.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Chat/Client/ChatMainTabProvider.cs:91>)。

- **E04 Chat submission/message rendering**：[ChatMessageSubmissionAdapter.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Chat/Client/ChatMessageSubmissionAdapter.cs:16>)；[ChatMessageList.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Chat/Client/ChatMessageList.cs:1159>)。

- **E05 成员/搜索**：[ChatSidebarProvider.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Chat/Client/ChatSidebarProvider.cs:60>)；[UserList.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Chat/Client/UserList.cs:229>)。

- **E06 Trade**：[TradeList.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Trade/Client/TradeList.cs:103>)；[TradeWindow.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Trade/Client/TradeWindow.cs:459>)；[TradeWindow.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Trade/Client/TradeWindow.cs:158>)。

- **E07 Inventory**：[InventoryTab.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Inventory/Client/InventoryTab.cs:83>)；[InventorySettingsPanel.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Inventory/Client/InventorySettingsPanel.cs:29>)；[InventoryDeliveryChoiceDialog.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Inventory/Client/InventoryDeliveryChoiceDialog.cs:9>)。

- **E08 Store**：[ManagedPluginStoreView.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/PluginStore/Client/ManagedPluginStoreView.cs:520>)。

- **E09 主题公共契约/runtime**：[IClientTheme.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/UI/IClientTheme.cs:10>)；[UiTheme.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/UI/UiTheme.cs:186>)；[ThemeInputAdapter.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/UI/ThemeInputAdapter.cs:62>)；[ThemeLoader.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/UI/ThemeLoader.cs:16>)。

- **E10 主题编辑与导出**：[ThemeSettingsWindow.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/UI/ThemeSettingsWindow.cs:88>)；[ThemePackageExportWindow.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/UI/ThemePackageExportWindow.cs:12>)。

- **E11 主题包本地闭环**：[LocalThemePackagesWindow.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/PluginStore/Client/LocalThemePackagesWindow.cs:34>)；[ClientThemePackageService.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/UI/ClientThemePackageService.cs:48>)；[ManagedThemePackageStore.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Dependencies/Phinix.Common/Common/Utils/Framework/Themes/ManagedThemePackageStore.cs:127>)；[ThemePackage.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Dependencies/Phinix.Common/Common/Utils/Framework/Themes/ThemePackage.cs:20>)。

- **E12 Host management/公共组件**：[ManagedExtensionManagerView.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/Framework/ManagedExtensionManagerView.cs:11>)；[ClientWidgets.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/UI/ClientWidgets.cs:66>)；[Displayable.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/GUI/Displayable.cs:8>)。

- **E13 作者API/Example**：[IClientExtensionPoints.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/Framework/IClientExtensionPoints.cs:8>)；[ClientExtensionPointService.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/Framework/ClientExtensionPointService.cs:81>)；[IClientApiDirectory.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/Framework/IClientApiDirectory.cs:6>)；[IClientTaskService.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/Framework/IClientTaskService.cs:8>)；[IClientLifetimeService.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/Framework/IClientLifetimeService.cs:7>)；[ExampleActions.cs](</home/hunyuan2333/Phinix/Phinix-Example-Plugin/example/ExampleActions.cs:10>)；[Api-Semantics-Example.md](</home/hunyuan2333/Phinix/Phinix-Example-Plugin/Api-Semantics-Example.md:33>)。

- **E14 客户端设置**：[SettingsWindow.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/GUI/Windows and panels/SettingsWindow.cs:122>)；[Client.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/Client.cs:586>)；[ChatSettingsPanelProvider.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Extensions/Chat/Client/ChatSettingsPanelProvider.cs:33>)。

- **E15 连接及版本**：[Client.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/Client.cs:768>)；[Client.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/Client.cs:27>)；[AssemblyInfo.cs](</home/hunyuan2333/Phinix/Phinix-Rework/Client/ClientExtensionAbstractions/Properties/AssemblyInfo.cs:15>)；[Client.csproj](</home/hunyuan2333/Phinix/Phinix-Rework/Client/Source/Client.csproj:12>)；[About.xml](</home/hunyuan2333/Phinix/Phinix-Rework/Client/About/About.xml:10>)。

- **E16 维护legacy消费者**：[RedPacketTab.cs](</home/hunyuan2333/Phinix/Phinix-Legacy-RedPacket/Client/RedPacketTab.cs:22>)；[ThemeAdapter.cs](</home/hunyuan2333/Phinix/Phinix-Legacy-RedPacket/Client/Compatibility/ThemeAdapter.cs:14>)；[TalentTradeTab.cs](</home/hunyuan2333/Phinix/Phinix-Legacy-TalentTrade/Client/TalentTradeTab.cs:7>)；[ThemeAdapter.cs](</home/hunyuan2333/Phinix/Phinix-Legacy-TalentTrade/Client/Compatibility/ThemeAdapter.cs:14>)。

### 文档优先级与最新状态

- [Design-Philosophy.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/Design-Philosophy.md>)
- [Compatibility-Boundaries.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/Compatibility-Boundaries.md>)
- [Theme-Package-T5-Implementation.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/branch-local/dev/Theme-Package-T5-Implementation.md>)
- [Plugin-Api-Integration-Standard.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/branch-local/dev/Plugin-Api-Integration-Standard.md>)
- [Provider-and-Lifetime-Api-Implementation.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/branch-local/dev/Provider-and-Lifetime-Api-Implementation.md>)
- [Theme-and-Plugin-Publishing-Flows.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/branch-local/dev/Theme-and-Plugin-Publishing-Flows.md>)
- [UI-Evaluation-Handoff-Checklist.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/branch-local/dev/UI-Evaluation-Handoff-Checklist.md>)
- [UI-UX-Refactor-Delivery-Gates.md](</home/hunyuan2333/Phinix/Phinix-Rework/docs/branch-local/dev/UI-UX-Refactor-Delivery-Gates.md>)

## 附录 A：源码注册的主题 key 清单

这是当前注册调用的静态提取；不包含用户文件中未注册的 unknown keys，也不把兼容 alias 当 canonical 新键。颜色默认与 fallback 的具体值请按链接检查。

### Host

Color：`ui.text.primary`、`ui.text.secondary`、`ui.background`、`ui.surface`、`ui.separator`、`ui.hover`、`ui.pending`、`ui.error`、`ui.success`、`ui.warning`、`ui.accent`、`ui.button.background`、`ui.button.hover`、`ui.button.pressed`、`ui.control.disabled`、`ui.input.background`、`ui.input.focus`、`ext.statusActive`、`ext.statusFailed`、`ext.statusDisabled`、`ext.statusDependencyDisabled`、`ext.statusRegistered`、`ext.statusDefault`、`ext.logDiagnostic`、`ext.logWarning`、`ext.headerText`、`ext.summaryText`。

### Chat

Color：`chat.messageText`、`chat.inputText`、`chat.mentionText`、`chat.mentionSelfBg`、`chat.selfName`、`chat.selfMessageBg`、`chat.rowHoverBg`、`chat.groupIndentLine`、`chat.replyQuoteBorder`、`chat.replyQuoteBg`、`chat.replyQuoteText`、`chat.noticeAccent`、`chat.noticeBg`、`chat.noticeBannerBg`、`chat.noticeProgress`、`chat.inputReplyBorder`、`chat.inputReplyBg`、`chat.blockedBg`、`chat.blockedName`、`chat.pendingMessage`、`chat.deniedMessage`、`chat.imagePlaceholderBg`、`chat.imageFailedText`。

Float：`chat.nameColor.saturation`、`chat.nameColor.value`。

### Trade

Color：`trade.ourOfferAccent`、`trade.theirOfferAccent`、`trade.ourOfferBg`、`trade.theirOfferBg`、`trade.cancelButton`、`trade.acceptedBadge`、`trade.pendingBadge`、`trade.rowHoverBg`、`trade.panelBg`、`trade.searchPlaceholder`。

### Inventory

Color：`inventory.infoBackground`、`inventory.infoAccent`、`inventory.editorBackground`、`inventory.noticeBackground`、`inventory.errorBackground`、`inventory.selectedBackground`、`inventory.alternateBackground`、`inventory.choiceBackground`、`inventory.directDeliveryAccent`、`inventory.storedDeliveryAccent`。

### Store

Color：`plugin-store.badge.official`、`plugin-store.badge.managed`、`plugin-store.badge.workshop`、`plugin-store.badge.local`。



## 2026-10-12 范围覆盖说明

本审计为历史状态。最新源码/验收以 roadmap §16.13 为准；用户已明确不要求兼容无人使用的旧主题数据/旧 UI 协议。保留当前主题能力与明确维护的红包/人才贸易 1.9 adapters，不添加新 UI legacy mode。B6 fix 外框/文字仍有用户截图反证，B7 源码修正未游戏复测。


2026-10-12 B10 实机反馈更正：B9 商店单列投影的 UX 被用户截图否定，不能因公共 API 迁移/编译完成宣称视觉通过。现实现改为公共双栏、独立滚动与窄窗双入口；源码/检查完成，游戏待测。最新状态见 roadmap 16.17；聊天时间戳位置待办见 16.18。


2026-10-12 B12 用户再次否定商店整栏滚动 UX：改为全局工具/搜索筛选/插件头部操作固定、目录与正文滚动；恢复原维护/来源 badge，去除空摘要卡片。公共固定区域与 badge 能力同批维护 Host/Example；最新状态见 roadmap 16.20，游戏待测。


2026-10-12 B13：用户否定 B12 卡片密度、工具层级和徽章辨识度；商店采用公共 plain sections、owned action menu 分组、对齐双栏、四个注册 badge 色角色及原图标。源码/编译与非 GUI 完成，实机视觉待测。最新状态见 roadmap 16.21。


2026-10-12 B15 响应式核对：共享导航/composer/确认/footer/徽章/metadata/表单及 Settings owner docking 修复见 roadmap 16.23。源码/编译与非 GUI 已检查，游戏待测。固定 icon 点击区不自动等于缺陷；尚未迁移的 private Draw 不宣称已全量重排。


2026-10-12 B17：B16 可见时间/导航排版获用户确认；本地插件 ZIP 与本地主题包视图迁移同一公共 Host，单栏固定头与 Example 1.0.24 同批，见 roadmap 16.25。源码/编译与隔离检查通过，新增业务游戏待测；主题 editor/preview/export 仍待迁移，Trade 门槛不跳过。


2026-10-12 B19：主题 Editor/Selector/Export 已消费同一公共 Host；slider/swatch 与 Example 1.0.26 同批。单向赠送合法空 direct completion 缺口修复，原非空交付校验/ACK 边界不变。源码/编译/隔离检查通过，游戏/T5 恢复专项待测，最新状态见 roadmap 16.27。


2026-10-12 B20：Trade list/selection/cancel 使用同一公共 Host，完成 ID 过滤修复，Example 1.0.27 同批。147 stub/127 actual DLL assertions 通过，游戏待测。Reset 的前返还和 Window pending owner 是未过门槛，不以列表迁移替代报价整批完成；最新见 roadmap 16.28 与 Trade-R44-Semantics-Gate-2026-10-12.md。


2026-10-12 B21：Trade pending 由插件 operation owner 持有，Reset 确认后返还、local settlement 失败保留、共享 facade 冲突拦截；公共恢复投影/确认和 Example 1.0.28 同批。180 stub/134 actual DLL checks 通过，游戏延期合批；跨存档 escrow 和完整报价 Host renderer 仍未完成，见 roadmap 16.29。
