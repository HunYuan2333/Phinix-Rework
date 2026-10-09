# 中立 DI、生命周期与资源所有权

实现模块或服务时读。签名依据见 [basis.md](basis.md)，实现前核对目标版本 `IClientComposition.cs`、`FrameworkTypes.cs` 与现代 Example，不复制旧入口推荐给新作者。

## 最小组合

公开入口：`PhinixClient.Framework.ClientExtensionModule` 的 `ExtensionId` 和 `Compose(Utils.Framework.IExtensionBuilder)`。需要激活/关闭时实现 `Utils.Framework.IActivatablePhinixExtensionModule` 的 `Activate(ExtensionHostContext)`、`Shutdown(ExtensionHostContext)`。模块标记 `PhinixExtension` 的 ID 与 ExtensionId 一致。

在 Compose 中通过 `builder.HostContext.GetRequiredService<IClientCompositionFactory>()` 创建 scope；`CreateScope(Action<IClientCompositionBuilder>)` 提供 `Borrow<T>(instance)` 与 `Register<TService,TImplementation>()`。在组合边界 `scope.Resolve<T>()`，然后 `builder.RegisterApi<T>(instance)` 发布 UI/契约。业务/UI 类用构造注入，不到处 Resolve，不引用 Autofac 或宿主具体实现。

Example 的已核对模式：借用 `IClientSettingsContext`、`IClientMainThreadDispatcher` 和日志委托；注册一个 `ExampleState`，Tab 与 settings provider 都注入这一个状态。构造只存依赖。Compose 注册失败释放已创建 scope；Activate 获取 `IClientLocalizationService.ForModule(this)` 并 Start；Shutdown 先清空持有字段，再 Dispose 自有 scope。Shutdown 对未完成激活、多次调用都可用。更复杂清理应防止清理异常掩盖原始错误。

## 所有权

| 对象 | 所有者与清理 |
| --- | --- |
| HostContext 服务、跨插件 API、DI Borrow 值 | 宿主/提供者拥有，消费者不 Dispose |
| 自建 DI scope、注册的可释放实例 | 插件拥有，scope 同步 Dispose；不注册仅异步释放的所有权资源 |
| `ForModule(this)` 返回的 `IClientLocalizer` | 插件 scope 对象拥有：先取消 LanguageChanged，再 Dispose；宿主 localization service 不释放 |
| 库存 scoped codec/source registration 返回的 IDisposable | 注册者保存并在关闭时 Dispose；不释放库存 API |
| 本插件订阅、线程、timer、流、取消源 | 成对退出/释放，停止产生新回调后释放依赖 |

## 四种生命周期

- 模块：发现 → Compose → Activate → Shutdown。部分 Compose/Activate 失败也可能调用 Shutdown。
- 连接：Disconnected 等事件不是模块 Shutdown；结束连接相关请求/订阅，保留未知结果记录。
- 存档：切档/回主菜单不会重建所有模块；释放旧世界引用，按当前存档重新取得能力和恢复状态。
- 操作：独立稳定 ID、上下文和提交状态，不能随窗口关闭丢掉托管物品。

Example 的 `Stop()` 增加 generation、取消翻译事件并释放 localizer；排队回调和确认窗口捕获 generation，执行时检查 Active 与相同 generation，避免旧激活修改设置。涉及世界/联机的插件还须检查当前 world/save/account/session/connection，单个 generation 不自动覆盖这些维度。

`IClientMainThreadDispatcher.Enqueue(Action)` 只负责排队；游戏对象、库存操作及 GUI 必须在主线程动作内部重查上下文并捕获异常。停止后过期回调不执行副作用。设置 `Get<T>/Set<T>` 没有自动隔离，使用包/模块唯一键前缀与 SectionId。

传统 `IPhinixExtensionModule.Register` 是兼容过渡入口，目标 1.0/abstractions 2.0 的移除仍取决于迁移门槛。Example 工作区已迁移；红包/人才贸易保持传统入口，不能因此要求新插件照抄它们。
