# 插件安装流程复核与通用修复 / Store installation flow review

2026-10-07，dev。本批针对 Player(1).log；保留 F4-B 和其他并行修改，未提交，未运行游戏。

## 日志证据与修复 / Evidence and changes

- 人才贸易要求 Assembly-CSharp 1.6.9676.18020，实际加载 1.6.9676.17735。游戏程序集按 Major.Minor 比较；通用元数据层提供注入式 `ManagedHostReferenceRule`，客户端以实际 Verse.Game 所在程序集声明规则。其他库默认仍要求同 major 的兼容升级；名称、culture、token、歧义和载荷锁不放宽。启动绑定和安装/管理预检使用同一规则，重新构造 HostFacts 时也保留规则。
- 红包 HTTP 200、下载和载荷校验完成后，暂存写入 DirectoryNotFoundException，随后 ManagedInstallRecoveryRequired。日志中的暂存文件路径长 273，目录长 237，符合旧 Windows 路径限制风险；未在原用户 Windows 环境复现，不能仅凭日志排除权限或其他文件系统因素。
- 安装事务 schema 2 用批次索引 p0/p1 替代临时目录及临时 receipt/state 文件中的完整包哈希。最终包目录、身份记录及状态格式不变。schema 1 的准备回滚和提交恢复继续支持；回归覆盖两者。
- 所有安装文件、备份和元数据路径在创建事务前预检，Windows 使用保守的 Framework 路径限制。开始写入标记移到实际写入边界，预检拒绝不应变成“结果未知”。权限、空间、文件被占用等运行时错误仍必须由真实写入结果处理。
- 结构化商店日志新增 operation、exceptionType、contextReasons，限定安全代码、去重、最多 32 条；环境缺失和托管清单异常分别记录。保留请求、事务及程序集引用诊断。状态栏显示具体友好提示，增加路径过长、存储失败、待恢复及环境未就绪的中英提示。

These are generic storage and host policy changes. There is no RedPacket/EasyUpgrades exception, no inspection of unrelated mod DLLs, and no blanket assembly-version relaxation. The reported IncompleteEnvironment text is absent from the supplied log; its exact source remains unconfirmed. Added readiness diagnostics are intended to resolve that uncertainty.

## 当前顺序 / Current sequence

目录与远端清单校验 → 依赖规划 → 下载及哈希/载荷校验 → 远端状态和本地清单复核 → 主体预检 → 路径预检 → 准备日志与暂存 → 提交前复核 → 持久化提交决定 → 文件/记录提交 → 重启加载。

This ordering is reasonable: installation does not execute downloaded code, the host owns filesystem mutations, cancellation before the durable decision rolls back preparation, and recovery after the decision finishes the same batch. A successful download is not installation or activation success.

## 仍需分批解决 / Remaining work

1. **F4-F2 环境复核**：controller 的复核仍使用传入快照；通过主线程 dispatcher 重新采集，再在工作线程规划，避免使用下载前事实。不得在工作线程访问 RimWorld API，也不应让 store 直接依赖 Client 实现。
2. **故障范围**：planner 把任一托管包诊断汇总为 IncompleteEnvironment；主体存在未完成事务、清单/身份不确定的整体拒绝保护。先区分目标/依赖缺失与真实全局所有权不确定，再缩小影响范围，不能直接忽略未知记录或事务。
3. **统一商店报错**：本批新增通用上下文及关键阻塞错误提示；旧整模组安装、浏览控制器、同步动作错误仍有原始文本/分散映射。后续收敛到同一错误分类与安全日志入口，保留全部诊断代码。不要宣称本批已覆盖所有商店错误。
4. **F4-F2c**：旧整模组 About.xml 检查及主题资源边界仍按既有审查单单独迁移。

## 风险 / Limits

- 游戏同 1.6 的补丁版本允许安装，并不证明具体游戏 API 一定兼容；跨 1.5/1.6/1.7 仍拒绝。
- 老版客户端不能理解 schema 2 的未完成事务。回退版本前，须由本版完成或恢复事务；不能手动删除事务、安装凭据或包目录。已完成安装的包/状态格式没有改变。
- 真正清单损坏、文件所有权不明确、缺依赖和事务未恢复仍可能拒绝安装。这些保护不因 UI 提示优化而消失。
- 编译、模拟 Windows 路径和回归不能替代 Windows 游戏内安装核查。

## 验证 / Validation

最终验证：完整构建 0 errors / 9 warnings；ManagedExtensionRuntimeTests 在 .NET 10 和 Mono/net472 各 3124 assertions（各包含商店操作 2137 assertions）；PluginStoreRuntimeTests 904 assertions；validator snapshot 14 文件一致；产物检查 31 项通过；git diff --check 通过。回归包含原路径超过 260、新路径缩短，新旧日志恢复、全部崩溃点、批量升级及取消、默认严格引用、显式系列策略和规则传递、安全诊断字段。

## 游戏核查步骤

1. 替换完整主包并重启，保持 EasyUpgrades 等普通模组启用；若存在旧事务，先观察启动恢复日志。
2. 安装红包和人才贸易，确认下载、安装成功及待重启状态；记录失败详情及 operation/contextReasons/transactionId。
3. 重启后检查插件条目与功能；单纯读档后检查聊天历史保留。
4. 安装、禁用、重新启用和卸载示例插件，重启核对实际状态。其他普通模组继续由游戏加载。

### 实际命令 / Exact commands

```sh
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
python3 /tmp/phinix-store-path-package.py
git diff --check
```

系统没有 pwsh，因此产物检查使用上述 Python 等价核对脚本，未执行原 PowerShell 脚本。主包 ZIP 的 Utils、Client、Store 字节已核对为本次 Release 产物。未运行 Windows 或游戏内测试。

包：`/tmp/phinix-rework-store-install-fix-20261007.zip`
SHA256：`7bc155651c67a00a7bd3be914c877170d226c2d6d184a987a1512173b7fe78c1`

## 用户验收及下一批（2026-10-08）

用户反馈“已修复 成功”，记录上一批实际安装/部署问题已通过核查；不扩展为每个游戏步骤或所有模组均验证。后续 Player(2)/(3) 日志显示旧运行库和未加载主包，最终重新部署成功；无需修改正确 About.xml 或删除托管事务。日志和用户文件不入仓库。

环境刷新候选已实施，见 [F4-F2-Environment-Refresh-Handoff.md](F4-F2-Environment-Refresh-Handoff.md)。上面的“仍需分批解决”第 1 项现在为实现/回归已完成、游戏核查待完成；第 2–4 项继续待办。

## 故障分类与提示候选收尾（2026-10-08）

第 3 项统一商店提示/日志已实现候选，见 F4-F2-Store-Failures-Handoff.md，待游戏核查。第 2 项现在能准确区分目标、环境及恢复原因，但没有放宽归属不确定时的整体写入保护；更细的安全隔离仍需明确身份证据。第 4 项继续独立待办。
