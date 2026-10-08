# 扩展管理已知问题 / Extension management known issues

记录：2026-10-07，dev，F2 已提交 `58c043e`，F3 游戏反馈阶段。用户要求：先定位并标记后续修复，不修改行为代码。状态：OPEN；本轮仅源码审计，无游戏复现或新增/运行测试。

## EM-01：禁用托管模块后上方条目消失，包状态显示造成误解

用户路径：示例插件模块禁用后确实不实例化；上方扩展列表找不到条目，无法从原入口重新启用；下方安装/卸载区仍显示“已启用”，卸载仍可操作。

已确认代码链：

1. 模块复选框写入 `Settings.DisabledExtensions`（`ExtensionControlSettingsPanelProvider.cs:96`、`Settings.cs:198`），并未改变包的 `ManagedExtensionDesiredState`。
2. `ManagedExtensionModuleGate.cs:54` 对全部模块均被禁用的包返回 `ManagedAllModulesDisabled`。`ManagedExtensionRuntime.cs:94` 的 FinalModuleGate 将此结果保存到包 Code；随后只为 Code 为空的包建立/加载程序集条目。
3. 上方 `ExtensionControlSettingsPanelProvider.cs:58` 的列表只取 `frameworkClient.ExtensionResults`。托管包已在更早的加载阶段被过滤，没有模块 Type 可供发现，无法走 `PhinixExtensionRegistry.cs:117` 的“禁用但保留结果且不实例化”分支。因此条目消失不是禁用对象被误实例化，而是管理列表遗漏未加载的声明。
4. 下方 `ManagedExtensionManagerView.cs:127` 显示包的持久期望状态，而非模块实际激活状态。包仍 Enabled、模块 ID 在禁用设置里，两个字段可以同时成立；UI 未充分说明两层语义。

注意：下方包行代码已有基于 manifest 的“模块操作”菜单（`ManagedExtensionManagerView.cs:76–90`），读取并修改模块禁用设置。不能仅根据上方条目消失认定所有恢复入口均失效；该菜单实际是否可用/明显，以及 ModuleSettingsBlockCode 的值仍待游戏核实。记录用户“原入口无法启用”的反馈，不虚构菜单已验证。

后续修复范围：提供不依赖实例化/程序集加载的声明列表或明确恢复入口；展示包期望状态、模块禁用意图、当前加载/激活状态与待重启区别。严禁为了保留条目而实例化禁用插件、跳过托管安全检查或热卸载程序集。

## EM-02：卸载后重装被拒绝，只报告 ManagedDependencyConflict

用户日志：2026-10-07T10:09:47.2849320Z，clientRequestId `c6edbbb5894046d989de40d69e573615`，source `phinix.official`，stage `ManagedStore`，event `managed.operation_failed`，reason `ManagedDependencyConflict`。前一条 ManagedInventoryRefreshed 为库存读取审计；堆栈进入 Controller.Plan，说明失败发生在规划阶段，不能据此声称下载失败、服务器错误或安装事务失败。

已确认可达代码链：

- `ClientEnvironmentCapture.cs:80` 将 `Settings.DisabledExtensions` 带入环境快照。禁用设置与包库存独立；托管卸载实现不负责清除宿主 Settings 中的模块 ID。
- `ManagedStorePlanner.cs:90` 的 Compatible 检查拒绝所有模块 ID 仍在 DisabledModuleIds 中的候选包，原始原因 `ManagedAllModulesDisabled`。
- Search 在 `ManagedStorePlanner.cs:72` 捕获 StoreValidationException 后跳过候选；其他身份检查失败也可能在 :68 被折叠。最终 :47 统一抛出 `ManagedDependencyConflict`，具体原因丢失。
- 所以“禁用示例 → 卸载 → 保留模块禁用设置 → 重装相同模块”存在明确的拒绝链，并且吻合这次汇总错误。单条日志未包含禁用 ID、manifest、库存及原始候选失败，不能证明本次唯一原因。

还需核实的替代原因：卸载是 PendingRemoval 意图还是已在启动恢复中物理删除？Planner.Local 在 :101 拒绝非 Enabled 的现存记录；当前进程已加载的程序集不可卸载，CheckIdentities 在 :111 检查程序集占用。即使库存为空，同一会话的残留已加载程序集也可能造成冲突并被汇总成同一错误。不要将保护性的待重启冲突处理成允许重复加载。

后续修复范围：保留/展示规划器原始拒绝原因；明确卸载、待重启、模块禁用设置与重装之间的策略。可以评估允许安装但保留禁用意图，或提供用户明确恢复模块设置的入口；不能未经设计自动清除禁用偏好或绕过程序集身份冲突。

## 后续验收

- 托管示例禁用→重启：无实例化，上方仍有可识别条目或明确恢复入口，包/模块状态展示准确。
- 从恢复入口启用→重启：只激活一次；其余禁用 ID 不被改动。
- 禁用→卸载→重启确认物理删除→重装：具体拒绝原因可见，或按明确策略安装并保留禁用意图。
- 同一会话 PendingRemoval/已加载程序集重装仍安全拒绝并提示重启；不得重复加载或破坏事务所有权。
- 覆盖全禁用、部分禁用、多模块、依赖模块和混合包状态的确定性回归；游戏检查 UI 可见性与恢复路径。

## English summary

EM-01 (OPEN): the managed preload gate excludes packages with every module disabled before ordinary discovery. The upper settings list only uses discovery results, so it loses the disabled declarations. The lower manager displays package desired state, which can remain Enabled independently of module disable intent. A manifest-based module action menu already exists; its actual accessibility in the reported game session is not confirmed.

EM-02 (OPEN): retained module disable IDs can reject reinstallation with ManagedAllModulesDisabled, which the planner collapses into ManagedDependencyConflict. The supplied log confirms planning failure, not its unique underlying cause. Pending removal records or assemblies retained in the current process are alternative protected conflicts to check. Future work must improve diagnosis/recovery UX while preserving user intent, no-instantiation behavior, transaction ownership and assembly safety. No behavior fixes, tests, commits or deployment were performed for this audit.

## 本次源码复核补充（2026-10-07）

保留已有问题记录及计划链接，复核当前 dev/F3 工作区，未修改实现、禁用设置或库存。

- 下方状态文本还区分程序集已加载/未加载，但“已启用”来自 `Desired(p.DesiredState)`（ManagedExtensionManagerView.cs:126–127）；不能将包 Enabled 解读为模块已实例化或已激活。
- 下方模块菜单的可操作性不是由全禁用本身决定：Refresh 将 `global ?? OwnershipGate(row) ?? ModuleSettingsGate(row, inventory)` 作为阻止代码（ManagedExtensionManagementRuntime.cs:26–28）。ModuleSettingsGate 检查启动宿主或其他库存包的模块 ID 冲突（:101–106）。因此需要实际观察该按钮及诊断，不能假定禁用后完全没有恢复入口。
- 日志堆栈来自 Plan 的库存读取，然后报告规划失败；此阶段没有进入 Download/Install。日志 bytes=0 本身不是下载失败证据，也未提供本次原始候选拒绝原因。
- OPEN EM-01 / EM-02 保持后续修复项。本轮仅文件搜索、源码读取、文档补充和 diff/status 检查；没有构建、运行测试、游戏复现、提交或推进 F3 验收。

English checkpoint: verified the existing records against the current worktree. Package desired state and assembly load state are separate; the manifest module menu is gated by inventory/ownership/module conflicts, not merely by all-disabled intent. The supplied stack identifies planning failure before download/installation. Both issues remain OPEN for a later authorized fix.

## 修复实施 / Implemented fix（2026-10-07）

用户已明确授权先修复以解除核查阻塞。EM-01/EM-02 状态更新为 **IMPLEMENTED — GAME CHECK PENDING**；前文 OPEN 为初始诊断记录。未提交/推送，已有 F3 与并行修改保留。

- `ExtensionDisplayState.IncludeDisabledSettings` 在两个扩展管理列表的缓存重建中合并保存的禁用 ID，大小写不敏感去重。未加载或已卸载的条目仍可明确恢复，附中英恢复说明。这些仅是界面条目；不写入实际发现结果/注册表，不加载程序集，不实例化模块。没有自动清空用户禁用偏好。
- 下方包列表显示“包期望”和“模块禁用：n/总数”，区分程序集加载状态、包状态与模块设置。
- Planner 保留实际候选拒绝异常；仍进行原有回溯搜索，不放宽兼容性、模块启用意图、占用或安装规则。Store 对全模块禁用、包禁用/待删除、程序集占用给出对应恢复提示；无法找到具体拒绝时仍可返回 ManagedDependencyConflict。多候选规划的诊断表示一次实际拒绝，不声称枚举所有冲突。
- 禁用→卸载→重启物理删除后，禁用 ID 仍可从管理列表恢复。恢复本插件 ID 后规划及实际安装可成功，其他插件禁用 ID 保留。已加载程序集冲突继续拒绝并提示重启；不支持热卸载或绕过事务。

### 本轮验证 / Validation

- Phase35 全部框架回归通过；新增恢复条目断言覆盖无发现结果、已卸载 ID、大小写去重、实际发现结果不变、恢复要求重启。
- Managed runtime：.NET 10 和 Mono net472 各 **3,092** 主断言；本修复新增 **10** 条，已有 2,137 条 F3 状态断言仍通过。六个既有启动子进程分别 18/18/18/16/16/20。新增场景使用实际 runtime 完成禁用、卸载、启动恢复、设置恢复、规划及安装；确认不加载被禁用程序集，包只有一份。
- 原 PluginStore runtime：**902** 断言通过；占用回归更新为实际 CandidateAssemblyConflict，保护性拒绝依旧成立。
- 最终完整解决方案构建成功：**0 错误、6 条既有警告**。首次清理构建暴露视图局部 settings 变量缺失，已补齐并重新完整构建成功；不是隐藏首次失败。
- 分发等价检查：31 个必需文件、唯一匹配依赖、无游戏参考 DLL。中英 Core/Store XML 均解析成功、键集合一致、无重复键。可信校验器 14 源码快照一致。git diff --check 通过。

从仓库根目录实际执行（输出记录在 /tmp/phinix-em-*.log）：

```sh
dotnet build Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj -c Release -p:NuGetAudit=false -p:BuildInParallel=false -m:1
dotnet Tests/Phase35RuntimeTests/bin/Release/net10.0/Phase35RuntimeTests.dll
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet clean Phinix.sln --configuration 'Release 1.6' -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
python3 /tmp/phinix-f3-check-artifacts.py
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
git diff --check
git status --short
```

额外执行了 Python XML 解析、重复键及中英键集合比较。PowerShell 未安装，分发使用 F3 的等价 Python 检查；未运行游戏。无需修改持久格式、协议、交易 ACK 或物品所有权；新增 helper 为 Common 的附加方法，部署必须使用完整匹配主包，不能只换 Store DLL。

### 游戏核查 / Game checklist

1. 更新完整 Output/phinix-rework 包。已禁用示例重启后，在设置“扩展”及管理器“模块”页找到其 ID，仍未激活；包页应区分包期望与禁用模块数。
2. 在模块列表恢复对应条目（或已安装包的“模块”菜单）；已安装模块重启后应恢复一次，不改变其他禁用插件。未加载/卸载 ID 恢复后会从纯设置恢复列表消失，这是移除该禁用设置，不代表自动加载。
3. 对本次已卸载的示例：先在扩展列表恢复其保留的禁用 ID，再刷新商店并重装。若卸载尚待完成或该进程仍占用程序集，先重启，再试；应看到具体提示。
4. 再验证禁用→卸载→重启→恢复 ID→重装的完整路径，库存仅一份，仍可卸载，重启后按新意图激活。然后继续原 F3 核查。

English: both UI lists retain saved disabled IDs as recovery settings, without discovering/instantiating modules. Package intent and disabled module counts are separate. Actual candidate rejection codes and actionable recovery hints are preserved. Explicitly restoring only the affected IDs enables reinstall after completed removal; existing transaction and assembly safety gates remain. Implemented and regression-tested; in-game acceptance pending.

## 游戏反馈（2026-10-07）

用户反馈“没问题了”，记录 EM-01/EM-02 本轮游戏核查通过。未提供每步日志，不另行扩大通过范围；F4 按双语计划 4.2 分批。This repair's game check was accepted by the user; no individual-step evidence is invented.

## EM-03：安装红包被第三方 LoadFolders 路径检查阻止（2026-10-07）

状态：IMPLEMENTED — USER MOD RETEST PENDING。用户要求先给修复版；F4-A 暂未实施，已验收 F3/修复的提交也尚未完成。没有新提交或推送。

截图显示 LocalIdentityUncertain / LoadFolders.xml / LocalLoadFolderInvalid。模组 ID 初读为 haiuan.angel；重新核对截图及公开日志，对应 hailuan.angel / Seraphim Viska（黑洞天使）。[公开运行日志](https://gist.github.com/HugsLibRecordKeeper/d0007e5f23b30ab2dec98d656274b7fd) 记录 Seraphim Viska(HaiLuan.Angel)，[作者创意工坊页面](https://steamcommunity.com/workshop/filedetails/?id=3775396880) 确认该第三方模组。公开页面未提供 LoadFolders.xml 源文件，当前工作区也无此模组；不能声称已拿到该文件或逐字复现用户安装。

Phinix 的 ManagedStoreLocalGate 会检查本地模组（含未启用模组）的程序集身份是否与待安装包冲突。此前会直接拒绝路径里的反斜杠以及任意 '.' 路径段，导致普通根目录/相对目录写法也阻止无关插件安装。截图只证明失败在该文件解析/路径检查，无法确定具体是上述路径写法还是原 XML 错误。

本次修复 ResolveLoadFolder：先统一本地路径分隔符，移除无害 '.' 和重复分隔符，再组合/规范化并验证仍在模组根目录内。保持父目录 '..'、盘符、UNC 网络路径、真实程序集冲突、链接、XML DTD、体积/数量限制的拒绝规则；不跳过所有本地扫描，也不改第三方文件、红包插件或安装事务。

新增 LocalLoadFolderTests，21 条断言：10 种安全写法、3 种规范化后仍能发现真实冲突的写法、6 种不安全路径，以及实际 controller 规划/下载/持久安装 2 条。使用隔离测试目录及假 HTTP，不包含用户真实模组或红包载荷。

验证结果：

- .NET 10 / Mono net472 各 3,113 主断言通过；既有启动子进程及 F3 状态回归保持通过。
- PluginStore 原回归 902 条通过。
- 完整 clean + Release 1.6 构建：0 错误、7 条既有警告。
- 分发检查：31 必需文件、每个 runtime 唯一且与构建字节匹配、无游戏 DLL 或主包内旧红包/人才插件；ZIP 完整性及商店 DLL 字节比对通过。
- git diff --check 通过。PowerShell 未安装，用等价 Python 路径/哈希检查；未进行真实 Unity/RimWorld 测试。

实际命令（从仓库根目录执行，日志 /tmp/phinix-loadfolders-*.log）：

```sh
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet clean Phinix.sln --configuration 'Release 1.6' -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
python3 /tmp/phinix-loadfolders-package.py
git diff --check
```

交付完整主包：Output/phinix-rework-local-loadfolders-fix-20261007.zip；SHA256 d14b10ee6af2cd22068cd6e49c26f1558c70b5643c8c9a596bfd8497a25064e2。没有改写固定线上发布资产或玩家设置/存档。主包含此前 F3/管理修复，保留原工作区并行改动。

用户复测：退出游戏→解压更新匹配主包→保留黑洞天使及原模组列表→刷新商店安装红包→重启确认插件激活。若仍为同一文件错误，必须读取用户这份 LoadFolders.xml 或提供对应日志以确定剩余 XML/路径问题；不能据此要求用户卸载第三方模组或宣称它损坏。仍须游戏验证，保留真实冲突/路径越界保护。

English: normalize harmless dot segments and Windows-style relative separators in local LoadFolders paths before containment checks. Actual ownership, assembly collisions, traversal and transaction protections are retained. The screenshot identifies a third-party file veto, but its exact contents were unavailable; the full repair package needs retesting with the user's actual mod list. Both runtime targets passed 3,113 assertions and the complete package was built/verified. No commit/publication or live game validation.

### EM-03 补充日志（2026-10-07）

用户提供完整审计：2026-10-07T11:36:09.7039471Z（新加坡 19:36:09），clientRequestId=3df1a4d6e1824fdba9f647bc9886fed8，source=phinix.official，stage=ManagedStore，event=managed.operation_failed，reason=LocalIdentityUncertain，localMod=hailuan.angel，localFile=LoadFolders.xml，localReason=LocalLoadFolderInvalid。前一条 ManagedInventoryRefreshed 表示库存读取，堆栈进入 Plan。

确认是第三方本地 LoadFolders 检查阻止规划；没有下载或安装事务失败的证据。具体失败内容未记录：路径校验拒绝和 XmlException 都会映射为此相同诊断，所以不能仅据日志断言是反斜杠、点目录、XML 错误或真实越界。也不能用 bytes=0/status=0 推定 HTTP 故障。

已询问日志是否来自修复包更新并重启之后；回答前不把此日志记为修复失败或通过。若为更新后复现，应核对实际加载的 DLL 和原始 XML，再决定下一次修复，不直接放宽所有本地校验。本轮仅补充记录，无代码改动、构建、测试或提交。

English: supplemental audit confirms a local LoadFolders veto during planning. Path rejection and XML parse errors share this diagnostic, so the precise cause and repair outcome remain unproven. Whether the log predates the repair package is pending clarification.

用户随后确认：这份日志来自更新修复包之前。作为原问题证据保留，不代表修复包仍失败；修复后的实际模组复测结果仍待反馈。User confirmed this is a pre-update log, not a post-repair failure.

2026-10-07 最新工作区整包反馈：用户确认“没问题了，继续”。记录 EM-03 及 F4-A 当前批次游戏核查通过；无逐项日志，不扩大为全模组兼容认证。

## EM-04：EasyUpgrades 本地身份读取阻止商店安装（2026-10-07）

状态：定位到检查路径，原始 DLL 待提供。用户明确这份日志来自不含 F4-A 的 LoadFolders 修复版，不归因于 Compose/DI 迁移。

审计时间 2026-10-07T12:21:15.1162808Z，clientRequestId=7c110f7bd7134f28aa8cf570804a8a8f，localMod=mlie.easyupgrades，localFile=EasyUpgrades.dll，localReason=AssemblyMetadataInvalid，reason=LocalIdentityUncertain。

代码路径：ManagedStoreLocalGate.Check 扫描本地 DLL → ManagedExtensionMetadataReader.ReadIdentity → Reader.OpenTables。ReadIdentity 虽只取程序集身份，仍经过插件载荷使用的 PE/CLI/元数据布局限制；任何 ManagedExtensionValidationException 都被本地门禁转换为 LocalIdentityUncertain 并终止规划。真实名称重复走 LegacyModAssemblyConflict，因此当前日志不能证明 EasyUpgrades 与红包存在程序集身份冲突，也不能证明第三方文件损坏。具体触发的字节/限制尚未复现。

后续先用用户实际 DLL 复现，区分普通程序集格式兼容与损坏，再补回归。关联 F4-F2 扫描边界收敛；不得用按模组 ID 白名单或吞掉所有读取失败来绕过真实名称冲突保护。本轮保留 F4-B 并行修改，仅记录和审查代码，未修改运行逻辑、构建或执行测试。

English: the pre-F4-A repair build rejected local EasyUpgrades.dll during identity inspection, not with the actual assembly-name collision code. Identity-only reads currently share strict payload layout checks. The exact rejected bytes require the user's DLL to reproduce; no claim of binary corruption or confirmed runtime conflict is made. Track this with F4-F2 and retain real collision protection.

### EM-04 最小修复实施

用户要求从架构收敛并立即交付最小版本。已删除普通模组磁盘门禁；实际依赖/已加载冲突及托管载荷验证保留。Chat 线程/订阅者缺陷同步修复，对方消息故障未获日志、未复现；用户本机消息正常。验证及交付见 Store-Chat-Minimal-Fix-Handoff.md。游戏验收待反馈；未提交或发布。

English: removed the ordinary-mod disk veto and retained actual runtime/dependency and managed payload checks. Chat callback defects repaired, remote report not reproduced. See the bilingual handoff for validation and pending game acceptance.
