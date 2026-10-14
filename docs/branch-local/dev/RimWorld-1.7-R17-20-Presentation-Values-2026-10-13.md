# R17-20 首个切片：公共布局参数与 RGBA 样本

2026-10-13。源码、选定消费者编译和白盒通过；游戏待测。P2 尚未全部完成，未部署或发布。

## 改动与消费者

`Client/ClientExtensionAbstractions/UI/ClientPresentationValues.cs` 定义两个只包含 float 的只读数值类型：`ClientPresentationBounds`（X/Y/Width/Height）与 `ClientRgba`（Red/Green/Blue/Alpha）。没有 Unity/Verse/Waylith 引用、引擎句柄、隐式转换、像素取整或颜色空间转换。

`IClientPresentationSession.Draw` 改为接收 Bounds，Host 的现有 `ClientPresentationService.Session.Draw` 在入口显式构造当前引擎 Rect，后续布局/主题/输入/动作流程保持原实现。边界值原样传入，保留 renderer 既有非法/过小布局处理，未新增另一套坐标系统。

`ClientPresentationSwatch` 改为接收并返回 ClientRgba；仍在 Swatch 边界拒绝非有限或不在 0～1 范围内的四通道。Host 绘制时显式转换 Color，ThemeSettingsWindow 与 Example 同步构造 RGBA。主题注册、继承、热应用、导出、包和保存格式未改。

同步直接 session 调用：Chat feed/sidebar/composer、Inventory 主视图/设置/交付选择、Trade 列表/报价窗口、Store 窗口/管理视图、Host 主题窗口、Example。Verse Window/原 provider Draw(Rect) 仍是当前游戏壳层接入，不冒充已完成中立窗口 API。

红包和人才的既有 presentation 绑定层反射构造同一 Bounds，并以准确参数类型查找 Draw；在申请 contribution scope 前验证新类型、构造器和 Draw 签名。没有增加旧客户端 fallback、官方 renderer 或私有动作入口；对应两个 source-manifest 摘要已更新并校验。

## 当前暴露点与后续范围

| 实际入口 | 本切片状态 | 后续责任 |
|---|---|---|
| Session.Draw(Rect) | 已改为中立 Bounds；当前消费者闭合 | renderer backend 在内部消费数值，不把引擎转换再放回公共类型 |
| PresentationSwatch.Color | 已改为中立 RGBA；主题编辑与 Example 同步 | 与后续主题数值契约统一，不创建第二套 theme |
| IClientThemeReader/Registry/Catalog 的 Color | 源码检查；仍含 Unity 类型 | R17-20 后续主题切片迁移整条 reader/draft/registry/catalog 链，保留现有编辑、热应用和包能力 |
| Row/Badge/Media/User 的 Texture2D、Func<Texture> | 源码检查；仍暴露 | R17-21 borrowed image owner/lifetime/revision/visible draw 边界；不把 Pawn/RenderTexture 所有权交给 Host |
| IClientPresentationRegion.Draw(Rect, theme) | 源码检查；仍含 Rect 与间接 Color | 只补当前受控自绘真正需要的中立 canvas，保持纯 Measure/Draw、裁剪和输入隔离 |
| Session 的 FloatMenuOption、provider/layout hints、Window 服务 | 源码检查；仍含 Verse/Unity | 中立菜单/窗口描述与游戏壳层 adapter；不复制动作/确认系统 |
| GUI/Event、IME、测量、资源销毁 | 尚未迁移 | P3/P4 用实际 1.7 backend 与专门 1.6 renderer adapter 验证 |

仅两个新数值类型不代表整个 Abstractions 程序集已无游戏依赖；也没有修复或验收 1.7 的真实画面。

## 引用变动与证据范围

验证期间安装目录切回 1.6：Version.txt 为 **1.6.4871 rev598**，DLL 位于 `RimWorldLinux_Data/Managed`。实际 Assembly-CSharp 元数据 `1.6.9676.18020`，MVID `b4d967f0-d45a-413f-bb02-23eefee4f2ae`，SHA256 `082db1dd4f7f1d0b72960d7e1beead8fbfe6957200e8627f65bda0dbbe1dd8f8`，与先前核验的较新 1.6 DLL 字节一致。旧评估记录中的 rev600 与当前 Version.txt 的 rev598 分别保留，不依据文件推断本轮实际运行日志版本。

本次 1.7 编译改用 R17-11 隔离 run 中保留的真实 DLL，先重新核对：Assembly-CSharp `be2d5a0bcc44c8e9b713ac19cb073bb128418dab3c23b1ebf8eca6eceacbc2b8`、UnityStubs `49badc8e2569062c693983555fc431622eab5ffc7e1e72252e8103f8d540e154`，对应此前核验的 1.7.4977 rev926。不是从当前 1.6 安装拼造 1.7 引用，也没有修改安装目录。Harmony 使用同一缓存的实际 3.0 DLL。

1.6 编译继续使用原 GameDlls 4633 基线与 vendored Harmony 2.3.6；本轮没有以当前 4871 安装替换它。两版白盒在系统 .NET 10 下运行，不能代替游戏 CLR/GPU 验收。

| 检查 | 1.6 | 缓存 1.7.4977 | 限制 |
|---|---|---|---|
| 22 个选定项目 | 编译通过 | 编译通过 | R17-11 的 20 项 + ExampleLifecycleTests、PresentationValuesRuntimeTests；基础通信/protobuf 等仍从既有 Output 读取，完整构建图待测 |
| 新数值契约 | 白盒通过，15 断言 | 白盒通过，15 断言 | 实际编译合同；数值与透明度保留、无引擎转换 operator、准确签名、RGBA 拒绝规则；不执行 GUI |
| Example 生命周期 | 白盒通过，45 断言/五轮 | 白盒通过，45 断言/五轮 | 普通发现/激活/关闭与 borrowed services；不渲染窗口 |
| Trade/Inventory 四套回归 | 白盒通过 | 白盒通过 | Trade 204、Inventory composition 84、game adapter 19 断言；Inventory 原 19 组场景 |
| 现有公共 renderer 测试 | 白盒通过，228 检查 | 同一中立/fake GUI 检查 | 生产合同与 renderer 源码链接到 stubs，包含列表、输入、动作、主题角色、footer 与关闭；不是实际 UnityStubs/Waylith 渲染 |
| 红包/人才源码摘要 | 检查通过 | 同一只读检查 | 没有发布或更新正式 Host pin |
| 原生画面、DPI、字体/IME、图像、联合包 | 待测 | 待测 | 游戏通过：无 |

复现工具：`Tests/TradeCompositionRuntimeTests/run-isolated.py`；本轮输出 `/tmp/phinix-r17-20/final-1.6`、`final-1.7`，1.7 的 `--game` 与 `--harmony` 指向上述经哈希核验的缓存。工具新增缺失真实引用的早期检查，避免游戏安装变化后把大批“类型找不到”误当业务迁移问题。缓存与引用不得提交或分发。

假 GUI 检查：`python3 Tests/PresentationRuntimeTests/run-isolated.py --output /tmp/phinix-r17-20/final-presentation`。输出需新目录；工具链接当前源码，独立生成工程与日志，不写正式 bin/Output。玩家日志与正式存档均未修改。

## ABI 与执行顺序

Draw 参数、Swatch 构造器/Value 类型改变，加上 R17-10/11 的类型归属改变，已经要求 API major 升级。本迁移按 **API 3.0** 联合收口，不添加旧 Draw(Color/Rect) overload 或旧客户端组合分支。它是迁移目标决策，正式 API/CLR 身份、三插件与 Example 的 manifest 范围、固定 Host pin 尚未切换；不能把当前中间代码当作可部署的正式 API 2.0 包。

下一步 R17-21 图像边界，再逐项处理主题值、自绘区、窗口/菜单与输入。等公共合同闭合、两版 backend 有可运行联合候选后，再通知用户进行首轮游戏测试。目前不要求切换游戏版本或安装中间 DLL。
