# 1.7 原生描边坐标修复与候选05

用户确认候选04的1.6测试“没问题了”；只记录其已执行测试范围的通过反馈，不扩大为所有业务/存档路径验收。随后1.7截图出现跨内容区长线、聊天选中框偏移及空白。最新1.7日志未再出现原SDK/UnknownItem加载异常；StoreReleaseNoticeFailed仍有记录，其throw栈不完整，仍待定位。

用户后来明确撤回无存档库存吞物疑点，说明选的是直接空投。本轮未修改Inventory、Trade的存入/空投/账本/回执代码，也不将撤回的描述写成已证实数据丢失。

## DLL证据与修改

真实1.7.4977 rev1248的 `RimWorld.dll`：`RimWorldWaylith.Imgui.ImGuiGuiBackend` 中：

- `GuiToScreenPoint` 先对局部点应用m00/m01/m10/m11，再加group offset与translate。
- `ToScreen` 先加group offset，再应用同一矩阵和translate。
- `FillRectRounded` 经ScreenBounds调用ToScreen。
- `DrawPrimitives` 经PrimPosition；screenSpace=false时调用ToScreen，true时直接使用顶点坐标。

旧adapter使用GuiToScreenPoint转换顶点再传true；填充与描边因此不是同一条绘制变换路径。缩放、分组/滚动偏移下会错位。此前fake backend只验证了GuiToScreenPoint输出，未覆盖这两个不同变换次序，是白盒覆盖缺口。

`Client/ClientExtensionAbstractions/Game/Drawing/ClientGameDrawing17.cs` 现在保留局部顶点，DrawPrimitives传false，让原生backend完成绘制转换。三角环、颜色/透明中心、Repaint限制、资源所有权与公共调用入口均保留；没有重写renderer或业务。

`Tests/GameDrawing17RuntimeTests/Program.cs` 按真实IL区分两种变换，使用非单位矩阵及非零group offset，检查实际提交为局部坐标、没有调用GuiToScreenPoint，原生投影后的边界/面积/透明中心正确。

## 验证与剩余问题

| 项目 | 结果 | 状态 |
|---|---|---|
| backend转换规则 | 实际RimWorld.dll方法IL | DLL检查通过 |
| adapter | 1138检查，真实IGuiBackend接口+fake实现 | 编译/白盒通过 |
| 完整1.7源码图 | 26项目 | 编译通过 |
| 联合候选及语言 | 61目录/身份/语言检查、六项payload校验、ZIP CRC/hash | 白盒通过 |
| 1.6产物 | candidate05的整个1.6目录逐文件摘要与candidate04完全一致 | 字节核验通过；用户此前反馈04通过 |
| 1.7长线/选框/GPU裁剪 | 新adapter未实机执行 | 待测 |
| 大段空白 | Red使用固定底部操作区，Trade/Store使用对齐页头；这些确有设计占位，尚不能从截图判定所有空白均为bug | 源码检查，游戏对比待测 |

未随坐标修复修改fixed header/footer布局；先比较正确绘制下的窗口边界，再检查仍异常的测量高度，避免误改1.6既有语义。

## 候选05

`/home/hunyuan2333/Phinix/local-compatibility-analysis/drawing-coordinate-fix/candidate-05/phinix-rework-local-api3-20261014-05.zip`

SHA256：`fd950c5751777bc15165173ea594224e70dec8aa95015d768266e75b8bedc67c`。

单Mod本地测试包、共同API3、两版目录，120文件；未发布。退出游戏后替换旧测试目录，不覆盖残留DLL；在1.7复测同一Chat/Trade/Store/Red页面，改变UI缩放及滚动位置，重点观察边框与填充是否重合、分隔线是否越过剪裁边界。空状态留白与线条分开反馈。没有修改正式存档或安装目录。

双版本独立商店包目标保留，仍未完成安装链；不能把本联合普通bundle包作为商店双payload支持完成证明。后续先确认显示修复，再继续共享包声明/全包校验/选中变体/回执与恢复闭环。
