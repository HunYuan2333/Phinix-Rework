# Store cache staging path fix / 商店缓存暂存路径修复

2026-10-08, dev. User requested fixing the cache-path omission identified in the referenced ManagedStoreFailed investigation. Implemented, automatically validated, game/Windows acceptance pending. No commit/push; F4-G and all parallel changes retained.

用户要求修复排查中发现的缓存路径遗漏。本批实现与自动验证完成，原 Windows 环境和游戏验收待完成；未提交/推送，保留 F4-G 与并行修改。

## Evidence and implementation / 证据与实现

Player (1).log line 2698 reports Reading / ManagedStoreFailed / DirectoryNotFoundException after successful metadata requests. The trace does not identify the failing filename, so Windows path length is a supported hypothesis rather than a proven exclusive root cause. The previously completed compact installation staging fix did not cover repository catalog caching.

日志 2698 行显示读取目录阶段抛 DirectoryNotFoundException；缺少具体失败文件名，路径过长是有依据的嫌疑，不能排除其他文件系统原因。既有安装暂存路径修复没有覆盖目录缓存。

Both `RepositoryCache.Stage` and `ManagedRepositoryCache.Stage` now use `RepositoryCacheWrite.TemporaryPath(directory)` and create `<full 32-character GUID>.tmp` beside the original target. The reported directory length 204 produces a 241-character temporary path, versus 263 with the prior `managed-catalog.cache.<GUID>.tmp` name. No per-mod workaround, alternate cache root, truncation of identity/random IDs, or long-path platform toggle is introduced.

两条缓存路径共用短临时文件名：同目录中的完整 32 位随机 ID 加 .tmp。报告的 204 字符目录对应暂存路径由 263 缩短到 241；没有针对特定模组的例外、切换缓存根目录、截断身份/随机 ID 或依赖系统长路径开关。

Permanent filenames, repository identity isolation and binary formats are unchanged. Existing caches remain readable without migration or deletion. Existing validation/link checks, CreateNew, Flush and File.Replace/File.Move are retained; no delete-then-move fallback. Cancellation and cleanup still touch only the operation's own temporary file. Installation records/journals, recovery, protocol and item ownership are unchanged. Arbitrarily longer user roots can still exceed platform limits; this fix addresses the unnecessary staging-name expansion.

正式文件名、仓库隔离及缓存二进制格式保持不变，旧缓存不需要迁移或删除。验证、链接检查、独占新建、刷盘与同目录替换流程保留，不增加先删除后移动的回退。取消和清理只影响当前操作自己的暂存文件；安装凭据/事务、恢复、协议及物品所有权未改。任意更深用户路径仍可能超出平台限制，本修复消除多余的暂存文件名增长。

## Validation / 验证

```sh
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
python3 /tmp/phinix-cache-fix-package.py
git diff --check -- Extensions/PluginStore/Client/RepositoryCache.cs Extensions/PluginStore/Client/ManagedRepositoryCache.cs Tests/PluginStoreRuntimeTests/CacheStagingTests.cs Tests/PluginStoreRuntimeTests/RepositoryTests.cs Tests/PluginStoreRuntimeTests/RepositoryAdapterTests.cs docs/branch-local/dev/Store-Cache-Path-Fix-Handoff.md
```

- Runtime regression: 968 assertions passed, including 22 added assertions covering deterministic reported path lengths and actual staging through both production cache implementations, concurrent unique names, original cache preservation, cancellation, cleanup isolation, unrelated old temporary files and successful whole-bundle replacement. Existing corrupt-cache, failed-commit and adapter checks continue passing.
- Narrow build: zero warnings/errors. Full build: zero errors, two pre-existing protobuf SDK warnings. net472 client sources compile; this console regression runs on .NET 10/Linux and does not reproduce Windows MAX_PATH enforcement or certify game behavior.
- Frozen validator snapshot: all 14 sources checked, no refresh required. Artifact checks: 31 required files, unique runtime bytes, game DLL exclusion, translation keys, ZIP integrity and compiled module byte comparison passed. PowerShell is unavailable; the Python equivalent was used.
- Scoped diff checks passed; git status reviewed. Build output/test logs remain uncommitted.

商店回归 968 项通过，新增 22 项覆盖路径长度及两个实际缓存写入器的并行、取消、清理和替换行为。窄构建零警告/错误，全量零错误、两项既有 SDK 警告；net472 客户端编译通过，但 Linux/.NET 控制台测试不等于 Windows 或游戏验证。验证器 14 个冻结来源检查通过，无需更新快照。31 项输出文件及运行库/翻译/ZIP/模块字节检查通过；无 PowerShell，使用等价 Python 检查。已审查 diff/status，输出与日志不提交。

## Package and game check / 包与游戏核查

Package: `/tmp/phinix-rework-f4g-cache-fix-20261008.zip`

SHA-256: `8fff9435016e22882e90c21cb5287c9b86270b82f06ab1923bd478041c5b2512`

1. Replace the client package and restart RimWorld; keep the original affected user's save-data path and existing cache.
2. Open the store and refresh. Confirm catalog listings appear and Reading does not fail with a storage exception. Switch GitHub/CF and refresh again if both are available.
3. Restart and refresh once more to check persisted cache reuse. Install the RedPacket package as the end-to-end follow-up check; this is separate from catalog-cache write success.
4. If failure persists, provide the fresh Player.log with operation/exceptionType/causeType. Do not delete installation journals or ownership records as a cache troubleshooting step.

替换整包并重启，保留原出错用户的保存路径及缓存；刷新商店确认目录能显示，再测试 GitHub/CF 切换。重启后再次刷新核查旧缓存读取，并安装红包验证后续下载链路。若仍失败提供新日志；缓存排查无需删除安装事务或凭据。

This interruption does not close F4-H or the separately tracked ownership-boundary work.

本次插入修复不代表 F4-H 或独立边界事项已经关闭。
