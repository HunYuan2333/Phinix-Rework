# F5-B: contracts and complete local consumers / 契约与完整消费者演练

Date: 2026-10-08. Source baseline: `328c556cd80949dc76d7252039ec8f0eeb5eff1b` on dev. User authorized continuing local F5. No production source moves or new GitHub repositories; F6 remains unstarted. Existing parallel changes are preserved.

## Ownership / 归属

Lab: `/tmp/phinix-f5-local-20261008-s085jpgv`.

| Local repository | Commit | Responsibility |
| --- | --- | --- |
| Shared | `3f3af8d39e900070b8e43ba796f37343418475a4` | Common core and neutral Chat/Trade contracts, recursive protobuf |
| ClientFull | `3109475` | Complete client, official client extensions, game contract wrappers, client legacy protocol sources |
| ServerFull | `907f1bde85b3574dde926f96ca5aa3929c8c055f` | Complete server and official server extensions |

Both full consumers pin the same Shared gitlink. Their fresh copies are under `fresh/ClientFull` and `fresh/ServerFull`. Shared contracts target net472/net10 and reference no Client or game assemblies. Client wrappers compile the single neutral source plus the original client/game interfaces into the original ChatExtension/TradeExtension assemblies. Server consumes Shared net10 contracts. Client-named neutral DTOs remain shared to preserve public surface; naming cleanup is excluded.

两端锁定相同 Shared 提交。共享契约不依赖 Client 或游戏程序集；客户端包装工程链接同一份中立源码，并补上原客户端/游戏接口，保持原程序集名、版本和接口。服务端使用共享 net10 契约。中立 DTO 的既有名称保留。

The neutral net472 Shared contract assembly is a standalone build check, not an additional client runtime DLL. Client packaging includes only the full client contract assembly under each original numbered filename. A future plugin SDK must select the proper endpoint contract; same-identity neutral and full client assemblies must never be distributed together.

共享 net472 契约用于独立构建核查，不另发一个客户端运行时 DLL。客户端包只发包含完整接口的原编号程序集；后续 SDK 必须正确选择端点契约，避免同名的中立/完整客户端程序集同时分发。

## Validation / 验证

All commands run from the respective fresh consumer except explicitly stated. Full logs and manifests are retained in the lab. Offline restore uses installed SDK/framework packs and existing NuGet cache; it does not establish provisioning on an empty machine.

```sh
# ClientFull: offline restore and full client build
 dotnet restore PhinixClient.sln --source /tmp/phinix-f5-local-20261008-s085jpgv/empty-feed -p:Configuration='Release 1.6' -p:NuGetAudit=false -p:BuildInParallel=false
 dotnet build PhinixClient.sln --configuration 'Release 1.6' --no-restore -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
# ServerFull: offline restore and full server build
 dotnet restore Server/Server.csproj --source /tmp/phinix-f5-local-20261008-s085jpgv/empty-feed -p:NuGetAudit=false -p:BuildInParallel=false
 dotnet build Server/Server.csproj -c Release --no-restore -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
# Shared: independently build BOTH contract targets (repeat with Trade/TradeExtension)
 dotnet build Extensions/Chat/Contracts/ChatExtension.csproj -c Release -p:RestoreSources=/tmp/phinix-f5-local-20261008-s085jpgv/empty-feed -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
```

- Full client: 0 errors, 3 warnings. Full server: 0 errors, 2 warnings. Independent Shared Chat and Trade builds: each 0 errors, 3 warnings. Warnings concern existing protobuf targets/trimming and obsolete Trade Items usage.
- Cecil comparison in separate processes: all 4 original/candidate Chat/Trade net472/net10 contract pairs match. 988 public metadata rows include assembly identity, type/member signatures, enum constants, parameter defaults and custom attributes. 400 method bodies and local variable signatures match, including private serialization methods. Evidence: `f5b-api-parity.json`, `f5b-il-parity.json`, API dumps and empty IL diffs under `logs/`. This proves those contract surfaces/implementations, not whole-game behavior.
- 11 isolation checks: clean tracked fresh consumers, private references not tracked, equal gitlinks, contained existing owned literal ProjectReferences, no Shared contract Client/game dependency, no Client tree in ServerFull. Vendored protobuf compatibility test projects are outside this check; their historical unresolved references were not edited. Evidence: `f5b-isolation.json`.
- Python equivalent of artifact checks: 31 required files, one copy of composition/Stateless runtime DLLs with exact build bytes, both official server extension outputs, no game/Unity/mscorlib/Steamworks DLLs, failure translations present, ZIP entries match build output. Command from original root: `python3 /tmp/phinix-f5b-artifacts.py`.
- Actual server startup/shutdown passes, exit 0, using isolated lab `runtime-data`, loopback address and requested port 0. Config generated through actual `Config.Save`; fresh history/user/credential files remain only in the lab. First sandbox run failed socket creation with permission denied; approved retry passed. Commands: `dotnet run --project /tmp/phinix-f5-local-20261008-s085jpgv/ServerConfigProbe/ConfigProbe.csproj -c Release -p:RestoreSources=/tmp/phinix-f5-local-20261008-s085jpgv/empty-feed -p:NuGetAudit=false -- /tmp/phinix-f5-local-20261008-s085jpgv/runtime-data/server.conf`, then `dotnet ../fresh/ServerFull/Server/bin/Release/net10.0/PhinixServer.dll` from runtime-data with `exit` on stdin. Logs: `ServerFull-start-stop.log`, `ServerFull-start-stop-permitted.log`.

完整客户端/服务端及独立共享契约构建通过；四组接口元数据和方法体一致，11 项路径检查与31项产物核查通过。实际服务端在临时数据目录正常启动/关闭，未使用真实玩家数据。编译/元数据检查不能替代游戏测试。

## Acquisition findings / 获取问题

The initial client snapshot omitted client-owned LegacyAdapter/Contracts source and ignored local `.nuget` references. Corrected by retaining legacy sources in ClientFull and separately provisioning ignored private `.nuget` and GameDlls in the fresh client. No vendor generated source was edited. Input/reference SHA hashes and final commits are in `f5b-manifest.json`. Future clean-machine dependency provisioning remains a gate; merely copying local legacy NuGet folders is not the final published acquisition design.

首轮客户端快照遗漏了旧协议源码和被忽略的本地依赖，已补齐客户端归属源码；游戏/Harmony 等本地编译依赖单独提供且不入 Git。未修改生成协议源码。正式获取流程仍需解决空机器依赖准备，不能把复制本机 .nuget 当成最终发布方案。

SourceLink local nested URL workaround and Docker socket permission limitation remain as recorded in F5-A. No Docker image build/start, game run, mixed-harness migration, connection interoperability or external plugin acceptance is claimed here.

Rehearsal generator: `f5-local/create-contract-rehearsal.py --lab <new-F5-A-lab>` from the main source root. It requires an unused F5-A lab. The corrected generator received a syntax check; the recorded successful lab was corrected incrementally and pulled into the fresh client. Do not claim a second complete generator replay.

## Game acceptance / 游戏核查步骤

Local trial package: `/tmp/phinix-rework-f5b-local-20261008.zip`.
SHA-256: `72a071bbb4c8c60967627efe7540a3f432a820b639338e13d8dc519daa2a19d9`.

1. In a separate test installation, replace the old Phinix folder completely with the ZIP's phinix-rework folder; enable Harmony and Phinix, check normal entry/button and no duplicate assembly errors.
2. Log into the usual test server; check Chat live messages and load a save without restarting, confirming history remains.
3. Check inventory and a small trade with authoritative acknowledgement, rejection/cancel and recovery behavior; confirm no lost/duplicated items.
4. Install/disable/enable an example managed plugin, restart as requested, confirm discovery/loading. Check Store download and management.

此包来自本地分仓消费者，主仓日常构建输出仍是原 dev 布局。上述游戏核查尚未执行，不要求将试验包交给普通用户。

## Next / 后续

F5-B contract/full-build checkpoint passes with the acquisition limitations above. Continue F5-C: separate mixed runtime harness responsibilities without dropping cases; replay complete clean acquisition with reproducible references, verify plugin loading and image build/start. Do not mark overall F5 complete or start F6. No business, persistence, wire-format or ownership logic was changed in this extraction batch.
