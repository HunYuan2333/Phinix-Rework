# F4-D1 Inventory attachment and shutdown / 库存绑定与停止清理

## Scope / 范围

F4-C was accepted by the user on 2026-10-08 (no detailed individual game-test log). F4-D is split into D1 attachment/shutdown and D2 menu/reentry lifetime. This candidate completes D1 only; D2 remains pending.

用户于 2026-10-08 接受 F4-C；未提供逐项游戏测试日志。F4-D 分为 D1 绑定/停止清理和 D2 返回菜单/重新进入生命周期。本批仅完成 D1。

Queued activation/attachment work captures an activation generation. Shutdown invalidates it before resource cleanup, including reuse of the same module instance. Attachment, readiness and status check membership in the current game's component list, rather than relying on a stale static component pointer. Scribe-restored objects are supported. Cleanup attempts journal, Harmony, shell subscription and owned scope independently, clears references, then reports accumulated exceptions. Borrowed services/codecs remain externally owned.

排队的激活和绑定回调记录激活代次；停止先使旧回调失效，包括同一实例复用。绑定、可用性和状态依据当前游戏组件列表判断归属，支持 Scribe 恢复的实例。journal、Harmony、界面事件和作用域分别尝试清理，清空引用后汇总抛出异常；借用的服务和 codec 仍由原所有者管理。

Ledger algorithms, save fields, save-ID/path journal key, journal records, recovery approval, reservations, rollback and ownership rules are unchanged. Returning to menu still retains the journal lock until a subsequent valid attachment or shutdown; immediate release and old snapshot display cleanup belong to D2. Do not treat D1 as complete F4-D.

账本算法、存档字段、journal 标识/格式、恢复确认、预留、回滚和物品归属规则不变。返回菜单后 journal 锁仍保留到下一次有效绑定或停止；立即释放和旧快照显示清理属于 D2，不能宣称整个 F4-D 完成。

## Validation / 验证

- Actual F4-C baseline DLL preserved before edits; before/after facade API maps, capabilities/limits, inactive status and codec events identical on .NET 10 and Mono (24 baseline assertions, 62 candidate assertions). Candidate includes 10 new lifecycle assertions: current/other/no game membership, stale work after stop and same-instance reuse, injected unsubscribe failure, scope cleanup, repeated stop and borrowed ownership.
- Unchanged 13 inventory scenarios passed with both actual DLLs on both runtimes: reservations, grouping, component snapshot and journal/recovery. Only the test output DLL was temporarily substituted and restored.
- Full solution: 0 errors, 9 warnings (NuGet vulnerability lookup unavailable and existing protobuf target warning). Narrow composition build: 0 errors, 5 NuGet warnings. Mono's compile-only game references emit the pre-existing Unity.Burst attribute warning in the domain harness. No Unity/Harmony activation or in-game rendering was exercised.
- Distribution: 31 required-file checks, one copy of runtime dependencies, no game/Unity DLLs; packaged host/Utils/Store/Inventory match compiled bytes. `git diff --check` passes (existing CRLF notices).

中文：两种运行时的新旧实际 DLL 公开行为事实一致；候选 62 项断言通过，其中新增 10 项生命周期断言。原有 13 个领域场景在新旧 DLL 和两种运行时全部通过。整包编译 0 错误/9 警告，窄编译 0 错误/5 警告；发行包 31 项检查通过。未执行游戏内验证或实际 Harmony 激活，编译不能证明切档行为。

Commands run (repository root):

```sh
dotnet build Tests/InventoryCompositionRuntimeTests/InventoryCompositionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/InventoryCompositionRuntimeTests/runtimeconfig.json Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe
dotnet exec --runtimeconfig Tests/InventoryCompositionRuntimeTests/runtimeconfig.json Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe --legacy /tmp/phinix-inventory-f4d-before/InventoryExtension.Client.dll
mono Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe
mono Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe --legacy /tmp/phinix-inventory-f4d-before/InventoryExtension.Client.dll
dotnet build Tests/InventoryRuntimeTests/Portable/InventoryRuntimeTests.Portable.csproj -c Release --no-restore -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/InventoryRuntimeTests/runtimeconfig.json Tests/InventoryRuntimeTests/Portable/bin/Release/net472/InventoryRuntimeTests.exe
mono Tests/InventoryRuntimeTests/Portable/bin/Release/net472/InventoryRuntimeTests.exe
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
python3 /tmp/phinix-f4d1-package.py
git diff --check
```

The two domain run commands were also run against the baseline DLL by temporary test-bin replacement, with restoration in `finally`. Packaging uses equivalent Python checks because PowerShell is unavailable.

领域测试的两个运行命令还通过临时替换测试输出 DLL 对照运行旧版，并在 finally 中恢复。PowerShell 不可用，发行包使用等价 Python 检查。

## Candidate and game steps / 候选包与游戏步骤

ZIP: `/tmp/phinix-rework-f4d1-inventory-lifetime-20261008.zip`
SHA-256: `286a13946e585e351a5bef8fa4eaf3597992afe974b231099536d9ccfc37e8bc`

1. Back up saves; load saved game A, open inventory, deposit/extract a small test amount, save and check quantities.
2. Without restarting, load saved game B with different inventory; confirm it displays B and operations affect only B.
3. Return to menu, reenter A and verify A's quantities and existing recovery prompt behavior; never delete journals to bypass a fault.
4. Restart and reopen A/B; check reservation/restoration and extraction reconciliation remain consistent. Check logs for attachment/journal/shutdown failures.

1. 备份存档，进入已保存的 A 档，开库存，少量存入/取出，保存并核对数量。
2. 不重启切换到库存不同的 B 档，确认显示 B，操作仅影响 B。
3. 返回菜单再进入 A，核对 A 数量与既有恢复提示；不要删除 journal 绕过故障。
4. 重启检查 A/B，核对预留/恢复和取出对账行为，留意绑定、journal 和停止异常。

Persistence and ownership risk: the new membership check must match RimWorld's live/restored component list. This requires the manual two-save check above. No protocol or persistence-format migration. Parallel edits and generated assets were preserved; no commit/push in this batch.

风险：新增归属判断依赖 RimWorld 当前/恢复后的组件列表，需要上述双存档游戏核查。无协议或持久化格式迁移。保留并行修改与生成物，本批未提交/推送。
