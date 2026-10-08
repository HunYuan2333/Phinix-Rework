# F5-C: runtime ownership and acquisition / 测试归属与获取核查

2026-10-08. Main dev/origin-dev baseline stays `328c556`. User authorized continuing local F5. Production source boundaries remain in the main repository; no F6 repository creation, publication or push was performed. Existing parallel changes remain untouched.

## Final local replay / 最终本地重放

Lab: `/tmp/phinix-f5-local-20261008-gslqn8eh`. Both full consumers are fresh recursive clones under `replay/`.

| Repository | Pinned commit |
| --- | --- |
| Shared | `54c822d9b74d87046d732d14776824ba3688d4ba` |
| ClientFull | `0719d9ac8fe7cbfb44a4ff9bb73a209b70682d2d` |
| ServerFull | `a93a0b25e0f16c4137ece6e8540f56faa760b6a5` |

All three generators were replayed successfully from the current working-source snapshot, followed by fresh recursive clones and explicit reference provisioning. HEAD identifies the baseline; manifest input hashes also record pending test-project corrections. Both consumers pin the exact Shared commit above. Full client build: 0 errors/8 warnings; full server: 0 errors/4 warnings. Existing warnings concern protobuf targets/trimming, obsolete protocol members and locked-package approximations; logs retain their exact messages.

三套生成脚本完整重放，再重新递归获取两端并准备明确指定的编译依赖。源码基线未提交变化，测试工程补正由输入哈希记录。两端锁定相同 Shared 提交；完整客户端/服务端构建均零错误。新获取核查使用本机已有 SDK、框架包和 NuGet 缓存，不宣称空机器首次准备可完全离线。

## Coverage / 覆盖

| Check | Result |
| --- | --- |
| Original Phase35 | Original 18 entry scenarios retained; instrumented baseline 117 Assert calls; actual repaired main harness passes |
| Split Phase35 | Shared 4 entry cases/7 assertions, Client 7/97, Server 8/13; sum 117 |
| Scenario preservation | All 17 non-source-audit entry bodies/partial files unchanged; three original legacy source checks redistributed by endpoint ownership |
| Contract parity | Chat/Trade net472/net10: 988 public metadata rows and 400 method bodies match original binaries |
| Endpoint relocation | NetClient, NetServer, ClientAuthenticator, ClientUserManager bytes unchanged |
| Source graph | 488 existing contained literal Compile/ProjectReference links; clean fresh worktrees and equal gitlinks |
| Plugin Store | 968 assertions on .NET 10 |
| Managed extensions | 3212 assertions each on .NET 10 and Mono; includes 2224 operation-regression assertions, ordinary startup/registry child processes and recovery |
| Extension loader | 13 assertions each on .NET 10 and Mono; declared owned graph, ambiguity, rejected foreign requests, normal registry discovery |
| Client composition | 69 assertions, actual net472 binary on Mono, including built author-entry fixture |
| Inventory composition | 77 assertions on Mono |
| Trade composition | 112 assertions on Mono |
| Store composition | 93 assertions on Mono |
| Legacy adapter | All 11 scenarios/68 assertions on Mono |
| Chat | All 17 regression scenarios on Mono |
| Inventory runtime | All 13 reservation/grouping/snapshot/journal scenarios on Mono |
| Responsive geometry | Existing harness passes |
| Package cleanup | Both existing actual MSBuild layout tests pass |
| Artifacts | 31 required files, unique runtime assets with build bytes, no game DLLs, ZIP/build byte parity |
| Server image | Local image builds and starts/shuts down, exit 0 |

Shared owns core tests; Client owns its runtime/UI/store/game regression sources; Server owns pipeline tests. The original legacy source-audit entry is split into client/server entries, hence 19 entry calls across three binaries preserve 18 original scenarios. No assertions were dropped to produce the matching total. Disabled-setting checks throw directly and are retained byte-for-byte; they are not counted as Assert calls in either total. Loader fixtures are supplied as explicit CLI test payloads produced by the managed fixture projects, not as a Shared production dependency on Client.

按职责迁移测试，旧 API 源码检查拆成两端入口，因此三组入口合计19项，保留原18个场景。117条是原 Assert 方法调用数；禁用设置检查的直接异常条件完整保留，不混入计数。加载器的测试输入通过明确的 DLL 目录参数提供，不增加 Shared 对客户端实现的生产依赖。

The source-path check excludes vendor compatibility-test projects and generated Output source snapshots. Existing packaged `Abstractions/*.csproj` snapshots do not constitute a standalone buildable SDK; selecting/generating a publishable author SDK project remains a later tooling/F6 ownership decision. Do not use package source snapshots as evidence that external author tooling is complete.

源码路径检查排除供应商历史兼容测试和生成的 Output 源码快照。包内既有 Abstractions 工程快照不等于可独立构建的插件 SDK；对外工具/SDK 发布工程仍需后续阶段明确，不能据此宣布外部开发工具完成。

## Corrections and limits / 补正与限制

1. F4 left the main Phase35 linked-source project without IClientComposition.cs; both original and migrated test builds failed with missing IClientExtensionModule. Added that actual source link to the main test project and regenerated the client test graph. Production runtime code was not changed.
2. Classic ChatRegressionTests copied Autofac but omitted System.Diagnostics.DiagnosticSource and companion DI dependencies, causing Mono TypeLoadException. Added a test-only composition runtime copy target and imported it from the classic Chat harness. All 17 scenarios now pass; the real client package already contained those dependencies.
3. LegacyTradeRuntimeTests requires the old pinned Memory 4.5.3 and Unsafe 4.5.2 HintPaths. Provisioning initially included only the local Harmony nupkg; legacy build failed. Corrected provisioning uses the corresponding cached nupkg files, with recorded SHA-256 hashes. No scenario source, package version or production dependency was changed for this correction.
4. SourceLink query disabling remains a purely local Git URL workaround, not a vendor modification. Hosted-source SourceLink behavior is not verified here.
5. Mono prints missing Unity.Burst custom-attribute warnings for the compile-only game reference DLLs. Harnesses above return 0 after the test dependency correction. These runs are not RimWorld/Unity game acceptance or live client-server interoperability tests.
6. Buildx is absent; the first Docker command failed on unsupported --progress. Existing Docker builder successfully built the same context with ordinary docker build. Access to the daemon required sandbox escalation; it is available. This supersedes F5-A's untested Docker gate for the local image only.

主仓仅补正两个测试工程的依赖准备：Phase35 增加真实契约源码引用，聊天经典工程增加测试运行时复制规则。未修改业务、协议、持久化或物品所有权行为。旧适配器缺失的是明确版本的缓存编译依赖，已按包哈希准备。SourceLink、本机游戏运行及托管 CI 发布验证仍有上述边界。

## Exact commands / 实际命令

Generation and provisioning, from the main source root:

```sh
python3 docs/branch-local/dev/f5-local/create-core-rehearsal.py
python3 docs/branch-local/dev/f5-local/create-contract-rehearsal.py --lab /tmp/phinix-f5-local-20261008-gslqn8eh
python3 docs/branch-local/dev/f5-local/create-runtime-rehearsal.py --lab /tmp/phinix-f5-local-20261008-gslqn8eh
git -c protocol.file.allow=always clone --recurse-submodules /tmp/phinix-f5-local-20261008-gslqn8eh/ClientFull /tmp/phinix-f5-local-20261008-gslqn8eh/replay/ClientFull
git -c protocol.file.allow=always clone --recurse-submodules /tmp/phinix-f5-local-20261008-gslqn8eh/ServerFull /tmp/phinix-f5-local-20261008-gslqn8eh/replay/ServerFull
python3 docs/branch-local/dev/f5-local/provision-local-client-references.py --client /tmp/phinix-f5-local-20261008-gslqn8eh/replay/ClientFull --game-references /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --harmony-package /home/hunyuan2333/Phinix/Phinix-Rework/.nuget/Lib.Harmony.2.3.6/Lib.Harmony.2.3.6.nupkg --nuget-cache /home/hunyuan2333/.nuget/packages
```

Full consumer builds, each from its respective replay root:

```sh
dotnet build PhinixClient.sln --configuration 'Release 1.6' -p:RestoreSources=/tmp/phinix-f5-local-20261008-gslqn8eh/empty-feed -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
dotnet build Server/Server.csproj -c Release -p:RestoreSources=/tmp/phinix-f5-local-20261008-gslqn8eh/empty-feed -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
```

Runtime invocations are recorded as full argument arrays in `f5c-runtime-results.json`, `f5c-extra-results.json`, `f5c-phase35-results.json`. Executed scripts are retained as `verify-run-checks.py`, `verify-extra-checks.py` in the lab; all recorded commands returned 0 in this final replay. They build each existing harness against actual local source/output, then run Mono where applicable. Protocol/input fixtures remain tracked test data. Compile-only game DLLs and legacy reference directories remain ignored/untracked.

Additional commands from the original source root:

```sh
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj -c Release -p:RestoreSources=/tmp/phinix-f5-local-20261008-gslqn8eh/empty-feed -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false
python3 .github/scripts/test-client-package-layout.py
python3 /tmp/phinix-f5c-artifacts.py
```

API/IL comparison used the existing Cecil dumper (`mono /tmp/phinix-f5-il.exe <original-or-candidate-contract.dll>`, source `/tmp/phinix-f5-il.cs` compiled with `mcs -r:/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll -out:/tmp/phinix-f5-il.exe /tmp/phinix-f5-il.cs`). Separate invocations compare full sorted outputs; `f5c-contract-parity.json` records counts. `f5c-manifest.json`, `f5c-source-paths.json` and the ignored private-reference manifest retain acquisition evidence.

## Docker evidence / 镜像证据

Image: `phinix-rework:f5-local-20261008`, built ID `54ce20065b0b`. Built from the prior corrected fresh consumer at `/tmp/phinix-f5-local-20261008-s085jpgv/replay/ServerFull`; final generated consumer has identical Dockerfile/dockerignore and all 3546 compared source/build inputs, recorded in `f5c-image-input-equivalence.json`.

```sh
# Prior corrected ServerFull root; local only, no push.
docker build -t phinix-rework:f5-local-20261008 .
# Config made using actual Config.Save, loopback/port 0; isolated fresh image data.
docker run --rm --network none -i --mount type=bind,src=/tmp/phinix-f5-local-20261008-s085jpgv/runtime-image-data,dst=/data phinix-rework:f5-local-20261008
# stdin: exit
```

Build/start logs: previous lab `logs/F5C-Docker-build-2.log`, `logs/F5C-Docker-start-stop.log`. Runtime starts with empty users/credentials/history and exits 0; container has no published ports or external networking. Data and test image are retained locally for review; no running test container remains. No real user data, existing service or Docker Hub image was changed.

镜像本地构建并在无外部网络、无开放端口、隔离数据目录下启动退出。最终脚本重放生成的服务端与该构建的输入逐文件一致；不是第二次重复构建镜像。未推送镜像、未修改现有服务或真实用户数据。

## Trial and remaining acceptance / 试验包与剩余验收

ZIP: `/tmp/phinix-rework-f5c-local-20261008.zip`.
SHA-256: `cce9c0b1fa5a60d18b6f54f38b431851590ffe33df5939a4ed61073b73d2717b`.

Use a separate RimWorld test installation; replace the entire old Phinix directory with this ZIP's phinix-rework directory. Keep saves/managed plugin data separately and do not merge old DLLs into the new package.

1. Enable Harmony/Phinix, check normal entry and absence of duplicate assembly/startup errors; install/enable an example managed module and restart, checking discovery/load/disable/re-enable.
2. Connect to the usual test server, check chat/send/receive, read a save without restarting and verify history. Check plugin-store download/install/manage.
3. Check inventory and a small trade, including authoritative acknowledgement, rejection/cancellation and recovery; verify no lost/duplicated items.

独立测试游戏目录核查启动、示例插件加载、商店、聊天读档历史和库存交易即可。试验包由最终本地分仓消费者构建；主仓正常构建仍使用原 dev 物理布局。用户于 2026-10-08 后续明确反馈“F5-C通过”，记录整体游戏验收并授权继续 F6，不补造逐项反馈。

Rollback keeps each endpoint commit, its exact Shared gitlink and matching complete runtime package together. In a local rehearsal checkout: record git status first, use the selected endpoint commit, recursively update pinned submodules with file protocol enabled, provision the same private references, rebuild and replace the complete trial folder. Use saved trial archives for game rollback rather than mixing DLLs. This batch introduces no persistence-format migration; source rollback is not a substitute for data rollback if later work changes formats.

回退以端点提交、固定 Shared 提交和完整运行依赖包为一组；先保留未提交工作，再切换本地试验提交、递归获取固定子模块、准备相同编译引用并重建/整包替换。本批未改变持久化格式，后续若改格式需另定数据回退策略。

F5-C automatic/local checkpoint passes; the user subsequently reported “F5-C passed” on 2026-10-08 and authorized F6. This records collective game acceptance, without individual invented results. Hosted CI/source-link/publication and final physical/public boundaries are F6 work; author tooling, themes and RedPacket optimization remain separate. F6-S mandatory signing admission has been canceled.
