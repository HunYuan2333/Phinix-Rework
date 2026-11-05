# 库存 Journal 紧急修复候选（2026-10-27）

工坊基线：6fe404e，用户已上传的 10/21 UI 包。首先在 UI 工作区修复；P3 只适配库存部分，保留既有业务改动。无协议、Common/vendor pin 或服务端产品变化。本记录不是全项目生命周期审计完成报告。

## 确认的缺陷与状态模型

- InventoryJournal.TryAppend 的容量/载荷拒绝静默 false；UI 只显示通用提取失败。旧版负对照真实写到上限，因未发布 Faulted 明确失败。
- RefreshAfterSave -> QueueAttach -> Attach 对相同 component/path/journal 提前返回，无 checkpoint。原包真实保存回调负对照复现日志没有回收。
- recovered 保留每个完整历史状态；现在逐条校验但仅保留最后一个完整状态。P3 额外累计“所有未来状态是否改变归属”，没有用最后一个状态掩盖中间变更。

运行中 InventoryLedger 是已发布本地状态；TryAppend 写入完整候选状态并 Flush(true) 后才能 Publish。提取先持久预留，再执行真实交付，之后持久结算；中间故障仍保留待核对，不自动返还/重投。物理游戏副作用和游戏保存从未被 journal 数量当作成功证据。

.rws 保存了对应 component 的 saveId 和库存全量 JSON；journal 是此路径的后续写前状态。加载仍验证 checksum、state、单调序列、未来连续性及同序列全状态一致性；未来分支显式批准后才成为写入基线。手动拒绝规则不变，P3 保留既有归属变化否决。

## 保存、压缩及失败边界

InventorySaveCheckpoint 用读写文件 lease 拒绝并发写/删除；完整解析实际 .rws 到文档结束，要求 savegame/game/components 下唯一正确 component、匹配 saveId 与完整规范状态；P3 另外核验 journal authority。随后 Flush(true)。不把 SaveGame Postfix、Sequence 或文件存在当作保存成功。

RefreshAfterSave 捕获 component/path/activation generation，派发主线程并再次验证归属/路径。读取失败、失败保存留下旧内容、半份 XML、同序列不同状态、错存档或排队时切游戏/路径：不执行回收。成功后 Attach，独立调用 checkpoint，避开相同 attachment 的 early return。加载时构造本身只校验；模块必须另取实际文件 lease 才能执行维护。

压缩在 journal 内部 gate 下与 append 互斥。写 .compact：保留已确认 .rws 的完整 anchor（Sequence>0），原样保留其后全部完整记录。临时文件 Flush(true) 后用 File.Replace 原子替换；源原本不存在才 File.Move。不先删除源文件，不因替换失败改用非原子 fallback。首次有效 checkpoint 可以重新写入旧遗留的 .compact；启动不删除或提升孤儿临时文件。

受控异常前原文件保留；替换后进程死亡恢复到新文件。torn/rejected 原证据保持既有保留策略，不主动清理真实玩家文件。磁盘满/权限/文件占用按实际 IO 错误拒绝，故障注入不冒充真实系统盘故障。目录元数据在真实断电下的持久性、Windows/Mono 文件系统语义未专项验证，不声称掉电验收通过。

## 容量、错误与兼容

正常追加仍限 128 MiB，单 payload 8 MiB、encoded record 16 MiB。旧文件验证扫描单独硬限 1 GiB，允许先核对超过旧 128 MiB 上限的历史并在实际快照证明后回收；不是无限扩大写预算。超过扫描限仍只读，需离线处理。未保存、有效快照太旧、未覆盖未来记录太多时依旧可能触发容量拒绝。

容量/序列化/IO/关闭错误具有阶段、journal 文件标识、sequence，容量含大小；不记录完整 payload/绝对玩家路径。持久化错误进入 Faulted，API 的 availability 与 UI 故障一致；提取失败立即附带原因。重复/跳号及等待恢复属于可诊断拒绝，不把正常调用拒绝变成永久损坏。容量故障只在有效压缩后解除，其他损坏不会被维护清除。

不改变旧 envelope、JSON byte[] 编码、saveId、持久字段或 API/protocol。独立 literal 旧 schema fixture 缺少后来新增的 Reservations，升级可读；原发布版本 journal reader 能读取新压缩 anchor+future，并仍要求显式批准。不能因此保证任意很旧备份都可无损回退：早于保留 anchor 且无法证明连续性的快照明确拒绝。

## 验证与剩余范围

本轮 Deliverables 保存命令、源码/DLL/fixture hash、负对照失败及后续修复；单元文件测试含 >128 MiB 合成日志、旧 schema、1 万次操作、实际保存文件核对、保留未来、未完成提取、并发维护、容量解除、三固定 barrier 强杀/重启和独立 raw-record oracle。真实包的保存回调与原有库存 runtime 场景分开运行。

所有数据为合成身份/隔离目录，未读写真实玩家库存。自动检查不是 RimWorld Thing 物化、实际资产守恒、原生 UI 或游戏存读档验收。编译支持 1.6 与 1.7（引用 1.7.4977）；net10 中执行 1.6 DLL 的探针不等于 Mono/当前 1.7.4981 Waylith 验收。

后续：真实老存档复制件与库存目录备份后加载/保存/提取/再存读档；失败存档、自动存档轮换与另存为引擎行为；Windows/Mono 和真实存储故障。P1 payload codec/Base64、全项目资源审计、历史孤儿 journal/证据文件总量清理未完成，不自动删除它们。


## 此切片资源清单

| 资源 | Owner / 创建 | 回收 / Bound | 失败与恢复策略 |
|---|---|---|---|
| 当前 .journal | 每个 saveId+路径的 InventoryJournal | 实际快照证明后 checkpoint；写 128 MiB / 扫描 1 GiB | 不删未覆盖未来；超限明确拒绝 |
| .lock | Journal 构造持有 FileStream | Dispose/进程退出释放 OS 句柄，空锁文件保留 | 独占失败只读，不窃取活锁 |
| .rws lease | 单次 InventorySaveCheckpoint.Open | using 关闭，文档扫描 512 MiB | 文件不符/占用/不可刷盘则不回收 |
| .compact | 单次 checkpoint | 刷盘后原子替换，同路径最多一个候选 | 启动不提升；失败保留原始 authority |
| 恢复状态 | Journal 实例 | 仅最后完整状态；Dispose 释放 | 历史仍逐条验证；P3 另外保留 ownership veto 累积位 |
| torn/rejected 证据 | 既有 Journal 修复/用户拒绝路径 | 本轮不自动清理，跨存档总量未界定 | 不碰真实证据；全项目资源审计待后续 |

已验证的最终包：工坊候选双版构建和 92 文件审计；两版实际保存回调各 3 检查、原有库存 runtime 各 21 场景；独立文件 suite 10366 断言，旧 reader 消费压缩格式通过。P3 同步独立文件 suite 10368 断言、1.7 保存回调 3、库存组合 85、交易组合 291 检查。测试仅声明实际完成的层级；完整命令和失败原因以 Deliverables manifest 为准。


## 自动兼容维护器（同日后续切片）

`InventoryJournalCompatibility.cs` 是独立的内部兼容维护代码，随现有库存插件加载，不注册新插件、UI 或协议能力。不使用“已经迁移一次”标记跳过后续必要核对。当前存档挂载及保存后自动调用，继续由 InventorySaveCheckpoint 提供实际文件证明、InventoryJournal 执行唯一的原子回收逻辑。

只有成功且实际减少的字节数才记 INFO：`[Inventory][Compatibility] Reclaimed … bytes … sequence=…`。无法取得证明/压缩失败记 history retained；不输出绝对玩家路径、saveId 或 payload。重复运行不报告虚假的回收量；日志观察者抛异常不改变持久化结果。

“无感”指无需额外安装、按钮或恢复确认即可回收**已证明覆盖**的历史，不代表自动批准未来记录。工坊旧存档可在首次有效挂载时维护；P3 额外要求 actual save 的 journal authority 匹配，旧存档尚未保存新 authority 时会延后到首次成功保存，不能为自动清理放松身份绑定。

范围只涉及已挂载的 journal，不能自动遍历/删除其他存档的 journal、torn/rejected、用户物品、幂等凭证或预留账本。新入口测试逐项核对这些相邻文件字节保持原样。超过 proof/scan 边界或无法验证的数据仍保留，不以安装新版为删除依据。

先在工坊/UI 基线实现，再将同一热修复向 P3 业务工作区移植；没有从 P3 向 UI 引入业务代码，原 UI 产品文件 hash 保持不变。新的包/最终 DLL 验证和命令 hash 见 `Deliverables/Phinix-Rework-Inventory-Compatibility-2026-10-27-02/`；原 hotfix 包保留不覆盖。
