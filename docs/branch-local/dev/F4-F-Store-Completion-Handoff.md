# F4-F Store DI completion / Store DI 迁移完成

## Status / 状态

User requested completing Store before one combined game test. F4-F provider and activation composition are implemented; combined game acceptance remains pending. No commit/push; preserve all parallel changes. F4-G LegacyAdapter is next after Store acceptance. Existing F4-F2 ownership/whole-mod boundary follow-up retains its separate scope.

用户要求 Store 完成后合并游戏测试。F4-F 提供者与激活组合迁移实现完成，合并游戏验收待完成。未提交/推送，保留并行修改。商店验收后进入 F4-G LegacyAdapter；既有 F4-F2 所有权/整模组边界跟进仍为独立工作。

## Ownership / 所有权

The provider scope owns the three published providers. The activation scope borrows endpoint abstractions and providers and owns diagnostics, localizer lease, controller lease, lazy badge resources, view factory and release-notice lease. Acquired module localizers are released once; the host localization/environment/management/installation services are never disposed. Controller construction and fresh environment callback delegate to the original implementation. No controller/state-machine/installation algorithm or persistent/protocol format was changed in this batch.

提供者作用域持有三个公开提供者；激活作用域借用端点抽象和提供者，持有诊断、本地化租约、控制器租约、按需图标、视图工厂和发布提示租约。取得的模块本地化对象仅释放一次，主机本地化/环境/管理/安装服务不释放。控制器构造和新环境捕获委托使用原实现，本批未改控制器/状态机/安装算法或持久化/协议格式。

The scoped factory creates distinct tab/window views sharing one activation controller/localizer/icon set. View search, selection, scrolling and install intent remain independent. The factory tracks weak references so closed windows are not retained until shutdown. Disposal invalidates live views before controller/resource release and rejects subsequent use of captured old factories; stopped views return before Unity drawing/installation intent handling. Lazy texture construction still belongs to main-thread Draw, and acquired textures keep the original dispatcher destruction path; empty icon disposal now returns without entering Unity.

作用域工厂为标签/各窗口创建独立视图，共享同一组控制器/本地化/图标；搜索、选择、滚动、安装意图仍独立。工厂用弱引用跟踪，避免关闭的窗口视图一直保留到退出。释放先使现存视图失效，再释放控制器/资源；旧工厂拒绝新建视图，停止视图在 Unity 绘制/安装意图处理前返回。纹理仍在主线程绘制时按需创建，取得的纹理沿原 dispatcher 销毁路径；空图标释放不再进入 Unity。

Repeated Activate preserves the previous teardown/rebuild semantics while retaining published provider identity. Activation failures roll back both scopes. Notices stop before queued show work; their leases close owned windows through the dispatcher. Cleanup continues after individual failures and reports them through the existing composition reporter. Stop still cancels the original controller and does not roll back a committed install or bypass journal recovery.

重复 Activate 保留原清理/重建语义，公开提供者身份不变。激活失败回滚两个作用域；提示先停止再使排队显示失效，租约通过 dispatcher 关闭自己的窗口。单项失败后继续清理并通过既有组合报告机制报告。停止仍取消原控制器，不撤销已提交安装、不绕过 journal 恢复。

## Validation / 验证

- Real prior F4-F1 DLL saved before edits, run in an isolated probe directory with loaded-path assertion. .NET 10 and Mono: 16 baseline / 93 candidate assertions; three facts match: APIs/passive presentation and actual startup capture/empty-inventory refresh/independent views.
- Candidate runs actual Activate with managed fake endpoint services: same controller/resource sharing but independent search, reactivation disposal and stable provider identity, captured factory rejection, stopped views, cancelled queued notices, missing dependency/localizer acquisition/view construction/environment capture failures, localizer disposal error, repeated shutdown and no borrowed-service disposal.
- Original store regression: 946 assertions. Managed runtime on each platform: 3212 assertions, including 2224 store operations. Validator snapshot: all 14 sources unchanged. Full solution: 0 errors, 6 warnings in the final synchronized build (earlier full build 9 warnings); narrow probe: 0 errors/5 NuGet vulnerability lookup warnings. Distribution: 31 required files, unique runtime bytes, no game/Unity references, exact compiled host/Utils/Store/Inventory/Trade bytes. Final probes were rerun with the synchronized composition/Store DLLs used by the package.
- `git diff --check` passes with existing CRLF notices. New activation source is explicitly listed in the client csproj. No generated artifacts or local game DLLs committed.

中文：实际旧 DLL 隔离运行并断言加载路径，两种运行时基线 16/候选 93 项断言，API、未激活显示和实际启动捕获/空库存刷新/独立视图事实一致。候选覆盖资源共享、搜索独立、重激活释放、提供者身份、旧工厂拒绝、视图停止、提示取消、依赖/本地化/视图/环境失败及释放异常。原商店 946 项、管理两种运行时各 3212 项（含商店操作 2224 项）通过，Validator 14 源一致。最终整包 0 错误/6 警告（之前全编译 9 警告），窄探针 0 错误/5 警告，发行包 31 项检查通过，最终使用发行包相同 DLL 重跑候选探针。

### Limits / 限制

Old empty-icons Dispose unconditionally enters Verse.UnityData and cannot run against compile-only game DLLs (native Unity initialization exception). The baseline fixture removes only that empty icon reference before teardown: startup/resource-sharing facts are compared, but old icon teardown is not certified. Candidate empty-icon cleanup is tested; actual texture load/destruction, translations/windows, real network installation and game teardown remain manual. Managed empty-inventory startup intentionally skips network. Fixture results do not prove real install durability or force a failing foreign dispatcher/localizer to release successfully.

旧版空图标 Dispose 无条件进入 Verse.UnityData，在编译用游戏程序集下会抛原生 Unity 初始化异常。基线夹具在停止前仅排除这个空图标引用：可对照启动/共享事实，不能证明旧图标释放。候选空图标清理已验证；真实纹理加载/销毁、翻译/窗口、在线安装和退出仍需游戏核查。空库存启动按原规则跳过网络。夹具不能证明实际安装持久化，也不能强制失败的外部 dispatcher/localizer 成功释放。

## Commands / 命令

```sh
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/StoreCompositionRuntimeTests/StoreCompositionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/StoreCompositionRuntimeTests/runtimeconfig.json Tests/StoreCompositionRuntimeTests/bin/Release/net472/StoreCompositionRuntimeTests.exe
mono Tests/StoreCompositionRuntimeTests/bin/Release/net472/StoreCompositionRuntimeTests.exe
dotnet exec --runtimeconfig Tests/StoreCompositionRuntimeTests/runtimeconfig.json /tmp/phinix-store-f4f-complete-before/probe/StoreCompositionRuntimeTests.exe --legacy
mono /tmp/phinix-store-f4f-complete-before/probe/StoreCompositionRuntimeTests.exe --legacy
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
python3 /tmp/phinix-f4f-complete-package.py
git diff --check
```

Baseline setup copies the latest probe directory and substitutes only the saved Store DLL. One initial artifact check detected composition DLL drift after narrow builds; a final full build synchronized artifacts, and the check then passed. PowerShell unavailable; Python performs equivalent artifact checks. Logs: `/tmp/phinix-f4f-complete-*`.

基线复制最新探针目录，仅替换保存的旧 Store DLL。首次发行检查发现窄编译后的组合 DLL 与发行目录不同，最终全编译同步后检查通过。PowerShell 不可用，Python 执行等价发行检查。

## Combined game test / 合并游戏核查

ZIP: `/tmp/phinix-rework-f4f-store-di-complete-20261008.zip`
SHA-256: `d882428e5663e966d16904bda53327e6ecc4093d1bad3ae2962cc923d3eeb20b`

1. Restart and open Store tab plus settings browser. Verify badges/translations/update notice. Give the two views different searches/selections; neither should overwrite the other.
2. Refresh/switch GitHub/Cloudflare; download a test official plugin. Cancel/retry and install, then restart and check its management state.
3. Disable/re-enable, uninstall/reinstall through normal management. Keep existing recovery journals if requested; do not delete them to bypass a fault.
4. Close/reopen independent windows, leave the session while background work is pending and restart. Check no stale window actions, duplicate notices or disposed-resource errors.

1. 重启打开商店标签和设置浏览窗口，核对徽标/翻译/更新提示；两边输入不同搜索、选不同条目，不能相互覆盖。
2. 刷新/切换 GitHub 与 CF，下载官方测试插件，取消重试后安装，重启核对管理状态。
3. 正常禁用/恢复、卸载/重装；若有恢复提示保留 journal，不删除绕过。
4. 独立窗口关闭重开，后台操作中退出再重启，检查无旧窗口操作、重复提示、已释放资源异常。

F4-F composition implementation complete; combined game acceptance remains the gate before F4-G.

F4-F 组合迁移实现完成，合并游戏验收后进入 F4-G。

## User acceptance / 用户验收（2026-10-08）

The user reported “测试通过” for the combined Store batch and then requested finishing F4. F4-F game acceptance is recorded; no additional individual test logs were supplied. Next batch: F4-G LegacyAdapter.

用户报告合并商店批次“测试通过”，并要求优先完成 F4。记录 F4-F 游戏验收通过，未提供额外逐项测试日志；下一批为 F4-G LegacyAdapter。
