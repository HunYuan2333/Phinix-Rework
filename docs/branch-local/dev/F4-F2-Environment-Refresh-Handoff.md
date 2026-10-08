# F4-F2 商店最新环境复核 / Store environment refresh

2026-10-08，dev；待游戏核查，未提交。保留 F4-B 和其他并行修改。

## 变更 / Changes

- 商店生产入口将现有 IClientEnvironmentService.Capture 与 IClientMainThreadDispatcher.Enqueue 接到通用异步桥接。Controller 只接收采集 callback，不依赖 Client 实现、Verse 或 Unity，不更改共享接口。
- 下载工作开始时重新采集一次；远端提交复核后、安装事务主体调用前再采集一次。用新快照复核已加载程序集、模组依赖、用户禁用状态及托管清单。新事实拒绝安装时不创建安装事务。
- 游戏数据/模组根目录、Phinix 主包根目录或宿主兼容版本改变时拒绝，不能把已有下载缓存和运行时事务服务转向另一环境。
- Cancel/Dispose 结束等待，排队的迟到回调检查 token 后跳过采集。复用操作超时机制，30 秒未完成主线程采集报 EnvironmentCaptureTimeout；不为游戏或单个插件写例外。
- 连接 adapter 的 using 范围覆盖重新采集等待，成功、拒绝或取消都释放；续体不在主线程内联执行。主体持久化提交后的完成语义继续保留。
- 记录 managed.environment_rechecked；采集不完整使用已有 contextReasons 日志。新增宿主环境变化的中英提示。

The production module always supplies the main-thread capture callback. The optional callback in the internal controller constructor preserves standalone deterministic test callers; production capture failure does not fall back to the old snapshot. No plugin payload, signature, wire protocol, inventory ownership, journal schema or installation commit algorithm changes in this batch.

## 验证 / Validation

完整构建 0 errors / 9 warnings；ManagedExtensionRuntimeTests 在 .NET 10 与 Mono/net472 各 3168 assertions（各包含商店操作 2181 assertions）；产物检查 31 项通过；validator snapshot 14 文件一致；git diff --check 通过。确定性回归覆盖排队执行、采集异常、队列异常、取消及迟到回调；生产 controller/runtime/HTTP 模拟链覆盖环境不变、禁用、同名程序集冲突、目录改变、采集不完整及取消。每个失败场景核对未调用安装主体、未写包或事务，且下载 adapter 已释放。

## 边界 / Limits

- 复核是提交前的一次事实快照，不提供 RimWorld/CLR 全局原子锁，也不证明任意第三方 API 兼容。主体继续负责事务中的文件与清单复核。
- 当前宿主程序集基线在启动时建立；新增可用宿主依赖未承诺热加载，必要时须重启。
- 游戏暂停主线程超过 30 秒会拒绝本次安装，可恢复主线程后重试；不删除文件或绕过安全检查。真实超时等待未作为回归中 30 秒计时场景执行，取消和故障传播已有确定性覆盖。
- 全局清单异常影响范围和所有商店错误映射仍待后续批次；旧整模组路径和主题亦未迁移。F4-B 样例未因本次主包核查而宣称验收。

## 游戏核查 / In-game steps

1. 部署完整本批主包并重启，正常安装一个尚未安装的插件，应成功并提示重启生效。
2. 下载较慢时，在扩展管理/设置中禁用待安装插件的全部模块，返回商店等待；应拒绝安装，重新启用后重新确认计划可重试。如下载太快无法操作，此项以回归证据为准，不能声称亲测。
3. 安装/下载过程中取消，重新刷新后可重试；返回主菜单或退出时不应留下卡住的操作。
4. 重启确认已安装插件仍加载，聊天、库存和已有交易功能无回归。

安装主体和持久化格式未修改；仍保留上一批 schema 2 未完成事务须在新版恢复后再回退的限制。

### 实际验证命令 / Exact validation commands

```sh
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
python3 /tmp/phinix-env-refresh-package.py
python3 Extensions/PluginStore/RepositoryAutomation/scripts/validator_snapshot.py check --source-root .
git diff --check
```

pwsh 不可用，未执行 PowerShell 产物脚本；使用 Python 等价检查其必需文件、唯一运行库、完整构建字节和无游戏引用 DLL。没有运行游戏内核查。测试及产物通过不替代用户验收。

交付主包：`/tmp/phinix-rework-environment-refresh-20261008.zip`
SHA256：`c6d0430d4a9dc11ccc3ccc722a5746730a095d8808dc17805af4b4389cf06eec`
本批未改变持久化格式、协议或物品归属；上一批 schema 2 的恢复/回退限制继续有效。
