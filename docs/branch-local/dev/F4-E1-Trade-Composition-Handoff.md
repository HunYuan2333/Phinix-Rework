# F4-E1 Trade passive composition / Trade 被动组合迁移

## Scope / 范围

The user authorized continuing after D2; do not infer detailed F4-D in-game acceptance. Split F4-E into E1 passive construction and E2 activation/connection/inventory-registration lifetime. E1 migrates Trade to the ordinary `ClientExtensionModule.Compose` path with a private scope. It owns seven implementation registrations: pipeline, framework service, legacy adapter, domain facade, UI context, tab and shared settings/migration provider. Interface/concrete aliases resolve to the same instance. The log delegate is borrowed and retains the original activation-time host logger forwarding.

用户授权 D2 后继续，不能推断 F4-D 每项游戏核查通过。F4-E 分为 E1 被动构造和 E2 激活/连接/库存注册生命周期。E1 通过普通 `ClientExtensionModule.Compose` 创建私有作用域，持有 pipeline、框架服务、Legacy 适配器、领域门面、界面上下文、页签、共享设置/迁移提供者共七项实现注册。接口与具体类型别名使用同一实例；日志委托借用，保留原激活时主机日志转发方式。

Pipeline and framework-service constructor overloads delegate to their old constructors, retaining Unknown compatibility and null user directory until activation. Eleven published APIs, capability/command owners, priority and registration order remain the same. Theme reload, Inventory API lookup/codec registration, connection subscription, snapshot request, default behavior construction and Start/Stop remain in their original activation path for E2. `Activate` and all source from `EnsureActivationServices` to the file end match the saved pre-change source after newline normalization.

新增 pipeline 和框架服务构造函数仅转发到原构造函数，激活前仍使用 Unknown 兼容模式和空用户目录。十一项公开 API、能力/命令所有者、优先级和注册顺序不变。主题刷新、库存 API 查询/codec 注册、连接订阅、快照请求、默认行为构造及 Start/Stop 仍沿原激活路径，留待 E2。`Activate` 和从 `EnsureActivationServices` 到文件结尾的逻辑与变更前源码一致（仅规范换行比较）。

The trade list previously subscribed to eight UI/service events without a release path. E1 adds idempotent `Dispose` to the list/tab, released by the scope even after partial API publication. Shutdown guarantees scope disposal and clears core references in `finally`. E2 still must audit individual activation failures, connection/Inventory token cleanup and queued callbacks; E1 does not complete that audit. No server/protocol, item conversion, authoritative ACK, reservation, journal or persistent-format change.

原交易列表构造时订阅八个界面/服务事件但没有释放路径；E1 为列表/页签增加幂等 Dispose，由作用域在停止或部分 API 发布失败时退订。停止 finally 保证作用域释放并清空核心引用。单项激活失败、连接/库存令牌清理与排队回调仍需 E2 审查，本批不宣称该审查完成。无服务端/协议、物品转换、权威确认、预留、journal 或持久化格式变更。

## Validation / 验证

- Actual baseline DLL saved at `/tmp/phinix-trade-f4e1-before/TradeExtension.Client.dll`. Same probe on .NET 10 and Mono: 23 baseline and 42 candidate assertions each; API maps, capability list, priority and outgoing packet pass-through facts identical.
- Candidate checks interface/concrete singleton identity, shared repository across legacy/framework/domain views, no activation work during composition, all eight tab subscriptions installed once then detached, partial resolution/publication failure, missing composition factory and repeated shutdown.
- Existing `LegacyTradeRuntimeTests` source unchanged in this batch. All 10 scenarios pass with before/after actual Trade DLLs on both runtimes: only responses confirm offers, rejection/send failure, uncertain outcomes remain pending, unsupported items reject the whole offer, authoritative empty snapshot, unique wire tokens, nonpartial encoding and legacy item shape (plus existing early chat-history coverage).
- Full solution: 0 errors, 10 warnings (existing deprecated inline offer payload, protobuf target warning, unavailable NuGet vulnerability lookup). Narrow probe build: 0 errors; legacy harness: 0 errors, 12 warnings including existing assembly-version conflicts. 31 distribution checks pass; runtime DLLs unique, no game/Unity references in the ZIP, compiled Trade/Inventory/Store/host/Utils bytes match.
- Diff review and `git diff --check` pass; parallel changes retained. No commit/push, no external sample publication.

中文：两种运行时真实新旧 DLL 对照，基线 23 项/候选 42 项断言，API、能力、优先级、命令透传事实一致。八项列表事件完整验证订阅/退订；覆盖单实例、部分失败、缺少工厂和重复停止。既有十个协议场景在新旧 DLL 与两种运行时通过。整包 0 错误/10 警告，Legacy harness 0 错误/12 警告，发行包 31 项检查通过。保留并行修改，未提交/推送或发布样例。

### Limits / 限制

These tests run managed fixtures, not actual game rendering, transport connection, theme reload, inventory delivery or Harmony. The new probe references built Trade DLLs; build the solution before rebuilding/running the probe. Actual disable/re-enable registry operation, connection startup and game transactions remain manual/E2 checks. Successful compilation and response regressions do not certify live item ownership or server completion. Preserve F4-D menu/new-game verification as a manual check too.

测试使用托管夹具，未执行实际游戏绘制、网络连接、主题刷新、库存交付或 Harmony。新增测试引用已编译 Trade DLL，应先编译解决方案再编译/运行测试。实际禁用/恢复注册、连接启动和游戏交易需人工/E2 核查；编译和协议回归不能证明实际物品归属或服务端完成。F4-D 菜单/新游戏核查仍需执行。

## Commands run / 已执行命令

```sh
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/TradeCompositionRuntimeTests/TradeCompositionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/TradeCompositionRuntimeTests/runtimeconfig.json Tests/TradeCompositionRuntimeTests/bin/Release/net472/TradeCompositionRuntimeTests.exe
dotnet exec --runtimeconfig Tests/TradeCompositionRuntimeTests/runtimeconfig.json Tests/TradeCompositionRuntimeTests/bin/Release/net472/TradeCompositionRuntimeTests.exe --legacy /tmp/phinix-trade-f4e1-before/TradeExtension.Client.dll
mono Tests/TradeCompositionRuntimeTests/bin/Release/net472/TradeCompositionRuntimeTests.exe
mono Tests/TradeCompositionRuntimeTests/bin/Release/net472/TradeCompositionRuntimeTests.exe --legacy /tmp/phinix-trade-f4e1-before/TradeExtension.Client.dll
dotnet build Tests/LegacyTradeRuntimeTests/LegacyTradeRuntimeTests.csproj --configuration Release -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/LegacyTradeRuntimeTests/runtimeconfig.json Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
mono Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
python3 /tmp/phinix-f4e1-package.py
git diff --check
```

The first probe build restored packages (same command without `--no-restore`). Legacy commands also ran against the baseline DLL by temporary replacement of only the test-bin Trade DLL, restored in `finally`. PowerShell unavailable; packaging uses equivalent Python artifact checks. Logs saved under `/tmp/phinix-f4e1-*`.

首次探针编译带包还原（同命令去掉 `--no-restore`）；Legacy 命令还通过临时替换测试输出的 Trade DLL 对照基线，finally 恢复。PowerShell 不可用，Python 执行等价发行检查。

## Package and game checks / 包与游戏步骤

ZIP: `/tmp/phinix-rework-f4e1-trade-di-20261008.zip`
SHA-256: `1f5bf01a7114f02db1e8207ecc6e6923764f83a7dcfc80a3c0a9fce2cf4ab01a`

1. Log in, open Trade and settings; check accepting-trades synchronization and list names.
2. With a second player, create trade, update small offers, reject/cancel once, then complete once. Check quantities and Inventory delivery/receipt; no success before server confirmation.
3. Disconnect/reconnect; check refreshed list and no duplicate windows/messages or subscriptions.
4. Restart with Trade disabled, then re-enable/restart through the ordinary management flow; check normal startup and no duplicate registration/codec errors.
5. Repeat F4-D A/B/menu/new-game checks; confirm unrelated Inventory lifecycle behavior remains correct.

1. 登录打开交易和设置，核对接收交易开关同步和列表名称。
2. 与另一玩家建立交易，少量更新报价，拒绝/取消一次再成功交易一次；核对数量、库存交付/回执，服务端确认前不能算成功。
3. 断线重连，检查列表刷新，无重复窗口/消息/订阅。
4. 通过正常管理流程禁用 Trade 重启、再启用重启，确认启动正常，无重复注册/codec 报错。
5. 同时保留 F4-D 的 A/B/菜单/新游戏核查。

Next: E2 activation and borrowed dependency/registration ownership, before claiming F4-E complete. Game acceptance remains pending.

下一步 E2：激活与借用依赖/注册所有权，完成后才可宣称 F4-E 完成。本批游戏验收待完成。
