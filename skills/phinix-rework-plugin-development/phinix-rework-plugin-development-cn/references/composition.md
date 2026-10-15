# 组合、生命周期与所有权

实现模块入口或管理资源时读。声明与程序集位置见 [api-cheat-sheet.md](api-cheat-sheet.md)。

## 组合入口

当前客户端入口继承 `ClientExtensionModule`，重写 `ExtensionId` 和 `Compose(IExtensionBuilder builder)`。`ExtensionId` 与 `[PhinixExtension("id")]` 一致。需要激活与清理时实现 `IActivatablePhinixExtensionModule` 的 `Activate(ExtensionHostContext)`、`Shutdown(ExtensionHostContext)`。Common 的共享 Register 契约仍存在，但只实现旧 Register 的入口不满足当前客户端发现规则。

构造函数保持被动。Compose 从 `builder.HostContext` 取得 `IClientCompositionFactory`，调用 `CreateScope(local => ...)` 建立私有作用域：用 `Borrow` 引入宿主服务，用 `Register<TService,TImplementation>()` 注册私有实现，用 `Resolve<T>()` 取得实例并通过 `builder.RegisterApi<T>(instance)` 发布公开入口。不引用 Autofac 或宿主容器实现。

Compose 失败时清理已经创建的资源再抛出；Shutdown 容忍部分初始化和重复调用。最小实现见 [模板](../templates/minimal-plugin/ExampleMinimalExtension.cs)。

## 谁负责清理

| 对象 | 所有者与清理 |
| --- | --- |
| 借入的 settings、theme、API directory 等宿主服务 | 宿主拥有；插件不 Dispose，Borrow 不转移所有权 |
| 私有 composition scope | 插件拥有；Shutdown 时 Dispose |
| 私有 scope 注册的实例 | 资源实例提供同步 IDisposable 清理；无资源的普通 State 不必实现 IDisposable。仅支持异步释放的实例不适用 |
| ForModule 创建的 localizer、action scope、task scope | 调用方拥有；按实际契约 Dispose，避免重复释放 |
| RegisterModule 返回的主题注册句柄 | 调用方拥有；Commit 发布，Dispose 撤销 |
| 事件订阅 | 订阅方对称退订；不要等待退出游戏才清理 |
| presentation session 与 action set | session 由调用方释放；Publish 成功才接管传入 action set，失败仍由调用方清理 |

Task scope 提交的是主线程同步动作。关闭 scope 可取消排队工作，不能中止已运行的回调或撤销其业务效果；不是“自动取消所有后台任务”。

## 分清四种生命周期

- 模块：发现、Compose、Activate、Shutdown。初始化失败也可能进入清理。
- 连接：断线不是模块关闭。清理连接临时状态，保留已经发出且结果未知的操作记录。
- 存档：返回主菜单、切档时释放旧 Thing/Pawn/Map 引用；延迟回调重查当前世界与代次。
- 业务操作：用稳定操作 ID 和权威回执推进。关闭视图不等于取消业务或自动返还物品。

跨插件服务通过当前 provider 的 `TryGet(out api)` 获取，不永久缓存失效实例。回调完成、任务完成和业务确认分别处理；详见 [verification.md](verification.md)。
