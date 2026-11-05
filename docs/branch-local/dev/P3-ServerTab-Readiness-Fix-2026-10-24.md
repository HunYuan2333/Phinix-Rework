# 已撤回：P3-02 绘制 guard

实际 Player.log 已证明 Mod 构造被错误的 `gameScopes.Publish()` 主线程校验中断。下文只记录当时的局部假设和有限测试，不代表现场根因已修复。P3-02 不应继续用于联合验收；绘制修改已撤回，参见 P3-Startup-Thread-Fix-2026-10-24.md。

# P3 主窗初始化空引用修复（2026-10-24）

用户报告：Waylith / RimWorld 1.7 绘制 `ServerTab.DoWindowContents` 大量 NullReferenceException，无源码行号。本轮只改独立业务分支的 ServerTab 生命周期边界，保留原 UI 升级工作区冻结 37 文件原 hash。

确认的路径：Client 在构造期先发布 Instance，随后通过 LongEvent/main-thread dispatcher 延迟初始化 Theme；ServerTab 原绘制没有判断就调用 HostThemeDrawing.BeginDraw。原 P3-Test-20261023-01 实际 DLL 的负对照复现 `HostThemeDrawing.Scope..ctor → BeginDraw → ServerTab.DoWindowContents` NullReferenceException。该结果确认可复现的一条路径，不能唯一确定用户没有行号的 Waylith 现场。

修复：绘制前检查 Host、Settings、Theme 与 Event.current；未就绪时不进入 GUI 作用域，取消旧 accept handler，每帧重新检查，避免把暂未初始化永久缓存为失败。provider 刷新可处理缺 Host，侧栏不访问空 GUI event。没有 catch NullReferenceException 或禁用日志。若主题初始化本身失败，仍需处理此前启动异常；本修复不伪装初始化成功。

测试：`tools/quality/run-client-component-probes.py --suite shell-readiness` 新增真实 Host DLL 生命周期探针，只有调色板端口使用独立测试代理，无原生 GUI。原包负对照失败；最终 1.6/net472 与 1.7/net10 两版各 90 次未就绪实际绘制调用、6 个检查通过，主题发布后真实 drawing scope 可恢复并正确释放；最终包交易 wire 各 34 项通过。双版完整构建、资源、程序集闭包与 ZIP 审计通过。未运行真实 Waylith GUI，不声称用户现场已通过。

客户端 P3-Test-20261024-02 包及完整命令/源码/DLL hash、失败对照：`/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-P3-Test-2026-10-24-02/`。双方完整退出并装同一完整包，服务端仍用 P3-01，无更新必要；原测试存档、凭据和日志保留。先验主窗启动/连接/断开/重开，再继续交易用例。没有安装、部署、发布或提交。
