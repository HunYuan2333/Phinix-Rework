# 正式交付与 Index

本地已验证、准备面向用户交付时读；云端流程另读 [actions.md](actions.md)，版本证据见 [basis.md](basis.md)。

## Git 与授权

新项目推荐 main/dev：dev 做开发，main 是维护者确认交付后的正式发行入口。已有项目先核对实际分支、并行修改与流程，不强改历史。固定宿主提交及其 Common/protobuf gitlink。先准备可审阅 ZIP、摘要、验证记录、发布说明与候选元数据，再按已有用户授权执行推送、Release、Issue/PR；没有授权停在具体产物。不能把 main 当临时编译触发器。

## 两条路线

| 路线 | 顺序与边界 |
| --- | --- |
| 托管 DLL | 规范 ZIP/预检与游戏验证 → 正式非草稿/非预发布 GitHub Release → Index candidate/准入审核 → 更新策略与来源更新 → 索引受控发布 → 商店可见 |
| RimWorld Mod | 完整 Mod 游戏验证 → Steam 工坊发布 → 工坊条目申请与准入/索引发布；游戏/Steam 管理文件，不交整 Mod ZIP 给商店安装 |

Index 版本化材料：README.zh-CN.md、ControlledPublication.zh-CN.md、SourceUpdates.zh-CN.md、`examples/managed-submission.json` 与 `examples/workshop-submission.json`、实际 Issue forms/workflows。按目标 Index 版本获取，不从旧工作区推断最新政策。先核对模板是否存在，字段以其 schema 为准。

DLL 候选包含公开源码身份、正式 Release/tag/提交、资产 ID/名称/大小/SHA-256、与 ZIP 一致的 manifest、多语言介绍/更新日志。禁止捆绑游戏/Unity/宿主/Harmony DLL、凭据和个人路径。静态检查不证明 DLL 安全或源码与字节必然对应。工坊候选绑定固定 Workshop/Mod 身份与元数据，只有元数据准入，不做 DLL ZIP/PE 校验，不生成 DLL 更新策略。

首次新插件需申请并由 maintainer/admin 审阅批准标签 `plugin-approved`；作者不自己越权加批准标签。可信流程生成准确候选证据 PR、准入，再重新验证资产/来源与依赖闭包，发布不可变目录并原子更新 stable。Release 存在不等于准入通过、自动加入 Index 或马上商店可见。

## 已批准来源更新

只有获批 update-policy 的 DLL 包才可自动来源更新；不是固定检查三个插件。策略可能限制 stable 三段版本、same-major、资产前缀、作者/仓库/程序集/模块/依赖/外部 Mod 身份及 tag 后继。身份/主版本等越界、缺资产、字节变更、静态失败、上一版本等待发布时停下，走人工申请/策略审阅。

2026-10-09 已核对新部署配置：`plugin-source-updates.yml` 每小时计划扫描（当前 `17 * * * *`）并遍历所有获批策略，合格更新批量准入；不是硬编码三个插件。执行前仍核对目标版本 workflow/source_updates.py 的排序、上限与参数。手动 `check_only=true` 只发现/验证，false 才进入准入；发行后通知也调用此入口，不授予新的批准。新插件仍先 candidate/人工准入，不符版本、来源或契约策略则暂停。

小时扫描继续补漏，但 GitHub schedule 可能延迟或漏触发，不保证每小时准点；可选即时通知只是加速。不能承诺所有中间 Release 逐一入库或发布后立刻上架。通知参数、可选维护者凭据与等待规则见 [actions.md](actions.md)，第三方无该凭据仍可使用正常准入和定时更新。

失败时读对应运行与 `Update blocked: <packageId>` Issue/申请反馈。旧版本永不覆盖；修正元数据不能使已批准字节可被替换。策略/标签证据失效要按实际新事件/新运行处理，不能跳过审核、编辑锁或直接改 stable。Index 当前流程要求首次尝试证据时，来源更新失败后发起新的运行而非重跑旧 attempt；这与插件 main 同提交重试不同。

索引目录更新只提示玩家，确认下载并重启才加载；不是自动修改已安装 DLL。GitHub 与 CF 缓存入口应按正式 stable 及不可变证据排查，网络失败不伪造结果。仍待审核/发布时报告在哪一步及下一项责任，不宣称上架。

## 分层判断成功

依次核对：插件构建通过 → 正式 Release 已公开 → 通知已接收 → 扫描完成 → 候选准入完成 → 受控目录发布 → 商店看到版本。通知是可选加速层，第三方可直接等待定时扫描，不能把它列为必备步骤。

| 看到的信号 | 还需核对 |
| --- | --- |
| release job/build success | 正式 Release 非草稿/非预发布、目标提交与准确 ZIP/摘要 |
| notify-index job success | 是否只有缺 secret 的 warning；是否返回具体扫描 run ID，流程/main/事件是否匹配 |
| 扫描 success | 实际该 run 的 `updates.scan_complete` changed/errors 与 source-updates 工件 `updates/report.json`；不要把测试 fixture 的 runId=123 输出当实际扫描 |
| changed=false | 没有可准入的新版本是正常结果，不代表刚发布的某个版本已在商店；继续看 errors/来源策略/现有目录 |
| 单来源拒绝，其他来源成功 | 按对应 packageId/reason 排查；整体绿色不能证明所有来源更新成功 |
| changed=true / propose 成功 | 对应候选/批量准入证据、准确提交/审批记录，再看受控发布 run |
| 受控发布 success | 正式 stable 指向的 snapshotId、catalog 摘要/大小及目标包版本，最后核对客户端来源刷新/商店显示 |

## Index 元数据并发与恢复

`plugin-admission.yml`、`plugin-label-admission.yml`、`plugin-source-updates.yml`、`plugin-publish.yml` 四个写入流程共用 `index-metadata` 写锁，配置 `queue: max`、`cancel-in-progress: false`。这保护多次准入/发布的等待任务不按旧 pending 规则互相替换；不是无限队列或零丢触发承诺。GitHub 当前最多允许该组 100 个等待任务，超出不能保证保留；仅 cancel-in-progress:false 而未 queue:max 的旧配置仍会替换 pending，不推荐沿用。[并发规则](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)、[队列上限](https://docs.github.com/en/actions/reference/limits)。

排队期间其他准入/目录发布可能推进 main，扫描启动时先比较最新 main 与原 GITHUB_SHA，过期会报 `TrustedHeadChanged`。保留来源、证据、提交与不可变发布校验；不能改写 GITHUB_SHA、强推、删除锁/收据或跳过验证。新事件基于当前 main 才是新快照，rerun 旧事件不是刷新。

人工恢复按已有授权先看失败原因和当前状态，再在当前 main 新开 `plugin-source-updates.yml` 扫描（需要准入时 check_only=false）；这不是重跑旧 attempt。准入已完成但目录未发布时，核对证据/验证结果后，使用当前 main 的 `plugin-publish.yml` 受控恢复入口：先 check_only=true，检查通过后才按授权 check_only=false。不改变旧资产/锁，不把来源拒绝当队列故障绕过。具体实时状态与已验证并发例见 [basis.md](basis.md)。
