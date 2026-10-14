# R17-23 新安装 rev1248 与构建/加载检查（2026-10-14）

用户继续迁移，并恢复 Steam 1.7。当前实际游戏为 **1.7.4977 rev1248**，不是此前 rev926。本轮未拉取待验证的 1.6 bugfix，未提交、推送、发布或修改正式存档。

## DLL 与运行环境

程序集在游戏安装根目录，已保存 compile-only 副本到工作区各 Git 仓库之外的 `local-compatibility-analysis/references/1.7.4977-rev1248`。不再依赖 /tmp 缓存；目录内 input-fingerprints 保留来源与每个文件 SHA。无游戏 DLL/源码分发。

| DLL | 身份与 SHA/MVID |
| --- | --- |
| Assembly-CSharp | 1.7.9782.37458；SHA `3d4f1ddfe71bd8760cb488255e3bdeac9366f537dc1ef813cc6f8d09824e2da9`；MVID `de18e239-08d3-45a3-b8ee-b67b13536b54` |
| UnityStubs | 1.0.0.0；Product `1.0.0+f9010d04527f7c11c670d89bd7b1c00da632dc17`；SHA `b9e08c6bf72f57fa6f0c09277eadca209a30b157508795072f2dd9f79de655dd`；MVID `72503703-3ce7-49c4-abad-87b980bc0cc3` |
| Waylith | SHA `850e99644283a94e78bd6dc5cf0bbfa25367d8e1e11c1acb1f82e9f73a321b37` |
| Harmony workshop 1.7 | 3.0.0.0，Product rc.2；SHA `94dd929a06f720869f6a718a64bc6713a37dbe153c2f4c16c031b2587d7ee694`；MVID `64580f62-9d9e-41bc-b0fc-58cd936a45fc`，与此前引用相同 |

Version.txt 与 RimWorld.runtimeconfig.json 已同时保存。runtimeconfig 声明 net10.0 / Microsoft.NETCore.App **10.0.10-waylithclr.dev**、UnityEngine*→UnityStubs 等重定向和 lax binding 配置；这是运行配置证据，不是本轮启动游戏后实际 runtime/Harmony 加载日志证据。没有新的 Player.log，不推断主模组与插件的实际游戏加载版本。系统 SDK/runtime 的白盒进程仍不等于游戏自定义 CLR。

## 最新 UI 实现证据

通过 System.Reflection.Metadata/PEReader 检查方法签名、IL 和调用目标，结果仅保留本地 `ui-il.txt`、`gui-backend-il.txt`，没有将游戏源码提交。

- **已确认变化**：`UnityEngine.Input.get_compositionString()` IL 直接调用 `UnityStubs.Internal.ImeComposition.get_Text()`；`set_compositionCursorPos(Vector2)` 获取 GuiBackend.Current，经 IGuiBackend.GuiToScreenPoint 后调用 ImeComposition.SetCandidatePos（backend 空时直接使用输入坐标）。因此早期“compositionString 为空实现”的结论不能套到 rev1248。
- **已确认风险仍在**：三种八参数 `GUI.DrawTexture` 重载只转发 rect/texture/scale 与 GUI.color×color 到四参数 DrawTextureImpl；IL 未读取 borderWidth/borderWidths/borderRadius/borderRadiuses，对应参数仍没有生效。`Client/ClientExtensionAbstractions/UI/ClientWidgets.cs` 的 RoundedSurface/RoundedBorder 仍调用这些重载；边框可能变成整块填充，圆角缺失。这是源码+IL 的行为推断，实际显示影响范围仍需游戏。
- **可用 API 元数据**：IGuiBackend 声明 FillRectRounded、FillRect、DrawPrimitives、MeasureText、FontMetrics、PushClipRect/PopClipRect、PushGroup/PopGroup 等；GuiBackend.Current 有 public getter/setter。GUIStyle.CalcSize/CalcHeight 中调用自身 MeasureText。不是宣布 native backend 的圆角/文本/裁剪已验收；下一批 adapter 使用这些真实接口，不虚构 stroke API，也不以反射全局扫描代替显式引用。
- **窗口路径**：WindowStack.Add(Window) IL 依次 RemoveWindowsOfType、PreOpen、InsertAtCorrectPositionInList、FocusAfterInsertIfShould、PostOpen。CurrentMajor/CurrentMinor getter 元数据存在，并在本轮 Host 编译中实际绑定。尚未证明所有窗口输入、焦点、关闭或生命周期路径等价。

上述证据没有要求改业务状态机、Scribe 格式、Pawn 返还或账本。IME 改进不等于原生输入法已游戏通过。

## 构建图与加载实现

新增 `tools/compatibility/build-graph-isolated.py`：复制真实 Client/Extensions/Common/protobuf/测试源工程到独立目录，原 bin/obj/Output 不写；GameDlls 与旧 Harmony 是只读引用。offline feeds、系统 SDK 通过进程 MSBuildSDKsPath 选择，不编辑 vendored global.json/pins。还可恢复三个测试锁图、构建红包/人才/Example 的真实工程，不打包或发布。

首次真实 locked restore 确认 NU1004：新增中立合同的 ProjectReference 与旧锁图不一致。在隔离副本刷新后，逐项确认所有原 NuGet 非 Project 条目的 requested/resolved/contentHash/dependencies 不变，才同步 Composition、Store 和三个受影响测试的 packages.lock.json；只改变项目图，不绕过正式锁定检查。新的严格锁定构建输出在 `local-compatibility-analysis/r17-graph-20261014`，包含命令与源码/引用指纹。这一完整真实工程构建仍为 **1.6**，不能冒充完整 1.7 图。

新增 Host 内部 `Client/Source/Framework/ClientGameAssemblyLayout.cs`，Client.GetExtensionProbeDirectories 按实际 RimWorld.VersionControl.CurrentMajor/CurrentMinor 选择：

- 对已有 1.6 Common 布局保留原路径；这维护 1.6 引擎构建，不是新加旧客户端组合支持。
- 有 `<version>/Assemblies/10-ClientExtensionAbstractions.dll` 时只探测该版本 Assemblies/Extensions，沿用原 bundle ownership/即时子目录与 Resources 排除规则；不探测另一游戏版本或 Common。
- 1.7 缺自己的版本化 SDK 时明确拒绝，不静默借 1.6 Common；未执行 DLL 时的目录标志不能替代后续 identity/profile 验证。

未改正式 LoadFolders、输出布局或 API/pin 身份。版本化完整制品尚未生成，这个加载器修改只是后续联合候选的前置；不可单独部署到原 API 2.0 正式包。

## 验证状态

| 检查 | 状态与结果 | 限制 |
| --- | --- | --- |
| 当前版本、DLL/运行配置、所列 UI IL | 源码检查 / DLL 检查 | rev1248 实际文件；无新游戏日志 |
| rev1248 选定消费者 | 编译通过 | 23 项；固定基础设施仍从原 Output seed，非完整 1.7 构建 |
| rev1248 六套非 GUI 回归 | 白盒通过 | 合同62、Trade204、Inventory game adapter19、原Inventory19场景、Inventory composition84、Example45；系统 .NET10 |
| rev1248 实际插件/商店 | 编译通过、白盒通过 | 红包32、人才84、商店109；fake services + 真实 DLL，非原生 UI |
| 版本目录选择 | 白盒通过 | 9 检查，两个版本隔离、Resources 排除、缺 1.7 SDK 拒绝、1.6 原布局保留；不加载 DLL |
| 1.6 真实工程及三插件 | 编译通过 | 严格 locked 图重验，基础设施源码重建；不能证明原生游戏或 1.7 自定义 CLR |
| 完整 1.7 图/native adapter/版本包 | 待测 | 引用已恢复，可继续实现；本轮未完成这些阶段 |
| IME/字体/圆角/边框/焦点/滚动/GPU | 待测 | 游戏通过：无，不要求安装中间 DLL |

rev1248 的选定编译输出 `/tmp/phinix-r17-rev1248/build`，consumer probe `/tmp/phinix-r17-rev1248/consumers`；JSON 与顶层测试日志另保存 `local-compatibility-analysis/r17-rev1248-verification`。新目录选择测试源在 Tests/GameAssemblyLayoutRuntimeTests，临时工程在 `/tmp/phinix-r17-rev1248/layout`。1.6 原 GameDlls/Output/正式存档保留。

下一批直接推进新版绘制 adapter 与完整 net10 构建/版本制品路径；只替换实际有问题的绘制能力，保持同一 renderer/theme/action/confirmation。用户的 1.6 bugfix 等验证完成后再按真实远端差异整合。首个可运行联合候选仍主动通知准确组合和测试步骤。
