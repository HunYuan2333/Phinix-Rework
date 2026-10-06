# 托管 DLL 分步实现

2026-10-06 规则更新：宿主库引用允许唯一的同主版本升级，名称/语言/公钥不变，优先精确版本；启动绑定到已声明且启动前已加载的真实程序集并记录版本。包自身/依赖包身份、摘要与兼容范围仍严格。本项取代旧的所有宿主引用均须精确版本的说明，见[验收](宿主引用向上兼容验收.md)。

[English](ManagedDllImplementation.md)。2026-10-04，`dev` 分支。依据[双路线评估](双路线与托管DLL扩展评估.md)。完整 Mod 提供工坊索引/链接；GitHub 扩展由 Phinix 下载管理，不生成 RimWorld Mod 壳。

## 执行批次

| 批次 | 实现 | 独立验收 | 状态 |
| --- | --- | --- | --- |
| M1 | 通用托管清单 v1、目录与只读库存契约；文件归属/状态读取 | 严格 JSON、声明边界、停用/待卸载可见、字节变化/额外文件/损坏记录/链接失败、读取不执行 DLL | 已完成静态契约和库存；未接加载 |
| M2 | 加载前恢复和候选集合、程序集身份/引用检查、受限解析；通用环境归属 | 禁用/待卸载/坏包不加载，冲突和缺失引用可诊断，商店停用仍独立加载 | M2a/M2b 代码和运行时回归完成；游戏/平台验收待做 |
| M3 | 通用扩展管理接库存；包启停、期望/实际状态、待重启和卸载意图 | 内置模块仍可管理；未加载包仍可启用/卸载；依赖阻止有明确原因 | 代码/本地回归完成；游戏验收待做 |
| M4 | catalog 新版本与托管 ZIP 校验；复用下载/提交事务 | 新旧格式不可混装；确认前后刷新；取消、断电/重复恢复和归属保护 | M4a–c 代码/回归与 staging 真实下载完成；游戏验收待做 |
| M5 | 新版 DLL Playtest 与游戏验收 | 无 RimWorld Mod 条目；Tab/计数/100 白银；停用、卸载与重新安装；数据保留 | 1.2.1 已公开发布；用户报告基础安装到卸载全流程通过，扩展场景待验收 |
| M6a–e | 内置商店、资源/存档前置、红包及人才贸易可下载拆包、工坊/生产交付 | 商店恢复入口、两插件历史数据/缺包保护、干净升级、Windows/Mono/多网络 | M6a 本地内置完成；其余后续 |

2026-10-04 发行决定补充：正式商店随主体内置，红包与人才贸易从主包拆为可下载托管插件。M3–M5 顺序不变；M6 拆为可验证子批次，资源加载/人才存档组件兼容先于拆包。详见[内置商店与官方插件拆分计划](内置商店与官方插件拆分计划.md) / [English](BuiltinStoreAndOfficialPackages.md)。

M1 的静态托管 manifest 使用独立 schema v1 与固定 `management: phinix-dll`，不是旧商店 catalog/manifest v1 的新含义。M4 再接 catalog 新版本，stable/published 传输协议可保留外层版本但必须显式宣布所支持 catalog 版本。旧 Worker/客户端当前只接旧 catalog，M1 不改变线上源。

## M1 的存储契约

`<SaveData>/Phinix/ManagedExtensions/`：

- `packages/pkg-<SHA256(sourceId + LF + packageId)>/`：完整单包；根 `manifest.json`，声明的 `Assemblies/*.dll` 和可选 `Resources/*` 文件。没有 About、Defs、Patches 或游戏原生 Languages 扫描。
- `state/installed/pkg-<hash>.json`：安装归属凭据，包含来源/endpoint 摘要、包 ID/版本、清单摘要、固定 catalog 快照与摘要、载荷摘要、安装事务 ID、全文件路径/长度/摘要；清单自身也必须在文件集合中。
- `state/desired/pkg-<hash>.json`：启停/待卸载意图，并绑定来源/包 ID/当前清单摘要。缺失或损坏不默认启用。
- `transactions/`：后续安装/卸载日志、暂存与隔离；不属于加载候选。

路径分配和 M1 库存读取不创建目录、不改文件、不加载程序集、不执行插件；仅凭一个 manifest 或目录名不能认领安装。完整收据与字节核验通过显示为 `ContentVerified`，不代表 PE 身份、依赖、兼容或模块注册已经验收。M2 另外确定可加载候选。

清单声明包版本与 CLR 四段版本，兼容范围、包依赖、模块及其所属程序集/入口类型、外部 Mod 前置、程序集完整身份/路径/摘要及资源文件摘要。一包允许多个模块和程序集。归属库存保存期望包状态 `Enabled`、`Disabled`、`PendingRemoval`；实际模块激活仍由原有生命周期提供，不合并为一个布尔值。

库存枚举以 installed 凭据为入口。单包失败产生稳定错误码而不隐藏其他包；未知目录不加载、不自动删除。文件集合必须精确一致，禁止符号链接/junction、路径别名、重复大小写、路径越界及改变字节。关闭商店不影响通用层使用这些契约。

## 后续边界

安装确认授权新包下次启动启用；当前游戏不执行刚下载的 DLL。已加载包卸载保存意图，重启后在加载前再次核验依赖和归属，事务删除。恢复失败保持排除加载、保留记录。卸载不删除插件设置、业务恢复记录和存档。

日志串联 source/package/version/hash、事务和启动 ID、核验/解析/发现/激活/移除阶段；凭据、远端正文和用户本地路径不进入公共结构化记录。损坏包的详细诊断保留内部日志，界面使用安全的稳定错误码。

旧 `installation-v1` 本地 Mod 收据、Playtest 1.1.0 和既有目录不自动搬迁；新测试包发布新版本和不可变资产。红包/人才贸易的拆分与历史存档迁移另行验收，不在 M1 搬动其数据或业务代码。

## M1 实现与验证记录（2026-10-04）

已实现：Common/Utils 中通用静态格式、版本/范围、不可变清单、归属库存和稳定错误码；客户端新增 `ClientEnvironmentPaths.ManagedExtensions`，抽象程序集/兼容版本 1.4.0，保留原有成员；开发者指南中英文同步 §8.20。格式示例见 [managed-extension-protocol-v1](managed-extension-protocol-v1/README.md)。宿主尚未注册库存服务或读取它作为加载输入；M2 再接入，不把 M1 当作安装功能已经上线。

库存读取不会认领未知目录、修改状态或删除文件；损坏归属和目录键不一致会保留失败条目。收据保留 source/endpoint/catalog/artifact/transaction 的固定关联，但这些本地字段不是签名，也不能冒充远端批准。全局诊断（例如库存数量超限）要求未来加载器整体关闭该托管候选域。静态读取是当时文件事实，不能代替加载前重新核验或事务并发控制。

实际命令，从仓库根运行：

```sh
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mcs -langversion:7.2 -out:/tmp/phinix-managed-extension-mono-smoke.exe -r:Common/Utils/bin/Release/net472/Utils.dll Tests/PluginStoreRuntimeTests/Fixtures/ManagedExtensionMonoSmoke/Program.cs
MONO_PATH=Common/Utils/bin/Release/net472 mono /tmp/phinix-managed-extension-mono-smoke.exe Tests/PluginStoreRuntimeTests/Fixtures/ManagedPayload/bin/Release/net10.0/Fixture.Plugin.dll
git diff --check
```

结果：商店 **675 项断言通过**（旧基线 585，本批新增 90）；框架回归通过；最终 solution Build **0 错误、6 项既有警告**，主体/商店产物刷新。下载检查工具 net472 编译通过。Mono 使用生产 Utils，清单、完整库存、启用/停用/待卸载状态、篡改、实际符号链接与悬空链接、无执行检查通过；候选 DLL 仅作字节读取，未加载或执行。早期还单独编译了 Common/Utils net472（0 错误/警告）。恢复此前工具链产生的完整解决方案 Rebuild 问题不属于本批，仍使用普通 Build，不使用 --no-incremental。

警告为已有弃用成员/旧框架 SDK 提示；回归和下载工具另有 NU1900（受限网络无法访问 NuGet 漏洞查询）。文档相对链接、空白及示例清单/收据/期望状态摘要一致性检查通过。未做游戏验证、Windows junction/磁盘故障验收，未改变 GitHub 已发布资产、目录版本或 Worker 配置，未提交/推送主仓库。M1 没有接启动加载、启停写入或安装/卸载恢复；这些按 M2–M4 实现。

## M2a 静态预检实现与验证（2026-10-04）

M2 拆为可独立检查的两个步骤。本批 **M2a** 已实现；下一步 **M2b** 接启动租约、事务恢复、受限 AssemblyResolve/冻结字节加载、统一发现过滤和客户端环境归属。M2 全批仍未完成，不要求玩家现在验证新 DLL 路线。

新增通用 `ManagedExtensionMetadataReader`，只读取 PE/CLI/ECMA-335 元数据，获得真实程序集完整身份、AssemblyRef、目标框架、入口类型和 `PhinixExtension` 属性 ID/DependsOn；不调用 Assembly.Load、ReflectionOnlyLoad、GetCustomAttributes 或插件构造器。`ManagedExtensionPayloadInspector` 冻结并再次核验 DLL 长度/摘要，将真实声明与清单逐项比对，拒绝漏报模块、伪报入口/ID/依赖、CLR 身份与目标框架不一致。调用者取得字节时获得副本，不能改变已检查的缓冲区。

`ManagedExtensionCandidatePlanner` 接受本次库存条目实际使用的清单、检查后载荷和宿主提供的可信事实。损坏/停用/待卸载/缺失意图/未检查条目被排除；全局库存不确定会排除整个托管域。分别核验游戏/Phinix/抽象版本、实际激活的外部 Mod、包版本依赖、CLR 引用和模块依赖。不同源的冲突包、程序集/模块与宿主或其他候选冲突均不会按枚举顺序选一个。包依赖环拒绝；后续提供者拒绝传播到依赖包。跨包 DLL 引用只允许来自显式包依赖，不能借用不相关包的文件。

每条计划结果都有不可变 `ManagedExtensionCandidateAudit`：启动 ID、阶段、稳定原因码、source/package/record/version、manifest/catalog/artifact 摘要、安装事务和状态操作 ID。失败条目不暴露可执行载荷。当前返回审计数据；M2b 再接宿主日志输出，以及检查失败、解析、注册/激活和恢复日志。没有新增公共本地路径、凭据或异常正文。

本批格式边界：要求真实 TargetFramework 为 `.NETFramework,Version=v4.7.2`；CLR 引用完整身份精确匹配，兼容范围不会自动产生绑定重定向。入口是公开非抽象/非泛型、公开无参构造的 `IPhinixExtensionModule`，可沿同一程序集的基类/接口继承；跨程序集基类推导暂不支持。拒绝多模块程序集、指针表/Edit-and-Continue 布局、混合模式/native-entry/32BITREQUIRED。元数据表 0–44，单表最多 200000 行/总计 1000000 行、最多 20000 类型/256 引用/64 标记模块，类型名称总量 2 MiB，读取的 blob 最多 16 KiB，继承/嵌套深度 64；超限明确失败。此检查不是 IL 完整验证、签名信任或代码沙箱。模块内部依赖环、已加载程序集的发现范围和事务并发/恢复还要经 M2b 的最终启动门槛，`Payload` 非空本身不能触发执行。

新增 `Tests/ManagedExtensionRuntimeTests`，直接引用生产 Utils，真实 net472 插件、辅助 DLL 和提供者 DLL 只作为文件复制；包含入口静态初始化哨兵。CI 增加 .NET 10 预检 harness，远端 CI 尚未运行。验证命令：

```sh
dotnet restore Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --ignore-failed-sources -p:NuGetAudit=false -p:BuildInParallel=false
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

结果：新 harness **230 项断言**分别在 .NET 10 和生产 net472/Mono 通过，包含 128 个固定种子单字节破损样本、截断/PE 错误、真实入口与引用、冻结字节、兼容/依赖/冲突/环、全域库存不确定与审计关联；插件/辅助 DLL 未进入 AppDomain，静态初始化哨兵没有执行。既有商店 **675 项**和框架回归通过；完整主体/商店 Build **0 错误、7 项既有弃用/旧 SDK 警告**；下载工具 net472 编译通过（已有 NU1900）。

本机无 PowerShell，未直接执行 `.github/scripts/check-artifacts.ps1`；通过 Python 对该脚本声明的必需文件、预览唯一 DLL/身份、LoadFolders、禁止游戏引用及禁止测试 DLL 分发做等价检查。发行内容检查通过，无新增第三方运行库。未做游戏、Windows/远端 CI、真正加载/恢复/卸载验收；未修改商店远端索引、资产或 Worker。业务协议、物品归属和插件数据未变动。

## M2b 启动加载与卸载恢复实现（2026-10-04）

本批代码和运行时回归已完成，覆盖上节 M2a 当时未接入的宿主部分；**下一步 M3** 为通用扩展管理增加包级操作，再由 M4 接联网托管安装。旧商店仍下载旧本地 Mod 格式；新 DLL Playtest 的 Tab/100 白银游戏验收在 M5。本批没有发布新远端资产、改 catalog/Worker 或自动迁移旧 Mod。

`Utils.Framework.ManagedExtensionRuntime` 不依赖商店或游戏程序集。客户端在 Mod 加载结束后经既有主线程分发器调用它，然后走通用发现、Register、Activate、Shutdown；自动连接延后到框架初始化后。只读 `IManagedExtensionInventoryService` 报告启动库存/诊断/字节加载事实；模块生命周期仍由框架报告。客户端抽象升为 **1.5.0**，保留旧构造方法，新增显式托管 source/package/root 归属。按真实 Assembly 对象认领字节加载 DLL，不伪造 RimWorld Mod 条目或 SourceModRoot；商店关闭不影响宿主启动能力。

启动持有 `state/runtime.lock` 独占租约直至关闭，Mono 上验证了跨进程互斥。M3/M4 写入必须经同一宿主协调器。加载前先恢复卸载，再核验完整文件树和固定字节，校验宿主/托管联合模块图（最多 4096 声明）及实际 CLR 引用。全部直接停用的模块包不加载；缺失依赖、冲突、循环和拒绝提供者传播阻止候选。冻结 DLL 总量限制 256 MiB，CLR 引用环拒绝。

仅 `Assembly.Load(核验后的字节副本)` 加载；受限解析器只处理自己拥有的请求程序集及其声明引用，返回已加载的准确宿主对象或批准的托管对象，不探测未知文件。旧 Phinix 解析器增加通用守卫，避免对托管请求走旧文件探测；托管根不加入旧 probe dirs。入口及运行时类型再次匹配，失败包及已部分加载的失败程序集不进入统一发现。已加载字节不能卸载，运行时不是代码沙箱，也不控制其他 Mod 自己的解析器。

卸载日志为 `transactions/rm-<operationId>.json`，持久化原始收据/期望状态/清单，再把完整包原子移动到同目录下隔离目录，逐个复核并删除已认领文件。重放只接受严格规范日志、相同归属和完整原包或隔离剩余子集；重复恢复无副作用。反向依赖包含停用包、宿主模块及已加载/引用的程序集；待卸载链从依赖方开始。改动/额外文件、目录、状态、日志或归属不确定时停止并保留证据；未知事务关闭托管加载域。只删除包代码和对应安装/期望状态记录，不删除 ExtensionData、设置、业务恢复记录或存档。

公共结构化日志串联 UTC 时间、启动 ID/序号、阶段/稳定原因码、source/package/record/version、manifest/catalog/artifact 摘要、安装事务/状态操作和模块/程序集；不输出本地绝对路径、凭据或异常正文。恢复异常也进入内部诊断回调，客户端 DevMode 开启时输出详情。注册/激活/关闭记录来自真实通用生命周期。当前库存是启动快照，尚不表示启停写操作或界面已经交付。

本批实际验证命令（仓库根目录）：

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

结果：每个 **.NET 10、生产 net472/Mono** 运行均通过主进程 **307 项** 和真实加载/注册子进程 **15 项** 断言（各共 322 项）；覆盖六个断电检查点、重复恢复、文件/状态/日志篡改、数据保留、真实跨进程锁、宿主/托管依赖环和正常链、统一生命周期、解析请求范围、归属及退出后发现排除。商店 **677 项** 及框架回归通过。完整主体/商店 Build **0 错误、7 条既有废弃 API/旧框架警告**；net472 下载检查编译通过，含既有 NU1900 网络查询警告。PowerShell 不可用，原打包脚本未直接运行；等价 Python 检查通过必需文件、预览唯一 DLL/身份、LoadFolders、无游戏引用/测试 DLL；diff 和本批文档链接检查通过（开发者指南旧章节已有链接问题未在本批清理）。较早单独构建 Client 的 `Release 1.6` 因旧项目配置映射失败，完整 solution 的配置映射已通过。

未执行实际游戏、Windows/macOS、远端 CI 或真实断电验收。.NET 10/macOS 因 FileStream.Lock 不支持明确拒绝，生产 net472 的其他平台留待 M6。业务协议、物品所有权和旧插件数据未迁移。启动/自动连接时机改变，建议先做小范围游戏冒烟：重新启动游戏，确认连接及聊天/交易/商店 Tab、扩展管理和 Mod 设置入口仍正常，查看日志中 ManagedStartupCompleted 与最终模块状态；截图/日志才能作为游戏证据。M3 后再测未加载包的启停/待卸载，M4/M5 后跑完整托管安装和 100 白银验收。

## M3 包管理与异步宿主界面（2026-10-04）

M3 代码和本地回归完成，游戏验收待做。**下一步 M4**：新版目录、托管 ZIP 校验及下载/安装事务；M5 再发布可点击 Tab/计数/100 白银的新 DLL Playtest。正式商店首版采用文字简介、标签和随包通用图标，不请求远端图片或 README；范围已同步至[发行计划](内置商店与官方插件拆分计划.md)。

宿主注册 Common/Utils 的 `IManagedExtensionManagementService`。`Refresh` 重新读取归属、内容和期望状态，同时保留不可变的本次启动实际结果；未加载/失败包仍可见。扩展管理增加“模块 / 托管包”页，保留内置模块和日志。托管包卡片展示期望状态、字节是否加载、诊断与待重启，包内“模块”菜单可恢复未加载包的模块开关；重复/碰撞的模块身份不允许从包菜单改设置。

启用前重新验证文件、PE 元数据、兼容性、宿主与包依赖/模块图及拒绝传播。停用保护启用中的依赖方；卸载还保护已停用的依赖方，先明确标记依赖方待卸载后，允许整条链在下一次启动按依赖顺序恢复。宿主模块依赖及非托管 CLR 引用仍保守阻止删除。包开关与模块开关是独立意图；两者都在重启生效，不调用当前会话的 Activate/Shutdown，不删除正在使用的代码或业务数据。

状态写入由持有生命周期租约的同一协调器串行处理。每次点击重读库存，比较来源/目录/制品/安装事务/manifest/状态操作身份，拒绝过期页面；完整新 JSON 同目录 Flush 后原子 `File.Replace`，无先删后移降级。提交前再次核验原状态、凭据、临时文件和完整文件树；取消只在提交点前生效。日志按 startup/stage/code 和新 operation ID 关联请求、准备、成功或拒绝；替换阶段异常报告 `ManagedStateWriteUncertain`，需要刷新核对，不伪称回滚。异常/未知事务阻止写入，修改过的证据不清理。

界面主线程只捕获现有模块设置、绘制、保存模块开关与处理完成结果。扫描、摘要/元数据校验和状态写入通过后台控制器执行，列表虚拟化且按版本缓存；关闭窗口取消任务。“状态已保存但刷新失败”单独提示，详细异常仅进入内部诊断，不暴露公开路径。确定性几何测试覆盖极小及正常窗口尺寸。

实际验证命令（仓库根目录）：

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release --no-restore
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

结果：每个 **.NET 10 和生产 net472/Mono** 运行通过主进程 **355 项**，正常真实加载子进程 **15 项**，已加载包管理意图子进程 **18 项**（各 **388 项**）。相对 M2b 新增 66 项，覆盖未加载包、过期状态/来源、反向依赖链、取消、损坏 PE/文件、未知事务、提交前后中断、已加载代码/统一生命周期保持、后台控制器及提交成功后刷新失败。现有商店 **677 项**、框架及响应布局回归通过；完整主体/商店 Build 和 net472 下载工具编译通过，既有警告见输出。PowerShell 不可用，原打包脚本未直接运行；等价产物检查、翻译键一致性及 diff 检查通过。

未执行实际游戏、Windows/macOS、远端 CI 或物理断电验收。同目录替换/Flush 的平台与断电持久性不能由故障注入替代。尚未发布新托管测试包，游戏中没有新包时“托管包”页为空是预期；先验证页面切换、设置恢复和既有模块控制，M4/M5 后验收完整安装→重启→操作→停用/卸载。当前旧商店仍用本地 Mod 格式，不自动迁移旧凭据；业务在途状态/人才存档的卸载保护仍须在 M6 前置工作处理。没有改变远端 catalog/Worker 或提交推送。

## M4a 新版目录与托管 ZIP 静态校验（2026-10-04）

M4 分为可独立验证的三批，**本批 M4a 代码/本地回归完成，M4 全批未完成**：

| 子批次 | 范围 | 状态 |
| --- | --- | --- |
| M4a | catalog v2、显式路线、stable/published 版本绑定、托管 ZIP/真实静态元数据校验 | 代码/回归完成 |
| M4b | 通用宿主安装服务与持有租约的事务；安装前联合依赖/现有库存核验，提交/取消/启动恢复/重复回放 | 本地实现/回归完成 |
| M4c | 新版目录生成器/Worker/传输缓存/依赖规划与 UI；确认前后 freshness、预算/取消/审计，保留旧记录移除能力 | 客户端/新来源/部署与真实下载完成；游戏验收待做 |

新增独立 `ManagedStoreCatalogSnapshot` / `ManagedStoreRecord`，不伪造 RimWorld Mod ID，也不传给旧本地 Mod 安装器。catalog schema 2 明确区分 GitHub phinix-dll / managed-dll-zip 与工坊 rimworld-mod。内嵌完整托管 manifest v1 并匹配目录身份；作者、许可、1024 字符简介和最多 8 个标签由固定目录提供，没有图片/README 网络地址字段。旧 catalog v1、manifest v1 的 Mod 语义和 `installation-v1` 凭据不重解释。

外层 stable/published 保持 schema 1，显式 `catalogSchemaVersion: 2`；来源、snapshot、长度/摘要、固定 catalog.json 身份逐项绑定。新版解析入口与旧入口分开，旧入口拒绝版本 2；固定哈希资源 URL 复用，仍不接受任意下载地址或工坊下载。M4a 尚不调用新版网络请求、缓存或 Worker，不触发线上切换。

`ManagedStorePayloadValidator` 冻结并验证压缩字节，拒绝超限、路径逃逸、链接、大小写/文件目录别名、未声明文件/目录和 Mod 壳。只接受 manifest.json、声明的 Assemblies DLL / Resources 及其祖先目录。ZIP/raw manifest 摘要、解析后的全声明和全部文件长度/摘要分别核对，再调用生产静态 PE/CLI 检查实际程序集完整身份、net472、模块/入口/DependsOn。有效校验不加载 DLL、不调用属性构造器；返回冻结 manifest 与检查证据，不授权另一份或随后变化的字节。包数据资源允许并校验，不代表商店远端图片索引或自动原生内容发现。

兼容性、宿主/包联合图、加载中身份、现有库存及安装授权/事务仍在 M4b 检查；因此静态“校验通过”不能称为安装成功。目录/ZIP/单文件/展开/条目/膨胀均有界，最大包的 CPU/内存及 Unity 游戏平台验收仍待做。新格式/示例详见[协议 v2](managed-store-protocol-v3/README.zh-CN.md) / [English](managed-store-protocol-v3/README.md)；示例资产/DLL 摘要为占位，不是发布物。

实际验证命令（仓库根目录）：

```sh
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore -p:BuildInParallel=false
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --framework net472 --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStorePayloadCheck/PluginStorePayloadCheck.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

每个 **.NET 10、生产 net472/Mono** 运行通过主进程 **427 项** 与真实加载子进程 **15 + 18 项**（各 **460 项**），本批新增 **72 项**。覆盖两路线、新旧拒绝、元数据摘要/版本不一致、目录/manifest 身份冲突、非法格式/简介/标签、固定路径/非选中记录、有效多 DLL、资源内容/长度变化、未声明 Mod/DLL/目录、路径/链接/别名、重复/缺失文件、压缩炸弹、伪造模块/坏 PE、双重 manifest 绑定、对象顺序及协议示例。旧商店 **677 项**通过。完整主体/商店 Build **0 错误、2 条既有 SDK 警告**；net472 下载及 net10 静态检查工具编译通过，工具有既有 NU1900 网络查询警告。PowerShell 不可用，等价产物与本批文档链接/摘要/diff 检查通过。

未执行游戏、Windows/macOS、大包性能、远端 CI、真实下载/断电验收；未发布新的 GitHub 资产/catalog、部署 Worker 或提交推送。当前商店 UI/下载仍是旧 Mod 格式，不能用游戏点击证明新托管安装成功。下一步 M4b，再 M4c，M5 才进行全程托管 Playtest 游戏验收。业务协议、物品所有权和旧插件数据未修改。

## 2026-10-05：商店首版本地交付

M4b 宿主事务与 M4c 客户端联网/UI 已完成本地实现与回归；按用户“先把插件商店做完”提前接入 M6a 内置发行。当前 562 + 15 + 18 + 16 = 611 项 .NET 10/Mono，旧商店 677 项，Worker 119 用例。M5 Playtest 1.2.0 本地候选已校验。明确授权后已公开发布并部署 staging，.NET 10/Mono 无代理真实下载通过；游戏验收未完成；见[当前交付与测试清单](商店首版交付与验收.md)。
