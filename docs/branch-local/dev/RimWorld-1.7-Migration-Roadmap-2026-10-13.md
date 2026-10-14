# RimWorld 1.7 迁移 Roadmap

起始日期：2026-10-13；最新状态：2026-10-14。**两版 API3 本地联合候选 local-api3-20261014-05 已重建（保留加载修复，纠正1.7描边坐标），目录/绑定/语言白盒通过；1.7 第一轮游戏待测，正式双版本插件 ZIP 仍待实施。**

本文件承接本轮真实源码、DLL 与 Player.log 评估。最初仅评估与规划，用户随后明确授权逐步实施，并在设计哲学复核后确认继续。优先新 API，1.6 由独立 adapter 保留；发现业务语义泄漏先收束。不使用旧版本 skill。当前实施仅修改可审查源码与检查工具，不部署正式包、不修改存档、不提交、推送或发布。

## 1. 目标与完成定义

在保持业务权威、公共能力和 1.6 可复现的前提下，把当前公共呈现迁到 1.7 新后端。所有内置与第三方插件继续消费同一套 presentation/theme/action/confirmation；1.6 与 1.7 只在引擎和游戏接入处区分。

2026-10-14 用户补充的最终发行约束：**Mod 本体及每个插件各自一个产品版本、一个发行包，同时支持 1.6/1.7。** 两个游戏目标不是两个产品分支；共享同一业务源码、API 主版本、模块 ID、主题和动作语义，各自编译 net472/net10 DLL。正式插件包选择双 payload 单 ZIP，由共同包读取/校验与 Host 选择当前游戏变体；商店推送暂缓，优先全部本地闭环。分版 ZIP 仅为当前本地校验中间产物，不能作为最终双版本插件方案的完成证据。

移除 1.6 的成本必须限制在：删除 1.6 adapter/引用/构建目标/包目录及对应 profile/测试矩阵；无需改业务、公共呈现模型或每个功能页面。普通业务/UI 不新增判断游戏版本或目标框架的分支。不为了当前双版本要求增设旧客户端 API 兼容分支。

当前实物：游戏 **1.7.4977 rev1248**；公共 SDK 与中立呈现合同 CLR identity 均为 **3.0.0.0**，两版共享 API3。Host 源产品版本为修复提交带来的0.10.1，本地候选不是正式0.10.1发布；正式插件 pin、商店记录和历史发行版本未改。详见 [本地联合候选及 G1 测试](RimWorld-1.7-Local-API3-Cohort-2026-10-14.md)。下表保留原评估起点，不代表当前安装与实现状态。

完成须同时满足：

- 已确认的 Host 共享契约业务泄漏移回业务插件；Host 不管理 Thing/Pawn、报价托管、账本或业务协议。
- 1.7 公共绘制不再依赖已证无效的旧边框重载；字体、输入、焦点、裁剪、滚动、图像和窗口行为有真实游戏证据。
- 公共契约不再要求普通呈现消费者直接使用 Unity/Verse/Waylith 类型；确需游戏对象的接口留在插件各自的游戏 adapter 边界。
- 新 API 在同一迁移批次维护 Host、Chat、Inventory、Trade、主题、商店、红包、人才与 Example；普通第三方入口具备相同能力。
- 1.6 独立 adapter 与 1.7 adapter 使用同一公共模型、主题和动作语义。1.6 正式历史制品可重现，新 adapter 另行回归；历史游戏验收不自动覆盖新 adapter。
- 完整构建图、程序集绑定、候选包和游戏测试通过。编译或假 GUI 白盒通过不能代替游戏通过。

不纳入本路线：R44 持久托管、Reset 权威回执、乱序恢复等后续业务增强；服务器改造；存档格式、协议标识、packageId 改名；旧客户端组合支持；第二 renderer/第二主题/第二动作系统；全面游戏对象抽象；纯装饰重设计。

## 2. 起点：已经知道什么

| 事实 | 证据与限制 | 状态 |
|---|---|---|
| 当前正式基线 | Host 产品 0.10.0；红包 1.2.2、人才 1.2.1、Example 1.1.0；插件固定 Host `783fcb0bb6ad56b97e8ca5875457babfe6520c25`、Common `6a1320bcfd6e17f5d4e46efcb3c021b6f132a37e` | 正式记录已核对；仅有 1.6 收口验收 |
| 上轮游戏运行环境 | 当时本地 **1.7.4976 rev1043**；Waylith、UnityStubs、Brutal.ImGui；定制 `.NET 10.0.10-waylithclr.dev`；Harmony `3.0.0.0-rc.2` | 日志、runtimeconfig 与 DLL 元数据已核对 |
| 上轮核验并缓存的 1.7 DLL | Version.txt 当时为 **1.7.4977 rev926**；Assembly-CSharp `1.7.9782.27799`，游戏/UnityStubs 指纹较 4976 已变化；Harmony 哈希未变 | R17-10 隔离编译/非 GUI 白盒通过；4977 游戏待测，旧后端行为需按新指纹复核 |
| 当前安装目录 | Version.txt **1.6.4871 rev598**；Managed Assembly-CSharp `1.6.9676.18020`，SHA `082db1…`；不覆盖旧 GameDlls | 元数据/哈希已核对；本轮 1.7 检查使用同哈希的 4977 隔离缓存 |
| 可用 1.6 DLL | GameDlls 是 **1.6.4633 rev1261**；历史测试输出另有 **1.6.4871 rev600**，对应旧运行日志 | 两份保留；较新引用缓存不完整 |
| 描边重载 | 新 `UnityStubs.GUI.DrawTexture` 忽略 borderWidths/rounding，输出整块矩形；旧实现传给 native texture draw 参数 | 源码检查、真实 DLL 白盒复现；当前游戏画面失败 |
| 输入组合状态 | 新 `Input.compositionString` 恒空，相关 IME 属性是 stub；仍有字符输入路径 | 源码检查、getter 白盒通过；真实 IME 待测 |
| Verse 核心 | 与 4871 对照，Window/WindowStack 基本延续；ScribeLoader 和 PawnTextureAtlas 所查实现一致 | 源码检查；不等于完整游戏兼容 |
| 签名/构建 | Host 与正式三插件的 500 个不同游戏/Unity/Harmony 成员结构签名存在；11 个隔离 net10.0 项目 0 errors | 编译通过；复用部分既有依赖，尚非完整发布构建图 |
| 当前游戏路径 | Host 初始化/更新、认证、已有 Chat 显示路径实际执行；首轮三个旧候选被预检拒绝，随后日志记录移除 | 仅这些窄路径有运行证据；UI 失败，插件业务未验收 |
| 首个 Phinix 异常 | `StoreReleaseNoticeFailed: NullReferenceException`；捕获层没有保留原始 throw 栈 | 源码范围已定位；根因待补证，不能猜成字体或 WindowStack |

评估依据为本地制品与反编译，不冒充官方文档，也不推广到所有 1.7 构建。游戏或 Harmony 更新后重验指纹与受影响路径。

正式发布记录：[UI-UX-Official-Releases-2026-10-13.json](UI-UX-Official-Releases-2026-10-13.json)。1.6 收口状态：[原 UI/UX roadmap §16.52](UI-UX-Redesign-Roadmap-2026-10-11.md#1652-正式插件发布与当前客户端范围2026-10-13)。完整本地评估另存于本次分析目录；本路线自身保留必要依据，不依赖临时反编译文件才能理解。

## 3. 目标依赖与所有权

| 层 | 拥有 | 允许依赖 | 边界约束 |
|---|---|---|---|
| 公共呈现契约 | 页面、字段、列表、feed、输入意图、动作、确认、主题值、借用图像及受控自绘能力 | 游戏无关契约/基础库 | 不执行 GUI、不引用业务插件、不暴露原始引擎对象 |
| Host 公共 renderer | 布局、测量缓存、会话、裁剪范围、动作 freshness、确认视图 | 公共契约、通用 engine backend 接口 | 一个实现体系，所有消费者平等；不裁决业务成功 |
| Host engine adapter | 绘制、字体、输入、焦点、窗口、图像接入、退出/主线程接入 | 对应版本的 Unity 或 Waylith/UnityStubs/Verse | 每个游戏进程只加载其有效版本；不外泄 native handle |
| 业务插件 | 校验、协议、托管、账本、物化与交付权威 | 自有契约、公共 Host 服务、自有 game adapter | 关闭视图不取消业务，callback/task 完成不等于回执 |
| 插件 game adapter | Thing/Pawn/Scribe、地图交付、人物肖像及游戏生命周期接入 | 对应版本游戏 DLL、自有业务接口 | 保留在插件，不搬到 Host；不抽象掉必须保留的完整状态 |

1.6 adapter 是引擎适配，**不是旧客户端 API 1.9 兼容分支**。外部协议/保存读取 adapter 与引擎 adapter 分开，原有内部历史 adapters 不构成新包的旧客户端支持承诺。

保留 Verse 窗口壳层是可行的过渡实现：所查签名和核心流程仍在。是否替换具体 shell 方法由新后端行为证据决定，不预设整套窗口都要重写。

## 4. 阶段与依赖

| 阶段 | 任务编号 | 交付 | 开始条件 | 结束门槛 | 当前状态 |
|---|---|---|---|---|---|
| P0 基线与诊断 | R17-00～03 | 两版引用指纹、完整构建清单、异常诊断依据 | 已有评估 | 基线不被覆盖，完整引用来源明确 | 评估完成；工程化待实施 |
| P1 业务边界 | R17-10～12 | 所有权清晰的 Trade/Inventory 接口及调用链 | P0 | Host 无交易专用契约或业务实现反向依赖 | R17-10/11 源码/隔离编译/白盒通过；12 边界源码检查完成；游戏/完整包待测 |
| P2 公共契约 | R17-20～23 | 最小中立契约、程序集/API 版本决策、Example 迁移样例 | P1 | 当前消费者闭合，ABI/绑定范围可审查 | Bounds/RGBA、普通 image lease、主题色值、Region 与菜单源码/编译/白盒通过；其余契约与正式身份待实施，游戏待测 |
| P3 公共绘制与两版 backend | R17-30～33 | 1.7 新 API 后端、独立 1.6 adapter、公共控件 | P2 | 第一轮真实游戏视觉验收 | 待实施 |
| P4 输入与生命周期 | R17-40～43 | IME、焦点、窗口、调度与资源寿命适配 | P3 首个可运行切片 | 第二轮游戏输入/窗口验收 | 待实施 |
| P5 消费者联合迁移 | R17-50～54 | 内置、主题/商店、三插件和 Example 全部使用同一当前 API | P2 后可做模型迁移；完成依赖 P3/P4 | 无残留旧 GUI 公共入口、无官方特权 | 待实施 |
| P6 插件游戏边界 | R17-60～63 | Scribe/交付/存档/头像的两版接入与验证 | P1，可独立审查；游戏验证依赖 P4/P5 | 第三轮真实业务与存档验收 | 待实施 |
| P7 构建和候选冻结 | R17-70～73 | 完整两版构建图、联合候选、包/程序集检查 | P3～P6 | 准确版本与指纹、所有检查可复现 | 待实施 |
| P8 联合验收 | R17-80～82 | 明确通过/待测的游戏矩阵与交接记录 | P7 候选 | 指定平台/构建/组合全部满足支持范围 | 待测 |

主顺序：P0 → P1 → P2 → P3/P4 → P5 → P7 → P8。P6 的静态审查可在 P1 后开展，业务实现只在发现确切变化时修改。阶段间允许为验证做普通候选，不以一次全面重写代替可审查切片。

不设未经验证的日历工期。每个切片完成源码、必要编译/白盒、指定游戏检查后更新状态；未知项不会因时间经过变成完成。

## 5. 任务清单与验收

### P0：保留基线，补齐可复现与原始异常依据

| ID | 工作与源码依据 | 验收 |
|---|---|---|
| R17-00 | 固定 Host/插件正式 SHA、产品/API/CLR 身份、ZIP 哈希；为两份 1.6 DLL 和本地 1.7 DLL记录版本/MVID/SHA256 | 清单区分正式、候选、历史 DLL；不提交游戏 DLL、反编译源码、私有引用或玩家日志 |
| R17-01 | 建立独立引用/输出目录；1.6 目标框架与 1.7 net10/Harmony3 分开；核对完整依赖图和缺失模块 | 不覆盖 GameDlls/Output/安装目录；不改 vendored protobuf pins；分析用系统运行时与游戏定制 CLR 分开记录 |
| R17-02 | `Extensions/PluginStore/Client/StoreReleaseNotice.cs` 的异常捕获和 `StoreActivationServices.cs:97` 通知路径保留原始异常上下文，核实启动时开窗前提 | 能从实际 throw 栈定位根因；捕获仍隔离失败，notice 仅真实开窗成功才标记；不吞错换取无日志 |
| R17-03 | 将目前小范围签名/白盒探针整理成必要检查；明确尚未包含 GPU、Def、Pawn/Thing 往返 | 记录输入指纹、命令、范围与结果；不把 500 签名存在写成业务兼容 |

R17-02 是后续实施任务，不是本轮已修复。诊断必要时先于其他阶段，不等待整套新 renderer 才收集日志。

### P1：先收束业务语义与所有权

| ID | 工作与源码依据 | 同步消费者与验收 |
|---|---|---|
| R17-10 | `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs:177` 的 `ITradeRequestApi` 迁入 Trade 契约；通用 Host 只保留发现与动作能力 | Trade 注册/实现、ChatSidebarProvider、BuiltInChatClientExtension 一起维护；Trade 缺失/停用时菜单明确不可用，不创建 Host 私有交易桥 |
| R17-11 | `Extensions/Inventory/Contracts/InventoryContracts.cs:370` 的投递实现迁入 Inventory 游戏边界；盘点 `InventoryMaterializationResult.Things` 与 codec 接口调用方 | Inventory/Trade/红包的实际调用保持整批校验、custody 与 Unknown 语义；中立账本接口与游戏物化接口分开，不把 Thing/Pawn 或账本交给 Host |
| R17-12 | 审查 `Trade/Contracts/ClientTradeContracts.cs:70` 的游戏接入；区分插件自己的接口和通用 Host 契约 | 名字含 Host 不作为反向依赖证据；报价与物品仍归 Trade；正常快照、回执、callback、视图关闭的语义测试不回退 |

退出门槛：列出已迁接口、实际消费者、程序集依赖和仍必须保留的游戏对象边界；不存在“业务迁入 Host”的快捷方案。仅搬定义/实现和调用归属，不借机修改协议、保存键或确认条件。

### P2：最小公共契约与版本决策

| ID | 工作 | 验收 |
|---|---|---|
| R17-20 | 以当前 `IClientPresentationService`/`IClientTheme` 为清单，设计中立 geometry/RGBA、窗口/菜单描述、输入意图；具体名称在 API 审查时确定 | 普通呈现消费者不引用 Unity/Verse/Waylith；保留现有 page/list/feed/composer/theme/action 功能，不创建泛化游戏对象框架 |
| R17-21 | 替换 `Texture2D`/`Func<Texture>` 暴露；定义 borrowed image source 的 owner、有效期、尺寸、revision、view lease 与失效处理 | Host 不拥有 Pawn/RenderTexture；仅可见绘制解析，测量不触发 GPU，释放视图不销毁插件/游戏拥有的图像；无 GPU 读回 |
| R17-22 | `IClientPresentationRegion.Draw(Rect,theme)` 改为有限 draw context；普通字段/动作承接交互，公开自绘只获得必要绘制能力 | Example.CounterPreview 用相同入口；Host 控制裁剪与状态恢复，异常后下一消费者正常；不暴露全局 GUI/事件或原始 native draw list |
| R17-23 | 决定 contracts 目标框架、assembly binding、API major、game/package 范围与两版载入布局 | 若移除/改签名造成破坏性变化，按 major 管理，不冒充 API 2.x 小更新；不靠现有 CLR 1.9 身份掩盖 ABI 改变；每个进程只载入一个有效公共契约与 adapter |

目标是两版共享一份引擎中立契约和 renderer 逻辑，adapter 用各自目标框架编译。`netstandard2.0` 等只是公共契约候选，须验证固定依赖后确定，不在规划阶段宣称框架选择已完成。不预先分配产品或正式插件版本。

第一批验证消费者用 Example 与一个真实 Chat/主题公共流程，证明契约足够；随后同批维护其他当前消费者。中间只做隔离构建，不让不完整公共 API 进入正式包。

### P3：共享 renderer，1.7 新后端与独立 1.6 adapter

| ID | 工作与 DLL 依据 | 验收 |
|---|---|---|
| R17-30 | 拆出 `ClientWidgets`/`ClientGuiScope` 的具体引擎实现；保留一个公共布局/控件体系；1.6 adapter 使用原 Unity 原语 | 核心契约/renderer 不强绑 Waylith 或旧 Unity 模块；两版无业务分支、无重复主题/动作注册 |
| R17-31 | 1.7 绘制使用现有公开 backend 新能力：`IGuiBackend.FillRectRounded`、`DrawPrimitives` 等；评估透明描边图元环 | 不再调用已证忽略 borderWidths 的重载做描边；半透明背景/描边不覆盖内容，1×/1.5×/2×缩放正确；不反射 internal ImGuiGuiBackend/private drawList |
| R17-32 | 测量/绘制使用一致字体、富文本、padding 与缩放；迁移 group/clip/scroll 与图像接入；按真实失效因素更新缓存 | 中文/长文本/菜单/嵌套滚动不溢出；热换主题或缩放后尺寸一致；可见行虚拟化与原数据更新节奏保留 |
| R17-33 | 普通 Example 与真实公共页面的最小候选，通知用户第一轮游戏测试 | 本地新 backend 的 GPU/字体/透明/裁剪实测通过再扩展；1.6 adapter 编译/白盒通过，游戏回归另列 |

公开 `IGuiBackend` 仍使用 UnityStubs 值/纹理类型，只能成为 1.7 adapter 内部依赖，不能直接升格成插件公共契约。新 API 是否足够须通过图形探针确认；缺能力时只扩展最小通用 backend 接口，不新增官方私有路径。

### P4：IME、焦点、窗口与生命周期

| ID | 工作 | 验收 |
|---|---|---|
| R17-40 | 重做输入 adapter 的 composition/commit/submit 分界；对照新字符 hook、文本编辑与 Return 消费路径 | fcitx5 选词/确认不会发消息，后续发送恰好一次；不以空 compositionString、一次文本变化或猜测按键时序替代组合状态证据 |
| R17-41 | 稳定控件身份、焦点请求、搜索/聊天切换、弹窗遮挡、Enter/Esc、滚动消费；必要时继续用 Verse shell | 同一输入只由正确控件/动作消费；嵌套滚动与窗口拖动正确；关闭/刷新不触发过期动作 |
| R17-42 | 将主线程/退出/游戏切换接入 adapter；保留现有有界队列、任务与模块 scope；图像/订阅成对释放 | 换档不写新 Game 的资源；模块停用撤销迟到回调；callback Completed 仍不确认业务；退出/重复初始化无重复绑定 |
| R17-43 | 第二轮游戏检查：composer、公共确认、主题编辑输入、本地包选择与同帧关窗 | IME、模态、动作 freshness 与生命周期有真实日志/场景证据，未测平台单独标记 |

IME 是高风险探针，P3 首个可运行切片后立即验证。如果新公开输入能力缺少所需组合状态，先调查 native/平台输入接入；不能虚构一个已存在的 composition API，也不能在插件中各自新增输入法桥。Linux 通过不等于 Windows/macOS 通过。

### P5：当前消费者同批迁移

| ID | 范围 | 必须保留 |
|---|---|---|
| R17-50 | Host shell、Chat feed/sidebar/composer、图片与菜单 | 普通贡献入口、滚动跟随/引用、现有消息/提交权威；Trade optional 能力发现 |
| R17-51 | Inventory/Trade 列表、详情、编辑、核对与确认 | 只读事实与输入意图；空报价合法；unknown/预留/整批拒绝提示；关窗只释放视图 |
| R17-52 | Theme Editor/Selector/Export、本地主题包和第三方入口 | 原 tokens/catalog/readers/draft/热应用/导出规则；同一公共 session/action；不依赖 Store 激活 |
| R17-53 | Store 浏览、管理、本地插件包选择、通知与恢复入口 | 普通插件身份、restart policy、兼容预检和包校验；Store 禁用后 Host 恢复入口 |
| R17-54 | 红包、人才、Example 生产页面/设置/窗口与借用图像 | 插件业务 authority；普通第三方自绘/主题/动作/确认；旧内部 adapter 不扩大成旧客户端包支持 |

每个消费者切片记录迁移前后功能映射与不变量；源码摘要、语言资源、格式占位符、静态引用检查随实际变更同步更新。仅有控件截图不能代替所有入口验收。

### P6：插件游戏边界与行为验证

| ID | 依据 | 验收 |
|---|---|---|
| R17-60 | `TradeThingStateSerializer`、codec、预览与物化；对照实际 Scribe 阶段 | 完整 Thing/ThingComp 状态往返；缺 Def/损坏/不支持 codec 整批拒绝；busy Scribe 不接管；异常后清理。发现已有跨版本问题另列，不误写成 1.7 新变化 |
| R17-61 | 人才 `PawnDeserializer`、`PawnScribeLoadScope`、pending return、地图交付 | LoadingVars 单次 finalize 的实际 Pawn 验证；装备/健康/年龄/关系；下架恰好返还一次；失败/unknown 数据保留，不因关窗返还 |
| R17-62 | Inventory 保存身份、GameComponent 附加/类型解析、日志恢复、地图投递；红包批量投递 | 保存/重载/换档不串账本；无地图/地图失效/飞船状态/部分物化失败不误报交付成功；Harmony 窄补丁在新版本正常安装/撤销 |
| R17-63 | 人物肖像、动物图标、远端中性图像、RenderTexture 生命周期与 atlas GC patch | 动物行为不变；只借用有效资源；切地图/返主菜单不使用过期头像；不因字段仍存在就宣布 private Harmony patch 全面兼容 |

只有确认实际行为差异才更改业务实现；有充分证据维持原实现时只调整 game adapter/引用。R44 后续增强不成为本次 UI 完成的前置任务，也不得被标为已完成。

### P7/P8：完整构建、候选与游戏验收

| ID | 工作 | 验收 |
|---|---|---|
| R17-70 | 全部生产 project graph 用各自游戏引用构建，固定 gitlinks/SDK/依赖 | 不复用未经核验的旧 Contracts 来宣称完整构建通过；不改 protobuf pin 绕过失败；两版输出隔离 |
| R17-71 | 每个版本准确 LoadFolders/manifest/game/API/assembly identity；整理 1.7 自带 System 库与旧包附带库的冲突 | 不删除 owned-loader/包预检保护来消除警告；不盲删 System DLL；不打包游戏/UnityStubs/Harmony 私有编译引用 |
| R17-72 | 冻结联合 Host+三插件+Example 候选，分别检查依赖绑定、source digest、语言、ZIP CRC、资产 hash | 候选明确支持游戏/平台/API；跨 major 旧包准确拒绝；两版有效文件不造成重复加载 |
| R17-73 | 1.6 原基线可复现与新 adapter 回归，形成证据差异 | 当前缺真实 1.6 运行环境时明确通知用户，保留未测项；不能借历史验收替代新 adapter 验收 |
| R17-80 | 第三轮：联合功能、真实 Thing/Pawn/地图/存档生命周期测试 | 采用新测试档或副本，不覆盖正式档；清楚记录版本、DLC、Mod 组合、场景与结果 |
| R17-81 | 反复加载→主菜单→换档、断线/拒绝/迟到回调、optional 插件停用、第三方 parity | 无重复交付、错误自动返还或跨档写入；新用户和升级安装流程都验证 |
| R17-82 | 支持范围与最终交接 | 未通过场景明确未支持/待测；原始日志不提交。提交/推送/发布须另有用户授权，规划不授予发布权限 |

## 6. 三轮游戏检查与通知时点

| 轮次 | 何时通知用户 | 核心检查 | 必要结果 |
|---|---|---|---|
| G1 底层显示 | R17-33 的普通候选形成后 | 描边/圆角/透明、字体/CJK富文本、测量与缩放、scroll/clip、Example自绘 | 真实新 backend 可用，当前橙色覆盖缺陷消失 |
| G2 输入与窗口 | R17-43 完成源码/编译/白盒后 | fcitx5选词/回车/粘贴、焦点切换、模态/确认/双击/Esc、本地包同帧关闭、主题热应用 | 正确的输入意图，业务确认标准不变 |
| G3 联合业务 | 联合冻结候选可用后 | 全消费者、人物返还/物品往返/地图交付、保存重载/换档/断线、optional组合 | 真实业务与存档路径满足支持范围 |

1.6 adapter 在对应 G1/G2/G3 场景做缩小但有效的回归，尤其关窗、IME、存档和交付；没有环境时保留明确的 1.6 待测门槛。每轮给用户准确联合版本和最小操作清单，不反复要求测试尚未进入普通激活流程的正式 1.6 插件包。

UI 测试至少覆盖 1024×768、1920×1080、4K 与 1×/1.5×/2×缩放，中英长文本、透明主题、热换主题/语言、长列表和画像。联机与保存测试组合限定到实际受测 DLC/Mod/平台，不笼统宣布所有组合兼容。

## 7. 记录、阻断与回退规则

每个任务保留：任务 ID、源码 SHA/修改范围、game/Harmony/reference 指纹、构建命令/输出、白盒范围、候选包 hash、游戏场景及结果。状态分别标记 **源码检查、编译通过、白盒通过、游戏通过、待测**，不以单一 Done 掩盖游戏未测。

以下情况阻断对应阶段：

- 业务权威流入 Host，或 appearance adapter 开始决定协议/托管/自动返还：返回 P1。
- 公共契约仍暴露无需公开的引擎对象，或官方消费者需要私有入口：返回 P2。
- 新 API 缺少所需能力但只有签名/假 backend 证据：保持 P3/P4 待测，补 native/平台探针。
- 1.7 新 System 库/绑定冲突原因不明、游戏 DLL 被打进候选、正确包未激活：停止该候选验证，修 P7，不绕过预检。
- 真实交付、Scribe、未知结果或切档失败：保留原数据与证据，修对应插件 game adapter/已证行为；不把 R44 增强伪装成紧急 UI 修复。

回退以独立候选与固定原制品实现，不 reset 工作区、不覆盖正式历史资产、不以禁用功能/吞异常/弱化回执保证换取表面通过。游戏升级导致 DLL 指纹变化时，只重验受影响阶段，但支持范围必须指向重新核对的真实构建。

## 8. 本轮交付与下一实施入口

本轮最初交付规划，随后开始 R17-10。实施范围、引用指纹、检查命令与 ABI 门槛见 [R17-10 实施记录](RimWorld-1.7-R17-10-Trade-Boundary-2026-10-13.md)。其余任务不随这一切片自动完成。

R17-11 实现迁出 Contracts，R17-12 归属审查记录见 [Inventory/Trade 游戏边界记录](RimWorld-1.7-R17-11-Inventory-Game-Boundary-2026-10-13.md)。R17-20 首切片见 [公共数值契约记录](RimWorld-1.7-R17-20-Presentation-Values-2026-10-13.md)。R17-21 列表图像记录见 [图像 lease 首切片](RimWorld-1.7-R17-21-Row-Image-Leases-2026-10-13.md)。R17-21 其余图像入口见 [Badge/media/composer 实施记录](RimWorld-1.7-R17-21-Presentation-Image-Slots-2026-10-13.md)。主题注册首切片见 [主题注册记录](RimWorld-1.7-R17-20-Theme-Registration-2026-10-14.md)。主题 Draft/Preview/Reader 记录见 [主题色值实施](RimWorld-1.7-R17-20-Theme-Values-2026-10-14.md)。R17-22 记录见 [受控自绘 context](RimWorld-1.7-R17-22-Draw-Context-2026-10-14.md)。公共菜单记录见 [动作菜单边界](RimWorld-1.7-R17-20-Action-Menu-2026-10-14.md)。窗口入口实施见 [窗口边界记录](RimWorld-1.7-R17-20-Window-Boundary-2026-10-14.md)。中立合同程序集首切片见 [R17-23 呈现程序集记录](RimWorld-1.7-R17-23-Presentation-Assembly-2026-10-14.md)。下一实施入口为 **R17-23 完整工程 restore/锁图与版本加载布局**，再闭合窗口/输入、两版 backend 与 API 3.0 联合身份；R17-00/01 的完整工程基线仍需补齐。R17-02 诊断并行于这些工作顺序推进；随后审查 R17-20～23 的最小公共契约与版本决策，再做新 API 第一可运行切片。暂不扩写业务状态机，也不先发布临时描边补丁。


2026-10-14 最新入口：R17-71 两版Host分析布局和17项实际目录检查完成，正式targets/身份/游戏仍待闭合。发现managed包合同仅接受net472，下一切片先同步TFM声明、游戏运行时预检和商店/profile选择，随后联合候选。不能将版本目录检查当作managed安装通过。[实施记录](RimWorld-1.7-R17-71-Versioned-Layout-2026-10-14.md)。


最新阻断更新：候选03两版初次SDK GetTypes失败，合同编号修为09后候选04在两系统运行时加载回归通过；实机需要重验。商店双payload单包尚未完成。[共同加载修复](RimWorld-Shared-SDK-Load-Order-Fix-2026-10-14.md)。


2026-10-14 最新状态：用户反馈候选04的1.6通过；候选05保持其1.6文件不变，修正1.7原生绘制坐标，1138白盒/完整26项编译通过，1.7显示待测。[依据及复测清单](RimWorld-1.7-Native-Border-Coordinates-2026-10-14.md)。

2026-10-14 最新入口：用户确认候选05显示与布局；候选06已接入独立双版托管包，红包/人才各一个ZIP覆盖两游戏，Host不重复内置独立插件。按游戏选择/统一所有权及静态绑定白盒通过，下一步候选06游戏导入、重启激活、切游戏与卸载；远程更新与双版故障恢复专项仍待补齐。[双版本包证据与清单](RimWorld-Dual-Managed-Packages-2026-10-14.md)。

2026-10-14 候选06实机发现CoreCLR按需框架依赖误拒绝；候选07补齐runtime可信框架facts，未放宽身份校验或改插件业务。独立ZIP不变；先复验红包/人才导入和重启激活，再继续双版切换/卸载闭环。[候选07证据](RimWorld-1.7-Lazy-Framework-References-2026-10-14.md)。

2026-10-14 新入口：候选09修复“预检通过但启动actualHost仍缺未加载框架DLL”阻断，补拒绝审计和测试包1.7支持声明。全图引用审计完成并区分原Host框架桥接与managed严格身份；1.6 Unsafe/vendor异步路径列入复测，不变更pins。先验证两个插件实际ACTIVE与页签，后继续业务及两游戏切换。[审计记录](RimWorld-Dual-Reference-Audit-2026-10-14.md)。
