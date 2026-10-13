# B22 合批游戏验收（全部待测）

按用户要求留到后面一起验收。候选和 SHA 见 UI-UX-B22-Build-2026-10-12.json；B19–B21 原记录继续有效，不把编译或 DLL 检查记作游戏通过。

- 正常同 Game：添加地图/库存混合报价，成功、拒绝、Reset；检查数量与重复回执，当前地图改变保持原投放偏好。
- pending 时返回主菜单/加载另一存档，再收到匹配报价结果；旧窗口关闭，当前存档物品/库存不被旧操作修改，owner unknown 保留，无盲目重试。
- 已知 local blocked 时切游戏，再手动本地重试；不向新 Game 结算，进入 unknown。
- 关闭窗口不取消 operation；重开继续原状态，同 Game 正常响应。
- 停用/重新激活后旧 operation scope 不被复活。
- 库存 journal 原预留记录与当前 ledger 隔离；进程重启 physical escrow 重建仍未实现，不作为可通过项。

完整 R44 报价 Host 迁移和跨存档恢复尚未完成。T5、主题与早先 UI 验收按 B19–B21 清单合并。
