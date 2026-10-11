# Provider 查询与模块生命周期作用域

日期：2026-10-11。当前 API 实验批次，SDK/API 仍为 2.0，CLR 身份仍为 1.9；线上发行延期。Example 1.0.6 展示用法，未运行游戏或自动化测试。

## Provider 查询

IClientApiDirectory.Capture<T>() 在主线程返回精确契约类型的不可变 provider 信息；空集合就是没有已注册 provider。数据来自现有 ExtensionApiRegistry，未引入第二套注册器。

- OwnerId、RegistrationId 和 State 是捕获时的元数据。状态区分 Available、Registered、Unavailable；注册不是激活成功。需要激活的模块仅实际 Active 时可用，无激活阶段的模块在 Registered 时可用。
- TryGet(out T) 每次重新核对实际模块状态和精确注册 ID，返回的是借用 API，不转移所有权。注销后旧 handle 失效；同一实例重新注册产生新 ID，不使旧 handle 复活。
- 多 provider 保持实际注册顺序。调用方按所属业务契约选择，不把所有 provider 盲目合并，也不将缺可选 provider 解释成兼容模式。
- Capture/TryGet 都在主线程；这是当前注册/可用状态查询，不代表网络业务已确认或模块各业务功能全部健康。已取得的普通 API 引用本身不能被强制撤销；调用方应在操作前重新查询/获取，并遵守业务自己的结果语义。
- 本批没有新增状态订阅；按打开/刷新时查询。Host 停止后目录 handle 不能获取服务。普通插件不访问 private registry 或生命周期内部状态。

## 模块拥有的作用域

IClientLifetimeService.ForModule(actualModule) 在真实 Activate 阶段返回独立 owned IClientLifetimeScope；模块停止及部分激活失败后的 observer 清理关闭所有作用域。

| 能力 | 语义 |
| --- | --- |
| OwnerId / IsClosed | 来自实际模块及当前 scope，不接受伪造 owner 字符串 |
| Stopping | scope 关闭时取消的 token；不声称取消已经完成的业务副作用 |
| Own(IDisposable) | 转移自有同步资源；返回可提前释放的 lease；不允许传入借用 Host 服务/API |
| Subscribe(add, remove, callback) | 主线程绑定/移除，拥有精确 guarded delegate；关闭/释放后迟到 callback 不调用业务 |
| Dispose | 先标记关闭及取消，再逆序释放；幂等；一个释放异常不阻止其他释放 |

每模块最多 16 个 scope，每 scope 256 个拥有资源，全局 1024 个。订阅失败尝试移除可能部分绑定的 delegate；绑定过程中重入关闭也补做移除。回调与释放异常仅记录 owner/阶段/异常类型，回调恢复 GUI 状态；错误线程的回调不调用业务。额度拒绝时新资源的所有权仍由调用方负责。

作用域操作/释放在主线程。后台工作可以观察 token，然后通过已有任务 API 回到主线程；没有新增文件路径权限、异步资源处置或网络重试。此 API 是拥有资源与订阅的基础，不等同已经完成模块文件存储/资产读或全部内置业务 API。

## Example 1.0.6

普通 DI 借用目录与 lifetime 服务，Compose 不获取活动 scope；Activate 绑定实际模块。Tab 的 provider/lifetime 按钮打开独立实验窗口：

1. 查询主题 provider、自身 Tab provider 与刻意未注册的 optional contract。
2. 打开时拥有演示资源，订阅演示事件与主题 Changed；显示回调次数、释放次数和取消状态。
3. 关闭 scope 后再次触发保留的迟到 delegate，guard 不调用业务；重绑使用新 scope/token。窗口关闭立即释放，模块停止同样清理。

这只是 API 语义实验，没有真实交易/物品/远端操作。状态快照不会被当作权威写入口；示例不含 1.9 判断。维护插件已有内部 adapter 保留。

后续继续模块资源/存储及更多 typed 内容贡献/内置高层操作，沿用 Plugin-Api-Integration-Standard.md；每项可在 Example 演示，不强制等待内置消费者。
