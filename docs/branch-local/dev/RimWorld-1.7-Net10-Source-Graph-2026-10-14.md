# 1.7 完整 net10 源码图与 1.6 影响（2026-10-14）

基线为已合入 fix 的 Host 13412fe / Common be978cf；游戏引用为 1.7.4977 rev1248，Harmony 3.0.0.0 rc.2。未提交、推送、发布、修改正式存档或原 GameDlls/Output。

## 1.6 是否受影响

**使用 .NET 10 SDK 编译，不等于要求游戏使用 .NET 10 runtime。**本轮仍保留两种构建：1.6 的 net472 DLL，与 1.7 的 net10.0 DLL。不能将 net10 产物放回 1.6 的 Common/Assemblies；即使 AssemblyName/Version 相同，TargetFramework/System.Runtime 等引用和 CLR 要求已不同，可能发生程序集解析或类型加载失败。

真实 1.6 严格锁定构建的四个 DLL 经 PE 元数据检查，PhinixClient、ClientExtensionAbstractions、ClientPresentation.Contracts、Utils 均声明 `.NETFramework,Version=v4.7.2`。本轮新图的 26 个源码 DLL 均声明 `.NETCoreApp,Version=v10.0`。此处不是按目录名猜目标框架，也不是认为两个同名 DLL 能互换。

共享源码改动仍可能影响两版：网络/序列化/恢复、关闭/确认、缓存/主线程逻辑不会因为分开 target framework 就自动隔离。继续保留 1.6 编译、回归及游戏验收；引擎差异放各自 adapter。同样，API 3.0 的参数与程序集归属变更，意味着两版各自的 Host 和插件都要同步重建；这是客户端合同变更，不是 .NET 10 本身要求新增旧客户端支持。维持同一业务权威、renderer/主题/动作系统。

## 本轮实施

新增 `tools/compatibility/build-net10-graph.py`，从 Host、全部内置扩展、红包/人才/Example 的实际 csproj 递归读取 ProjectReference，DLL 型公共 SDK 引用转换为同一源码项目边；生成独立 net10 SDK 工程。26 项包括 Common、Google.Protobuf、连接/认证/用户客户端、合同、Host、内置插件及三个独立插件。

- **没有基础设施旧 Output seed**：Utils/protobuf/Connections/Authentication/UserManagement 等均从真实当前源码重编。
- 保留原 PackageReference 版本与签名信息；Autofac 固定8.4.0、Stateless固定5.20.1。vendored protobuf global.json、源码、SDK pins 和原工程文件未修改。
- 游戏/Harmony 用实际私有编译引用，Private=false；NET10 框架引用来自 SDK，不携带旧 Unity/System DLL 当成 net10 API。
- LiteNetLib 使用原已存在的 netstandard2.0 资产，两个实际消费者显式引用；初次遗漏 Connections.Client 的直接引用属于图生成器问题，已补齐，未改网络业务。
- package runtime 资产按生成工程的真实 project.assets.json 获取（Autofac net8，Stateless net10）；遇同名不同 SHA 明确拒绝。游戏已提供的同名 DLL 不复制进 infrastructure；该目录不是发行包，最终游戏绑定仍需按 identity/profile 审核，不能只靠文件名删除候选依赖。
- 原 AfterBuild/Copy/LoadFolders/release targets **没有执行**；这是完整源码依赖图的迁移编译，并不是原生产工程正式 1.7 构建/打包已闭合。生成工程不自动修改产品/API/pin/manifest。

`tools/compatibility/AssemblyTargetAudit.cs` 是本轮通用 PE 检查器，不执行输入 DLL；读取 TargetFrameworkAttribute、程序集身份与 AssemblyRefs。它通过独立临时 SDK 工程运行，原 bin/obj 不写。

## 检查矩阵

| 范围 | 状态 | 依据/限制 |
| --- | --- | --- |
| 完整 net10 源码图 | 源码检查、编译通过 | 26 项，0 errors；73 warnings 包括既有废弃API/未使用事件、旧SourceLink无Git信息与SDK警告，不改业务绕过 |
| 所有生成 DLL 的目标框架 | 白盒通过 | 26 项均为 .NETCoreApp v10.0；1.6 四核心 DLL 对照为 .NETFramework v4.7.2 |
| runtime 资产清单 | 白盒通过 | 29 个本地分析 DLL：26源码+Autofac/Stateless/LiteNetLib；无 Assembly-CSharp/UnityStubs/Waylith/0Harmony |
| 新 net10 基础设施下的迁移回归 | 编译通过、白盒通过 | 新图产物作 seed，选定23项消费者重新编译；六套：合同62、Trade204、Inventory adapter19、原Inventory19场景、Inventory composition84、Example45；系统 .NET10 |
| 实际插件反射绑定 | 编译通过、白盒通过 | 红包32、人才84、商店109；fake Host/session配实际新DLL，不执行原生UI |
| 1.6 保留 | 编译通过 | 上轮fix基线严格locked真实客户端/三插件工程通过，本轮未改变这些原生产目标；不是新的1.6游戏验收 |
| 1.7 原生产工程/版本目录/完整包/profile | 待测 | 生成工程不运行原targets，API3.0/正式pin/包范围未闭合 |
| 1.6/1.7 原生游戏与业务路径 | 待测 | 游戏通过：无；没有把系统.NET10回归进程当作游戏CLR |

最终生成工程 `/tmp/phinix-net10-graph/closed`，含 graph、source/reference fingerprints、runtime-assets、build.log、各项目真实 packages.lock.json。目标框架审计在 `final/assembly-audit.json`（同一源码/图、首次完整通过产物）；完整26项目再次构建及资产收集在 closed，未改变源码目标。初次 first 的 LiteNetLib 与 regression 的 Stateless 遗漏属于分析引用集，分别补实际直接引用/已解析 runtime asset 后重跑，不作为通过证据。

六套最终回归 `/tmp/phinix-net10-graph/closed-regression`，使用 final/infrastructure 的真实 net10 源码/包资产；反射检查 `/tmp/phinix-net10-graph/consumers`。检查命令参数使用持久的 rev1248 引用与 Harmony；源项目不依赖 /tmp 中的私有游戏 DLL。

## 下一实施批次

新版 backend 仍需要实际消费 IGuiBackend，而非继续依赖忽略描边参数的 GUI.DrawTexture。保持1.6专用实现和同一公共呈现语义。随后把已验证的源码图落到正式双目标构建与**版本化**程序集/扩展路径；Common 只保留真正共享的静态资源，不让两个runtime的同名DLL同时进入进程。

网络 WebRequest 在net10发出obsolete警告，不据此重写红包/人才协议；序列化、Scribe阶段、Pawn交付/返还、未知结果与存档生命周期继续独立验收。首个可运行联合候选仍主动提供准确Host/插件/游戏组合和测试步骤，当前不安装分析产物。
