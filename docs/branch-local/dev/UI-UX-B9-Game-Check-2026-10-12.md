# B9 商店主流程验收（全部待测）

B9 Host + Example 1.0.15，重启游戏；包含 B8 和先前修复。编译/56 stub presentation/109 composition/1000 fixture-backed Store assertions 不代表实机通过。

1. 商店主 tab 与设置打开的独立窗口均使用公共列表/字段；两处搜索/选择独立，共享真实 controller 状态。首次查询只在视图更新推进，Draw 不重复启动任务。
2. search/原三个 filters、版本列表、Workshop link、managed/local/official 来源标记正确；选择后详情可直接到达，长目录不把详情埋到底部。
3. installed version、desired、current、next、restart 分别显示；unknown inventory/offline/stale/withdrawn/不可恢复模块的 install/update/control gates 保持。
4. 用已有本地测试 repository/测试插件走 plan → 公共冻结摘要 → 显式确认 → 原安装 → 实际结果；摘要含所有依赖/替换/下载量，关闭确认不派发。确认期间切版本/刷新/另一视图改变状态/模块 stop，陈旧动作不得执行。
5. 原 cancel request、安装失败、saved-refresh-failed、copy diagnostics 准确；回调完成不能显示成运行成功。关闭 tab/window 不自动取消已提交任务。
6. enable/disable/单模块恢复/取消 removal/卸载确认保留原管理逻辑，待重启不能显示成当前已运行。勿用有价值正式插件测试破坏性移除，使用既有测试包。
7. DevMode local DLL 和主题本地包/编辑器原入口仍可达；本批未宣布其 UI 迁移通过。
8. wrapped tools/actions 窄窗换行不重叠，长文本/确认滚动可达；Example wrapped actions/reset frozen counter/task 正常。继续 B8 库存与 Chat 专项。

每项记录版本/包 SHA、操作、实际状态与日志/截图；当前无游戏验收证据。
