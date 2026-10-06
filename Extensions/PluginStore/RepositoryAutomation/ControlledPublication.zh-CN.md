# 准入与受控发布

上线前清理：正式 index 不展示 Playtest。`catalog-exclusions.json` 是维护者在 main 管理的下架清单，仅影响新目录的可见条目，不改写已批准版本、原 ZIP、审批或发布锁。发布仍复核全部批准记录与原件，再验证可见目录的完整依赖闭包；允许空目录。不得用该清单批准未发布版本。测试插件仅保留在独立 Playtest 仓库，用户客户端只提供正式目录的 GitHub/CF 访问切换。历史快照用于追溯，不应删除。

[English](ControlledPublication.md)。2026-10-05。正常流程改为维护者加一次批准标签，后续 A2/A4 自动完成；A3 版本追踪仍未启用。

## 维护者操作

1. 审阅申请、作者源码及 Plugin intake 报告。静态通过不能证明运行安全或源码与 DLL 对应关系。
2. 用仓库 **admin 或 maintainer** 身份，给已审阅的开放申请 Issue 加 **`plugin-approved`** 标签。这是日常唯一的人工批准操作。
3. 可信 **Plugin label admission** 将事件正文绑定到规范化候选指纹及具体标签事件/审核者数字 ID，重新核验公开上游、ZIP、PE、本地化，不执行作者代码；自动创建证据 PR，并指定准确 head SHA 自动合入。
4. 准入成功后，**Plugin controlled publication** 自动复核批准来源、PR 内容、资产字节和完整包/模块依赖闭包，发布不可变目录并原子更新 stable。Issue 评论提供两次运行链接；发布成功后核验已加锁凭据/当前目录及未变化正文，再自动关闭申请并清除 `plugin-error`。准入或发布失败加 `plugin-error`，保持开放。无需再次审批、合 PR 或手动启动发布。

人工测试命令：

```sh
gh issue edit ISSUE --repo HunYuan2333/Phinix-Plugin-Index --add-label plugin-approved
gh run list --repo HunYuan2333/Phinix-Plugin-Index --workflow plugin-label-admission.yml --limit 5
gh run list --repo HunYuan2333/Phinix-Plugin-Index --workflow plugin-publish.yml --limit 5
```

发布前正文变化、Issue 关闭、移除或重新添加标签，会使待发布批准失效。修正申请后移除再添加标签授权新运行；**重新运行旧准入尝试会被拒绝**。普通 write 协作者或机器人加标签、任意评论/标签、过期事件不能批准。发布后版本和审核锁不可变，历史申请修改/撤标签不会撤销已发布版本。

静态检查失败会在未变化的申请下 @发布者，给出错误代码、格式修正说明、示例和运行日志，并加 `plugin-error`。修正申请会触发重新检查；异常标签直到最终成功发布才清除。回报/关闭异常也会尽力标记并留日志，不改变已提交的发布结果。

## 证据与权限

新版本只新增四个元数据文件：`packages/包ID哈希/候选哈希.json`、`reviews/...`、`policies/...` 和 `label-approvals/运行ID.json`。审核记录绑定源、规范化候选与 Issue 正文哈希、审核者/数字 ID、标签名称/事件 ID/时间、可信工作流/运行/提交/尝试、静态报告及策略哈希。策略固定仓库/作者 ID、渠道、管理方式、程序集/模块身份和依赖 ID；**manual-only** 表示每个版本仍需批准，批准后自动完成其余步骤。A3 自动版本准入需要另行实现。

完全相同的已发布候选只新增一份标签批准凭据，重新检查同一包，发布只有一个条目的新快照；保留原审核记录、版本锁和 DLL 资产。相同包 ID/版本的不同候选直接拒绝。发布时加入不可变 `approval-locks/运行ID.json`，绑定凭据哈希。这样可以用 Playtest 1.3.0 测试新标签流程而不覆盖已接受版本。

默认工作流权限保持只读。检查任务只读；准入任务仅为证据 PR 和自动合入申请 contents/PR 写权限；发布任务为目录/锁/stable 申请 contents 写权限；回报任务申请 issues 写权限。流程不提交批准 PR review。GitHub 合并提供的创建/批准设置已启用以允许创建 PR；无需新 PAT、App 或服务器。CF 使用独立的只读回源 token。[GitHub 设置说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository)。

发布器用 `workflow_run` 衔接已成功完成的可信标签准入，重新核验真实运行身份，并要求其专属凭据已在输入树中。只检出固定的默认 main 提交，不检出作者代码或消费上游工件。[GitHub workflow_run](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run)。

## 原子发布与恢复

准入与发布共用 `index-metadata` 串行队列，不取消运行。自动合 PR 前检查当前 main 和最新批准。发布器检查完整 v3 闭包、创建固定草稿 `catalog-v3-快照` Release、无覆盖上传、下载核验字节/哈希/大小和源码身份后公开 Release。最终一个禁止强制的 Git 更新同时提交不可变 published、版本/审核锁与 `stable.json`。并发修改会阻止旧输入写入；未引用的已上传快照不会替换玩家入口。

发布器复用匹配的部分上传。若 stable 已提交而成功响应丢失，重试核验准确的直接子发布提交及资产，输出 `publication.already_complete`，不写入。评论回报失败不会把已提交操作改判失败。准入失败可能留下未合入的证据 PR，重试前检查对应运行。已合入但未发布的凭据若正文/标签变化会阻止发布，需处理该准确的待发布批准；不要绕过拒绝或修改已接受的锁。

保留手动故障恢复入口，但它不是日常审批步骤：

```sh
gh workflow run plugin-publish.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f check_only=true
# 上一步通过后：
gh workflow run plugin-publish.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f check_only=false
```

旧准确指纹的 `Plugin admission` 手动入口保留用于已接受人工路径的恢复；其记录仍要求真人合入准确的三个文件 PR。标签记录只允许固定的 GitHub Actions 机器人创建和合入证据 PR，不把任意机器人合入视为批准。

试运行限制：八个版本记录、八份标签凭据、400 条 Issue 历史事件、总 ZIP 512 MiB、发布器 512 次 API 调用及 25 分钟 API 期限，另有每文件/包限制；扩容单独实施。作者 DLL 仅静态检查，不加载执行；客户端继续检查宿主/游戏版本和实际 CLR 兼容。正式源是 `phinix.official`，游戏默认仍为 `phinix.managed`。

## 验证

```sh
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
```

索引仓库对应路径为 `Validator/Validator.csproj` 和 `tests`。52 项回归覆盖准确人工/标签批准、操作者/事件身份、正文变化/撤标/重新加标、自动合入范围/head、成功运行与机器人证明、不可变版本/凭据锁、workflow_run 来源、上传后复核、回报和原子发布恢复。远端真人加标签是独立验收步骤；控制台/Actions 检查不代表游戏验证。
