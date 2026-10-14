# R17-22：受控 Region 自绘 context

日期：2026-10-14。用户要求继续实施，并在适合游戏测试时明确通知。当前只关闭 Region 合同与生产源码白盒，不部署中间包或宣称新版绘制已可用。

## 源码检查与最小范围

实际生产 IClientPresentationRegion 消费者只有 `Phinix-Example-Plugin/example/ExamplePresentation.cs:176` 的 CounterPreview；它绘制 12 高度的进度预览。其他 Host/内置/红包/人才没有 Region 实现。依据这个真实需求，新 context 只提供 Bounds、只读 Theme 和 Progress(bounds,fraction)，不预设文字/图片/全局 canvas/原始 draw list。

`Client/ClientExtensionAbstractions/UI/IClientPresentationService.cs:215`：Region.Draw 改为接收 IClientPresentationDrawContext，移除 Rect/theme 两参数旧签名。Measure(float,theme) 已是中立合同，保留。context 无 Unity/Verse/native handle、事件、输入、动作、文件访问或生命周期管理能力，不提供旧 overload。

Example.CounterPreview:181 改为 `context.Progress(context.Bounds, ...)`，保留原 count→fraction 与高度；仍是普通第三方使用公共 renderer，没有官方专用绘制路径。交互仍走原公共字段、动作与确认。

## Host 生命周期与行为

`Client/Source/UI/ClientPresentationService.cs:857` 的 DrawRegion 仅在 Repaint 调用自绘。Layout 与输入只使用测量/普通控制流程，不执行 Region.Draw。Host 继续以自身 GUI group 裁剪，禁用被动区域输入，并以 ClientGuiScope 恢复 owned GUI/text 状态；错误报告、failed region fallback 和下一消费者绘制保留。

同文件 RegionDrawContext:872 在创建时捕获当前 page identity、theme snapshot 和局部 Bounds。每次访问检查 context 尚有效、主线程、原投影/视图仍有效以及 Repaint。回调正常/异常结束后 finally Close，清除 session/page/theme 引用；保存的 context 不能再调用或取后续视图 facts。重入 Publish、关闭视图或离开主线程使当前 context 拒绝调用。

Progress 拒绝非有限值、负尺寸与 extent 溢出，空区域或完全位于 Region 外时不进入 native 绘制；部分相交仍交由 Host clip，保留原几何，不先缩窄矩形而改变进度比例。合法有限 fraction 沿既有 widget clamp 到 [0,1]。新 context 对 NaN/Infinity 明确拒绝，旧 widget 的直接入口仍保持原规则；这是 API 3.0 context 的明确边界，不是业务数量/确认规则调整。

这里只限制提供的公开能力，不能把任意 C# 插件视为安全沙箱。测试会故意直接修改全局 GUI 状态来验证 scope 恢复，但合同不提供这些入口；不保证修复第三方自行破坏的任意 native 状态或不配对 group。

## 验证与限制

| 检查 | 状态 | 依据 / 限制 |
|---|---|---|
| Region 生产消费者、调用路径、context 签名 | 源码检查 | Example 唯一当前生产实现；其余消费者同批编译，无特殊 owner 判断 |
| 1.6 / 缓存 1.7 各 22 个项目 | 编译通过 | Host、内置、三个独立插件、Example、六测试项目；基础通信/protobuf 使用保留 Output，不是完整发行图 |
| 实际合同，两版各 50 断言 | 白盒通过 | 新增 Region.Draw 的 context 参数、中立签名和限定 primitive 检查；不运行 native UI |
| 两版其他五套回归 | 白盒通过 | Trade 204、Inventory composition 84、game adapter 19、Example 45 断言，以及原 Inventory 19 组场景 |
| 生产 renderer + stub GUI，288 检查 | 白盒通过 | Layout/input 不自绘、局部 bounds、被动 clip group、固定 snapshot、geometry、非法值/溢出/off-thread 拒绝、迟到 context、重入 Publish、异常恢复、下一 region/fallback |
| 红包/人才 source-manifest、四仓库 diff | 检查通过 | 本轮两插件源码未变；既有摘要仍匹配 |
| 实际画面裁剪、主题/GUI native 状态、GPU、1.7 backend | 待测 | 游戏通过：无；fixture 不模拟 native 裁剪或实际 GPU |

引用仍为原 1.6 GameDlls 4633/Harmony 2.3.6，与已核验 SHA 的 1.7.4977 rev926 缓存/Harmony 3。当前安装 Version.txt 仍是 1.6.4871 rev598，未修改安装或正式存档。系统 .NET 10 的白盒不等同游戏 CLR。

最终结果在 `/tmp/phinix-r17-22/final-1.6`、`final-1.7`、`final-presentation`。上一切片的主题 73 项结果保留；本轮主题生产源码未变，没有重新运行其专项 suite。游戏 DLL/分析引用与原始玩家日志不提交或分发。

整个 Abstractions 仍有引擎依赖；同一 session 的 CreateActionMenuOptions 还返回 Verse.FloatMenuOption，公共 window/shell/widgets 与 Game bridge 尚未物理隔离。不能把 Region 中立误写成整个 presentation/程序集已经中立。Region 签名变化继续按 API 3.0 联合闭合，正式身份/API 范围/pin 未更新，不能单独部署中间 DLL。

## 下一步与游戏测试通知门槛

下一切片整理公共菜单/窗口入口及 R17-23 程序集/API 布局，再进入 R17-30～33 的新版绘制和专门 1.6 adapter。保持同一 presentation/theme/action/confirmation，Host 不接管任何业务、Thing/Pawn/资源 owner。

**当前不建议游戏测试这个中间组合。** 到 R17-33 的首个可运行联合候选时，主动通知用户，并给出：

1. 需要的准确游戏/Harmony 指纹、Host 与三个插件/Example 候选来源和匹配范围；核验每进程只有一份有效公共合同与对应 adapter。
2. 明确可安装的候选包及完整替换清单；候选不携带游戏 DLL/私有参考，排除旧 API 混装，不覆盖正式发行资产。
3. 第一轮临时环境步骤：启动/日志、公共窗口与滚动、主题编辑/热应用/导出及本地包重载、Example 的字段/动作/确认与 Region、字体/透明/裁剪/图像；真实 IME/焦点随后按输入切片验收。
4. 收集该次 Player.log、截图、实际加载版本和触发顺序；无错误只覆盖已执行路径。业务交付/Pawn/Scribe/切档另用临时测试存档，不改正式存档。

若此前发现必须实机才能判断的阻断，也单独通知用户说明待验证问题和最小步骤，不要求安装已知合同不完整的包。第一轮游戏通过后再扩展消费者与生命周期验收；不靠编译/白盒宣布全面兼容。
