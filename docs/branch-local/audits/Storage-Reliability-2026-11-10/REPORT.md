# 客户端持久化与异常状态专项调研

日期：2026-11-10。任务：检查与库存/交易日志事故类似的容量、失败分类、恢复及隔离问题，评估 SQLite 迁移。此轮不修产品代码、不迁移玩家数据、不推送或部署。

## 结论与决策建议

建议引入通用 SQLite 基础设施，优先迁移 **Inventory 权威库存与 Trade 操作/收件记录**。不能只改库存、继续保留 Trade 当前的文件预算与全历史扫描。红包托管可随后接入；插件文件、主题、可重建商店缓存和身份凭据不应一并搬进数据库。

本轮整理出 15 项具体问题或不符合新恢复策略的设计债：8 项 P1、7 项 P2。P1 表示可阻断主要业务、导致交付长期悬置或影响整个模块；P2 表示局部失败、错误提示或长期使用的资源耗尽风险。它们不是 15 起已发生的数据损坏事件。证据区分为现场日志、真实生产类的隔离探针、源码路径与明确的策略取舍。

根因分为三类，不能都靠更换数据库解决：

1. **存储结构问题**：全状态反复追加、多文件关联、历史累计预算、存档/外部日志双权威，SQLite 有明显替代价值。
2. **操作阶段错误**：没发出去却标 Unknown、忽略失败返回、回调丢失、异常中断订阅链。需要修改业务逻辑，SQLite 不会代劳。
3. **恢复承诺过重**：未知操作无限保留、局部记录故障阻塞全局。按用户新决定改为有限恢复、允许 Abandoned 并提醒，避免无限期冻结正常业务。

## 范围与证据限制

- Client：`939e8e2`（产品代码基线 `075c929`；后续两次仅新增设计文档），Common 固定 `c165f61`。
- 独立红包：本地 HEAD `a4a88ad`；人才交易：本地 HEAD `5df4626`。针对实际读取的工作树源码记录 SHA-256，不假设它们与玩家安装包一致。
- 服务端：仅对照现有 `SqliteOperationJournal`、交易存储和归档；main 为 `207f17d`。当前服务端工作树位于 Relay 分支，本文所引存储/归档文件与 main 的差异核对为空。未执行完整服务端审计。
- 全集检索范围包括 Client、Extensions、固定 Common/Utils、红包/人才 Client；对持久化、容量和 Unknown 相关匹配逐类筛查，深入阅读核心业务链。附 [60 个匹配源文件及 SHA-256 清单](source-inventory.tsv)。匹配清单不是每个文件都完成完整功能审计的证明，也不能保证已找出所有缺陷。
- 现场：本机 `Player (1).log`（库存分支校验失败）及 `Player (2).log`（交易意图容量失败）。不把不同玩家的问题视为同一次事故。未将玩家原始日志、身份、载荷或交易 ID 复制进仓库。
- 没有读取玩家 journal/存档目录、远程服务端日志或线上资产；不能断言当前资产丢失数量、日志真实大小或旧记录具体来源。

## 问题总表

| ID | 优先级 | 问题 | 证据 | SQLite 的作用 |
| --- | --- | --- | --- | --- |
| F01 | P1 | 明确的发送前存储失败进入 Unknown，托管不解除 | 新日志＋生产类探针 | 不能修阶段分类 |
| F02 | P2 | Submit 返回 false，UI 仍显示已提交 | 源码 | 无直接作用 |
| F03 | P1 | Trade intent 累计历史及虚拟预留耗尽准入 | 新日志＋源码＋容量模型 | 适合替换，仍需终态保留策略 |
| F04 | P1 | Completion inbox 终身累计容量耗尽后拒收新结果 | 源码 | 适合替换，需清理大载荷 |
| F05 | P1 | 一条旧记录损坏可阻止整个 Trade 激活 | 源码明确如此设计 | 需要隔离策略，数据库自身也可能损坏 |
| F06 | P1 | 业务 payload 上限与存储序列化包络不一致 | 源码＋BCL 序列化测量 | 规范化表/BLOB 有益，需端到端预算 |
| F07 | P2 | Inventory 全快照写放大及永久幂等记录上限 | 源码；日志证实大量历史回收 | 增量表写入有直接价值 |
| F08 | P1 | 正常保存换路径后，库存因无标记旧目标进入只读 | 旧日志＋源码 | 改为跨存档单一权威后消除该分支 |
| F09 | P2 | 单个 pending extraction 阻断整库及下游接收 | 源码；历史安全策略 | 需缩小业务锁定范围 |
| F10 | P1 | 终态落盘后订阅者异常可中断后续交付，重放又被去重 | 源码；独立 PR 也涉及此机制 | 需订阅隔离和投递状态，非换引擎即解决 |
| F11 | P2 | 结束后成功回包被忽略，独立结果核对又依赖整库可写 | 生产类探针＋源码 | 需改状态机和核对前置条件 |
| F12 | P1 | void 主线程队列拒绝可丢首次交付动作，缺少可恢复计划 | 源码路径；未做满队列游戏复现 | 需可观察任务或明确放弃通知 |
| F13 | P2 | 红包已结算文件仍计入终身托管容量 | 源码 | 适合作为第二批模块接入 |
| F14 | P2 | 红包接收在副作用前保存失败也变成不允许重试的 Unknown | 源码；与 F01 类似阶段混淆 | 仍需更正状态推进 |
| F15 | P2 | 人才购买超时条目保留并占用 128 个会话槽 | 源码；受控但与新有限恢复策略不符 | 不必为此引入持久 DB |

## 详细证据与处理方向

### F01 / F02：发送前失败被当成结果未知，界面还报已提交

`Player (2).log` 06:03:36 首先报 `Intent journal has insufficient result evidence headroom`，06:05:18 同操作报 FinishedBeforeOfferResult，06:54:33 停止模块仍未解除。不是先有网络回包乱序才引起该次失败。

[`FrameworkClientTradeServiceAdapter.cs:244`](../../../../Extensions/Trade/Client/FrameworkClientTradeServiceAdapter.cs#L244) 在发送 item 和 command 之前调用 PrepareIntent。容量拒绝发生在 `.intent` 发布之前，[`TradeIntentJournal.cs:246`](../../../../Extensions/Trade/Client/TradeIntentJournal.cs#L246)。但 [`TradeOfferOperations.cs:65`](../../../../Extensions/Trade/Client/TradeOfferOperations.cs#L65) 在调用 send 委托前设置 Attempted，并把捕获到的所有异常统一转 Unknown。此前窗口可能已预留库存或把地图物品 DeSpawn，不能因为命令根本没进入网络就永久悬置。

[`TradeWindow.cs:194`](../../../../Extensions/Trade/Client/TradeWindow.cs#L194) 忽略 Submit 的 false，继续显示 editorOfferSubmitted；重置报价同样如此（219 行）。外层 catch 也收不到 Submit 已吞掉的异常。

隔离探针直接编译当前 TradeOfferOperations.cs：注入发送前 IOException，结果 Unknown、restore 次数 0、MarkUnknown 次数 1。这是生产类行为复现，不是整条网络链或玩家 DLL 的复现。

处理：区分 Preparing、NotSent、TransportStarted、RemoteKnown、Abandoned；发送边界由真正触及传输的层报告。已知未发出可恢复本地托管并准确提示失败；已发送但不确定按有限恢复规则保留/放弃，不能一概退款。UI 消费真实返回。

### F03：保守空间预留成为正常交易的寿命限制

[`TradeIntentJournal.cs:57`](../../../../Extensions/Trade/Client/TradeIntentJournal.cs#L57) 的上限为 64 MiB / 1024 个 intent；192–207 行为所有已有操作缺失的 result（2 MiB）、query（8192 B）、offer reservation（2 MiB）和 attempt 预留空间。目录包含所有 endpoint/主体的历史，准入先遍历全体记录。

即使实际文件很小，16 个 result/binding 均缺失的报价意图，预留额度本身也已超过 64 MiB。这是条件化模型，不是该玩家历史恰好有 16 条记录的证据。缺失 binding 的非库存报价等记录也应纳入后续计费设计核对，不能假设每个槽未来必定会使用。

当前仅在失败写入后清理临时文件，没有 intent 终态回收路径。正常结果能释放部分预留，但永久条数和字节上限仍存在。历史文档已注明冷归档未实现，不能把“拒绝得很安全”当作容量问题已解决。

处理：Trade 自有结构化 operation/result 表，终态只保留小型 operationId/fingerprint/outcome 去重记录，大 payload 有明确生命周期；普通准入预算与既有操作收尾所需空间分开。SQLite 仍有磁盘/配额，不保留现有原样的保守全历史预留公式。

### F04 / F05：收件箱既会累满，也会被单个坏文件拖垮

[`TradeCompletionInbox.cs:49`](../../../../Extensions/Trade/Client/TradeCompletionInbox.cs#L49)：单 receipt 4 MiB、累计 receipt 32 MiB、最多 4096 条；辅助证据另有 128 MiB 预算（206 行）。274 行容量满则拒绝新 receipt，且没有终态收据清理机制。

构造函数 60–69 行会读取并验证所有历史 receipt 和辅助文件；一条损坏、孤儿文件或超预算会直接抛异常。Trade 激活直接创建 inbox 和 intent journal，[`BuiltInTradeClientExtension.cs:131`](../../../../Extensions/Trade/Client/BuiltInTradeClientExtension.cs#L131)，没有将旧记录故障限制在那笔操作内。Record 的跨主体/endpoint 扫描也可能被无关历史坏记录中断。

处理：完成记录、交付阶段和小型去重结果进 Trade 库；坏的业务行隔离并提醒、阻断相应交易，不因历史单行内容错误停整个模块。底层数据库确实打不开或结构损坏时仍须有清晰的不可用状态，不能承诺数据库绝不会全局故障。

### F06：合法业务数据可能无法写进自己的持久化格式

Inventory 对外允许单 payload 8 MiB、批次 32 MiB，见 [`InventoryLedger.cs:87`](../../../../Extensions/Inventory/Client/InventoryLedger.cs#L87)。journal 却把整个 InventoryState JSON 限在 8 MiB，[`InventoryJournal.cs:233`](../../../../Extensions/Inventory/Client/InventoryJournal.cs#L233)。多条合法小条目累计也会超限；异常路径可把 journal 标 Faulted，而不是只拒绝该笔新增。

当前 FrameworkSerialization 使用 DataContractJsonSerializer（固定 Common 的 FrameworkSerialization.cs:169–182）；byte[] 会序列化为 JSON 数字数组。测量 3 MiB、字节均为 255 的合成数组，JSON 为 12,582,913 B，未计条目字段就超过 8 MiB。测量只证明包络膨胀，并非用完整真实 codec 建立了 3 MiB 游戏物品。

Trade intent 的 2 MiB 和 completion/plan 的 4 MiB 也是整个 JSON 包络预算，见 intent 239 行、inbox 262 行、[`TradeDeliveryReceipts.cs:118`](../../../../Extensions/Trade/Client/TradeDeliveryReceipts.cs#L118)。公共 stateful 物品协议原始状态上限为 4 MiB；因此不能把业务允许的 raw 大小当作其嵌套 JSON 一定可保存的保证。具体 codecs 还可能有更小限制，未声称每个物品都能达到理论上限。

处理：payload 使用 BLOB，按实际变化行写入，明确 raw/encoded/batch/store 各层容量；在提走地图物品或建立托管前验证可存储性。不能只扩大 journal 上限。

### F07 / F08：库存重复持久化和存档身份绑定制造额外失败面

[`InventoryJournal.cs:252`](../../../../Extensions/Inventory/Client/InventoryJournal.cs#L252) 每笔变更序列化完整 ledger，128 MiB 写入上限且故障后暂停写入；checkpoint 依赖匹配的真实游戏存档。`Player (2).log` 06:13:47 回收了 101,568,332 B 的保存历史，证明写放大在现场发生，但该回收成功本身不是错误。Commits 和 Reservations 还有各 100000 的累计数量限制，终态记录未见对应清理。

旧 `Player (1).log` 三次保存后的 `.journal` 分支校验失败，快照序号均为 0。无 authority 的旧目标必须有末条精确快照，而 Parse 不接受序号 0 的记录；空文件也无法提供末条证明，[`InventoryJournal.cs:203`](../../../../Extensions/Inventory/Client/InventoryJournal.cs#L203)。这是严格历史归属规则触发的只读，不证明文件损坏，也不证明玩家物品已丢。源码虽有放弃恢复动作，日志不能证明该玩家包内界面已提供它。

处理：SQLite 作为跨存档单一权威，去掉以保存路径分 journal 的常规写路径；存档只保留升级信息。历史 reader 仅用于一次导入，失败不删除源快照；允许不恢复旧日志中的不明后续变更并提醒。

### F09 / F11：局部不确定状态扩大到整库与交易核对

[`BuiltInInventoryClientExtension.cs:1109`](../../../../Extensions/Inventory/Client/BuiltInInventoryClientExtension.cs#L1109) 只要全局单个 PendingExtraction 存在就不 Ready；TryDeposit 等正常入口也受此条件影响。GetStatus 报 ExtractionReconciliationRequired，使下游 CanWrite 为 false。局部交付未知由此阻断不相关的入库/交易。

[`TradeOfferOperations.cs:91`](../../../../Extensions/Trade/Client/TradeOfferOperations.cs#L91) 在 FinishedBeforeResult 后忽略匹配的普通成功回包。生产类探针确认它仍是 Unknown；独立 ApplyDurableResult 可以结算，对照探针通过。但 [`TradeIntentSettlementCoordinator.cs:24`](../../../../Extensions/Trade/Client/TradeIntentSettlementCoordinator.cs#L24) 在应用独立结果之前要求库存 CanWrite，即使这笔 live 操作没有需要修改的库存 reservation 也会先返回。

这些是原本强调证明完整性的策略，迁移 SQLite 不会自行改变。需要把“已知远端结果”和“本地某项资源可否结算”分离，锁定相关数量而非整库；允许对事故操作显式 Abandoned，避免无限等待。不能仅删掉安全判断并把所有迟到事件当成功，事件与操作的匹配仍须保留。

### F10 / F12：已落盘不等于业务动作成功入队或全部订阅者收到

[`PhinixFrameworkTradeClientService.cs:588`](../../../../Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs#L588) 先落 receipt，之后 Decode、RepositoryChanged 和 OnTradeCompleted/Cancelled 直接多播调用；任何前置解析/订阅者异常可阻断后续流程。重发相同 completion 命中已记录分支，590–593 行直接返回，不再触发。未证明该路径在新日志发生，但源码存在实际中断窗口；开放 PR #2 涉及异常隔离，不能原样合旧架构补丁就宣称新版已解决。

默认交付通过 void dispatcher.Enqueue 提交，[`PhinixDefaultTradeBehaviour.cs:207`](../../../../Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L207)。pendingCompletions、save binding 和 inventory plan 都在动作实际执行后才建立；队列满拒绝只记 warning，[`ClientMainThreadDispatcher.cs:66`](../../../../Client/Source/Framework/ClientMainThreadDispatcher.cs#L66)。拒绝后可能只有 Received receipt，没有可恢复 plan；后续重放又只做 receipt 去重。恢复器对 plan==null 的记录直接跳过。这不是已修复的 TradeRecoveryPump 满队列重试问题，而是首次交付入口仍用 void adapter。

处理：使用可观察的任务结果、逐订阅者隔离；receipt 的接收状态与消费状态分开。至少把入队拒绝变成玩家可见的失败/放弃状态，不能静默假装已安排。若保留重试，只重试明确未执行的部分，不重复物化。

### F13 / F14：独立红包也存在相似的容量与阶段问题

红包 `RedPacketInventorySendStore.cs:44–78` 限制 128 MiB/2048 文件，Save 每次把所有旧 inventory-send 文件计入。`RedPacketInventorySending.cs:281–285` 结算后只是把状态写成 Settled 并移除内存字典，未删除或精简旧文件。因此已完成历史持续挤占新发送，目录满也可能拒绝现有记录的收尾更新。读取恢复时仍可能枚举所有未 Restored 的库存 reservation，终态 metadata 不会自然消失。

接收 `RedPacketReceiveRetry.cs:53–57` 在实际业务副作用前先置 Uncertain/Attempts 并 Save；Save 失败 catch 又强制 Paused/Uncertain，Retry 在 Uncertain 时不允许。与 F01 类似，需要区分“本地尝试标记未写入，尚未执行”与“所有权可能已转移”。影响是该条接收被保守挂起，未确认现场发生。

建议第二批将 sender custody/retry 元数据接入自己的逻辑库，仍由红包管理表和协议；保留小型终态去重、丢弃无用大模板。不要让 Inventory 管理红包表，也不要让 SQLite 自动认定 HTTP/relay 接受就是库存结算完成。

### F15：人才购买超时是会话级积累，不需要数据库才能解决

`TalentTradeManager.cs:1715` 最多 128 个 PendingPurchases；超时 1745 行只置 TimedOut，原 listingId 不能再登记且继续占槽；只在成功交付或 Clear 路径移除。PendingMarketPurchase 明确是内存状态，不是持久事务。128 次一直未解决的购买可耗尽该会话准入；未有该玩家现场证据。

在用户接受丢弃事故操作的策略下，可用明确放弃、按 listing 的终态和有界保留解决。既有 PawnReturnDelivery 已区分准备失败、交付未知及交付后通知失败，是应保留的控制。暂不为追求跨崩溃恢复把整个 Pawn/地图生命周期强行搬进 SQLite。

## 不建议迁移的表面及已有有效控制

| 表面 | 本轮观察 | 结论 |
| --- | --- | --- |
| TradeRecoveryPump | 可观察任务、QueueFull 保留原页重试、scope 取消可终止 | 已有控制；旧文档的 queued=true 风险不能当成当前缺陷 |
| PluginStore catalog/cache | 可重建数据；TryRead 失败作为无缓存、暂存文件后原子替换 | 不优先迁 DB；处理刷新降级与诊断即可 |
| Common 插件安装/移除 | 已有明确事务决策文件、文件所有权/清理状态 | SQLite 也无法把 DLL/目录安装并入一笔数据库事务；保留文件协议，专项验证其恢复 |
| 主题/设置 | 小型可替换文档/用户配置 | 暂不迁移，避免扩大 native 依赖和 schema 维护范围 |
| 身份凭据 | 独立保护、身份绑定和原子写入 | 不与普通库存/Trade 数据混库，不借此次变动凭据策略 |
| 服务端 SQLite | 已有 operation/state/outbox 原子提交、Busy/Capacity 分类、备份接口与领域配额 | 可借鉴分层；不是复制代码即完成客户端兼容 |

## SQLite 必要性评估

本地应用数据与多文件关联记录是 SQLite 的适用场景。官方明确支持事务内原子提交及相应崩溃恢复：[适用场景](https://www.sqlite.org/whentouse.html)、[原子提交](https://www.sqlite.org/atomiccommit.html)。据本轮源码判断，Inventory 与 Trade 已经自行实现了足够多的数据库职责，继续补文件存储的边际维护成本高于引入一个通用嵌入式存储模块。此为工程判断，不是测得 SQLite 性能一定更快。

| 方案 | 能解决 | 仍有成本 | 建议 |
| --- | --- | --- | --- |
| 仅补当前异常 | F01/F02、队列和回调可较快修 | 全快照、路径分叉、跨文件证据和寿命配额继续存在 | 先做止损，但不作为最终存储方案 |
| 全部文件改成 SQLite 内的 JSON 文档 | 获得原子存储 | 仍整份重写、仍可能错误配额、缺乏领域查询与清理 | 不采用机械搬运 |
| 通用 SQLite＋Inventory/Trade 各自结构化表 | 替代文件事务、按行写入/索引、原子状态与去重回执 | provider/native 打包、升级/备份、状态机整改 | 推荐 |
| 所有模块共用一套万能业务数据库 | 表面集中 | 版本、锁、故障和业务权限耦合扩大 | 不采用 |
| 全部库存上服务端 | 可集中远端资产权威 | 离线/身份/协议和玩法均重构，地图保存仍独立 | 本轮不扩大范围 |

SQLite 不会自动限制日志保留期、压缩终态 payload、避免错误 Unknown 或修复回调丢失；也不能让网络发送和游戏实体保存加入同一原子事务。服务端已用 SQLite，仍明确维护领域容量，恰好说明“用数据库”与“如何处理容量”是两件工程工作。

通用层提供连接、事务、schema runner、错误分类、一致备份；Inventory/Trade 各自拥有表/SQL，不互相借用数据库入口。默认每模块一个逻辑库；需要共同原子边界的数据放在同一 owner 的事务内，跨模块关系依赖幂等操作 ID，不声称多库事务原子。备份通过 SQLite 支持的快照接口，[官方备份说明](https://www.sqlite.org/backup.html)。

## 建议实施次序与退出门槛

1. **止损：**先区分 NotSent/TransportStarted，消费 false 返回、隔离终态订阅、首次交付使用可观察任务。与数据库引入可分别提交，不能让换数据库掩盖阶段错误。
2. **有限恢复策略：**定义每类操作的成功/拒绝/未发送/未知/放弃终态；允许事故丢失并提醒，不自动凭未知退款；无关条目继续使用。写失败本身不能删掉已保存余额。
3. **通用基础设施：**一个 provider、统一运行时/native 打包、短连接工作单元、事务、版本与备份；独立测试 schema 不引用 Inventory。支持平台先实测再承诺。
4. **Inventory：**增量余额/预留/回执，BLOB 存 payload，跨存档单一权威；旧快照一次导入，DB 提交后才随下一次保存清理。未知旧日志可放弃恢复并提醒。
5. **Trade：**intent/result/completion/delivery 使用自己表，保留最小持久去重；大历史 payload 有界回收，收尾预算独立。不要只迁 Inventory 留下 F03/F04。
6. **红包与其他消费者：**迁移托管元数据、核对新库存身份；人才/设置/商店不因存在 SQLite 就自动迁移。

验收至少覆盖本报告五项探针，以及容量拒绝时地图物品未发送可恢复、失败不报已提交、队列拒绝可见、单坏业务行隔离、终态清理后重复消息不二次交付、大 payload/多条目不触发隐含全库锁、旧档重复导入、切档和强杀后有限恢复。无需以金融级全局强一致或全面防作弊作为退出条件。

## 本轮实际验证

在隔离目录编译原始生产 `TradeOfferOperations.cs`，使用合成 service/同步 dispatcher：

- P1：发送委托在触及传输前抛异常，被标 Unknown、未 restore。
- P2：Finished 后匹配成功回包被忽略，pending 保留。
- P3：独立 durable result 对同一操作结算成功，作为对照。
- P4：使用相同 BCL serializer 测量合成 byte[] 的 JSON 膨胀。
- P5：按源码常量计算缺失证据槽的准入预算，属于数学模型，不是生产 journal 的全流程测试。

SDK 10.0.114；探针构建和五项观察/对照退出 0，测试替身的未使用事件/字段有编译告警。源码/结果指纹见 [probe-results.json](probe-results.json)。可复现入口：

```sh
python3 docs/branch-local/audits/Storage-Reliability-2026-11-10/probes/run-probes.py \
  --output /tmp/phinix-storage-audit-reproduction
```

探针断言的是“当前缺陷确实存在”，退出 0 不表示产品无缺陷。未运行完整构建、真实游戏、Windows/Waylith、多人网络、玩家资产断言、数据库迁移或故障强杀。不能用本报告声称 15 项均已动态复现或已修复。
