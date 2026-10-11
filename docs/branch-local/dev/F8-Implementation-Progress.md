# F8 实施进展

日期：2026-10-10。对应主题/API 阶段计划与兼容接口边界审计。当前为未发布的源码变更，未创建提交或发布目录；第二批已将客户端抽象源码版本提升到 2.0.0，尚未发布。

## 首批已实现

- `IClientTheme` 是唯一当前主题读取与注册契约，删除 `IUiTheme`。主题文件发现、旧键与 XML v1 读取归入输入边界；当前业务使用规范键。
- 主线程主题提交、有效样式版本与变更事件；注册具有 owner 与可释放作用域，停用插件撤销默认值和编辑项。
- 独立主题窗口与宿主设置入口，不依赖 Chat 或商店启用。支持搜索、主题选择、局部预览、RGBA 编辑、重置、导入与导出。
- 编辑草稿不修改全局主题；校验和原子保存成功后再发布。坏文件保留已验证的活跃样式，主题被删除时使用默认值并保留选择意图。
- 原有 Mod/用户 XML 叠加配置首次归一化为普通用户主题；原文件保留。目录缺失、选中项缺失、文件拒绝和配置拒绝均在读取边界处理。
- `ClientWidgets` 统一基础控件的主题读取和 GUI 状态作用域，处理嵌套、异常和禁用状态。现阶段主题窗口使用此入口，其余 UI 的完整控件迁移尚未结束。
- Chat、Trade 订阅样式变化；Chat 消息富文本、回复预览和用户名缓存失效。商店使用当前契约和注册作用域，扩展管理器状态色纳入编辑项。
- Chat、Trade 设置面板移除旧键迁移实现；各自独立 `SettingsInputAdapter` 继续按原规则读取历史数据，没有删除持久化键或改变既有缺省值。

## 当前契约的使用

在模块组合处取得宿主 `IClientTheme` 并注入 UI。激活时持有 `IClientThemeRegistration`，注册模块默认值；停用时先取消主题事件订阅，再释放注册。

```csharp
registration = theme.RegisterOwner(ExtensionId);
registration.RegisterColor("my-plugin.panel", defaultPanelColor);
theme.Changed += RefreshStyles;
// UI: ClientWidgets.Label(rect, label, theme);
// Shutdown: theme.Changed -= RefreshStyles; registration.Dispose();
```

主题读取、注册、编辑与事件消费限定游戏主线程。不要在业务中检查文件版本、读取历史别名或主动重载旧接口。主题窗口通过 `IClientThemeWindowService.OpenThemeWindow()` 打开；目录与草稿由 `IClientThemeCatalog` 管理。

输入限制：单文件 1 MiB、最多 512 个主题条目、键最多 160 字符；禁止 DTD、非有限数值和重复键。设置最多 4096 个覆盖条目。导入导出当前使用路径输入，尚未接入文件选择器。

## 已有验证与限制

- RimWorld 1.6 Release 全客户端解决方案编译通过。
- 首轮独立主题运行时测试 29 项断言通过：输入归一化、草稿隔离、保存恢复、坏主题替换、移除回退、注册释放、订阅异常隔离、保存失败、DTD/重复值拒绝和嵌套 GUI 状态恢复。
- 构建环境无法在线读取 NuGet 漏洞审计源，存在 NU1900 警告；没有通过修改依赖或 vendored SDK 绕过。
- 未在真实 RimWorld/Unity 内完成窗口视觉验收、联机验收或旧存档读写验收。模拟控件断言不代表真实游戏 UI 验收。

## 尚需实现的后续切片

| 切片 | 状态与接续工作 |
| --- | --- |
| F8-B 完整边界清理 | 设置与主题输入边界已拆；Chat 当前发送入口、Trade 状态输入、库存交付和客户端作者入口已收口；生命周期协议模式、Trade 请求编码和历史物品 JSON 等仍需迁移 |
| F8-1 主题运行时 | 首批已实现；待接入经过管理器校验的主题包来源及事务完成后的刷新 |
| F8-2 全部 UI 主题覆盖 | 当前契约已迁移；待 Inventory、红包、人才贸易及宿主其余控件迁移、配色覆盖和游戏内布局验收 |
| F8-3/F8-5 商店与 Index | 尚未实现 `phinix-theme`/`theme-zip`、多变体清单、目录 v4、纯数据校验、安装/更新/卸载事务及 Index 上架工具；当前商店仍不接受主题包 |
| F8-4 包装 API | 主题与控件基础契约已实现；资源、设置作用域、Chat/Trade 当前意图、红包跨插件操作与人才贸易输入边界尚需实现 |
| F8-6 开发者示例与验收 | 当前主题使用方式如上；完整指南、示例、发行兼容范围和维护插件协调打包待全部删除门槛完成 |

客户端旧二进制兼容没有恢复为目标。剩余 adapter 只保护需要继续读取的数据和支持的外部协议；当前业务不能新增旧接口、schema 或协议模式依赖。首批完成不代表上述门槛已全部通过。


## 第二批：当前操作入口与删除门槛

- 删除库存 `void Deliver` 和 `IInventoryVerifiedDeliveryCodec` 双契约，`IInventoryCodec.DeliverVerified` 成为必需方法。库存主体不再判断旧 codec 接口，交付结果为 Uncertain 或异常时继续保留托管；异常后的实际 custody 核对仍保留。InventoryContract 提升到 6，持久化 codec ID/版本不变。
- Trade 删除三个 `IFrameworkLegacyTrade*` 接口、转发类 `FrameworkLegacyTradeClientAdapter` 和主体的四个 `*LegacyTrade*` 方法。adapter 直接提交 `ITradeStateInput` 当前状态；普通状态不确认操作 token。Native 完成/取消事件复用同一完成入口，在解码成功后再移除 repository 条目，原始交付 payload 继续保留。
- Chat 新增 `ChatMessageIntent`/`IChatMessageSender`。UI 不再判断远端能力、构造 wire packet 或直连发送；失败时保留编辑文本和回复目标。红包公告使用同一 API，Chat 未启用时跳过公告。
- Prepared message 通过宿主提交边界按 handler 优先级发送，回复/提及元数据传入 adapter；失败后停止，不选择另一协议重发。`TrySend` 成功表示已派发，不能当作服务端确认。旧协议的元数据限制留在 adapter，沿用原始文本发送行为并记录降级；本地回声只在派发成功后生成，回声观察者失败不触发重新发送。
- 删除 `LegacyClientExtensionModule` 和客户端旧 Register 警告桥梁。客户端发现阶段只接受 `IClientExtensionModule`，恢复借用的 discovery policy；共享/服务端 Register 不变。Common canonical 源码、客户端固定 checkout 和 Index 静态校验源码同步移除旧客户端基类识别，未更换 submodule gitlink。
- 客户端抽象源码/环境版本统一为 2.0.0；红包、人才贸易、Example 和打包工具的当前范围统一为 `>=2.0.0 <3.0.0`。未发布，已有包/CI 的宿主提交 pin 仍需在源码正式提交及发行协调时更新，不能将旧发布包视为已迁移。

编译结果：RimWorld 1.6 Release 全客户端解决方案通过；红包和人才贸易分别使用同批宿主输出编译通过，均为 0 warnings / 0 errors。对应旧接口 fixtures 已迁移，本批没有运行回归测试或游戏内测试，没有发布新候选 ZIP。

完整 F8-B 仍未完成：Trade 的请求 adapter、生命周期 wiring 和 item pipeline 仍有模式信息，主体仍有历史 JSON 预览读取，人才贸易的 Def/schema 输入边界与静态 Runtime 仍待处理。主题包/商店/Index v4 尚未实现。


## 用户补充：维护插件支持旧客户端 1.9

2026-10-10 用户确认：红包和人才贸易的新版本须继续支持升级前客户端抽象 1.9。该要求限于维护插件发行线；当前宿主继续只有 Compose/当前接口，不恢复旧入口。

两插件以同一业务源码分别构建 1.9/2.0 ZIP。红包引入中性的公告发送端口，1.9 transport 和库存旧基接口只在 Client/Compatibility/Client19Adapters.cs 出现；2.0 adapter 仍消费 Chat 当前意图 API。人才贸易无新增旧业务接口，仅针对各宿主重编。

本地候选：2.0 两插件为 1.1.0；1.9 红包 1.0.3、人才贸易 1.0.2。两条发行线避免相同包 ID/版本拥有不同载荷，允许未来目录按宿主范围选择候选。默认 publication.json 标记新候选 1.1.0，1.9 构建要求显式版本号。

最终四个 ZIP 的静态校验、逐文件哈希及对应宿主 CLR 引用检查通过，另一主版本均拒绝。具体包与报告保存在工作区 local-plugin-packages/2026-10-10-f8。未发布线上目录，未做游戏验收；先前“未生成候选 ZIP”的状态由本节更新。


## 最新修正：同一插件包内部适配，不分发行线

用户明确要求：每个新插件的同一个包在内部 adapter 中同时适配旧 1.9 与新 API 2.0，等客户端更新完成后再收尾。此前双目标四包方案已撤销。

最终候选改为红包/人才贸易各一个 1.1.1 ZIP，manifest 范围 >=1.9.0 <3.0.0。统一使用 1.9 基线编译；红包当前 Chat API 延迟解析、运行时版本读取和旧库存入口仅在 Client/Compatibility/ClientAdapters.cs，业务/UI 不接触版本信息。新 Chat 停用时不回退旧发送路径。人才贸易使用两边不变的稳定接口。

纠正此前过早的 CLR 身份提升：当前宿主 API 兼容常量/AssemblyFileVersion 仍为 2.0.0，CLR AssemblyVersion 临时冻结 1.9.0.0，使仅使用稳定子集的单包能绑定两端。不存在核心旧 API 恢复或宽松跨 major 加载规则；旧版本清单仍按实际 API 范围判定。当前开发宿主已重新整包编译，使用此前 CLR 2.0 输出的游戏须更新宿主并重启。

打包工具区分文件 API 版本和 CLR 身份，并增加 --compile-client-api 1.9.0 编译基线检查。单份 ZIP 对旧/新宿主共四组最终静态、哈希、CLR 引用和实际类型加载检查通过；红包的交付接口在两端分别映射旧 Deliver 与当前 DeliverVerified。检查未调用插件业务，不替代游戏内验收。当前正式候选目录 local-plugin-packages/2026-10-10-single-adapter；此前四包与 drafts 中间产物已替代，未发布线上目录。

更新完成后再协调移除插件 adapter 旧分支、收紧声明并结束 CLR 绑定过渡。原定“客户端仅当前业务契约、兼容语义仅在 adapter”的边界继续有效。


## 第三批：库存主题覆盖与公共控件（2026-10-11）

用户已反馈上一批游戏测试通过，允许继续实现；该反馈不代表本批库存主题窗口已验收。

- 库存 Tab、分组/条目、提取与计划编辑、恢复/未决提示、故障卡片、交付选择和两种设置面板接入当前主题。文本、边框、悬停和按钮使用宿主 ClientWidgets；库存专属背景和交付强调色注册为 10 个 inventory.* 可编辑项。
- InventoryTheme 通过普通模块 DI 注入，激活时注册并持有 owner lease，组合作用域释放时撤销。每次绘制读取有效颜色，库存文本/余额缓存不保存颜色，切换主题不重置选择、展开组、输入文本或滚动位置，也不触发库存操作。
- ClientWidgets 增补 Fill、Border、LabelEllipses、显式颜色 Label、RadioButton 和带描述的 RadioOption。组合控件复用现有 Verse 鼠标/Tooltip 行为，并恢复字体、对齐、换行、GUI 颜色与禁用状态。库存交付设置使用 RadioOption 作为真实消费方。
- 库存各绘制入口与文字测量加绘制作用域，异常路径恢复 GUI/Text；插件停止先关闭其交付选择窗口，再释放主题注册，避免悬挂窗口读取已注销键。
- 仅客户端 UI/基础控件变化，没有改变 InventoryContract、codec ID/版本、存档/日志格式、物品托管、提取与确认标准。维护插件 1.1.1 单包的内部 1.9/2.0 adapter 保留。

RimWorld 1.6 Release 全客户端解决方案编译通过；本批未新增或运行测试，未完成新库存配色的游戏视觉验收。构建保留原有依赖审计/旧 wire 字段弃用警告，没有修改 vendored protobuf。产物在 Output/phinix-rework；没有同步游戏 Mods 目录或发布插件/Index。商店主题包/Index v4、其余 UI、Trade 请求及物品输入边界和其余公开 API 仍按原计划接续，未宣称整阶段完成。


## 第四批：主题架构接续 T1（2026-10-11）

计划见 Theme-Architecture-Continuation-Plan.md。用户已验收上一批热加载/库存 UI；本批结果不沿用上一批验收结论。

- 当前主题拆为 IClientThemeReader / IClientTheme（读取、事件、Capture）与 IClientThemeRegistry（模块注册）。删除本阶段未发布的任意字符串 RegisterOwner；注册只接受生命周期观察器确认的实际模块实例，namespace 防止占用 ui/ext/Host 或另一模块的前缀。
- 颜色/参数先暂存，Commit 校验重复键、跨类型冲突、namespace、回退依赖与循环后一次发布。显式释放及 Host OnStopped 均撤销 owned lease，未提交批次不触发颜色更新。每模块最多 128 条、全局最多 4096 条，不把全局注册数量误当主题文件必须覆盖的数量。
- 解析只输出已注册键；当前用户覆盖/主题/builtin.default 的显式值优先，再继承 Host 角色或同模块回退键，最终使用注册默认值。未知原始键/用户覆盖保留，模块停用期间不对业务暴露，重新注册恢复。
- ClientThemeSnapshot 防御复制颜色/参数且无可写集合，版本随整批发布更新；ClientWidgets 接受 reader，可消费绘制快照。预览复用同一按键解析函数；导出保留显式原始值、未知键和用户覆盖，避免冻结所有继承值，继续沿用文件 512 条限制。
- Chat/Trade/Inventory/Store 全部使用同一 RegisterModule/Commit。通用行背景、次级文字、等待/错误等声明语义回退；仍保留有意义的业务专属默认值。Host 生命周期 observer 聚合 Theme 与 Localization，清理一方失败仍释放另一方。
- 新增 chat.messageText / chat.inputText，缺键时跟随 ui.text.primary；普通已确认正文使用基础富文本色，用户显式富文本颜色和提及高亮继续覆盖基础色，待发送/拒绝使用状态色。当前用户名 HSV 参数显式注册，旧 nameColor.* 仅在输入边界归一化。
- API 常量/文件版本保持 2.0，CLR 身份保持 1.9；维护插件 1.1.1 原包与内部 adapter 保留，不依赖此次新主题接口。主题热更新，程序集仍须整客户端同步并重启。

后续 T2 编辑元数据/浮点参数、T3 其余控件与绘制快照覆盖、T4 维护插件主题 adapter/示例、T5 商店主题包仍未完成。本批未新增或运行测试，游戏验收待进行，未同步游戏 Mods 或发布 Index。


T1 本批编译：RimWorld 1.6 Release 全客户端解决方案通过，保留既有 NuGet 审计不可达、protobuf 目标与旧 Trade wire 字段弃用警告。现有主题/Chat/Trade/Inventory/Store fixtures 已同步新的接口签名与依赖，不新增测试场景，未构建或运行这些测试项目。输出仍位于 Output/phinix-rework；需用户整包同步后重启验收。


## 第五批：T2 主题编辑器（2026-10-11）

用户已验收 T1；本批不沿用其游戏验收结论。

- 注册元数据增补 owner 显示名、分组和用途；Host 与内置插件的条目提供中英文本，编辑器按模块/类别展示，支持独立搜索及来源提示。列表只绘制可见行，语言/注册目录变化重新构建列表，不重置草稿。
- 颜色预览返回用户覆盖、当前文件、默认文件、继承路径与注册默认值的来源；浮点参数公开定义/范围/来源，昵称饱和度与亮度可编辑。预览与应用继续复用同一解析函数。
- 清除用户覆盖与恢复继承/默认值是不同操作：前者继续消费文件显式值，后者使用有界 inherit 意图跳过该键的文件层并继承角色或使用注册默认值，不改写原始主题。按主题保存颜色、参数和意图。
- 每个主题保有独立窗口草稿、当前项、输入与滚动。只选择条目不会制造覆盖；只在有效输入实际变化时写草稿。无效输入保留并禁止应用/导出，已应用样式不变。取消仅丢弃未应用窗口草稿。
- 未注册键与不支持类型计入未使用项，原值继续保留；文件/注册变化不销毁草稿。窄窗口增加编辑滚动和两行底部操作。
- 所有格式判断集中到 ThemeInputAdapter：主题读 v1/v2/v3，普通导出 v2、有 inherit 指令导出 v3；设置读 v1/v2、写 v2。设置替换保留 .previous，验证和持久化成功后才发布当前样式。未知键保留、重复/冲突与非有限值拒绝，既有限额不放宽。
- API/文件版本仍为 2.0，CLR 身份仍为 1.9，红包/人才贸易原 1.1.1 包及其内部 adapter 不变。旧客户端读取新主题格式不属于维护插件的兼容承诺，源码与当前内置插件同批编译，不混装 SDK/旧内置 DLL。

游戏验收见 Theme-Editor-T2-Playtest.md。T3 全控件与绘制快照覆盖、T4 维护插件主题接入/作者示例、T5 商店主题包仍待接续；本批未新增或运行测试，未同步游戏 Mods、提交或发布 Index。

T2 收尾编译：RimWorld 1.6 Release 全客户端解决方案通过。构建静态资源补齐 Themes/default.xml 的输出复制，整包部署包含默认主题、本批中英文本和同批 SDK/内置插件程序集。保留既有依赖审计、protobuf 目标和旧 wire 字段警告；未运行自动化测试，游戏内验收仍待进行。

### T2 导出位置反馈

用户反馈 T2 应无问题，补充要求优先处理导出后打开文件夹；没有逐端验收条件，原生导入/另存为选择器暂缓。

- 导出使用规范化绝对路径，写入成功后显示该路径并尝试打开父目录；窗口保留复制路径和再次打开目录按钮。导出失败不启动文件管理器，启动失败不改变已写入文件的成功结果。
- 系统调用集中到 Host 内部 LocalFileLocationAdapter：Windows 使用目录的 Shell 关联，macOS 调用 /usr/bin/open，Linux 调用 xdg-open。命令直接启动，路径为独立引用参数，不经过命令 shell，不等待桌面进程退出。启动成功仅表示请求已交给系统，不能保证文件管理器实际显示。
- 不新增原生 DLL、文件选择依赖或公开插件 API；主题解析、格式、持久化与维护插件 adapter 不变。导入保持手填路径，后续商店安装另按 T5 接续。

RimWorld 1.6 Release 全客户端编译通过，未新增或运行测试；Windows/macOS/Linux 桌面行为未实机验收。手工验收只需导出到含中文/空格的路径，确认文件存在、目录打开、复制路径与再次打开正确；无法打开目录时路径仍可获取。

## 第六批：T3 公共控件与绘制快照，首批

用户认可上一批可继续，导出目录是否自动打开不作为后续实施阻塞条件。

- ClientWidgets 按钮使用宿主注册的背景、悬停、按下和禁用角色，保留 Verse 按钮交互/声音；禁用状态同时传递 native active 和 GUI.enabled，拒绝点击。主操作/危险操作文字继续消费 success/error。
- 输入保留 Widgets.TextField 原生编辑、最大长度和文本控件行为，增补背景染色、禁用文字、稳定控件名称、焦点边框及未聚焦空值占位提示。不修改全局 GUI.skin，背景染色仍基于游戏原生输入框纹理，并非独立纯色背景替换。主题窗口搜索和各值输入是实际消费点。
- 增补主题化复选框和有界进度绘制；扩展管理的模块开关、商店下载进度及主要按钮/search 接入。Host 增加 6 个控件角色与中英文元数据，部分主题缺少新键仍使用回退/注册默认值。
- 库存 Tab、设置与交付选择绘制作用域捕获一次不可变快照，嵌套作用域复用并恢复外层快照。商店主视图、Host 扩展模块页与包管理页使用同次绘制的 palette。
- Host 扩展模块状态、依赖警告和日志缓存保存语义状态/日志等级，移除缓存的解析颜色；每次绘制使用当前快照，不为切换主题重建列表/清空滚动。注册项及缓存没有引入主题格式或旧客户端信息。

本批保留 API/文件版本 2.0、CLR 身份 1.9 与维护插件 1.1.1 内部 adapter。当前未发布 SDK 与内置消费方同批更新；TextField 增补可选参数属于本阶段源码 API 调整，不能把旧阶段编译的内置 DLL 混装。新增 InputLegacyModule 仅为基础游戏的输入引用，不随包分发游戏 DLL。

全客户端 RimWorld 1.6 Release 编译通过；未新增或运行测试，未部署游戏或发布 Index。现有 ThemeRuntimeTests 的轻量 Widgets/Unity stub 同步公共控件签名，仅为源码对齐，未构建/运行该项目，不能计为本批验证。T3 尚未全部完成：Chat/Trade 绘制入口与设置、旧商店辅助窗口、链接和滑块等控件继续接续。游戏内建议检查主题窗口搜索/焦点/数值输入、库存按钮和扩展页状态/日志的热更新，以及商店禁用操作；无需为验收刻意发起安装或库存操作。

## 第七批：T3 聊天、交易与参数滑块

- ChatTheme/TradeTheme 不再逐字段复制可变颜色；保存不可变 reader 并在绘制作用域捕获当前快照，嵌套列表共用外层 palette，异常/退出恢复原作用域。主题变更事件更新非绘制时的读取快照，不改写正在绘制的样式。停用时撤销订阅并释放静态宿主引用。
- 聊天主 Tab、用户侧栏、正文列表、用户名列表、通知横幅/侧栏和设置，以及交易 Tab、列表、窗口和设置均建立主题与 GUI 状态作用域。正文缓存继续按快照版本失效，原富文本/提及规则保持；不新增主题格式或旧接口判断。
- 在线人数和通知相对时间改为缓存无颜色字符串，绘制时消费角色颜色；不需要等待人数/时间桶变化才看到更新。昵称 HSV 参数从当前快照读取。
- 聊天发送/回复关闭、侧栏按钮、通知操作和交易全部常规按钮接入 ClientWidgets。交易已接受状态、窄布局当前报价页及取消强调色显式保留；没有更改接受、发起、取消、物品选择与托管规则。
- 聊天输入保留原 CHAT_INPUT_CONTROL 名称及输入所有权边界；交易数量字段保留 maxLength=100 和 itemCountInputRegex。搜索和设置字段使用稳定控件名称，输入焦点边框使用通用角色，聊天正文输入与交易搜索占位继续消费插件专属颜色。
- 交易列表滚动视图改为 try/finally 配对释放。通知剩余时间复用有界进度控件。主题参数在足够宽的编辑区提供滑块；没有滑块动作不会生成覆盖，数值框保留，滑块只修改窗口草稿，应用/取消语义不变。

API/文件版本 2.0、CLR 1.9 与维护插件 1.1.1 内部 adapter 保持；公共控件可选参数调整由当前客户端与内置插件同批编译。全客户端 RimWorld 1.6 Release 编译通过，保留已有依赖警告；未新增/运行测试、未同步游戏目录、未发布包或 Index。ThemeRuntimeTests 的原生控件 stub 仅同步签名，未构建或运行。

本批验收建议见 Theme-Controls-T3-Playtest.md。T3 仍有商店辅助窗口、链接和宿主其他外壳绘制待核对，不能把本批视为 T3 全部完成；T4 维护插件主题 adapter 和 T5 商店主题包继续按原计划。

## 第八批：T3 收尾，集中游戏验收

用户要求 T3 做完后一起进行游戏内测试。本批完成剩余实现与源码覆盖核对，不运行自动化或游戏测试。

- Host 保有自己的 IClientTheme，HostThemeDrawing 将主窗口、侧栏抽屉、连接/登录窗口、扩展管理窗口、Mod 设置和保留的 Displayable 控件限制在绘制快照内。内部辅助类不对插件开放；插件继续通过普通 IClientTheme 服务读取自己的绘制快照，不依赖 Host 内部 UI。
- 主/侧栏 Tab 复用主题化的原生 overflow TabDrawer，保留原导航、溢出、徽标、选择和接受键行为。连接、显示名与登录字段使用稳定名称，连接按钮沿用有效端口/连接中的禁用规则。Mod 设置逐个 provider 使用 GUI 状态作用域，Listing/滚动视图在 finally 释放。
- Host 扩展设置提示缓存改存无颜色文本及提示语义，绘制时读取 warning/secondary；模块标题、不可用开关、重启说明和状态均使用注册角色，移除此处的备用硬编码颜色。保留 Displayable 的公开构造方式，不新增旧/新 UI 双实现；文字测量与绘制恢复原 GUI/Text 状态，滚动容器异常路径也配对 EndScrollView。
- 商店本地包选择器、设置入口和更新横幅使用注入的普通主题服务，窗口按可用屏幕约束并只绘制可见路径行。主视图拥有并在 Stop 时关闭本地包窗口；选择 ZIP 的规则、DevMode 限制、路径长度和目录数量限制保持。
- 新 Link 控件在 Workshop 外链操作和 Host 透明按钮中真实消费，返回点击而不执行 URL；商店继续调用原 IClientLinkService 的策略。滑块/进度/复选/单选/按钮/输入的角色有真实消费点，新增控件的说明改为玩家可理解的用途。
- 聊天在线状态、用户名悬停、交易空列表/交替行/物品面板、库存编辑字段焦点等收尾接入共享或模块角色。物品图片、聊天图片和交易箭头绘制保持中性颜色，避免周边文字配色染色图片。

T3 源码实现完成，等待统一游戏验收。覆盖矩阵见 Theme-Controls-T3-Playtest.md。范围为当前编译和激活的 Host/Chat/Trade/Inventory/ManagedStore UI；未编译的历史 PluginStoreView 原型不作为当前 UI 路径迁移，也没有为它增加主题分支。Verse 的窗口边框、滚动条、原生 Tab/输入/复选/单选/滑块纹理和通用原生确认/菜单保留游戏素材；角色提供文字、背景/染色、焦点、状态等样式，不替换全局 skin、字体资源或图标素材。

保持主题 v1/v2/v3 输入边界、用户覆盖与缺键回退、API 2.0/CLR 1.9 身份；没有更改红包/人才贸易 1.1.1 单包及内部 adapter，没有迁移旧协议、存档、库存托管或商店安装事务。当前 SDK/Host/内置插件必须同批部署，维护插件仍按原兼容承诺运行；其主动主题注册属于 T4。

RimWorld 1.6 Release 全客户端解决方案编译通过；保留原有依赖审计/旧 wire/protobuf 目标警告。现有 ThemeRuntimeTests stub 仅补齐控件签名，未新增场景、构建或运行该项目。未同步 Mods、提交或发布 Index。T4/T5 仍未实施；本批游戏与三端桌面行为均未验证。

## 阶段调整：UI/UX 重构与在线交付延期

用户反馈 T3 “没问题”，同时明确后续大范围 UI/UX 重构，在线更新推迟，并要求阻塞依赖项。用户反馈记录为当前版本可用，不推定全部验收矩阵或三端已验证。

新增 UI-UX-Refactor-Delivery-Gates.md：阻塞在线发行/Index 更新、主题商店最终集成与上架、UI API 正式冻结/作者交付、维护插件最终主题 UI 迁移/打包及主题键定稿；维护插件 1.9 adapter 的删除与 CLR 身份变更继续保有独立退出门槛。主题核心、纯数据校验、业务 API 边界和真实故障修复可独立继续。原接续计划和阶段计划已同步该约束。

本次只修改文档，没有禁用现有商店/更新检查，没有改发布配置、运行构建或测试、部署或发布。下一步先规划 UI/UX 重构，不自动开展 T4/T5 在线交付。


## 第九批：恢复原计划，T4 维护插件与作者示例

用户纠正 UI/UX 重构不属于本任务，要求先实现原计划可推进的工作。上一节对 T4/T5 本地实现、维护插件候选打包和作者示例的泛化阻塞撤回；在线发行与 Index 更新仍延期，UI API 正式冻结不提前承诺。当前边界见 UI-UX-Refactor-Delivery-Gates.md。

- 红包、人才贸易在各自 Client/Compatibility/ThemeAdapter.cs 封装新主题接口发现、注册与释放。单包仍按保存的 1.9 SDK 编译，没有静态引用新主题类型；当前宿主绑定普通主题服务，1.9 使用插件默认配色。当前接口形状异常明确失败，不恢复已删除的旧主题入口。
- 两插件分别注册 panel、highlight、accent 三个真实消费键，按普通模块身份提交注册；颜色回退到共享 ui.* 角色。业务和绘制仅消费插件内部 palette，不判断客户端版本或主题格式。一次绘制固定快照，版本改变时更新颜色缓存。
- Tab、详情/交易窗口和设置接入背景、悬停、强调、按钮状态、禁用、输入焦点及复选框。原生输入长度/正则、选择、公告、库存交付和交易业务保持原规则；图标保持中性颜色。停用释放注册，人才贸易部分激活失败时也释放主题引用。
- 普通 Example 直接借用当前 IClientTheme/IClientThemeRegistry，在 Activate 以实际模块实例注册，作用域拥有并释放 registration；服务本身不由插件释放。panel/counter 两键与 ClientWidgets 有实际消费点。普通示例要求 API 2.0，不包含维护插件的 1.9 adapter。
- 本地候选位于 ../local-plugin-packages/2026-10-11-t4：红包/人才贸易 1.1.2，Example 1.0.3；旧 1.1.1 包保留。配套部分主题为 ../local-theme-samples/2026-10-11/08-plugin-t4.xml。目录路径相对 Phinix-Rework 仓库根。

两个维护插件按 1.9 基线编译通过，Example 按当前 SDK 编译通过；两维护包分别通过 1.9/2.0 的包及程序集引用静态校验，Example 通过 2.0 校验。维护插件类型引用不包含新主题类型。结果与摘要见候选目录 validation-report.json、SHA256SUMS。Example 编译保留依赖审计网络不可达警告。

本批没有新增或运行自动化测试；现有 Example 测试宿主仅同步借用服务签名，未构建或运行。没有执行运行时 adapter、安装到游戏、提交或在线发布，静态检查不代表两客户端实际运行已验收。T4 源码和本地候选完成，游戏验收见 Theme-Plugins-T4-Playtest.md；后续继续 T5 本地纯数据主题包与商店事务。


## 第十批：T5 纯主题包基础与第三方扩展点盘点

用户继续原计划，并要求检查 Host/插件接口尤其右键注入。

- 共享 ThemePackage.cs 定义纯数据 phinix-theme 清单、变体、冻结载荷和 JSON/XML/ZIP 校验，工具和商店客户端编译同一份源码。没有增加模块/空 DLL，也没有改 Common gitlink、DLL 收据或 journal。
- ManagedPackageTool 增加 --pack-theme / --validate-theme，先验证再写包、拒绝覆盖已有候选；限制路径/大小/额外文件/链接、哈希长度和规范 XML。当前主题 v2/v3 可打包，旧格式先由 Host 输入 adapter 导入/导出归一化；未知键保留且不要求完整键集合。
- 演示 ZIP 与配置位于 Phinix/local-theme-packages/2026-10-11-t5，静态结果标明 installed=false。目前不是可通过现有商店安装的候选，不手工复制受管文件。
- 第三方接口盘点见 Third-Party-Extension-Api-Assessment.md。Chat 用户/消息及 Trade 物品私有菜单没有公开追加入口，将 Chat 用户/消息扩展列为 API 第一批；Host 通用菜单机制与插件 typed 业务上下文分开，生命周期/会话失效、异常隔离和真实操作确认一并交付。另记录资源、能力状态、主线程结果、库存快照、Store Contracts 与原始传输/兼容接口的边界。

ManagedPackageTool Release 编译通过，保留 protobuf 既有目标警告；PluginStore.Client Release 编译通过，0 警告/0 错误。示例打包与独立 --validate-theme 静态检查通过。未新增/运行自动化或游戏测试，未同步游戏目录或在线发布。T5 首批完成，商店数据包安装/恢复、主题目录刷新、更新/卸载和目录 v4 尚待接续；右键 API 本批仅设计，未实现。详见 Theme-Package-T5-Implementation.md。


## 第十一批：插件 SDK 全范围评估与 Host 事件隔离

用户明确右键注入只是例子，要求大范围评估。新增 Plugin-Sdk-Comprehensive-Assessment.md，按完整作者流程覆盖发现/身份、API 发布与激活、DI、生命周期/事件、主线程结果、资源/配置/存储、导航与组合 UI、内置业务、游戏挂点、网络、诊断与制品；右键细节保留，但后续优先级统一按 A0–A4。

源码发现：ResolveAll 直接返回注册对象，不自行保证 Active；Dispatcher 满 500 项会丢最旧动作且只有累计统计；用户事件使用 multicast 直接 Invoke；存储路径清洗未绑定模块身份。InventoryLedger 的普通 Snapshot 已防御性 Clone，不能仅因 DTO 可变就称权威账本泄漏。把这些语义基础放在菜单/工具栏/详情贡献之前，不新增万能事件总线、第二注册中心或泛化全部 RimWorld API。

本批先行修复 ClientUserEventStream 的四种事件与 Shell 的 MainWindowOpened：共用 ClientEventDispatch，逐订阅者 catch，诊断只含事件/订阅者类型/异常类型，日志失败不阻断后续；保持原发布线程、同步顺序与公开 event 签名。不改变队列、注册解析、业务确认或协议语义。其余事件/订阅撤销/owner 失效以及 A1–A4 仍待实施。

T5 存储路径同步完成源码核对与设计：实际 DLL 安装主体在 Common ManagedExtensions，当前清单/row/recovery 强绑定代码包，下一批须抽取中立事实与类型 adapter，共用 staging/lease/preparing→committing 标准，不能假造空模块或调用历史 Mod 安装器。显式主题卸载和 theme-settings 的非原子后处理需要恢复意图。方案见 Theme-Package-Storage-Design.md，本批尚未实现数据包安装事务。

RimWorld 1.6 Release 全客户端解决方案编译通过（首次命令使用普通 Release 配置被拒，改用既有 Release 1.6 配置完成），保留已有 NuGet 审计网络/protobuf 目标警告。没有新增或运行自动化/游戏测试、部署、提交或线上发布。API/文件版本 2.0、CLR 1.9 和两个维护插件内部 1.9 adapter 保持；不把本次隔离代码视为整个 A1 已完成。


## 第十二批：明确 UI 评估交接与 Example 任务语义

用户要求明确这一轮结束点，并同意用 Example 展示插件 API 语义。新增 UI-Evaluation-Handoff-Checklist.md：本轮本地 T5 闭环、采用的作者基础/贡献/业务接口、Example 真实样例、同批制品/静态检查与游戏步骤齐备后，明确告知可开始 UI 评估；A4 的潜在能力不无限加入结束前置。当前尚未达到交接点，线上发布与三端实机不作为启动 UI 评估的先决要求。

- 新增 IClientTaskService / IClientTaskScope / handle 与不可变状态。实际模块实例在 Activate 绑定独立 owned scope，最多 16 个未关闭 lease；停用/部分激活失败由同一 lifecycle observer 撤销。主线程绑定和 Drain 有线程检查。
- 当前任务与旧 void adapter 共用一份 500 项队列，满时拒绝新动作而不挤掉已接受动作，每帧仍最多取 100 项。scope 关闭即时撤销其待执行动作，token 在执行前检查；运行中取消不强制回滚。回调失败/拒绝/取消/完成有稳定可观察 code，不公开 Exception；完成释放 callback/owner/token 引用。旧 void 消费方尚未迁移，不能计为领域结果恢复已解决。
- Example 1.0.4 真实消费 task scope，计数后的日志回调展示状态，预取消和失败按钮只模拟无游戏业务的回调。计数同步设置保存与 callback completion 明确分开；Stop 清理 owned scope 和 handle，旧确认仍检查 generation，成功重复 Activate 不重复绑定。
- Example 候选、校验报告与摘要在 Phinix/local-plugin-packages/2026-10-11-api，原 1.0.3 与两个维护插件包保留。需要本批完整当前客户端，早期 T3 SDK/Host 不混装；API/文件仍 2.0、CLR 1.9，维护插件旧 1.9 adapter 保留。

RimWorld 1.6 Release 全客户端编译通过，Example 编译/打包/Host 引用静态检查通过，保留既有 NuGet 审计网络和 protobuf 目标警告。现有 Example fixture 仅对齐服务/scope mock，未新增测试场景、构建或运行。没有游戏验收、同步 Mods 或线上发布。

A1 provider 状态、模块资源和世界/会话辅助仍待接续；其他内置任务消费者还未迁移，不能称整个任务 API 推广完成。T5 事务/目录与贡献接口仍待实现。详见 Client-Task-Api-Implementation.md 和 Example 的 Api-Semantics-Example.md。


## 第十三批：接入规范与强类型操作点，Example 1.0.5

用户纠正第二项交付要求：大规模放开可接入点、形成大致一致规范并在 Example 实验，不一定需要每个新 API 有内置消费者。此前强制内置消费门槛撤回，UI-Evaluation-Handoff-Checklist.md 与主评估同步；删旧接口仍核对现有消费者，不能借此删除维护依赖。

- 新增 IClientExtensionPointService / owned scope、typed DefineActions/ContributeActions、point/目录快照与可释放 action set。Host 不引用插件业务类型，专用贡献目录复用实际模块 lifecycle 准入，不接管 Common 发现/DI/API 注册。
- 定义名限定 owner 前缀，贡献强类型匹配，缺 optional 点可暂存；多模块排序稳定、数量/文本有界，重复/非法 factory 结果整批隔离。factory/lazy enumeration/执行均隔离异常并恢复 GUI 状态；模块停用或 scope 关闭撤销现存结果和 callback。
- 执行区分 Completed 与 Invoked，CallbackFailed 可已发生副作用，不能盲目重试；每收集动作至多调用一次，上下文 guard 执行前再验证。菜单/工具栏/行操作可复用此模型，未把任意 object payload 放进公开接口。
- Example 1.0.5 声明 public context 和自己的 actions 点，并贡献计数/重置确认，演示禁用、上下文失效与清理；菜单 PostClose 即释放 set。其他插件可以引用该实验上下文贡献同一点，真实业务 context 仍建议放 Contracts。当前尚未接入 Chat/Trade 等真实视图的点。

规范与作者使用见 Plugin-Api-Integration-Standard.md、Local-Plugin-Quickstart.md、ExampleActions.cs。RimWorld 1.6 Release 全客户端与 Example 编译、打包/Host 引用静态检查通过；旧依赖审计/protobuf/wire 警告保留。现有 Example fixture 只对齐新增服务 mock，不新增测试场景、构建或运行；没有游戏验收、Mods 同步或线上发布。

本地 Example 1.0.5 在 Phinix/local-plugin-packages/2026-10-11-api 根目录，1.0.4 保留；drafts 为被替代产物，不安装。API/文件版本仍 2.0、CLR 1.9，维护插件内部 1.9 adapter 保留。广泛资源/状态/内容贡献及业务入口继续接续，T5 事务/目录仍未完成，目前不宣告 UI 评估交接。


## 主题 ZIP 导出与快捷投稿评估（文档）

2026-10-11 用户调整：选择器本地导入交给商店，导出保留并计划改成标准主题 ZIP；导出后可选提交商店。用 gh 只读检查远端 Index main 198227bb45f034077465d5f21120324afd08bf02，确认已有 Issue intake/审核/受控发布，但现有目录路线限定 DLL，不能直接收录纯主题 ZIP。未更新与远端不同的本地 Index checkout。

新增 Theme-Export-and-Quick-Publishing-Assessment.md，比较作者仓库、共享主题库、无 GitHub 接收服务和本地委托发布，建议先完成 T5 本地事务与编辑器 ZIP 导出，再做预填快捷提交；提交成功不等于上架，完整无账号上传需要独立身份/代发布边界，不放维护者凭据进客户端。已同步阶段计划、T5 记录与 UI 交接项；既有用户 XML/配置继续保留，旧兼容信息仅在 adapter。

本批只修改文档，没有修改代码、构建/运行测试、安装、创建 Issue/PR、上传或发布 Index。现有窗口仍导出 XML，主题商店安装尚未完成；线上发布仍延期，快捷投稿不扩大本轮结束前置。


## 第十四批：T5 本地闭环、provider 与 owned lifetime

用户要求先实现 T5 本地闭环，再扩 API；快捷上传与现有线上流程的后续实施一并延期。

- 主题校验/打包核心移到 Common；安装 Recovery 抽出中立文件/所有权事实及共用 InstallItems，保留旧 DLL journal/receipt adapter。主题 schema 3 数据记录共用 preparing/committing 重放；卸载共用 owned 删除引擎，默认选择后处理失败保留 journal。主线程遇共同变更 gate 忙立即拒绝，避免与后台 DLL 主线程授权形成等待环。
- Host 包来源/借用服务、Store 非 DevMode 本地主题窗口及安装/更新/卸载接通；选择器热刷新。编辑器输出标准 ZIP、作者元数据/稳定 ID、绝对路径反馈；选择器导入按钮移除但既有 XML/配置保留。产品 0.9.7、SDK 2.0、CLR 1.9 的版本语义分别处理。
- 新增 provider typed 查询/精确注册 handle 与模块 owned lifetime/token/资源/主线程订阅，实际 owner 停止自动撤销，迟到 callback 不调用业务。Example 1.0.6 实验可选缺 provider、资源释放、scope 关闭与重绑；文件资源/存储及更多内置高层 API 仍待后续。
- 新增 Theme-and-Plugin-Publishing-Flows.md，记录目前 GitHub 投稿/审核/发布链与设想的快捷/共享库/无账号投稿，整体延期，不关闭已有线上功能/工作流。

同批客户端/工具/Example 编译与本地打包；操作步骤见 Theme-T5-Local-Playtest.md 和 Example Api-Semantics-Example.md。未新增或运行测试/游戏/故障注入，未同步 Mods、提交、远端写入或线上发布。T5 本地代码完成不等于恢复矩阵已验证，整轮 API/UI 交接仍需剩余选定接口接续。


本批制品记录：RimWorld 1.6 Release 全客户端编译通过（保留既有 NuGet 审计网络/protobuf/wire 警告），ManagedPackageTool 与 Example Release 编译通过。Example 1.0.6 包 14200 bytes，SHA-256 6eff61d5dbc7b4cb3fcba7c8719caa0be105a3d2128ea5a5b36f6960c3004a9f；打包器核对当前 Host/框架程序集引用，pack.py 同步 System.Core 参考程序集输入，不将框架 DLL 放进包。主题 1.0.0/1.1.0 样例的摘要见 t5-local/artifacts.json。没有启动测试项目或游戏。
