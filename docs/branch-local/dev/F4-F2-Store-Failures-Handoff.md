# F4-F2 商店故障分类与统一提示 / Store failure classification

2026-10-08，dev；实现和回归完成，待游戏核查，未提交。F4-B 与其他并行修改保留。

## 实施范围 / Changes

- PackageModels 内的 StoreFailureInfo 为统一的提示/诊断模型，提供 Target、Environment、Repository、Storage、Recovery、Action、Unexpected 分类。只决定提示及日志字段，不授权安装、重试或文件清理。没有按红包/EasyUpgrades/第三方模组写特判。
- 明确区分环境未就绪、托管记录归属不确定和待恢复事务。Planner 不再把所有清单问题统一称为 IncompleteEnvironment；无法确认的自身托管记录报 ManagedInventoryUncertain 并保留具体原因。事务原因可引导恢复，原始门禁代码仍保留。
- 两套商店控制器及视图共用分类。主要失败入口记录 operation、failureScope、exceptionType、causeType、contextReasons、requestId，以及原有引用/身份诊断。下载开始到失败复用同一操作关联 ID，安装结果已有的 transactionId 继续关联。
- 玩家详情保留安全错误码、请求 ID、去重的有界原因及程序集引用信息；取消直接打印异常 Message/ToString 的旧浏览控制器和 UI 路径。未知错误仍有通用提示和原始安全代码，不吞掉异常或宣称操作成功。
- 同步按钮错误也进入结构化审计。徽章、维护者资料和一次性提示的非致命警告统一结构化；资源失败仍保留原降级行为。
- 中英提示覆盖网络、限流、禁用、依赖、冲突、文件校验、路径/存储、环境、记录不确定、待恢复、计划变化、输入错误和未知错误。只在视图重建时计算异步错误描述，避免每帧创建分类对象。
- DownloadCheck 项目补上已有 InstallationInput 扩展方法的源文件链接；未改变实际下载协议。
- PackageModels 是索引校验器的固定生产源码之一，已同步 14 文件快照并验证静态校验器构建。

## 保留的边界 / Preserved boundaries

This batch changes failure presentation and audit data, not installation authority. Uncertain managed inventory continues to block mutation. Missing/disabled/incompatible dependencies continue to reject affected plans. Recovery journals, ownership proofs, loaded assembly collision checks, cancellation and durable commit rules are preserved.

把一个局部托管文件损坏安全隔离成不影响其他包，需要更细的身份可信证据；本批没有通过忽略记录或跳过主体门禁实现隔离。旧整模组 About 检查和主题边界仍按 F4-F2c/独立主题批次处理；不宣称全部边界审查已经完成。旧浏览器的事务算法没有修改。

Common managed runtime 的开发模式详细异常诊断仍保留；本批的安全日志要求针对 RepositoryDiagnostics 和商店玩家提示，不是删除整个 Player.log 中其他系统的诊断。

## 验证 / Validation

- ManagedExtensionRuntimeTests：.NET 10、Mono/net472 各 3212 assertions，其中各包含商店操作 2224 assertions。
- PluginStoreRuntimeTests：946 assertions，包含旧浏览控制器的安全错误快照及无专用 endpoint audit 时仍记录失败。
- 完整 Release 1.6 构建：0 errors / 9 warnings（既有依赖/漏洞数据访问警告）。
- PayloadCheck 构建：0 errors / 1 warning；DownloadCheck 两目标构建：0 errors / 2 warnings。没有访问线上服务执行 live 下载测试。
- 31 项产物核对通过；唯一运行库、无游戏引用 DLL、错误提示中英键齐全、ZIP 中 Utils/Client/Store 与实际 Release 构建字节一致。
- Validator 14 文件快照一致；校验器构建 0 errors / 0 warnings，6 个源码快照回归通过。git diff --check 通过。
- 没有执行游戏测试。取消/回滚、清单归属拒绝、主线程环境复核、请求关联和安全日志有确定性回归；不将其等同于实际联机或所有插件兼容认证。

### 实际命令 / Exact commands

```sh
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStorePayloadCheck/PluginStorePayloadCheck.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py refresh --source-root .
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj -c Release --no-restore
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -p test_validator_snapshot.py
python3 /tmp/phinix-failure-package.py
git diff --check
```

pwsh 不可用，产物检查使用 Python 等价核对。

## 游戏核查 / In-game steps

1. 部署完整匹配主包并重启，正常刷新、安装插件，取消后重试，确认成功/取消状态及重启加载无回归。
2. 遇到缺依赖、禁用或网络错误时，状态栏应给出对应中文提示；错误详情保留代码和请求 ID。不要为验证而修改托管记录或删除事务。
3. 若有未完成事务，使用新版正常重启恢复；仍失败时核对日志中的 operation/failureScope/contextReasons/transactionId，不能把重启视为已恢复成功的证明。
4. 一般聊天、库存、读档及交易功能做简短回归；没有改其协议、存档或物品归属。

## 后续 / Next

临时商店故障分类与提示改造已收拢。回到 F4 插件装配主线，先核对 F4-B 示例候选的验收状态，再分步推进 F4-C Inventory 普通服务/UI 装配；F4-D 专门处理存档生命周期。整模组安装边界与主题事项保留，不混入 Inventory 领域逻辑。

主包：`/tmp/phinix-rework-store-failures-20261008.zip`
SHA256：`ccbf349d6ceabd2e2952a36d0de1aafe61354f3c2dc9b869e2ecede33821fb9e`
本批无新的持久化格式变更；上一批 schema 2 未完成事务必须在新版恢复后再回退的限制继续有效。
