# F6-A: local history and ownership migration / 本地历史与归属迁移

2026-10-08. User reported “F5-C passed”, authorized continuing and confirmed exact repository identities: Client keeps HunYuan2333/Phinix-Rework; new Shared is HunYuan2333/Phinix-Common and Server is HunYuan2333/Phinix-Server. F6-M follows F6 before F7; F6-S is canceled.

## Prepared candidates / 已准备候选

Local root: `/tmp/phinix-f6-history-20261008-r2`. Fresh canonical-URL replay checkouts are under `replay/`. No remote repository creation, remote push, current-workspace replacement or publisher retirement has occurred. Original dev HEAD remains `328c556`; dirty/parallel work is retained. All candidate commits are local dev commits.

| Repository | Candidate dev commit | Reachable dev commits | History |
| --- | --- | --- | --- |
| Phinix-Common | `e0ff969319650e778fbe4d62a27567a0464c927b` | 509 | Relevant original dev paths, empty changes pruned |
| Phinix-Rework | `9d82918f7cd58a31e2644cf268c836be61370234` | 1262 | Original dev ancestry retained; 328c556 is an ancestor |
| Phinix-Server | `206a6723cb4ae0d0d6e81ebbca1f85baf485cc85` | 245 | Relevant original dev paths, empty changes pruned |

Both consumers pin Shared `e0ff969319650e778fbe4d62a27567a0464c927b`. Shared pins protobuf `4b0c3aacf0657fbf38253b38918d3358dd4319ec`. Committed .gitmodules files use canonical public GitHub URLs, without /tmp URLs or floating branches. Explicit per-command local URL overrides support recursive acquisition before publication.

两端固定相同 Shared 提交，protobuf 保留原固定提交。提交中的子模块地址已是正式 URL；本地获取通过命令级 URL 映射，不把临时路径写进发布文件。新仓过滤相关 dev 历史，客户端沿现有 dev 历史追加；其他分支/标签的迁移策略不假称已经执行。

Historical path inspection finds no state/log/build/game path candidates in the filtered Shared/Server ancestry. The original Client ancestry already contains historical credentials/users/server.conf paths; this batch neither reads their values nor rewrites the already-existing client history. Current candidate tips contain none of those state files. Local filter backup/original refs and old origin tracking refs must not be mirrored to a new public repository; publication must use explicit reviewed branch/tag refs.

新 Shared/Server 的相关历史路径检查未发现状态/日志/构建/游戏引用候选。客户端保留既有历史，历史中已有 credentials/users/server.conf 路径，本批不输出其内容，也不擅自重写原历史；当前候选树不包含这些状态文件。过滤过程的原始备份引用仅保留本地，禁止 --mirror 推送；正式发布只推送明确审核的分支/标签。

## Ownership and entry points / 归属与入口

- 412 .cs/.proto files exactly match the user-accepted F5 snapshot. No business, wire, persistence or item ownership behavior changes.
- Client keeps the familiar `Phinix.sln` build entry, containing only the client graph. PhinixClient.sln is the byte-identical F5 rehearsal/test marker during transition; their equality must be retained.
- Artifact scripts are endpoint-owned. Client's default artifact check no longer requires Server output. Server retains its own default output check. PowerShell is unavailable locally; Python equivalent checks execute against actual outputs.
- Server candidate owns Dockerfile, compose, artifact checks and the exact existing Docker workflow; registry/deployment names and data mounts remain unchanged. Client candidate removes the old publisher from its tip. The original live remote/workspace publisher remains active until a validated remote cutover; no new job has been triggered.
- Minimal repository instructions and stable architecture/recovery docs travel with candidates. No author SDK/tool implementation, signing, themes or F6-M behavior was added.
- Root LICENSE is absent in the original project; do not invent a new license for extraction. Explicit licensing/notice policy remains a publication decision. Existing vendor/sample notices are not substitutes for a root project license.

主仓仍是原物理布局，正常构建仍使用原 dev 源码。候选中保留旧构建入口，校验与发布按端点归属拆开；Docker 现有镜像名、卷和入口不变。原仓没有根 LICENSE，本批不编造许可证；正式分仓发布时需明确说明策略。

## Executed validation / 已执行验证

- Fresh recursive clone with canonical URLs and local mirrors, including nested protobuf; private Harmony/Memory/Unsafe nupkg and legal GameDlls are explicitly provisioned, hashed and ignored.
- Full candidate client: 0 errors/8 warnings; candidate server: 0 errors/4 warnings, using local offline cache and SourceLink query workaround flags.
- Preserved entry incremental build: 0 errors/2 warnings. Exact usual command also passes with 0 errors/48 warnings; most warnings are NU1900 because the sandbox cannot fetch NuGet vulnerability data. This is not a completed vulnerability audit or hosted-source-link verification.
- Actual original main solution additionally builds, 0 errors/7 warnings; it was not migrated/replaced.
- Phase35 split: Client 97, Server 13, Shared 7 Assert calls; all 117 pass. Original scenario coverage remains the accepted F5 split.
- Plugin Store 968, managed runtime 3212 (.NET 10), client composition 69, inventory composition 77, trade composition 112, Store composition 93 and legacy adapter 11 scenarios/68 assertions pass. Composition/legacy executables are actual net472 binaries on Mono. Responsive geometry passes.
- 31 artifact checks pass; runtime assets are unique and match actual build bytes, no game reference DLLs are packaged. Current clean candidate trees and Shared pins are checked. 412 accepted source bytes match; client original history ancestry is verified.
- No new game run, second Docker image build/start or remote workflow run is claimed. F5-C game acceptance and image evidence apply to unchanged business inputs; actual F6 remote publication/cutover acceptance remains a later batch.

### Commands / 命令

From original source root, initial history generator run failed because Git required a working .gitmodules file before re-adding a previously tracked submodule. The failed candidate is retained at `/tmp/phinix-f6-history-20261008`. Corrected retry succeeds:

```sh
python3 docs/branch-local/dev/f6-local/create-history-checkouts.py --snapshot-lab /tmp/phinix-f5-local-20261008-gslqn8eh --output /tmp/phinix-f6-history-20261008-r2
```

The final helper also includes the subsequently validated endpoint artifact ownership and client solution alias corrections; these were applied as reviewable local commits in the recorded candidates. The final helper was syntax checked, but no second complete history rewrite with that final helper is claimed.

Fresh acquisition (repeat with Phinix-Server):

```sh
git -c protocol.file.allow=always -c url./tmp/phinix-f6-history-20261008-r2/Phinix-Common.insteadOf=https://github.com/HunYuan2333/Phinix-Common.git -c url./tmp/phinix-f5-local-20261008-gslqn8eh/protobuf.git.insteadOf=https://github.com/protocolbuffers/protobuf.git clone --recurse-submodules /tmp/phinix-f6-history-20261008-r2/Phinix-Rework /tmp/phinix-f6-history-20261008-r2/replay/Phinix-Rework
python3 docs/branch-local/dev/f5-local/provision-local-client-references.py --client /tmp/phinix-f6-history-20261008-r2/replay/Phinix-Rework --game-references /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --harmony-package /home/hunyuan2333/Phinix/Phinix-Rework/.nuget/Lib.Harmony.2.3.6/Lib.Harmony.2.3.6.nupkg --nuget-cache /home/hunyuan2333/.nuget/packages
```

From the respective candidate fresh root:

```sh
dotnet build PhinixClient.sln --configuration 'Release 1.6' -p:RestoreSources=/tmp/phinix-f5-local-20261008-gslqn8eh/empty-feed -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
dotnet build Server/Server.csproj -c Release -p:RestoreSources=/tmp/phinix-f5-local-20261008-gslqn8eh/empty-feed -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false -p:BuildInParallel=false -m:1
# Actual familiar client command, without the local SourceLink/audit flags:
dotnet build Phinix.sln --configuration 'Release 1.6' -p:BuildInParallel=false -m:1
```

Runtime runner command: `python3 /tmp/phinix-f6-history-20261008-r2/verify-runtime.py`. Full per-project argument arrays/results are retained in `f5c-runtime-results.json`, `phase35-results.json`; every listed final command returned 0. Artifact command from original root: `python3 /tmp/phinix-f6-history-20261008-r2/verify-artifacts.py`. Logs retain original failures, builds and test outputs; manifest records history parents, overlay input hashes and final commits. Git filter-branch was run only on disposable new local clones, never on the original repository.

## Validator finding / 校验器发现

The current independent Index repository's frozen validator has 11 of 14 sources matching the migrated ownership trees. Three are already behind the original accepted source baseline: PackageModels.cs, ManagedExtensionManifestReader.cs and ManagedExtensionMetadata.cs. Differences include F4 diagnostic presentation and DI/host-reference policy support. This mismatch is not introduced by extraction. Exact ownership/digests and diffs are retained in `validator-source-ownership.json` and logs. No Index worktree or immutable catalog/release records were changed.

Index scripts currently assume one monorepo source root. F6-B must prepare explicit Client/Shared source roots pinned to the reviewed commits, refresh/test the affected frozen validator from those trusted inputs, and retain immutable approved catalog history. Do not blindly overwrite it from candidate/plugin code. Host manifests and independent example consumption need corresponding provenance checks.

Index 当前固定校验器有11/14份源码一致；3份在拆仓前就滞后于已验收源码，涉及 F4 的诊断和 DI/宿主引用策略。原 Index 工作区未改。F6-B 需要增加明确、固定提交的 Client/Shared 来源映射，从可信输入更新并测试固定校验器，核对宿主清单和独立示例消费，保留历史已批准目录/发行记录。

## Next and game check / 后续与游戏核查

F6-A local history/ownership checkpoint passes; overall F6 remains incomplete. Continue F6-B with validator/provenance and independent-consumer/publication preparations. Confirm actual new remotes/shared commits are obtainable before consumer publication; configure Server publisher prerequisites before retiring the old publisher. Transfer current dirty work safely before any original workspace cutover. Repository naming approval does not mean a remote switch already happened.

Trial package: `/tmp/phinix-rework-f6a-local-20261008.zip`, SHA-256 `556becf83be7581f6caa431f18b1f27a521f56787536e07294f38d346afafcd9`.

Business code is unchanged from accepted F5-C. If testing this intermediate package, use a separate game folder and check only startup/entry, existing managed example discovery and Store opening/download as incremental checks; retain the accepted F5-C package and player data for rollback. This does not replace the later remote-cutover client/server/store/example acceptance. No new signing requirement or save-format migration exists.

本批业务源码不变，若要检查中间包，独立目录整包替换后增量确认启动入口、已有示例加载和商店即可；保留已验收 F5-C 包及玩家数据。正式远端切换后还需联机/商店/示例验收。F6-M 待 F6 完成后实施，F6-S 不做。
