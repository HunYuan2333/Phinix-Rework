# F4-A Client author entry / 客户端作者入口

Date: 2026-10-07. Branch: dev. Accepted baseline: `1be33dd` (F3 + extension management repair), no push. F4-A is implemented but uncommitted; game check pending. Separate pending EM-03 path normalization and parallel edits are preserved.

## Delivered / 交付范围

- ClientExtensionAbstractions introduces `IClientExtensionModule` and `ClientExtensionModule`. New authors derive from the base and override `Compose(IExtensionBuilder)`; passive constructors and optional `IActivatablePhinixExtensionModule` Activate/Shutdown remain unchanged. Scope ownership is still the module's responsibility.
- The base explicitly implements the shared Register entry, checks the host composition factory, then forwards once to Compose. The shared registry, dependency graph, disabled-before-construction, rollback and stop paths are unchanged. No client implementation dependency or endpoint networking is added to Common; no separate plugin discovery pipeline exists.
- Chat now derives from the new base and overrides Compose; its previously accepted DI graph and business behavior are retained. Existing direct Register modules continue to work; server Register is unchanged. Examples migrate in F4-B.
- Client API compatibility, CLR and file version increase from 1.8 to 1.9. Managed reference selection allows existing 1.8 requirements on the additive 1.9 host, and rejects 1.9 requirements on 1.8. New authors must target matching contracts and declare minimum abstractions 1.9 when packaged. Deploy/rollback complete matching packages.
- Static metadata inspection recognizes the known client interface/base in ClientExtensionAbstractions. Arbitrary foreign external base classes remain unsupported. Validation snapshots refresh only this reader and its recorded hash; existing release profiles/assets are not rewritten or published.

## Deprecation proposal / 弃用版本方案

F4-A establishes the author entry; it does not mark shared server/client Register Obsolete. F4-H applies deprecation to the distinct old client author adapter/path, proposes the coordinated post-F4 host 0.9.8 release for deprecation, and host 1.0/client abstractions 2.0 for removal after migration, rollback and game acceptance gates. These are release proposals, not newly published versions or frozen commitments. Common's internal bridge/server contract is distinct from continuing to support old client author modules. Freeze actual release versions in F4-H and explicitly list unfinished independent repository migrations.

## Validation / 验证结果

| Check | Result |
| --- | --- |
| Composition net472 binary on Mono / .NET 10 / packaged dependency preload | each 60 assertions |
| Managed runtime .NET 10 / Mono | each 3,115 main assertions, plus existing startup children 18/18/18/16/16/20 |
| Chat | 16 scenarios |
| Phase35 framework | passed |
| Full clean + Release 1.6 client/server build | 0 errors, 7 existing warnings |
| Trusted Validator build | 0 warnings/errors |
| Validator snapshot / admission-publication tests | 6 / 19 passed |
| Source snapshot check | 14 files |
| Artifact/ZIP verification | 31 required assets; unique matching runtime bytes; no game DLLs; ZIP readable and packaged Store matches build |
| git diff --check | passed |

Production composition tests now use the new author base for healthy, disabled, registration-failure and activation-failure modules, preserving their previous cleanup assertions. New compiled net472 DLL fixtures prove static base recognition without Compose execution, foreign-base rejection, new/old registration exactly once, shared idempotent stop, rejection before Compose on an unprepared host, and old/server registration without a client factory. CLR host-reference assertions cover 1.8→1.9 compatibility and 1.9→1.8 rejection. The metadata reader intentionally accepts DLLs, so tests use a real separate DLL rather than treating the test EXE as a plugin.

Managed totals include the separate pending EM-03 path regression (21 assertions). This does not certify user-mod reproduction. The old raw Autofac probe still prints its known unguarded-disposal limitation; production guarded releases pass their cleanup tests. PowerShell is unavailable; Python executed equivalent committed artifact checks. No Unity/game session, live index publication or F4-B migration was performed.

## Exact commands / 实际命令

From repository root; diagnostic logs were redirected to `/tmp/phinix-f4a-*.log`.

```sh
phinix_repo_root=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet restore Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj --source /home/hunyuan2333/.nuget/packages -p:NuGetAudit=false -p:RestoreLockedMode=false -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet build Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj -c Release --no-restore -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
dotnet exec --depsfile Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.deps.json --runtimeconfig Tests/ClientCompositionRuntimeTests/runtimeconfig.json Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe --host-dependencies Output/phinix-rework/Common/Assemblies
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet clean Phinix.sln --configuration 'Release 1.6' -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
dotnet build Tests/ChatRegressionTests/ChatRegressionTests.csproj -c Release -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -p:NuGetAudit=false -m:1
MONO_PATH="${phinix_repo_root}Client/Composition/bin/Release/net472" mono Tests/ChatRegressionTests/bin/Release/ChatRegressionTests.exe
dotnet build Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj -c Release -p:NuGetAudit=false -p:BuildInParallel=false -m:1
dotnet Tests/Phase35RuntimeTests/bin/Release/net10.0/Phase35RuntimeTests.dll
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj -c Release -p:NuGetAudit=false -p:BuildInParallel=false -m:1
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py refresh --source-root .
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -p test_validator_snapshot.py
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -p test_admission_publication.py
python3 /tmp/phinix-f4a-package.py
git diff --check
git status --short
```

Lock update was required only to add the new fixture project, not change runtime package pins. Subsequent builds used --no-restore with the actual resolved graph. Full clean+build uses available GameDlls root references; it retains the previously documented mixed-project --no-incremental limitation.

## Package and game steps / 包与游戏步骤

`Output/phinix-rework-f4a-author-entry-20261007.zip`, SHA256 `90f679182f309ef6c4139a5e8776c5e2aaf3877873eeef3b95c31cae41835b4f`. This package includes the pending local-path compatibility fix; the earlier EM-03 ZIP remains separate. No old fixed online release asset was overwritten.

1. Exit the game and update the whole matching package (contracts 1.9, Utils, composition, Chat and runtime dependencies). Start and confirm no missing type/assembly or registration/cleanup error.
2. Send/receive chat, disconnect/reconnect and repeat; no duplicate messages/notifications. Open Chat panels/settings as before.
3. Disable Chat and restart: it stays unconstructed/inactive; restore and restart: exactly one active Chat. Existing example/Inventory/Trade/Store/Legacy modules still use their previous entry and should retain their existing behavior.
4. Normal quit/restart; check no cleanup errors. Retest red-packet installation against the unchanged actual third-party mod list to verify the pending EM-03 fix separately.

After acceptance, commit F4-A separately on dev, then begin F4-B example migration. Local-check scope changes belong in F4-F2: prioritize actual loaded identities/effective directories and distinguish proven collision from inability to inspect, with an explicit policy and its own regression/game batch. No persistence/protocol/trade ACK/item-ownership changes in F4-A.

## User acceptance (2026-10-07)

The user compiled the current workspace package and reported “没问题了，继续”. F4-A game check accepted; no invented individual-step logs. Next: separate commits for the accepted EM-03 and F4-A slices, then F4-B sample migration.

Accepted F4-A commit: b339cb6; EM-03 commit: c0c8d4e. No push. F4-B is the next separate sample batch.
