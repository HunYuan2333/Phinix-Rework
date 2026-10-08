# F4-G LegacyAdapter composition / LegacyAdapter 组合迁移

## Status / 状态

2026-10-08, dev. User accepted the combined Store test and requested finishing F4 before later phases. F4-G implementation and automated validation are complete; game acceptance is pending. No commit, branch or push. Parallel changes are preserved. F4-H deprecation/freeze and the tracked F4-F2c whole-mod/theme boundary work remain open; this is not overall F4 completion.

用户已报告 Store 合并测试通过，要求优先完成 F4。本批 F4-G 实现与自动验证完成，游戏验收待完成；未提交、开分支或推送，保留并行修改。F4-H 弃用/冻结与已登记的 F4-F2c 整模组/主题边界事项仍未关闭，不能据此宣称整个 F4 完成。

## Implementation / 实现

- The module uses the ordinary `ClientExtensionModule.Compose` bridge and the same shared discovery/registration/activation/shutdown path. The message and command handlers still have priority 500. Disabled discovery does not require composition services; no separate official-module registration path was added.
- A private composition scope borrows transport, display sink, session context, lifecycle and optional Trade APIs. It owns one `LegacyAdapterSession`, which constructs the existing protocol adapters and owns compatibility-mode subscription and transport registrations. It never disposes borrowed services.
- Unknown mode still registers Chat early for login history; Legacy registers Chat and Trade; FrameworkV2 relinquishes both. Mode changes and shutdown share a lifecycle lock; repeated activation is idempotent. Existing mode-change INFO logs are retained. Partial activation rolls back; shutdown attempts both handler removals even when one fails, and the composition factory reports aggregate cleanup failure.
- Each session transport registration has its own callback lifetime. Unregister invalidates captured callbacks; scope stop invalidates all session callbacks before handler removal. Calls already executing are not synchronously cancelled. Stopped outgoing text returns Continue without local echo.
- Chat's registration flag is set before registration and cleared before removal so partially successful registration can be cleaned up. No wire format, Trade response/ACK algorithm, item conversion or persistence format changes.

模块改用普通 Compose 桥接，保持相同发现/注册/激活/停止路径与优先级 500。私有作用域借用宿主端点和可选 Trade API，持有会话对象；会话负责原协议适配器的构建、模式订阅与端点登记，不释放借用服务。Unknown 仍提前接收聊天历史；Legacy 启用聊天和交易；V2 移交两者。模式切换与停止共用生命周期互斥，保留原 INFO 日志；重复激活幂等，部分注册失败回滚；停止尽力清理全部端点，并由组合工厂报告清理异常。

每次端点登记使用独立回调寿命，取消登记使已捕获的旧回调失效，停止会话先使回调失效再移除端点；已经执行中的回调不作同步取消。停止后的文本处理直接 Continue，不产生本地回声。聊天登记标记调整仅服务于失败清理；协议格式、交易确认/ACK、物品转换和持久化格式未改。

## Validation / 验证

Commands actually run from the repository root:

```sh
dotnet build Tests/LegacyTradeRuntimeTests/LegacyTradeRuntimeTests.csproj -c Release -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/LegacyTradeRuntimeTests/runtimeconfig.json Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
mono Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
# Isolated probe directory copies the test output and replaces only LegacyAdapter.Client.dll
# with the actual DLL captured before F4-G.
dotnet exec --runtimeconfig /home/hunyuan2333/Phinix/Phinix-Rework/Tests/LegacyTradeRuntimeTests/runtimeconfig.json /tmp/phinix-f4g-baseline-probe/LegacyTradeRuntimeTests.exe --baseline
mono /tmp/phinix-f4g-baseline-probe/LegacyTradeRuntimeTests.exe --baseline
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
python3 /tmp/phinix-f4g-package.py
git diff --check -- Extensions/LegacyAdapter/Client Tests/LegacyTradeRuntimeTests docs/branch-local/dev/F4-G-LegacyAdapter-Handoff.md
```

Both runtimes pass all 11 scenarios: baseline 58 assertions, candidate 68. A module location assertion verifies the intended isolated DLL is used. Common behavior checks include ordinary registry/disabled/failure policy, handler priority, early history, reconnect/Unknown/Legacy/V2 transitions, server-response-only confirmation, rejected/send-failed/uncertain operations, authoritative snapshots, wire tokens, all-or-nothing encoding and stateful item compatibility. The ten additional candidate assertions cover Compose, repeated activation, inert captured Chat callback, stopped text, fresh activation, partial Chat/Trading registration rollback, retry, cleanup failure reporting and borrowed services.

.NET 与 Mono 均通过 11 个场景：旧 DLL 58 项断言，新 DLL 68 项；加载路径断言确认对照的是隔离目录中的指定 DLL。共同检查原有历史接收、模式切换、交易服务端确认、不确定发送、整批物品转换、旧协议兼容及普通注册路径。新增检查针对 Compose 与作用域生命周期。

The classic test project now explicitly copies the composition runtime dependencies after build. The first Mono attempt failed due to missing DiagnosticSource; the corrected project passes without manual runtime file substitution. Mono still warns about missing Unity.Burst custom attribute metadata in compile-only game references; it exits successfully. This does not validate Unity/game behavior. PowerShell is unavailable; package validation uses the Python equivalent of required-artifact/runtime ownership checks and verifies ZIP integrity and exact compiled module bytes.

经典测试项目补齐 DI 运行库复制，修复初次 Mono 的 DiagnosticSource 缺失后复跑通过。Mono 仍报告编译用游戏引用的 Unity.Burst 属性元数据缺失警告，退出码为零；控制台回归不能替代游戏测试。本机无 PowerShell，输出包使用等价 Python 检查必需文件、运行库唯一性、ZIP 完整性和编译字节一致性。

## Game check / 游戏步骤

1. Replace the current package and fully restart RimWorld. Confirm extension management lists LegacyAdapter and there are no activation/cleanup errors.
2. With the normal V2 server, check login, chat send/receive, a normal trade and reconnect; verify no duplicate chat/legacy handling.
3. If a legacy server is available, verify login-time history, local chat echo once, incoming chat, normal offer update/rejection and completion. A send attempt must not become success before the server response. Disconnect/reconnect and repeat.
4. Load a save again without restarting the game and verify chat history/receipt and inventory remain correct. Quit/restart and check no duplicate registrations.
5. Disable LegacyAdapter and restart: entry remains discoverable as disabled, with no activation. Re-enable and restart; normal behavior returns.

替换整包并重启；核查扩展管理、普通服务器聊天/交易/重连。若可连接旧服务器，核查登录历史、本地单次回声、入站聊天、交易成功/拒绝/完成及重连；发送不等于服务端确认。不重启读取存档，检查历史、收信和库存。最后检查禁用/启用后重启，禁用条目仍可发现且不激活。没有旧服务器时，应明确该项尚未验证。

After batch acceptance, continue F4-H and close the separately tracked ownership boundaries before declaring F4 complete or entering F5. No blanket compatibility, persistence or ownership certification is implied by the build.

本批验收后继续 F4-H，并关闭单独登记的边界事项；这些门槛完成前不宣布 F4 完成或进入 F5。构建通过不代表所有兼容性、持久化或物品所有权行为已经游戏验证。

## Final output / 最终输出

Final full build: 0 errors, 2 protobuf SDK warnings. Artifact check: 31 required files plus unique runtime bytes, no game reference DLLs, translation keys, ZIP integrity and exact compiled LegacyAdapter/Trade/Inventory/host/Store bytes. The tested LegacyAdapter DLL also matches the final compiled DLL.

最终全量构建零错误、两项 protobuf SDK 警告；31 项必需文件及运行库/翻译/ZIP/模块字节核对通过。测试的 LegacyAdapter DLL 与最终编译并打包的 DLL 一致。

Package: `/tmp/phinix-rework-f4g-legacy-di-20261008.zip`

SHA-256: `e464f7effef185245b50918f3753dde2f93013d55c495fdefcc5afb66f5bd116`

## Phase checkpoint supersession / 阶段检查点更新（2026-10-08）

用户后续明确要求取消整模组方案、主题转后续评估，并在文档完成后关闭 F4。该范围决定覆盖本文件先前“必须完成边界事项才可结束 F4”的排期；不虚构 LegacyAdapter 各项游戏测试结果。F4-H-Closure-Handoff.md 记录当前状态，F5 不自动开始。

The later user scope decision closes F4 after documentation, cancelling the whole-mod proposal and deferring theme work. It supersedes the prior scheduling gate here without inventing individual game acceptance. See F4-H-Closure-Handoff.md; F5 remains on hold.
