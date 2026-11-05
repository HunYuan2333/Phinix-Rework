# P3 意图日志容量与结果空间（2026-10-29）

## 范围与不变量

AUD-07 前置子切片；关联 SV-33/36/38、TQ-30/31 客户端子集、PF-07、FR-12。只改 `codex/p3-trade-business` 的 Trade 插件及测试，不跨分支合并，不改 Host UI、Common gitlink、协议、schema 或服务器产品代码。原普通 Host 实机包保持不变。

用户反馈此前普通 Host 测试包“没问题”，记录为用户反馈通过；没有逐项用例/平台记录，不能扩展为全部 Windows、P3、人才插件或真实资产守恒验收。

不变量：拒绝新意图时不创建意图/attempt/binding；不删除任何旧证据；同 ID 原意图仍可查，原结果持久化及去重保持；Unknown 不转退款/重发许可。容量预留是本地准入措施，不是数据库 COMMIT、网络 ACK、磁盘空间或游戏保存证明。

## 已确认问题与修复

1. `TradeIntentJournal.ReadAll` 原来只统计 intent/result/attempt/query，漏掉 reservation 和其他残留文件；`Publish` 又使用另一套统计，并用完整路径包含 `.tmp-` 排除文件。启动与写入的容量判定不一致。
   - 新增唯一 `EvidenceBytes`：检查目录和每个文件的祖先链接，按全部文件的逻辑长度统计；reservation、未知文件、强杀临时文件都计入，不盲删。保留原 64 MiB 和 1024 条上限。
2. `Prepare` 原来只检查当前意图能否写入。接受意图后，attempt/原始响应/query/绑定可能因同一预算满而无法保存。
   - 新意图准入前，为所有已有意图尚未出现的证据槽预留：attempt 64 B、原结果最多 2 MiB、query 最多 8192 B、offer reservation 最多 2 MiB。
   - 槽已落盘后用实际大小计算，未使用的预留自然释放；即使先有 query，仍为可能晚到的原始响应保留槽。
   - 对既有意图的精确查找/结果写入不重新施加新增准入门槛，只执行实际物理记录预算，兼容此前没有预留空间的记录。
3. `BindReservation` 原来可发布超过读取上限的绑定，也未先核对意图确实持久化。
   - 写前 Resolve 原意图、检查绑定不超过 2 MiB；冲突原绑定保持不变。防止自身生成重启无法读取的证据或 orphan binding。

这是保守预留。64 MiB 内约可同时容纳 31 条尚无结果的普通小意图，或 15 条尚无 binding/结果的报价意图；已有文件/载荷会进一步降低。不是提高可支持交易数量，也不是永久存储扩容。终态冷归档仍未实现，不能把有限记录容量问题标为关闭。

## 回归与实际执行

环境 Linux、SDK 10.0.113、runtime 10.0.13；Client HEAD `53c92ce8eb314dcb8ca422ae4b6891a007931128` 加未提交修改，Common `c165f61d749189f700ca896d63d9366d5c71dbf7`。1.7 引用目录 `local-compatibility-analysis/references/1.7.4977-rev1248`；synthetic FrameworkV2，合成身份/隔离真实文件，固定边界而非随机测试，seed 不适用。

证据在工作区 `Deliverables/Phinix-Rework-P3-Intent-Capacity-2026-10-29/`，含命令、源码快照、hash、成功和失败日志；不含游戏 DLL、玩家数据或可安装包。

- `python3 Phinix-Rework-P3-Business/tools/compatibility/build-net10-graph.py --game /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-intent-capacity-graph-20261029`：初始 26 项目构建通过。
- `dotnet build /tmp/phinix-intent-capacity-graph-20261029/Joint.csproj -c Release --no-restore -t:Rebuild -m:1 -nr:false -p:BuildInParallel=false`：最终完整 26 项目图通过，80 个既有告警，0 错误。
- 证据内 `run-tests.py negative3`：编译通过，旧产品源上相同“无结果空间应拒绝”预期实际失败（进程 -6）；不是将错误设为通过。
- `run-tests.py final4`：L1/L2 意图套件 59 检查通过。含近容量准入拒绝、未完成操作预留、实际结果释放预留、近 2 MiB 响应、旧记录结果可继续落盘、offer/binding预算、超限启动保护、孤儿绑定拒绝、原 scope/hash/批次和去重测试；既有 L4 子进程 barrier `intent.attempt-before-transport` 后强杀/独立重开仍为 OutcomeUnknown。
- `dotnet /tmp/phinix-intent-capacity-20261029/inbox/bin/Release/net10.0/Probe.dll`：原接收日志 95 检查通过，含 `committed-receipt-held` 强杀及去重；未修改 inbox 产品代码。
- `python3 Phinix-Rework-P3-Business/tools/quality/run-client-component-probes.py --assemblies /tmp/phinix-intent-capacity-closure-20261029 --game /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-intent-capacity-wire-20261029 --suite trade-wire`：实际编译适配器 34 项 wire 检查通过。

历史失败保留：首轮新增测试漏 namespace、产品 helper 参数误传 byte[] 已修正；一次在完整 Rebuild 尚未结束时启动依赖 DLL 的测试，发生引用暂缺。待构建退出后重新执行有效测试，没有改变预期、隔离失败用例或无限重试。初始 graph 指纹早于本轮产品改动；最终产品 hash 以本轮 manifest/source 为准，最终编译以 rebuild.log 为准。

## 兼容性与剩余依赖

没有格式迁移、没有自动删除或旧记录提升；已有超过 64 MiB 的目录会保留原文件并明确拒绝加载，未知残片现在会消耗预算。不得通过自动移除 Unknown/原结果绕过限制。原格式仍可读，但旧 Mod 不具备新准入保护。

限制：不是操作系统磁盘空间预留。外部程序占盘、多个进程或多个 journal owner 并写、实际断电、Windows Junction/Waylith、1.6 Mono 与真实游戏存读档本轮未验证。本实例锁只保护此 journal 实例；现有普通部署单 owner，未增加跨进程锁。

后续：安全终态归档必须保留原 operationId/fingerprint/result、已交付去重证据、save checkpoint 与回退约束；不能仅凭服务端 Committed 就删除客户端 binding。另在 `TradeIntentSettlementCoordinator.Schedule` 和 `TradeDeliveryRecovery.Schedule` 静态核对到同类 void 队列拒绝会遗留 queued=true 的路径，下一切片先为这两个恢复协调器补确定性回归及完成闭环，再接归档。尚未把该发现标为运行复现或已修复。

P3/P6 仍未整体验收；不重新要求用户测试未生成候选的代码。未提交、推送、发布或部署。
