# 1.6 fix 整合到 1.7 迁移工作区（2026-10-14）

用户要求先合入 GitHub 最新 fix。远端 main 仍为 c6988e5；本轮新增修复位于 origin/codex/plugin-upgrade-host-migration。工作分支 codex/uiux-release-main 从 c6988e5 快进到 **13412fed6567d6bb0199b0649d1aaee88b1249bf**，没有创建新的合并提交，也没有推送或发布。

合入远端两个已有提交：

- 29e8ea0：固定 Common 新提交并添加 Host API 迁移后的独立插件升级/恢复回归。
- 13412fe：修复 Chat Enter 用户意图和 composer 控件 ID/焦点稳定性。

Common 按已合入的精确 gitlink 更新到 **be978cfbb3f9299c24fa3fa068ccc109262efc22**，没有 update --remote，子模块无本地修改，protobuf SDK pin/source 没有变化。其他三个插件仓库仅 fetch 核对，未发现相应新增 fix，未改变其本地迁移工作与固定 Host pin。

## 修改保存和冲突处理

操作前将全部 142 个修改/新增路径、工作区/index binary patch、原 HEAD 和 SHA 保存到各 Git 仓库之外的 `local-compatibility-analysis/pre-fix-13412fe`；同时创建 pre-fix-13412fe-r17-migration stash，恢复后仍保留作为额外备份。

stash apply 有两处冲突，已按双方语义整合，没有采用整文件覆盖或 reset：

| 文件 | 合入内容 | 保留的迁移 |
| --- | --- | --- |
| Extensions/Chat/Client/ChatMainTabProvider.cs | composer 在 feed/reply 之前绘制，避免动态列表改变其 numeric control ID | Draw 继续用 ClientPresentationBounds；composer 只绘制一次 |
| Tests/PresentationRuntimeTests/Program.cs | 原新的 Return/KeypadEnter、consumed event、IME 文本变化/未聚焦/重复意图回归 | 全部 session Draw 使用中立 Bounds |
| Client/Source/UI/ClientPresentationService.cs | 自动合并，并人工核对：TextField 前捕获 Enter；字段消费事件但文本未变仍提交一次意图；composition/文本改变继续抑制发送 | 原 image lease、menu/context、主线程/失效/disabled guard 与同一 action/confirmation 机制保留 |

首次恢复后的 SHA 对照确认：142 个原路径仅上述三处与远端 fix 重叠的文件变化；其他 139 个修改/新增路径原样保留。随后验证工具仅增加 fresh infrastructure 参数和测试 protocol fixture 的隔离复制支持。index 恢复到操作前的未暂存状态，没有未解决冲突；stash 与独立副本不删除。

源产品 FileVersion/InformationalVersion 按远端 fix 为 **0.10.1**，CLR Host 身份仍 0.9.7.0；这是源分支版本，不宣称已正式发布。迁移 API 3.0 身份/pin/manifest 门槛未因此闭合，正式发布记录 JSON 不改。

## Common 修复边界

旧 Host API 不兼容且已经被拒绝的安装包仍留在候选图中、不能满足依赖，但不再阻止独立包被修复。新候选及仍与 Host 兼容的现有消费者继续必须通过验证；没有去掉 CLR 引用、依赖、模块冲突或包预检。management-preflight 追加明确审计，安装审计测试按 stage 筛选以支持重复诊断。新 HostMigrationReplacementTests 验证独立修复顺序、保护依赖及数据保留等。

这不接管业务托管、协议、Thing/Pawn 或游戏保存，也不把 UI 回调成功变成业务确认。

## 检查与限制

| 范围 | 状态 | 证据 |
| --- | --- | --- |
| 分支/精确 Common pin/冲突/修改完整性 | 源码检查 | 两 commit 快进；无冲突、index 无新暂存；独立备份与 stash 保留 |
| 聊天/公共呈现 | 白盒通过 | 315 source/stub 检查；新 Enter/IME/焦点意图与迁移共同执行，非实际输入法 |
| 新修复的托管插件 | 编译通过、白盒通过 | 真实新 Common 源码，ManagedExtensionRuntimeTests 3390 断言；另有 Store operation 2225 和 actual startup child 检查；fake 包/路径/启动场景，不是真实玩家商店 |
| 1.6 真实客户端/插件工程 | 编译通过 | 严格 locked restore、完整客户端 solution（包含基础设施源码）、三插件真实工程；独立 snapshot |
| rev1248 1.7 选定消费者 | 编译通过、白盒通过 | 23 项、六套非 GUI 回归；最终用上述真实构建的 fresh infrastructure DLL，避免原 Output 的旧 Common 掩盖 fix；仍非完整 1.7 原生构建 |
| 真实 1.6 / 1.7 游戏 | 待测 | 游戏通过：无；没有修改正式存档或安装中间 DLL |

完整真实工程输出 `local-compatibility-analysis/fix13412fe/full-build`，托管测试日志 managed-tests-results-final.log；初次 test harness 缺 protocol 文档 fixture 是隔离复制遗漏，补入真实 fixture 后重跑通过，非 fix 业务错误。315 呈现检查输出 `/tmp/phinix-fix13412fe/presentation`。1.7 最终输出 `/tmp/phinix-fix13412fe/closed-1.7`，使用 `run-isolated.py --infrastructure <fresh-build>/Phinix-Rework/Output`；先前 `/1.7` 为旧 seed 的编译结果，不作为新 Common 运行证据。

下一步在新基线上继续新版绘制 adapter、完整 net10 构建和版本化联合候选。可运行联合候选仍主动提供准确版本/hash/安装及测试步骤，不把当前合入或白盒通过当作游戏验收。
