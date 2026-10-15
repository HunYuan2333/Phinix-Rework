# 主题包正式发布准备与客户端索引升级

## 结论

需要客户端更新。现有本地主题导入不等于在线主题商店支持。不能只更新 Index 或 CF，也不能向现有 schema3 插件 catalog 直接加入 `phinix-theme` 记录。

首次核查完成后，本地在线主题链路已实现并验证，见文末后续状态；未发布主题、升级线上 Index 或部署 CF。浅色窗口修复仍待实机验收。

## 当前源码依据

| 环节 | 实际实现 | 发布缺口 |
| --- | --- | --- |
| 主题数据包 | Common `Common/Utils/Framework/Themes/ThemePackage.cs` 有独立 `phinix-theme` manifest、XML 摘要、严格文件白名单 | 沿用纯数据格式；一份 ZIP 通用于 1.6/1.7，不加 net472/net10 variants |
| 本地主题生命周期 | `Client/Source/UI/ClientThemePackageService.cs` 支持 Inspect/Install/Uninstall；Install 使用 CompatibilityVersion 检查范围 | 远程下载完成后复用此安装/卸载服务；下载、安装、应用保持分离 |
| 商店展示 | `Extensions/PluginStore/Client/ManagedStoreListing.cs` 只有 InstalledThemePackage 分支，本地包进入筛选与搜索 | 增加远程主题记录及与本地库存的合并、可更新状态；远程主题不能套 DLL manifest |
| 客户端目录 | `Extensions/PluginStore/Client/ManagedStoreCatalog.cs:ReadPackage` 只接受现有 DLL 与 Workshop；未知 management 会抛 UnsupportedManagement | 新增独立主题目录读取、来源、缓存及刷新/下载状态，统一进入已有商店呈现 |
| 客户端端点 | `Extensions/PluginStore/Client/RepositoryMetadata.cs` 固定 phinix.official profile 与现有稳定目录证明 | 增加主题 feed 的确定身份与独立 stable/发布证明；任一 feed 失败不拖垮另一个 |
| Index | Validator/Production 与现有工作流只有插件准入/发布；Actions 优化仅在工作树 | 主题候选、纯数据 ZIP 校验、维护者审批、不可变发布证明、后续更新范围必须闭合 |
| CF | `Phinix-Plugin-Gateway/src/protocol.mjs` 仅接受现有 catalog schema1/3 和对应插件 payload | 增加主题目录与下载路由，验证固定 Release/asset/digest；避免重现 PackageNotFound |

## 发布顺序与门槛

1. 固定独立主题 feed 的来源 ID、metadata 路径、schema、Release 身份和 digest 规则。保留 phinix.official 现有 stable/catalog 内容语义，使旧客户端继续使用插件商店。
2. 同步实现 Index 主题准入/发布与客户端主题读取/下载；CF 接入同一不可变证明。不得只凭 ZIP 后缀识别主题，不得跳过摘要/来源检查。
3. 客户端沿用已有全部/插件/主题与安装状态筛选；主题安装后不自动应用。安装确认绑定目录快照、候选哈希和已安装 artifact，防止刷新后执行过期动作。保留本地导入、编辑、热应用与导出。
4. 联合检查主模组产品版本与 `Client.CompatibilityVersion`。当前分别为 0.10.2 与 0.10.1；正式主题不能继续以 `>=0.10.1` 作为“已包含窗口修复和在线主题能力”的保证。最终 Host 能力版本确定后，再构建主题包并冻结摘要。CLR identity 不随之乱改。
5. 先完成两版构建/白盒：旧插件目录回归、旧客户端目录不变、主题目录失败隔离、同 ID 冲突、候选被撤回、损坏包/错误 digest、升级/卸载与重启恢复、GitHub/CF 下载字节一致。
6. 用户实机验证 1.6/1.7：本次浅色窗口修复、两主题安装/选择/切换/导出、在线更新与卸载。此前无复现栈的卡死不能标记为已修复。
7. 先发布主模组与 Index/CF 能力，确认新主模组可下载，再发布主题候选并走审批/索引更新。主题简介提示先更新主模组。既有三个业务插件无须为纯主题发布重新发包。

## 本地候选复验

2026-10-15 使用实际 ManagedPackageTool --validate-theme：

| 包 | 版本 | ZIP SHA256 | 状态 |
| --- | --- | --- | --- |
| phinix.theme.daylight | 0.1.1 | fc9bd010699cb4635a34b1416eaa19be91ecdfb72df5cf6c42ca7773d54a59ac | 纯数据静态验证通过；当前范围 >=0.10.1 <1.0.0；非正式发行冻结包 |
| phinix.theme.tokyo-night | 0.1.1 | 9216c6aec0c1c0cfafd877b08ba592c0e68a9002a29fa6712fa59a6173a5367a | 纯数据静态验证通过；当前范围 >=0.10.1 <1.0.0；非正式发行冻结包 |

原文件在工作区父目录 local-compatibility-analysis/theme-palette-observability-20261015，保留不覆盖。待最终 Host 范围确定后生成新候选，不能修改已冻结 ZIP 而复用旧 digest。

状态区分：源码核查完成；上述两 ZIP 静态验证通过；在线主题端到端尚未实现/通过；最近窗口修复已完成两版编译和白盒，游戏验收待测。没有因这次准备核查改变任何线上服务。

## 后续实现状态

上述准备缺口已在工作树按实施计划接通，详见同目录 `Store-Themes-and-Index-Actions-Plan-2026-10-15.md` 的“在线主题实现切片”。Host 候选 0.10.3；两份纯数据主题候选 1.0.0。仍未提交/推送、部署 CF、创建线上 themes 分支或发布主题。不要把本地编译/白盒通过标记为线上或游戏通过。

## 线上发布进展（2026-10-15，晚于上述本地状态）

- 用户创建组织 `Phinix-Rework-Themes` 后，已使用 gh 创建并推送两个独立公共仓库：`Tokyo-Night`、`Daylight`；均已发布 v1.0.0。包要求主模组 >=0.10.3 <1.0.0，README、Release 和商店摘要均提示先更新主模组。两包为纯数据，共用 1.6/1.7。
- Index 已推送 `d7f00d50da829f1b022059d6573e43cf67a3d28b`，主题发布工作流 38679716960 成功，已创建独立 `themes` 分支与 schema4 feed。主题 snapshot 同提交；catalog SHA256 `ea31fda62db704bb204a0676a94df92711bb30d2d417ee013ed0c884bf2a4828`。
- 原 `phinix.official` 指针保持原 snapshot `ac45d5eb450fb9906e3d7d1b5ce48ed0702919d5`；三个业务插件未重新发布。线上 CF 原目录、缓存与 304 重新校验通过（19 条目录记录）。
- Gateway schema4 支持 `8695bacb67f43cf57479abd967709b02474cf3d4` 已部署，Worker version `9c66ce2f-3e3f-4251-8a10-102b1d79fc67`。两个主题经真实客户端 GitHub 直连、摘要/manifest/XML 校验、隔离安装与收据重载均通过；CF stable/published/catalog 通过，主题 ZIP 下载未通过。
- CF 在新组织作者仓库首次查询收到上游 403、剩余额度 4952。旧实现把所有 403 都写作 OriginRateLimited；这不是主额度耗尽的证据，未捕获上游响应正文，不能武断确定具体令牌策略。
- 后续网关修复已提交并推送 `214bf1b`：公共配置按已批准 owner ID 选择组织包独立只读凭据；目录仍用原凭据，旧插件路径保留。403 区分访问拒绝与限流，不输出上游正文。169 项 Node 测试、原生 workerd/正式配置打包、Python 检查通过。该后续修复尚未部署：Worker Secret 名称核查只有原 GITHUB_TOKEN，待用户添加组织只读 GITHUB_PACKAGE_TOKEN 后继续 CF 实际下载验收。
- Host/Common 本轮工作树仍有未提交实现；用户所述主模组已上传是 Steam 发布状态，不能据此记为 Git 源码均已推送。游戏内安装、切换和卸载仍由用户验收；隔离传输检查不等于游戏通过。

正式仓库：https://github.com/Phinix-Rework-Themes/Tokyo-Night 、https://github.com/Phinix-Rework-Themes/Daylight 。

## 第三方分发及客户端交互修正（后续实测结论）

按用户要求撤销未部署的组织专用凭据方案：Index `20a6e446ac6f9c9ee1729668f555aac387bc026b` 已发布新主题目录，原作者来源记录不变，在同一受控 Release 保存 `theme-<ZIP SHA256>.zip` 分发副本。CF `c2207a3a1bd55404b683c7dda2f08d6d16cf9e98` 已部署为 Worker version `ccf0be0b-450d-4270-868b-8af699ac195c`。任意获准公共作者均走同一路径，不需要作者令牌，不限制为两个官方主题。

新 snapshot 为 `20a6e446ac6f9c9ee1729668f555aac387bc026b`，catalog SHA256 `b62360e5d17d42f94a4e1c7f94940fa25fbbfb22fbb3a3cd37895b272e4f4b75`。发布工作流 38702787215、网关 CI 38702881411 成功。CF 实际下载两个主题、ZIP/manifest/XML 校验、隔离安装及收据重载通过；两包 hash 与原作者原包一致。原插件目录及缓存/304 验证通过，19 条记录，原 snapshot 未变。

GitHub 直连对同一批原包此前通过，新快照复验遇到匿名额度耗尽：X-RateLimit-Remaining=0、RepositoryRateLimited。不能把这次失败写成当前直连验收通过。GitHub 官方说明匿名 REST API 按出口 IP 每小时 60 次：https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api 。CF 服务端凭据额度与玩家匿名额度分开。生产仍只启用元数据缓存、未启用 ZIP/R2；镜像托管不是 CF ZIP 缓存。

用户游戏验收发现主题条目点击无法保持。真实根因在 `ManagedStoreListing.RestoreEntry`：远程主题没有 DLL CatalogRecord，又不是 installed-only，被旧插件恢复条件排除；`Select` 立即重建后清空选择。已加入同 key/version 的主题恢复分支，刷新后使用当前记录，筛选移除继续清空；原插件选择逻辑保留。此前隔离安装检查没覆盖该 UI 行为，“不需要客户端更新”的判断只适用于目录格式、不能作为游戏功能结论。

本地 Host 0.10.4 修正该交互；主题错误单独友好显示，新增用户默认 CF，已选线路保留。普通主题浏览不强制绕过元数据缓存，安装完成前仍强制重新读取当前指针。产品/能力版本为 0.10.4，API 3.0 和 CLR identity 未改；现有三个插件与纯主题包无需重新发行。

两版隔离全图编译、引用闭包/打包审计和 3437 项 managed 白盒断言通过。交互测试覆盖选择后重建、刷新绑定当前条目、筛选移除、远程已安装主题、浏览允许缓存/安装前强制读取。本地 ZIP：`../local-compatibility-analysis/theme-store-interaction-0104-20261015/Phinix-Rework-0.10.4-theme-store-fix-dual.zip`（相对工作区父目录），SHA256 `911886a03ff902fadac89c2e8f9720c64ca24775021d1bbd8f1695f289fd957d`。未上传 Steam、未改游戏/存档。真实鼠标输入、确认弹窗、在线安装及热应用仍需游戏验收。

## 用户验收后启用正式缓存

用户确认 0.10.4 交互无问题后，明确授权启用 CF 缓存和 R2。网关实现 `a826166b6d9f69c5b19cd06944e702fd2e72cdbb` 已通过 170 项 Node 检查、原生 Worker 与 CI 38709690756，部署版本 `13d64673-c272-451b-bfcc-9a580a10b788`。当前为元数据 + 边缘 ZIP Cache API + 私有 R2/SQLite 缓存链路；旧文中的“ZIP/R2 未启用”仅指先前部署状态。

容量 256 MiB、单包 8 MiB、每 UTC 月 Class A 10,000 / Class B 100,000 的本缓存操作预算；跨月只轮换计数，不重置状态、对象、未决容量或租约。命中前验证目录审批，交付字节继续摘要校验；预算/容量/服务故障可回源。R2 桶 phinix-plugin-cache-production-20261015，旧 PoC 数据未删除。真实客户端两个主题下载/校验/隔离安装通过；实测 edge 与 r2 响应，独立读取 R2 对象摘要一致；Example 1.1.2 下载/PE 校验和原目录 304 回归通过。此服务端启用不再要求客户端更新。

运维限制：本机到正式服务的内部账本检查桥接返回 OperationsRpcTimeout，不能记为线上运维 RPC 通过；公网缓存下载和独立 R2 对象读取已通过，未执行恢复/重置/删除。已关闭本轮临时本机运维监听。完整部署、摘要与限制记录在 Gateway docs/production-cache-2026-10-15.md。
