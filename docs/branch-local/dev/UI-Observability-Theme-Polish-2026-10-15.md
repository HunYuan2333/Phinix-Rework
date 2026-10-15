# UI 可观察性与主题统一（本地切片）

用户要求：不改变布局与业务，只增强消息组边界、控件状态和主题一致性。保留此前商店主题筛选与 Common 卸载修复工作。

## 实现

- Chat owner 在不同普通发言人之间声明 `chat.speakerSeparator`；系统通知不成为普通发言人。同一人连续消息不加横线。公共 FeedRow 新增可选 `WithLeadingSeparator` 数值投影：renderer 只画现有间隔中的 1px 线，不改变测量、排序、合组时限、时间戳、点击或业务。原自绘路径也保留对应显示。
- 公共列表在没有 owner 自定义 hover role 时，使用 `ui.navigation.selected` 区分选中与普通悬停。已有 owner 配色优先级保留。
- 完整设置页补齐内容区背景，沿用现有主题捕获；快捷设置与公共主题编辑器没有第二套皮肤。当时仍保留原生窗口外框；下文“窗口全幅背景补漏”进一步修正。其他模组界面不受影响。
- 公共 checkbox/slider 在原有标记/滑块位置补主题轮廓。输入、命中区域、值、声音和尺寸仍由原生控件处理，不创建重复控件。
- 1.7 公共滚动区域在原生 scroll group 结束后补画主题槽/滑块，复用原生 16-unit 位置与比例。只在 Repaint 绘制，不接管滚轮、拖拽、ControlID、滚动栈，不修改全局 skin。新增 `ui.scroll.track/thumb/hover`，旧主题经现有角色 fallback。1.6 的滚动条几何不同，本轮保留原生滚动条外观；其余改动共用。
- Tokyo Night/Daylight 0.1.1：补齐四个新增颜色键，共 92 色与 2 参数；降低自己的聊天气泡底色面积感，亮色 surface 更中性，选中/悬停与滚动条颜色单独配置。两个旧 ZIP 保留，不覆盖已安装主题和个人设置。

## 证据与边界

本机实物：RimWorld 1.7.4979 rev109。本地反编译 `UnityStubs.dll` 的 `UnityEngine.GUI.BeginScrollViewImpl/HandleScrollbars` 确认原生槽与滑块使用固定深色，参数中的 GUIStyle 不提供这里的换色能力；因此黑槽并非主题包缺键。对照 1.6 `Verse.Widgets.CheckboxLabeled/HorizontalSlider` 验证装饰没有迁移其原生命中几何。反编译结果仅在 /tmp，不提交。

`ClientPresentationFeedRow` 的可选属性/方法及 `ClientWidgets.ScrollbarDecoration` 为增量能力；现有构造签名、CLR identity 与当前 API3 插件绑定不变。Host/Chat 同包构建，三个维护插件联合编译；正式对外开放新成员前，必须以最终发行的 Host/API 能力范围声明新调用方的最低版本，不承诺在旧 Host 上调用新增成员。

未改自定义富文本昵称中的用户颜色、物品贴图、存档、交易/红包权威或动作确认语义。未宣称此前一次设置卡死已修复：该日志未提供主线程栈，仍需复现取证。

## 验证

- 公共契约/renderer 绘制桩：364 项检查，包含分隔线复制、无高度变化、真实绘制和默认选中角色。
- 实际 1.7 绘制 API + fake backend：4979、保留的 4977 均 1142 项检查；新增滑块位置、尺寸、无溢出与非 Repaint 不绘制。
- 新主题包真实 parser/builder 校验、主题源与桩白盒：390 项检查；94 键切换、导出/重载、持久化恢复及卸载。
- 两版完整构建与三个维护插件联合构建、最终分发审计在隔离输出目录执行；后续最终结果追加。没有游戏/GPU 验收，没有提交原始日志、游戏 DLL 或反编译源码。

最终两版与三个插件联合构建通过，公开资源/Workshop ID/私有引用排除与程序集闭包审计通过。主模组本地 ZIP SHA256：`be93ee6158c81991620257f7297f713653cf6eb87bb6da3f6b51ef345d3e8a15`。测试包位于工作区父目录 `local-compatibility-analysis/ui-observability-20261015`，未安装、推送或发布。托管启动/商店白盒另通过 3407 项断言；游戏显示仍待用户验收。

## 包装工具修复

使用真实 1.7 安装目录和独立 Harmony 模组引用时，旧 stager 误要求 `0Harmony.dll` 存在于游戏根目录，造成编译成功后打包失败。新增明确 Harmony 路径传递，要求哈希与该 graph 的原始 reference fingerprint 相同；仍不打包 Harmony。修正过时 STATUS 中“net10 包仍受 net472-only schema 阻断”的描述。

## 窗口全幅背景补漏（用户浅色截图后续）

此前只在 `DoWindowContents` 填充背景，遗漏主题编辑器的整个背景，且快捷设置等窗口仍留下原生深色外边距；主窗口自己的圆角底下也还画着原生方形背景。不能通过继续调整 Daylight 配色解决。

本地反编译实际 1.7.4979 `Verse.Window.InnerWindowOnGUI`：调用 `IWindowDrawing.DoWindowBackground(windowRect.AtZero())` 后，才绘制关闭按钮、执行 `rect.ContractedBy(Margin)`（默认 18）并进入内容 group。1.6 `Window(IWindowDrawing customWindowDrawing = null)` 具有同一挂载点；最终联合编译核验两边真实签名，不修改游戏或全局 skin。

- 公共 game bridge `ClientWidgets.cs` 增加 `ClientThemeWindowDrawing : IWindowDrawing`：仅用现有主题背景、分隔线与圆角绘制整个窗口；关闭、group、灰显等委托 `DefaultWindowDrawing`，不自行重写 WindowStack、焦点、关闭或拖拽逻辑。每次绘制重新获取当前主题，支持打开窗口时热应用。
- Host 的快捷/完整设置、认证、扩展管理、主题编辑/选择/导出和公共确认窗口使用此公共绘制入口；商店本地文件/主题窗口、独立商店、库存交付选择与交易报价窗口也接入。主题从各 owner 已有公开服务传入，不从插件反向调用 Host 内部实现。
- `ServerTab` 已自己全幅绘制圆角，故关闭其 `doWindowBackground`，消除角部露出的原生方形底板。原生窗口阴影、关闭与缩放控件保留；没有重排控件或修改现有 18-unit 边距。
- 不在所有 presentation session 中无条件填底，避免嵌套 feed、composer 和侧栏覆盖父容器背景。没有新增第二套主题。
- 新增公开 game bridge 类型是增量能力；新调用者必须跟随包含该能力的 Host 分发，不声称旧 Host 支持该新类型。本轮不发布插件新包。

白盒 371 项通过（包含新增 7 项全幅覆盖、原生底板替换、热应用重新捕获、GUI 状态恢复、原生关闭委托与内容 group 对称检查）。GPU/游戏窗口的实际圆角仍待用户确认，不能以白盒代替实机。

窗口补漏最终 1.6/1.7 Host 与三个维护插件联合编译、分发程序集与资源审计通过。新本地包 `local-compatibility-analysis/themed-windows-20261015/Phinix-Rework-themed-windows-dual.zip` SHA256 `7654fb2ef1dc7376f56dd5b69dba9a22f36af34bf5a19c7647755475e7bdbbc9`。旧包保留，未自动安装、提交、推送或发布。
