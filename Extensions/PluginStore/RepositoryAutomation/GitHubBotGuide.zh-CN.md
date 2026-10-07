# GitHub 插件申请与发布

[English](GitHubBotGuide.md)。作者在自己的公开仓库发布源码及固定 DLL ZIP，再向正式索引提交元数据。GitHub Actions 完成检查和发布，无需作者提供 token，也无需独立机器人服务器。

## 工坊条目收录

进入 Issues → New issue → **Workshop listing / 工坊收录**，复制 [workshop-submission.json](examples/workshop-submission.json)。填写 `channel: steam-workshop`、`management: rimworld-mod`、工坊 ID、Mod packageId、名称、简介、作者、许可证说明、标签和支持的 RimWorld 版本。示例指向 Phinix Rework 本体，**仅供工坊收录流程测试**；申请自己的 Mod 时应替换这些字段。

无需 GitHub 仓库、Release、ZIP、DLL manifest 或包内语言文件。机器人只检查收录元数据，不检查工坊 Mod 的代码，也不认证后续 Steam 更新。维护者加 `plugin-approved` 后，沿用证据 PR、自动发布、异常标签和成功关闭申请的流程。订阅、下载、启用、前置依赖和内容更新由 Steam / RimWorld 管理；GitHub 和 CF 只分发相同的目录元数据。

修改简介等收录信息仍需新申请和人工批准。玩家目录展示最新批准的元数据修订，历史候选、报告和发布锁保留且不可变。同一条目 ID 不允许悄悄换成另一个工坊 ID 或 Mod packageId；GitHub DLL 的来源版本监控不适用于工坊条目。

本地仅元数据校验：

```sh
python3 scripts/bot.py check --input examples/workshop-submission.json --validator Validator/bin/Release/net10.0/Validator.dll --output workshop-check
python3 scripts/catalog.py project --candidate examples/workshop-submission.json --validator Validator/bin/Release/net10.0/Validator.dll --output workshop-record.json
```

## 提交插件

进入 Issues → New issue → Plugin submission。复制[当前候选示例](examples/managed-submission.json)，把所有身份、源码提交、版本、资产 ID、长度和摘要替换成自己的正式发布。schema-v3 目录支持多语言名称、简介和 changelog；插件 UI 的语言 JSON 放在 ZIP 内，并受文件摘要校验保护。

完整实例是 [Phinix 示例插件](https://github.com/HunYuan2333/Phinix-Example-Plugin)，见[源码及 v1.0.0 发布](https://github.com/HunYuan2333/Phinix-Example-Plugin/releases/tag/v1.0.0)。正规准入过程可查看 [Issue #15](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/15)、[证据 PR #16](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/16) 和[成功发布记录](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37335979507)。

申请新建、编辑或重新打开时会收到静态报告。检查覆盖严格元数据、公开仓库/作者身份、固定 tag/源码 commit、正式发布及资产身份、大小、SHA-256、ZIP 布局、PE 引用、manifest 与资源声明。不会加载或执行作者 DLL。格式错误按照报告修改申请；候选变化会使先前批准失效。

## 审核与发布

1. 审阅具体候选和报告；静态通过不能证明源码与 DLL 对应关系或游戏内行为。
2. 添加 `plugin-approved` 批准该候选，只有有权限的维护者可以批准。拒绝尚未批准的申请，直接关闭 Issue 即可。
3. 可信机器人重新检查、合入只含元数据的证据 PR 并发布目录，不需要第二次人工审批。
4. 发布成功移除 `plugin-error` 并自动关闭申请；失败保留申请，添加 `plugin-error` 和排查提示。详见[发布操作与恢复](ControlledPublication.zh-CN.md)。

正式玩家来源是 `phinix.official`，GitHub 直连和 CF 加速使用同一协议、身份和不可变原件。Playtest 不进入玩家目录，其历史批准记录和回归输入仅作审计保留。后续稳定版本已在明确批准的来源策略内自动监控；超出策略的变更仍需新的固定候选和维护者审核，见[来源自动更新](SourceUpdates.zh-CN.md)。

## 重试与本地验证

```sh
# 只自检可信工具。
gh workflow run plugin-intake.yml --repo HunYuan2333/Phinix-Plugin-Index -f issue_number=0
# 重新检查并回报已有申请；这个命令本身不会批准候选。
gh workflow run plugin-intake.yml --repo HunYuan2333/Phinix-Plugin-Index -f issue_number=123
gh run list --repo HunYuan2333/Phinix-Plugin-Index --limit 5
gh run view RUN_ID --repo HunYuan2333/Phinix-Plugin-Index --log-failed

dotnet build Validator/Validator.csproj --configuration Release
python3 -m unittest discover -s tests -v
python3 scripts/bot.py check --input examples/managed-submission.json --validator Validator/bin/Release/net10.0/Validator.dll --output /tmp/phinix-bot-example-check
```

最后一条会访问 GitHub 并验证真实发布，使用未占用的输出目录。工作流报告保留 14 天；批准记录、摘要和不可变目录快照永久保留在仓库及 Releases。

## 维护边界

工作流默认只读，仅报告、准入及发布任务申请必需的写权限。固定可信代码和 Actions 版本，显式编排发布，不带发布凭据运行作者构建脚本。Cloudflare 使用单独的只读回源 token。索引不接收凭据、私人数据、游戏参考 DLL 或生成二进制。

`Validator/Production` 是明确维护的生产校验器源码快照，来源及摘要记录在 `production-provenance.json`；更新后验证回归和真实原件。旧 Playtest 回归输入位于 `tests/fixtures`，与当前作者示例分开。保留已接受版本锁和审计快照，不覆盖已发布原件。GitHub App 和 AI 审查等有实际需要后再考虑。
