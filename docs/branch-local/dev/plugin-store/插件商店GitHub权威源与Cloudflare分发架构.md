# 插件商店：GitHub 权威源与 Cloudflare 强制分发架构

2026-10-05 用户决定变更：客户端默认 GitHub 直连，提供手动 CF adapter/cache 加速切换；不再强制经过 CF。以[新访问计划](GitHub直连与CF适配器计划.md)为准。下文强制 gateway、无直连回退等内容保留为此前设计，固定源、身份/摘要、撤回、缓存预算及未签名信任边界仍需遵守。


日期：2026-10-03。分支：`dev`。状态：分发方向已确认，实现与部署待 PoC，正式格式/信任模式等仍待决策，不代表线上能力。仓库调查基线为 `fec85a5a442d6d657031c5f1d122fd5fe776145a` **加上当前工作区已有的未提交商店实现**；不能只用 HEAD 判断完成度。Cloudflare/GitHub 资料查阅日期同上，额度和平台行为上线前须再次核对。结合当前代码与索引初始化的执行顺序，以 [分步实施计划 §0](插件商店分步实施计划.md#0-当前进度与重新排期) 为准。

同日补充已确认方向：引入 R2 作为有容量上限的按需文件缓存；首次请求两层缓存均未命中时，从 GitHub 流式返回并尝试保存，后续读取 R2。优先使用免费额度，容量不足先删旧版本，不建设全量同步或专门的撤回删除系统。具体策略见 §7、§8.5、§14.5；这是设计确认，不表示已经实现或能保证整个账号零费用。

本文接续[现有商店设计](README.md)，重新设计其网络分发边界，保留审核、包身份、依赖、校验、安装和生命周期约束。本轮仅新增本文与专题导航，不实现 Worker、客户端网络层、发布工作流，也不部署或执行大陆测速。

## 1. Background：结论与适用范围

已确认采用方案 B 的按需 R2 变体：**GitHub 保存权威发行记录及原始文件，客户端商店通过统一 Phinix repository endpoint 获取内容；边缘缓存命中直接返回，边缘未命中先读 R2，R2 也未命中才从 GitHub 流式回源并尝试写入 R2。** 不向客户端转交 GitHub 下载重定向。

这个方向是旧方案的传输层演进：官方仍只发布审核索引，作者仍发布自己的源码和 Release 包；Cloudflare 不决定哪个版本被批准，不重新解依赖，不保存唯一的批准记录。增加一个私有 R2 桶及最小容量/操作预算协调账本，不增加审核数据库、全量镜像工作流或常驻发布服务。协调账本只负责预留容量、同键填充和额度，不保存第二份版本权威；具体实现由 PoC 验证。

“客户端只访问 Cloudflare”约束的是**商店自动获取索引、包、伴随清单及更新数据的网络链**。Steam 工坊仍在 Steam 游戏内浏览器打开、由 Steam 下载，项目/源码链接仍可由玩家主动打开。它不是对整个 RimWorld 进程所有网络连接的限制。若要求 Steam 也经过 Cloudflare，将与已确认双渠道边界冲突，不纳入本提案。

两个前提不能直接当成结论：普通免费 Cloudflare 不等于中国大陆 CDN，也没有证据证明它对所有大陆运营商比 GitHub 稳定；技术额度足够也不等于大文件分发在服务条款下可以长期免费。前者由 §13 的实测决定，后者见 §14。

## 2. Existing design：仓库证据和 Git 历史

### 2.1 调查覆盖与旧文档

搜索范围包括仓库自有 `docs/`、`Client/`、`Common/`、`Server/`、`Extensions/`、`Tests/`、`.github/`、项目文件、README、Issue 模板和可达 Git 历史。vendored protobuf 与生成产物不作为 Phinix 商店设计来源；没有读取凭据或服务器状态。关键词覆盖插件/商店、Marketplace、registry/index/catalog、manifest、Release/artifact、Workshop、版本、SHA-256、签名、代理和缓存。没有发现独立 ADR 编号体系、现成 Cloudflare Worker 或另一套已落地商店后端；本地检索不能证明外部 Issue 或不可达历史不存在。

| 证据 | 原有决策或当前用途 |
| --- | --- |
| [Design-Philosophy](../../../Design-Philosophy.md)、[设计哲学](../../../设计哲学.md) | 插件平等、宿主不引用具体插件、抽象契约、主线程、生命周期、版本兼容 |
| [英文开发者指南](../../../Phinix-Submod-Developer-Guide.md)、[中文开发者指南](../../../Phinix附属Mod开发者指南.md) | 标准附属模组发行、Contracts/实现边界、发现与加载、通用宿主服务 |
| [兼容与恢复边界](../../../Compatibility-Boundaries.md) | 真实回执、整批物品转换、未知结果和既有业务数据恢复不因商店改变 |
| [商店可行性与功能设计](插件商店可行性评估与功能设计.md)，尤其 §4–8、§15 | 独立客户端模块、GitHub 固定快照、专用代理、包规划、离线与安装事务 |
| [双渠道索引与 GitHub 自动审核发布](插件商店双渠道索引与GitHub自动审核发布评估.md)，尤其 §6–11 | 官方索引/作者 Release 分工；首次人工、普通更新自动；索引 tag、Release、stable 指针 |
| [安全性与维护成本](插件商店安全性与维护成本评估.md) | 源码准入、具体候选指纹、同进程 DLL 无沙箱、签名只是加强方向 |
| [安装目录与官方拆包](插件安装目录与发现加载及官方插件拆分评估.md) | 独立本地 Mods 包；不写主包/Workshop；红包和人才贸易后续拆包 |
| [术语](术语与功能分层.md)、[实施准备](实施准备与代码基线.md)、[分步实施计划](插件商店分步实施计划.md) | 内置功能/可选扩展分类、实施顺序、未完成门槛 |
| [清单 v1 与依赖计划](清单v1与依赖计划实现.md)、[运行环境与路径](运行环境与稳定路径实现.md) | 实际开发格式、严格解析、固定计划、环境事实与独立商店数据根 |
| [本地预览](独立模块与本地浏览预览实现.md)、[静态载荷校验](载荷静态校验实现.md) | 已有本地浏览、任务终态、ZIP/PE/hash 检查；尚未接网络/安装 |
| [索引 bootstrap 中文](index-repository-bootstrap/README.zh-CN.md)、[英文](index-repository-bootstrap/README.md)、[source.json](index-repository-bootstrap/source.json) | `phinix.official`、`HunYuan2333/Phinix-Plugin-Index` 的开发初始化配置；空索引，不是正式 stable Release |
| [扩展管理方案](../插件启用禁用与扩展管理实施方案.md)、[开发体验方案](../扩展开发体验增强-设计方案.md)、[Host 能力审计](../Host能力缺口与统一插件日志审计.md) | 禁用/依赖图、API 兼容；早期 manifest/签名方向没有完整签名协议 |
| [dev README](../README.md)、[CI 文档](../../../CI.md)、[mono.yml](../../../../.github/workflows/mono.yml) | Marketplace 的历史远期愿景；当前 CI 构建/上传 artifact，不是索引审核发布链 |

bootstrap 源地址已通过 [索引仓库初始化记录](索引仓库初始化记录.md) 核对为真实仓库，九个文件及空 catalog/checksum 一致，gh 可用；仍没有正式批准包、stable 指针或可用下载安装服务。此前调查基线与本轮实际进度分别保留。

### 2.2 已删除、迁移及替换文档

使用 `git log --all` 的路径/提交关键词/删除及重命名记录，并用 `git show` 阅读历史内容：

- `fec85a5`（2026-10-02）新增三份商店评估，原路径在 `docs/branch-local/dev/`。当前工作区显示原文件删除，内容归集至 `plugin-store/`；这是**未提交的目录迁移**，不是历史中商店设计被否定。旧提交确认官方只发索引、作者发 DLL、Steam 仅链接及索引 Release/stable 的原始意图。
- `f50fce9`（2026-05-27）的 `docs/superpowers/plans/2026-05-27-client-official-extension-runtime-loading.md`，后迁至 branch-local，最终在 `2d4fb39`（2026-06-21）删除。它记录了从客户端编译期引用官方插件走向运行时预加载的过渡；历史中的官方专属解析调用不能恢复成今天的宿主特权。
- `f68dba8`（2026-05-29）中的 `docs/phase6-core-only-extension-architecture.md`，后在 `a5be7aa` 的文档整理中删除/由中文架构文档接续，明确 core/host 不理解 chat/trade 等业务，官方与第三方仅发行方式可不同。当前依据是[Phase6 中文设计](../Phase6-Core级宿主与动态扩展架构.md)和设计哲学。
- README 草稿在 `0af5e06` / `ef19319` 归入不同分支目录，`2375c26` 改名为正式 README。其 Marketplace/热加载愿景不等于今天已有下载安装或热替换。

可达历史中未找到早于上述商店评估的另一套已实现 registry/签名/Cloudflare 分发方案。历史文档只用于还原意图，不能取代当前源码和实施记录。

### 2.3 当前代码实际完成度

| 当前代码 | 已存在 | 未交付或限制 |
| --- | --- | --- |
| [Client.cs](../../../../Client/Source/Client.cs)、[Loader](../../../../Common/Utils/Framework/ExtensionAssemblyLoader.cs)、[Registry](../../../../Common/Utils/Framework/PhinixExtensionRegistry.cs) | 扫描/加载程序集，统一模块发现、Register/Activate/Shutdown，模块依赖排序 | 下载成功不等于启用；不支持 DLL 热替换，不是代码沙箱 |
| [PluginStoreClientExtension](../../../../Extensions/PluginStore/Client/PluginStoreClientExtension.cs) | `phinix.plugin-store` 通过普通模块契约注册设置入口 | 没有宿主商店专属分支；真实游戏验收尚未完成 |
| [CatalogReader](../../../../Extensions/PluginStore/Client/CatalogReader.cs)、[PackageVersion](../../../../Extensions/PluginStore/Client/PackageVersion.cs)、[PackageModels](../../../../Extensions/PluginStore/Client/PackageModels.cs) | 严格 schema v1；稳定三段版本；源/包/模组/程序集身份分离 | 非通用 SemVer；只支持精确范围或 `>=x.y.z <x.y.z`；未知字段/schema 拒绝 |
| [DependencyPlanner](../../../../Extensions/PluginStore/Client/DependencyPlanner.cs) | 同源、固定快照、兼容/冲突与有限依赖求解；计划锁定原始索引 SHA-256 | 工坊前置只提示；不能跨源猜依赖或覆盖来源未知本地包 |
| [StoreBrowserController](../../../../Extensions/PluginStore/Client/StoreBrowserController.cs) | 有界本地文件读取、后台规划、权威任务终态、取消/超时和迟到隔离 | 目前只读本地文件，十秒任务 watchdog 不能直接作为大包下载总超时 |
| [PayloadValidator](../../../../Extensions/PluginStore/Client/PayloadValidator.cs) | 资产精确长度/SHA-256、清单摘要、About、ZIP 路径/配额、实际 PE 程序集身份 | 尚未接下载安装；当前有界读入内存冻结字节，**不是已实现低内存流式校验** |
| [商店 harness](../../../../Tests/PluginStoreRuntimeTests/README.md) | 实施记录报告 231 条游戏无关断言通过 | 本轮没有重新运行；不能证明网络、游戏、代理、Steam 或文件恢复可用 |

当前格式的关键事实：索引最多 2 MiB/1024 条/每包 32 个版本；`snapshotId` 是 40 位源输入提交；包最多 128 MiB。`artifact` 固定 repository/owner/release/asset 数字 ID、tag、sourceCommit、assetName、payloadKind、sizeBytes、sha256、manifestSha256，**不接受任意下载 URL**。ZIP 根含 `phinix-package.json`；单 DLL 要伴随清单。当前展开限额是 256 MiB/4096 条目/单文件 64 MiB，优先于早期评估的 512 MiB/10000 文件建议。

包内清单禁止 artifact/state；生成顺序是清单字节 → 清单摘要 → 资产 → 资产摘要 → 官方索引。审核状态由官方索引控制，不由包自述控制。

### 2.4 保留、调整和理解冲突

| 原有方案 | 本提案关系 |
| --- | --- |
| 官方索引仓库 + 作者 Release；源码准入、首次人工、后续正常版本自动检查 | 保留；Cloudflare 不获得发布/审批权 |
| 已发布完整 catalog + mutable stable 指针；一次计划固定快照/hash | 保留；增加 gateway 的可重建发行映射 |
| 客户端 `GitHubSource` 请求 GitHub API/raw/Release，商店专用代理 | 调整为 RepositoryTransport；GitHub 适配放 Worker，客户端代理只连接 repository endpoint |
| 默认及自定义源，当前单源求解 | 保留源身份边界；公网官方 Worker 只接预配置源，不接受任意用户 GitHub 仓库参数 |
| GitHub 和 Steam 双轨 | 保留；只重构 GitHub 包的数据分发，不能承诺工坊版本锁定 |
| 当前 schema v1 绑定 `github-release` 和 GitHubArtifact | 明确冲突：统一 URL 不能自动让元数据与 GitHub 解耦；见 §6.3 的兼容路径 |
| 签名、immutable releases 为可选加强建议 | 保留此状态；未发现已落地 publisher signature、根公钥或轮换协议 |
| CI artifact 上传 | 不是正式商店包；到期删除使“清空缓存后只从 GitHub 重建”无法长期成立 |

## 3. Goals / Non-goals

目标：客户端无 GitHub origin 网络依赖；cache miss 也只向 Cloudflare 连接；边缘和 R2 缓存完全丢失可从 GitHub 已发布记录和仍存在的资产重建；保留逐包完整校验与整套依赖集合提交；按需保存包、容量不足淘汰旧版本、控制免费额度内的存储与操作预算；明确缓存时效、失败和容量；用大陆实测决定是否上线；提供未来替换 origin 的稳定传输 namespace。

非目标：第二套版本数据库、Cloudflare 托管审核平台、代作者重新编译 DLL、提前全量镜像所有版本、永久归档或专门的 R2 撤回删除同步系统、代理 Steam、安装脚本、热加载、自动静默升级、服务器自动安装、插件运行时沙箱、借分发改动迁移红包/交易/库存数据。本轮也不创建外部仓库、Issue、PR、tag、Release、账户或部署。

## 4. Architecture：组件和 Source-of-truth model

```text
作者 GitHub：源码 + manifest + 正式 Release 包
                    │ 官方校验具体字节/候选
                    ▼
官方 GitHub 索引：审批记录 + 固定 catalog + 发行映射 + stable 指针
                    │ read-only origin adapter
                    ▼
Cloudflare：统一 HTTPS endpoint + allowlist + 边缘缓存 + 私有 R2 按需缓存
                    │ 容量/操作预算协调；GitHub 回源时流式返回并尝试保存
                    │ 元数据/文件字节；无 GitHub Location
                    ▼
PluginStore.Client：固定计划 → 下载 → 完整校验 → 暂存/事务 → 玩家启用重启
```

**GitHub = authoritative source；Cloudflare = non-authoritative serving/cache layer。** 权威不表示任意 GitHub URL 都可信：只有选定受信索引发布者批准的固定候选属于该源。作者的自述、GitHub `latest` 和新上传资产不自动上架。

| 状态 | 所在位置/权威 |
| --- | --- |
| 作者、批准范围、发行版本、依赖、兼容、withdrawn/unmaintained、hash | 官方 GitHub 索引及其审核记录 |
| 发布二进制、包清单、源码 commit | 已批准的作者 GitHub Release/仓库 |
| stable → 完整快照；已发布快照 → Release/asset/hash | 官方 GitHub 发布元数据 |
| HTTP response/body、ETag 辅助信息、临时回源定位 | Cloudflare 可丢缓存；不编辑业务记录 |
| 已完整写入的包/伴随清单副本 | 私有 R2，可因容量淘汰；版本/hash 来自 GitHub，不决定批准或最新版本 |
| 对象字节数、正在写入的预留、填充锁、操作预算 | 最小协调账本；故障时停止新写入/受限操作，不能参与审核或安装授权 |
| 安装归属、文件摘要、接受的快照、提交/恢复日志 | 玩家本地；不是 Cloudflare 的账号/安装数据库 |
| endpoint、源 allowlist、超时、GitHub 只读凭据 | 可重新部署的服务配置；凭据单独保管，不是插件业务状态 |

Worker 可缓存派生映射、解析结果和响应，文件缓存必须能从上述 GitHub 输入重新计算。诊断统计/日志可丢失，不参与批准、版本选择或安装授权；容量和操作预算账本不能当作可随意丢弃的统计，账本不可用或无法确认额度时停止对应 R2 操作。不要用 KV 里唯一的“最新版本”或 D1 里唯一的“已批准”记录启动请求。

全冷恢复仍要求 GitHub 权威记录和资产存在。**缓存可重建不等于已删除 Release 可恢复。** R2 中已经保存的固定字节可能在源删除后继续存在，但允许容量淘汰，不承诺永久下载。原有 GitHub 索引状态与客户端安装前检查保留；首版不额外建设源删除追踪、R2 同步删除或紧急全层 purge 系统。

## 5. GitHub publication：沿用发布链并补最少的映射

保留申请 → 固定候选检查 → 首次人工批准 → 后续批准范围内自动检查 → 固定索引输入提交 → catalog Release 完整上传并核实 → 最后更新 stable。索引和作者版本是两种版本，不要求 tag 一致；合并 PR 不等于正式发布。

为了旧快照 URL 在全冷时也能定位，不应只保留指向最新的 stable。建议正式发布链增加**GitHub 内的 append-only 发行描述文件** `published/<snapshotId>.json`：绑定源、输入提交、catalog 原始摘要/大小、索引 Release/asset 身份、必要的伴随清单资产身份。P0 已有独立 v1 格式本地草案和固定向量，见[远端目录与缓存实现](远端目录协议与浏览缓存实现.md)；正式格式仍待 PoC 冻结；它是旧索引发布元数据的补充，不是 Cloudflare 数据库。

先核实 catalog 及描述文件，再登记已发布描述，最后更新 stable；读取必须把 stable、描述和 catalog 的身份/hash 交叉核对。`snapshotId` 仍指生成输入提交，描述文件在后续提交写入，避免生成文件引用自己的提交。已登记快照不覆盖；描述内容变化是异常而非新版本。

ZIP manifest 由客户端从完整包中读取，不要求 Worker 解 ZIP。单 DLL 模式目前只有 `manifestSha256`，**没有伴随清单 assetId/名称/大小的完整定位**；在发行描述里补充受审定位并绑定 catalog 摘要。不能从任意 filename/URL 猜清单，也不能给严格 v1 catalog 临时塞字段。PoC 先用 ZIP，DLL 分发须在这个缺口关闭后验收。

浏览只读取官方完整快照，不逐个列作者 Releases。请求某个包时才按固定记录核对 repositoryId/ownerId/releaseId/assetId/tag/commit/name/size，仓库转移或身份不符停止；缓存的核对记录可丢失。官方不向索引仓库 Release 上传作者 DLL；R2 仅按需保存已批准原始字节副本，源码准入与构建一致性证明仍是不同问题。

正式发行使用 Release assets。GitHub Actions artifact 默认有保留期限，故不纳入长期下载 contract；当前 CI 上传不改变这一点。[GitHub artifact 保留说明](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/remove-workflow-artifacts)

## 6. Client contract 与 URL/API design

### 6.1 稳定 namespace

示例主机 `https://repo.example.com` 仅为占位，实际域名待定。首版只允许 GET/HEAD；不提供写入、任意 URL 查询或匿名 purge。

| 路径 | 语义 |
| --- | --- |
| `/v1/sources/{source}/stable` | mutable 发布指针，包含固定快照 ID、catalog 摘要/大小、受支持格式；客户端路径由规范拼接 |
| `/v1/sources/{source}/snapshots/{snapshot}/published/{publishedSha}` | P0 草案新增：stable 绑定的发行描述原始字节；固定身份/hash，immutable |
| `/v1/sources/{source}/snapshots/{snapshot}/catalog/{catalogSha}` | 该已发布快照的完整 catalog 字节，immutable |
| `/v1/sources/{source}/snapshots/{snapshot}/packages/{package}/{version}/{artifactSha}/package` | 该快照批准的精确资产；不按最新版本重新求解 |
| `/v1/sources/{source}/snapshots/{snapshot}/packages/{package}/{version}/{manifestSha}/manifest` | 单 DLL 的原始伴随清单；ZIP 首版不提供此独立资源 |

source/包 ID 沿用当前标识规则；version 沿用稳定三段格式；snapshot 首版为 40 位小写提交标识；摘要为 64 位小写 hex。路径中的 hash 不是授权：必须与受信已发布 catalog 中该包记录相等。禁止客户端传 repository、assetId、origin、url、ref、任意 host 或 cache-buster。首版拒绝 query、重复斜杠、尾斜杠和多次编码路径；以后增加参数必须版本化。

不提供 `/latest/package` 或仅凭版本号取文件的模糊路径。source、snapshot、包身份和 hash 都进键，避免跨源同名、快照漂移、同版本替换和并发安装计划串包。UI 可从已锁 catalog 展示单包详情，无须首版增加逐条 REST 查询。

成功响应固定 Content-Type、长度（可确定时）、ETag、缓存策略；不回传上游 Set-Cookie、Authorization、内部调试地址或签名 query。错误采用有界 JSON，包含 `code`、`retryable`、`requestId` 和安全的重试时间。流已开始后的失败表现为下载中断，不能再伪造一份 200 JSON 错误页。

### 6.2 网络边界

商店 RepositoryTransport 只从显式配置的 HTTPS endpoint 生成上表路径，拒绝跨主机跳转及 GitHub 降级；Worker 失败时保留错误/本地缓存，不自动改访问 GitHub。TLS 验证照常，HTTP/SOCKS5 配置只作用商店请求，DNS、取消、超时仍须在 net472/游戏 Mono 验证。

项目/作者/sourceCommit 可作为 provenance 展示，**不能用作自动请求 URL**。图标/额外 metadata 若以后增加，也必须有受审资源路由，不能让浏览阶段从任意作者 avatar/GitHub URL 取图而破坏网络约束。诊断、HEAD、校验文件、伴随清单都遵守同一 endpoint 边界。

自定义源保留当前单源、显式信任和归属规则。官方 Worker 只接受部署配置中已批准的源 ID。用户可配置提供同一 contract 的独立 repository gateway；不能把“支持自定义源”实现成官方 Worker 的任意 GitHub 代理。首版是否开放源申请是产品待决项，不默认运营无限量代理服务。

### 6.3 origin 可替换性与 schema v1 的真实限制

URL/传输层可以不依赖 GitHub；**现有 CatalogReader 和包内 manifest 仍依赖 `github-release` / GitHubArtifact**。仅把 v1 原始 JSON 经 Cloudflare 返回，未来改 GitLab 时旧客户端会拒绝 metadata。不能声称今天换 origin 无需改客户端。

建议分两步：PoC 复用原始 v1 字节/hash，单独验证分发链，不修改解析器；正式网络客户端发行前冻结一个 provider-neutral catalog/manifest 格式（建议新 schema v2），保留 source/包/版本/兼容/依赖/模块/程序集/size/SHA-256/state，分开 `downloadable-package` 与 `steam-workshop`。origin 固定身份和回源定位留在 GitHub 发布描述，由 Worker 适配；provenance 是受控展示字段，客户端不解释成下载逻辑。

新格式必须明确原始字节校验及签名对象，旧 v1 与 v2 分别发布并协商；不能由 Worker 随意删/改 v1 字段却沿用其摘要。包内 manifest 若改 channel，也必须按新格式发布新包版本，不能重新打包覆盖旧版本。两种格式会产生不同 catalog 摘要和缓存键，但相同物理包始终保持原始字节身份。v1 计划和校验器继续走明确兼容适配，遇到不支持格式拒绝。

这样将来替换 origin adapter 时，已支持中立格式的客户端和上表 namespace 可以保持；当前 v1 的 GitHub 特性是迁移事项。是否正式首版即交付 v2 或先发行仅支持 GitHub 的 v1 gateway，需人工决定并准确标注能力。

## 7. Request flow：命中、未命中与流式返回

```text
Client → Cloudflare 统一 endpoint
          ├─ edge hit → 边缘缓存 body → Client
          └─ edge miss → Worker 验证路径/已发布映射
                          ├─ R2 hit → R2 body 流式返回 → 边缘缓存 → Client
                          └─ R2 miss / R2 操作预算不足或不可用
                              → GitHub 固定 Release asset
                              → Worker 内部处理允许的 GitHub 跳转
                              → 流式返回 → 边缘缓存 → Client
                              └─ 获准填充时，同步分流写入 R2
```

上图是包和固定伴随清单的路径；mutable stable 与索引发布映射继续按原策略向 GitHub 再验证，不从 R2 文件副本判断最新批准状态。R2 `get()` 可返回流，`put()` 可接受流；同一次回源可以一边给用户发送、一边尝试保存，不能先把整个包放进 Worker 内存。只有完整写入且符合期望长度/摘要的对象才能作为下一次 R2 hit。细节见 §8.5。[R2 Workers API](https://developers.cloudflare.com/r2/api/workers/workers-api-reference/)

GitHub asset API 可返回 200 或 302，Worker 必须自己处理两者。每一跳重新校验 scheme/host/path、固定目标和凭据边界，最多 5 次；GitHub 临时对象存储 URL 只在本次回源中使用。不能缓存 302 到外部 Location 再发给客户端，也不能把临时 URL 写进 stable/catalog contract。[GitHub Release assets API](https://docs.github.com/en/rest/releases/assets)

Worker 先有界读取并验证小 metadata，确定 asset 映射、期望大小和资源类型，再让完整包 body 流式通过。禁止将 128 MiB 包 `arrayBuffer()` 到 Worker 内存，禁止在边缘解压 ZIP、扫描 DLL 或使用整包 JS buffer 计算 SHA-256；发布侧和客户端完成完整载荷验证，R2 写入使用 API 的期望 SHA-256 校验。官方支持流式响应和背压，但必须实测 R2、边缘缓存与客户端分支速度不同时内存是否有界；不能认为简单 `tee()`/clone 就自动解决慢分支积压。[Cloudflare Streams](https://developers.cloudflare.com/workers/runtime-apis/streams/)、[R2 校验选项](https://developers.cloudflare.com/r2/api/workers/workers-api-reference/#r2putoptions)

**边缘可以开始发送尚未完整校验的包，客户端不能开始安装尚未完整校验的包。** TLS、完整长度、SHA-256、manifest、ZIP/PE 校验全部成功才进入暂存提交。响应 ETag 是期望身份，不能当成 Worker 已验证 body 的证明。

成功 edge/R2 hit 不需要再向 GitHub查询同一不可变文件。旧 immutable 缓存不是“当前仍可安装”的授权；客户端安装前需要 §8.3 的新鲜状态检查。首版不以撤回同步删除或紧急缓存 purge 为上线前提。

## 8. Cache strategy：采用哪种 Cloudflare 缓存

### 8.1 三种机制必须分清

| 机制 | 特征 | 本提案用途 |
| --- | --- | --- |
| **Workers Caching（Worker 前置缓存）** | 命中不运行 Worker；官方当前文档描述自动分层与同键并发合并；命中仍按请求计费 | 推荐 PoC 的主缓存，缓存统一 endpoint 的最终响应 |
| **Worker `fetch()` 的 CDN cache** | 缓存 outgoing origin URL；可配 `cf` TTL；Tiered Cache 在启用/适用时参与 | 可选回源优化；不能靠它承诺临时 GitHub asset URL 的高命中 |
| **Cache API：`caches.default.match/put`** | 每次仍运行 Worker；写入/查询仅本数据中心，无 Tiered Cache，无自动回源合并；不支持 SWR/SIE | 主方案不可用时的显式替代 PoC，或小型可丢辅助 metadata 缓存 |

这不是把 Cache API 升格成 Redis 数据库。三者互相独立；不要对 Cache API 宣称 Tiered Cache，也不要认为只给 Worker response 添加 Cache-Control 就自动启用了 Worker 前置缓存。[Workers Caching](https://developers.cloudflare.com/workers/cache/)、[机制关系与限制](https://developers.cloudflare.com/workers/cache/limitations/)、[Cache API](https://developers.cloudflare.com/workers/runtime-apis/cache/)、[fetch/CDN 交互](https://developers.cloudflare.com/cache/interaction-cloudflare-products/workers/)

推荐前置缓存的原因是它直接缓存 Phinix 的稳定路径，避免 GitHub 302 后带签名 query 的 URL 成为包的长期缓存键。已查到文档支持，不等于已在 Phinix 账号验证；Wrangler/账户可用性、ETag/Range/缓存中断行为列为 PoC 必测。不能用 Dashboard Playground 的预览缓存行为作为生产证据。

不依赖 outgoing `fetch(cf.cacheKey=...)` 的套餐特性。官方示例仍把该自定义 origin cache key 标为 Enterprise 相关功能；具体账户支持以部署实测为准，首版无需它。[cache using fetch 示例](https://developers.cloudflare.com/workers/examples/cache-using-fetch/)

### 8.2 Immutable 与 mutable：建议参数

以下是 **Phinix 初始策略建议**，不是 Cloudflare 默认值或已批准的上线配置。

| 资源 | 客户端 `Cache-Control` | Cloudflare 前置缓存 `Cloudflare-CDN-Cache-Control` | 说明 |
| --- | --- | --- | --- |
| 固定 catalog / 单 DLL manifest | `public, max-age=31536000, immutable, no-transform` | `public, max-age=31536000, immutable, no-transform, stale-if-error=0` | 原始字节不可改；小 metadata 可在 Worker 有界核对摘要 |
| 固定 package | 同上 | 同上 | hash 在路径；全年 TTL 是上限，不是保证驻留一年 |
| mutable stable | `public, max-age=0, must-revalidate, no-transform` | `public, max-age=300, must-revalidate, stale-if-error=0` | 客户端每次网络检查可条件请求；共享 freshness 5 分钟；不由 gateway 提供过期 stable |
| 未知资源/错误/健康诊断 | `no-store` | `no-store` | 禁止默认按状态码缓存错误 |

Manifest 是否 immutable 由定位决定：版本/hash 固定的包内或伴随清单不可变；“latest manifest”、包展示状态和 stable 属于 mutable。完整已发布 catalog 快照不可变，不能把所有 index 一概设短 TTL。新包字节发布新版本，如 `1.4.2 → 1.4.3`；同版本身份/hash 改变拒绝。

Cloudflare-specific header 可以将 edge TTL 与客户端 TTL 分开。当前 Workers Caching 文档还说明 `s-maxage`、`must-revalidate`、`proxy-revalidate` 会禁用 stale 行为；未显式限制时某些错误可长期供应 stale。上述 stable 故意严格再验证并显式 `stale-if-error=0`，避免把撤回信息无限延迟。[Workers 缓存配置](https://developers.cloudflare.com/workers/cache/configuration/)

可选的**只读浏览**实验策略为 edge `public, max-age=300, stale-while-revalidate=60, stale-if-error=3600`；不要同时加入上述禁止 stale 的指令。首版推荐不用该策略：离线浏览交给客户端已校验缓存并标记“过期、不可确认最新撤回”。若未来启用，必须同时设计可验证的 freshness/age 和安装阻断，不能把一个 stale 200 当作最新批准结果。[CDN 再验证行为](https://developers.cloudflare.com/cache/concepts/revalidation/)

长期边缘/R2 包缓存可能仍供应后来从 GitHub 删除的同一文件，这是缓存副本语义，不意味着 Cloudflare 成为权威档案馆；两层均 miss 时不能保证源存在。撤回状态仍通过新 stable/catalog 表达；首版不增加 R2 删除同步或 purge 传播保证，客户端不能缓存“允许安装”一年。

stable 的 300 秒预算必须覆盖整条服务链。默认不要再给 outgoing GitHub stable fetch、辅助 metadata cache 或 Worker isolate 中的 stable 副本各加独立的 300 秒 TTL；前置条目过期时要真正向 GitHub 再验证。若保留这些副本，必须携带并扣除原始 freshness/age，不能由旧副本重新启动外层 TTL，否则撤回窗口会叠加甚至长期续期。只读发布快照与资产映射可长期缓存，当前状态指针不能照用。

### 8.3 ETag、304、客户端缓存与安装新鲜度

- 对固定字节资源使用强 ETag，建议 `"sha256-<原始摘要>"`。stable 可在有界读取真实字节后算强 ETag。ETag 不是签名、兼容性证明或已下载哈希结果。
- Client → CF：有有效本地 body 时发送 If-None-Match；304 复用该 body，重新遵守 freshness/撤回检查。没有 body 时收到 304 要无条件重取，不能生成空索引。
- CF → Worker：前置缓存过期后应再验证；Worker 检查 GitHub 后可返回 304 表示固定表示未变。精确条件请求/更新 Date、Age 的行为必须生产 PoC 核实，不把 Cache API 的文档移用为前置缓存的完整条件语义。
- Worker → GitHub：使用 GitHub 自己的 ETag，不把 Phinix 的 sha256 ETag直接转发。辅助缓存丢失时完整 GET 和重新验证即可；GitHub 304 但本地无 body时重新完整取，不假造响应。REST 官方确认条件请求，正确认证且返回 304 时不占主限额；这不能推广为匿名 raw/asset 请求都免限额。[GitHub 条件请求](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api)
- 上游 raw/Release/对象存储各自的 ETag、If-None-Match、Content-Encoding 与 Range组合须按实际 URL 实测。Worker 输出与源一致的原始字节，禁止 JSON 重新序列化、自动重新打包和内容转换使已锁摘要改变。
- 保留旧设计的本地浏览缓存（建议 30 分钟）和无效刷新保留上次有效目录；但**安装确认前重新向统一 endpoint 检查 stable**，最多接受 300 秒的共享 freshness，不能用 30 分钟浏览缓存授权新安装。Age/Date 缺失或无法证明时刷新/阻断，具体可验证 freshness 表达列为 PoC 项。
- 若 stable 指向新 catalog，先加载验证其完整字节，检查计划闭包中每个包是否仍 active 且身份/hash 不变；变更、撤回或依赖不再满足时重新规划/确认。不能边下载边悄悄升级计划，也不能把旧快照记录当成永久批准。
- origin 不可用且 stable 过期：可离线浏览，不创建新在线安装。旧设计允许锁定完整缓存的离线安装；本提案建议正式首版暂不开放，或仅由玩家显式选择并展示撤回信息未知，需人工决定。

正常撤回延迟预算首先是 stable 的 5 分钟，再加网络/任务执行窗口；它不是全球一致事务。计划确认及提交前复核关闭较长任务窗口，但仍不能承诺零时差撤回或强制终止已运行 DLL。

### 8.4 Cache key、purge 与前置验证

首版只有公开、无账号差异的内容；所有源身份都进入 path，拒绝 query，禁止从客户端 Cookie/Authorization/Forwarded 生成不同内容。Workers Caching 的 host **不在默认 cache key**，同 Worker 多域名会共享同 path；prod/测试用不同 Worker，关闭不需要的 workers.dev/预览入口，不能用不同域名承载不同源却省掉 source path。[Workers cache keys](https://developers.cloudflare.com/workers/cache/cache-keys/)

缓存前置命中可能绕过 Worker 的每次校验，因此只有 Worker 首次验证过并正确标为公开的资源可以进入缓存。若要求每次命中都实时鉴权或阻断撤回，则使用不缓存的 gateway 检查再访问内部缓存 entrypoint，或改 Cache API 路径并重新评估成本；不在首版偷加用户权限体系。

Workers Caching 用其自己的 purge 接口；Cache API 的 delete 只影响本 colo；fetch origin cache 的 purge 对象是实际 subrequest URL。不能 purge 客户端路径就声称所有层和客户端本地缓存全部清除。删除缓存失败不能撤销 GitHub 新 stable 的权威性。[Worker 缓存与 purge 边界](https://developers.cloudflare.com/workers/reference/how-the-cache-works/)

purge API 返回成功也不证明某个键此前存在或现已驱逐，必须重新请求并结合源计数确认。PoC 的批量全冷测试应遵守当前 Free purge 频率，不能连续清空导致测试本身被限流。[Cloudflare purge 说明](https://developers.cloudflare.com/cache/how-to/purge-cache/)

### 8.5 已确认补充：有上限的 R2 按需文件缓存

**定位与范围。** R2 使用 Standard 存储，保存已批准、固定身份/版本/hash 的包及必要伴随清单；不提前同步整个生态，不把 R2 是否有对象当作上架依据。容量淘汰不会修改 GitHub 版本记录或玩家安装记录，旧版本以后仍可通过同一 endpoint 回源。首版不承诺所有历史版本在 CF 永久驻留，也不追踪作者删除并同步清除 R2。

**寻址与版本。** 物理对象键绑定 source、package、版本、SHA-256 及文件角色；相同包出现在多个 catalog 快照时复用同一物理对象。不同版本/hash 不覆盖，沿用当前包大小上限；写入前必须取得可信记录中的大小和摘要。只对完整包响应填充，HEAD/304/部分 206 不当作新包保存；mutable stable 不放进这层永久文件缓存。

**容量与淘汰。** 初始存储硬目标设为 `9,000,000,000` 字节，即十进制 9 GB，不是 9 GiB。计入全部对象、管理数据和正在写入的预留；临时或 multipart 数据若引入也要计入。写入前统一预留容量：

```text
已占用字节 + 尚未完成的预留字节 + 候选对象大小 <= 容量上限
```

空间不足优先删除旧版本副本，尽量保留每个包的最新版本；淘汰顺序由固定版本记录确定，不用每次下载更新一份高频访问排行榜。删除完成并确认账本后才能复用空间。最新版本总量也放不下、清理失败或对象过大时，本次只从 GitHub 流式返回，不保存；不为了必须存入而无限清理或增加容量。删除 R2 不要求删除边缘缓存，边缘仍可继续服务相同固定字节。

**并发控制。** 同键只允许一个有效填充任务，容量检查和预留必须通过最小强一致协调账本完成；Worker 的进程内变量、各节点自行 `list()` 后估算、或 CDN 同节点请求合并不能保证全局上限。其他同键请求可有界等待完成后读 R2，或只回源服务而不重复写入。锁/预留带期限，失败后释放；租约过期的旧任务不能继续提交，避免两个写入者同时占用空间。账本只记录容量、对象与预算，不复制审核/依赖系统。具体协调实现尚待 PoC，未验证时停止新填充，而不是宣称已有硬限额。

2026-10-03 排期复核补充：优先试验 SQLite Durable Object 作为受控桶的唯一容量/操作协调入口，Free 支持该存储后端但仍有独立限额；它是待验证技术选型，不是已部署系统。[DO 定价](https://developers.cloudflare.com/durable-objects/platform/pricing/)、[SQLite 事务 API](https://developers.cloudflare.com/durable-objects/api/sqlite-storage-api/)。**DO 事务不能把外部 R2 put/delete 包进同一事务。** 因此上段的“失败后释放”仅适用于已确认无遗留对象/未完成写入的失败；租约过期或 put 结果未知不能立即把空间重新分配。PoC 要注入 put 成功但账本提交丢失、租约过期后旧任务继续写入、delete 成功但记账失败及账本恢复；不确定字节保留占用/预留，阻止重复填充，复核和清理也先扣预算。不能只用账本 fencing 拒绝迟到回调就宣称物理 R2 上限已证明；无法确认时停止填充，仍可在 Worker/源可用时只回源服务。这是本轮工程推论与验收补充，不是平台的自动事务保证。

**流式保存与校验。** GitHub 成功响应分流给用户和 R2，使用有背压的有界传输；R2 `put()` 带可信的期望 SHA-256，完整写入成功后才记录为可命中的副本。R2 保存失败不使已经成功的用户下载变成失败；源短读、超量或摘要错误仍按完整性失败处理，不能把半包登记为成功或长期复用。客户端始终完整校验后安装；R2 的写入校验不替代 ZIP/PE/manifest 校验。[R2 流与校验 API](https://developers.cloudflare.com/r2/api/workers/workers-api-reference/)

**重试与断连。** 首版允许保存失败后由下一次用户下载重新填充，不增加独立常驻预热/重试队列。客户端沿用有界退避，R2 每次额外尝试也必须重新计入预算。用户断开后，`waitUntil()` 最多延续 30 秒，不能承诺大文件必定写完；允许本次没有保存成功，但要释放/回收预留并保证下次不读半包。[Workers 执行时限](https://developers.cloudflare.com/workers/platform/limits/#duration)

**入口与额度。** R2 桶保持私有，不开放 `r2.dev` 或直接桶自定义域名下载；对外仍是 Worker 的统一 repository 域名，边缘缓存位于它前面。这样受控链路可以统计所有 R2 操作；直接公开桶会绕过代码预算。边缘缓存的总容量由 CF 管理，没有本方案可设置的账号级 GB 硬上限；9 GB 限制只针对 R2。额度降级和账本故障行为见 §14.5。[R2 公开入口说明](https://developers.cloudflare.com/r2/buckets/public-buckets/)、[缓存保留与驱逐](https://developers.cloudflare.com/cache/concepts/retention-vs-freshness/)

## 9. Range Request、断点续传及不完整响应

推荐 PoC 主路径采用 Workers Caching：官方配置文档描述请求 Range 时，平台在调用 Worker 前移除 Range，让 Worker返回完整 200，随后从完整缓存切片返回 206/416；Worker 自己返回的 206 不进入该缓存。**不能假设冷 Range 只让 GitHub 发所需后半段。**[Workers Range 配置](https://developers.cloudflare.com/workers/cache/configuration/#range-requests)

CDN outgoing fetch 的 Origin Range Requests 有另一套资格、分块和 Content-Length 行为，不同于上段；Cache API 则从完整带长度的 200 缓存响应提供 Range，而 put 不接受 206。本方案不能把三种模式拼成一条未经验证的续传保证。[CDN Range 行为](https://developers.cloudflare.com/cache/reference/range-requests/)、[Cache API Range](https://developers.cloudflare.com/workers/runtime-apis/cache/)

首版客户端可延续“断线整包重试”，下载并发最多 2、取消和退避；续传作为后续能力，不作为第一版上线前提。若增加续传：

1. 仅允许单个规范 bytes range，拒绝多 range，限制 offset；客户端固定 URL/源/快照/总长/hash/强 ETag，不混合两个版本。
2. 若 206，核对 Content-Range 起止、总长与已保存偏移，才追加；若 200，丢旧片段重头写；416 核对本地完整大小后完整 hash，无法确认则重下。
3. If-Range 匹配/不匹配、cold/hot Range、HEAD、Content-Encoding、长度保持须实测；特别是前置缓存是否支持要求的强 validator 不猜测。
4. 最终对重组文件做整体 SHA-256 和完整载荷检查，不能只校验下载的新片段。没有全部完成就不生成成功安装记录。

单 range 的校验不能假定放在缓存 Worker 的 handler 就有效，因为前置缓存可能先命中或移除 Range。PoC 先核实平台多 range、If-Range 和非法范围的实际处理；若无法满足限制，再在不缓存的入口校验后调用内部缓存 entrypoint，单独统计该额外调用/CPU。未关闭这个边界时保持首版整包下载，不宣称续传已交付。

不完整响应要分开看：客户端中断不一定阻止平台把从 origin 完整获取的响应缓存；origin 中断则不得把半份包当完整 200复用。Worker 不以 catch 后正常关闭 stream 隐藏回源错误，不把部分 206 写到完整文件键。边缘检查实际字节数与已知总长，在短读/超量时让流失败；客户端独立核对。

**官方材料未给出本组合在所有流错误/断连条件下“绝不保留截断缓存”的完整保证。** PoC 必须主动断 origin、断 client、取消 cache fill，再用第二个客户端请求相同键验证完整长度/hash及是否重新回源。失败需禁用该缓存路径或修改实现后重测，不能靠长期 immutable TTL 掩盖。`waitUntil` 只有有限延续窗口，不承诺玩家断线后仍能完成大文件预热。

## 10. Failure handling

错误默认 `no-store`，不把 HTML 错误页或上游私有详情发给客户端；逻辑包不存在和已批准包的上游缺失区分。下表是建议对外语义，不是当前代码实现。

| 故障 | 服务行为 | 客户端/发布侧行为 |
| --- | --- | --- |
| 边缘缓存清空 | Worker 优先从 R2 返回固定文件；metadata 按原策略再验证 | 不等于 GitHub 全冷回源；记录 R2 hit 与 edge miss |
| 边缘与 R2 文件缓存全部清空 | 从官方 stable/发行描述/catalog/固定 asset 重建；账本失效时先禁填充并复核 | 性能退化但身份/hash不变；全冷恢复 PoC 必测，不依赖 R2 保留历史 |
| GitHub 不可用 | 已有 fresh metadata 和 edge/R2 固定文件可返回；两层均 miss 返回 503 `OriginUnavailable` | 保留本地有效索引，显示离线；stable 新鲜度约束仍适用，不得直连 GitHub |
| GitHub 很慢 | metadata 回源建议 10 秒，包首部 15 秒；body 空闲 30 秒，包总预算建议 10 分钟，待测调优 | 504 或 stream 中断；最多两次带抖动的重试，取消始终有效 |
| Worker 部署/运行故障 | 已缓存公开响应可能仍可命中；未命中失败 | 保留缓存/任务失败；使用 Worker 作为 origin 的 Custom Domain；若另用传统 route，则配置 fail closed，不旁路 origin |
| Worker Free 日额耗尽 | 平台可能返回 1027，不能承诺业务 JSON 或缓存仍可供给 | 错误分类、降频；默认等待恢复，不自动升级 Paid 或直连 GitHub |
| 未知 source/plugin/version/hash | 本地验证拒绝或 404 `ResourceNotPublished`，不访问任意上游 | 不把非法 path 用于回源/缓存键爆破 |
| 已批准 GitHub asset 返回 404 | 502 `PublishedArtifactMissing`，no-store；404 不证明永久删除 | 默认源监测复核，发布新索引暂停该版本；保留原摘要/审计记录 |
| Release/仓库删除 | edge/R2 固定字节可能仍可下载；两层 miss 无法重建 | 不改用同名仓库或新 asset；不增加源删除追踪，历史版本不保证永久保存 |
| index 更新但 asset缺失 | 发布侧先检查完整依赖集和资产再动 stable；漏检时下载阻断 | 全闭包全部验证前不提交任何包；不静默跳过依赖 |
| 同时 miss | 前置合并不等于全局一次；由协调账本授予一个 R2 填充任务并预留容量 | 其他请求有界等待或只回源，不重复写入；账本故障禁填充 |
| R2 写入失败/空间不足 | 完整源响应仍可流式返回，不记为已保存；释放预留 | 下次用户下载再尝试；不无限重试或扩大容量 |
| R2 读取失败/读预算不足 | 跳过 R2，由 Worker 从 GitHub 流式返回；无法确认对象状态时也不新填充 | 仍只连接 CF，源不可用则失败；不绕过预算直接访问公开桶 |
| GitHub 403/429限流 | 区分限流与权限；返回 503 `OriginRateLimited`及有界 Retry-After | 遵守 reset/退避，停止刷 API；不上报 token/私有路径 |
| body断线/错误长度/错误摘要 | 连接中断或客户端校验失败；不产生安装成功 | 保留或删除受控临时片段；重新获取仍不匹配则暂停/报告，不刷新期望 hash |
| 合法/非法 Range | 主缓存切片，协议结果可200/206/416；不完整片段不缓存为全包 | 校验范围/总长；必要时整包重试 |
| GitHub 401/私有内容 | 502 `OriginPolicyViolation`，不把授权 body缓存 | 公共源不支持私有包/token上传；不得把可读私库经公网曝光 |
| 4xx/5xx及无效metadata | 首版全部 no-store；保留既有已验证本地索引 | 区分404、429、502、503、504；不记录为可安装 catalog |

正式正常响应显式设 TTL，禁止平台默认把 404/5xx缓存很久。未来为已确定不存在的内部资源加 15–30 秒负缓存，只能在确认不会掩盖刚发布资源时单独测试，不能一律缓存 GitHub 404。

GitHub REST 公共未认证额度按出口 IP 计，不按玩家计；Worker集中回源可能比直连更早遇到共享限额。官方确认 60 次/小时，常见认证额度 5000次/小时并有 secondary limits。优先使用完整 catalog、固定资产、缓存/条件请求；若真实回源量需要令牌，仅保存在 Worker secret，使用最小只读权限且只访问公共 allowlist。Worker 没有控制自己的公网出口 IP 的承诺。[GitHub REST limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api)

Worker 每次请求仅回源当前所需小描述和资产，禁止临时扫描整个生态/递归下载闭包。边缘 CPU预算和 6 个连接限制下串行关闭不用的 body；单包 miss建议不超过 10 次 subrequest含重定向。客户端重试和发布监测分别设上限，防止冷缓存突发与攻击造成源限流。

## 11. Security：严格 allowlist / deterministic mapping

1. **先选受信源再读发布记录。** sourceId映射到部署固定的公共索引仓库及repositoryId/ownerId；不能让用户 query/path换 owner/repo。Worker 源配置保存在可审计配置中，业务批准保存在GitHub。
2. **请求必须对应已发布catalog中的完整记录。** 输入 snapshot也不能等于任意仓库 commit；先通过 GitHub已发布描述确认该快照曾正式发布。path hash与记录不符拒绝。映射是approved record → 固定asset，不是用户 URL → fetch。
3. **来源身份必须核对。** approved repository/owner ID、release/asset归属、tag/commit、名称/长度都绑定；只允许public、非draft、批准渠道的Release。仓库转移/重建暂停，不因为同名可访问继续信任。
4. **严格路径与配额。** 沿用标识/版本/hash校验；拒绝 `..`、`.`、编码斜杠/反斜杠、双重解码、NUL/control、空段、过长输入。URL标准化与路由解码必须做攻击样例测试，不能仅检查解析后的字符串。未知请求不制造大量独特cache keys。
5. **重定向逐跳验证。** 固定 HTTPS host allowlist，初始只允许预期GitHub API/raw/release端点；返回对象域名只有来自正确asset的GitHub响应才可使用，同时约束精确host/路径/端口。禁止 `*.githubusercontent.com`任意匹配、用户信息、HTTP降级、外部/private/loopback目标。GitHub实际下载host需PoC建立有限名单，不能认为只写`objects.githubusercontent.com`覆盖全部情况。
6. **凭据不外传。** 跨主机去掉GitHubAuthorization；客户端Authorization/Cookie/Proxy-Authorization不送GitHub。Worker若有读令牌不能具备不必要的私库权限；public属性由元数据核实，带令牌能读不代表可公开。拒绝用户上传PAT和私有URL，logs不保存签名query、token或代理密码。
7. **缓存投毒防护。** 只缓存有效metadata及严格映射成功的包响应；固定Content-Type/no-transform，不按任意请求头输出差异内容，不盲透传Location/Set-Cookie/Vary。未知Host/Forwarded不能影响映射；缓存前置host不入键的问题见§8.4。hash错误由客户端拒绝，但边缘污染仍须purge与调查。
8. **资源滥用控制。** 公共读取有请求/CPU/源负载上限；no query避免无限busting。采用客户端抖动/退避、实际可用的边缘限速与观测；不预设付费WAF或强制浏览器challenge（游戏HTTP无法解challenge）。purge和部署权限不暴露公网。

仅隐藏GitHub URL不是可信来源验证，也不是SSRF完整防护。协议必须用结构化身份生成origin路径，不把外部catalog/Release描述里的任意URL直接传给fetch。客户端报告异常也是数据，不能自动触发权限变更、重新批准或修改摘要。

## 12. Integrity / supply-chain considerations

延续当前链：受信source → 官方批准的固定catalog字节 → 锁定包记录及期望摘要 → 实际package/manifest字节 → SHA-256/长度 → manifest交叉核对 → About/ZIP/PE检查 → 全闭包暂存/提交。

hash由**官方可信发布校验器对实际获取的候选字节独立计算**，存入GitHub索引，不接受作者清单自报hash作为唯一事实。catalog摘要来自GitHub正式stable/发行描述，客户端还计算其原始字节摘要。Worker缓存命中、ETag、TLS、GitHub `digest`字段都不能代替客户端检验，也不能自动改旧记录期望hash。

信任基线要如实说明：未签名阶段客户端信任配置的repository endpoint/TLS及其正确代表的官方GitHub发布者。GitHub作为Source of Truth是架构约定；**只有同链返回的SHA-256，不能让客户端独立证明Cloudflare从未改写索引和hash。** 如果要求防止被攻陷的gateway伪造整个catalog，需要GitHub发布侧签名、客户端固定信任公钥，且签名不能由Worker生成。

旧方案没有已实现签名协议。建议正式联网发行前决定是否增加**索引/发布指针签名**（优先于每位publisher一套钥匙）：签署原始catalog摘要、source、快照/格式、发布序列、时效和资产映射；签名/密钥轮换记录保存在GitHub，客户端固定root keys。需要规范序列化或明确原始字节签署规则，以及回滚/离线/密钥吊销行为，不在本文凭空指定已实现算法。publisher signature可作来源加强，但增加作者key登记、丢失、撤销与自动更新负担，首版不默认强制。

签名证明批准内容来源，不证明DLL无害；GitHub发布凭据与签名key同任务失陷仍可能伪造发布。源码可审查不等于binary可复现；已有manifest/PE校验不执行候选、不建立进程沙箱。强名称也不是publisher安全签名。

建议索引Release启用GitHub immutable releases，鼓励作者使用：官方文档说明已发布asset/tag不可变，但整个Release仍可能删除，所以不是永不丢失归档；普通Release也可能替换asset，必须靠固定identity/hash拒绝变化。[GitHub immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)

撤回在GitHub新索引里标state，短TTL传播；已有同版本cache不可被重新认定为新内容。手工历史下载不等于允许新安装。按本轮确认，首版不建设 R2 撤回删除同步或历史 URL 紧急阻断系统；这些需求若将来出现再单独设计。不得自动删除存档、库存、pending delivery、红包/人才数据或回滚结果未知物品；代码包回滚不是业务数据回滚。

## 13. Mainland China connectivity validation plan

### 13.1 网络定位与部署域名

普通Cloudflare全球网络是待测试的跨境公网路径；本提案不购买或声称使用大陆节点。Cloudflare China Network是Enterprise另行订阅、合作方JD Cloud提供的大陆网络，要求域名具备适用ICP备案/许可；产品支持不能由全球Workers能力推定。[China Network官方说明](https://developers.cloudflare.com/china-network/)

workers.dev适合快速验证，但生产建议使用受项目控制的repository自定义域名。官方推荐production使用route/custom domain；本方案优先使用Worker本身作为origin的Custom Domain，不设置指向GitHub的可旁路传统origin。自定义域名方便保持contract、换Worker和调整DNS，但本身不保证大陆加速、解封或可达。测速同时观察workers.dev和自定义域，最终决策以实际生产候选域为准；关闭不需要的preview/备用入口，避免缓存别名和滥用。[路由选择](https://developers.cloudflare.com/workers/configuration/routing/)、[Custom Domains](https://developers.cloudflare.com/workers/configuration/routing/custom-domains/)、[workers.dev](https://developers.cloudflare.com/workers/configuration/routing/workers-dev/)

### 13.2 A/B/C实验和样本

| 路径 | 必须测到的行为 |
| --- | --- |
| A. GitHub direct | metadata raw/固定Release包，从客户端实际跟完所有GitHub跳转；记录每个连接域名 |
| B. CF edge miss + R2 miss | CF→GitHub真正回源→CF→Client，并尝试 R2 填充；捕获客户端仅连CF、Worker源请求和两层 MISS |
| B-R2. CF edge miss + R2 hit | R2→Worker→Client；记录 R2 读取，核实包 body 无 GitHub 回源 |
| C. CF cache hit | 相同字节/同一canonical URL，由cache返回；记录HIT以及colo，核实无源请求 |

使用同一组**自有、公开、非执行测试文件**，GitHub正式测试Release作为authoritative origin；记录全长/SHA-256。metadata取5–50 KiB并补2 MiB极限；包取5/10/20 MiB不可压缩ZIP或二进制；大文件取64/128 MiB验证现有包上限。512MB只在必要、额度/条款已确认时做平台边界测试，不代表Phinix允许这种包。避免零填充文件经压缩获得假吞吐。

先用ITDOG做覆盖和连接分段诊断，其HTTP页面有电信/联通/移动选择以及响应信息；不能仅凭页面200或数秒小响应证明整包成功。[ITDOG HTTP](https://www.itdog.cn/http/)

17CE公开V2.12接口文档包含DNS/连接/TTFB/总时间、FileSize/RealSize和响应头；文档较旧，当前账户/接口、完整body上限、重定向、导出、TLS分段和教育网节点须先核实，不能假定都免费且能完整下载128MiB。本次打开首页未得到可用正文，未执行任何测速。[17CE官方接口PDF](https://www.17ce.com/soft/17CE_WS_API_v2.12.pdf)

工具不能完整下载/算hash时，只作为可达性样本；补充大陆志愿测试者/受控探针的curl完整下载、SHA-256，以及实际游戏Mono下载。学校网络尽力加入CERNET，平台不支持则写明样本缺失；不把海外或BGP节点替代三大运营商家庭网络。

建议至少三大运营商各10个省份/城市节点，连续7天，早晚和晚高峰覆盖，A/B/C随机轮换、每节点每时段至少3次；可分批先测小文件控制配额。失败样本不能从总体剔除；节点供应商类型、IPv4/IPv6、HTTP版本、代理、DNS缓存、连接复用和超时参数一并记录。云机节点结果与家庭宽带分开报告。

### 13.3 指标定义与冷/热控制

- 成功：在预设deadline内完整下载、状态码符合预期、长度及SHA-256正确；仅HTTP200不算。
- 记录DNS、TCP connect、TLS、TTFB、总下载时间、实际字节数/下载阶段时间计算吞吐，redirect每跳时间另列。curl时间通常是累计值，应相减得到阶段耗时；全程记录失败阶段、错误码、重试次数。
- 每个运营商/城市/文件/路径分别报告成功率、失败节点比例、TTFB和完整下载P50/P95、吞吐P50/P95、重试率；成功样本延迟和全样本失败率同时展示，不能把失败删除后用低均值宣布改善。
- Cache命中/上层命中、Age、colo、源请求数、CPU/内存错误和输出hash全部进入测试记录；cache status不是完整性的替代。
- B需在隔离测试Worker清空**前置、辅助origin缓存及对应 R2 测试对象**，同步复核容量账本，或使用预登记的有限测试资源键；不向生产API开放随机cache-buster。B-R2 只清边缘层并保留 R2，不能混作 GitHub 冷回源。第一批multi-node请求可能只有少数真MISS、其余HIT/合并，上层HIT不能标为GitHub冷回源。
- 因Tiered Cache，不能用“该城市第一次访问”代表全冷；以源日志/请求统计核实真正GitHubfetch。C应在相同URL/路径上预热再测，保留cold/hot分类，不能用一个HIT头推定每个节点都命中。
- 避免为了多节点冷测把origin打爆；批量purge串行安排，暂停限流后继续。A是PoC独立测试工具的路径，正式客户端不会保留A为自动fallback。

### 13.4 建议继续条件

下面是待团队批准的gate，不是实测结论：

1. 三大运营商各自B、C完整下载成功率目标≥99%；若A本就≥99%，B/C不得下降超过1个百分点。A明显低于95%时，B/C应至少提升5个百分点，且不得有某大运营商系统性退化。样本不足时不给强结论。
2. C的5–20MiB完整下载P95比A降低≥30%，或在延迟相近时显著降低断连/重试；B的P95建议不超过A的1.5倍且更稳定。成功率优先，不能用热缓存速度掩盖冷缓存不可达。
3. 64/128MiB完整校验、中断重下、全冷恢复通过；所有B/C客户端连接只到CF，零GitHubLocation泄漏、零截断cache复用。安全/完整性是硬gate，不用性能换取。
4. Free候选metadata/包MISS CPU无超限，真实请求与峰值有余量；R2 填充/淘汰/预算降级通过，大文件条款/套餐确认。超限时先降低调用/验证实现并报告，Paid 只作后续可选评估，不自动启用。

不达标时继续测不同自定义域/配置或保留A作为人工发行路径，不发布“大陆优化已解决”。B成立但只某些网络受益时准确标注覆盖；Cloudflare本身故障域和跨境波动仍存在。

## 14. Cloudflare current limits and cost

### 14.1 2026-10-03核实的官方技术额度

| 项目 | Workers Free / 相关Free zone | Paid / 设计含义 |
| --- | --- | --- |
| 入站请求 | 100000/天，UTC零点重置（本地08:00）；账户级共享 | Paid按月计费；cache HIT/304/HEAD/Range也要计请求 |
| 每次HTTP CPU | 10ms | 默认30s，可配置至5分钟；等待网络不计CPU，解析/hash/JS流处理计CPU |
| 内存 | 每isolate128MB | Paid仍128MB；流式代理≠整包buffer |
| external subrequests | 50/次，重定向每跳计数 | 默认10000/次；本服务应远低于额度 |
| 并行outgoing connections | 6/次 | 不为了一个包并行列遍作者仓库 |
| response body | 不设硬大小上限 | 不等于cache无上限或允许无限流量 |
| zone cache单对象 | Free/Pro/Business 512MB | Enterprise一般5GB；现有128MiB包更小 |
| HTTP wall time | 客户端连接存续时无硬总时限 | 仍设自己的超时；断开后waitUntil最多延续30s |

额度依据[Workers limits](https://developers.cloudflare.com/workers/platform/limits/)。Free上传request body的100MB限制不应误写成包下载response限制；本服务GET/HEAD无需上传包。Workers Caching当前文档另注明推出阶段所有套餐按Free大小上限，不能把Enterprise5GB直接套给这个实现。[Workers Caching限制](https://developers.cloudflare.com/workers/cache/limitations/)

2MiB strictcatalog解析、metadata摘要与cold mapping可能超过10ms，不能由“只是代理”推断Free必够。PoC要分cachehit、stable更新、2MiBcatalog全冷、package全冷等测CPU。Worker轻量校验与源身份核对必要，不能为节约CPU删验证；超限先评估Paid。大包整体验证保留发布端/客户端，客户端PayloadValidator当前内存冻结也需在实际游戏单独验收。

### 14.2 粗容量模型

基准假设：每DAU每天4次stable网络检查；10%的用户取得一份新catalog（0.1请求/人）；10%每天下载一个ZIP包（0.1请求/人），平均10MiB；额外为确认freshness、重试、HEAD、诊断留10%。不计publisher监测（在GitHubActions），不逐包调用metadata。**这是模型假设，没有现有DAU/真实下载统计。**

```text
基础入站 = DAU × (4 + 0.1 + 0.1) = DAU × 4.2
含余量   = DAU × 4.62
月请求   = 日请求 × 30
包字节   = DAU × 0.1 × 10 MiB = DAU MiB/日
```

| DAU | 基础请求/日 | 含10%余量/日 | 30天请求 | 仅包流量/日 | Free日额判断 |
| --- | --- | --- | --- | --- | --- |
| 100 | 420 | 462 | 13860 | 约0.098GiB | 请求充足 |
| 1000 | 4200 | 4620 | 138600 | 约0.98GiB | 请求充足 |
| 5000 | 21000 | 23100 | 693000 | 约4.88GiB | 请求充足，关注发布峰值 |
| 10000 | 42000 | 46200 | 1386000 | 约9.77GiB | 请求有余量，仍需CPU/条款检查 |
| 20000 | 84000 | 92400 | 2772000 | 约19.53GiB | 仅剩7.6%，不适合作长期生产余量 |

按基准临界约21645DAU；每天8次检查则为`DAU × 9.02`，10000DAU约90200/日，20000约180400/日，Free不够。若每次还请求每个包更新metadata，额度会进一步恶化；客户端从完整catalog本地比较即可。自动检查随机抖动、不在Draw发请求、30分钟浏览缓存降低重复请求；条件304减少字节而非请求数。

下载率/包大小只影响流量与origin压力，不直接改变单次请求计价；依赖闭包、DLL伴随manifest和每次Range重试增加请求。包平均20MiB时上表字节翻倍。边缘缓存命中不是免费请求，也不是让每天100000额度按miss数量算。

### 14.3 Paid、计费和源负载

当前WorkersPaid最低$5/月，含1000万请求/月与3000万CPU毫秒/月；超额每百万请求$0.30，每百万CPU毫秒$0.02，无额外egress/bandwidth计费。Workers前置cache也按标准请求计费，HIT不收CPU。依据[Workers pricing](https://developers.cloudflare.com/workers/platform/pricing/)和[Workers Cache pricing](https://developers.cloudflare.com/workers/cache/#pricing)。

示例：20000DAU基准277.2万/月，如果平均CPU每次2ms且每次都执行Worker（保守估算），约554.4万CPUms，处于基础Paid包含量；不用把DAU增长理解成高额带宽账单。若平均每次20ms且都执行，约5544万CPUms，CPU超额约$0.51；这是情景计算，前置HIT比例、真实CPU、账号其他Workers会改变结果。域名、测速服务、GitHubActions/可选AI审核、额外日志/WAF/产品费用不在这$5里。

日请求持续达70000–80000、一次发布可能突破日额或必要验证超过10ms时，先报告并减少请求/优化实现；Paid 是需要另行选择的低成本候选，不是本轮默认预算。Paid不改变GitHub限额：例如10个colo各300秒回源一次stable，仅此一项120次/小时；前置分层合并可能减少，但不能假设未认证60次/小时一定够。统计**实际**GitHubAPI请求、redirect及secondarylimit，必要时最小只读认证，不能对所有下载URL套REST60/h。

### 14.4 大文件与“永久免费”的限制

Cloudflare当前ApplicationServices条款对非Enterprise的CDN大文件服务指出需适当PaidServices，并保留对大量大文件用途限制访问的权利；DeveloperPlatform条款也允许在过度负载等情况下限制服务。这里讨论的是插件ZIP/DLL分发，不能只引用Workers的无egress费推导普通FreeCDN可无限长期分发二进制。[CDN服务条款](https://www.cloudflare.com/service-specific-terms-application-services/#content-delivery-network-free-pro-or-business)、[DeveloperPlatform条款](https://www.cloudflare.com/service-specific-terms-developer-platform/)

**待确认：**我们这种GitHub权威、私有R2按需缓存、经Workers及其cache分发5–128MiB插件包的具体用途，在Free和$5Paid各自是否满足适当服务要求；是否存在额外内容/流量限制。官方技术文档能证明可流式代理与大小上限，不能单独证明所有套餐下这种用途被无条件接受。上线前核实账号适用条款/向Cloudflare确认具体模式；Paid是合理低成本候选，不在本文保证仅付$5就解决所有条款问题。

R2 并非流式回源在技术上的必要条件，但本轮已决定把它作为有界按需文件缓存，以减少边缘驱逐后的 GitHub 冷回源。不能由“用了 R2”推导整个组合无条件免费或自动满足所有适用条款；它不是新的版本 authority。限速、预算与域名/源监测仍必要。

### 14.5 已确认补充：R2 免费额度与代码预算

当前 R2 Standard 每月含 10 GB-month 存储、100 万次 A 类操作、1000 万次 B 类操作，出站流量不收费；免费量不适用于 Infrequent Access。超出后按存储与操作计费，不是到 10 GB 自动停止写入。存储按账期内每日峰值计算，月底删除不能抹去之前的超量，因此采用始终预留后的 9 GB 容量目标。[R2 定价与存储计算](https://developers.cloudflare.com/r2/pricing/)

| 预算 | 初始设计值/处理 |
| --- | --- |
| R2 总字节 | `9,000,000,000`；先预留、再写入，不足先删旧版本；不能腾出则只转发 |
| R2 A 类操作 | 建议先设每账期 800000 次受控预算，给免费 100 万次留余量；耗尽后停止填充及其他 A 类调用 |
| R2 B 类操作 | 建议先设每账期 8000000 次受控预算，给免费 1000 万次留余量；耗尽后停止 R2 读取及新填充，Worker 直接回源 |
| Worker 入站 | 保持 Free 的账户级 100000/天限制；HIT 仍计请求，耗尽允许暂停服务，不自动转付费 |
| 协调账本/其他 CF 服务 | 实现选型时一并核算其自身请求、CPU、存储与免费额度；不能把协调资源当作免费无限使用 |

A/B 数值是可调的保守设计起点，不是已部署计数器。`put`/`list`/multipart 各步、`get`/`head`、重试、清理查询和管理脚本均按实际操作类别计入；删除本身当前免费，但为删除执行的 `list` 仍消耗 A 类。不要每次命中都扫描整个桶或先 `head` 再 `get`。计数按账号适用账期重置，不用自然月假装等同；Worker 日额另按 UTC 重置。[操作分类](https://developers.cloudflare.com/r2/pricing/#class-a-operations)

严格控制需要在操作前通过统一账本预留预算，覆盖并发和失败重试；私有桶只允许受控 Worker/管理入口访问，并扣除同账号其他 R2 用量。仅靠滞后的平台统计、日志告警或各 Worker 本地计数，不能构成硬停止。容量账本丢失可扫描对象重新确认占用，但扫描也要有预算；操作累计值不能仅从现存对象还原，无法确认时停止相应操作直至复核/安全重置。账本故障不能启动一轮不受控回填。

额度不足时降级的是 R2 读写：用户仍通过 CF Worker 获取 GitHub 字节，不跳转或直连 GitHub；若 Worker/回源自身也不可用，则返回错误或暂停，而不是保证不受限供给。存储限制与操作预算控制的是这条受控链路，不能单凭 9 GB 承诺整个账号绝对零费用；CF 的预算提醒是通知，不是代码熔断。[CF 预算提醒](https://developers.cloudflare.com/billing/manage/budget-alerts/)

## 15. Alternatives considered：结合Phinix的A/B/C决策

| 维度 | A：Client→GitHub | B：GitHub权威+强制CF+按需R2 | C：CF保存权威registry/package |
| --- | --- | --- | --- |
| 与当前计划关系 | 旧设计待实现的网络路径；v1GitHub字段自然适配 | 增量替换网络层，增加有上限文件副本/预算协调，不改生命周期/安装边界 | 新增服务端状态/发布平台，超出当前独立索引设计 |
| 权威/发布责任 | 官方GitHub索引、作者Release | 同A，CF读取/缓存，R2不决定批准或版本 | 若D1/KV决定批准/版本或R2唯一持包，出现第二份权威/唯一状态 |
| 大陆网络 | 玩家需要访问多个GitHub域名 | 玩家只连接CF；是否改善由PoC决定 | 仍依赖玩家到CF网络；并不自动获得大陆节点 |
| 失效恢复 | GitHub可用才可下载；客户端本地缓存可浏览 | 边缘miss可读R2；两层全冷需GitHub；允许淘汰，源删除后不保证恢复 | 增加同步、备份、校验、回滚、删除、权限和一致性维护 |
| 可信/完整性 | 同源未签名hash无法防索引接管 | 多一个serving信任边界；延续hash，签名可独立验证gateway | 同样需要hash/签名；多存储不自动更可信 |
| 小团队成本 | 服务最少，终端代理/限流/跳转复杂 | gateway、私有R2和最小预算账本；按需填充，旧版本淘汰，无全量同步服务 | 难以用现有少量维护者/未上线商店证明额外复杂度值得 |

本轮已确认 B 的按需 R2 变体，先做 PoC；A保留为比较基线和玩家主动手工下载方式，正式商店不做自动fallback。若PoC显示B对主要运营商整体更差，应停止上线并重新选分发路径，不能为倾向B而预设成功。

C需区分两种情况：D1/KV里允许独立修改批准/最新版本是第二权威；R2仅存GitHub内容hash寻址且允许淘汰的副本仍是非权威缓存，现已纳入 B，不再作为待决定是否引入的额外平台。永久归档、全量同步或允许 CF 独立发布仍不在本轮范围；容量满删旧版本不等于删除官方历史版本记录。

## 16. Migration from current design

当前没有已交付的GitHub在线安装客户端，所以这是**实施前重新定网络contract**，不是迁移已有线上安装数据库。现有未提交的本地浏览/解析/校验继续保留；测试fixture的假GitHubID/hash不能用于PoC下载。

1. 接续主计划步骤1/5，在实现transport之前增加本提案PoC gate；步骤7的GitHub发布自动化仍保留。旧文档保留原始评估，本文明确覆盖其“客户端直接GitHub”的网络段，不静默改历史。
2. PoC复用schema v1原始catalog、固定ZIP/期望摘要，先证明统一endpoint、两层全冷重建、edge/R2命中、流式填充、容量淘汰/预算停止、Range/错误与大陆链路。
3. 正式发行前决策schema v2/签名和stable/发行描述格式；不向v1添加未知字段。同步schema、生成器、catalog/manifest校验、测试样例及作者中英文指南，不要求本轮提前实现。
4. 客户端GitHubSource替换为RepositoryTransport，GitHub解析/跳转挪到Workeroriginadapter；包模型/求解/游戏环境适配保留。共用代码不倒灌Common，不让Host引用商店实现。
5. 后台任务仍以控制器终态为准；网络任务需专门的连接/空闲/总预算，不能照搬本地读取十秒watchdog。主线程捕获游戏环境/翻译/UI，流式IO和校验在后台。
6. 传输缓存、安装记录按source+快照+摘要隔离，已有来源未知Mods不认领。当前PayloadValidator冻结包内存，落盘transport不代表校验峰值已降低；测真实游戏128MiB峰值，必要优化单独设计验证。
7. 安装仍每包独立本地Mods目录，全闭包校验后事务提交，玩家启用/重启；不写Phinix主包和Steam目录。不迁移已有`framework-extensions/client`或业务数据；商店新数据根沿用`<SaveData>/Phinix/ExtensionData/phinix.plugin-store`。
8. 红包/人才独立发行、资源归属、旧数据及存档验收仍是主计划单独阶段，CF分发不替代此门槛。服务端继续手动部署配套插件，客户端下载成功不证明服务器能力。

## 17. Open questions：人工决策与待验证项

### 17.1 需要团队决定

R2 按需缓存、优先免费额度、容量不足删旧版本、填充失败由后续下载重试，以及不建设专门撤回删除系统，已由本轮讨论确认，不再列为是否采用的待决项。9 GB 上限与操作预算是初始设计值；协调实现和组合行为仍待验证。

| 决策 | 推荐起点 | 为什么尚不能定案 |
| --- | --- | --- |
| repository域名、运维归属及免费方案可行性 | 自定义域名；优先Free，不自动转Paid | 大陆实测、账号CPU/协调服务额度及大文件服务条款未验证 |
| 正式首版metadata格式 | PoC复用v1；正式首版优先评估v1配独立版本的stable/发行描述，v2不进首批 | v1仍绑定GitHub，不能承诺无需客户端升级换provider；若首版要求该能力则提前实施v2，并同步包清单/生成器/规范 |
| 是否正式首版签名索引/指针 | 明确serving威胁模型；若需独立验证CF则签名必须在GitHub发布侧 | 根key、轮换、序列/时效和自动发布key保管未设计完成 |
| stable TTL、离线安装 | 300秒、无gatewaystale；离线只浏览，不增加撤回删除SLO | 参数及玩家手动离线缓存安装仍需验证/产品决定 |
| 自定义源托管范围 | 官方gateway先仅`phinix.official`，保留外部同contractgateway配置 | 不能因旧自定义源功能默许运营无限GitHub代理 |
| 来源授权与未来永久归档 | 首版R2按容量淘汰，GitHub历史不保证删库后可恢复 | 镜像仍遵守来源许可证；永久归档是未来独立需求 |
| 容量/操作协调实现 | 最小强一致账本，只做预留、填充和预算，不保存业务权威 | 需验证并发、租约过期、账本故障及协调服务自身免费额度 |
| PoC gate、样本和测速预算 | §13建议阈值和7天跨运营商样本 | 团队容忍度、家庭/教育网可用节点和平台费用未知 |

### 17.2 已有官方依据但仍需组合实测

- Workers Caching在账号/Wrangler的实际启用、quota及大小限制；不能仅用文档或本地模拟宣布可用。
- 前置cache的ETag/If-None-Match/304、Date/Age/freshness、If-Range，以及生产TLS/Content-Length/no-transform下原始字节一致。
- GitHub实际raw/asset主机、有限redirectallowlist、私有/转移拒绝、匿名/认证回源限流；临时签名URL过期后的miss重建。
- origin短读/client断开、Range冷请求、并发fill、边缘/R2分别或同时清空时不完整响应处理；官方未确认的保证不猜。
- R2校验失败不能登记成功；慢客户端/慢R2分支内存有界；保存失败后下一次回填、同键锁/容量预留、删旧版本及操作预算耗尽的停止行为。
- 账本不可用/丢失/租约过期、并发接近9GB边界、管理脚本和同账号其他用量是否全部计入；预算不得依赖延迟统计承诺硬停止。
- 2MiBmetadata和128MiB代理的CPU/流背压峰值；net472/Unity下载/代理/取消/落盘以及现有校验器内存峰值。
- ITDOG/17CE当前完整body大小、TLS分段/导出/节点权限，三大运营商和CERNET样本；普通CF比GitHub稳定这一假设尚无测试数据。
- Cloudflare大文件用途在所选套餐的适用要求；Paid与“无限带宽”不能替代具体模式确认。

## 18. Recommended PoC

**最小范围：一个公开官方测试源、一个固定v1catalog、一个5/20MiBZIP及一个128MiB测试文件、一个自定义域Worker、一个隔离私有R2桶及最小容量/预算协调实现，无审核数据库。** 测试文件不包含游戏DLL、凭据或玩家内容；PoC请求不会安装/执行第三方代码。测试容量和操作阈值可设得很小，用少量对象触发淘汰/停止，不需要真实消耗9GB或免费额度。

先在受控环境证明路径→批准记录→GitHub固定asset、客户端仅连接CF、edge hit/R2 hit/两层 miss以及长度/hash；再验证流式保存、校验/断连、容量预留、旧版本淘汰、预算耗尽、账本故障、条件请求、并发、Range和大小/CPU；最后跑大陆A/B/C及B-R2。主缓存按当前官方能力选择Workers Caching；若账号不支持或组合行为不满足，单独用CacheAPI做替代试验并如实降低TieredCache/并发合并预期。

硬验收包括：非法source/id/hash/ref/URL不回源；私有/draft/身份漂移拒绝；asset被换内容hash拒绝；GitHub302不泄露；2MiB索引不产生部分发布；stream错误后第二用户不收到半包edge/R2缓存；清空两层文件缓存仍仅凭GitHub复原；容量含并发预留不越界，旧填充租约不能重复提交；达到预算不再调用对应R2操作，故障不绕过计数；保存失败不误报成功，下次仍可填充；origin和Worker故障不自动客户端GitHubfallback。成功率/运营商gate及成本按§13–14记录，失败结果同样保存。

PoC报告应交付：配置/文档版本、测试源与完整hash、网络连接证据、cold/hot请求及源计数、原始节点样本与分组P50/P95/失败率、CPU与额度、条款/套餐确认记录、通过/失败门槛及残余风险。报告放本专题目录，不能把PoC成功升级为游戏内安装验收。

## 19. Implementation plan（仅阶段，不编码）

本表保留架构阶段；当前按代码进度拆出的实际批次见 [主实施计划 §0.2](插件商店分步实施计划.md#02-当前实现批次及依赖)。先本地定义供 PoC 使用的最小测试协议/发布描述及固定输入，再做公网链路验证，结果用于正式 contract 冻结；不能要求没有定位格式的 PoC 先完成。已有游戏预览、客户端 metadata/cache 的本地替身测试及网络样本采集可以按依赖同时推进，不等待全量发布自动化或七天观测结束才写客户端。

| 阶段 | 产出 | 进入下一阶段条件 |
| --- | --- | --- |
| 0. 本轮调查/设计 | 本文、旧方案对照、官方来源与待决项 | 本轮交付止于此，无部署 |
| 1. 平台/公网PoC | §18证据、R2填充/淘汰/预算验证及大陆A/B/C/B-R2报告 | 无外部跳转/半包/任意proxy；容量/预算、网络与套餐gate通过 |
| 2. 发布contract冻结 | stable/已发布描述、schema兼容路径、签名选择、域名与源策略 | 明确provider-neutral能力与v1限制；作者中英文规范同步 |
| 3. GitHub发布侧补齐 | 可信校验器、审批指纹、完整快照/descriptor/指针最后提交、回滚记录 | 失败保留旧stable，旧已批准hash不覆盖，单DLL清单可定位 |
| 4. gateway与客户端transport | allowlist、edge/R2、流式填充、容量/操作协调、etag/错误、代理/取消/重试、观测与文档 | 预算/容量及网络测试、实际Unity/Mono通过，不让Host/Common承载商店业务 |
| 5. 安装/恢复集成 | 全闭包校验、staging/事务/归属、重启恢复、明确启用提示 | 文件失败不部分安装；未知本地包不覆盖；真实游戏验收 |
| 6. 候选发布与有限放量 | 平峰/发布峰值、索引状态更新、缓存淘汰/额度停止、预算/限流监控、作者指南 | 再审官方额度与条款，保留未验证边界，必要时推广稳定文档 |

后续变化按现有harness职责加回归：商店纯逻辑/transport接近`PluginStoreRuntimeTests`；通用生命周期变更才扩`Phase35RuntimeTests`；几何变化才扩响应式harness；拆包按产物检查脚本。真实游戏、Steam、安装恢复和业务数据仍须独立验收。本轮仅文档，未运行构建/游戏测试/PoC，不改变协议、持久化或物品所有权。

## 20. 调查与交付复核

关键调查命令为仓库`rg`/`rg --files`、`git status --short`、`git log --all`按路径和关键词查询、`git log --all --diff-filter=DR --summary`，以及对`fec85a5`、`f50fce9`、`f68dba8`执行`git show`读取旧设计；上文已把历史建议、当前代码和新设计分开。

文档交付仅含本文件和[专题导航](README.md)的对应内容；同日补充R2按需缓存、9GB目标、淘汰/重试与操作预算，统一修正原“暂不引入R2”的结论、失败表和PoC阶段。现有未提交代码、旧文档迁移删除、IDE/生成产物均不恢复、不清理、不提交。实际执行的文档检查（仓库根目录）：

```bash
python3 -
git diff --check
git diff --no-index --check /dev/null docs/branch-local/dev/plugin-store/插件商店GitHub权威源与Cloudflare分发架构.md
git diff --no-index --check /dev/null docs/branch-local/dev/plugin-store/README.md
```

`python3 -` 输入为本轮临时只读检查器：读取上述两份Markdown，核对本地链接目标存在、围栏配对、无行尾空格及末尾换行；全部通过。`--no-index`检查没有诊断，退出码1表示新文件与空文件不同，不是校验失败；两份文件在已有未跟踪专题目录中，所以普通`git diff`不能覆盖它们。全局`git diff --check`没有空白错误，仅显示已有工作区文件的CRLF规范化警告。最终复核`git status --short`及文档内容，没有新增代码或生成物。

Cloudflare部署、大陆测速、客户端/服务端构建、游戏加载/代理、文件安装及恢复均未运行；原因是本轮只有设计文档改动且未授权实施，不把引用官方文档当作本项目实测。
