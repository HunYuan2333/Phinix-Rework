# F4-B sample migration / 示例迁移

Date: 2026-10-07. Branch dev. Accepted commits: EM-03 `c0c8d4e`, F4-A `b339cb6`; neither pushed. User reported the current compiled package works; no individual-step logs invented. F4-B is implemented, uncommitted and awaiting its own game check. Parallel edits and historical sample releases/Package trees are preserved.

## Delivery

Example candidate 1.0.3 and Playtest candidate 1.4.0 derive from ClientExtensionModule and override Compose. Each owns a scope, borrows host settings/log, resolves passive ordinary services, publishes the same public tab/settings APIs, starts localization only in Activate and disposes scope-owned services in Shutdown. Required settings are constructor dependencies. Public service constructors are used for DI; the module has no UI/service allocations in its constructor.

ExampleState and PlaytestTab provide synchronous IDisposable cleanup. They invalidate callback generations and clear active references before external localization cleanup. Localization remains acquired during Activate because the host binds it only during activation; normal stop detaches language handlers and releases the module localizer. Settings IDs, counter/hint semantics, stale callback checks and Playtest map/silver behavior remain unchanged. No owned game objects, save-format changes or trade behavior changes.

Packager adds optional --abstractions-range, retaining its historical default for other callers. These two source/package recipes explicitly use >=1.9.0 <2.0.0. Both manifests and CLR references target the new host contract. Packaging is through the existing public tool, not a sample-only loading path. No index metadata, fixed assets or live releases were overwritten/published; Playtest stays a developer fixture.

## Evidence

- Both actual sample net472 projects build: 0 errors, 2 existing protobuf SDK warnings each. The packager builds (3 existing warnings in first graph build); no new runtime package dependencies.
- Public tool checks actual plugin metadata, CLR references, localization and produces each managed ZIP with four exact declared files (DLL + two JSON + manifest). Python verifies every declared SHA256, minimum contract range, ZIP integrity and absence of host/game DLLs.
- Mono checks both actual bundled DLLs using the real registry, ClientCompositionFactory and ClientLocalizationService: new Compose entry, passive-before-Activate, English/Chinese switch, fallback, retained settings, idempotent shutdown/closed localizer, disabled no-module/no-tab and missing-settings no-partial-publication.
- Separate fresh Mono processes check both actual managed ZIPs with production installation runtime: install exactly one owned package without loading candidate DLL, removal intent, startup recovery, reinstall. Temp data only; no game/player inventory touched.
- Existing composition binary rerun: 60 assertions. Existing managed .NET 10 binary rerun: 3,115 main assertions + six startup children. These are explicitly reruns, not newly rebuilt full solution/harness graphs in F4-B.
- Trusted snapshot source check: 14 files. git diff --check passes. No geometry/helper changes; UI drawing and silver actions are not invoked by console checks and require game testing.
- No full solution build needed for this sample/tool-only slice; Phinix.sln does not include these sample projects. Host baseline remains the accepted F4-A full build. No remote publication or game session performed.

## Actual commands

Repository root used throughout. Diagnostic outputs: /tmp/phinix-f4b-*.log.

```sh
phinix_repo_root=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet build Extensions/PluginStore/Samples/Example/Example.csproj -c Release -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj -c Release -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj -c Release -p:NuGetAudit=false -p:BuildInParallel=false -m:1
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
python3 /tmp/phinix-f4b-pack.py
dotnet build Extensions/PluginStore/Samples/Playtest/tests/RegistryCheck.csproj -c Release -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
MONO_PATH="/usr/lib/mono/4.5:${phinix_repo_root}GameDlls/" mono Extensions/PluginStore/Samples/Playtest/tests/bin/Release/net472/RegistryCheck.exe Output/phinix-f4b-example-1.0.3-compose-bundle Phinix.Example.Basic
MONO_PATH="/usr/lib/mono/4.5:${phinix_repo_root}GameDlls/" mono Extensions/PluginStore/Samples/Playtest/tests/bin/Release/net472/RegistryCheck.exe Output/phinix-f4b-playtest-1.4.0-compose-bundle Phinix.Store.Playtest
MONO_PATH="/usr/lib/mono/4.5:${phinix_repo_root}GameDlls/" mono Extensions/PluginStore/Samples/Playtest/tests/bin/Release/net472/RegistryCheck.exe --managed Output/phinix-f4b-example-1.0.3-compose.zip GameDlls
MONO_PATH="/usr/lib/mono/4.5:${phinix_repo_root}GameDlls/" mono Extensions/PluginStore/Samples/Playtest/tests/bin/Release/net472/RegistryCheck.exe --managed Output/phinix-f4b-playtest-1.4.0-compose.zip GameDlls
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
git diff --check
git status --short
```

Packaging script invokes ManagedPackageTool once per sample with --assembly, --package-id, --name, --version, --abstractions-range, --output, --bundle-output, both --language-file paths and seven --host-assembly identities: Mono mscorlib, four GameDlls references, Utils and client abstractions. Mono's BCL is used for compile/reference validation and given priority in MONO_PATH. Game references never enter the samples. First output paths were prior to changelog updates; final paths below were generated afresh, not overwritten.

## Candidate artifacts

- Output/phinix-f4b-example-1.0.3-compose.zip: SHA256 8542b1d2d0d267565728f2014d2da5c17bdcba8712f89503d68f91260add2603.
- Output/phinix-f4b-playtest-1.4.0-compose.zip: SHA256 3d32d3f5a7d35bb8cfa599c90fc7673a3e615b88396a559067e71be8496bbad2.
- Output/phinix-f4b-samples-local-test-20261007.zip: eight exact files (two DLLs, two companion declarations, four language JSON) under Common/Extensions/{package-id}; SHA256 9a5a5a41b9ba17bf761c7c492e5209b5c8833ab3715e8c78fb55342544a86d0f. Local preview only, not a full host distribution or store publication.

## 游戏核查

1. 使用已通过的 F4-A 主包（客户端抽象 1.9）。若已从商店安装示例，先通过扩展管理卸载并重启，确认删除完成；设置保留。避免同时存在同一插件的托管版本与本地测试版本。
2. 退出游戏，把本地测试 ZIP 解压到 Phinix 主模组根目录，形成 Common/Extensions/phinix.example.basic 与 phinix.poc.playtest 两个目录。原主包其余文件保持原样。启动确认两个 tab、示例设置区和资源均正常。
3. 示例计数/提示/重置取消和确认、切中英语言、重启检查原设置保留。Playtest 检查计数及提示；银币操作仅在测试存档自愿验证，不由控制台测试宣称已通过。
4. 分别禁用→重启→恢复→重启，检查无实例化、重复订阅或残留回调；正常退出无清理报错。
5. 测试结束退出游戏，只移除这两个本地测试目录。它们不是托管库存包，不能用包列表卸载；模块禁用入口仍可用。真实远端安装/卸载/重装待新候选按正式流程发布后核查；自动回归已覆盖实际 ZIP 的本地事务路径，不能冒充远端游戏验收。

游戏核查通过后单独提交 F4-B dev，再进入 F4-C Inventory 装配。发布候选需要另行明确授权；不为了游戏测试改写固定 Release/Index。F4-F2 本地模组检查收窄保持原顺序。
