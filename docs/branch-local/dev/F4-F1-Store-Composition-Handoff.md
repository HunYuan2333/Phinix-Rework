# F4-F1 Store provider composition / 商店提供者组合

## Status and scope / 状态与范围

The user accepts E2 and authorizes Store migration. F4-F is split: this first batch migrates the three public providers; the next F4-F activation batch owns controller/localization/icons and view factories. Do not confuse that next batch with the already planned F4-F2 local-mod boundary work. F4-F is not complete yet. Game acceptance of this candidate remains pending. No commit/push, preserve parallel changes.

用户接受 E2 并授权商店迁移。F4-F 拆步：本批迁移三个公开提供者，下一批迁移激活时的控制器/本地化/图标和视图工厂。下一批不能与既有 F4-F2 本地模组边界工作混淆；整个 F4-F 尚未完成。本候选游戏验收待完成，未提交/推送，保留并行修改。

Store now uses the ordinary `ClientExtensionModule.Compose` path. Its scope owns settings panel, main tab and update banner; public constructor declarations allow ordinary scope resolution. Interface/concrete aliases resolve to the same provider. The module constructor no longer constructs UI providers. Three API IDs/types/order and passive presentation remain the same. Providers implement idempotent Dispose/Stop; no controller/view singleton is introduced.

商店改用普通 `ClientExtensionModule.Compose`，作用域持有设置面板、主标签和更新提示。提供者显式公开构造函数，接口/具体类型共享同一实例；模块构造时不再创建 UI 提供者。三个 API 的身份/类型/顺序及未激活显示不变，提供者通过幂等 Dispose/Stop 清理；未将控制器/视图改为共享单实例。

Shutdown attempts all notice/provider/controller/localizer/icon/scope releases independently and then reports collected failures. Panel and release-notice references are cleared before scheduling window close; a dispatcher failure cannot retain/repeat the close request or prevent remaining releases. Scope release uses the existing reporter. Queued Close behavior remains the existing main-thread pattern; a failed dispatcher cannot be forced to close the window.

停止分别尝试提示、提供者、控制器、本地化、图标及作用域释放，再汇总失败。面板/发布提示先清空引用再排队关窗；dispatcher 失败不会残留引用、重复该请求或跳过后续释放。作用域沿用原报告机制。关窗仍走原主线程模式；dispatcher 真正失败时不能保证窗口关闭。

The complete `Activate` method is byte-for-byte unchanged relative to this batch's saved source (text comparison). Download state machine, environment recapture, controller/view construction, per-window view identity, repository access, installation transaction/recovery, dependency policy, failure diagnostics and persistent/protocol formats are unchanged. Activating twice still follows the existing teardown/rebuild path; activation lifetime migration is next.

整个 Activate 与本批开始前保存源码完全一致（文本比较）。下载状态机、环境复查、控制器/视图构造、各窗口视图独立性、仓库访问、安装事务/恢复、依赖策略、错误诊断及持久化/协议格式不变。重复激活仍沿原清理/重建路径，激活生命周期迁移留到下一批。

## Validation / 验证

- Actual baseline saved at `/tmp/phinix-store-f4f1-before/PluginStore.Client.dll`. An isolated probe directory contains that DLL and the same latest test executable; loaded DLL path is asserted. .NET 10 and Mono each pass 6 baseline / 38 candidate assertions. API mappings, panel section/order, tab order, passive visibility and banner height facts match.
- Candidate: constructor creates no providers, singleton aliases, missing factory, partial resolution/publication, repeated stop, ordinary registry discovery, disabled zero-instance and registration rollback. Injected close-queue and localizer-dispose failures prove controller/scope release continues and references are cleared; one attempted Close scheduling only.
- PluginStoreRuntimeTests: 946 assertions. ManagedExtensionRuntimeTests: 3212 assertions on each runtime, including 2224 store-operation assertions each. These exercise unchanged download/manage/install/recovery flows; they do not test game UI activation.
- Full solution: 0 errors, 9 warnings. Probe build with restore: 0 errors, 13 NuGet vulnerability lookup warnings. Store harness build: 0 errors/0 warnings; managed harness: 0 errors/7 existing NuGet warnings. Validator snapshot: 14 sources unchanged. Distribution: 31 required files, unique runtime dependencies, no game/Unity references, compiled host/Utils/Store/Inventory/Trade bytes match. Diff check passes (existing CRLF notices).

中文：同一探针在独立旧 DLL 目录运行并断言实际加载路径；两种运行时均通过基线 6/候选 38 项断言，API 与未激活显示事实一致。覆盖构造被动性、别名单实例、部分失败、重复停止、普通发现/禁用/回滚和两处停止异常。商店领域回归 946 项，管理安装回归两种运行时各 3212 项（其中商店操作各 2224 项）。整包 0 错误/9 警告；还原后探针编译 0 错误/13 警告。Validator 14 源文件一致，发行包 31 项检查通过。

### Limits / 限制

No native game rendering, actual tab/settings activation, localization/theme/image resource load, sockets or live repository/install operation was exercised by the new probe. It uses an uninitialized Window only to test failing close scheduling without invoking Close/native game code. Domain harnesses use fixtures. In-flight installs remain governed by the existing durable commit/recovery rules; disposal is not a rollback of committed installation. Manual startup/window/cancellation/recovery checks remain necessary.

新增探针未执行原生绘制、实际标签/设置激活、本地化/主题/图像资源加载、真实网络或在线安装。未初始化 Window 只用于检查关窗排队失败，不调用 Close/原生代码。领域 harness 使用夹具。执行中的安装仍按原持久化提交/恢复规则处理，Dispose 不等于撤回已提交安装；启动、窗口、取消、恢复仍需游戏核查。

## Commands run / 已执行命令

```sh
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/StoreCompositionRuntimeTests/StoreCompositionRuntimeTests.csproj -c Release -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/StoreCompositionRuntimeTests/runtimeconfig.json Tests/StoreCompositionRuntimeTests/bin/Release/net472/StoreCompositionRuntimeTests.exe
mono Tests/StoreCompositionRuntimeTests/bin/Release/net472/StoreCompositionRuntimeTests.exe
dotnet exec --runtimeconfig Tests/StoreCompositionRuntimeTests/runtimeconfig.json /tmp/phinix-store-f4f1-before/probe/StoreCompositionRuntimeTests.exe --legacy
mono /tmp/phinix-store-f4f1-before/probe/StoreCompositionRuntimeTests.exe --legacy
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
python3 /tmp/phinix-f4f1-package.py
git diff --check
```

Isolated baseline preparation copied the probe output directory then substituted only the saved Store DLL. The initial shortcut `dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore` failed during build without a detailed diagnostic; explicit build with SolutionDir and direct DLL execution above succeeded. PowerShell unavailable; equivalent Python artifact validation used. Logs: `/tmp/phinix-f4f1-*`.

旧版隔离目录复制探针输出后仅替换保存的 Store DLL。最初快捷 run 命令在编译阶段失败、没有详细诊断；上述显式 SolutionDir 编译和直接运行 DLL 成功。PowerShell 不可用，使用等价 Python 发行检查。

## Package and game steps / 包与游戏步骤

ZIP: `/tmp/phinix-rework-f4f1-store-di-20261008.zip`
SHA-256: `30db12b48ce4d465f02133ec54bb190c0418299a17701803584f6b8271861f30`

1. Restart/log in, open Store tab and settings browser; both retain their normal presentation and navigation. Closing/reopening the standalone browser should work.
2. Browse/refresh and switch repository access method. Select an official test plugin, download/install, cancel/retry once and restart to check inventory/management state.
3. Disable/re-enable and uninstall/reinstall that plugin through normal management; verify friendly errors and diagnostics remain intact.
4. Open standalone browser, leave/stop the session, restart and reopen. Check no duplicate release notice/update banner or lingering stopped window. Preserve installation journals if recovery is requested; never delete them to bypass it.

1. 重启登录，分别打开商店标签和设置浏览窗口，核对原显示/导航；独立窗口关闭重开正常。
2. 浏览刷新/切换访问方式，选择官方测试插件下载安装，取消重试一次并重启核对管理状态。
3. 正常管理流程禁用/恢复、卸载/重装测试插件，核对友好提示和日志。
4. 打开独立窗口后退出/停止、重启再开，检查无重复发布提示/更新条、无残留停止窗口。若要求恢复，保留安装 journal，不能删除绕过。

Next F4-F batch: activation-owned controller/localizer/icons and per-window view factory, preserving F3 transitions and original transaction service. This is separate from existing F4-F2 local environment/whole-mod boundary follow-up.

下一 F4-F 批：激活作用域持有控制器/本地化/图标和各窗口视图工厂，保留 F3 状态流与原事务服务；与既有 F4-F2 本地环境/整模组边界跟进分开。
