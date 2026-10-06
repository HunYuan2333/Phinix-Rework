# GitHub 机器人接入与操作

2026-10-05 用户最新决定：一种语言一个 JSON，DLL/语言资源同包；先补宿主通用翻译服务，再贯通商店展示和发布。开发期直接替换旧格式，不做 v2 兼容/缓存迁移/双格式发布。以[语言文件与宿主契约](插件语言文件与宿主应用契约.md)为准，覆盖下文此前的兼容与顺序安排。

2026-10-05 最新排期以[发布格式与实施顺序调整](发布格式与实施顺序调整.md)为准：先冻结多语言/changelog 契约并贯通兼容解析，再做通用 UI 本地化、商店体验；机器人先 A2 与 A4 受控发布，再启用 A3 自动版本监控。此前 UI 优先的安排保留为历史记录。

本地化后续检查补充：允许单语言资源/元数据，不要求英语/中文双语；只检查已提供语言的有效性和回退声明。新格式/API 尚未实现，A1 不宣称已经支持，A2–A4 同步可信校验器，见[专项计划](多语言元数据与插件UI本地化计划.md)。

[English](GitHubBotSetup.md)。2026-10-05，`dev`。承接[审核发布评估](插件商店双渠道索引与GitHub自动审核发布评估.md)，按用户要求用现有 `gh` 完成可落地配置。无需用户先部署服务器或注册 GitHub App。

## 本轮交付 A1

[索引仓库 PR #1](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/1) 已合入默认分支，提交 `544f5f13b246f3fc64f6a8793a9542aa6ea22772`。包含申请 Issue Form、可信 Actions 构建/回归、固定 GitHub 资产静态检查、分离的自动报告任务和双语操作指南。既有官方空索引/source/包目录/审核目录保留，没有新批准记录或 stable 发布，不修改 CF 配置或客户端 staging 来源。

角色是 `github-actions[bot]`，工作流默认只读；检查任务使用 `contents: read` / `issues: read`，报告任务使用 `contents: read` / `issues: write`。现有 Actions 已启用，当前 `gh` 有 repo/workflow 权限，完成本轮无需新 token、App 私钥或网页设置。仓库原本没有分支保护；本轮机器人不写包/批准/目录，A2–A4 实现时再一起配置相应规则与最小写权限，不能把当前状态当作正式发布保护已完成。

完整操作和接续顺序见[本地中文指南](../../../../Extensions/PluginStore/RepositoryAutomation/GitHubBotGuide.zh-CN.md) / [English](../../../../Extensions/PluginStore/RepositoryAutomation/GitHubBotGuide.md)，公开副本位于[索引仓库指南](https://github.com/HunYuan2333/Phinix-Plugin-Index/blob/main/GitHubBotGuide.zh-CN.md)。源码交付目录为 `Extensions/PluginStore/RepositoryAutomation`；其中生产校验器是明确导出的 23 个源码快照，有逐文件来源与摘要，不含游戏引用或二进制。

## 后续流水线

1. **A1 作者申请 → 静态检查与报告**：本轮已上线；不执行作者 DLL、项目或脚本。实际下载并检验仓库/作者/Release/tag/commit/资产身份和摘要，随后做生产 ZIP/PE 校验。申请变动时旧报告不能继续使用。
2. **A2 首次批准 → 条目 PR / 永久审核记录**：尚未实现。机器人整理固定候选，维护者批准具体指纹及后续允许规则；申请、资产或 PR 变动使旧批准失效。不能用普通评论的“approved”或未经身份核验的标签批准。
3. **A3 正常新版本 → 自动检查与目录输入**：尚未实现。定期检查已批准来源，符合规则的正常版本自动处理，不要求你逐版审批；同版本换字节或来源身份/渠道变化暂停，越界重新人工批准，依赖闭包必须可解。
4. **A4 正式目录发布 → gateway / 客户端验收**：尚未实现。串行核验批准输入并生成完整目录，固定 Release 上传/核实后最后更新 stable，故障保留上一指针。明确调用可信发布流程，不依赖 bot 推 tag 自动触发下一 workflow。接官方或隔离来源并实测后才开启无人值守目录发布。

以上 A 编号表示功能划分；最新实施顺序为先冻结 R1/R2 格式与校验，随后 **A2 → A4 受控发布 → A3 自动化**。受控发布先验成功，再接无人值守版本监控，详见[最新排期](发布格式与实施顺序调整.md)。

你现在不需要额外配置。未来需要你做的是首次收录的人为判断、来源/规则变更的复核；代码、CLI 权限配置、工作流与排错可以继续由 Codex 执行。GitHub App / AI 审查后置，不是当前上线申请检查的前置，也不复用 CF 回源 token 写索引。

## 验证与边界

本地独立 .NET 10 校验器构建通过（零错误，现有 NU1900 查询警告）；**10 项**机器人回归通过，包括不可信 URL/身份、重复 JSON 字段、过期报告拒绝、CDN 不带 Authorization、预发行拒绝及生产目录严格校验。真实 Playtest 1.2.1 的公开身份核对/传输/ZIP/PE 校验通过，候选摘要 `311a26c0f1b033fcc6a8726db35e8ba9cb5be779bb5eadc44a1eb3d33ed56af1`；未加载 DLL。

远端分支 CI [37266985444](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37266985444) 和合入主分支 CI [37267063678](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37267063678) 均成功。受控 [测试 Issue #2](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/2) 只用于确认自动报告，不是申请或批准 Playtest 上架；真实正例 [37267110766](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37267110766) 检查/报告均成功；编辑摘要为故意错误后，负例 [37267363387](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37267363387) 正确以 `OriginDigestMismatch` 拒绝，报告任务仍成功。两个候选指纹不同，旧报告不代表新输入。测试 Issue 已关闭，未收录或批准任何包。

执行命令（前三条在仓库根目录，后两条在 `Extensions/PluginStore/RepositoryAutomation`；新输出目录必需）：

```sh
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
dotnet Extensions/PluginStore/RepositoryAutomation/Validator/bin/Release/net10.0/Validator.dll payload phinix.official /tmp/phinix-bot-local-catalog.json /tmp/phinix-managed-playtest-1.2.1.zip phinix.poc.playtest 1.2.1 /tmp/phinix-bot-local-static.json
git diff --check
python3 -m unittest discover -s tests -v
python3 scripts/bot.py check --input examples/managed-submission.json --validator Validator/bin/Release/net10.0/Validator.dll --output /tmp/phinix-bot-live-check-20261005
```

用户操作命令与重试方法在指南。静态报告不证明 DLL 与源码对应、代码安全或全部游戏 CLR 兼容；A1 尚未验正式批准、依赖联合规划、普通版本监控、目录发布并发/恢复或生产 gateway。未重新构建主体/商店或测试游戏，本轮无需玩家更新 Mod。下一批先冻结多语言/changelog 契约并更新兼容解析/可信校验器；A2 接同一新格式，后续按最新排期推进。
