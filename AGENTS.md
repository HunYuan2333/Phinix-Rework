# Phinix-Rework

Owns RimWorld client and client plugins. Read docs/Design-Philosophy.md and docs/Compatibility-Boundaries.md before changing boundaries or recovery. Preserve plugin parity, authoritative acknowledgements, all-or-nothing item delivery and main-thread game dispatch. Preserve dirty changes. Never commit credentials, server data/logs, GameDlls or build output. Do not edit vendored protobuf SDK pins/source for compatibility. Shared consumers pin exact gitlinks, never update --remote during normal acquisition. No additional Phinix NuGet SDK channel.

## Reliability proportional to a game mod / 可靠性大前提

This is a game mod. Prefer normal availability, understandable failures and small maintainable mechanisms. Keep basic transactions, idempotency, bounds and correct ownership; do not pursue financial-grade end-to-end consistency, automatic recovery of every crash, or comprehensive anti-rollback guarantees. A rare interrupted operation may be abandoned with a visible warning about possible item loss. Never silently erase committed balances, manufacture remote success, or refund an uncertain sent request. A local preparation failure is NotSent, not Unknown. Isolate affected operations instead of indefinitely freezing unrelated inventory. SQLite owns storage transactions; business modules own their tables and outcome policy, through neutral shared infrastructure.

这是游戏 Mod：优先正常可用、可理解的失败和可维护的简单机制。保留基本事务、幂等、边界与所有权；不追求金融级全链路一致性、每次崩溃自动恢复或全面防读档。少见的中断操作可以放弃，并明确提醒可能丢失物品。不得静默删除已提交余额、伪造远端成功或自动退款未知已发送请求。本地准备失败属于未发送；尽量隔离相关操作，不长期冻结无关库存。数据库基础设施保持中立，各业务拥有自己的表及结果规则。

## Project naming / 项目命名

The mod is **Phinix Rework** (`phinix-rework`), distinct from the original Phinix project. The canonical repositories are `HunYuan2333/Phinix-Rework` (client), `HunYuan2333/Phinix-Rework-Common` (shared contracts/source), and `HunYuan2333/Phinix-Rework-Server` (dedicated server). Use these names in repository URLs, clone instructions and project documentation; do not refer to this project as the original Phinix. Preserve existing assembly names, namespaces, protocol identifiers, mod package IDs, persisted keys and the submodule directory `Dependencies/Phinix.Common` for compatibility. A repository rename does not authorize changing those identifiers.

模组统一称为 **Phinix Rework**（`phinix-rework`），与原 Phinix 项目区分。客户端、共享层、服务端仓库分别为 `Phinix-Rework`、`Phinix-Rework-Common`、`Phinix-Rework-Server`；仓库链接、clone 说明和文档使用统一名称。改仓库名不改程序集、命名空间、协议标识、模组 packageId、持久化键或 `Dependencies/Phinix.Common` 子模块目录，避免破坏兼容性。
