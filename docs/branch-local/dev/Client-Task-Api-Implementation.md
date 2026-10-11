# A1 首批：主线程任务结果与 Example

日期：2026-10-11。源码实现与本地候选，不代表整个 A1 完成或游戏验收。

## 当前契约

IClientTaskService.ForModule 在 Activate 中绑定实际模块实例，返回自有 IClientTaskScope；服务本身借用。每次绑定获得独立 lease，同一模块最多 16 个未关闭 scope。Host lifecycle observer 准入实际模块，非活动实例不能凭 ID 字符串绑定。

Submit(Action, CancellationToken) 返回可在任意线程 Capture 的不可变状态：Queued、Running、Succeeded、Rejected、Canceled、Failed，含 task ID 和稳定 code。没有同步 Wait，不公开内部 Exception；null action 属于作者错误抛出 ArgumentNullException。

- 队列满：Rejected / QueueFull，已接受任务不会为了新任务被挤出。容量仍为 500，每帧最多取 100 个。
- scope 关闭后提交：Rejected / ScopeClosed；取消 token 已请求：Canceled / RequestCanceled。
- 排队后 token 取消：在执行前观察并结束为 Canceled；不注册异步取消回调，因此 Capture 可能暂仍为 Queued，直至队列检查。
- scope Dispose 或模块停用：移除所属待执行任务并标记 Canceled，清除 callback；不会使其他 scope 失效。
- 回调异常：Failed / CallbackFailed；记录模块/任务/异常类型，不暴露任意异常文本，不阻断后续任务。
- 回调正常返回：Succeeded / Completed，只表示同步回调完成。远端确认、设置持久化或领域成功由业务 API 自己表达。
- 运行中取消不强行终止同步回调，不回滚已发生副作用。世界/会话依赖仍由消费者的代次/身份检查保护，本批未提供通用世界上下文服务。

## 单一队列与旧入口

ClientMainThreadDispatcher 同时实现当前任务服务与 IClientMainThreadDispatcher void adapter，只有一份队列。旧 void 入口在满队列时拒绝新动作并记录告警，不再丢最旧；DroppedCount 现在累计容量拒绝数。原签名保留，但旧消费者仍无法逐动作获取结果；不会据此宣称所有旧通知/交付消费者已迁移。现有关键业务回调需逐项迁移，不能用日志替代物品恢复事实。

Host 设置 module types，在同一 lifecycle observer 组合内处理 task scope。模块停止和部分激活失败均撤销 scope；完成的 handle 只保留状态/模块 ID 等数据，释放 callback/owner/token 引用。主线程绑定/执行受 UnityData.IsInMainThread 检查。

## Example 1.0.4 本地候选

普通 Example Activate 绑定 task scope，ExampleState 拥有并 Stop/Dispose；借用服务不释放。Count a click 的已存在设置行为保留，额外日志回调改用 Submit，页面展示最后一次任务状态。增加预取消/失败按钮，仅示范无业务回调，失败按钮会产生预期 CallbackFailed 诊断。

状态通常会从 Queued 很快变为终态；Running 是同步执行过程，IMGUI 不一定能观察到，不能为展示人为阻塞主线程。scope 不提供容器、旧版本/模式/schema。

候选在 Phinix/local-plugin-packages/2026-10-11-api/example-1.0.4.zip，要求本批完整当前客户端，不能混装早期 T3 的 SDK/Host。该阶段 API 2.0 尚未正式发行，API/文件版本仍为 2.0、CLR 身份仍为 1.9；普通示例不支持旧 1.9 Host，两个维护插件内部 adapter 保留。

现有 Example fixture 只对齐新增借用服务和 scope mock，不新增测试场景、构建或运行。当前已有独立作者实际消费；其他内置消费者尚未接入任务结果，属于后续推广事项；用户最新已撤回“每项新增 API 必须内置消费”的门槛。

## 游戏操作说明（尚未执行）

1. 整体部署当前客户端、安装 Example 1.0.4 本地候选并重启；确认模块激活。
2. 打开 Example，点击计数，观察最后回调完成与日志；设置计数行为与回调状态区分。
3. 演示取消应显示 RequestCanceled，回调不执行；演示失败应显示 CallbackFailed，并有预期诊断，后续计数/回调仍可用。
4. 原主题、语言、提示设置和重启保留计数继续可用；停用/部分激活失败应释放 scope。不得为观察 QueueFull 对真实游戏队列做大量压入。

本批编译/包静态检查通过，没有新增/运行自动化或游戏测试，没有同步 Mods 或线上发布。交接标准见 UI-Evaluation-Handoff-Checklist.md。
