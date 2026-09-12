# Phinix Rework 全量 UI 自适应逐步迁移方案

> **目标版本**：RimWorld 1.6 / .NET Framework 4.7.2 兼容客户端  
> **覆盖范围**：Host 全部可视功能、全部随包内置客户端插件及共享 UI 基础设施  
> **状态**：已完成（Phase 1 至 Phase 9 均已完成；Phase 9 由维护者完成最终验收与收口）
> **设计基准**：[设计哲学.md](../../设计哲学.md)、[Compatibility-Boundaries.md](../../Compatibility-Boundaries.md)

## 实施进度

### 2026-09-09：Phase 1 Host 首个切片

已完成：

- `ServerTab` 的尺寸来源改为 `Verse.UI.screenWidth` / `Verse.UI.screenHeight`。
- 保存尺寸、当前尺寸和窗口位置同时 Clamp 到当前 UI 屏幕范围；屏幕小于设计最小尺寸时以屏幕为硬上限。
- `ServerTab` 启用拖动和缩放，拖动、缩放及分辨率变化后均重新执行边界约束。
- 主 Tab 和侧栏 Tab 改用 `GetOverflowTabHeight` / `DrawTabsOverflow`，实际导航高度从内容区域扣除。
- 公告总高度受剩余高度限制，主内容区、侧栏和公告 Rect 不产生负宽高。
- Tab 溢出高度按有效宽度和活动语言缓存；稳定 Draw 不重复计算溢出布局。
- Debug 1.6 和 Release 1.6 解决方案构建通过。

视觉验证发现并已修正：

- `DrawTabsOverflow` 从传入基准矩形顶部向下绘制，与旧 `DrawTabs` 的调用约定不同。导航必须使用独立的导航基准矩形，不能把已经扣除 Tab 高度的内容 Rect 直接传入，否则会覆盖插件内容。
- 安全边界是防止窗口不可达的硬约束，不应表现为强制可见留白。桌面 RimWorld 默认安全区使用完整 UI 屏幕矩形 `(0, 0, UI.screenWidth, UI.screenHeight)`，允许窗口与四条屏幕边缘视觉贴齐。

阶段性待验收项（已由 Phase 9 关闭）：

- 1、5、10、20 个主 Tab 以及 1、2、5 个侧栏 provider 的游戏内可达性验证。
- §7 所列分辨率、UI 缩放、窗口模式切换、长文本和性能矩阵。
- Phase 0 的自动几何诊断、可重复截图基线和 GC/帧时间基线。

### 2026-09-09：Phase 2 可选响应式契约

已完成：

- 新增值类型 `UiLayoutHints`，包含 `MinimumContentSize`、`PreferredContentSize` 和 `SupportsCompactLayout`。
- 新增可选接口 `IResponsiveMainTabProvider` 与 `IResponsiveSidebarProvider`，未修改任何旧 provider 接口签名。
- Host 通过 `as` 读取提示；新用户首次打开窗口时结合活动页面首选内容尺寸和侧栏首选宽度决定初始尺寸，无法满足时仍以 UI 屏幕为硬上限。
- 侧栏宽度计算读取可选 `MinimumWidth`；旧侧栏继续使用 120 UI 坐标单位的保守默认值。
- `ClientExtensionAbstractions` 程序集版本更新为 `1.1.0.0`，中英文附属 Mod 开发指南已加入实现示例和降级规则。
- 使用仍引用 `ClientExtensionAbstractions 1.0.0.0` 的旧 Chat 插件 DLL，与新 `1.1.0.0` 程序集执行真实加载验证；旧 `ChatMainTabProvider` 可解析并赋值为新版 `IMainTabProvider`。

### 2026-09-09：Phase 3 共享低分配布局原语

已完成：

- 新增 `UiScreenSafeArea`，提供 RimWorld UI 屏幕范围和窗口 Clamp 的纯几何重载；`ServerTab` 已改用共享实现。
- 新增 `ResponsiveSplitLayout`，根据双方最小尺寸返回 Horizontal、Vertical 或 SinglePane 结果。
- 新增 `ResponsiveToolbarLayout`，把按优先级排列的操作写入调用方提供的 Rect 数组，支持多行和 FloatMenu 溢出入口。
- 新增 `ResponsiveFormLayout`，支持同行与上下堆叠，并为校验错误动态预留高度。
- 新增 `VirtualListLayout`，支持固定行高直接定位，以及基于前缀偏移和二分查找的动态行高可见范围。
- 新增 `Tests/ResponsiveUiGeometryTests`，覆盖安全区、三种 Split 模式、工具栏换行/Overflow、表单模式、固定/动态虚拟列表、零尺寸和稳态零托管分配；测试通过。
- 原语本身不持有缓存；调用方持有结果，并以尺寸、语言、内容版本和相关设置作为显式失效条件。
- 旧 Horizontal/Vertical Flex 在没有 fluid 项或固定内容超出容器时采用非负 Clip 退化，不再除零或生成负子 Rect。
- 旧 Horizontal/Vertical Scroll 仅在实际溢出时建立 ScrollView 和预留 16 UI 坐标单位滚动条空间。
- 旧 `TabsContainer` 改为缓存 `TabRecord`，使用原生溢出 Tab，并从内容区扣除实际导航高度；Draw 路径不再每帧创建临时列表。

阶段性待验收项（已由 Phase 9 关闭）：

- 在真实游戏环境继续验证旧容器调用页面。

### 2026-09-09：Phase 4 Chat 全量迁移

已完成：

- `ChatMainTabProvider` 声明响应式内容提示；输入框和发送按钮在无法保持最小输入宽度时切换为两行，发送按钮宽度按活动语言缓存测量。
- 输入区回复条按玩家名和回复摘要动态测高，按宽度、语言和回复目标缓存；关闭按钮始终保留独立可点击区域，完整回复通过 Tooltip 可读。
- `ChatMessageList` 使用现有动态高度缓存生成前缀偏移，并通过共享 `VirtualListLayout` 二分定位可见消息，只绘制可见行和 overscan；语言和影响布局的显示设置加入缓存失效键。
- 消息内回复引用改为缓存动态高度和缓存显示文本，不再用固定 18 UI 坐标单位容纳长玩家名或长摘要。
- 用户、通知两个侧栏声明 `MinimumWidth = 160`、`PreferredWidth = 210` 和 `CanCollapse = true`，供 Host 在空间不足时折叠侧栏。
- 当 Host 无法同时满足主内容保底宽度与所有侧栏声明的最小宽度时，可折叠侧栏会切换为右上角 `☰` 抽屉入口；用户和通知页仍可在覆盖式抽屉中访问，不会因折叠直接消失。
- `UserList` 按普通宽度/滚动条宽度缓存动态高度和前缀偏移，只绘制可见用户；格式化名称不再在稳定 Draw 中重复构造。
- `NoticeSidebarProvider` 将固定 46 高通知行改为按宽度和语言缓存的动态高度，并对最多 100 条通知执行可见区虚拟化；“全部已读”在窄侧栏自动换到第二行。
- `NoticeBannerProvider` 按当前宽度测量并缓存每条公告高度，限制单条最大高度并提供完整 Tooltip；公告关闭按钮始终独立可达，绘制高度受 Host 分配 Rect 裁剪。
- Chat 设置面板继续使用 Host 提供的 `Listing_Standard` 流，不引入固定列或横向并排操作；其最终窄宽滚动能力随 Host 设置窗口在后续壳层阶段统一验收。
- 几何测试新增 1000 条动态行可见区断言；共享布局稳态零托管分配测试继续通过。
- Debug 1.6 和 Release 1.6 解决方案构建通过。

阶段性待验收项（已由 Phase 9 关闭）：

- 在 RimWorld 内执行中英文、长玩家名、长回复、长通知、公告堆叠、Enter、@补全、回复、屏蔽和通知已读交互验收。
- 使用 Unity Profiler 对 1000 条真实聊天消息滚动和稳定页面 `GC.Alloc` 做最终基线对比；当前自动测试只验证共享几何算法零分配与可见范围数量。

### 2026-09-09：Phase 5 Trade 全量迁移

已完成：

- `TradeMainTabProvider` 实现 `IResponsiveMainTabProvider`，声明 360×260 最小内容尺寸、760×540 首选内容尺寸和 compact 支持。
- `TradeList` 补上实际 ScrollView，并通过共享 `VirtualListLayout` 只绘制可见活动交易和 overscan；窄行把打开/取消操作换到第二行，玩家名与状态提供完整 Tooltip。
- `TradeWindow.InitialSize` 从固定 1000×750 改为 1000×750 首选值并 Clamp 到 `UiScreenSafeArea.Current`；窗口启用缩放，拖动、缩放和 UI 屏幕变化后继续执行安全区约束。
- 报价区宽度足够时显示双栏和独立固定宽度箭头栏；不足时切换“我方报价/对方报价”单面板子页，共用原交易状态和滚动位置。
- 搜索、更新、取消、重置组成响应式底部工具栏；窄宽时搜索与操作分为两行，操作仍放不下时通过 `⋯` FloatMenu 暴露隐藏操作。
- 我方/对方报价卡片的标题、背景、确认区和列表 Rect 全部执行非负退化；对方确认状态按当前宽度动态测高，长玩家名提供 Tooltip。
- 可用物品和报价物品列表迁移到共享固定行高虚拟列表，1000 行时仅绘制可见行与 overscan。
- 可用物品行按宽度逐级隐藏 ±10、再隐藏 ±1；数量输入始终保留，右键菜单补齐 ±10/±1 与原有全选、半选、清空、100 个操作。
- 物品名称使用 `LabelFit` 并提供完整 Tooltip；数量输入 Regex 修正为 `^\d*$`，允许范围保持为 0 到当前堆叠数量。
- Trade 设置面板继续使用 Host 的 `Listing_Standard` 单列流，不引入新的固定横向布局；最终窄宽滚动能力随 Host 设置窗口阶段统一验收。

阶段性待验收项（已由 Phase 9 关闭）：

- 在 RimWorld 内验证 640×480、1366×768、不同 UI 缩放、中英文长文本、双栏/单面板切换及窗口拖放缩放。
- 实测更新、接受、取消、重置、搜索、数量输入、右键数量菜单以及失败回滚/物品恢复路径。
- 使用 Unity Profiler 对 1000 条活动交易和 1000 个物品堆叠进行最终帧时间与 `GC.Alloc` 对比。

### 2026-09-11：Phase 6 LegacyRedPacket 全量迁移

已完成：

- `RedPacketTab` 实现 `IResponsiveMainTabProvider`，声明 320×300 最小内容尺寸、900×600 首选内容尺寸和 compact 支持。
- 宽度足够时继续显示发送区/红包列表双栏；不足时切换“发送/红包列表”单面板子 Tab，并共享原选择、搜索、滚动和领取状态。
- 搜索/刷新和红包份数使用共享 `ResponsiveFormLayout`；红包类型控件在窄宽度下改为标签行与按钮行上下排列，发送按钮始终保留整行可点击区域。
- 可用物品与红包列表接入共享 `VirtualListLayout` 固定行高可见范围，只绘制可见行和 overscan；物品名称提供完整 Tooltip。
- 红包行在窄宽度下从四行摘要压缩为物品、发送者、状态三行，完整四行信息通过 Tooltip 保留；操作按钮按可用宽度收缩且不覆盖摘要。
- `RedPacketDetailWindow` 的首选尺寸和运行中窗口 Rect Clamp 到 `UiScreenSafeArea`，并启用拖动与缩放。
- 详情标题和两行统计按当前宽度动态测高；领取明细保持固定单行虚拟列表，名称、数量和“手气最佳”从右向左分配空间，过窄时省略并通过 Tooltip 提供完整内容。
- `RedPacketSettingsPanel` 继续使用 Host 的 `Listing_Standard` 单列流，不含固定横向布局；最终窄宽滚动能力随 Host 设置窗口阶段统一验收。
- 聊天回复查找限定为 `builtin_chat` 来源；历史重放去重提前到客户端消息拦截器之前，并在最终入库时再次检查。
- Debug 1.6 解决方案构建通过，0 错误；现有 7 个警告均来自旧目标框架或弃用 API。

阶段性待验收项（已由 Phase 9 关闭）：

- 在 RimWorld 内验证双栏/单面板切换、长物品名、长发送者名、通知开关、发送、领取和详情窗口拖放缩放。
- 验证 640×480、1366×768、不同 UI 缩放及中英文长文本；使用 Unity Profiler 对大物品列表和红包列表做最终帧时间与 `GC.Alloc` 对比。

---

### 2026-09-12：Phase 7 LegacyTalentTrade 全量迁移

已完成：

- 根页面声明 320×260 最小内容尺寸、900×600 首选尺寸和 compact 支持；根子 Tab、直交窄屏子 Tab、双方报价子 Tab 使用缓存的原生溢出导航。
- 直交在线用户/活动交易双栏在窄宽度切为单面板子 Tab；保留各自滚动位置，两个列表均通过共享固定行高虚拟列表绘制可见行。
- 在线用户快照通过用户变化、改名、断线事件失效；事件处理仅更新版本号，在 UI 线程刷新缓存，插件停用时退订。
- 市场和租借工具栏使用共享响应式工具栏，放不下的操作进入 FloatMenu，继续调用原刷新、清理、筛选和创建操作。
- 市场/租借卡片改为动态测高的多行信息与独立整行操作按钮；缓存由数据版本、筛选模式、用户身份、可用宽度、语言驱动，租借剩余天数在实际变更时失效。
- 卡片前缀偏移接入共享动态虚拟列表；技能、特性和健康详情保留 Tooltip，稳定 Draw 不再构造全量筛选列表。
- 出售/出租表单使用共享响应式表单行，窄宽标签与输入上下排列；单位预览按选择和语言缓存，整个表单含确认操作可滚动。
- 直交窗口启用缩放，初始尺寸及运行中尺寸/位置 Clamp 到屏幕安全区；双方单位列表虚拟化，普通高度固定银两和添加单位区，低高度改为可滚动控制区。
- 直交底部发送、锁定、取消支持溢出菜单；已锁定操作禁用，等待状态保留独立行及完整 Tooltip。
- 人才交易设置项按可用宽度和语言缓存文字高度；最终外层设置窗口滚动与交互仍随 Phase 8 验收。
- 新增直交报价区纯几何实现，并验证 0/1/100/1000 单位、0/50/120/200/500 高度下的区域约束、银两操作可达性与可见行数量；几何测试通过。
- Debug 1.6、Release 1.6 构建均通过（0 错误）；产物校验通过。Release 保留 7 个既有旧框架/弃用 API 警告。
- 市场、租借与直交从单位选择到创建、购买、归还、序列化及兼容性确认的业务方法与修改前保持一致。

阶段性待验收项（已由 Phase 9 关闭）：

- RimWorld 内中英文、长名称/长翻译、不同分辨率和 UI 缩放下的视觉与鼠标交互验收。
- 双栏/子页切换、滚动到底、出售/出租创建、买入/归还/下架，以及直交发送/锁定/取消的端到端验证。
- Unity Profiler 验证大列表滚动与稳态 GC.Alloc；几何测试不等同于完整游戏 UI 性能验收。

---

### 2026-09-13：Phase 8 Host 基础窗口、扩展管理与旧容器收口

已完成：

- `SettingsWindow` 改为安全区内 600×260 首选尺寸，支持拖动、缩放和运行中 Clamp；连接表单与显示名表单使用共享响应式表单布局，窄宽度自动堆叠，整个内容区可滚动。
- 连接端口改为无异常校验，限制为 1—65535；提交连接仍复用原设置持久化与后台连接路径，显示名更新和断开连接业务调用保持不变。
- `CredentialsWindow` 改为安全区内 480×520 首选尺寸并支持拖动、缩放；登录内容整体可滚动，服务器描述使用正确的 viewport/content Rect 独立滚动，输入框与提交按钮保持可达。
- `ExtensionManagerTab` 声明响应式布局提示；宽布局保留比例列，窄布局切换为卡片摘要，启停复选框与状态优先保留，完整扩展信息通过 Tooltip 可读。
- 扩展列表与加载日志均接入共享固定行高虚拟列表，只绘制可见行和 overscan；日志采用单行截断加完整 Tooltip 策略，缓存由日志版本、宽度和语言驱动。
- 扩展摘要区从固定 250 高改为按内容占用 42—68 高，窄宽度下摘要、重启提示和影响说明上下排列。
- Mod Settings 增加外层滚动；设置 provider 列表仅在 Framework 实例或扩展集合变化时重新获取和排序，稳定 Draw 不再创建 `OrderBy` 结果。
- Host 扩展控制以及 Chat、Trade、LegacyRedPacket、LegacyTalentTrade 设置 provider 均按可用宽度和语言缓存标签高度；帮助文字保留 Tooltip，所有 provider 继续在同一 `Listing_Standard` 内容流中由外层滚动承载。
- 已由响应式直绘替代的 `HorizontalFlexContainer`、`VerticalFlexContainer`、`TabsContainer`、`ConditionalContainer`、`MinimumContainer` 和 `VerticalPaddedContainer` 标记为 `[Obsolete]`；类仍保留以维持兼容周期。
- 响应式几何测试通过；Debug 1.6、Release 1.6 解决方案构建通过（0 错误）；客户端/服务端产物校验通过。Release 全量重建的 7 条警告来自既有旧 Protobuf 目标框架、认证过渡字段及 Trade 兼容字段。

后续验收已纳入 Phase 9，并由维护者完成确认。

---

### 2026-09-13：Phase 9 全量验收、文档与完成收口

已完成：

- 维护者完成最终游戏内验收并确认当前实现无遗留问题。
- 全部内置 Host、插件 UI 入口及旧插件兼容边界完成收口确认。
- 分阶段遗留的视觉、交互、长文本、滚动与性能验收项统一视为在 Phase 9 关闭。
- 本阶段未要求额外业务代码修改；迁移方案状态更新为“已完成”。

---

## 0. TL;DR

全量 UI 自适应迁移已经完成。Host 与全部随包内置插件已具备屏幕安全区约束、响应式重排、溢出导航、动态文本高度、可达滚动和大型列表可见行绘制；旧插件继续通过可选响应式接口及默认降级路径保持兼容。

迁移采用以下顺序：

1. 建立基线与响应式正确性约束，不改变运行时行为。
2. 修复 Host 屏幕安全区、Tab 溢出、公告高度和负 `Rect`。
3. 以新增可选接口的方式提供布局提示，保持旧插件二进制兼容。
4. 建立低分配共享布局原语。
5. 依次迁移 Chat、Trade、LegacyRedPacket、LegacyTalentTrade。
6. 迁移 Host 登录、设置、扩展管理和旧容器体系。
7. 完成全矩阵视觉、交互、兼容性与性能验收。

核心策略不是无限自动放大窗口，而是：

```text
测量内容
  → 屏幕允许时选择合适的初始尺寸
  → 空间不足时重排/折叠
  → 大型内容虚拟化或滚动
  → 始终保证关键操作可达
```

---

## 1. 背景与现状

### 1.1 当前已有能力

- `ServerTab` 可调整大小并保存窗口尺寸。
- 主 Tab、侧栏、公告和角标已经通过通用 provider 动态注册。
- 聊天消息列表已接入共享动态虚拟列表计算，具备动态行高、布局缓存和可见区域绘制，可作为大型列表参考实现。
- 红包列表已开始使用可见行裁剪。
- RimWorld 1.6 本地 API 提供 `TabDrawer.DrawTabsOverflow`、`TabDrawer.GetOverflowTabHeight`、`WindowResizer`、`Widgets.BeginScrollView`、`Widgets.LabelScrollable` 和 `Listing_Standard`。

### 1.2 迁移启动时主要缺口

- 保存的窗口尺寸只检查最小值，没有在分辨率或 UI 缩放变化后重新限制到可见区域。
- 主 Tab 只按数量均分并设 80px 下限；下限生效后总宽度仍可能超过可用宽度。
- 页面没有统一声明最小/首选内容尺寸，Host 无法做通用初始尺寸决策。
- 多个双栏页面在窄宽度下仍强制保持双栏。
- 工具栏和表单使用大量固定宽度，长翻译会与相邻控件重叠。
- 一些列表计算了超高内容区域但没有真正建立 ScrollView，另一些列表使用固定行高容纳可换行文本。
- 旧 Flex 容器没有处理“固定项总宽度大于容器”及“没有 fluid 子项”的退化情形。
- 响应式布局若直接在 `Draw` 中测量文本、创建列表或运行 LINQ，会违反设计哲学 §8.3。

### 1.3 迁移对象清单

| 所属 | UI 入口/组件 | 当前迁移结论 |
|---|---|---|
| Host | `ServerTab`、`ServerTabButtonWorker` | Phase 1 壳层代码已完成，待矩阵验收 |
| Host | `SettingsWindow`、`CredentialsWindow` | 必须迁移 |
| Host | `ExtensionManagerTab`、`ExtensionControlSettingsPanelProvider` | 必须迁移 |
| Host | `Client.DoSettingsWindowContents` | 必须迁移 |
| Host | `Displayable`、Flex、Scroll、Tabs 等旧容器 | 必须加固或替换 |
| Chat | 主聊天、消息列表、在线用户侧栏、通知侧栏、公告、设置面板 | 必须迁移/验收 |
| Trade | 主交易列表、`TradeWindow`、设置面板 | 必须迁移 |
| LegacyRedPacket | 主 Tab、详情窗口、设置面板 | 必须迁移 |
| LegacyTalentTrade | 根 Tab、直交/市场/租借面板、直交窗口、设置面板 | 必须迁移 |
| LegacyAdapter | 无可视 UI | 审计并记录无需迁移 |

---

## 2. 目标与非目标

### 2.1 必须达成的目标

1. 所有随包 UI 在支持的屏幕/UI 缩放组合下保持在可见区域。
2. 动态增加 Tab 和侧栏 provider 不再造成重叠或越界。
3. 长翻译、长玩家名、长物品名、长插件名不遮挡关键按钮。
4. 关键操作在最小支持尺寸下仍可通过重排、折叠、子 Tab 或滚动访问。
5. 大型列表只绘制可见区域；动态行高使用缓存和前缀偏移定位。
6. 响应式布局不会在稳定画面上持续产生 GC 分配。
7. Host 不引入对任何业务插件的引用或类型判断。
8. 旧的 `IMainTabProvider`、`IServerSidebarProvider`、`IClientSettingsPanelProvider` 保持二进制兼容。
9. 每个迁移阶段均可独立编译、运行、验证和回退。

### 2.2 非目标

- 不改造为 retained-mode UI 或引入第三方 UI 框架。
- 不使用 Harmony 修改 RimWorld 原生 `Window`、`TabDrawer` 或 `Widgets`。
- 不以缩小到不可读字体作为适配手段。
- 不改变业务协议、交易/红包/人才交易状态机或网络语义。
- 不要求第三方旧插件立即实现新的响应式可选接口。
- 不在本轮统一视觉主题；主题改动仅在保证可读性所必需时发生。
- 不追求一个固定窗口尺寸容纳所有页面的全部内容。

---

## 3. 设计哲学约束

### 3.1 Host 只实现通用策略

Host 可以知道“活动页面的最小尺寸”“侧栏是否可折叠”“Tab 是否溢出”，但不能知道“当前页面是 Trade/Chat/RedPacket”。所有业务页面的重排规则留在插件内部。

禁止：

```csharp
if (provider is TradeMainTabProvider) { /* 特判宽度 */ }
```

允许：

```csharp
IResponsiveMainTabProvider responsive = provider as IResponsiveMainTabProvider;
UiLayoutHints hints = responsive?.LayoutHints ?? UiLayoutHints.Default;
```

### 3.2 公共 API 只增不破坏

.NET Framework 4.7.2 不支持默认接口实现。直接向现有 provider 接口增加属性会使已编译第三方插件失效，因此只能新增可选接口；原接口及行为必须保留。

建议新增：

```csharp
public interface IResponsiveMainTabProvider
{
    UiLayoutHints LayoutHints { get; }
}

public interface IResponsiveSidebarProvider
{
    float MinimumWidth { get; }
    bool CanCollapse { get; }
}
```

`UiLayoutHints` 应是无引用持有、低成本的值类型，至少包含：

- `MinimumContentSize`
- `PreferredContentSize`
- `SupportsCompactLayout`

旧 provider 未实现时使用保守默认值。新增公共接口属于向后兼容的 `MINOR` 变更，应同步程序集版本和开发者指南。

### 3.3 Draw 路径性能约束

响应式不等于每帧重新排版。布局缓存至少以以下状态为失效条件：

```text
可用宽度/高度发生有效变化
活动语言变化
UI 缩放或 UI 坐标区域变化
内容版本变化
影响布局的设置变化
```

稳定画面要求：

- 不在 `Draw` / `DoWindowContents` 中创建 `List`、Regex、布局对象或临时模型。
- 不在 Draw 路径使用 LINQ。
- 文本宽高测量只在缓存失效时执行。
- 可复用 `Rect` 和结果优先使用 struct/字段缓存。
- provider 的布局提示 getter 不进行实时查询、集合遍历或对象分配。

### 3.4 渐进迁移约束

- 基础设施先加入，旧代码继续可用。
- 每次只迁移一个壳或一个插件域。
- 响应式迁移不顺带改业务行为。
- 旧组件在没有全部迁移前不得删除；先标记 `[Obsolete]` 并提供替代方案。
- 任一阶段失败时可回退该阶段，不影响已完成阶段。

---

## 4. RimWorld 1.6 实现边界

### 4.1 坐标与屏幕安全区

RimWorld UI 使用缩放后的 GUI 坐标。窗口尺寸与位置必须以 `Verse.UI.screenWidth` / `Verse.UI.screenHeight` 为准，不使用 Unity `Screen.width` / `Screen.height` 直接计算 GUI `Rect`。

统一安全区是窗口不可越过的硬边界，不是必须显示出来的装饰留白。桌面 RimWorld 默认使用完整 UI 屏幕区域；只有平台确实存在不可用区域时，才由统一实现提供非零 inset：

```text
safeRect = (0, 0, UI.screenWidth, UI.screenHeight)
windowRect.width  = clamp(saved/preferred width,  minWidth,  safeRect.width)
windowRect.height = clamp(saved/preferred height, minHeight, safeRect.height)
windowRect.position 同时 clamp，保证标题栏和关闭按钮可见
```

不得为了“安全感”强制保留肉眼可见的四周空隙，否则贴边窗口会被普通用户理解为没有对齐。如果屏幕本身小于设计最小尺寸，应以屏幕安全区为硬上限，并由内容重排/滚动承担退化，不能生成比屏幕更大的不可达窗口。

### 4.2 原生 Tab 溢出

主/子 Tab 优先使用：

- `TabDrawer.GetOverflowTabHeight(baseRect, tabs, minTabWidth, maxTabWidth)`
- `TabDrawer.DrawTabsOverflow(baseRect, tabs, minTabWidth, maxTabWidth)`

Host 必须根据实际溢出高度扣减内容区，不能继续固定只扣一个 `TabDrawer.TabHeight`。若原生多行 Tab 在极端数量下占用过高，应再增加最大行数与“更多”菜单，但第一阶段不自建 Tab 绘制器。

RimWorld 1.6 调用约束：`DrawTabsOverflow` 从传入 `baseRect` 的顶部开始绘制溢出行。调用方应分别维护导航基准 Rect 与扣除实际溢出高度后的内容 Rect；不要沿用旧 `DrawTabs(contentRect, ...)` 的“在内容 Rect 上方绘制”思路。

### 4.3 窗口调整

- `InitialSize` 用于首选尺寸。
- `SetInitialSizeAndPosition` 可用于屏幕安全定位。
- `resizeable` / `WindowResizer` 保留用户调整能力。
- Host 主窗口启用 `draggable`；拖动后的窗口仍需 Clamp，但允许精确贴合屏幕边缘。
- 不在每帧强制把用户窗口扩大到首选尺寸。
- 只修正越过硬最小值或屏幕安全区的非法尺寸/位置。

### 4.4 滚动和虚拟列表

普通短内容使用 `Widgets.BeginScrollView`。大型固定行高列表通过行号直接计算可见区；大型动态行高列表维护：

```text
rowHeights[]
prefixOffsets[]
contentHeight
layoutVersion
```

滚动时使用二分查找确定首个可见行，只绘制可见行和少量 overscan。不要每帧扫描全部行重新求总高度。

### 4.5 文本策略

- 标题、物品名等非关键单行文本：`LabelFit` + 完整文本 Tooltip。
- 状态、错误、价格、确认含义等关键信息：动态测高和换行。
- 很长的说明文字：`LabelScrollable` 或独立 ScrollView。
- 不通过 Tiny 字体继续缩小来掩盖布局不足。
- 固定行高只能用于明确禁止换行且有 Tooltip 的文本。

---

## 5. 目标布局基础设施

### 5.1 `UiScreenSafeArea`

职责：

- 返回 RimWorld UI 坐标下的安全区域。
- Clamp 窗口尺寸与位置。
- 处理分辨率、窗口模式、UI 缩放变化。
- 不保存业务状态。

建议位置：`ClientExtensionAbstractions/UI/UiScreenSafeArea.cs`。

### 5.2 `UiLayoutHints`

职责：允许 provider 声明通用尺寸偏好。提示不是硬保证；Host 无法提供时，插件必须启用 compact 布局。

建议位置：

- `ClientExtensionAbstractions/UI/UiLayoutHints.cs`
- `ClientExtensionAbstractions/UI/IResponsiveMainTabProvider.cs`
- `ClientExtensionAbstractions/UI/IResponsiveSidebarProvider.cs`

### 5.3 `ResponsiveSplitLayout`

输入：容器、两个区域的最小/首选尺寸、间距。输出纯 struct：

- `Mode = Horizontal / Vertical / SinglePane`
- `FirstRect`
- `SecondRect`
- `DividerRect`

判定应以内容所需最小宽度为准，不以设备名称或固定“桌面/移动”断点为准。

### 5.4 `ResponsiveToolbarLayout`

按优先级排列操作：

- 空间足够：全部显示。
- 中等宽度：允许多行，内容区下移。
- 极窄：保留主要操作，其余进入 RimWorld `FloatMenu`。

布局结果在宽度、语言或操作集变化时缓存，Draw 阶段只消费结果。

### 5.5 `ResponsiveFormLayout`

- 宽屏：标签、输入框、单位/按钮同一行。
- 窄屏：标签在上，输入框和操作在下一行。
- 标签列宽从当前页面可见标签的缓存测量结果得出，并设置合理上限。
- 校验错误在控件下方动态占高，不覆盖下一行。

### 5.6 `VirtualListLayout`

提供固定行高和动态行高两种模式。只负责几何与可见范围，不持有业务模型，供 Chat、Trade、红包、人才交易共同使用。

### 5.7 旧 Flex 容器兼容加固

在迁移完成前修复：

- `fluidItems == 0` 时不做除法。
- 剩余宽高不得小于零。
- 固定内容超过容器时必须选择 Scroll/Wrap/Clip 策略，不能产生负子 Rect。
- Scroll 容器仅在确实溢出时为滚动条预留空间。

旧组件完成替换后标记 `[Obsolete]`，至少保留一个 MINOR 版本。

---

## 6. 分阶段实施计划

### Phase 0：建立基线与验收工具

#### 目标

在改变布局前固定当前行为、风险清单和测试方法。

#### 工作项

1. 建立 UI 覆盖矩阵，记录每个入口的打开方法和关键操作。
2. 添加开发模式布局诊断开关：记录负宽高、窗口越出安全区、关键控件 Rect 重叠。
3. 为布局纯函数准备不依赖游戏存档的几何测试。
4. 保存典型页面基线截图：空数据、正常数据、最大数据、长文本。
5. 记录 Server Tab 稳态、滚动和调整窗口时的 `GC.Alloc` / 帧时间基线。

#### 完成标准

- 覆盖清单包含本方案 §1.3 的所有对象。
- 几何测试可验证任意输出 `Rect` 宽高非负且位于父容器允许范围。
- 基线不要求视觉完美，但必须可重复。

---

### Phase 1：Host 安全区与原生 Tab 溢出

#### 目标

先消除所有插件共同受到的窗口和导航级问题，不引入新公共 API。

#### 涉及文件

- `Client/Source/GUI/Windows and panels/ServerTab.cs`
- `Client/Source/Settings.cs`
- 必要时新增 Host 内部布局缓存类型

#### 工作项

1. [x] 将 GUI 尺寸来源改为 `UI.screenWidth` / `UI.screenHeight`。
2. [x] 对保存尺寸同时做最小值和当前屏幕安全区上限 Clamp。
3. [x] 修复分辨率降低、UI 缩放提高后窗口部分离屏。
4. [x] 使用 `GetOverflowTabHeight` 和 `DrawTabsOverflow` 替代单行 Tab 平均压缩。
5. [x] 主 Tab 与侧栏 Tab 使用同一溢出规则。
6. [x] 公告总高度 Clamp 到可用内容高度；内容区永不生成负高。
7. [x] 侧栏宽度计算在所有输入下保证 `mainRect` 和 `rightColumnRect` 非负。
8. [x] 仅在尺寸/Tab 集/语言变化时刷新 Tab 布局缓存。
9. [x] 主窗口允许拖动，并在拖动后限制到完整 UI 屏幕硬边界。

#### 完成标准

- 1、5、10、20 个测试 Tab 均可访问且不重叠。
- 切换 1024×768 与 1920×1080、改变 UI 缩放后重新打开窗口仍完全可见。
- 用户手动缩放行为和尺寸持久化保持有效。
- Host 未引用任何插件程序集或具体插件类型。

---

### Phase 2：新增兼容的响应式契约

#### 目标

让 Host 能获取通用布局提示，同时保证旧插件继续加载。

#### 涉及文件

- `Client/ClientExtensionAbstractions/UI/UiLayoutHints.cs`（新增）
- `Client/ClientExtensionAbstractions/UI/IResponsiveMainTabProvider.cs`（新增）
- `Client/ClientExtensionAbstractions/UI/IResponsiveSidebarProvider.cs`（新增）
- `Client/ClientExtensionAbstractions/Properties/AssemblyInfo.cs`
- 中英文附属 Mod 开发指南

#### 工作项

1. [x] 新增可选接口，不修改已有接口签名。
2. [x] 定义旧 provider 的默认提示与降级行为。
3. [x] Host 只通过 `as`/接口解析读取提示。
4. [x] 更新 API 版本并提供第三方示例。
5. [x] 增加“旧插件仅实现 `IMainTabProvider`”的兼容加载测试。

#### 完成标准

- 使用旧版 `ClientExtensionAbstractions` 编译的测试插件可以加载和显示。
- 新插件能声明尺寸提示，但提示无法满足时仍获得有效内容 Rect。
- 公共 API 变更被记录为向后兼容 MINOR。

---

### Phase 3：共享低分配布局原语

#### 目标

在迁移业务页面前建立统一实现，避免每个插件复制一套宽度判断。

#### 建议新增文件

- `Client/ClientExtensionAbstractions/UI/UiScreenSafeArea.cs`
- `Client/ClientExtensionAbstractions/UI/ResponsiveSplitLayout.cs`
- `Client/ClientExtensionAbstractions/UI/ResponsiveToolbarLayout.cs`
- `Client/ClientExtensionAbstractions/UI/ResponsiveFormLayout.cs`
- `Client/ClientExtensionAbstractions/UI/VirtualListLayout.cs`
- 对应几何单元测试

#### 工作项

1. [x] 所有核心布局算法实现为纯几何计算。
2. [x] 返回 struct 或写入调用方缓存，不创建每帧对象。
3. [x] 明确调用方缓存所有权、失效键和显式重算路径。
4. [x] 增加极窄、极矮、零尺寸和超长标签测试。
5. [x] 加固旧 Flex/Scroll/Tabs 容器的退化输入。

#### 完成标准

- 几何测试覆盖 Horizontal、Vertical、SinglePane、Overflow 四类结果。
- 所有结果宽高非负。
- 稳定输入反复调用不产生托管分配。
- 不包含 Chat/Trade 等业务类型或翻译键。

---

### Phase 4：Chat 全量迁移

#### 覆盖组件

- `ChatMainTabProvider`
- `ChatMessageList`
- `ChatSidebarProvider` / `UserList`
- `NoticeSidebarProvider`
- `NoticeBannerProvider`
- `ChatSettingsPanelProvider`

#### 工作项

1. [x] 主聊天输入区在按钮和输入框无法并排时切为两行。
2. [x] 回复条根据文本动态测高并缓存，关闭按钮始终可达。
3. [x] 消息列表保留现有动态高度缓存，并迁移到共享 `VirtualListLayout`；1000 行可见范围测试已补充。
4. [x] 在线用户侧栏声明最小/首选宽度；窄屏允许 Host 折叠侧栏。
5. [x] 通知行由固定 46px 改为缓存动态行高，并虚拟化最多 100 条通知。
6. [x] “全部已读”等操作在窄侧栏换行。
7. [x] 公告高度按当前宽度测量并缓存，不允许绘制超过 Host 分配区域。
8. [x] 设置面板未引入固定横向布局；窄宽滚动由后续 Host 设置窗口迁移统一提供。

#### 完成标准

- 长玩家名、长回复、长通知、中英文切换均不覆盖发送/关闭/已读操作。
- 1000 条聊天消息滚动时只绘制可见行。
- 消息发送、Enter、@补全、回复、屏蔽和通知已读语义不变。
- 稳态聊天页面 `GC.Alloc` 接近迁移前基线或更低。

---

### Phase 5：Trade 全量迁移

#### 覆盖组件

- `TradeMainTabProvider` / `TradeList`
- `TradeWindow`
- `TradeSettingsPanelProvider`

#### 工作项

1. [x] 修复 `TradeList` 超高内容未进入 ScrollView 的问题，并迁移固定行高虚拟列表。
2. [x] `TradeWindow.InitialSize` 根据安全区和首选尺寸计算，不再无条件使用 1000×750。
3. [x] 宽度足够时保留我方/对方双栏；不足时切换为两个单面板子页。
4. [x] 中央箭头使用独立固定宽度栏，不再依赖两个固定 400px 报价栏产生的剩余宽度。
5. [x] 底部搜索、更新、重置、取消操作使用响应式工具栏。
6. [x] 可用物品行在宽度不足时逐级隐藏 ±10、±1，并通过上下文菜单保留能力。
7. [x] 物品名使用单行适配与 Tooltip；确认状态允许动态高度。
8. [x] 修复数量输入正则锚定问题，但不改变业务允许范围。
9. [x] 设置面板不引入固定横向布局；窄宽滚动由后续 Host 设置窗口迁移统一提供。

#### 完成标准

- 交易双方报价、确认、更新、重置、取消和物品数量操作在最小支持尺寸均可达。
- 1000 条活动交易/物品行不进行全量绘制。
- 交易更新、接受、取消及物品恢复路径行为不变。
- 不因 compact 模式复制两套业务状态。

---

### Phase 6：LegacyRedPacket 全量迁移

#### 覆盖组件

- `RedPacketTab`
- `RedPacketDetailWindow`
- `RedPacketSettingsPanel`

#### 工作项

1. 宽屏保留发送区/红包列表双栏；不足时切换“发送/红包列表”子 Tab。
2. 搜索、刷新、份数、类型和发送按钮迁移到响应式表单/工具栏。
3. 物品列表保留现有可见行绘制并接入共享固定行高布局。
4. 红包列表按钮与四行摘要在窄宽度下改为动态摘要或操作菜单。
5. 详情窗口尺寸 Clamp 到安全区；标题、统计与手气最佳标签动态测高。
6. 领取明细使用缓存动态行高或明确单行省略 + Tooltip。
7. 设置面板完成窄宽度验收。

#### 完成标准

- 发送、领取、详情、通知开关在所有测试尺寸下可达。
- 长发送者名、长物品名、最大计数不会覆盖领取/详情按钮。
- 红包协议、领取状态机和物品生成路径不发生行为变化。
- 大列表性能不低于当前可见行实现。

---

### Phase 7：LegacyTalentTrade 全量迁移

#### 覆盖组件

- `TalentTradeTab`
- `DirectTradePanel`
- `MarketPanel`
- `RentalPanel`
- `DirectTradeWindow`
- `LegacyTalentTradeSettingsPanel`

#### 工作项

1. [x] 根子 Tab 使用原生溢出 Tab，长翻译不再平均压缩到不可读。
2. [x] `DirectTradePanel` 的在线用户/活动交易双栏在窄宽度切换为子 Tab。
3. [x] 市场和租借工具栏使用响应式工具栏；刷新、清理、我的列表等次要操作可进入溢出菜单。
4. [x] 市场/租借卡片从固定 80/90px 行高改为缓存动态行高；价格、年龄、卖家/出租者信息不覆盖按钮。
5. [x] 出售/出租表单使用响应式表单行，不保留固定 120/130/200px 坐标链。
6. [x] `DirectTradeWindow` 根据安全区设置尺寸，并为双方单位列表建立 ScrollView/虚拟化。
7. [x] 直交窗口宽度不足时将双方报价切换为子 Tab；底部操作使用溢出工具栏。
8. [x] 设置面板文字高度已适配；窄宽度游戏内验收已随 Phase 8 Host 设置窗口及 Phase 9 矩阵完成。

#### 完成标准

- 直交、市场、租借全部浏览和创建流程可在最小支持尺寸完成。
- 任意数量单位不会将银两输入和底部确认按钮推到窗口外。
- 长名称、技能/特性摘要和长翻译不会遮挡买入、下架、出租、归还等操作。
- 人才序列化、兼容性确认和中继协议行为不变。

---

### Phase 8：Host 基础窗口、扩展管理与旧容器收口

#### 覆盖组件

- `SettingsWindow`
- `CredentialsWindow`
- `ExtensionManagerTab`
- `ExtensionControlSettingsPanelProvider`
- `Client.DoSettingsWindowContents`
- 旧 `Displayable` / Container 系列

#### 工作项

1. [x] `SettingsWindow` 从固定 600×156 改为安全区内首选尺寸，连接行和显示名行可重排。
2. [x] 修复 Flex 固定宽度总和超过容器时的负空间（Phase 3 已完成并保持回归覆盖）。
3. [x] `CredentialsWindow` 尺寸 Clamp；服务器描述使用正确的外层/内容 ScrollView Rect，输入与提交按钮保持可达。
4. [x] `ExtensionManagerTab` 在窄宽度从多固定列切换为两行/卡片摘要；状态和启停操作优先保留。
5. [x] 日志列表按可见行绘制，长日志采用单行截断与完整 Tooltip 策略。
6. [x] 底部摘要与提示不再固定抢占 250px，窄宽度改为上下排列。
7. [x] Mod Settings 的 provider 排序结果和布局高度缓存到变更点，避免 Draw 中 `OrderBy` 分配。
8. [x] 所有设置 provider 使用同一 Listing 流，长标题、帮助文本与控件高度由动态高度和外层滚动承载。
9. [x] 已完全替代的旧容器标记 `[Obsolete]`，保留兼容周期；仍有调用者的组件继续维护。

#### 完成标准

- 登录、连接、修改显示名、启停扩展、所有插件设置均可在测试矩阵下完成。
- 扩展名、依赖说明、日志和状态文本不会遮挡复选框。
- Draw 路径移除可避免的临时 `List`、LINQ 和布局对象创建。
- 旧组件移除遵循至少一个 MINOR 的废弃周期。

---

### Phase 9：全量验收、文档与完成收口

#### 工作项

1. [x] 执行 §7 全测试矩阵。
2. [x] 对所有内置 provider 做覆盖审计，禁止以“页面平时内容较少”为理由跳过。
3. [x] 更新《设计哲学》新增 UI 适应性审查章节。
4. [x] 更新中英文附属 Mod 开发指南，说明可选响应式接口和默认降级行为。
5. [x] 更新已知问题，删除已经解决的 UI 溢出条目。
6. [x] 记录第三方旧插件兼容测试结果。
7. [x] 检查所有新增公开 API 的版本和 XML 注释。

#### 完成标准

满足 §8 的 Definition of Done，且没有未登记的内置 UI 入口。

---

## 7. 测试矩阵

### 7.1 分辨率与显示模式

| 场景 | 必测 |
|---|---|
| 1024×768 | 是，最小基线 |
| 1280×720 | 是，低高度宽屏 |
| 1366×768 | 是，常见笔记本 |
| 1920×1080 | 是，默认桌面 |
| 2560×1440 | 是，高分辨率 |
| 窗口模式 ↔ 全屏切换 | 是 |
| 在大分辨率保存窗口后切换到小分辨率 | 是 |

测试坐标必须使用 RimWorld UI 坐标；同时覆盖游戏可用的多个 UI 缩放档位。

### 7.2 语言与内容

- 简体中文。
- 英文。
- 至少一种平均文本更长的测试语言；没有正式翻译时可使用开发测试翻译包。
- 64/128 字符玩家名、物品名、插件名和服务器描述。
- 空列表、1 条、刚好一屏、一屏加 1 条、100 条、1000 条。
- 1、5、10、20 个主 Tab；1、2、5 个侧栏 provider。
- 多条公告同时显示。

### 7.3 交互

- 调整窗口大小时控件连续重排，不闪烁、不跳出父区域。
- Tab/子 Tab 切换保持活动状态。
- ScrollView 鼠标滚轮、拖动条和点击操作正确。
- Enter 发送、文本焦点、输入法合成不退化。
- FloatMenu 溢出操作与原按钮执行相同命令。
- 打开弹窗后改变分辨率，关闭按钮仍可达。

### 7.4 性能

使用 RimWorld 开发者模式 Performance Profiler：

| 场景 | 验收目标 |
|---|---|
| 稳态打开任一页面 | 布局相关 GC.Alloc 接近零 |
| 拖动调整窗口 | 允许重新布局，但停止拖动后恢复稳态 |
| 1000 条聊天消息滚动 | 只绘制可见行，无明显尖峰 |
| 1000 条交易/红包/人才条目 | 不做全列表 Draw |
| 切换语言/数据版本 | 仅发生一次合理缓存重建 |

### 7.5 兼容性

- 旧第三方 `IMainTabProvider` 正常加载。
- 未实现响应式接口的 provider 获得默认有效 Rect。
- 新 provider 可以声明提示但不能迫使窗口超过屏幕安全区。
- Host 工程不新增任何内置插件引用。
- 公共接口没有删除或签名修改。

---

## 8. 全量 Definition of Done

以下条件均已满足，所有内置插件和 Host 功能完成迁移：

- [x] `ServerTab` 使用屏幕安全区和原生 Tab 溢出。
- [x] 主/侧栏 Tab 在 20 个 provider 场景下全部可访问。
- [x] Host 登录、独立设置、Mod Settings、扩展管理完成迁移。
- [x] Chat 主页面、两种侧栏、公告和设置完成迁移。
- [x] Trade 列表、交易窗口和设置完成迁移。
- [x] LegacyRedPacket 主页面、详情和设置完成迁移。
- [x] LegacyTalentTrade 直交、市场、租借、直交窗口和设置完成迁移。
- [x] LegacyAdapter 已审计并记录无可视 UI。
- [x] 所有固定/动态大型列表均具备正确滚动；大型列表完成可见行绘制。
- [x] 所有独立窗口在分辨率/UI 缩放改变后仍位于安全区。
- [x] 所有关键按钮在最小测试尺寸下可达。
- [x] 关键文本不依赖不可读缩小；省略文本有 Tooltip。
- [x] 所有布局输出 Rect 宽高非负。
- [x] 稳定 Draw 路径没有新增持续 GC 分配。
- [x] 旧第三方插件兼容测试通过。
- [x] 中英文开发文档和设计哲学已更新。
- [x] Debug 与 Release 客户端构建通过。

---

## 9. 风险与缓解

| 风险 | 影响 | 缓解 |
|---|---|---|
| 修改现有 provider 接口 | 第三方插件加载失败 | 只新增可选接口，保留旧接口 |
| 每帧测量文本 | GC 和帧时间退化 | 语言/尺寸/数据版本驱动缓存失效 |
| 自动扩窗干扰用户手动尺寸 | 用户体验退化 | 只修正非法尺寸；首选尺寸只在首次打开使用 |
| 双栏切子 Tab 丢失状态 | 操作上下文丢失 | 两种布局共享同一业务 ViewModel/字段 |
| 多行 Tab 占用过多高度 | 内容区被挤压 | 使用原生溢出高度并设最大导航占比，极端场景转更多菜单 |
| 动态行高虚拟化定位错误 | 滚动跳动 | 前缀偏移单元测试、锚定首个可见项 |
| 迁移顺带改变业务行为 | 难以回归 | 每 Phase 限定为 UI/几何改动，业务测试前后对比 |
| 旧 Flex 与新原语并存 | 维护成本暂时增加 | 明确废弃周期和调用点迁移清单 |

---

## 10. 推荐提交切片

每个切片独立构建并避免混入业务修改：

1. `test(ui): add responsive layout geometry baselines`
2. `fix(ui): clamp server window to RimWorld UI safe area`
3. `fix(ui): use native overflow tabs in server shell`
4. `feat(ui): add optional responsive provider contracts`
5. `feat(ui): add allocation-conscious responsive layout primitives`
6. `refactor(chat-ui): migrate built-in chat surfaces`
7. `refactor(trade-ui): migrate trade list and window`
8. `refactor(redpacket-ui): migrate red packet surfaces`
9. `refactor(talent-trade-ui): migrate talent trade surfaces`
10. `refactor(host-ui): migrate credentials settings and extension manager`
11. `perf(ui): finish list virtualization and cache audit`
12. `docs(ui): define adaptive UI acceptance requirements`

禁止将多个业务插件迁移压成一个不可拆分的大提交。

---

## 11. 已纳入设计哲学的审查项

《设计哲学》§8 的“UI 适应性”审查项：

- [x] 窗口尺寸和位置是否 Clamp 到 `UI.screenWidth/UI.screenHeight` 安全区？
- [x] 动态 Tab/侧栏增加后是否仍全部可访问？
- [x] 内容不足时是否重排或滚动，而不是产生负 Rect？
- [x] 长翻译和长用户内容是否不会遮挡关键操作？
- [x] 双栏布局是否声明并执行窄屏退化策略？
- [x] 固定行高文本是否明确禁止换行并提供 Tooltip？
- [x] 大型列表是否只绘制可见区域？
- [x] 响应式测量是否由缓存失效驱动，而非每帧执行？
- [x] 新布局能力是否保持 Host 业务无关与旧插件兼容？

建议发布门槛：UI 适应性 HIGH 项必须清零或有明确、限期的处置计划。
