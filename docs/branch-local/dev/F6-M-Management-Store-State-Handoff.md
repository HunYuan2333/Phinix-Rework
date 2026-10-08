# F6-M — Management / Store state synchronization

Date: 2026-10-08. Implementation, automated verification and user acceptance complete (2026-10-08). F7 remains unstarted. The original implementation batch retained paused Index/Example main merges and Common/Server pins. The user subsequently resumed Index/Example delivery; see the acceptance checkpoint below.

## Shared command and state boundary

The client host owns `IClientExtensionControlService`. Store activation borrows it optionally; older compatible host integrations without that service retain their prior Store path. The same host coordinator wraps the existing managed management/installation services. It exposes immutable package inventory, saved module selections, current Active/Failed lifecycle facts, a shared revision and a command-busy flag. All three management surfaces (extension cards, package/module management and mod settings) and Store use this boundary. Views do not call one another.

Inventory reads and package/installation operations retain their existing background controllers. Main-thread Draw/Poll consumes the published snapshot; module settings writes and game/lifecycle capture require the main thread. No new background callback reads Verse settings. An unchanged inventory read does not bump the revision and trigger another refresh indefinitely.

Package UI projections distinguish current runtime from next-start intent, partial module selection, pending restart/removal and load failure. Assembly-loaded does not mean module-active. Both package views prefer the shared latest inventory over their previous controller cache. Counts mean **saved module choices**, not a promise of dependency validation or activation; existing gates and startup discovery remain authoritative.

## Explicit operation semantics

- Package enable/disable changes package intent only and preserves all module selections.
- A module command changes that module's saved flag only. It does not enable its package or reset other modules.
- Store Enable can restore the sole disabled module of an already-enabled single-module package. This writes only module settings.
- A multi-module package with all modules disabled, or a disabled package with all modules disabled, routes to management for explicit module selection. A later package-enable is a separate operation.
- The module overview/settings checkbox is gated when its owning package is disabled, pending removal or unavailable, and explains why. Package management can still prepare module selections before package enable where existing ownership gates permit it.
- There is no combined write spanning package intent and module settings, hence no new partial two-store commit protocol. Package validation/dependency/receipt/recovery logic is delegated unchanged to the existing runtime.

Shared command serialization rejects overlapping writes. Module menus carry the revision captured when opened; an obsolete callback is rejected instead of overwriting later selections. Package mutations retain the runtime's complete expected-record checks. A refresh racing newer settings cannot replace the published state with an obsolete snapshot.

## Failure and compatibility behavior

A package mutation invalidates inventory until refreshed. An inventory read failure yields unknown state and rejects module commands until recovery. If the initial management read fails after startup succeeds, startup discovery is retained and management can retry; it does not discard the successfully started runtime.

Module save exceptions restore the prior settings flag and attempt to persist that restoration. A failed restoration is reported as an aggregate diagnostic; the UI does not claim success and the inventory becomes unknown. Persistence behavior still depends on the game's existing settings writer: an error it only logs without throwing cannot be identified reliably by this coordinator. Actual disk-failure recovery remains a game/manual check.

Existing settings keys, package desired-state/receipt/journal formats, namespaces and protocol IDs remain unchanged. No save migration, hot unloading, new SDK distribution, forced signing or plugin feature fix is introduced. RedPacket summary/stack fixes remain deferred to their independent plugin update.

## Verification

- Full client solution: `dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1` — successful, zero errors. Existing target/obsolete API warnings and offline NuGet advisory warnings remain.
- Plugin Store runtime harness: 1000 assertions passed, including the new F6-M command/state checks and settings-write error classification.
- Actual net472 Store composition harness: 109 assertions passed; host controls are borrowed by both views, and their absence is accepted.
- Actual net472 client composition harness: 69 assertions passed.
- Final artifact/XML checks: 50 passed using a Python equivalent of the repository artifact checks (PowerShell is unavailable locally), plus current host/abstractions/Store byte comparisons and localized state-key checks. No game DLLs or optional business plugins were bundled.
- Automated tests cover shared revision stability, partial/package selections, single-module restore eligibility, stale callbacks, concurrent operations, ownership gates, inventory/write failure recovery, read races, current lifecycle facts and post-restart disable projection. They do not substitute for game UI or real filesystem-failure acceptance.

## Game acceptance / 游戏验收

Use the complete freshly built `Output/phinix-rework` folder, restarting the game to load its DLLs. Existing saves/configuration should remain usable.

1. 单模块插件（示例、红包等）：扩展管理停用模块，切到商店；应显示下次启动停用、当前仍运行和待重启。商店启用后切回管理，应恢复该模块选择。反向再测试一次。
2. 商店停用整包后，模块总览应显示受整包限制，不能靠模块复选框伪装启用；重新启用整包应保留此前模块选择。切页和刷新不应恢复旧显示。
3. 多模块插件：只停用其中一个，两页应显示部分停用；整包停用再启用不得重置这个选择。全部模块停用时商店引导到管理，不会自动启用所有模块。
4. 包停用叠加模块停用：先明确恢复需要的模块，再启用整包；两步分别保存，取消任何一步不应改变另一项。
5. 打开模块菜单后在另一入口改状态，再点击旧菜单；应提示状态已变化。安装/管理操作进行时，其他入口不应接受并发写入。
6. 重启后当前运行应与已应用的选择一致，已生效的停用不应继续显示待重启。加载失败应显示失败，不能因 DLL 已加载显示运行中。
7. 商店安装/卸载、切页刷新、旧存档读入仍正常；卸载实际生效及重新安装沿用现有重启/事务规则。

Do not deliberately damage live save/configuration files to simulate failures. Use a disposable test configuration if validating storage/read failures.

## User acceptance and remote delivery checkpoint

On 2026-10-08 the user confirmed acceptance and said audit issues are resolved. This records collective acceptance, not invented per-step evidence. Index a77c68d and Example 2235353 are now on remote main after resumed authorization. Their merged builds/static checks pass; published catalog and plugin ZIPs remain unchanged. The requested post-publication Store game check is pending; F7 remains unstarted.
