# Skill 更新提示词：正式发行通知与 Index 并发

请更新“Phinix Rework 插件开发 skill”。先读取并使用 skill-creator，保留现有渐进式披露、小白/开发者引导、开发者侧载、DI、生命周期和兼容约束。本轮只更新 skill 及其必要参考，不修改业务代码、workflow、系统环境或远端，不发布插件，不安装 skill。

## 先核对交付状态

读取工作区最新内容及差异，保留未提交文件和子模块修改。相关仓库为 HunYuan2333/Phinix-Plugin-Index、Phinix-Example-Plugin、Phinix-Legacy-RedPacket、Phinix-Legacy-TalentTrade；客户端仍为 Phinix-Rework。

本轮通知/队列修改在 `/tmp/phinix-release-notify-XRjHSq/` 的四个最新 main 临时克隆中准备，已按用户授权推送 main 并同步三个插件 dev；这个路径只供维护者查阅，不写入可分发 skill。核对凭据是否已配置、真实运行是否通过。未配置时明确写“代码已部署/通知待接入”，不能宣称自动通知已经在线生效。记录依据的提交与检查日期，不用维护者旧分支的 origin 引用推断最新 main。

本轮参考提交：Index main `5b1dbf2`；Example main `fd24674` / dev `78336cf`；红包 main `cadd912` / dev `8bcef15`；人才贸易 main `3693965` / dev `364e3b2`。三个插件的云端构建/正式发行和 Index 的新配置扫描已通过；通知 secret 在本轮检查时尚未配置。继续核对当前状态，不将这些提交当作永久固定版本。

实际并发验证：扫描 `37905776190` 一次准入三个 1.0.4；同时触发的 `37905780510` 因准入改变 main 被拒绝，重试 `37905950068` 又因目录发布改变 main 被拒绝，第三次新事件 `37906197744` 成功。受控发布 `37905904430` 已成功，正式目录快照 `723778ea1de6d911bda2e8018b8df9cf1f336d08` 的摘要和三个 1.0.4 均已核对。这验证了通知脚本和真实队列，不等于已配置插件仓库 secret 的端到端通知验收。

沿用完整编写要求：`docs/branch-local/dev/Plugin-Skill-Authoring-Prompt.md`。本次仅修正以下发布和排障内容，不重新扩张 skill 范围，不把尚待游戏验收的本地基础能力标记为已验收。

## 应写入按需参考的实际方案

1. 三个插件仅在 main push 时正式编译、打包和发行。dev、PR 不触发；此次 CI-only main 提交也会按现有规则发行版本，不能说只有业务变更才发行。
2. 正式 Release 成功后，在独立 `notify-index` job 调用 `ci/notify_index.py`。Index 凭据不交给编译/打包过程，借用的游戏引用仍不进入 ZIP。
3. 通知使用 GitHub `workflow_dispatch` 调用 Index 现有 `plugin-source-updates.yml`，固定 `ref=main`、`check_only=false`。没有新建 repository_dispatch 入口，也不以通知参数直接批准插件。
4. 每次扫描遍历全部获批更新策略，不硬编码三个插件。不获批的新插件仍先走 candidate 和准入；不符合版本/来源/契约策略的更新仍暂停。
5. 维护者可选配置 `INDEX_UPDATE_TOKEN`：fine-grained PAT 仅授权 Index 仓库的 Actions Read and write，令牌持有者须满足已有 Index 维护者校验。这个令牌不需要 Index Contents 写权限，但 Actions 写权限也不是“只许触发一个工作流”的精细授权。不要混用 BUILD_REFERENCES_TOKEN，不读取、打印或索取凭据内容。
6. 不向第三方开发者分发 Index 维护者令牌。第三方通过获批更新策略和定时扫描即可更新，即时通知接入由维护者另行安排；不能要求所有作者都配置这个 secret。
7. API 返回具体扫描 run ID 后，通知端检查目标流程、main、事件，等待该次扫描结束。失败时新开基于最新 main 的扫描，最多三次 dispatch，总等待预算二十分钟；不是重跑旧 SHA。API/凭据错误、超时、重试耗尽只警告，不重新发行、覆盖或撤回已成功发布的插件 Release。
8. 四个 Index 元数据写入流程共享 `index-metadata` 写锁，使用 `queue: max` 且 `cancel-in-progress: false`。这是有上限的队列（GitHub 当前最多 100 个等待任务），不能描述成无限队列或保证永不丢触发。默认只设置 cancel-in-progress:false 仍可能替换 pending 任务，因此不能推荐旧配置。
9. 排队期间 main 可变化，扫描先拒绝过期 GITHUB_SHA；原有来源、证据、提交和发布校验保留。不得通过重写 GITHUB_SHA、强推、删除锁/收据或跳过验证来解决过期问题。人工恢复应新开当前 main 扫描；检查验证结果后可从当前 main 的受控发布入口恢复发布。
10. Index 小时扫描继续作补漏；GitHub schedule 可能延迟或漏触发，不能承诺每小时准点执行。即时通知是加速功能，也不能承诺所有中间 Release 都逐一入库。

## 明确区分成功层次

按顺序解释：插件构建通过 → 正式 Release 已公开 → 通知已接收 → 扫描完成 → 候选准入完成 → 受控目录发布 → 商店看到版本。

扫描 success 不代表某个插件一定准入，通知 job success 也可能只是记录了凭据缺失的警告。必须查看具体扫描报告（changed/errors）、对应准入/受控发布 run 及已发布目录。没有新版本时 changed=false 是正常结果；报告中有单来源拒绝时，应按该来源原因排查，不能把整个流程绿色当作全部插件更新成功。

引用实际 `.github/workflows/release.yml`、`ci/notify_index.py`、`ci/INDEX-NOTIFICATION.md` 和 Index 工作流/ControlledPublication 文档。核对固定宿主与工具版本；不能把本地现代 DI Example 的工作区修改与 main 仍固定旧宿主的 CI 混为一谈。

红包和人才贸易还有 `source-manifest.json` 与 `check-source.py`：修改已登记的 workflow/源码后必须更新对应审核过的文件摘要，新纳入的 CI 文件也应登记。本轮曾因遗漏 workflow 摘要导致两次构建失败，补齐后通过。不要关闭摘要校验或把全部未审查的工作区文件重新签为可信。

## 保持渐进式披露

GitHub App Release 事件发现仅为后续可选增强，当前不实施。依据 [增强计划](plugin-store/Release-Event-Discovery-Enhancement-Plan.md)，明确未来也保留既有 main 发布 Actions、通知 Actions、手动入口和定时扫描；不能要求作者更换发布工具或安装 App，也不能宣称当前已支持 App 接入。本轮只在后续事项中简短说明，不编写未实现的接入教程。

SKILL.md 只需保留“正式发布/Index 排障时读取此参考”的入口和关键区别。令牌配置、API、并发队列、重试及恢复细节放入发布/自动构建参考，不塞进入口或本地开发步骤。面向小白解释 main 提交的发行影响，技术人员再进入具体 workflow 和权限说明。

## 验证

执行 skill 的结构/frontmatter/引用验证；用场景演练核对：

- 三个插件几乎同时发行，等待任务不互相替换，过期扫描使用新事件重试。
- 无 INDEX_UPDATE_TOKEN 的第三方作者仍能走正常准入和定时更新。
- token 已配置但不是 Index 维护者，遵守拒绝结果，不放宽维护者检查。
- Release 已成功但通知失败，不覆盖资产；给出扫描和发布阶段的排查入口。
- scan success、changed=false，或单来源被拒绝，不误报商店已更新。
- main 在排队中发生变化，不把重跑旧事件当作刷新快照。
- 上游代码已准备但未推送/未云端验证，只声明实际完成层次。

演练不是实际云端运行，不为验证触发 Release、提交准入、安装外部内容或改远端。交付修改清单、验证结果、依据版本和待完成事项，保留并行修改，不自动提交推送。
