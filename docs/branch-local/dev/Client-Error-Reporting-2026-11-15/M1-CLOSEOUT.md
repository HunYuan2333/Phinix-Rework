# M1 Relay 联合整合完成

日期：2026-11-16。当前 `codex/client-error-reporting` 已整合本地 `codex/command-capability-relay`，保留 E0–E4 和现有 SQLite/Inventory/Trade 工作。**本记录的自动验证完成，不代表 E5 实际游戏或发行验收。** 没有推送、修改 main/dev、部署服务端或替换已交付包。

## 来源与精确引用

| 来源 | 提交与处理 |
|---|---|
| 原客户端 | `939e8e2c27e70432875dc2be306856b5a6621ea2` 上的未提交工作先冻结，再保存为独立检查点 `e00872e57aeb25c7e72d9071920a1c6452734e28`；明确包含先前 SQLite/业务工作和 E0–E4，未归为 Relay 新增内容 |
| 客户端 Relay | 合并 `311fdd64479a36d5244aa656cc9ac765c53cd6d6`，保留其来源历史 |
| 原 Common 错误处理 | 原 `c165f61d749189f700ca896d63d9366d5c71dbf7` 上的错误处理独立保存为 `a269cdf4eecba6f8c43ad246661dd4c417fd711e` |
| Relay Common | 合并 `911047cf8a6dd34e661e107e182e9c921b8e9586` |
| 最终 Common pin | **`ff0593e81ef44250669d0ff81df1389ccc056d87`**，具有上述错误处理与 Relay 两条父历史；客户端 gitlink 指向此精确提交 |
| 对应服务端 | 从 `5744a0e` 的完整 HEAD 导出隔离副本，再叠加本次合并的 Common；服务端工作树中的独立未提交修改被排除，未改动其分支或 gitlink |
| Example | 使用本地 `ef3b20c` 源码构建并运行原生命周期套件，未改其候选发行 pin 或工作树 |

来源文件及合并前源码 SHA-256 保存在 `M1-PREMERGE.json`。没有 reset/stash 清空工作树，也没有覆盖既有业务修改。使用的是已核对的本地分支，没有假称已刷新远端。

## 最终代码结合

- **Common 注册与撤销**：既保留 E2 的逐项回滚及 Aggregate 原因链，也执行 Relay 能力撤销。API/persistent/capability owner 清理分别观察；一个失败不跳过后续撤销。
- **Common Relay**：原构造/验证签名保留，新增可选异常回调重载。能力快照发送失败保留原错；业务 Relay 派发抛错仍为 Unknown，不误报为 NotSent。坏诊断观察者不能改变结果，安全 Trace 兜底保留类型、原因与帧。
- **Host**：保留 E3 的普通发送准备/派发边界、predicate 观察和原始异常传播；添加能力服务、ACK/Result、epoch 和受守卫的 timer。坏控制包记录原异常，过期/不匹配控制包按原规则拒绝。Shutdown 先停 timer、终结 Relay 等待，再停止模块，清理错误分别记录。
- **传输**：保持 `IClientTransport`、Configured 和 TLS，不退回 UDP 专用 Host。新增可选 `IClientRelayTransport` 准入能力，Configured 只转交此入口，不回退到普通 Send。UDP 保留 Relay 2048 包积压拒绝和固定后端收包/分片预算；TLS 使用已有实际 16 帧/4 MiB 队列。已知准备/未准入失败返回 false；可能跨派发边界的异常保持未知。
- **公共调用方**：CommandRelay Sample 已在两套目标编译；RedPacket/TalentTrade/Example 两版编译。Phase35 双方重复的 Compose 夹具已去重，保留所有原断言；工具脚本保留双方 suite 和别名。

Host 没有增加特定业务或内置插件专属恢复路径；Relay 结果仍是路由/传输结果，不被当成物品交付或游戏业务 ACK。普通 Chat/Trade/Legacy 请求不因合并而重发、退款或物化。

## 合并后验证

| 检查 | 结果 |
|---|---|
| 1.6/net472 Host/内置图与外部消费者 | 完整隔离构建、最终增量构建及 RedPacket/TalentTrade/Example 构建通过 |
| 1.7/net10 源码图 | 29 项目和最终增量构建通过，含上述三个消费者 |
| CommandRelay Sample | net472/net10 两版编译通过 |
| Example 原生命周期 | 140 项、五轮激活/关闭通过 |
| 新整合故障注入 | 29 项通过：原错/帧、派发 Unknown、断线未知、旧 epoch、坏诊断观察者、Configured 禁止普通发送回退、生产 TLS Channel 帧/字节准入与排空 |
| 实际客户端 UDP 收包 | 1 MiB 分片、恶意分片/合包、事件/字节水位、排空、poll 故障和重连通过；另 18 项线程所有权检查通过 |
| 客户端 Phase35 | 7 场景、97 项通过 |
| 冻结服务端 + 合并 Common | 完整服务端编译通过；21 组、628 项通过，含四种新旧组件组合 × JSON/protobuf、18000 个虚拟时钟请求、部分提交、广播预算、100 轮生命周期、64 pending 上限、超时 Unknown、真实本机 UDP ACK 排空及生产 TLS 队列准入 |
| 错误服务/窗口控制/RootPatch | net10、net472/Mono 各 92 项通过 |
| 编译后 Host/共享层边界 | net10 82 项、net472/Mono 76 项通过 |
| SQLite 业务 | 89 项通过，生产代码、实际 SQLite、合成资产 |
| 完整呈现 | 535 项通过，GUI 替身 |
| 完整 managed/商店 | 3436/1042 项通过；managed 包含其中 2225 项 Store operation 回归 |
| Legacy / Chat / wire | 89 项 / 20 场景 / 28 项通过 |

本机 socket 回归在允许创建本地 socket 的执行环境运行；第一次受限运行的拒绝不计作通过。测试只使用隔离输出和本机端口，不连接实际外部服务或启动生产服务器。TLS 准入使用注入的会话和实际 Channel，不宣称完成 TLS 实机联机。压力检查中的时间推进是虚拟时钟，不宣称实际运行了 30 分钟。

可复跑入口为已有隔离构建、E4 回归脚本及新增 `run-relay-integration-tests.py`；`verification-m1.json` 保存最终源码、产物、日志和来源摘要。`/tmp` 可被清理，不能把旧 R5 或 E4 的历史通过记录替代本轮证据。

## 后续

进入 E5 验收实际游戏窗口、复制详情、最终 RimWorld 传播、真实联机和平台/发行包。Common 新提交和客户端本地合并尚未推送；发布仍是独立操作。服务端的现有独立工作继续保留，若后续整合它，需要重新针对其最终状态验证。
