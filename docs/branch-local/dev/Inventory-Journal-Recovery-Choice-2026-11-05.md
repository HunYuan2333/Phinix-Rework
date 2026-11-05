# 库存 journal 分支失败及恢复选择（2026-11-05）

## 现场与已确认因果

用户提供 `Player (10).log`，Windows / RimWorld 1.7 / Waylith，实际加载工坊目录中的模块。此处未读取或修改玩家存档、journal 或云服务器。

- 20:14:00.643：自动存档后 `RefreshAfterSave → Attach → OpenSavedBranch → InventoryJournal`，snapshot sequence=15，目标 journal 已存在但 `.authority` 缺失。不是仅仅“新保存路径还没创建文件”的旧缺陷，也不构成磁盘损坏证据。
- `Attach` 在候选 journal Faulted 后仍替换当前引用、释放原本健康来源。
- 20:15:39.687：手动保存再次走 OpenSavedBranch，却拿到了第一次失败的 journal，于是报 `Live inventory journal cannot authorize a saved branch`。第二条是已确认的级联失败。
- 日志不能证明孤立目标文件为什么缺标记，也不能证明它与 sequence=15 的快照完全一致。可能是旧格式、残留、缺失或损坏，不能仅凭异常名无条件补标记。

## 用户明确选择的策略

能由独立文件证据验证安全的情况自动接续；无法核对时默认只读，提供“保持只读”与“抛弃日志变更”。用户确认抛弃的语义为：**保留存档内库存，只放弃无法恢复的日志变更。** 不清空整个库存，不自动退款、重新发送交易或撤销服务端提交。

恢复存档快照是玩家明确选择的本地回退，可能舍弃存档之后的交付、扣除、预留或幂等记录，因此不能继续声称该分支具有完整的最新账本/资产守恒保证。服务端已完成交易不会随之撤销。此例外只发生在本机明确确认后，不开放网络强制重置能力，也不是自动处理 Unknown 的通用规则。

## 实现

1. `InventoryJournal.ValidateUnmarkedSavedBranch`：只有健康的存活源 journal、实际 `.rws` 保存证明、正确身份共同授权时，才允许迁移既有无标记目标；整份文件须校验格式、校验和、序号，最终完整状态必须与存档快照逐字指纹一致，不能含更新记录。过期、冲突、残缺和未来记录均保留原文件，不能补造 authority。普通冷加载不会仅凭相同字节自动获得此授权。
2. `BuiltInInventoryClientExtension.Attach`：候选路径失败时保留原健康 journal/锁/账本，不提升错误候选。当前路径不匹配及故障继续限制写入；后续有证明的新路径可以正常接续，不继承失败候选的 Faulted 状态。
3. `InventoryTab`：故障页增加现有通用 action/confirmation 合同内的两个动作；抛弃有独立确认文案，取消标签为“保持只读”。故障状态优先于普通恢复/提取对账，避免故障时藏起只读选择。原窗口布局、正常提取/交易界面不变。动作生效要求当前 Game、存档路径代数及呈现上下文仍有效。
4. `DiscardUnrecoverableHistory`：仅活动模块、游戏主线程、当前存档组件和真实可读的匹配存档可以执行。恢复的是已保存的完整 InventoryState，包括其仍存在的收据、预留、定时计划及载荷，不采纳损坏 journal 的猜测内容。
5. `InventoryJournal.DiscardToSavedSnapshot`：先验证快照/实际保存证明及可编码性，再独占目标锁；不通过目录/目标/标记/锁文件的直接链接操作。原 journal、原 authority 移入同目录 `<journal>.discarded-<guid>/`，保存 decision.txt（选择、序号、指纹）；以保存快照建立新的完整 anchor/authority 并 Flush。成功前不宣布 Ready；失败保留证据且仍只读。无 journal/authority 的情况也不伪造被备份文件。旧备份不自动清理。
6. 中英文文案说明丢失存档后变更的风险、备份、取消保持只读；没有添加全局日志系统。Server、Common/gitlink、协议、现代交易结果/ACK/inbox 均未修改。本包包含上轮 b00862a 的 Legacy 修复。

## 验证

产品基线 `b00862ac5ad1d3d9602caf8c81476e1e65f4c1f7` 加本轮代码；业务分支 `codex/p3-trade-business`。Common 固定 c165f61d749189f700ca896d63d9366d5c71dbf7，protobuf 固定 4b0c3aacf0657fbf38253b38918d3358dd4319ec。并行文档整理改动未覆盖/提交。

所有测试只用合成身份、隔离目录、合成 XML 存档/库存。实际执行入口（RUN 仅为现有 SDK 环境包装）：

```sh
RUN=/home/hunyuan2333/.codex/visualizations/2026/10/15/01a13ec4-18b2-7860-931d-fd14d2747341/p4-work/run
$RUN python3 tools/compatibility/build-dual-host.py \
  --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls \
  --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --output /tmp/phinix-inventory-discard-choice-20261105/build
$RUN python3 tools/quality/run-client-component-probes.py --suite inventory-checkpoint \
  --assemblies /tmp/phinix-inventory-discard-choice-20261105/dll17 \
  --game /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --output /tmp/phinix-inventory-discard-choice-20261105/save-callback17
$RUN python3 tools/quality/run-inventory-checkpoint-tests.py \
  --references /tmp/phinix-inventory-save-target-20261105/dll17 \
  --output /tmp/phinix-inventory-discard-journal-20261105
```

1.6 保存回调同命令替换 dll16、GameDlls、save-callback16。另分别以最终 dll17 执行 `--suite inventory-runtime` 和 `--suite inventory-composition`，输出 inventory-runtime17 / inventory-composition17。

通过：

- 双版本完整构建及包布局/目标程序集/ZIP CRC 审计。
- 1.6 / 1.7 保存回调及选择各 **61** 项：真实打包模块、完整快照/42 件合成载荷，sequence=0/2，新路径/手动/自动保存，准确旧目标接续；冷加载、过期/冲突/未来/截断文件仍拒绝；失败保留来源、后续正常保存；默认只读无隔离动作；真实故障动作/确认合同；用户选择后完整快照与备份均核对。
- 日志/检查点专项 **10,391** 项断言，含新增显式抛弃：旧文件逐字保留、真实保存证明拒绝、目标锁冲突、明确选择后恢复保存 anchor、重开追加正常、替换写入失败仍保留备份并拒绝隐式 Ready。既有检查点强杀/固定调度回归同时通过，但不能当作新增“抛弃替换”路径已完成强杀验证。
- 库存运行 **21** 组场景；库存组件 **85** 项断言。
- 两个负对照在上轮发布测试包真实 DLL 上失败：准确无标记目标不能接续；临时只执行切换失败子用例时，原健康 journal 没有被保留。不是修改测试期望来接受旧错误。
- 中英文 XML 解析/键唯一性、git diff --check 通过。

实际 manifest/test.log 及产品指纹随测试包 evidence 保存。各轮固定构造数据，无随机 seed、无生产网络故障点；没有实际玩家资产断言。

## 实机验收与限制

先备份存档和 `framework-extensions/client/builtin.inventory`，完整退出游戏替换完整包。不要手工删 `.authority` 或 journal。

1. 原本健康的测试存档：手动保存、另存为、轮换自动保存，库存数量/opaque 状态应不变，退出重开核对。
2. 故障存档：打开库存应保持只读；点击“保持只读”或取消抛弃确认，文件/数量不变。
3. 自愿选择回退时：点击“抛弃日志变更”阅读风险并确认，应回到**存档中已保存的库存**；核对 `.discarded-<guid>` 中原始文件仍在，再保存、重启核对。存档后的增减可能不保留，这正是本次明确选择。
4. 保存文件不可读/内容不符/其他进程持锁时不得恢复可写；保留原文件及新增备份用于核对。

未执行 Windows/Waylith 实机、多 Mod 现场、NTFS 掉电/目录持久化或新重置步骤强杀；不将 Linux 组件测试等同于这些验收。用户原文件的缺标记来源及实际可恢复内容尚未核实，不能声称这份 sequence=15 的库存已在玩家机器上修复。
