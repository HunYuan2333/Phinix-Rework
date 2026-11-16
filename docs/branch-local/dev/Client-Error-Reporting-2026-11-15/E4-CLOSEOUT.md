# E4 完成记录

日期：2026-11-16。**五个内置插件的 E4 代码迁移、位置审阅及自动验证完成。** 实际游戏弹窗、剪贴板、最终 RimWorld 传播、联机和发行验收仍归 E5；Relay 整合 M1 尚未执行。本轮没有提交、合并、推送或发布，原有 SQLite/业务修改均保留。

## 最终行为

| 模块 | 已闭合路径与保留的结果 |
|---|---|
| Inventory | 存储、迁移、物理交付、codec/预览、兼容 journal/checkpoint、图标、事件、批量提取和 UI 清理保存原始原因与调用帧。存储未确认仍是不可确认，不伪装为已回滚；已提交余额、原 Game 所有权和批次规则不变。事件/日志观察者失败不能把成功提交变成业务拒绝。可选图标、旧数据维护和正常数量溢出保留原降级/拒绝规则 |
| Trade | offer、item pipeline、转换/采集、inbox/journal、收据、交付/恢复泵、窗口和生命周期清理均接入。准备失败保持 NotSent；派发后不确认、窗口关闭或停止不生成退款/重发。坏观察者不阻止后续观察者，坏清理不掩盖主错；Scribe 主错和 FinalizeLoading 错误分别观察，无主错时 finalization 错误继续传播。Quarantine 与数据库错误传播保留原实例和原始帧 |
| LegacyAdapter | Chat/Trade 转换、协议解码、登录期延迟包、回显、会话和清理记录完整敏感诊断。Legacy Chat 发送抛错沿原同步调用链裸重抛，避免变成丢失原因的 bool/新异常；已派发后回显失败保持已派发，不降级、不再次发送。Legacy Trade 的未知发送包装保留 inner，全批拒绝规则不变 |
| Chat | 解码、图片启动/处理/取消、提及通知、转换、订阅者、操作菜单和清理均观察原错。单个坏消息/图片不阻断后续消息；被动媒体错误安静记录并按原规则降级，图片不使整个页面失效。队列并发、尝试预算、回调代数及主线程绘制所有权不变 |
| PluginStore | 固定字段 RepositoryDiagnostics 审计保持独立，旁路保存完整敏感原因树和堆栈。规划、元数据/载荷、缓存、临时资源、窗口、主题商店、安装和取消均覆盖。超时包装保留原 OCE；请求的取消/授权撤销保留普通结果。两个回溯规划器用 ExceptionDispatchInfo 传播保存的原异常。已提交安装和刷新/清理失败分开，不改缓存权威、安装事务或路径防护 |

SDK 增量增加模块自有 `ClientModuleDiagnostics` 绑定和独立清理观察器，原错误服务接口保持可用。各模块通过普通 `IClientErrorService.ForModule(this)` 获取实际所有者，Shutdown 的 finally 撤销静态绑定；Host 没有内置插件专属分支。Inventory 游戏适配程序集为使用同一诊断合同增加 SDK 引用，两版实际依赖图已编译验证，最终发行打包仍归 E5。

敏感报告保留类型、原因树、LoaderExceptions 和调用帧，省略任意异常消息与本地源码路径；原固定字段商店审计仍可提供安全业务错误码。正常缺失、取消及预期业务拒绝不机械变成弹窗。被动解析、缓存、图标和清理以安静诊断为主；玩家发起的操作故障按边界请求通知，由 Host 主线程窗口泵处理。上报不重试、不退款、不确认结果，也不从隔离或后台路径人为抛出异常。

## 位置审阅

| 模块 | catch | throw | 总计 | 待审 |
|---|---:|---:|---:|---:|
| Inventory | 50 | 74 | 124 | 0 |
| Trade | 65 | 224 | 289 | 0 |
| LegacyAdapter | 23 | 31 | 54 | 0 |
| Chat | 23 | 39 | 62 | 0 |
| PluginStore | 100 | 479 | 579 | 0 |
| 合计 | 261 | 847 | **1108** | **0** |

不可变初始清单未修改。E4 登记为 263 个实现项、733 个原始校验、64 个保留传播、41 个原因包装和 7 个正常结果。“原始校验”是创建原始拒绝，不是用没有 inner 的新异常替换已捕获原因。正常结果及逐位置理由见 `E4-source-review.json`、`migration-decisions.json` 和 `migration-ledger.tsv`。新加的诊断、清理及捕获点不在历史数量中，由本轮源码指纹和故障注入覆盖。

可复跑的登记门槛：

```sh
python3 tools/quality/inventory-client-error-migration.py --require-stage-complete E4
```

该门槛只验证登记闭合；运行时结果另外验证，不能用匹配数量代替业务证据。

## 最终验证

| 检查 | 实际结果 |
|---|---|
| 1.6/net472 实际 Host/内置依赖图 | 完整隔离构建及最终增量构建通过 |
| 1.7/net10 实际源码依赖图 | 28 项目及最终增量构建通过，含维护的 RedPacket/TalentTrade 消费者 |
| 错误服务/窗口控制/dispatcher/RootPatch | net10、net472/Mono 各 92 项通过；包含五个模块的归属、敏感复制、停止后撤销、坏 reporter/清理观察者 |
| 实际编译组件边界 | net10 82 项、net472/Mono 76 项通过 |
| 真实 SQLite 与生产 Inventory/Trade 业务源码 | 89 项通过，合成资产，不修改游戏物品 |
| 完整 PresentationRuntimeTests | 535 项通过；原 GUI/游戏替身，未放宽图片与页面操作断言 |
| 完整 managed 套件 | 3436 项通过，包括其中 2225 项 Store operation 回归；新增 managed 规划器原帧断言 |
| 完整 PluginStoreRuntimeTests | 1042 项通过，包含普通规划器原帧断言、原载荷/仓库/缓存/安装场景及 metadata fixture |
| 实际 Legacy 适配器 | 89 项通过；新增 Chat 发送后抛错保持原实例/传输帧、仅发送一次且不生成成功回显 |
| 原 ChatRegressionTests | 19 场景通过 |
| 实际交易 wire adapter | 28 项通过，真实 SQLite intent/attempt 在传输前存在，故障注入保持 NotSent 且不发送 |
| 检查与样例 | E4 登记零待审，Python 语法与根仓库/Common diff --check 通过；五份生产错误服务生成的合成复制报告已保存 |

旧测试夹具按当前真实合同修复：反射构造补齐可选诊断参数；交易 wire 检查由旧文件日志改为实际 SQLite journal，保留“先持久化再传输”及会话不持久化的原断言，并直接注入已知本地写入失败。没有给产品补虚假旧接口来凑测试。

测试入口为 `run-client-error-tests.py`、`run-client-error-boundary-tests.py`、`run-sqlite-business-tests.py`、`run-client-presentation-tests.py`、`run-client-managed-tests.py`、`run-client-store-tests.py` 和 `run-client-built-in-tests.py`。后两者生成隔离测试工程和原 metadata fixture，补齐依赖与原测试数据布局，不写源码 bin/obj 或修改发行目录。

源码、产物、日志及结果摘要见 `verification-e4-complete.json`；合成样例见 [E4-sample-reports](E4-sample-reports/README.md)。`/tmp` 可被系统清理，摘要和 SHA-256 已保存在仓库。Mono 的既有 Unity.Burst 元数据提示仍存在，断言通过；未替换游戏 DLL。

## 下一阶段

M1 的 E3/E4 代码迁移门槛已满足。按照 [Relay 整合安排](RELAY-INTEGRATION.md)，先整合 Common、形成新精确 pin，再解决 Host/传输与 Relay 冲突，完成两版联合回归。当前 Common 及客户端仍有未提交修改，尚未更新 gitlink。E5 才验收实机窗口/剪贴板/最终传播、真实网络、平台差异及发行包；E4 完成不等于首版已经发布。
