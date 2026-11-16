# E3 网络与认证边界切片

日期：2026-11-16。接续 E3-SEND-IMPLEMENTATION.md。E3 仍在实施：Framework 的坏包/控制面解析、SQLite、设置/昵称/主题和 managed 控制器等路径尚未闭合，不能把本切片视为 Host 全量完成。

## 已实现

- 增加 `ClientConnectionDiagnostics`，连接和身份组件向 Host 传递原始 Exception；Host 的实例回调调用 `ReportSensitiveBoundary`。不添加远端遥测、不使用业务数据库，任意异常消息在这些敏感边界省略，保留类型、原因与调用帧。
- 没有 Host 或诊断回调失败时，直接调用 Trace listeners 输出有界原因/帧；不依赖 `TRACE` 条件编译，不读取异常 Message，不输出源码路径。逐 listener 隔离失败，并限制递归。不同诊断实例之间的正常桥接允许继续，避免认证 → 连接诊断 → Host 被误判为递归。
- 保留旧构造函数和工厂签名，新增带回调的重载；不强制插件使用新接口。Host 在创建 Configured transport、具体 UDP/TLS 和认证组件时绑定本实例的敏感诊断策略，不保存进程静态 Host 引用。
- `ConfiguredClientTransport`：工厂/配置、注册、连接与 release 原故障均先记录；构造/连接回滚的清理失败另外记录，裸重抛原异常。释放失败保留旧 transport 所有权，不能替换仍未退出的实例；断线观察者逐个隔离，前一个失败不跳过后面的清理。
- `NetClient`：DNS SocketException 在保留原有失败结果前保存原栈；poll/probe 的实际线程入口观察故障、请求停止两条 worker，并尝试断开。断开故障单独保存，保留既有自线程不 join 与外部有界等待规则。
- `TlsNetClient`：无效 pin 包装保留 FormatException；连接清理不覆盖原错。Write/Ping 在实际执行处分别观察故障，并停止连接；channel/certificate/socket/cancellation 清理分别记录。读循环不在已经停止时开始新读取；不再通过“Stop 已请求”吞掉任意 Exception。仅在连接正在停止且 OperationCanceledException 的令牌已取消时按正常 IO 取消处理。channel 使用 linked deadline token，不能只比较 token 身份；活动连接的 frame deadline 仍是故障。断线观察者继续处理其他订阅者。
- `ClientAuthenticator`：历史凭据读取/写入先走敏感记录再裸重抛；构造绑定失败清理自己的订阅和 timer；session renewal 的 Timer 实际入口捕获发送故障，撤销本地认证、尝试断开、停止 timer。受控销毁后的 ObjectDisposed 不制造新的业务故障。
- `PasswordlessIdentityClient`：响应/身份存储异常在友好拒绝前保存原异常；实际 Timer 入口连 enabled/状态检查阶段的失败也捕获。失败通知与断开分别守卫，观察者失败不会阻止断开。没有选择旧协议、新建替代身份、重发业务请求或退款。
- `ServerIdentityCredentials`：JSON、Argument、Format 三种 InvalidDataException 包装均保留 InnerException，原原因/栈不再丢失。

## 验证

- 1.6 实际 Host/内置插件 net472 依赖图通过；1.7 net10 的 28 项目依赖图通过，包含维护中的独立 RedPacket/TalentTrade 客户端。
- 编译后真实组件边界套件：net10 58 项，net472/Mono 52 项通过（含原 E2、E3 发送检查）。现代身份/TLS 仅在 net10 存在，所以检查数量不同。
- 新增检查覆盖诊断回调故障与敏感兜底、配置/绑定/连接原错与清理错、断线订阅者隔离、真实 worker 入口自 join 规则及清理故障、历史/现代身份 Timer、正常 linked 取消与活动 frame 超时的区别。
- Phase35 7 场景、97 项断言通过，编译清单补入新连接诊断源码。
- 实际 Authentication.Client.dll 凭据组件 70 项检查通过，含 kill-after-flush/commit 与新增三种 wrapper 原因链检查。Linux 下验证，不代表 Windows ACL 或游戏内验收。
- 本环境创建 socket 曾返回 Access denied；边界套件最终使用无 socket 的入口/故障注入检查，不把它算作真实 UDP/TLS 联机验证。真实网络/游戏内、TLS 关闭竞争与最终包仍列入联合验收。Mono 保留既有 game reference 的 Unity.Burst 属性依赖提示，全部断言通过。
- 默认 net10 graph 仍会选择尚依赖 relay 契约的 Example 并失败；本次显式选择维护中的 RedPacket/TalentTrade 验证 28 项目，不新增虚假 Relay API。Example 在 M1 合并后重新构建验收。

最终源/程序集与日志摘要见 `verification-e3-network.json`。可使用已有 build-graph-isolated.py / build-net10-graph.py、run-client-error-boundary-tests.py、run-phase35-client-tests.py、run-client-identity-tests.py 复跑；每次使用新的隔离输出目录。

Common 仍未提交/更新精确 pin；当前未提交、合并、推送或发布。Relay 的实际合并门槛与冲突安排见 RELAY-INTEGRATION.md。
