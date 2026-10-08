# F4-E2 Trade activation lifetime / Trade 激活生命周期

## Status / 状态

User accepted E1 on 2026-10-08 and authorized E2. E2 implementation is complete as a candidate; game acceptance remains pending. No commit/push; preserve existing parallel work. Next batch is F4-F Store composition after Trade review. Do not infer detailed F4-D manual acceptance from continuation.

用户于 2026-10-08 接受 E1 并授权 E2。E2 实现完成为候选，游戏验收待完成；未提交/推送，保留并行修改。Trade 核查后进入 F4-F Store 组合迁移。不能由继续推进推断 F4-D 每项游戏核查通过。

## Ownership and behavior / 所有权与行为

- Passive core scope remains the E1 scope. A separate private activation scope borrows actual host services, Inventory APIs and core services, and owns codec/source objects, their registration lease, delivery helper, default behavior and connection subscription lease. No borrowed host/Inventory service is disposed. The new source presenter is explicitly included by its existing source file; the new lifetime source is listed in the classic client project.
- Activation retains original theme/host/Inventory lookup, UI initialization, extension-codec injection, negotiated snapshot request, accepting-trades synchronization and default-behavior Start order. Repeated activation/Start is idempotent. A failed activation rolls back both scopes; constructor-acquired tokens and subscriptions are explicitly rolled back when their later acquisition fails.
- Shutdown invalidates module callbacks and public mutation access first, then independently attempts behavior/UI stop and both scope disposals. Cleanup attempts all event/token releases, including after one failure. Direct cleanup failures aggregate; scope release failures use the existing composition-factory reporter, whose logging failure cannot interrupt subsequent releases.
- Default behavior and UI work capture a generation before queueing. Stop invalidates it before unsubscription; old queued completion, retry or window work is ignored even after a behavior restart. Default waiting/pending memory is cleared as before for completions, now also for creation waits. Module compatibility/user/settings callbacks are tied to their activation generation.
- Public Trade facade mutations reject use after stop, and stopped UI cannot queue new work or drop items. UI borrowed dependencies are cleared on disposal. Existing windows cannot continue sending through a stopped facade.

中文：保留 E1 被动作用域，增加插件私有激活作用域，借用真实主机/库存/核心服务，持有 codec、来源提供者、注册令牌、交付辅助、默认行为和连接订阅。保持正常启动顺序；重复启动幂等，部分启动失败回滚作用域、已取得的令牌和订阅。停止先使回调及公开写操作失效，再分别停止和释放；单项失败不跳过其余清理，通过原组合工厂报告释放异常。行为/界面排队回调记录代次，停止使旧交付/重试/开窗任务失效；既有 pending 完成内存清理保留，增加创建等待清理。停止门面拒绝写操作，界面不再排队或投放物品；不释放借用服务。

No changes to server/protocol, capability strings, public contract versions, normal API identity, payload conversion, reservation resolution, deposit receipt IDs, journal/save formats or delivery-before-ACK rules. Normal completion delivery is still handled on the main thread and ACK still goes through the command pipeline only after success. Stop does not assert success or acknowledge discarded work. Existing server completion replay/receipt idempotence remains the recovery mechanism; this batch does not add a new durable pending queue.

无服务端/协议、能力字符串、公开契约版本、正常 API 身份、载荷转换、预留处理、存入回执 ID、journal/存档格式或交付后确认规则变更。完成交付仍在主线程，成功后 ACK 仍经命令管线发送。停止不宣称成功、不确认被丢弃的任务；继续依赖既有服务端完成重放和回执幂等，本批没有新增持久化 pending 队列。

## Validation / 验证

- Actual E1 DLL saved before edits at `/tmp/phinix-trade-f4e2-before/TradeExtension.Client.dll`. Baseline runs use an isolated copy of the entire probe directory with that DLL substituted; the probe asserts the loaded path. This avoids .NET dependency resolution silently selecting the current DLL when `LoadFrom` requests a same-identity DLL elsewhere.
- Same executable on .NET 10 and Mono: 35 baseline and 112 candidate assertions. Four facts identical: eleven API mappings, capabilities, priority/pass-through and normal activation (seven host subscriptions, one inventory availability subscription, two snapshot requests plus one completion ACK, three accepting-setting updates).
- Actual module `Activate` exercised with managed host/theme/Inventory fixtures. Covers repeated Activate/Start, stopped and restarted behavior generation, stopped UI/facade, captured stale connection callback, source acquisition failure, missing host/Inventory dependency, connection/UI/behavior subscription failure after an event was added, and simultaneous subscription/token release failures. All acquired tokens and all fixture subscriptions are released; borrowed APIs are not disposed.
- Actual completion callback with an empty/idempotent delivery waits for dispatcher drain before ACK. An actual completion queued before stop emits no ACK afterward. This does not exercise physical items or native game delivery.
- Original 10 legacy runtime scenarios pass with baseline/candidate DLLs on both runtimes: authoritative response, rejection, send failure and uncertainty, all-or-nothing encoding/offer handling, snapshot and legacy item shape (plus existing chat history scenario). Existing source was not changed in E2.
- Full build: 0 errors, 10 existing warnings; narrow probe: 0 errors, 5 NuGet lookup warnings; legacy harness: 0 errors, 12 existing warnings. Mono emits the known missing Unity.Burst attribute warning from the compile-only game reference. Distribution: 31 required-file checks, unique runtime dependencies, no game/Unity DLLs, exact compiled host/Utils/Store/Inventory/Trade bytes. Diff check passes with existing CRLF notices.

中文：旧版在独立目录运行并断言加载路径，避免同身份程序集被 .NET 自动解析到新版。两种运行时均通过基线 35/候选 112 项断言；十一 API、能力、优先级/透传及正常激活事实完全一致。测试调用真实模块 Activate，以托管主机/主题/库存夹具覆盖重复启动、代次、旧回调、停止门面、注册/依赖缺失/连接/界面/行为订阅失败及同时两项释放失败。真实空完成回调证明主线程处理前无 ACK、停止后旧完成无 ACK。原十个协议场景在实际新旧 DLL/两种运行时通过。整包 0 错误/10 警告、窄编译 0 错误/5 警告、协议 harness 0 错误/12 警告、发行包 31 项检查通过。

### Limits and risk / 限制与风险

Managed activation tests use fake colors/transport and do not execute native game rendering, real sockets, physical item conversion/deposit/drop or server replay. The completion test uses empty items and cannot certify nonempty ownership. In-flight work already executing before stop is not transactionally cancelled. Broken third-party event accessors/tokens may fail before actually removing their resources; cleanup attempts all releases and reports failure, but cannot force a foreign implementation to release. Stop does not undo committed delivery or authorize uncertain reservation restoration. Preserve manual quantity, ACK/reconnect and Inventory menu/new-game checks.

测试使用假颜色/传输，不执行原生绘制、真实网络、实际物品转换/存入/投放或服务端重放。空完成测试不能证明非空物品归属。停止不会事务性撤销已在执行的工作。第三方事件访问器/令牌可能在真正释放前抛错；这里尝试全部释放并报告，但无法强制外部实现释放。停止不回滚已提交交付，也不授权恢复不确定的预留；必须保留数量、ACK/重连和库存菜单/新游戏人工核查。

## Commands / 命令

From repository root:

```sh
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/TradeCompositionRuntimeTests/TradeCompositionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/TradeCompositionRuntimeTests/runtimeconfig.json Tests/TradeCompositionRuntimeTests/bin/Release/net472/TradeCompositionRuntimeTests.exe
mono Tests/TradeCompositionRuntimeTests/bin/Release/net472/TradeCompositionRuntimeTests.exe
dotnet exec --runtimeconfig Tests/TradeCompositionRuntimeTests/runtimeconfig.json /tmp/phinix-trade-f4e2-before/probe/TradeCompositionRuntimeTests.exe --legacy
mono /tmp/phinix-trade-f4e2-before/probe/TradeCompositionRuntimeTests.exe --legacy
dotnet build Tests/LegacyTradeRuntimeTests/LegacyTradeRuntimeTests.csproj --configuration Release -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/LegacyTradeRuntimeTests/runtimeconfig.json Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
mono Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
python3 /tmp/phinix-f4e2-package.py
git diff --check
```

Isolated baseline preparation copied `Tests/TradeCompositionRuntimeTests/bin/Release/net472/` and replaced only its Trade DLL with the saved baseline. Legacy commands also ran with a temporary test-bin Trade DLL substitution, restored in `finally`. No generated/fixture/game DLL is to be committed. PowerShell unavailable; artifact validation uses equivalent Python checks. Evidence logs: `/tmp/phinix-f4e2-*`.

基线准备复制整个探针输出目录，仅替换为保存的旧 Trade DLL；协议 harness 同样对照旧 DLL，临时替换后 finally 恢复。生成物/夹具/游戏 DLL 不提交；PowerShell 不可用，Python 等价发行检查。日志见 `/tmp/phinix-f4e2-*`。

## Package and game steps / 包与游戏步骤

ZIP: `/tmp/phinix-rework-f4e2-trade-lifetime-20261008.zip`
SHA-256: `f08740e3e9b615638690b46a5462bcccfcdd14bfff767c5d0c513210367a6a0e`

1. Log in, open Trade, change accepting-trades and reconnect. Verify no duplicate list events/windows and correct negotiated snapshots.
2. Trade a small known quantity with a second player: modify offer, reject/cancel, then complete. Check both parties' quantities and configured Inventory/direct delivery; success must follow authoritative confirmation.
3. During delayed completion, disconnect/reconnect and check replay/receipt behavior: no duplicate item delivery, no premature ACK, pending reservations remain for reconciliation.
4. Disable Trade through normal management and restart; re-enable/restart and check one codec/source registration and normal behavior. Stop/close the session with pending UI work; no later delivery/window from the stopped plugin.
5. Retain F4-D A/B/menu/new-game verification; compare the same path with E1 when investigating regressions.

1. 登录、打开交易、修改接收交易开关并重连，核对快照及无重复列表事件/窗口。
2. 与另一玩家交易少量已知数量：修改报价、拒绝/取消再成功，核对双方数量及库存/空投方式，成功必须来自权威确认。
3. 完成延迟时断线重连，核对重放/回执：无重复交付、无提前 ACK，不确定预留保留待对账。
4. 正常管理流程禁用 Trade 重启、再启用重启，核对单次 codec/来源注册；停止/退出时已有排队工作不能随后交付或开窗。
5. 保留 F4-D A/B/菜单/新游戏核查；出现差异用 E1 同路径对照。
