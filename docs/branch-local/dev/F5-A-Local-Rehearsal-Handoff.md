# F5-A Local core-source rehearsal / 本地共享核心演练

2026-10-08. User authorized pushing dev and continuing F5, with independent rehearsal repositories kept local. `origin/dev` was successfully advanced to `328c556cd80949dc76d7252039ec8f0eeb5eff1b`; no new GitHub repository was created. F5-A passes; full F5 is not complete.

用户授权推送 dev 并继续 F5，本地试验仓库不发布 GitHub。dev 推送成功至 328c556；没有创建新 GitHub 仓库。本批 F5-A 验证通过，整个 F5 尚未完成。

## Repositories and input / 仓库与输入

Lab: `/tmp/phinix-f5-local-20261008-s085jpgv`

| Repository | Pinned local commit | Scope |
| --- | --- | --- |
| Shared | `3b22110010267393e4f7cb437084aa36adde66db` | Common core, libs, build rules, nested vendor gitlink |
| ClientProbe | `9898c8856726999d14abafb3b930815b428252cd` | net472 minimal consumer, original NetClient source and assembly attributes |
| ServerProbe | `3b93973fad12117dd8606430828f1c68c1fc7828` | net10 minimal consumer, original NetServer source and assembly attributes |

Both consumers pin the same Shared commit at `Dependencies/Phinix.Common`. Nested protobuf remains exactly `4b0c3aacf0657fbf38253b38918d3358dd4319ec`, acquired from an independent local bare mirror copied without hardlinks from the existing vendor checkout. Local submodule URLs require no GitHub. Fresh recursive clones are under `fresh/ClientProbe` and `fresh/ServerProbe`. Input SHA-256 manifest and verification results are in the lab's `manifest.json` and `verification.json`.

两消费者固定同一 Shared 提交，protobuf 固定原提交并从本地裸镜像递归获取，不访问 GitHub。干净递归副本位于 fresh；清单记录 119 个输入文件的哈希。共享核心移除原本已排除编译的客户端/服务端端点文件；本批只消费 NetClient/NetServer，完整认证/用户端点归属留下一批处理。源程序集身份和协议常量保留。

Input digest: `38be3607ba2c26a2f19b29cbd7671fc77c6f563a937c39805bef2f811976af72`.

This is a source snapshot rehearsal, not historical Git filtering or the final repository layout. Chat/Trade game-specific contracts, actual host composition and mixed tests have not been extracted. /tmp repositories are temporary and their local absolute submodule URLs need updating if moved; do not treat them as published remotes.

本批是源码快照试验，不是历史提取或最终仓库布局。Chat/Trade 游戏相关契约、完整宿主与混合测试尚未拆分。/tmp 仓库是临时副本，移动后须调整本地子模块地址，不视为已发布远端。

## Validation / 验证

A reproduced run of [create-core-rehearsal.py](f5-local/create-core-rehearsal.py) creates all repositories from the current working tree's selected inputs. Run from the original repository root. It makes commits only inside new local temporary repositories, never commits/pushes the source repository. Game DLLs, credentials, logs, IDE files and original bin/obj are not copied into the snapshot.

生成脚本已实际复跑，生成新的独立仓库及递归副本。须在主仓根目录运行；只在新建临时仓库中提交，不提交/推送主仓，不复制游戏 DLL、凭据、日志、IDE 或旧 bin/obj。

Commands actually run (repeat restore/build in each fresh consumer):

```sh
python3 docs/branch-local/dev/f5-local/create-core-rehearsal.py
# Use the generated lab path; the following records this run.
lab=/tmp/phinix-f5-local-20261008-s085jpgv
# Working directory: $lab/fresh/ClientProbe, then $lab/fresh/ServerProbe
dotnet restore Probe.csproj --source "$lab/empty-feed" -p:NuGetAudit=false -p:BuildInParallel=false
dotnet build Probe.csproj -c Release --no-restore -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
mono "$lab/fresh/ClientProbe/bin/Release/net472/Probe.exe"
dotnet exec --runtimeconfig "$lab/net10.runtimeconfig.json" "$lab/fresh/ClientProbe/bin/Release/net472/Probe.exe"
dotnet "$lab/fresh/ServerProbe/bin/Release/net10.0/Probe.dll"
# Working directory: $lab/Shared; builds the entire core dependency graph for both targets.
dotnet build Common/UserManagement/UserManagement.csproj -c Release -p:RestoreSources="$lab/empty-feed" -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
# Each fresh consumer evaluates its own Shared source/import ownership.
dotnet msbuild Dependencies/Phinix.Common/Common/Utils/Utils.csproj -p:TargetFramework=net472 -getProperty:DirectoryBuildPropsPath,DirectoryBuildTargetsPath -getItem:Compile,ProjectReference
dotnet msbuild Dependencies/Phinix.Common/Common/Utils/Utils.csproj -p:TargetFramework=net10.0 -getProperty:DirectoryBuildPropsPath,DirectoryBuildTargetsPath -getItem:Compile,ProjectReference
```

- Offline restore: both consumers pass using installed SDK/framework packs and existing local NuGet caches, with an empty feed and NuGet audit disabled. This does not prove first-time provisioning on an empty machine is offline.
- Fresh builds: client 0 warnings/errors; server 0 errors/1 existing protobuf trimming warning. Standalone Shared builds all four Common projects for net472/net10, 0 errors/3 existing warnings.
- Runtime: client net472 binary passes 11 assertions on Mono and .NET 10; server net10 passes 11. Cases include protobuf roundtrip, stable protocol identity, ordinary disabled/registration/activation/shutdown/API ownership and passive endpoint construction.
- Isolation: 16 checks pass for pinned recursive gitlinks, shared Directory.Build imports, evaluated Utils source paths, all existing/contained project references, unchanged moved endpoint bytes, game DLL exclusion and clean checked-out source.

离线还原、两消费者及 Shared 双目标构建通过；共享构建零错误、三项既有警告，客户端零警告，服务端一项。三次执行各通过 11 项，包含协议往返、普通注册/禁用/激活/停止/API 撤销和被动端点构建。另有 16 项路径/归属检查通过。本机已有 SDK 与 NuGet 缓存，不宣称空机器首次获取可完全离线。

## Findings retained / 保留的问题

1. Original protobuf SourceLink 1.0 Git tooling throws MSB4018/NullReferenceException while resolving this purely local nested Git topology. Local rehearsal build flags disable source-control queries/SourceLink; neither vendor source nor its global.json was edited. The original failure logs are retained in `/tmp/phinix-f5-local-20261008-9mtlv1xe/logs`. Future hosted build/source-link behavior needs separate verification.
2. Both existing NetClient and NetServer throw NullReferenceException on a second Dispose, after their internal manager has been cleared. The source bytes match the original repository. The probe records this existing limitation explicitly; it does not silently claim idempotent cleanup or change transport code during extraction. Final 11 assertions include one expected-limitation assertion.
3. `docker info --format '{{.ServerVersion}}'` failed because the current user cannot access `/var/run/docker.sock`. No image build/start was performed; no game run or mod distribution package was produced in F5-A.

纯本地嵌套 Git 触发旧 SourceLink 故障，仅演练命令关闭查询，原失败日志保留。原 NetClient/NetServer 第二次 Dispose 会空引用，源码哈希一致，测试明确记录限制，不顺带改传输实现；11 项断言含该限制的预期断言。Docker 守护进程权限不足，未做镜像/游戏验证，也未生成 F5 游戏包。

## Next F5 batch / 下一批

Separate shared protocol from game/client Chat/Trade contracts while preserving assembly/API identities, then migrate full client/server endpoint consumers and packaging in local copies. Split mixed runtime tests by responsibility and verify fresh acquisition, official extension outputs and server startup/image. No source moves in the main repository or F6 public repository creation are authorized by this probe result alone. Signing/theme/RedPacket business plans stay deferred.

下一批在本地副本整理共享协议和客户端/游戏契约边界，保留程序集/API 身份，再接入完整两端及打包，按职责拆测试并验证正式插件输出、服务端启动/镜像。不能仅凭最小试验结果移动主仓源码或创建 F6 公共仓库；签名、主题和红包业务事项不混入本批。
