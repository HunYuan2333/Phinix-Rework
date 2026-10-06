# 商店 Tab 与扩展管理入口实现

日期：2026-10-03。分支：`dev`。本记录补充四批本地预览代码，不代表联网、CF/R2、下载安装或正式发行完成。

## 游戏内入口

- 原宿主“扩展”顶层 Tab 不再注册。独立商店模块通过正常 `Register` 注册 `IMainTabProvider`，排序 999，处于原“扩展”位置，与聊天、交易、红包等提供者并列；该 Tab 默认绘制商店页。主窗口的其他 Tab、初始选择规则不变。
- 商店页面顶部固定“扩展管理”按钮，列表滚动、读取/规划任务期间仍可打开。浏览内容提取为 `PluginStoreView`，Tab 与模组设置的独立预览窗口复用同一绘制实现和控制器，不把商店实现放进宿主。
- 按钮调用通用 `IClientExtensionManagementWindowService`，宿主打开 `ExtensionManagerWindow`，复用原 `ExtensionManagerTab` 内容及开关/依赖/日志/待重启显示。服务限制主线程并避免重复窗口；关闭后可重新打开，打开失败后可重试。
- 商店不存在或禁用后，宿主 Phinix 模组设置仍提供独立管理按钮和既有开关，可用于重新启用商店。所有已发现内置、官方、第三方扩展继续使用相同静态激活策略；依赖被禁用时沿用原前置恢复规则。
- 开关保存既有 DisabledExtensions 设置，重启游戏生效，不热卸载 DLL、不改变 RimWorld 模组启用列表、不改变协议、历史数据或物品所有权。
- 红包、人才贸易未在本批拆包；仍按原发行布局提供各自 Tab。

## 代码与兼容

新增通用接口 [IClientExtensionManagementWindowService.cs](../../../../Client/ClientExtensionAbstractions/Framework/IClientExtensionManagementWindowService.cs)，客户端抽象当前程序集/文件版本 `1.3.0.0`、兼容版本 `1.3.0`。现有窗口服务成员与 `ExtensionManagerTab` 类保持兼容，新增源文件已显式加入旧格式项目。

商店仍只引用抽象和 Utils，不引用宿主工程；宿主不引用商店工程。预览须配套更新宿主与抽象 DLL，不随商店携带第二份框架。中英文入口、预览 README、About、设计哲学和开发者指南 §8.19 同步更新。

代码入口：[主 Tab](../../../../Extensions/PluginStore/Client/PluginStoreMainTabProvider.cs)、[商店视图](../../../../Extensions/PluginStore/Client/PluginStoreView.cs)、[宿主管理窗口](../../../../Client/Source/GUI/Windows%20and%20panels/ExtensionManagerWindow.cs)、[主线程与窗口控制](../../../../Client/Source/Framework/ClientExtensionManagementWindowService.cs)。

## 实际验证

```bash
dotnet build Extensions/PluginStore/Client/PluginStore.Client.csproj --configuration Release -p:BuildInParallel=false -m:1 --no-restore
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1 --no-restore
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release -p:BuildInParallel=false -m:1 --no-restore
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release -p:BuildInParallel=false -m:1 --no-restore
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release --no-restore
python /tmp/check-phinix-store-tab-artifacts.py
git diff --check
git status --short
```

商店 net472/C# 7.3 编译通过；普通顺序 solution Build 最终通过，0 错误、17 个 NU1900 警告（当前网络无法读取 NuGet 漏洞信息）。首次宿主编译发现 `PhinixClient.GUI` 与 Unity GUI 名称冲突，已改为限定 UnityEngine.GUI 后通过。新增框架回归覆盖重复打开、关闭后重开、后台请求不调用游戏回调、拒绝后主线程恢复及创建失败重试；框架 harness 全部通过。商店 231 项、响应式布局 harness 全部通过。商店首次回归仍断言抽象 1.2.0，按本批显式升版改为 1.3.0 后通过。

产物检查参照 `.github/scripts/check-artifacts.ps1` 核对 26 个必需文件、LoadFolders、预览身份、中英文管理入口及输出与实际构建 DLL 摘要一致；预览只有自身一个 DLL，分发目录未包含游戏/Unity 引用 DLL。当前没有 pwsh，未执行原 PowerShell 脚本；Python 替代检查通过。没有运行 `--no-incremental` Rebuild，也未解决既有跨目标清理问题。

真实游戏加载、绘制、设置持久化与启用/禁用重启仍未验收；无游戏 harness 与编译不能证明这些行为。

## 本轮游戏黑盒验收

1. 将更新后的 `Output/phinix-rework` 和 `Output/phinix-plugin-store-preview` 放入标准本地 Mods，启用并重启；主窗口应出现“商店”，不再出现独立“扩展”Tab，原聊天/交易/红包/人才入口正常。
2. 商店载入 [chain.catalog.json](../../../../Tests/PluginStoreRuntimeTests/Fixtures/chain.catalog.json)，源 `test.local`，选择 `a 1.0.0` 得到 `c → b → a`。跨 Tab 切换后浏览输入和选择保留，任务及计划不因切换取消。
3. 顶部管理按钮打开一个管理窗口，重复点击不产生多个窗口；关闭后可重开。检查扩展状态、来源、依赖、最近日志和待重启提示。
4. 关闭一项可独立禁用的内置扩展：当前进程显示待重启，重启后该功能消失；从管理窗口重新启用并重启恢复。检查依赖影响提示，避免把当前运行状态误认成立即停用。
5. 停用 `phinix.plugin-store` 并重启：商店入口消失，但 Phinix 模组设置的管理入口仍在；从设置恢复商店，再重启后 Tab 恢复。这里停用的是框架扩展开关，不必移除商店模组。
6. 检查主菜单/存档、中文/英文、窄窗口与 UI 缩放：管理按钮始终可达，窗口处于屏幕安全区，列表/日志滚动正常，开关没有被文字遮挡。记录 Player.log 错误与 UI 缩放。

## 后续发行定位补充

2026-10-03 用户要求商店后续达到类似 Trade 的功能模块级别。当前排期据此采用独立工程/DLL、统一生命周期与禁用规则、随主包交付的官方商店模块；按实际公开 API 需要再抽取 Contracts。CF/R2 是分发后端，不因此增加联机服务器上的商店业务。

本次只调整后续交付计划和中英文预览说明，没有修改当前代码或构建输出，也没有重跑构建/游戏测试。当前主包 + 独立预览的测试方法仍适用。正式整合时需更新 DLL/翻译复制、产物检查和 CI，验证单副本发现及启用/禁用恢复，并提示玩家移除旧预览副本；保持原模块、程序集、设置身份。游戏验收和 CF 后端实施顺序保持不变。

## 首轮游戏问题与修复

2026-10-03 用户提供真实游戏截图与堆栈：主商店页能绘制，`test.local` 样例能载入并选择 a/b/c；点击管理窗口在 `ClientExtensionManagementWindowService` 被主线程检查拒绝，点击计划同样显示 `Capture client environment on the main thread before starting background work.`。这证明前一版回归没有覆盖模组加载线程与实际 UI 主线程不同的情况，不能将已通过的纯逻辑回归当作游戏验收。

两处服务此前将构造时 ManagedThreadId 缓存为主线程。现在注入宿主的 `UnityData.IsInMainThread` 判断，使用时核实真实游戏线程，服务可以在加载线程创建；后台调用仍被拒绝。新增回归在加载线程构造这两个真实服务，再在模拟游戏主线程调用，并检查另一后台线程调用不会进入窗口/捕获回调。公共抽象 API 和版本不变。

界面调整：顶部固定管理按钮、已选条目/下一步提示、计划按钮及反馈；失败显示操作失败和错误码/原因，过长内容保留 tooltip。有效索引载入后自动收起路径设置，允许展开重载；三条样例列表只占 96 UI 单位。选择只展示条目，点击计划才执行依赖解析；本预览仍不下载安装。

新增 `StoreBrowserLayout` 纯几何回归覆盖 30 种宽高组合、长选择/错误文本、零空间、三条样例及超大列表边界。辅助布局仅使用公开 Unity 类型，不访问抽象内部的 Normalize。完整构建首次发现该内部辅助方法不可跨程序集访问，改为局部非负尺寸限制后通过。

实际命令：

```bash
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release -p:BuildInParallel=false -m:1 --no-restore
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release --no-restore
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1 --no-restore
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release -p:BuildInParallel=false -m:1 --no-restore
python /tmp/check-phinix-store-tab-artifacts.py
git diff --check
git status --short
```

最终普通 solution Build 通过（0 错误、2 个既有 protobuf 目标框架警告）；框架、布局和商店 231 项回归通过。26 个必需产物及主包/预览 DLL 摘要一致检查通过，中英文新入口键一致、无重复，输出未混入游戏/框架 DLL。没有 pwsh，原 PowerShell 产物脚本仍未运行。未执行 no-incremental Rebuild。

修复后需同时更新主包和商店预览并重启，再复测管理窗口、a 的 c → b → a 计划及折叠/反馈布局。上述游戏截图是修复前的部分进展，修复后真实游戏结果、环境身份捕获及扩展开关重启流程仍待用户复测。没有更改安装事务、协议、物品所有权或持久化路径。

## 用户复测反馈

2026-10-03，针对修复后的扩展管理、c → b → a 依赖计划、Tab 状态保留、错误恢复、扩展开关/商店恢复和布局语言的本轮清单，用户回复“没问题了”。据此将本轮本地预览基础游戏验证记为用户反馈通过，线程误判不再作为下一批工作的阻塞项。

反馈没有附逐项截图、Player.log、游戏/平台版本、分辨率和 UI 缩放明细，因此不扩展为跨平台/所有语言与缩放组合的验收证明。CF/R2、联网索引/代理、Steam、下载、安装恢复、Mods 写入和历史存档仍需独立验证。本次记录反馈、更新进度，没有新增代码或重跑构建。

下一批按主计划 P0 + P2 开始：最小 endpoint/stable/published 定位格式与严格校验、本地协议替身，以及客户端远端目录读取/有效缓存/离线浏览。P1 的实际 Worker/R2 联调在测试资产与平台资源齐备后接续；不改回客户端直连 GitHub。后续有界下载、安装恢复、正式发布与 Trade 级别的主包交付继续按原依赖推进。
