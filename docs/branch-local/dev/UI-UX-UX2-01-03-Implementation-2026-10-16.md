# UX2-01～03：快捷改名与右侧紧凑化实施记录

日期：2026-10-16。源码完成；隔离编译/白盒已执行；本地候选就绪；**游戏验收待测**。未安装、提交、推送或发布。

## 基线与范围

Host分支 `codex/uiux-release-main`，HEAD `27878e7fb78c11fec4f390db317dc0db5b0b10c9`；Common `c165f61d749189f700ca896d63d9366d5c71dbf7`；红包 `6f4e74a47a859a254f808064abbbad87d2ba72ef`；人才 `5df4626848e400c1dd6a966eee23d60cfedb9543`；Example `7a6e806e8d15ec32e95c6e59cc086602a32c801d`。候选由该基线加本轮未提交工作树构建，不能仅用HEAD复现。

读取仓库AGENTS、设计哲学、兼容边界和最新UI/UX实施记录。保留已有审计文档、roadmap修改及4个未跟踪fixture DLL。不改插件子模块、协议、存档schema、API3公开合同或正式包/pin。逐字调色器继续仅作远期文档；库存图标及其余高频流程按后续切片推进。

## 玩家可见变化

- Host右侧顶部增加固定本人昵称与“改名”，位于sidebar导航之前。显示完整富文本的裁切结果，悬停可看去标签全名及在线/离线状态；不按字符串截断损坏标签。切用户/通知和列表滚动不替换本人身份。所有sidebar收到同一剩余矩形，宽度不增加。
- “改名”打开独立编辑窗口：原文输入、实时渲染预览、保存/取消；失焦、Esc、X与取消不保存。设置菜单及连接设置页也进入同一窗口；无Chat或无sidebar时仍可达。既有全部设置原文入口保留。
- 本地保存、请求提交、发送未知、当前目录名字一致分别反馈。没有关联改名回执，绝不把目录观察冒充请求确认。保存前连接/身份/名称等事实变化要求再次显式保存，保留草稿；发送异常不自动重发。一次提交后原保存按钮禁用，修改草稿后可另行显式提交。
- 快捷输入采用现有公共控件4096字符上限，拒绝空白、换行和NUL；这不是新增协议限制。既有超长名字不截断或覆盖，提示到全部设置编辑。
- Chat删除重复用户标题和未选择时常驻说明卡。搜索、在线数、简短屏蔽折叠按钮作为公共工具栏；条件显示所选成员动作，第三方贡献保留在“更多操作”；列表使用公共紧凑行。没有全局缩小字体/padding或新增renderer。

快捷路径：右侧“改名→输入→保存”（2次按钮＋聚焦/输入）；无sidebar时“设置→修改名字→输入→保存”（3次按钮＋聚焦/输入）。异步目录观察另计。保存窗口保留结果供核对，额外关闭操作不混入提交次数。

## 实现归属

- `Client/Source/UI/NicknameEditState.cs`：纯草稿、保存与发送状态，外部事实变化核对。
- `Client/Source/UI/NicknameWindow.cs`：普通公共presentation session和typed action point；借用现有永久 `builtin.host` principal，仅创建自己的正常scope。关闭session、释放scope、Host停止按生命周期配对；不创建第二Host身份或动作系统。
- `Client/Source/Client.cs`、`ThemeSettingsWindow.cs`：Host已有用户服务/保存/窗口入口与所有权接线；显式保存调用AcceptChanges以免之后窗口PreClose重复发送。
- `ServerTab.cs`、`SettingsWindow.cs`：固定本人区、剩余区域及同一编辑器入口；不引用Chat业务合同。
- `Extensions/Chat/Client/MemberSidebarPresentation.cs`及`ChatSidebarProvider.cs`：只调整公共页面投影，业务typed action/过滤/选择身份仍走原链。
- 两版工程编译项、中英文Keyed资源、相应状态/密度/真实DLL生命周期测试同步更新。

## 实际验证

| 检查 | 结果与证据限制 |
|---|---|
| PresentationRuntimeTests | 403项通过，stub GUI；含草稿取消、原文保存、显式提交、防重复、发送未知、外部变化、离线重连、保存失败、长度边界与成员贡献保留 |
| 成员密度对照 | stub宽180/270/400，原viewport500，新viewport464（扣本人区36）：完整行7→12、7→12、8→12；只证明当前投影测量，不代表游戏截图 |
| WindowBoundaryRuntimeTests | 12项通过 |
| ResponsiveUiGeometryTests | **失败**：`Narrow form should stack label and controls.`；该测试及其链接的11文件与HEAD逐字一致。本轮未修改：280宽、200最小输入、10间距、80动作要求290，原测试期望70但当前几何返回三行110。保留失败，不顺手改基线或声称全套通过 |
| 真实Host DLL（两版各自） | Chat 19场景；Host组合10断言通过，含昵称scope与永久Host同属、释放昵称不移除主题scope；不执行Unity原生GUI |
| 真实维护消费者（两版各自） | 红包38、人才84、商店109检查通过；全图编译含Example。非游戏交互验收 |
| 1.6完整图 | 原经典net472工程、插件隔离构建通过；引用仓库GameDlls |
| 1.7完整图 | 26项目通过，0错误、73警告；引用独立缓存1.7.4977-rev1248，不代表当前游戏安装 |
| staging/打包 | 31个1.6 DLL、24个1.7 DLL检查；21项共享源码身份一致；包91文件，两版布局/禁止游戏DLL等审计通过 |
| 最终源码/资源 | 候选1.7编译清单557条源码指纹与当前生产源码一致；中英文25个昵称键一致、4个相关XML可解析；git diff --check通过 |

构建命令（隔离输出，不触发工作树AfterBuild覆盖）：

```sh
python3 tools/compatibility/build-dual-host.py --game16 GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-ux2-dual-02
```

日志/指纹：`/tmp/phinix-ux2-dual-02`；呈现 `/tmp/phinix-ux2-presentation-06/results.log`；窗口 `/tmp/phinix-ux2-window-01/results.log`；几何 `/tmp/phinix-ux2-geometry-01/results.log`；真实消费者 `/tmp/phinix-ux2-consumers-16`、`-17`；最终Host probes `/tmp/phinix-ux2-host-probes-final-16`、`-17`。Host probes使用现有真实消费者runner隔离生成net10测试工程，链接最终候选DLL。新scope断言在候选冻结后追加，未改生产源码。

## 本地候选与待测

Host双版ZIP SHA256：`23e3bdff1e62b5cdafd6340e331d1f20f65798fbb78c66970376a6dc4daeca41`。路径：`/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/ux2-01-03-2026-10-16/Phinix-Rework-UX2-01-03.zip`。独立插件未捆绑；仍配套现有独立API3双运行时插件包，不能混装历史API2 DLL。候选不是发布包或游戏通过结论。

游戏待测：1.6/1.7分别检查中英文、逻辑窗口640×480～1280×800、1×/1.5×/2×缩放；同条件截图记录本人行/导航/工具区/列表高度和完整行数。特别检查长彩色名裁切、改名按钮命中、中文IME选词Enter、Esc/失焦取消、在线与离线保存、断线重连保留草稿、两入口不重复开窗/提交、用户/通知切换、搜索/屏蔽/交易/第三方菜单、窄窗抽屉不穿透及无Chat入口。普通玩家任务仍需实测。

UX2-01/02不记“游戏完成”；UX2-03等待以上实机门槛。后续主题/库存/引导等切片未实现。
