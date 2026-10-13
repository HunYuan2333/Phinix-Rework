# B36 分帧分组白盒重跑

生产 GroupingJob / GroupPhysicalSteps 与真实合成 XML/gzip serializer；Verse 替身，非游戏 Scribe。.NET10 Release，tiered compilation 关闭；13 fixtures / 35 pipelines，每条预热2次、测量15次。运行结果 raw: Phinix-Legacy-RedPacket/Tests/RedPacketPerformanceTests/Results/sliced-index.json，包含源码 SHA。数量守恒、精查次数断言通过。

一次 Advance 为 2 ms 软预算，最多 2048 小工作步骤。初版64步导致50000单例要1563次处理，已调整到2048步并重跑：49次。pendingItems 的 FromPhysical 回调计入实际 view slice 预算；本 benchmark 只测分组 job，不包含 GUI 回调。

| 样例 | 总中位 ms | slices | 最差单次 ms |
| --- | ---: | ---: | ---: |
| simple-same-500 | 7.89 | 4 | 2.02 |
| simple-same-1000 | 15.16 | 8 | 2.02 |
| simple-same-10000 | 17.08 | 20 | 2.06 |
| unique-def-10000 | 1.83 | 10 | 0.27 |
| unique-def-50000 | 9.29 | 49 | 0.27 |
| mixed-10000-100-defs | 17.05 | 20 | 2.02 |
| complex-8KiB-1000 | 76.96 | 38 | 2.19 |
| complex-32KiB-1000 | 78.57 | 33 | 2.62 |
| oversized-160KiB-200 | 56.63 | 17 | 3.97 |
| stateless-comp-5000 | 17.87 | 14 | 2.02 |
| unknown-stateful-10000 | 1.25 | 10 | 0.22 |

不能保证实际帧耗时≤2ms：单次 serializer/Scribe/GC 不可抢占；地图枚举、库存 preview、最终 filter/page 发布仍同步。分帧不会减少总 CPU 或总分配；例中复杂8KiB仍约77ms/86MiB，发送前复核500/1000源仍同步约7/15ms。连续 Advance 测的是处理总时间，真实每帧更新会增加完成等待时间。实际Unity/Mono、复杂模组物品和大型存档 profiling 待测。

刷新时显示准备状态并禁发送，完整结果一次发布，不每 slice 变更页面 revision。刷新、关 view、stop、Game/uuid变化取消未完成本地索引；不取消红包业务、返还或修改协议。未知/超限源仍独立可选，发送前仍完整复核。
