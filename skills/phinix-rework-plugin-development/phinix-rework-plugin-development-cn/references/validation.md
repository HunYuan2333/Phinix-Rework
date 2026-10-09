# Skill 验证记录

维护本 skill 或评估引导场景时读。2026-10-09。以下是草案的静态检查和逐场景人工走读，不是实际用户试验、独立 agent 测试、RimWorld 执行或云端发布记录。

## 首轮静态检查

使用 skill-creator `scripts/quick_validate.py`：frontmatter/name/description 与未完成 scaffold 检查通过。补充检查覆盖目录内相对链接、公开仓库固定 SHA 链接在本地对象中的路径存在性、UTF-8/Markdown 围栏、JSON 示例解析、POSIX 命令语法、参数与已查阅源码的一致性。示例路径/身份明确为待替换输入，不含维护者绝对目录、TODO scaffold 或未随包分发的脚本调用。

实际补充检查结果：20 个相对链接、3 个固定 SHA 源码路径在本地对象中存在、1 个 JSON 围栏及 3 个 POSIX 命令围栏通过；远端链接可达性未证实。检查前保存的 4310 个原有文件 SHA-256 均保持一致，各仓 HEAD、分支与原有 status 条目未变，新增项仅本 skill 的 11 个文件；客户端 git diff --check 通过。

首轮目录包含 SKILL.md 与 10 份参考；本轮补充 engineering.md，当前共 12 个 Markdown 文件。无脚本/占位资产/安装动作；无需为本任务添加脚本。官方 SDK/游戏引用由用户环境提供，缺失时有停止点，不宣称本 skill 自带工具。

## 场景演练

下面逐条从请求沿 SKILL 路由到对应操作，核对源码/交付记录与停止点。结果“通过”指引导走向符合已核对约束，不表示执行了构建/安装或游戏验证。

| 模拟请求/状态 | 按需读取与走向 | 停止点 / 结果 |
| --- | --- | --- |
| 小白：做一个计数 Tab，本地试用 | environment → composition/UI → local-loop → verification；确认版本，独立 Assembly/package/module/settings 身份，改 Example，编译 ZIP、预检、开发侧载、重启和摘要验证 | 无 GitHub 要求；缺新宿主快照停在定位，实机验收由用户环境执行。通过 |
| 技术人员：从库存发奖励或预留转移 | composition + capabilities 库存 + verification；区分 module/package ID，检查激活与能力， scoped 注册清理，DepositId 幂等，权威回执/未知结果托管 | 缺 codec/依赖/可写状态拒绝；无真实回执不提交，游戏交付仍待实测。通过 |
| 技术人员：添加聊天/命令能力 | capabilities 消息 + composition + verification；真实 TryHandleOutgoing 管线和 Common handler；出站命令由被发现模块实现 | 不生成不存在的 AddClientOutgoingCommandHandler，不直接底层发送；服务端仅需求确需时读。通过 |
| 复杂 Def/资源/原生生命周期与 Harmony 功能 | routes → environment/Phinix 对应 API；说明 Mod/托管各自文件管理和限制，用户选路线 | 未授权停在外部 skill 下载/启用前，仍可设计 Phinix 接入；不强制按复杂度选 Mod。通过 |
| 缺 Python | environment → local-loop；独立 dotnet build 与工具 CLI 替代 pack.py | 仍缺 SDK/引用时停在编译准备；不擅自安装。通过 |
| 缺 Git/网络 | environment + basis；只能复用已验证固定快照/引用和已有完整缓存 | 缺固定契约不猜 API，首次恢复可能联网，不虚构离线下载器。通过 |
| 缺 gh/登录 | environment；本地步骤继续；正式阶段可指导认证或获授权浏览器 | 不阻塞本地构建，不读取 token。通过 |
| 缺游戏/Unity/mscorlib/Harmony 引用 | environment；从合法安装/引用准备，仅 Harmony 需求才查 Harmony；net472 本地缓存或显式 framework 路径 | 缺必要引用停在编译前，不将 DLL 放 ZIP/公开资产。通过 |
| 宿主过旧、不支持 Compose/侧载 | environment + basis；检查真实公开 DLL/工具/UI，取得已验证新版/固定快照或单独按旧版本约束评估 | 不把 abstractions 版本等同全部新能力，不伪装 Mod 绕过；未知支持停止新闭环。通过 |
| 同版本不同摘要修订 / 重复包 / 降级 | local-loop；本地旧包启用且可信才修订，新摘要需重启；重复与降级拒绝 | 不改收据或覆盖 ZIP；旧/新当前摘要区分。通过 |
| 正式包同 ID 冲突 | local-loop；独立开发 ID，或正常卸载与重启完成移除再换来源 | 不冒充正式审批、不直接覆盖。通过 |
| 确认前撤销 DevMode / 取消 | local-loop + verification；确认/执行重新核对模式，未提交释放输入；已提交恢复按决定 | 已安装包可管理，无代码热替换；实际 UI 测试待完成。通过 |
| 已安装摘要新、当前加载摘要旧 | local-loop；复制摘要确认来源/发现/激活，解释本会话旧 DLL，重启验证 | 不声称新代码已加载，不要求完整私人日志。通过 |
| 两处启停/卸载、取消卸载 | local-loop + verification；核对商店/管理同步与下一启动状态 | 取消卸载仍停用，不说自动恢复；schema 1 降级前新宿主卸载重启。通过 |
| 正式 DLL 与工坊两条交付 | publication；DLL Release→candidate→批准→索引；Mod 工坊→工坊元数据索引 | 未授权停在具体准备产物；不存在整 Mod ZIP 商店安装。通过 |
| main 旧工具、本地新工具 | actions + basis；旧 pin 403cea... 与新 Common 脏快照分开，先 Common 再客户端 gitlink，另批更新 CI | 本轮不改 workflow、不触发构建，不宣称已云端验证。通过 |
| main 构建失败、同提交重试/已公开资产 | actions；查准确 SHA 草稿，复用保留版本与相同上传字节，已公开跳过 | 资产不同停下调查、不 clobber；新代码提交须正式交付确认。通过 |
| Release 已有但 Index 不可见 | publication + actions；区分首次审核、获批 policy、批量更新等待/失败、controlled publish 与 stable | 不承诺每个中间 Release/立即上架，不跳审核或改锁；Index 新运行与 main rerun 区分。通过 |

## 保留的验收与待办

基础交付文档记载 ManagedExtensionRuntimeTests 3319、Example 45 条断言、打包 CLI 16 项及布局 2 项，宿主/Store/工具编译通过。这些为**原交付记录**，本轮未重跑，未冒充 skill 验证结果。红包 1.0.2 有用户确认的本地游戏验收，保留其限定范围。

新开发闭环的游戏重启/修订真实加载、五轮切档/断线/退出释放、模式撤销、窄窗口、管理同步及中断恢复仍需按交付记录完成；未声明正式发行游戏验收。另批交付 Common、准确更新客户端 gitlink，再更新/验证 CI 固定引用。本轮网络不可用，最新 main/Release/Index 状态未知，维护者提供的 Index 按小时批量更新单独标注依据。

本轮仅新增 skill 目录，未安装外部 skill 或本 skill、未改业务/Actions、未提交/推送、未发布 Release/Index。结束前检查原有文件内容与 Git 状态；并行任务若发生新增修改，保留并记录，不回滚以追求初始快照一致。

## 轻量工程组织补充验证

2026-10-09，参考仓库已按本轮授权克隆查阅，未安装/启用。沿用 skill-creator 检查通过；补充检查当前 12 个 Markdown、28 个相对链接、5 个固定 SHA 源码路径、JSON 与命令围栏，均通过。检查快照中的 695 个 skill 目录外原有文件内容不变；修改限于 SKILL/routes/local-loop/basis/validation 并新增 engineering。参考仓 clone 工作区干净。此处只验证 skill，未构建新 DLL、安装包或执行游戏场景。

重新评估后保留阶段路由和现有 DI/能力/本地迭代参考，将轻量工程组织放入自带 engineering.md，避免引入另一套游戏框架或为简单 DLL 强制加载外部 skill。以下补充场景逐项走读通过（同样不是实机执行）：

| 请求 | 走向与结果 |
| --- | --- |
| 小白只想做计数 Tab，不想装其他 skill | 入口托管默认路径 → engineering 单项目/少量类 → composition 与 local-loop；共享 State/settings 权威来源，外部 skill 不加载，不强制额外文档/Contracts/测试工程 |
| 已有 Tab，要增加设置面板 | engineering 职责划分 → composition；两 provider 共享已有 State，不各存一份计数，不仅为 IState 建接口 |
| 要把 ExampleExtension.cs 拆成几个文件 | engineering/local-loop 明确 EnableDefaultCompileItems=false；同步 Compile Include；如改 csproj 文件名同步 pack.py 定位，其他程序集/ID 仍按独立身份规则核对 |
| 一个 DLL 增加消息或库存功能 | 按功能分组、窄适配器与公开契约，不改宿主；恢复同时进切片，核心确认不变量不因“简单 DLL”而降低；纯规则测试与游戏边界验收分别选择 |
| UI 列表越来越慢 | 先看真实 Draw 热路径与数据量；少量数据不预建缓存，有缓存则明确数据/筛选/语言/世界失效，不机械禁 LINQ，不让每帧 I/O 或后台访问游戏对象 |
| 功能转向 Def/资源与大量原生 Hook | 回 routes 评估 Mod，联合外部工程 skill；已有下载查阅授权不自动扩大到安装/启用，缺相应授权停在该动作前 |

本轮吸收的是工程决策与组织方式，经 Phinix Example 实际布局调整；未复制外部全部手册或新增脚本。原有游戏验收及 CI 引用更新待办保持，不用参考仓成功 clone 推断 Phinix 远端发行状态。

## 双语分发结构与翻译验证

2026-10-09，按用户要求保留外层 `phinix-rework-plugin-development/` 分发目录，内部是两个独立 skill：`phinix-rework-plugin-development-cn/` 与 `phinix-rework-plugin-development-en/`。外层不再保留重复 SKILL.md；每套均为 SKILL.md + 11 份同名 references，frontmatter name 与对应目录一致。

中文原有正文完整保留，只调整 skill name 并补充本节；英文完整翻译所有文件，而非仅入口或摘要。两套逐文件检查：相同链接目标/顺序、提交与文件哈希、CLI 选项，以及完全相同的 JSON 和可执行 shell 示例；组织树仅翻译注释。人工核对路线、DI/所有权、工程组织、本地侧载、恢复、正式发布和历史验证边界，未扩大验收或权限。

两套 quick_validate 均通过；共 24 个 Markdown、56 个相对引用、10 个固定 SHA 源码路径、2 个 JSON 和 6 个命令围栏检查通过。外层仅含两个语言目录，没有占位脚本、安装副本或新工具。695 个 skill 目录外基线文件内容不变。未执行安装、业务/Actions 修改、提交/推送、游戏测试或发布；翻译验证不改变已有游戏验收/CI 待办。

## 正式发行通知与 Index 并发更新验证

2026-10-09，约 16:45（Asia/Singapore）。此次仅修改每套 SKILL.md、actions.md、publication.md、basis.md、validation.md；保留其他需求/工程/DI/本地侧载/兼容参考。skill-creator 结构/frontmatter 与引用、双语数据一致性检查用于草案，不触发云端运行或发行。

历史真实运行通过只读 GitHub API/日志复核，参见 basis.md 的具体 SHA/run/目录摘要。日志内测试 fixture runId=123 与真实扫描 runId 分开核对，避免将测试输出当实扫结论。三个通知 secret 仍未配置，日志是缺凭据警告，结论为“代码已部署/通知待接入”；真实队列/脚本证据不等于插件 secret 接入后的端到端验收。

本次新增人工场景走读（不是执行结果）：

| 场景 | 读取与正确走向 / 停止点 |
| --- | --- |
| 三个插件几乎同时发行 | actions → publication：各通知独立，Index 四个写流程同组 queue:max/cancel-in-progress:false；有上限，不能承诺零丢触发；过期扫描发新事件，最多三次/二十分钟后警告，小时扫描补漏 |
| 第三方无 INDEX_UPDATE_TOKEN | actions 凭据范围 → publication：首次 candidate/准入和获批策略定时更新照常；不索取/分发维护者 token，缺 secret 不阻塞第三方更新 |
| token 已配置但持有人非 Index maintainer | actions/publication：能 dispatch 也不代表可准入；保留拒绝原因与现有维护者检查，不放宽授权，失败通知不撤回 Release |
| Release 成功、通知/API/等待失败 | actions：保留准确 Release/资产，警告后看具体 scan URL、报告及 controlled publication；不重新发行/覆盖；无授权时停在可审阅诊断 |
| scan success、changed=false 或单来源拒绝 | publication 分层：实际 run/report changed/errors，正常无新版本不误报上架；单来源按 packageId/reason 定向排查，绿色不证明所有源成功；准入/发布/商店逐层核对 |
| main 在排队中变化 | publication：TrustedHeadChanged 属预期保护；fresh workflow_dispatch ref main 获取新快照，不 rerun 旧 SHA、不改 GITHUB_SHA/强推/删锁；已准入未发布时先当前 main check_only 验证再授权恢复 |
| 上游只准备未推送或未云端验证 | basis/actions：只报源码准备或对应已完成层次；不能复制本批成功结论，继续核对实际 SHA/运行/secret；本地现代 DI 不与旧 CI pin 混淆 |
| CI-only main 修改及旧摘要失败 | actions：main push 仍分配发行版本；只登记已审阅变更/新增 CI 文件摘要，不关闭 source 检查、不把全工作区重新签可信 |

保持原有基础功能游戏验收、Common/gitlink 交付与 CI pin 更新待办。维护者通知 secret 配置与端到端接入验收另行安排，第三方作者不承担该配置。本轮没有安装 skill、改业务/manifest/workflow/系统、提交/推送、dispatch、准入或发布；只读现有运行不算新增验收执行。

最终检查结果：两套 quick_validate 通过；24 个 Markdown、66 个相对引用、24 个固定 SHA 源码路径、4 个 JSON 与 6 个 shell 围栏均通过。两套逐文件链接目标、完整提交/文件哈希、CLI 选项及可执行示例一致。没有新脚本；只改 10 个已有 skill 文件，检查基线其余 1079 个原有文件内容一致，各仓 HEAD/分支/status 未变（包括 Common 与四个交付克隆）。原有未跟踪文件均保留。
