# F3 Store state handoff / 商店状态试点交接

Date / 日期：2026-10-07. Branch / 分支：dev. Accepted F2 commit / 已验收 F2 提交：`58c043e`（37 files，未推送）。F3 未提交，游戏验收待完成。保留已有接口、IDE、生成 fixture、输出目录、其他草稿及中文计划表格格式修改。

## Scope / 范围

- `ManagedStoreOperation` 使用锁定 Stateless 5.20.1。商店拥有依赖；宿主、Common 契约、业务插件不暴露 Stateless 类型。显式 Compile 链接已同步到两个商店回归项目。
- 保持状态枚举名称、顺序、UI 与存储格式。转移仅管理状态；下载、安装、库存读取仍由控制器调用原服务，无进入状态副作用或进度事件转移。
- 同一 controller gate 串行处理开始、取消、完成、停止和快照发布。代次、原 CTS 身份及忙碌状态拒绝重复或过期结果。取消后仍 Busy，任务退出才可重试；取消后忽略进度，失败保留实际诊断。
- 已提交安装/期望状态修改通过临时 `Committed` 完成信息保留真实成功结果。安装事务 journal、持久提交决定、恢复、所有权和校验未改。Installed 仍要求服务返回成功并完成新库存读取；不能由进度满格或发送尝试推定。
- Dispose 先发布 Stopped，再调用取消回调；重复停止无副作用，回调异常记录后不会阻止终态。切换仓库不能把已停止控制器改回 Idle。
- 主包 Common/Assemblies 只有一份 Store 构建产生的 Stateless.dll。新增托管程序集身份和文件别名保护；可信校验器仅更新对应源码快照与摘要，14 文件一致性检查通过。没有重写历史 host-module profile。

## Transition baseline / 状态表

Stable states: Idle, Ready, PlanReady, Verified, Installed, Failed, Canceled. Each may begin the existing operation; original controller eligibility/environment checks remain authoritative.

| Busy state | Permitted successful completion |
| --- | --- |
| Reading | Ready / Idle（无安装包时跳过启动检查） |
| Planning | PlanReady |
| Downloading | Verified |
| Installing | Installed |
| Managing | Ready |

全部忙碌状态允许 Failed / Canceled / Stop→Stopped；CancelRequested 保持忙碌并禁止进度发布。稳定态仓库切换→Idle；忙碌态拒绝仓库重置及重叠开始。Stopped 为终态，仅重复 Stop 无操作。非法成功转移拒绝且不改变状态；过期完成直接忽略。

## Validation / 验证

| Check | Actual result |
| --- | --- |
| Full clean + Release 1.6 build | 0 errors / 7 existing warnings |
| Managed runtime net10 + Mono net472 | each 3,082 assertions; includes 2,137 new Store assertions, plus six existing startup child runs (18/18/18/16/16/20) |
| Existing PluginStore runtime | 902 assertions |
| Phase35 runtime | passed |
| F2 composition: Mono / .NET 10 executing net472 / packaged dependency preload | each 52 assertions |
| Chat regression | 16 scenarios |
| Trusted Validator build | 0 warnings / 0 errors |
| Validator snapshot / admission-publication tests | 6 / 19 tests passed |
| Snapshot source check | 14 files |
| Main/server artifact equivalent check | 31 required files; unique matching runtime bytes; no game reference DLLs or retired plugins |
| Git diff check | passed |

新增回归用实际安装/管理 runtime 和受控适配器屏障，验证忽略取消的适配器、重试、旧进度进入新操作、停止/重建、取消回调及日志异常。事务故障点使用 `transition-written`（持久决定前）及 `commit-decided`（决定后）。`metadata-flushed` 位于提交后完成阶段，不能当作提交前边界；首次测试使用该点的假设已修正，最终两套回归通过。未改事务恢复实现。

### Commands actually run / 实际命令

Run from repository root. `phinix_repo_root` below expands the exact path used in commands; diagnostic outputs were redirected to `/tmp/phinix-f3-*.log`.

```sh
phinix_repo_root=/home/hunyuan2333/Phinix/Phinix-Rework/
# First restore downloaded the official pinned dependency; subsequent restores used the local package cache.
HTTPS_PROXY= HTTP_PROXY= ALL_PROXY= dotnet restore Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --source https://api.nuget.org/v3/index.json -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet restore Phinix.sln --source /home/hunyuan2333/.nuget/packages -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet restore Extensions/PluginStore/Client/PluginStore.Client.csproj --source /home/hunyuan2333/.nuget/packages -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet clean Phinix.sln --configuration 'Release 1.6' -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1

dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet restore Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --source /home/hunyuan2333/.nuget/packages -p:NuGetAudit=false -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
dotnet build Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj -c Release -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet Tests/Phase35RuntimeTests/bin/Release/net10.0/Phase35RuntimeTests.dll

dotnet build Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj -c Release --no-restore -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
dotnet exec --depsfile Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.deps.json --runtimeconfig Tests/ClientCompositionRuntimeTests/runtimeconfig.json Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe --host-dependencies Output/phinix-rework/Common/Assemblies
dotnet build Tests/ChatRegressionTests/ChatRegressionTests.csproj -c Release -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -p:NuGetAudit=false -m:1
MONO_PATH="${phinix_repo_root}Client/Composition/bin/Release/net472" mono Tests/ChatRegressionTests/bin/Release/ChatRegressionTests.exe

dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj -c Release -p:NuGetAudit=false -p:BuildInParallel=false -m:1
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py refresh --source-root .
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -p test_validator_snapshot.py
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -p test_admission_publication.py
python3 /tmp/phinix-f3-check-artifacts.py
git diff --check
git status --short
```

Limitations: `pwsh` unavailable, so the committed PowerShell artifact check was reviewed and equivalent Python path/hash checks executed. The documented single-command `--no-incremental` full build has the pre-existing mixed-project reference-output ordering failure recorded in F2; F3 used explicit clean then build. Game references available under GameDlls (not GameDlls/1.6); compile success is not Unity/in-game acceptance. No live repository publication or game session performed. External reference: [Stateless 5.20.1 package](https://www.nuget.org/packages/Stateless/5.20.1), [official transitions/concurrency documentation](https://github.com/dotnet-state-machine/stateless). Actual selected net462 asset compiled and ran on Mono; net10 harness uses its net10 asset. AssemblyVersion is 4.0.0.0 while NuGet version is 5.20.1; lock hashes identify the package.

## Game acceptance / 游戏测试步骤

1. 在测试环境更新完整 `Output/phinix-rework` 主包（含 Stateless.dll），启动游戏并打开插件商店；检查没有缺失程序集/重复加载报错，刷新及查看插件计划正常。
2. 读取元数据或下载时取消，等待原任务结束；状态应取消且可再次刷新/计划/下载，避免重复操作。旧进度不能跳回来。取消后切换仓库，再刷新，确认显示对应仓库。
3. 让一次请求失败，再恢复网络重试；实际错误保留且重试可用，不能卡在忙碌状态或误报已安装。
4. 用可移除测试插件安装：下载满格时若还在校验/提交，应保持 Installing；确认成功后才 Installed，库存只有一份。启用/禁用后检查状态及重启提示，再重启确认持久结果。
5. 测试环境操作进行中正常退出游戏，再启动并刷新库存；无迟到回调异常、重复安装或错误成功提示。关闭商店窗口本身不等于模块 Shutdown，不据此声称验证 Stopped。

持久提交边界的精确取消/停止时序已由故障点回归验证；不要求在真实游戏插件目录手工注入故障。新 DLL 的 Unity 加载和其他模组程序集冲突仍需本轮游戏确认。协议、存档结构、交易 ACK 与物品所有权未修改。F3 验收通过后再单独提交 dev，随后按 F4 迁移其余插件及旧注册入口弃用政策。

## Acceptance blocker repair follow-up (2026-10-07)

User authorized repairing EM-01/EM-02 before further F3 game checks. Current package includes explicit disabled-setting recovery entries in both management lists, separated package/module intent display and concrete planner rejection/recovery hints. No transaction/activation gates were relaxed. Current managed regression count is 3,092 per .NET 10/Mono, original Store 902 and framework regression pass. Exact repair commands, limitations and game sequence: [extension management record](ExtensionManagement-Known-Issues.md). No commit/push; game acceptance remains pending.
