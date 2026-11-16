# E4 首批实施：Inventory / Trade 核心错误路径

日期：2026-11-16。**E4 实施中，本记录只闭合本批路径，不代表 Inventory、Trade 或五个内置插件全部完成。** 当前仍在 `codex/client-error-reporting`；没有提交、合并、推送或发布。Relay 整合 M1 保持在 E4 完成后、E5 联合验收前。

## 已实现行为

| 路径 | 错误出口与保留的业务结果 |
|---|---|
| 插件敏感诊断 | SDK 增量增加 `IClientSensitiveErrorReporter` 能力与扩展方法，保留原 `IClientErrorReporter` 合同。Host 保存模块归属、异常类型、原因链和调用栈，省略任意消息与本地源码路径。缺少可用敏感 reporter 时走 Common 安全 Trace；不把业务载荷交给不能保证脱敏的旧 reporter。模块停止后的已绑定 reporter 不再生成通知 |
| InventoryDatabaseStore | 写入失败先交付原异常，再返回安全 LastError。Busy/Unavailable 不新增永久冻结；Corrupt/OutcomeUnknown 沿用原隔离策略。不确认提交时不发布内存余额、不盲目重放。中断提取放弃失败的 IOException 保留与诊断相同的原数据库 InnerException |
| Inventory 启动/旧格式导入 | 激活时绑定普通模块 reporter；存储服务仍保持中立。Attach、旧导入冲突和迁移提示错误保存完整敏感诊断；提示窗口失败安静记录，不把完成的导入改成失败。玩家状态文字不再嵌入任意 Exception.Message；存储未确认时表述为“无法确认”，不声称“没有提交” |
| TradeOfferOperations | 原 NotSent 准备错误及 InnerException 完整报告，已知未发送才恢复；未知发送仍保留 pending、不退款。结算、未知标记、日志回调与终态清理失败分别记录。逐个调用终态清理观察者，坏观察者不阻止健康观察者，也不让已结束操作重新进入 pending |
| TradeSqliteRecords | 导入原错先记录，初始化清理另行记录；导入与失败标记同时出错时 Aggregate 保存两者。Quarantine 对数据库错误用 ExceptionDispatchInfo 传播同一异常及原始调用帧，不把数据库暂不可用误标为业务记录损坏。损坏业务行仍保留并按原规则隔离，warning 回调失败不覆盖原错 |
| Trade journal/inbox | 保留旧构造重载，新增可选诊断回调并接入模块。ReadAll 原始异常继续交给 Quarantine 统一记录。数据格式、回执权威、幂等键和交付授权不变 |
| 完整呈现回归 | 补齐旧库存替身的 scope、StorageNotice、维护/放弃接口，保留原断言。回归发现 E2 曾把被动图片错误标成整个页面失败；生产 renderer 现单独记录图片错误并降级为文字，保留页面操作。真正页面失败仍设置 FailureCode |

异常上报不承担重试、退款或物化，不从隔离/后台路径伪造 throw。原本继续传播的同步异常仍保持传播；只在既有捕获边界增加观察。

## 本轮验证

| 检查 | 实际结果 |
|---|---|
| RimWorld 1.6/net472 实际 Host/内置依赖图 | 初始构建及最终库存原因链修改后增量构建均通过 |
| RimWorld 1.7/net10 实际源码依赖图 | 28 项目通过，包含维护的 RedPacket/TalentTrade 消费者；最终修改后增量构建通过 |
| 错误服务、controller、dispatcher、RootPatch | 两套运行时各 66 项通过，包含插件敏感报告与停止后撤销 |
| 编译后共享层与 Host 边界 | net10 82 项、net472/Mono 76 项通过 |
| Inventory/Trade 生产源码与真实 SQLite | 89 项通过，使用合成资产；包含原数据库错误身份/调用帧、故障行保留、回调独立失败、NotSent 恢复与 Unknown 不退款 |
| 完整原 PresentationRuntimeTests | 535 项通过；GUI 替身，不是游戏加载或窗口验收 |
| 完整原 managed 套件 | 3435 项通过，包含 2225 项 Store operation 回归及真实 metadata/startup 子进程 |
| 差异与工具语法 | 根仓库/Common diff --check，相关 Python 工具语法通过 |

Mono 的原有 Unity.Burst 元数据提示仍在，断言通过；没有替换游戏 DLL。示例 CommandRelay 在 M1 按实际 Relay API 联合验证。本批未重复声称通过真实游戏弹窗、剪贴板、最终 RimWorld 抛出、联机、Windows ACL 或发行包。

## 登记与后续

不可变评估清单未重写。本批为 **32** 个历史位置登记实现或保留理由；整个 E4 尚有明确的 pending-review。新增敏感能力、回调和清理代码不在历史清单中，通过本批源码指纹和故障注入覆盖。指纹、产物和日志摘要见 `verification-e4-core.json`；临时构建目录可能被系统清理。

下一批继续 Inventory 的物理投放、codec、事件与清理，Trade 的交付/恢复泵和窗口提交，再处理 LegacyAdapter、Chat、PluginStore。各模块全部审阅、补齐样例报告后才标记该模块完成，E4 全部闭合后才开始 M1。

可复跑入口为现有隔离构建脚本及：

```sh
python3 tools/quality/run-client-error-tests.py --output /tmp/fresh-errors
python3 tools/quality/run-client-error-boundary-tests.py --output /tmp/fresh-boundaries --runtime16 /path/to/runtime16 --runtime17 /path/to/runtime17 --game16 GameDlls --game17 /path/to/game17
python3 tools/quality/run-sqlite-business-tests.py --output /tmp/fresh-business --graph /path/to/net10-graph --game /path/to/game17
python3 tools/quality/run-client-presentation-tests.py --output /tmp/fresh-presentation --runtime17 /path/to/runtime17
python3 tools/quality/run-client-managed-tests.py --output /tmp/fresh-managed --runtime16 /path/to/runtime16 --runtime17 /path/to/runtime17
```
