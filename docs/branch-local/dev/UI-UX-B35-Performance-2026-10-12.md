# B35 红包分组白盒性能评估 — 2026-10-12

粗筛对大量互异候选有效；同桶复杂物品仍受序列化/解压/分配主导。测量发现并修复了失败解压不扣总预算的问题。游戏性能未验证，当前刷新仍同步。

环境：Linux x64、AMD Ryzen 7 9800X3D、.NET 10、Release、DOTNET_TieredCompilation=0。每个场景/管线预热 2 次、采样 15 次；每次测量前强制 GC。链接实际分组/发送准备源码，Verse 和 serializer 为测试替身；serializer 真正构造 XML/gzip。fixture 创建与测量后原堆守恒断言不计时。分配量为当前线程累计分配，不是峰值常驻内存。

| 场景 | 管线 | 中位 ms | 样本 p95 ms | 分配 MiB | 序列化次数 | 输出组数 |
|---|---|---:|---:|---:|---:|---:|
| simple-same-500 | coarse-index | 7.54 | 7.99 | 13.02 | 500 | 1 |
| simple-same-500 | eager-reference | 7.39 | 7.66 | 12.98 | 500 | 1 |
| simple-same-1000 | coarse-index | 15.28 | 15.60 | 26.03 | 1000 | 1 |
| simple-same-1000 | eager-reference | 15.38 | 15.73 | 25.96 | 1000 | 1 |
| simple-same-10000 | coarse-index | 16.13 | 16.80 | 28.24 | 1000 | 9001 |
| simple-same-10000 | eager-reference | 15.53 | 15.96 | 27.31 | 1000 | 9001 |
| unique-def-10000 | coarse-index | 1.47 | 1.52 | 3.59 | 0 | 10000 |
| unique-def-10000 | eager-reference | 16.03 | 16.24 | 27.58 | 1000 | 10000 |
| unique-def-50000 | coarse-index | 7.42 | 7.60 | 17.09 | 0 | 50000 |
| unique-def-50000 | eager-reference | 18.05 | 18.60 | 33.22 | 1000 | 50000 |
| mixed-10000-100-defs | coarse-index | 16.36 | 16.56 | 28.22 | 1000 | 9010 |
| mixed-10000-100-defs | eager-reference | 15.78 | 16.20 | 27.35 | 1000 | 9100 |
| complex-8KiB-1000 | coarse-index | 78.31 | 78.75 | 86.22 | 507 | 495 |
| complex-8KiB-1000 | eager-reference | 76.96 | 78.66 | 86.14 | 507 | 495 |
| complex-32KiB-1000 | coarse-index | 78.68 | 79.40 | 72.63 | 128 | 874 |
| complex-32KiB-1000 | eager-reference | 78.88 | 79.88 | 72.54 | 128 | 874 |
| oversized-160KiB-200 | coarse-index | 55.65 | 56.98 | 33.30 | 32 | 200 |
| oversized-160KiB-200 | eager-reference | 93.02 | 94.73 | 65.66 | 26 | 200 |
| stateless-comp-5000 | coarse-index | 17.74 | 18.22 | 29.18 | 1000 | 4001 |
| stateless-comp-5000 | eager-reference | 15.42 | 15.78 | 26.51 | 1000 | 4001 |
| unknown-stateful-10000 | coarse-index | 0.75 | 0.81 | 1.62 | 0 | 10000 |
| unknown-stateful-10000 | eager-reference | 0.65 | 0.83 | 1.47 | 0 | 10000 |
| send-recheck-500 | send-recheck | 7.39 | 7.54 | 12.74 | 502 | 1 |
| send-recheck-1000 | send-recheck | 14.48 | 15.03 | 25.46 | 1002 | 1 |

## 发现与修复

200 件超大状态：修复前 344.27 ms / 207.83 MiB / 200 次序列化；修复后 55.65 ms / 33.30 MiB / 32 次。原失败路径在 ComparableState 解压拒绝时跳过 characterBudget 扣除，每件都进入精查。现在失败也保守消耗一次最大检查成本，剩余源保持单堆可选；不修改发送保存上限或结果语义。新增单元回归修复前失败，修复后通过。

## 评估与下一步

- 10000 个 coarse 单例约 1.47 ms、0 次 serializer，对逐项精查参考约 16.03 ms 有明显收益。50000 单例仍需扫描/分组约 7.42 ms，不能认为常数成本。
- 同类 1000 简单堆约 15 ms，与逐项精查相当；stateless component 索引本身有少量额外分配/分类成本，不宣称所有场景提速。
- 同桶 8/32 KiB 状态约 78 ms、72–86 MiB 过程分配，即使预算限制精查数量仍可能造成同步卡顿。超限修复后约 56 ms，也不是小帧预算。
- 500/1000 原堆发送复核约 7/14.5 ms；必须保留复核，不能通过复用过期 UI 指纹跳过验证。
- 优先后续做主线程分帧精查、按实际 Scribe 与 XML 节点成本测量；可能降低精查字节预算与临时 XML 分配。不对未通知变化的组件使用跨刷新状态缓存，不把 serializer/Verse 操作直接移到后台线程。

## 测试边界

eager-reference 是相同 1000 堆/4 Mi 字符预算下的白盒逐项精查参考，不是完整旧版 UI；它不包含旧两两预分组或地图收集成本。不同访问顺序影响预算覆盖及输出组数，测试要求源实体和总数量完整，不要求预算合并结果相同。所有计时均非 RimWorld Mono/Unity 实机帧耗时，不能用于承诺具体玩家不卡顿或业务交付通过。

可重复命令及原始 JSON：Phinix-Legacy-RedPacket/Tests/RedPacketPerformanceTests/README.md 与 Results/*.json。本次 13 个场景、24 条管线，各 15 次，另有每条 2 次预热；31 repair cases、29 实际模型/fake session 回归通过。性能时间无 CI 硬阈值，正确性/预算/源守恒失败会退出。
