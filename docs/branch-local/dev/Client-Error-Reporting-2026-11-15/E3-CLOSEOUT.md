# E3 完成记录

日期：2026-11-16。**E3 代码、逐位置审阅和自动验证完成。** 本记录汇总 E3 的发送、网络/认证、控制解析、存储、设置、昵称、主题和 managed 控制器路径。真实游戏窗口/剪贴板/联机与发行包仍属于 E5；本次没有提交、合并、推送或发布。

## 最终行为

| 范围 | 保留的结果与错误处理 |
|---|---|
| Framework 普通发送 | 进入传输前失败为 ClientPacketNotSentException，保留 inner；进入 Send 后异常按未知结果记录并原样传播；不静默返回、不重发或退款 |
| UDP/TLS/认证 | 实际线程/Task/Timer 入口观察原错，清理故障另记；正常取消不伪装成其他错误。DNS 的 InvalidAddressException 保留原 SocketException；保留旧构造函数 |
| 共用 UDP 包装层 | 信封与模块处理异常进入原始 Exception 挂点，客户端绑定敏感出口，避免在共享层先格式化后丢失原因；没有更改报文格式 |
| Framework 控制解析 | 拒绝坏包前保存原因链/栈，使用敏感 WARNING，不自动弹窗、不触发协议回退 |
| SQLite | 独立内存/日志诊断不访问业务数据库；Busy、Corrupt、Unavailable、MigrationFailed、UnsupportedSchema、OutcomeUnknown 分开。原生 cause 保留，损坏文件不自动替换，回调不自动重做 |
| 事务/备份清理 | 记录原错后分别尝试释放；清理错误不覆盖原业务错误。已成功提交/备份的清理故障独立记录，不把成功结果改成“未提交”。通用命令准备/执行与清理同时失败时 Aggregate 保留两条完整原因链 |
| 普通/旧版设置 | 转换失败保留默认值并安静记录；XML/列表解析保留原异常。模块设置保存与回滚错误分开，双失败 Aggregate 保留原 cause；不依赖 DevMode |
| 昵称 | 保存失败不发送；发送抛错仍标 sendUnknown 并保持已派发标记，不二次发送。刷新失败不盖掉原保存错误；界面仍显示安全状态 |
| 主题 | 来源发现、解析、迁移、配置与观察者原错进入统一出口，保留最近有效主题。日志观察者失败不阻断其他清理/观察者；设置界面不展示任意 Exception.Message |
| 主题安装/删除/导出 | 安装/删除出错后的 Refresh 另行守卫，刷新失败不能替换原错或用旧库存假装验证成功；仅保留原有“刷新确证已完成”的恢复规则。导出 ZIP 已写入后的元数据/界面错误明确区分，不重复导出 |
| managed 管理/模块控制 | worker 立即记录，窗口关闭后也不丢失；只有已请求的取消才安静结束。设置确已保存但 Capture/Refresh 失败，保留成功事实并显示专用提示，不再谎报写入失败 |
| Host/UI 清理 | 每项资源单独尝试和记录，先失败的资源不阻止后续释放；主同步原异常仍裸重抛。ProcessExit 仅释放不依赖游戏的资源；窗口、语言/API scope 清理由主线程 StopExtensions 负责 |

公开旧构造签名保持可用。新增诊断参数为增量重载；SQLite 基础设施仍只引用中立存储合同和 SQLite 包，不依赖 Host、游戏或业务插件。新增敏感 Trace 兜底不输出任意消息、SQL/参数或源码路径，并明确记录预算截断。

主题包 Common 库存读取层也已增加原异常回调：安全错误码返回之前记录实际异常，Host 注入敏感出口；无 Host 时使用有界、非条件编译的敏感 Trace。身份文件/标记、主题 XML 和导出 ZIP 的写入、关闭、临时文件删除同时失败时，保留所有原因，不由最后一个清理错误替换最初故障。

## 审阅范围与迁移账本

按不可变初始清单审阅了 E3 及其 Host/主题调用方共 **369** 个历史位置；其中 **354** 个登记在 E3，另 15 个沿用 E1/E2 已完成处置。E3 354 项由 83 个实现项、235 个保留校验、27 个保留传播、4 个保留原因包装及 5 个正常结果组成，**pending-review 为 0**。

“保留校验”指原始条件检查抛出，不是把已捕获异常换成没有 inner 的新异常；“保留传播”指裸重抛或已在实际调用方观察的失败。“正常结果”包括预期缺失文件、用户名缺失/歧义、没有可连接 peer。后两种查找改为显式计数/null，不再用 InvalidOperationException 同时掩盖非预期故障。

新增加的 helper、清理、诊断和重载不在旧清单内，由本次源码指纹、相关测试和上表一并覆盖。整个 E0–E5 项目仍有 E4 插件等范围待实施，不能把 E3 清零解释成全仓库迁移完成。原始评估清单没有重写。

## 最终验证

| 检查 | 结果 |
|---|---|
| 1.6/net472 实际 Host/内置依赖图 | 通过 |
| 1.7/net10 实际源码依赖图 | 28 项目通过，含维护中的 RedPacket/TalentTrade 消费者 |
| 编译后真实组件边界 | net10 82 项、net472/Mono 76 项通过 |
| 错误服务/窗口控制/dispatcher/RootPatch | 两套运行时各 64 项通过 |
| 实际 SQLite 存储及诊断 | 两套运行时各 34 项，普通与实际打包目录关系四次运行全部通过 |
| Inventory/Trade SQLite 业务回归 | 75 项通过，生产源码与实际 SQLite，合成资产，不操作游戏物品 |
| 完整 managed 套件 | 3435 项通过；包含其中 2225 项 Store operation 回归及真实 metadata/startup 子进程 |
| 主题核心/编辑器 | 80/39 项通过；编辑器采用生产投影/catalog 与窗口替身 |
| Phase35 客户端 | 7 场景、97 项断言通过 |
| 实际身份凭据程序集 | 70 项通过，含写入/提交后杀进程与原因链检查 |
| XML/脚本/差异检查 | 中英文键一致无重复、脚本语法与根仓库/Common diff --check 通过 |

旧 managed 工程直接恢复的本机问题已用可复跑的 `run-client-managed-tests.py` 解除：使用实际构建的两版共享程序集，重建原始 metadata fixtures，并使用原 InternalsVisibleTo 测试程序集名。没有删减原套件场景。原“只在 UI Poll 才报告”的断言按 E3 明确的新合同改为在 worker 记录；保存成功和报告一次的原断言保留。

双运行时检查还发现测试假 DbCommand 在 Mono 最终化时抛异常；已把该假对象限制为 Dispose(true) 时模拟失败，避免用不符合 Dispose 模式的测试对象制造假崩溃。产品代码没有为此放宽错误处理。

## 复跑与证据

使用现有构建脚本构建新的隔离目录后：

```sh
python3 tools/quality/run-client-error-boundary-tests.py --output /tmp/fresh-boundaries --runtime16 /path/to/16/Assemblies --runtime17 /path/to/17/Assemblies --game16 GameDlls --game17 /path/to/17/references
python3 tools/quality/run-client-error-tests.py --output /tmp/fresh-errors
python3 tools/quality/run-client-storage-tests.py --output /tmp/fresh-storage
python3 tools/quality/run-sqlite-business-tests.py --graph /tmp/fresh-net10-graph --game /path/to/17/references --output /tmp/fresh-business
python3 tools/quality/run-client-managed-tests.py --output /tmp/fresh-managed --runtime16 /path/to/16/Assemblies --runtime17 /path/to/17/Assemblies
python3 tools/quality/run-client-theme-tests.py --output /tmp/fresh-theme --runtime17 /path/to/17/Assemblies
python3 tools/quality/run-phase35-client-tests.py --output /tmp/fresh-phase35 --runtime17 /path/to/17/Assemblies
python3 tools/quality/run-client-identity-tests.py --output /tmp/fresh-identity --authentication-assembly /path/to/17/Assemblies/Authentication.Client.dll
```

最终源/产物/日志摘要、处置统计见 `verification-e3-complete.json`。/tmp 可能被清理，已把指纹与结果保存在仓库内。先前 E3/E2 的验证文件为历史证据，不覆盖更新。

## 后续边界

E4 逐个闭合五个内置插件；M1 在 E4 后整合 Relay 和 Common，再由 E5 联合验收。Example/CommandRelay sample 的 Relay API 缺口在 M1 处理，不往当前 Host 塞入虚假合同来凑编译。Common 仍是未提交修改，发行前必须提交并更新精确 gitlink。

尚未声称通过：实际游戏窗口/剪贴板/最终 RimWorld 传播、真实 UDP/TLS 联机与关闭竞争、Windows ACL 或最终发行包。Mono 的既有 Unity.Burst 元数据依赖提示仍存在，断言通过；未替换游戏 DLL 来消除提示。
