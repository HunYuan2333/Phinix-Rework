# R17-71 两版 Host 隔离布局首切片

本轮生成分析布局，未安装、提交、推送或发布。正式 About、LoadFolders、API/CLR 身份、三个插件的固定 Host pin 与正式发布记录均未切换。分析包故意保留原 About 的 1.6 支持声明，并移除 PublishedFileId；不把它作为可供用户安装的候选。

## 实现与实物证据

`tools/compatibility/stage-versioned-host.py` 从严格 locked 的真实 1.6 工程输出及完整 26 项目 net10 源图生成两个版本目录。来源：

- 1.6：工作区外于各 Git 仓库的 `local-compatibility-analysis/native-drawing/1.6/Phinix-Rework/Output/phinix-rework`。
- 1.7：`/tmp/phinix-native-drawing/full-net10`，游戏引用为 1.7.4977 rev1248；检查最终 source-fingerprints 与 runtime-assets，源文件或 DLL 改动后拒绝复用。
- 最终布局与只读程序集报告：`local-compatibility-analysis/versioned-host-layout/final`。报告留在本地，不纳入发布制品。

布局只复制 About/Defs/Languages/Textures/Themes 及已核对 DLL。没有 Source、私有编译引用、玩家数据或正式插件 ZIP。LoadFolders 为各版本选择 `/` 与对应版本目录，所有 DLL 均进入 `1.6/Assemblies`、`1.6/Extensions` 或相应 1.7 目录；无 Common DLL 目录。

| 检查 | 实际结果 | 状态 |
|---|---|---|
| Host 与内置扩展来源/TFM | 21 个同名源码程序集 CLR identity 在两版一致；1.7 为 net10，1.6 除既有 protobuf net45 外为 net472；没有改 vendored pin | 源码检查、DLL 检查通过 |
| 分版实物 | 1.6 31 个 DLL，1.7 24 个 DLL；闭包不引入红包/人才/Example 的 5 个独立插件程序集 | 白盒通过 |
| 源码程序集之间的引用 | 名称/版本匹配；CLR identity 自身包含 culture/token，两版一致。引用报告仅核对名称/版本，未宣称逐项 token 或类型绑定验证 | 白盒通过 |
| 中立呈现合同 | 不引用 Assembly-CSharp/UnityStubs/UnityEngine/Host/游戏 SDK | DLL 检查通过 |
| 所有 1.7 外部引用 | 缓存中均找到；游戏/Harmony/Steamworks 引用版本相符 | DLL 检查通过 |
| .NET 运行时升级绑定 | LiteNetLib netstandard2.0→游戏2.1；Autofac net8 System 引用→游戏10，共 11 处差异已记录；不能由名称存在推导游戏 CLR 绑定成功 | 待测 |
| 原 1.6 依赖绑定 | 5 处库引用版本差异：protobuf→Memory/Unsafe、Autofac→DiagnosticSource、Memory/Tasks.Extensions→Unsafe；未改旧基线依赖 | 待测 |
| 真实目录选择 | 复用实际 ClientGameAssemblyLayout，对两版分析目录逐项检验选中目录、DLL身份无重复、Host/合同/GameAdapter存在；连同已有错误/回退场景17检查 | 编译通过、白盒通过 |
| 游戏 DLL 污染 | 以游戏文件名、程序集名和 Harmony 显式拒绝表检查；无游戏/UnityStubs/Waylith/Harmony DLL | 白盒通过 |
| 游戏加载、绘制、业务与存档 | 本轮未启动游戏；About 支持声明/API3联合身份/包格式尚未闭合 | 待测；无游戏通过 |

目录探针编译零警告/错误，在系统 .NET10 执行。它读取 DLL 元数据，不运行插件代码；不能替代游戏的 ModContentPack、Waylith CLR 或 Assembly.Load 路径测试。已有完整两版编译证据见前一切片，本轮未重复全图构建。

## 已确认的 managed 包阻断

以下均来自当前实际源码，不是新版游戏的推测：

- `Extensions/PluginStore/Tools/ManagedPackageTool/Program.cs:76` 写死 manifest `targetFramework=net472`。
- `Dependencies/Phinix.Common/Common/Utils/Framework/ManagedExtensions/ManagedExtensionManifestReader.cs:31` 拒绝其他 targetFramework。
- 同目录 `ManagedExtensionPayloadInspector.cs:54` 拒绝非 `.NETFramework,Version=v4.7.2` 的 DLL。
- `HostProfileExport.cs` 目前 profile 选择主要依据 phinixRange，不能仅用同一产品版本区分两套游戏/运行时制品。

因此，一个 RimWorld Mod 可按 LoadFolders 分版，不代表一个现有 managed ZIP 已支持两套 DLL。当前 managed 包是单 payload/单 TFM，不能放入双份同身份 DLL或将 net10 DLL 冒称 net472。本轮不弱化保护，不绕过预检激活。

## 下一实施切片

1. 共同合同先显式保留 targetFramework，并支持可验证的 net472/net10 声明；payload TFM 必须与声明一致。游戏/运行时匹配进入预检，1.6 明确拒绝 net10；不只扩大 parser 白名单。
2. 对照商店目录/profile 的现有选择机制，决定每游戏独立 payload 的选择字段。暂不发明双 payload ZIP；同产品版本的两版 profile 不能只靠 phinixRange 区分。同步 Host、三插件及 Example 打包/拒绝组合测试。
3. 闭合 API3 身份、程序集绑定、正式构建 targets 与候选摘要，再给用户 R17-33 第一轮可安装包及准确测试步骤。外部 System 版本升级绑定与真实 native backend 仍需游戏验证。

上述修改只涉及基础设施与版本适配，不调整业务权威、托管、回执、返还、交付或关闭视图的语义。
