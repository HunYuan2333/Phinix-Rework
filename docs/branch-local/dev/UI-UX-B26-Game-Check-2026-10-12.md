# B26 游戏/联机专项（待测）

- 正常框架服务器：添加地图/库存报价，真实 success response 和相同 correlation delta 到达后结算一次；单向赠送/Reset 与重复点击继续检查数量。
- Reset success response 后，注入同 Trade 的其他 correlation delta/full snapshot：刷新页面但不让旧 token 返还；只有匹配 delta 进入原后处理。
- 缺 response/delta correlation 时不把 message ID 当结果依据，不盲目退款或重发。
- 两个独立 operation token、其他 Trade、重复 response（等待中）、重复 delta：只匹配对应事件；owner 本地处理不重复。
- delta-before-response 模拟：保留待确认，不用随后 unrelated/full sync 补造成功。此项没有自动恢复实现。
- 状态确认、LegacyAdapter 已维护路径和当前共享 facade 冲突保护回归。

191 stub / 177 actual DLL、编译与包检查通过；以上均待真实游戏/传输验收。完整 R44 编辑迁移、跨存档/重启物理托管恢复仍未完成，主题/T5 与 B21/B22 业务步骤继续保留。

- 匹配 correlation 但缺本地参与者/参与者重复的 delta 不确认 Reset、不制造己方空报价；此项 DLL 通过，真实异常包组合待测。
