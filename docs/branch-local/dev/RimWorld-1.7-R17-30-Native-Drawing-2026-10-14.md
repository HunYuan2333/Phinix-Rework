# R17-30 原生圆角/描边绘制首切片（2026-10-14）

基于 rev1248 实际 UnityStubs DLL 与 Host 13412fe / Common be978cf。Host 和内置/第三方继续调用同一 ClientWidgets；没有官方私有 renderer、第二主题/动作系统或业务代码修改。

## 已确认的新 API

本地 PE/IL/反射检查确认 public UnityStubs.Gui.GuiBackend.Current、IGuiBackend.FillRectRounded(Rect,Color,float)、IGuiBackend.DrawPrimitives(int,ReadOnlySpan<GuiVertex>,Texture,bool screenSpace)、GuiToScreenPoint(Vector2)。GuiVertex public 字段是 Vector2 position/uv 与 Color32 color；不能将 DrawPrimitives 错当成 IList 参数或假造 stroke API。GL.End 的真实 IL 向 DrawPrimitives 转发 mode/ReadOnlySpan、texture 和 GuiTransformAbsolute。

旧 GUI.DrawTexture 的八参数 overload 在该 DLL 仍忽略边框/圆角，这是本切片替换的具体行为依据。签名与 IL 结果仅保留本地分析目录 vertex-api/gl-end-il，不提交游戏源码。

## 实现

共享 `Client/ClientExtensionAbstractions/UI/ClientWidgets.cs` 保留原几何 clamp、主题颜色选择和 ClientGuiScope；RoundedSurface/RoundedBorder 改调同一个内部 ClientGameDrawing。游戏实现文件：

- `Game/Drawing/ClientGameDrawing16.cs`：保留原 Unity GUI.DrawTexture 调用与参数；默认 net472 构建选取它，1.6 同样使用公共 widgets。无新增旧客户端分支。
- `Game/Drawing/ClientGameDrawing17.cs`：编译符号 PHINIX_RIMWORLD_17 选择，直接使用 rev1248 原生接口。仅 Repaint 绘制，backend 缺失时与旧 GUI helper 相同不绘制。填充使用 FillRectRounded；边框用外/内圆角边界之间的三角环，不画内层背景色来伪造空洞，透明主题仍保留中心内容。内区域退化时直接全圆角填充。

边框按当前 GuiToScreenPoint 转换各顶点，再以 screenSpace=true、GL.TRIANGLES、null texture 提交；颜色按实际 API 使用 Color32（8-bit alpha）。4～32段/角，按半径增长，整数转换在 clamp 后防溢出。每次提交独立数组，不复用可能被 backend 持有的内容；无 GPU 纹理/material 分配。其托管数组开销仍需游戏 profile，不宣称零分配或密集列表性能已通过。

既有主线程/视图回调/裁剪与异常边界归 Host/原窗口；adapter 不读取 Thing/Pawn、报价、账本或协议，不执行用户动作，不拥有/关闭视图，不改变业务确认。公共 API 没有新 raw native handle。当前 Game 实现仍位于原抽象程序集，最终物理布局/API3.0门槛不因此完成。

## 构建选择与验证

原 Abstractions csproj 显式包含两实现文件。1.6 默认预处理选旧实现；选定1.7 runner 与完整net10源码图 generator 明确传 PHINIX_RIMWORLD_17，不由 runtime 反射猜版本或兜底新旧客户端组合。正式原生1.7项目/打包尚未闭合，不能仅设置一个符号后用net472原工程冒充net10构建。

| 检查 | 状态 | 证据与限制 |
| --- | --- | --- |
| 类型/方法、IL调用与所有消费者 | 源码检查/DLL检查 | 实际rev1248；全部使用原公共 widgets，无business新增 |
| 原生接口源码白盒 | 编译通过、白盒通过 | 1135断言；直接引用真实UnityStubs/Waylith，fake实现真实IGuiBackend，source-link真实17adapter；中空、面积误差、零/大描边、alpha、平移/非均匀缩放、Repaint、独立提交、backend缺失/异常 |
| 1.6呈现回归 | 白盒通过 | 315 source/stub，链接专门16实现，原Host/动作/生命周期测试继续通过 |
| 1.6严格locked完整图 | 编译通过 | 真实客户端solution、基础设施源码、三插件工程，原net472输出在隔离snapshot；非游戏 |
| 1.7选定消费者与六套检查 | 编译通过、白盒通过 | 23项、合同62/Trade204/Inventory adapter19/原Inventory19场景/Inventory composition84/Example45；fresh net10基础设施 |
| 完整net10源码图 | 编译通过 | 26项目，源图新17adapter实际启用；原packaging targets未执行；最终代码增量重建完成后以build-final.log为准 |
| 原生GPU、窗口/滚动裁剪、GUI.matrix、RenderTexture、UI缩放/4K/抗锯齿、GC | 待测 | fake backend不证明真实坐标语义或渲染效果；特别需要与填充坐标对齐、深层Group/Scroll及旋转矩阵检查 |
| 正式身份/版本目录/插件联合包 | 待测 | API3.0/pin/范围未切，暂无可安装联合候选；游戏通过：无 |

异常由 adapter 传播，公共 widget 的 existing using/Host fallback负责清理；本轮直接接口测试证明异常传播，没有在标准CLR中执行真实Verse.Text初始化或GUI global恢复。共享Scope的stub回归是不同层的证据，不能拼接成完整GPU异常游戏证明。

测试源码 `Tests/GameDrawing17RuntimeTests` 与隔离 runner；结果 `/tmp/phinix-native-drawing/native-final`，之前native-tests为同场景的首次通过。1.6 snapshot在 `local-compatibility-analysis/native-drawing/1.6`；其余selected/full-net10/presentation16在 `/tmp/phinix-native-drawing`。JSON/log与私有引用继续本地保留，原Output/GameDlls不覆盖，protobuf pins/source不改，无提交/发布。

下一批闭合版本化输出与Host API3.0/插件profile/包，继续窗口/输入/font/image adapter的实证检查。首个可运行联合候选会主动通知用户准确版本、hash与最小游戏测试步骤；目前不安装这些中间DLL。需要测浅色/半透明主题、热应用、圆角/描边在Group/Scroll/窗口下的对齐、1×/1.5×/2×与4K、透明中心、关闭/重开和真实中文IME。
