# F2：客户端装配与 Chat / Client composition and Chat

Date: 2026-10-07. Input: dev / `702981b`; implementation remains uncommitted. Existing interface-file state, F2 preparation, other plans, IDE/output/fixture files were retained. No branch, reset, clean or push.

## 实现 / Delivery

- `Client/Composition/Phinix.ClientComposition.csproj` is the client-owned net472 composition implementation, with exact Autofac 8.4.0 and locked transitive dependencies. It is an SDK project because the classic host does not resolve ordinary NuGet package compile/runtime assets automatically. It references client abstractions; Common/server/plugins do not reference Autofac. The solution adds only this project and existing-configuration mappings.
- `IClientCompositionFactory`, `IClientCompositionBuilder` and `IClientCompositionScope` are additive neutral client contracts. Modules remain parameterless and use ordinary discovery/Register/Activate/Shutdown; ordinary services use constructor injection. Register/Borrow are module-local single instances; constructors do not activate work. Borrowed services are never disposed. Resolve is confined to the composition boundary. Owned resource types must implement synchronous IDisposable; async-only ownership is rejected before construction, because synchronous main-thread teardown must not silently skip DisposeAsync or block awaiting game-thread work.
- The host installs the factory before discovery, stops the ordinary registry first, then releases remaining scopes and removes only its own factory service. Scope/factory methods require the main thread and are terminal after disposal. Protected OnRelease catches each IDisposable exception; a throwing logger cannot interrupt cleanup. Cleanup snapshots outstanding scopes to tolerate one disposer releasing another scope.
- Chat's module assembles/publishes through the same neutral path available to third-party modules. Feed/store/settings/session/users/transport/dispatcher/theme/logger are constructor dependencies. Optional Trade stays a borrowed deferred action. The feed adapter, UI context, message list, user list and notice sidebar explicitly start subscriptions and symmetrically release them, including partial activation failure. Required-dependency Initialize methods and anonymous user-list subscriptions were removed.
- Chat queued mention/legacy notifications reject stopped modules, changed connection generations and different captured game instances. Scope shutdown cancels image requests, clears notices/users and releases cached Unity textures on the main thread. The behavior of messaging, history, unread counts, replies and legacy routing remains covered by the existing Chat scenarios.
- Main package owns Phinix.ClientComposition and eight support DLLs; Memory/Unsafe/Vectors compile references were coordinated with the selected package versions. The artifact checker requires one copy of each and compares bytes with the actual composition build. Managed package manifests protect composition/Autofac/AsyncInterfaces identities and filename aliases. The pinned validator matched its production inputs before the change; only the affected reader snapshot/hash were refreshed from this workspace and verified again.

中文摘要：新增客户端专属装配库和中立接口；Chat 普通服务改为构造注入；注册不启动订阅，激活显式启动，退出成对解绑/取消/释放；官方和第三方模块使用相同入口。主包成套持有运行库，插件不得重复携带。未提交、未推送，也未修改其他并行草稿。

## 版本、分发与影响 / Versions, distribution, impact

- Host/Utils remain 0.9.7.0; abstraction assembly remains 1.8.0.0; new Phinix.ClientComposition is 1.0.0.0. These additive interfaces require the corresponding host build. Chat concrete implementation constructors changed; business API interface signatures did not change. Maintained author/example migration remains F4, with no fixed published assets overwritten.
- Runtime package versions: Autofac 8.4.0; Microsoft.Bcl.AsyncInterfaces 8.0.0; DiagnosticSource 8.0.1; Memory 4.5.5; Unsafe 6.0.0; Buffers 4.5.1; Vectors 4.5.0; Tasks.Extensions 4.5.4. ReferenceAssemblies 1.0.3 is compile-only. No Autofac server dependency or dependency DLL copy into business plugin directories.
- 全量部署或回退 `Output/phinix-rework` 对应构建，包含宿主、Utils、抽象、Chat、新装配库和支持 DLL；不要仅替换 Chat。Existing Unity/mod combinations can load different support assembly versions, so console success is not game acceptance. Rollback to the F1 package must also remove the newly introduced composition/support DLLs from the test installation; source rollback alone does not remove old deployment files.
- No settings/save schema, protocol/capability, server ACK, item ownership/delivery, inventory recovery, installation journal/transaction, gateway access or accepted UI layout changes. The managed-package protected-name rule is additive and rejects duplicate host libraries. Independent index publication has not occurred. The user subsequently reported “F2 pass”; game acceptance is recorded below. The historical host-module profile was inspected: all eight local module DLL digests differ from its published-build evidence, while module IDs/dependencies are unchanged. It was not blindly overwritten with uncommitted local build digests; new release profiles and independent validator deployment belong to the coordinated release/F4 freeze.

## 验证 / Validation

All commands run from the repository root. Properties below use:

```sh
phinix_repo_root=/home/hunyuan2333/Phinix/Phinix-Rework/
```

Pinned dependencies were restored using downloaded official packages; an approved restore wrote the NuGet cache after the sandbox rejected that cache write. Final restore used the local cache and disabled network audit only for these commands:

```sh
dotnet restore Phinix.sln -p:Configuration="Release 1.6" -p:SolutionDir="$phinix_repo_root" -p:NuGetAudit=false -p:BuildInParallel=false -m:1 --source /tmp/phinix-f2-packages
dotnet clean Phinix.sln --configuration "Release 1.6" -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1

dotnet restore Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj --source /tmp/phinix-f2-packages -p:NuGetAudit=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet build Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj --configuration Release --no-restore -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
dotnet exec --depsfile Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.deps.json --runtimeconfig Tests/ClientCompositionRuntimeTests/runtimeconfig.json Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe --host-dependencies Output/phinix-rework/Common/Assemblies

dotnet build Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -p:NuGetAudit=false -m:1
dotnet Tests/Phase35RuntimeTests/bin/Release/net10.0/Phase35RuntimeTests.dll
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -p:NuGetAudit=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe

dotnet build Tests/ChatRegressionTests/ChatRegressionTests.csproj --configuration Release -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -p:NuGetAudit=false -m:1
MONO_PATH="${phinix_repo_root}Client/Composition/bin/Release/net472" mono Tests/ChatRegressionTests/bin/Release/ChatRegressionTests.exe

python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -p test_validator_snapshot.py
python3 /tmp/phinix-f2-check-artifacts.py
git diff --check
```

- Composition regression: 52 assertions each on Mono, .NET 10 and Mono with packaged host support DLLs preloaded. Tests reference the actual client composition project, link the production module runtime and two game-independent Chat services, and exercise ordinary discovered modules. They cover laziness, borrowed ownership, cancellation/subscriptions, disabled discovery, partial constructor/Register/Activate failure, terminal lifecycle, wrong-thread rejection, throwing disposal/logger and reentrant cross-scope cleanup. Original direct-Autofac probes remain comparison evidence; their unguarded-disposal limitation is expected, while production guarded cleanup passes.
- Clean followed by build passed with 0 errors and 7 existing protobuf/obsolete warnings. The earlier single --no-incremental command failed with 28 missing-reference errors after outputs built earlier disappeared; preserve `/tmp/phinix-f2-solution-no-incremental.log`. Splitting clean/build avoids that mixed-project rebuild behavior; no unrelated build-framework rewrite was made. Final incremental rebuild after the async-ownership guard passed before the final artifact check.
- Phase35 passed. Managed runtime passed 943 main assertions plus six startup/registry children on both runtimes. Six new assertions cover protected identities/aliases.
- Chat: all 16 regression scenarios passed under Mono, including actual full Chat graph construction/publication before activation and stopping an unactivated graph. Its old uninitialized-object fixture referred to an F1-removed field; it now uses real passive PhinixFrameworkClient construction. The classic test output needs the actual SDK dependency directory through MONO_PATH. This console test does not start RimWorld, draw UI, connect to a server or certify Unity native operations.
- Validator builds with 0 warnings/errors; source/snapshot check, six snapshot regression tests and 19 admission/publication fixture tests passed. Fixture publication uses mocked local records, not a real index write. Packaging validation is an equivalent read-only Python inspection because pwsh is unavailable; the PowerShell command was not run. It checks 30 required files, load folders/languages, absence of game/retired-plugin DLLs, and nine unique runtime assets matching real build bytes.
- Game references are legally available in existing GameDlls/, not GameDlls/1.6. No game files were committed. Geometry/LegacyTrade tests were not run because layout and trade/recovery paths were unchanged. Player settings/saves were not edited.
- Local evidence: `/tmp/phinix-f2-*-build.log`, `*-restore.log`, `*-mono.log`, `*-dotnet.log`, `chat-tests.log`, `phase35.log`, `validator-tests.log`; generated outputs/logs are excluded from commits.

## 游戏测试与下一步 / Human acceptance and next slice

1. 备份测试配置与两个测试存档；关闭游戏，部署本批完整主包，启动 RimWorld 1.6。确认 Chat、Store、已安装示例正常，日志没有新增程序集缺失/装配失败。
2. 发/收一条普通聊天，检查未读、屏蔽、回复/提及；断开重连再发一条，消息和通知都只出现一次。可用普通图片检查队列，无需真实物品交易。
3. 返回主菜单进入另一测试存档，确认没有旧存档延迟通知；正在加载图片时断开/退出，确认没有重复回调或销毁异常。正常退出游戏。
4. 在测试配置禁用 Chat 后重启，确认没有 Chat 页签/激活；重新启用并重启，确认聊天恢复。保持现有“重启后生效”策略。

User acceptance update (2026-10-07): the user reported “F2 pass”. Record F2 game acceptance as passed without inventing individually confirmed steps or clean-log evidence. F2 remains uncommitted on dev/702981b. Next: commit the F2 source/tests/lock files/docs as one batch on dev, excluding other drafts/generated output, then proceed to the F3 store-operation transition baseline and Stateless pilot. This acceptance-record turn does not repeat tests, commit or push.
