# B21 合批游戏验收记录（待测，2026-10-12）

按用户要求暂不催单独复测，后续合批验收。本清单含 B19 主题/赠送、B20 list 和 B21 owner/reset；源码/编译/180 stub/134 DLL checks 不代替游戏。使用 B21 Host + Example 1.0.28，完全重启，记录 SHA/分辨率/scale。

1. 地图和库存各选物品→提交报价→请求中禁用同 Trade 冲突更新/确认/取消→真实成功后报价更新；同时开同一 Trade 两窗也不能重复 stage；另一个 Trade 仍可操作。真实 shared facade/第三方入口不能绕过本会话 pending gate。
2. 提交后关闭原编辑窗/切 tab/关主窗→不取消、不返还→真实成功一次 commit、真实拒绝一次恢复；重开能查看 pending；重复/迟到/foreign ID/token 不重复处理。
3. 非空报价→Reset：发请求前不落物品；真实匹配清空结果后才完整返还；明确拒绝保留旧报价且不返还旧 offer。发送异常/不匹配 snapshot、未知完成顺序不自动返还。空 offer reset 不创建空 pod。
4. 注入库存 commit/restore 写失败：地图 escrow 不被销毁或返还，交易页显示本地受阻。明确本地原因解除→公共确认重试→只重试本地处理，不重新提交报价；重复 result 不触发自动重试。
5. 断线/无可投放地图/丢 codec/部分投放异常：区分 Rejected 与 Uncertain；Unknown 不出现可盲目重发/返还的操作。真实已知处理受阻的恢复记录在 active trade 消失后仍可选；成功处理才移除该记录。
6. module stop 后 queued callbacks 不执行、不自动退款；原 editor 关闭。**跨存档/切游戏/进程重启的 physical escrow 恢复仍是未完成业务门槛**，本批未新增保存格式，不列为已经安全通过。
7. Example 计数/字段/滑块/颜色样本、固定/分栏和公共动作目录继续正常；呈现说明不把 callback/task 成功当业务确认。未迁原 editor/设置/第三方入口仍可达。
8. 合并 B19：单向赠送双方、主题草稿切换/无效修正/热应用/导出/本地包安装更新卸载及 T5 恢复故障；B20 取消确认/等待/multi-trade 关窗；库存/IME/时间戳/默认配色各自记录。

尚无本批游戏通过记录，出现问题记下具体操作、真实日志、构建 SHA 和截图。
