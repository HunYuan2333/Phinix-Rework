# E3 发送路径切片（2026-11-15）

E3 正在实施，本次完成发送结果传播与 Phase35 夹具修复。认证、TLS/UDP 后台入口、SQLite 与设置路径仍待完成；本文件不替代 E2 的历史验证记录。

## 行为与合同

- `PhinixFrameworkClient.sendPacket` 不再在断线、关闭、协商拒绝或传输异常时静默返回。
- 进入底层 `Send` 前的准备失败，先保留原异常诊断，再抛出 SDK 的 `ClientPacketNotSentException`，原异常作为 InnerException。该类型只证明**这一次调用**未进入传输，不证明上层多包操作之前没有发送过任何内容。
- 进入底层 `Send` 后失败，按 `PacketSendOutcomeUnknown` 记录原异常并使用裸 `throw;` 继续传播；不根据异常推测服务端收件结果。
- 成功返回只代表底层传输接受调用，不代表服务端 ACK。不会自动重发、退款或切换协议。
- `SendFrameworkPacket` 对 null/已关闭实例也通过同一准备边界记录并抛出。
- `TrySubmitMessage` 保留不可用/无匹配处理器的 false；处理器或实际发送异常记录后继续抛出，避免把已进入处理器的失败伪装成可选择其他协议的“未处理”。发送统一调用 `sendPacket`，也遵守协商拒绝规则。Handled/null 仍可表示处理器自行消费，不是服务端确认。

公开方法签名保持原样，新增异常类型属于 SDK 的增量 API。原先依赖静默失败的调用方现在会看到异常，这是本次有意修正的行为。

## 调用方核验

- Trade `UpdateTradeItems` 仍将本地构建阶段包装为 `TradeRequestNotSentException`；进入 dispatch 后仍保守按 Unknown 处理。没有把多包操作中较后一次调用的 NotSent 当作整个交易未发送，没有新增退款/重发。
- Chat submission adapter 没有捕获后选另一个协议的逻辑，失败可继续向上传递。后续 E4 继续核验具体 UI 的提示/隔离策略。
- 原来的出站 Continue/Handled、能力不足时的玩家提示、入站错误隔离行为未在本切片重写。

## 验证

- 1.6/net472 实际 Host 与内置插件依赖图编译通过。
- 1.7/net10 实际源码依赖图 28 个项目编译通过，包含维护中的独立 RedPacket/TalentTrade 客户端消费者。
- 真实编译 Host 边界测试：net10 与 net472/Mono 各 38 项通过（E2 30 项 + 发送 8 项）。新增断线、null、关闭、协商拒绝、发送一次成功、原异常实例/原始来源堆栈、未知结果诊断检查。异常由测试传输真实抛出，不使用真实网络或服务端。
- Phase35 7 个场景、97 项断言通过。补入 `IClientTransport` 编译清单；生命周期夹具明确实现当前客户端入口 `IClientExtensionModule`，Compose 转接到原有 Register 测试逻辑。未放宽生产发现规则。
- 原 Phase35 工程的直接 restore 在本机返回“0 errors”但失败；新增 `run-phase35-client-tests.py` 使用已编译的实际共享程序集，在隔离目录编译同一组测试源码，避免重新评估旧的传递 SDK 工程。测试根目录可由 `PHINIX_TEST_ROOT` 指定。
- Mono 检查仍有既有 game reference 的 Unity.Burst 属性依赖提示；38 项断言全部通过，没有替换游戏 DLL。

可复跑命令：

```sh
python3 tools/quality/run-client-error-boundary-tests.py --output /tmp/unique-boundaries \
  --runtime16 /tmp/built16/Phinix-Rework/Client/Source/bin/Release \
  --runtime17 /tmp/built17/bin/Release/net10.0 \
  --game16 GameDlls --game17 /path/to/1.7/references
python3 tools/quality/run-phase35-client-tests.py --output /tmp/unique-phase35 \
  --runtime17 /tmp/built17/bin/Release/net10.0
```

详细源文件、产物和验证日志摘要见 `verification-e3-send.json`。`/tmp` 不是持久产物，丢失后应重新构建。未进行游戏内弹窗/剪贴板/最终 RimWorld 传播验收，未提交、推送或发布；Common 仍需提交并更新精确 gitlink 后才能发布。
