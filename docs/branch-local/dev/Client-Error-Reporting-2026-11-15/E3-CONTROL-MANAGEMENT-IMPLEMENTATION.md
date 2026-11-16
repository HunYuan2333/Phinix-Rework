# E3 控制包与管理器后台边界切片

日期：2026-11-16。E3 继续实施；本次闭合 Framework 解析和 managed 管理器的后台观察路径。SQLite、设置、昵称、主题等路径仍待完成。M1 合并时机保持为 E4 完成之后、E5 联合验收之前。

## 改变的行为

- Framework 原先只记录解析异常 Message、或直接 catch 的三处入口，现在在拒绝前保存原 Exception：完整信封、capabilities payload、admission control payload。
- 解析诊断使用敏感策略，不复制不可信 payload 或任意异常消息；保留类型、原因树和方法调用帧。坏包作为 WARNING 保存到近期错误，不自动弹窗。正常格式拒绝和不匹配会话保持原有结果，不人为制造异常。capabilities/admission 失败仍拒绝协商，不改变协议，不制造 ACK、退款或重试。
- `PhinixFrameworkClient` 保留原构造函数，增加 Host 注入解析诊断的重载；无 Host 的调用方使用连接诊断的有界 Trace 兜底。
- 错误服务保留原三参数敏感入口，增加四参数 notify 入口；正常上报默认保持 ERROR，明确关闭通知的解析路径按 WARNING 记录。
- 插件管理原先把原异常存在 Outcome，等界面 Poll 才尝试记录；窗口关闭后可能永远不记录。现在在 Task 实际 catch 内调用线程安全的错误入口，不再依赖 Poll 或 DevMode，也不在 Outcome 中长期保留原 Exception。
- 管理器只静默处理已请求的取消；没有取消请求的 OperationCanceledException 作为真实失败保存。Dispose 先清空自己的 UI/任务状态，再分别守卫取消与释放，取消回调异常不会中断清理。
- 管理器修改已经保存、随后 Refresh 失败，继续保留原 LastChange，并显示 ManagedStateSavedRefreshFailed；没有再次修改、回滚或假定未提交。
- 管理器诊断回调现在可能在后台线程、Dispose 后运行，构造函数已标注线程安全要求。Host 只采集错误，不从后台访问 UI 或游戏对象；窗口显示仍由原主线程通知流程处理。

## 实际验证与限制

- 真实 1.6/net472 Host/内置图与 1.7/net10 的 28 项目图编译通过，后者包含维护中的 RedPacket/TalentTrade 消费者。
- 真实组件边界套件：net10 **72** 项、net472/Mono **66** 项通过。新增 14 项，覆盖坏控制包的原原因/堆栈和静默 WARNING、敏感文本过滤、窗口关闭后故障、受控取消、非预期取消、取消观察者抛错，以及保存后刷新失败。
- 错误服务/通知/dispatcher/RootPatch 源链接回归：两套运行时各 **64** 项通过。
- 完整 ManagedExtensionRuntimeTests 的本机直接构建仍返回失败但没有可用的编译错误诊断；隔离快照中替换共享项目引用后仍未运行成功。本次不宣称该完整旧套件通过。实际管理器行为由新增的编译后组件测试覆盖，没有改变原套件断言以绕过问题。
- 未运行真实游戏窗口/剪贴板、实际网络或最终发行包；Mono 仍有既有 Unity.Burst 元数据属性依赖提示。测试使用实际生产组件加脚本化服务故障，不伪装为完整游戏验收。

源文件、日志和程序集摘要见 verification-e3-control-management.json。已有 E3 发送/网络验证文件保留为历史记录；/tmp 目录丢失时需按脚本重新构建。

本次没有提交、合并、推送或发布；Common 的精确 pin 尚未闭合。Relay 新增的解析、timer 和观察者路径须在 M1 整合时同样接入本策略。
