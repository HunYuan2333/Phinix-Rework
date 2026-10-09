# 本地插件安装：开发者模式增强计划

2026-10-09。状态：代码已实施，自动化回归与构建已通过，游戏内验收待完成。按用户要求已启动本地侧载与基础能力补齐；最小开发者闭环游戏验收前，skill 与其他新的作者工具工作全部阻塞。

统筹门槛见 [开发者基础能力交付计划](../Developer-Foundation-Delivery-Plan.md)。

## 目标与入口

- 直接导入符合 Phinix managed 包规范的本地插件 ZIP，供插件作者调试和游戏验收。正式插件仍通过商店发布与安装。
- 使用 RimWorld 原生 `Verse.Prefs.DevMode`：只有开发者模式开启时，商店工具栏显示“安装本地插件 ZIP”。窄窗口放入溢出菜单；关闭时不显示、不占布局位置。
- 已核对本地 RimWorld 1.6 编译引用：`Prefs.DevMode` 是静态布尔属性；原版 `Dialog_FormCaravan.DoBottomButtons`、`Dialog_LoadTransporters.DoBottomButtons` 等 UI 按该属性条件绘制开发按钮。当前客户端也使用它控制开发诊断。
- 按钮点击和最终安装确认均在游戏主线程再次检查开发者模式。不能只隐藏按钮而允许其他入口直接发起安装；关闭模式后取消尚未提交的安装意图，已完成事务仍按常规恢复规则管理。
- 关闭开发者模式只关闭新导入入口，不自动卸载、隐藏或删除已安装测试包，也不突然中断插件业务。扩展管理继续显示包的开发来源、状态及卸载操作。

## 安装与兼容边界

1. 用户明确选择 ZIP；文件选择交互支持 Windows/Linux，不自动扫描磁盘。输入复制到 Phinix 所有的临时区域，固定文件长度与摘要后再校验，防止检查后文件被替换。
2. 复用现有 managed 安装流程的清单、程序集身份、资源长度/哈希、解压大小、路径、依赖及冲突校验。开发模式不豁免完整性、依赖或事务恢复要求。
3. 本地文件没有 GitHub Release / Index 审批证据；明确记录为本地开发来源，不能伪装为官方已审核包。来源差异收敛在输入与来源策略，安装、回滚、启停和卸载继续使用通用流程。
4. 同包 ID / 同版本但不同哈希须有明确的开发修订策略，不能直接覆盖正式发布记录或正在加载的 DLL。开发修订如何标识，在实现前核对现有收据与缓存模型后确定。
5. 安装完成提示重启游戏后生效；不承诺 DLL 热替换。保留现有程序集、模块、协议及持久化标识，不改变物品归属、权威确认和整批交付规则。
6. 不生成传统 RimWorld Mod 的 About.xml，不向游戏 Mod 目录安装，不检查或改写其他模组文件。当前红包验收包的传统 Mod 包装只是临时测试载体，未来以本地 managed ZIP 导入替代。

## 后续实施与验收

- 第一步：核对安装控制器与收据模型，确定本地来源、开发修订、同名包冲突和缓存隔离方案，再实现输入适配。
- 第二步：开发者模式按钮、文件选择、安装确认和中英文来源提示；复用原事务并检查异步取消、重启与恢复行为。
- 第三步：验证模式开关、窄窗口溢出、模式关闭时未提交请求取消、正确 ZIP 安装，以及损坏包、缺失依赖、冲突包拒绝。
- 验证重启加载、重复导入、升级/回滚、启停/卸载、安装中断恢复；关闭开发者模式后仍能找到已安装测试包。
- 游戏验收通过后补充插件开发文档与 skill 的本地调试步骤。正式 main 自动 Release / Index 更新流程保持独立。

## 实施决定

本地来源使用保留 ID `local-development` 和独立 schema 2 收据，远端 repository/catalog 字段不写入；正式 schema 1 保持可读。输入复制到宿主扩展数据目录下的 `local-import` 临时文件，固定长度并冻结校验，完成/拒绝/取消后删除该临时文件。远端下载、本地导入、打包预检共用 Common 的 `ManagedExtensionZip`。

同本地 ID、同版本不同 ZIP 摘要允许修订，版本不允许降级、摘要相同拒绝重复导入。现有包须启用且内容可信；其他来源同 ID 拒绝覆盖。现有 journal/替换恢复负责在提交前恢复旧构建、提交后完成新构建。开发模式关闭不影响已安装包管理，不回滚已提交事务。

商店工具栏/溢出入口、文件选择窗口和确认都检查 DevMode；异步执行通过主线程 dispatcher 再捕获模式。文件选择支持绝对 ZIP 路径，或用户明确输入目录后浏览；不自动扫描其他磁盘/Mod。扩展管理展示开发来源，复制摘要包含当前/安装构建摘要与模块状态。快速操作见 [Local-Plugin-Quickstart.md](../../../Local-Plugin-Quickstart.md)。

回归覆盖无 catalog 导入、模式关闭拒绝、原 ZIP 被替换后仍安装冻结输入、缺依赖/同模块/同程序集/正式包冲突、同版本修订及中断恢复。游戏中的模式开关、窄窗口、重启加载与切档退出验收仍未完成。

## English summary

Implemented and covered by build/runtime regression checks; in-game acceptance remains pending. Skills and other new author tooling are blocked until sideloading and the minimum developer foundations pass game acceptance. The Store toolbar/overflow action and local ZIP selection/confirmation use RimWorld's `Verse.Prefs.DevMode`, with execution checks captured through the main-thread dispatcher. Disabling developer mode hides new import actions while installed development packages remain manageable.

Use an explicitly selected, staged and hashed local input, separate development provenance, and the existing manifest/resource/assembly/dependency validation and installation/recovery transactions. Define development revisions and official-package conflicts before implementation; never overwrite published receipts or promise DLL hot replacement. The feature does not create an ordinary RimWorld Mod or modify other mods. Cover invalid input, mode changes, restart, state management and interrupted-install recovery before adding developer-guide and skill instructions.
