# 商店主题包与 Index Actions 实施计划

## 范围与基线

用户授权整理 Actions，并接入在线主题包和组合筛选。基线：Host `23643e1`、Common `9d0710a`、Index `5e1de88`、Gateway `da8ddab`。主题本地导入/选择/导出已经存在；在线目录目前只接受托管 DLL 与 Workshop。当前正式 API3 双版本插件须保持可用，不重发插件来绕过目录问题。

## 实施顺序及验收

1. **A1 空发布入口**：在 .NET 准备前检查真实上游任务是否完成准入；无更新和 check-only 扫描跳过后续构建、发布与反馈。不信任单独 artifact 或运行标题，复用既有事件/来源/维护者核验。实际 publisher 保留独立复核。测试无更新、有更新、伪造来源、手动入口。
2. **A2 公共构建步骤**：抽取本仓库可信验证器准备步骤，保留审批入口、工作流路径/任务名和读写权限分离；不得破坏历史审批证据。先不跨信任边界传递可执行 artifact，不扩大写权限。
3. **T1 目录与静态校验**：将纯数据主题作为明确包类型接入；复用现有 ThemePackage 读取器，禁止 DLL、路径穿越、额外文件和错误哈希。插件 schema1/schema2 与 Workshop 不改义。主题不伪装成 DLL、不加入 .NET variants。设计旧客户端读新目录的兼容/门槛方案，不能让未知类型使现有正式目录整体失效。
4. **T2 Index 与 CF**：主题候选检查、维护者批准、发布锁、来源更新范围、静态校验和下载路径同步闭合；固定 Release 身份和 digest 验证沿用原链路。冻结验证器从真实已提交源码更新，禁止伪造 provenance。
5. **T3 客户端**：复用公共 renderer；类型“全部/插件/主题”与状态“全部/已安装/可更新”独立组合，搜索和数量一起更新。统一列表读模型，安装/卸载仍按明确类型执行。主题本地状态进入主列表；一包一行，详情列出配色，安装后不自动替换当前主题。保留更多菜单本地主题管理入口。
6. **T4 双版本与游戏验收**：隔离构建 1.6/net472、1.7/net10.0；插件与主题并存、同 ID 冲突、更新/卸载、选择/导出、空目录/断网/旧目录/旧客户端、筛选后选择失效。区分源码、编译、白盒、线上及游戏通过；需要游戏测试时通知用户。

## 不变量

Host 只提供主题包服务和公共呈现，商店拥有在线仓库操作；不新增主题系统/renderer，不接管业务。下载不等于安装，安装不等于应用主题。网络在后台处理，主题变更回到主线程；确认绑定候选哈希与已安装 artifact，刷新/切换选择不能执行旧动作。

当前实施记录在本文件追加。未经独立验证不修改正式 stable 目录，不上传测试主题到正式商店；先完成本地闭环。

## 首切片实施

- A1/A2 已写入 Index 工作树：只读前置 gate 复用真实上游证明，空扫描跳过重任务；公共 composite action 统一准备验证器，保留原路径、任务名、写队列和读写隔离。127 项 Python 测试，一项原有跳过。actionlint 1.7.12 尚不认识既有 `concurrency.queue`，只豁免该诊断后其余检查通过；没有删除写队列来迎合旧检查器。
- T3 本地先闭环：主商店纳入已安装主题包，类型/状态/搜索独立组合；同 ID 的插件与主题在读模型中分开，一包多配色仍为一行。主题操作复用现有选择器与 Host 卸载服务，按包清单/已安装列表变化使旧动作失效，不增加公共 API。
- 白盒发现并修正 Common 既有缺陷：未选用主题包的删除意图为空，写入器允许而 Base64 读取器拒绝，导致 `InvalidText`。只允许 intent 字段为空；receipt/state/manifest 字段及 canonical journal 验证保持严格。
- **在线主题目录未完成，不可作为已支持对外宣传。** 确定与旧 `phinix.official` 插件 feed 分开发布主题 feed；新客户端统一展示，旧客户端继续读取既有目录。下一切片先固定来源身份、metadata 路径、目录 schema、摘要来源和安装来源记录，再实现 T1/T2；不得仅把主题记录塞进当前正式目录。

### 首切片验证与本地包

- 最终隔离双版本构建完成，包含三个维护插件的联合编译；Host API 仍为 3.0，不修改插件范围。
- 托管运行时白盒：3407 assertions；含真实主题包存储、双配色单行、类型/状态组合、离线搜索、隐藏选择清空、空/非空卸载恢复意图。
- Index gate 对真实无更新扫描 run `38665982255` 做只读验证，返回 `needed=false`；没有批准候选或发布目录。
- 测试主模组 ZIP：`local-compatibility-analysis/store-themes-actions-20261015/Phinix-Rework-store-types-local-dual.zip`（相对工作区父目录），SHA256 `f2e11ab7ca9154b6b5bbeb980393b5e50c5adf51fa392db01ebd135d6f3f0119`。
- 同目录 `theme-filter-test-0.0.1.zip` 经真实打包/验证工具生成，两套强调色，无 DLL；未安装、未上传。
- 游戏验收待测；Actions 新配置尚未推送执行。此前线上状态不因本轮本地改动而改变。

## 在线主题实现切片（2026-10-15 后续）

当前工作树已实现独立目录：source `phinix.themes`、catalog schema4、同一 Index 仓库的 `themes` 发布分支。插件 `phinix.official`/main/schema3 不改义。客户端 `RepositoryProfile.Themes` 固定同一仓库/owner ID；GitHub 与 CF 共用来源身份，分别校验原始 stable/published/catalog 链与固定 Release/asset。目录刷新独立，失败保留插件与已安装主题；本切片不持久化远程主题目录缓存，离线仍可管理本地库存。

客户端复用 ThemePackage/Host 安装服务，不创建第二套主题系统。后台下载只生成一次可领取的已验证包，检查 ZIP/manifest/XML 与目录声明一致，再重新读取线上目录；快照改变必须重新审阅。最终安装在主线程、精确匹配所选记录和原已安装 digest；更换选择或刷新不得提交过期候选。安装不自动应用。

Common 增加纯数据 `ThemePackageOrigin` 与不可变 `WithOrigin`，安装收据保留 source/repository identity/snapshot/catalog digest。物理存储命名空间保持 `local-theme`，避免升级跨目录迁移；收据 sourceKind 区分 local/catalog，公开 InstalledThemePackage.SourceId 为来源身份。旧本地收据的 canonical 字节保持不变，新 origin 收据在同一持久恢复协议下写入/重读/删除。商店拒绝静默覆盖不同来源的同名包，用户可卸载后再安装。

Index 增加 `theme-publish.yml`：main 上维护者手动 dispatch 审批该精确输入提交，默认 check-only；只读校验 job 与 contents:write 发布 job 分开。写入前重新验证角色、主分支、来源仓库/tag/Release/asset、真实 ThemePackage 静态解析和历史版本连续性。新版本之外，历史记录只允许改 state。独立 writer 队列保留排队；不承诺首版自动追踪主题作者更新。发布不可变 `theme-catalog-v4-<commit>` 资产后，原子更新 themes 分支指针，保留历史 descriptor 和审批收据。

Index 使用的 ThemePackage.cs 从 provenance 已固定的 Common `9d0710aaa9b41c07f28d9bfa25599247e7bb1f93` 提取；静态 ZIP 格式未因 origin 存储扩展改变。新增冻结文件记录了真实 blob 摘要，未拿未提交源码伪造 provenance。工作树 HEAD 不等于旧 provenance client 提交时，checkout 比对器会正确拒绝；另逐个 git show provenance 原提交，核对全部 16 个冻结文件的原始字节和 SHA256，结果保留本地审计。

CF 增加 schema4/`theme-zip` 路由、主题 manifest 白名单和 8 MiB 上限；正式/过渡配置增加固定 themes 来源，不启用存储 PoC、不扩大来源为任意 URL。只更新本地代码与配置，尚未部署。

Host 产品候选与 CompatibilityVersion 统一 0.10.3，CLR/客户端 API3 identity 保留。两个主题 1.0.0 候选要求 `>=0.10.3 <1.0.0`，不混入 DLL 或 runtime variants；原 0.1.1 包保留。


### 当前切片最终验证与交付

- 1.6/net472、1.7/net10 Host 与三个维护插件联合构建通过；分发资源/程序集闭包/Workshop ID 审计通过。最终构建 `/tmp/phinix-online-themes-0103-ready`。
- 呈现白盒 371 项；托管启动/持久恢复/新主题来源与下载竞态 3432 断言；商店/真实 GitHub 与 CF HTTP 适配器（模拟来源）1040 断言。修正原测试把 schema2 一概视为 UnsupportedSchema 的过时预期：schema2 缺 variants 应为 InvalidVariants，schema3 仍 UnsupportedSchema，不改生产行为迎合旧测试。隔离 snapshot 默认不复制 docs，补入原始协议 fixture 后完成商店回归。
- Index 133 项测试通过，1 项原有跳过；CF 149 项通过；原生 Worker 与正式配置打包检查通过。原生验证需要回环端口，使用已授权的本地检查权限，未部署。
- 两个 1.0.0 候选分别通过 ManagedPackageTool 和 Index 真实 ThemePackage 静态验证；本地验证 fixture 的 repository/release/asset 是明确虚构值，不是发布候选记录，未放入 themes/packages。
- 主模组本地测试包 SHA256：`c04e32d2adaf4d70bf175dd0d877993391e7fabf40cb9bf6288f999d186ee8be`。目录：工作区父目录 `local-compatibility-analysis/online-themes-0103-20261015`，附两主题、来源/包审计和分阶段测试清单。
- 未提交/推送、部署 CF、触发线上 Action 或发布主题。尚无线上 themes 指针；游戏里在线主题入口的实际网络验收要在部署之后进行，不用现有“主题目录不可用”提示冒充成功。1.6/1.7 本地主题与浅色窗口可以先做实机验收。

## 源码推送前复验（2026-10-16，Asia/Singapore）

本次将在线主题商店、通用主题窗口/滚动条与聊天分界线、主题来源持久化及双语开发 skill 一起交付源码。Common 固定引用更新到 `c165f61`（远端默认分支 dev）；Host 目标为 main。Index、CF 和三个业务插件没有本轮待提交修改，不重复触发发布。

本轮重新构建并实际执行控制台测试：ManagedExtensionRuntimeTests 3437 项（另含内部商店操作回归 2225 项，不叠加计数）、PluginStoreRuntimeTests 1040 项、PresentationRuntimeTests 371 项，均通过。GameDrawing17RuntimeTests 对本地 1.7 实际原生 API 编译后以 fake backend 执行 1142 项检查通过；包布局脚本 2 项通过。构建保留 protobuf NETSDK1212 与 NuGet 漏洞源不可达 NU1900 警告，未修改 vendored pins。

skill 的双运行时模板编译、主题包正负例与 14 次 CLI 检查见其 validation.md；本轮不把这些证据扩展为新增游戏验收。测试 DLL、游戏引用和构建产物排除提交。
