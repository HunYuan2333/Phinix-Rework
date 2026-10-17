# UX2-04 首批：公共按钮状态与默认主题边界

日期：2026-10-17；Host `codex/uiux-release-main` / HEAD `27878e7fb78c11fec4f390db317dc0db5b0b10c9` 加前批未提交工作树。保留已有修改、审计、4个fixture和各子模块。用户授权继续优化，本批聚焦公共按钮可读性与默认面板边界；没有安装、提交、推送、发布或更新正式pin。

## 可见变化

- Normal按下角色缺失时改为继承 `ui.button.background`，避免默认落到亮Accent造成亮底亮字；按住时增加内侧Accent描边，区别于普通hover。显式配置的pressed值继续优先，不更改其保存值。
- Danger用普通可读正文加2像素Error边框，替代低对比的红色小字；保留具体取消/重置/卸载标签、禁用原因及原确认链。普通按钮、主要按钮和危险按钮职责不变。
- 公共按钮与图标按钮统一检查实际使用的背景与文字颜色：优先保持原先主题文字（Primary优先用外壳背景色），估算对比至少4.5时原样保留；不足时选黑/白中更清楚的一种。显式调用参数textColor继续权威；可读的主题文字Alpha保留。禁用按钮仍用Secondary文字、禁用底和原禁用原因，不渲染活跃危险/按下强调，不允许触发动作。
- 默认暖色主题面板Surface稍提亮，Separator更清楚，Error提高亮度。没有新增主题键或覆写玩家配置。自定义主题显式值保持；未配置而继承默认主题的键随默认值更新。按下fallback变化适用于所有继承者，不宣称所有自定义主题像素完全不变。
- 选中/hover继续用既有导航边框、下划线和列表选中条区分；处理状态/业务成功仍由owner文字/tone提供。本轮不新增假spinner或推测请求结果，也未把“当前已选却禁用”的插件业务投影全部改完。

修改归属：`ClientWidgets.cs`统一状态绘制/对比判断；`UiTheme.cs`按下fallback；`Client/Themes/default.xml`默认Surface/Separator/Error三项值；相应呈现和主题测试。无公开SDK签名、第二renderer/主题/动作系统、协议或游戏存档变更。名字补色、右侧布局和分隔条指针保留。

## 静态数值

读取HEAD与当前默认XML，并按实际注册fallback逐键解析。用sRGB通道线性化后0.2126R+0.7152G+0.0722B，亮度比两端加0.05。此表是默认不透明组合；不是游戏截图像素采样或无障碍认证。

| 项目 | 之前 | 之后 |
|---|---:|---:|
| 普通文字/普通底 | 9.97 | 9.97 |
| 普通文字/hover底 | 7.91 | 7.91 |
| 普通文字/pressed底 | 1.64 | 9.97 |
| Danger文字/普通底 | 2.94 | 9.97 |
| Danger文字/hover底 | 2.33 | 7.91 |
| Separator/Surface | 2.20 | 3.59 |

Danger前后文字角色不同（由Error改为正文），红色保留在边框。Primary自动选字色也考虑hover/按下的实际fill；半透明先按Surface/Background估算合成。外壳本身透明、游戏底图、原生tint、字号/缩放和任意自定义颜色都可能改变实际结果，尤其透明主题不能静态保证4.5。对比逻辑不重写fill/主题值。

## 已执行检查

- 415项PresentationRuntimeTests通过，stub GUI。新增覆盖Normal按下、Accent内框、Danger文字/边框、亮色与暗色自定义Primary、图标按钮、显式调用文字颜色、可读Alpha、禁用及GUI状态恢复；原名字/成员/交易等呈现回归仍通过。
- ThemeRuntimeTests新增pressed继承自定义background及显式pressed优先两项；75项主题注册、保存、继承、撤销及维护插件adapter断言通过。补齐旧测试夹具缺少的当前窗口/绘图/Event seam；隔离runner新增可选 `--references`，使用联合构建的Utils/Protobuf，不要求往仓库Output复制DLL。
- 沿用原CRLF的差异检查：`git -c core.whitespace=cr-at-eol diff --check`；默认XML可解析，无重复键。原几何套件已知断言失败未顺手修改，未声称历史全套重跑。

呈现日志 `/tmp/phinix-ux04-presentation-01/results.log`；主题日志按下方最终核验。415/75是白盒，不等于两版原生操作通过。

## 待测与后续

两版游戏使用默认暖色、用户当前蓝紫色及明亮/透明主题，比较Normal/Primary/Danger的普通、hover、按住、禁用状态；测试图标按钮、长中文/英文、1×/1.5×/2×缩放、禁用原因和确认是否仍准确。关注Primary选色是否闪变、面板边界是否过重及native tint；现有本人改名、鼠标分隔条、成员选择/交易均回归。

UX2-04本批源码与白盒完成，原生游戏及其余插件状态表达待测。库存图标/搜索和行内提取属于后续UX2-05/06，本批未实现。

## 并行工作区变更与候选隔离

在dual-02打包一致性检查时，另一个工作流修改了 `Client/Source/Framework/PhinixFrameworkClient.cs` 并新增 `FrameworkAdmissionControl.cs` 等连接准入文件。这些不是本轮UX实现，不审定其协议行为，不回滚或改写；检查按预期拒绝将构建中途变化当成同一源码候选。

本轮从 `/tmp/phinix-ux04-dual-02/build16/Phinix-Rework` 的编译前源码快照重新构建dual-03：ClientWidgets、UiTheme及默认XML与本轮最终UI改动逐字一致，旧Framework保留该快照值；三独立插件也来自同一冻结工作区快照。仅补入原兼容构建工具后使用原双版构建/审计流程，无跳过源码身份校验。最终候选不是随后持续变化的实时工作树整体，不包含这批新的连接准入改动。

## 最终核验和候选

冻结快照的1.6完整经典客户端、红包/人才/Example与1.7完整26项目构建、staging和ZIP审计全部通过。31/24 DLL、21项共享源码身份一致。最终1.7清单560项源码指纹与冻结来源逐项一致；三项UI生产文件与实时工作树的本轮UI改动也逐字一致，ZIP默认主题资源与当前XML一致。

使用最终快照联合Utils/Protobuf后重新执行主题测试75项通过：`/tmp/phinix-ux04-theme-final-03/results.log`。415呈现检查链接当前同一UI源码；日志 `/tmp/phinix-ux04-presentation-01/results.log`。

本地双版Host候选：`/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/default-states-2026-10-17/Phinix-Rework-Default-States.zip`。SHA256 `64b1cd1c861a6212496dbf2dc249ff22461f4fd11a2b5e8a17de20decf010e0d`。构建/审计 `/tmp/phinix-ux04-frozen-dual-03`。独立插件不捆绑，仍配套API3双运行时插件包；完整源码并非实时工作树后来新增协议代码，原因如上。未安装、提交、推送或发布。
