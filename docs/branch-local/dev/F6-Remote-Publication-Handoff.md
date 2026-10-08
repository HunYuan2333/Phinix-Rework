# F6 remote publication / 三仓远端发布

2026-10-08. The user explicitly authorized complete-source publication to the three public repositories, dev only, and dev as the default for the two new repositories.

用户明确授权三仓公开推送 dev，新 Common/Server 默认 dev；Rework 原默认 main 未改。所有推送均未使用 force。

## Published checkpoints / 已发布检查点

| Repository | dev commit |
| --- | --- |
| [Phinix-Common](https://github.com/HunYuan2333/Phinix-Common/tree/dev) | `6e1f47002a22c8ffa06841d09d4f68c325b70a1e` |
| [Phinix-Rework](https://github.com/HunYuan2333/Phinix-Rework/tree/dev) | `ae4cbaf7b018119ea71ccacc40648d91a102f967` |
| [Phinix-Server](https://github.com/HunYuan2333/Phinix-Server/tree/dev) | `5b7743e3e8c7ceda04a76ec26ffcfabd0aab23f5` |

The pre-split branch `codex/pre-split-20261008` was pushed and verified first at `0b17036a7ca6edddb8991305deec0718bcca4347`. Rework dev advanced from `328c556` by a normal fast-forward. Both endpoints pin the published Common commit above; nested protobuf remains `4b0c3aacf0657fbf38253b38918d3358dd4319ec`.

先推送并核对拆分前备份，再按 Common → Server → Rework 顺序发布。原工作目录 dev、索引、未提交及并行修改保持原样，未 checkout/reset/pull。原目录仍是未拆分工作区；拆分客户端的持久工作目录在 `/home/hunyuan2333/Phinix/Phinix-Split-Validation-20261008/Phinix-Rework`，不要误认为原目录已切换。

## Real remote acquisition and validation / 真实远端获取与验证

Fresh clones under `/tmp/phinix-split-remote-validation-20261008` used canonical HTTPS URLs without local URL rewrites:

```sh
git clone --branch dev --recurse-submodules https://github.com/HunYuan2333/Phinix-Common.git
git clone --branch dev --recurse-submodules https://github.com/HunYuan2333/Phinix-Rework.git
git clone --branch dev --recurse-submodules https://github.com/HunYuan2333/Phinix-Server.git
```

All three remote HEADs and recursive gitlinks matched the table. Private game/Harmony references were supplied only to the fresh client clone with the existing reference-provisioning helper; they remain outside Git and distributions.

| Check | Result |
| --- | --- |
| Common six owned projects, net472/net10 | Six successful builds; each 0 errors / 3 existing warnings |
| Client full `Phinix.sln`, Release 1.6 | 0 errors / 8 existing warnings |
| Server full `Server/Server.csproj`, Release | 0 errors / 4 existing warnings |
| Shared/Client/Server Phase35 | 7 + 97 + 13 = 117 assertions passed |
| Actual client net472 composition under Mono | 69 assertions passed |
| Artifacts | 31 required files, unique DI/Stateless runtimes, no game DLLs, translations and ZIP bytes passed |
| [Server GitHub CI](https://github.com/HunYuan2333/Phinix-Server/actions/runs/37773588914) | Success; image build/load only |

Exact command arrays and return codes are in `build-results.json` and `smoke-results.json` under the fresh-clone parent; recursive pins/status in `acquisition.json`. Builds used the same commands recorded in each README, with local validation flags `-p:RestoreSources=/tmp/phinix-f5-local-20261008-gslqn8eh/empty-feed -p:NuGetAudit=false -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false`; builds also used `-p:BuildInParallel=false -m:1`. This uses the installed SDK/framework packs and NuGet cache, and does not claim empty-machine setup or a live dependency vulnerability audit. Artifact command: `python3 /tmp/phinix-split-remote-artifacts.py`.

Client trial archive: `/tmp/phinix-rework-split-remote-20261008.zip`; SHA-256 `72d11b93c6821667c5160a6bdf66f39a1c5734a3ca4cfd3f3b6404c70abf340a`.

构建和回归均成功，保留既有 protobuf/旧协议警告。三仓 tracked 工作树干净；客户端 harness 生成的 `Extensions/Chat/Contracts/*Undefined*Output/` 未跟踪、未发布。原工作区并行修改保留。本次无游戏实测；需要增量游戏检查时，在独立游戏目录整包替换，检查启动入口、旧存档读取、聊天收发/读档保留、商店下载及已安装示例；源码和编译验证不代替实测。未改变协议、存档格式、物品所有权或服务器确认语义。

## Remaining delivery / 剩余交付

New Server CI builds successfully. Docker Hub publication is intentionally gated by repository variable `SERVER_IMAGE_PUBLISH_ENABLED=true` plus `DOCKER_HUB_USERNAME` / `DOCKER_HUB_TOKEN` secrets in the Server repository, as documented in its README. No secrets were copied/read. The old Rework main publisher is still present; sole production-publisher cutover is not complete.

F6 remains open for the Index validator's three stale inputs and pinned multi-repository provenance, independent example/host manifest verification, sole server publisher cutover, and safe original-workspace transition. This batch closes remote publication/acquisition, not all F6. F6-M follows F6; F6-S remains canceled.

本批完成三仓远端发布和实际获取验证。Index 三项滞后来源与多仓固定来源、独立示例/宿主清单、唯一服务端发布者切换和原脏工作区安全转移仍待处理；不宣称 F6 整体完成，不进入 F7。F6-M 后续执行，F6-S 取消。
