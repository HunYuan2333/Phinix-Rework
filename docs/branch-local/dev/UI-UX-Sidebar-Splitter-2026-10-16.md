# 右侧区域拖动分配实施记录

日期：2026-10-16；用户要求先实现左右分配，再继续其余UX任务。承接UX2-01/02未提交工作树，Host HEAD仍为 `27878e7fb78c11fec4f390db317dc0db5b0b10c9`，分支 `codex/uiux-release-main`。现有修改、4个fixture DLL及各子模块均保留。未安装、提交、推送或发布。

## 使用与边界

聊天主体与右侧区域之间原有8逻辑像素间隔增加竖向握柄，整个间隔均可按住鼠标左键左右拖动。悬停提示并提亮，拖动即时更新；不要求先进入设置。固定本人区、用户/通知导航及当前sidebar内容整体改变宽度。Host不依赖Chat类型，其他主页面及第三方sidebar得到相同能力。

比例保存在Host本地设置 `serverSidebarRatio`，默认0表示沿用provider首选宽度；切tab/sidebar共用该比例，关闭主窗随现有设置写入，拖动不逐帧写磁盘。旧配置缺失此键可正常读取；不更改游戏存档、业务协议、插件SDK合同或名字提交规则。

主内容至少480逻辑像素，侧栏至少 `max(120, provider.MinimumWidth)`：当前用户页180、通知页160。可拖动的上限由两侧最小宽度决定。窗口缩小造成临时clamp时不覆盖保存比例，放大恢复意图；总宽不足时沿用抽屉。第一次默认首选宽度必要时clamp，比原来38%硬上限更能利用可并排空间。

鼠标按下即取得GUI hotControl并清焦点，拖动/松开消耗事件，避免聊天选择或整个窗口跟随拖动；松开、主窗口关闭、失去输入、控制权变化、进入抽屉时释放。抓取位置偏移保留，按下不会跳宽度。需要在原生游戏确认输入分发。

## 修改文件

- `ServerTab.cs`：通用分隔条绘制、输入捕获/释放、宽度使用；与既有Host侧栏保持一处布局。
- `Client/Source/UI/SidebarSplitLayout.cs`：不依赖Unity的有限宽度/比例计算；Host内部实现，不新增公共renderer或插件专用入口。
- `Settings.cs`：可选本地比例键、变更检测及原值记录；沿用原CRLF。
- `Client.csproj`及中英文 `Shell.xml`：编译项、拖动提示。
- `Tests/SidebarSplitLayoutTests`：生产几何代码聚焦验证。

## 验证

16项几何检查通过：默认宽度、拖动往返、缩放比例、临时clamp后恢复、拖出左右边界、provider最小宽度、抽屉临界值、非法持久值及非有限输入。隔离runner位于 `/tmp/phinix-sidebar-split-tests-01`，未生成仓库bin/obj。

源码检查采用 `git -c core.whitespace=cr-at-eol diff --check`，保留Settings.cs既有CRLF。中英文提示XML需可解析且键一致。

双版完整图、最终候选指纹及包审计状态在下方最终核验中记录。本轮不重复声称前轮403呈现测试等已针对最新候选重跑。前轮原有ResponsiveUiGeometryTests断言失败仍保留，未顺手改动。

## 游戏待测

1. 用左键拖动竖向握柄到左右极限，松手后移动鼠标，确认聊天区不选字、不触发消息/成员操作、整个窗口不跟随。
2. 切用户/通知、主tab、关闭重开主窗及重启游戏，确认比例保留，最小宽度按当前provider生效。
3. 调小主窗进入抽屉再放大，恢复原分配；检查1×/1.5×/2×缩放、窗口边界外松手、拖动期间失焦或弹出窗口。
4. 长彩色名、成员搜索/更多动作、本人改名、消息输入框及两侧滚动条仍可用，无重叠或点击穿透。

以上游戏场景未执行，不能以编译或纯几何结果代替原生鼠标与布局验收。

## 最终核验与候选

1.6完整经典工程及红包/人才/Example隔离编译通过；1.7完整26项目编译通过。两版staging检查31/24 DLL、21共享源码身份一致，最终ZIP包审计通过。当前生产源码与最终1.7编译清单558条指纹逐项一致。中英文提示XML检查及保留CRLF的diff检查通过。

本地双版Host候选：`/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/sidebar-splitter-2026-10-16/Phinix-Rework-Sidebar-Splitter.zip`。SHA256：`461b57462f974664d77be70f81aac1270bdaeda747251026bf796d30ccdcb108`。包含前批快捷改名/成员紧凑布局；独立插件未捆绑，继续配套API3双运行时插件包。没有安装或发布。

构建与包审计：`/tmp/phinix-sidebar-split-dual-02`。首轮构建因GUI名称与项目namespace冲突失败，修正为UnityEngine.GUI后使用全新dual-02输出重新完成；dual-01不交付。原生拖动、输入捕获和配置重启恢复仍为游戏待测。
