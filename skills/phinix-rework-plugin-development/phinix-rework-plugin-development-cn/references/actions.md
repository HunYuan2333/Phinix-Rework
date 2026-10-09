# main 自动发行与故障排查

使用/配置 CI 或 main 失败时读。只需要本地试用时不加载此文件。

三个插件已有完整 main 自动编译、打包与正式 GitHub Release 流程；本轮已只读复核部署后的 main、成功的云端运行与正式 Release，准确版本见 [basis.md](basis.md)；没有为 skill 验证新触发运行。不能说“完整 Actions 尚未实施”。当前流程仅 main push 触发，dev/PR 不发布，无 workflow_dispatch 手动编译入口。

准确文件：`.github/workflows/release.yml`、`ci/config.json`、`ci/release.py`、`ci/build.py`、`ci/publish.py`、pack.py、`ci/notify_index.py` 与 `ci/INDEX-NOTIFICATION.md`；红包/人才贸易还有 `source-manifest.json`、`check-source.py`。Example/Talent 工作区缺一些 CI 文件，而已保存的 main 快照有，不能据当前分支缺文件判流程不存在。

## 如何使用与改编

先核对 repository/kind/baseVersion/assetPrefix、程序集/包/模块身份、引用路径/摘要、hostCommit、workflow 中实际 checkout ref 及权限一致。不要照抄维护者私有编译仓与 token，第三方采用自己的合法引用供应。修改/实施 Actions 需要对应任务授权；本 skill 不默认新增 workflow。

每个 main push 提交表达正式发行意图，CI-only 或文档/workflow 修改也会按现有规则分配正式版本，不是只有业务变更才发行。给小白说明：推送 main 会发布新版本，本地试验在 dev 和侧载闭环完成。先 reserve 一次提交的不可变版本/草稿，然后构建固定宿主和工具、仅打包插件，检查文件布局与摘要，保存构建工件，再发布正式 Release。当前脚本对同 major/minor 的历史 tag 分配后续 patch，同时复用同 source SHA 的已保留版本；不是简单使用 csproj 版本或每次重跑递增。

成功产物为 ZIP、`SHA256SUMS`、`build-summary.json`；build summary 记录插件/sourceCommit、hostCommit、commonCommit、版本、资产大小和摘要，Release 说明也记录源码/宿主。失败前的 Actions artifact 只能排障，不能当正式 Release 或准入证明。产物排除游戏/Unity/宿主/Harmony DLL。

## 当前固定引用差异

审阅的 Example main 快照固定宿主 `403cea6c207a630fae391c6dc29bbce5a5a17b3b`，三个快照配置亦如此。这是历史状态，不是未来用户永久必须使用的 pin。当前工作区新 Compose Example 与 `--config`/`--validate` 没有据此在 main 云端验证。交付这批能力应先提交/交付 Common 实现，再更新客户端准确 gitlink，游戏验收后另批更新对应 CI hostCommit 与 checkout ref、验证云端。不能混用新 pack.py 与旧 packager 或声称合并后必然构建成功。

## 失败的处理顺序

1. 只读查看对应 main source SHA 的 Actions 日志、reserve 草稿/tag 和工件，区分引用获取、宿主/工具、插件编译、ZIP 校验、上传/公开阶段。发布阶段才需要 gh 认证；不收集 token。
2. main 编译失败可能已留未公开草稿。按准确 SHA 的原运行重试，reserve 复用相同版本；无手动编译入口，不能虚构 workflow_dispatch。需源码修正则进入 dev 并经过交付确认，新 main 提交分配新版本。
3. 同提交已发布时流程跳过后续发布；部分上传按文件名及字节哈希复核，相同才复用，缺文件才上传。不同字节立即停下调查，不用 `--clobber`、删除 tag/Release 或覆盖旧资产。
4. 正式 Release 已有但商店不可见，转 [publication.md](publication.md) 查申请、update-policy、来源更新、受控索引发布与 stable；不反复发布或绕过批准。调度延迟/网络失败如实说明。

重试也遵守用户已有授权，修改源码/发布权限不能从读取日志推导。未授权外部变更时交付失败原因、可审阅修正与下一步，不启动发行。

## 已登记源码摘要

红包和人才贸易的 source-manifest.json 绑定审核过的 workflow/源码字节，check-source.py 检查摘要、编译 allowlist 和引用不复制。修改已登记文件后仅更新实际审阅过的对应摘要；新纳入的 CI 文件也登记，包括通知脚本/测试/说明。不要关闭校验或把全部未审查工作区重新签为可信。本批曾有两次构建因遗漏 workflow 摘要失败，补齐后通过；这不是重新发行/覆盖旧资产的理由。准确失败/成功运行见 [basis.md](basis.md)。本 skill 更新不执行这些源码或 manifest 修改。

## 正式 Release 后的可选 Index 通知

正式 Release 成功后，独立 `notify-index` job（needs: release）调用 `ci/notify_index.py`。Index 凭据只在该 job 提供，不传给编译/打包；借用游戏/宿主/Harmony 引用仍不进入 ZIP。仅 main push 触发发行和通知，dev、PR 不触发，未新增插件手动编译入口。

使用 GitHub workflow_dispatch 调用既有 `plugin-source-updates.yml`，固定 `ref=main`、`check_only=false`，没有新 repository_dispatch 入口，不通过通知参数批准包。请求格式依据实际脚本：

```json
{"ref":"main","inputs":{"check_only":false}}
```

### 凭据与接入范围（维护者）

可选 repository secret `INDEX_UPDATE_TOKEN` 使用 fine-grained PAT，仅选择 Index 仓库，授予 Actions Read and write；令牌持有者仍必须通过已有 Index maintainer/admin 校验。不需要 Index Contents 写权限；Actions 写权限也不是仅允许触发一个工作流的精细授权。不得混用 `BUILD_REFERENCES_TOKEN`，不读取、打印、索取凭据内容；状态核对只查 secret 名称/是否存在。

不向第三方分发维护者令牌，也不要求每位作者配置 secret。第三方正常完成首次 candidate/准入后，获批策略可通过小时扫描更新；即时通知接入由维护者另行安排。缺 secret 的当前状态应写“代码已部署/通知待接入”。通知 job success 可能仅表示缺凭据警告后正常退出，不能证明通知被接收或端到端有效。

### 等待、重试与停止点

脚本 API 使用 `X-GitHub-Api-Version: 2026-03-10`，读取 dispatch 返回的具体 `workflow_run_id`；验证该 run 的 path 为 `.github/workflows/plugin-source-updates.yml`、head_branch 为 main、event 为 workflow_dispatch，再等待其结论。失败扫描最多另发两次新事件，总共最多三次 dispatch，合计等待预算二十分钟（job timeout 为二十三分钟）；每次 ref main 由 GitHub 解析最新提交，不能 rerun 旧 SHA。最终 timeout、API/凭据错误、上下文不匹配或重试耗尽只警告。不能重新发行、撤回或覆盖已经公开的插件 Release。

扫描成功只表示该次扫描流程完成；查看 [publication.md](publication.md) 的 changed/errors、准入与目录发布证据后再报告插件结果。无权维护者的 token 即使能 dispatch，Index 仍拒绝；遵守结果，不放宽校验。修正接入配置属于另行授权任务。

Release 成功但通知未确认时：保留 Release 和摘要 → 看通知警告/具体 run URL → 看扫描报告和单来源拒绝 → 看准入/受控发布/stable。新开扫描或恢复发布会改变远端，需要已有相应授权；本地先交付可审阅诊断，不用“重发插件”补通知。
