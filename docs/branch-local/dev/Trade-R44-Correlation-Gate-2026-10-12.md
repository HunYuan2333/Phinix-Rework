# B26 报价响应与快照关联证据

## 源码事实

客户端 PhinixFrameworkTradeClientService.HandleOfferUpdateResponse 成功时 TrackPendingTradeUpdate(tradeId, token)。原 FlushPendingEventsForTrade 在任意同 Trade 快照上移除全部 tokens 并为每条发成功事件；full snapshot 也会触发。Reset 的“真实 response + token + 空报价”保护因而仍可能拿另一次更新/全量重同步的空报价作为该 token 的处理依据。

只读核对服务端源码（未编辑）：PhinixFrameworkTradeServerService.HandleOfferUpdate 先 SendOfferUpdateResponse，再 BroadcastSnapshot；二者传相同 command.GetCorrelationId。SendSnapshot 显式 SetCorrelationId、SetStateKind(Delta)、SetSnapshotVersion。这些字段已存在，不需要发明新协议。

FrameworkMetadataHelpers.GetCorrelationId 在 metadata 缺失时回退 packet.MessageId。独立成功响应/快照的 message ID 不是原操作证明，因此本次报价成功响应与 delta fulfilment 使用显式 CorrelationId metadata，缺失时不产生关联确认。

## B26 行为

- 成功 offer response 仅在明确非空 correlation metadata 时登记待确认 token。
- full snapshot 继续同步 repository/creation/OnTradesSynced，不消耗待报价 token。
- delta 只消耗本 Trade 下相同 correlation token（Ordinal），保留其他 token；Trade ID 保留既有 OrdinalIgnoreCase 语义。
- 无关联 delta 继续投影实际状态，事件不带待处理 token；不能因同 Trade ID 便制造 operation confirmation。
- 同 token 重复 response 在仍等待时去重。已消费 pending 后重复 delta 不再发该关联确认。
- 原 trade-ID-correlated status empty-token 表示保留，不能消费非空报价 token。LegacyAdapter 显式 Begin/Complete 路径不变；无修改 adapter/Common/server/schema/wire。

## 严格限制

本批不提供乱序自动重放：delta 先于 success response 时，后来的 unrelated/full sync 不能结束等待。仅匹配新的 delta 才可继续；缺证据保留待确认，不自动返还。这不是完成后的全时段 response replay ledger：pending-only 去重不能证明跨进程/任意延迟重复 exactly-once。TradeOfferOperations 原 matching/accepted/duplicate/lifetime gate 继续负责本地副作用，未新增 server recovery proof 或 durable physical escrow。

当前服务按既有来源线程模型处理网络状态；本批只收紧结果关联，不改变线程调度。Owner 的 game-resource settlement 仍经主线程 dispatcher。实际传输顺序、服务器与客户端组合、物品处理/Reset 空投尚待游戏。

## 验证

客户端 0 errors / 4 existing warnings；191 presentation stub checks；177 actual DLL assertions，新增 14 条：unrelated delta/full sync/缺 delta correlation（包括同 message ID）/两个 pending token/duplicate waiting responses/不同 Trade/nonce 大小写/确切 empty snapshot/重复 delta/缺 response correlation/乱序安全/status 原表示/adapter 结果路径。使用实际反序列化/metadata/client service，不使用 native GUI、Thing 或联机，不把这些算游戏通过。

补充：delta 还需恰好两个非空不同参与者，并包含当前本地 UUID；缺失/重复或没有本地 perspective 时丢弃该 delta，不把 local-null 解码出的空数组当 Reset 证明。全量同步仍不提供 operation confirmation；本批不是所有 full-snapshot validation 的重写。新增两个 actual DLL checks 后合计 177。
