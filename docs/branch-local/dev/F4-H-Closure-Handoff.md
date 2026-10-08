# F4-H and F4 checkpoint closure / F4-H 与 F4 检查点收尾

2026-10-08, dev. The user explicitly cancels the whole-mod ZIP direction, moves theme selection/store theme downloads to later evaluation, and closes F4 after documentation. F5 is not authorized. This final turn edits documentation only; previous H code changes remain uncommitted alongside preserved parallel changes. No branch, commit, push or publication.

用户明确取消整模组 ZIP 方向，主题选择/商店下载主题转后续评估，要求文档完成即关闭 F4；未授权 F5。本次仅修改文档，上一轮 H 代码与并行修改均保留且未提交；未开分支、提交、推送或发布。

## Implemented H slice / 已实现 H 切片

- `LegacyClientExtensionModule` is a temporary source compatibility adapter with Obsolete replacement guidance on the type and Register method. Direct existing client binaries continue on the ordinary registry; ClientExtensionRuntime adds one migration warning per registered old module per startup. Disabled candidates are not instantiated for diagnostics; repeated Start does not duplicate warnings. The Common/server Register interface is not marked Obsolete.
- Trusted managed metadata inspection recognizes the two known client contract bases by contract assembly identity; arbitrary foreign inheritance remains rejected. The frozen validator source/provenance was synchronized and all 14 inputs checked.
- Maintained Chat/Inventory/Trade/Store/LegacyAdapter and Example/Playtest already use Compose; bilingual author guides and branch README examples now use the new client entry. English/Chinese design notes record deprecation and removal gates.

客户端兼容源适配器及其 Register 带 Obsolete，旧二进制继续使用普通注册表；客户端运行时对实际登记旧模块记录单次启动迁移提示，禁用模块不为诊断构建，重复 Start 不重复提示。共享/服务端 Register 不标弃用。静态元数据限定可信契约基类，验证器 14 个来源同步检查通过。维护模块/示例均已 Compose，双语指南、README 示例和设计说明同步。

## Version and remaining release gates / 版本与后续发布门槛

Composition contract: client abstractions 1.9. First planned stable deprecation: host 0.9.8. Intended hard removal: host 1.0 / abstractions 2.0, conditional on maintained independent RedPacket/TalentTrade migration, matching immutable new artifacts/catalog/resources, rollback/ownership checks and game acceptance. Both independent repositories still have direct Register entries at inspection time; their migration is scheduled future work, not claimed complete. Shared server registration is outside this removal.

组合契约 1.9；首个稳定弃用版本计划 0.9.8，硬移除计划宿主 1.0 / 抽象 2.0，须独立插件迁移、匹配的新发布资产/目录/资源、回退/所有权核查及游戏验收通过。检查时独立红包和人才贸易仍直接 Register，不宣称已迁移；服务端入口不在移除范围。没有提升或发布宿主版本。

The user-directed F4 closure is a development checkpoint, not a declaration that every release/game check passed or that the old entry was removed. Final binary distribution freeze and hard removal still require these recorded gates.

F4 按用户决定作为开发检查点关闭，不表示全部发布/游戏核查通过或旧入口已经移除。最终二进制分发冻结与硬移除仍须遵守记录的门槛。

## Validation already performed / 已完成验证

```sh
dotnet build Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/ClientCompositionRuntimeTests/runtimeconfig.json Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py refresh --source-root .
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
```

Client composition build: zero errors/warnings; actual net472 harness passes 69 assertions on both .NET and Mono, including new/old bridge, enabled-only one-time warnings, passive metadata, disabled zero-instance and unchanged server contract checks. The harness also demonstrates the existing unguarded Autofac Dispose exception limitation; do not present it as repaired here. Validator refresh reports all 14 compared inputs valid. Managed runtime build: zero errors, seven SDK/framework/obsolete warnings; its executables were not run in this H turn. No final full-solution build or new package was made for H, and no new game results were supplied. The previous F4-G cache-fix ZIP predates H and must not be described as containing H.

客户端窄构建零错误/警告，真实 net472 回归在 .NET 与 Mono 均通过 69 项；既有裸 Autofac Dispose 异常限制仍由测试展示，不宣称本批修复。验证器刷新检查 14 个来源；Managed 双目标构建零错误、七项警告，本轮未执行其运行时程序。H 未做最终全量构建/新包，也没有新增游戏结果；此前 F4-G 缓存修复 ZIP 早于 H，不包含 H。

Before distributing H, perform the final full build/artifact verification and game smoke for official Compose modules plus legacy client plugins, disabled entries, reconnect and shutdown. Closure does not certify persistence/ownership safety from compilation alone.

分发 H 前仍需最终全量构建、输出核对和 Compose/旧插件、禁用、重连/停止的游戏核查。阶段关闭不等于仅凭编译认证持久化/所有权安全。

## Scope resolution / 范围处理

The F4-F2c whole-mod proposal is cancelled; the legacy code is not removed in this documentation turn. Theme selection and Store theme downloads move to [future evaluation](Theme-Store-Future-Evaluation.md). The original boundary audit remains historical evidence; cancelled/deferred findings are not falsely labelled repaired. RedPacket optimization remains deferred. Await the user's next instruction; do not automatically enter F5.

取消 F4-F2c 整模组方案，本次文档更新不删除旧代码；主题与商店下载主题进入后续评估。旧边界审查保留为证据，取消/延期的发现不伪标已修复。红包优化仍暂缓；等待用户下一步，不自动进入 F5。

## Dev checkpoint validation / dev 保存点核查（2026-10-08）

用户随后明确要求先将 F4 相关工作提交 dev，F5 演练停止；原 dev 分支不变，不推送。本次补做最终全量构建与输出复核，覆盖先前 H 缺失的分发编译门槛：零错误、六项既有 SDK/obsolete 警告；31 项输出检查和 14 个验证器来源检查通过。没有新增游戏结果，也未移除旧客户端入口。

The later user request authorizes a dev commit of F4-related work and stops F5 rehearsal. Final full build now passes with zero errors and six existing SDK/obsolete warnings; 31 artifact checks and 14 validator sources pass. This supersedes the earlier unperformed H full-build/package gate, without claiming game acceptance or old-entry removal. No push.

Commands run:

```sh
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
python3 /tmp/phinix-f4-commit-artifacts.py
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
```

Checked package: `/tmp/phinix-rework-f4-dev-checkpoint-20261008.zip`

SHA-256: `79728c7448338a97508281e6b786ee970e873121756c671c153e5a97c9cbec5d`

Source/test/documentation checkpoint excludes IDE files, output, game DLLs, generated fixture binaries, unrelated planning drafts and the deferred RedPacket optimization plan. No production persistence or protocol change is added in this commit preparation. Compact schema-2 installation journal compatibility limits from Store-Install-Flow-Review.md remain: recover pending transactions with a compatible client before rolling back to an older version.

提交仅包含相关源码、测试和文档，排除 IDE/输出/游戏 DLL、生成 fixture 二进制、无关草稿和暂缓的红包优化计划。本次提交准备未新增持久化或协议改动。此前 schema 2 安装事务回退限制继续有效：回退旧版前先由兼容客户端完成/恢复未决事务。
