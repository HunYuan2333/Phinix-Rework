# F4-D2 Inventory menu/reentry lifetime / 菜单与重新进入生命周期

## Changes / 改动

The user authorized continuing D1; no detailed game acceptance log was supplied. D2 implements the remaining menu/reentry boundary. Inventory's own Harmony lease observes `Root.Update` on the main thread. Leaving a game's component list detaches its journal, clears in-memory ledger/display/fault state and notifies listeners once. The next initialized current component can attach even if its earlier callback ran before the game became current. No per-frame queued actions, journal writes or logging occur for an unchanged binding. New games clear the prior save path and remain unavailable until saved. Shutdown also clears stale ledger display but retains the same ledger object and borrowed registration ownership.

用户授权在 D1 后继续，未提供逐项游戏验收日志。D2 通过库存插件自身的 Harmony 激活/停止机制观察主线程 `Root.Update`。离开当前游戏组件列表后释放 journal、清空内存账本/显示/故障状态并通知一次；当前组件初始化完成后可补上过早执行而错过的绑定。正常绑定每帧不会排队、写 journal 或打日志。新建游戏清空旧存档路径，首次保存前不可使用库存。停止也清空旧账本显示，保留原账本对象及借用注册项的所有权。

Recovery approval/rejection now require the active plugin, game main thread and matching live component/save identity. Ledger algorithms, save fields/IDs, journal path keys/record format, reservations, extraction reconciliation, server ACK rules and recovery approval semantics remain unchanged. Clearing display does not delete/rewrite the durable journal or its old save snapshot. GameComponent remains owned by RimWorld; no host implementation dependency or public API/version change.

恢复批准/拒绝现在要求插件活跃、游戏主线程及当前组件/存档身份匹配。账本算法、存档字段/ID、journal 路径键/记录格式、预留、取出对账、服务端确认规则及明确批准恢复规则不变。清空显示不会删除或改写持久化 journal 或旧存档快照。GameComponent 仍归 RimWorld 所有，没有新增主机实现依赖或公开 API/版本变化。

## Evidence / 验证

- Actual D1 DLL preserved before editing: `/tmp/phinix-inventory-f4d2-before/InventoryExtension.Client.dll`. Before/after API maps, capabilities/limits, inactive status and codec-event facts match on .NET 10 and Mono. Baseline probe: 24 assertions; candidate: 77, including 15 new D2 assertions.
- Real journal fixture verifies: same-game lock retention, other-game/menu lock release, cleared snapshots, single/idempotent notifications, unchanged record bytes, reentry pending recovery, new-game path reset and shutdown clearing.
- Existing 13 domain scenarios unchanged; passed with actual before/after DLLs on both runtimes. Only test-bin DLL substitution was temporary and restored in `finally`.
- Full solution: 0 errors, 9 warnings. Narrow composition: 0 errors, 5 NuGet vulnerability-lookup warnings. 31 distribution file checks pass; no game/Unity reference DLLs shipped, unique runtime files, exact compiled host/Utils/Store/Inventory bytes.
- `git diff --check` passes with existing CRLF notices. No commit or push; parallel edits retained.

中文：新旧实际 DLL 的公开行为事实在两种运行时一致；基线 24 项、候选 77 项断言通过（本批新增 15 项）。真实 journal 文件证明锁释放、内存快照清空和持久化记录保留；原 13 个领域场景在新旧 DLL/两种运行时均通过。整包 0 错误/9 警告，窄编译 0 错误/5 警告，发行包 31 项检查通过。未提交/推送，保留并行修改。

### Limits / 限制

These are game-independent fixture tests, not actual Unity frames, GUI/Harmony activation or in-game save/load. Mono lacks compile-only `Assembly-CSharp-firstpass` and reports the existing missing Unity.Burst attribute; its recovery-button fixture checks inactive rejection, while .NET additionally checks active/no-game rejection. Both exercise menu/switch cleanup via the same production method with managed fake game lists. Actual Root.Update ordering, initialized-component reattachment and new-game callback timing need manual game checks. Do not infer that all lifecycle branches were exercised identically on Mono.

这些是独立于游戏的夹具测试，没有执行 Unity 帧、界面、Harmony 激活或实际存读档。Mono 缺少参考集中的 `Assembly-CSharp-firstpass`，也报告既有 Unity.Burst 属性缺失；恢复按钮夹具在 Mono 核查停用状态拒绝，在 .NET 还核查活跃但无游戏时拒绝。两者均以托管游戏列表调用同一个生产方法核查菜单/切档清理。实际 Root.Update 顺序、初始化后重新绑定和新游戏回调时机仍需游戏核查，不能称 Mono 全部分支与 .NET 完全相同。

## Commands / 命令

Run from repository root:

```sh
dotnet build Tests/InventoryCompositionRuntimeTests/InventoryCompositionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/InventoryCompositionRuntimeTests/runtimeconfig.json Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe
dotnet exec --runtimeconfig Tests/InventoryCompositionRuntimeTests/runtimeconfig.json Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe --legacy /tmp/phinix-inventory-f4d2-before/InventoryExtension.Client.dll
mono Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe
mono Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe --legacy /tmp/phinix-inventory-f4d2-before/InventoryExtension.Client.dll
dotnet build Tests/InventoryRuntimeTests/Portable/InventoryRuntimeTests.Portable.csproj -c Release --no-restore -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/InventoryRuntimeTests/runtimeconfig.json Tests/InventoryRuntimeTests/Portable/bin/Release/net472/InventoryRuntimeTests.exe
mono Tests/InventoryRuntimeTests/Portable/bin/Release/net472/InventoryRuntimeTests.exe
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
python3 /tmp/phinix-f4d2-package.py
git diff --check
```

Domain commands were run with both DLLs via temporary test-bin replacement. PowerShell unavailable; packaging script performs equivalent artifact checks. Logs: `/tmp/phinix-f4d2-{build,full-build,after,after-mono,before,before-mono}.log`, domain `{after,before}-{net10,mono}` logs.

领域命令通过临时替换测试输出 DLL 对照新旧版本。PowerShell 不可用，发行脚本执行等价检查。

## Game handoff / 游戏核查

ZIP: `/tmp/phinix-rework-f4d2-inventory-lifetime-20261008.zip`
SHA-256: `1087267c076c9c94f3497c23600a105c36f508deadc01c5966509011fd55c754`

1. Back up saves; load saved A, deposit/extract a small amount, save, note its quantities.
2. Without restarting, load saved B with different inventory; only B data/operations should appear.
3. Return to menu, reenter A. Verify A quantities and expected recovery prompt. For an unsaved journal transaction, returning to menu must preserve recovery rather than silently discard/apply it.
4. After visiting A, start a new game. Inventory must remain unavailable until its first save; no A entries or recovery should appear.
5. Restart and reopen A/B; verify reservations/extraction reconciliation and no new journal-lock/shutdown failures. Do not delete journals to bypass faults.

1. 备份存档，进已保存 A 档，少量存入/取出并保存，记录数量。
2. 不重启读取库存不同的 B 档，确认仅显示和操作 B 的库存。
3. 返回菜单再进入 A，核对数量和既有恢复提示；若交易后未存档就返回，journal 应保留待确认恢复，不能静默丢弃/应用。
4. 访问 A 后新建游戏：首次保存前不可使用库存，不出现 A 的物品或恢复记录。
5. 重启再次检查 A/B、预留/取出对账，以及 journal 锁/停止异常；不要删除 journal 绕过错误。

F4-D implementation is ready for manual acceptance; F4-E Trade composition is next after lifecycle review. No protocol or persistent format migration. Compatibility risk is game callback ordering and live/restored component membership; successful fixture/build checks do not eliminate this manual gate.

F4-D 实现等待游戏验收，生命周期核查后进入 F4-E Trade 组合迁移。无协议或持久化格式迁移；风险在游戏回调顺序与实时/恢复组件归属，夹具与编译通过不能替代游戏核查。
