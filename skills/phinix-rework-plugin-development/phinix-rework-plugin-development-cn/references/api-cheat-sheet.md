# 客户端公共 API 速查

本表核对本地实际源码，不表示这些工作区修改已经推送 main。路径相对 Host 仓库。Framework 命名空间为 `PhinixClient.Framework`；另有说明者除外。返回值与复杂参数以声明文件为准，不据方法名猜测重载。

程序集简称：A = `ClientExtensionAbstractions.dll`，P = `ClientPresentation.Contracts.dll`，U = `Utils.dll`。A 仍含 Unity/Verse 边界，不能宣称整个 SDK 无引擎依赖。所有呈现、注册、本地化和主题变更按游戏主线程使用；后台只处理不依赖游戏对象的数据。

| 需求 | 声明/调用形状 | 程序集 | 使用与所有权 | 源码定位 |
| --- | --- | --- | --- | --- |
| 模块入口 | `ClientExtensionModule.Compose(IExtensionBuilder builder); string ExtensionId { get; }` | A | Compose 只组合依赖；构造函数不启动工作。 | `Client/ClientExtensionAbstractions/Framework/IClientComposition.cs` |
| 生命周期 | `IActivatablePhinixExtensionModule.Activate(ExtensionHostContext hostContext); Shutdown(ExtensionHostContext hostContext);` | U | 按模块激活代管理工作，Shutdown 容忍部分初始化并释放自有资源。 | `Dependencies/Phinix.Common/Common/Utils/Framework/FrameworkTypes.cs` |
| 私有 DI | `IClientCompositionFactory.CreateScope(Action<IClientCompositionBuilder> configure); IClientCompositionScope.Resolve<T>() where T : class;` | A | scope 自有；Borrow<T>(T instance) 借入不释放；Register<TService,TImplementation>() 注册自有对象。 | `Client/ClientExtensionAbstractions/Framework/IClientComposition.cs` |
| 发布 API | `IExtensionBuilder.RegisterApi<T>(T implementation) where T : class;` | U | Compose 中注册公开端点，实例由提供方管理。 | `Dependencies/Phinix.Common/Common/Utils/Framework/FrameworkTypes.cs` |
| 发现 API | `IClientApiDirectory.Capture<T>() where T : class; IClientApiProvider<T>.TryGet(out T api);` | A | Capture 返回 IReadOnlyList<IClientApiProvider<T>>；TryGet 重新核对当前注册，结果为借用引用。 | `Client/ClientExtensionAbstractions/Framework/IClientApiDirectory.cs` |
| 原生主页签 | `IMainTabProvider: string TabLabel { get; } float TabOrder { get; } void Draw(UnityEngine.Rect inRect);` | A | 类型命名空间为 PhinixClient；原生 Draw 使用 ClientGuiScope 和公共控件。 | `Client/ClientExtensionAbstractions/UI/IMainTabProvider.cs` |
| 视图生命周期 | `ClientPresentationMainTab(IMainTabProvider provider, Action viewOpened, Action viewUpdate, Action viewClosed, string navigationId);` | A | 五个参数均必需；Update 处理输入/投影，Draw 仅绘制；关闭视图不取消业务。 | `Client/ClientExtensionAbstractions/UI/ClientPresentationMainTab.cs` |
| 呈现会话 | `IClientPresentationService.Open(IPhinixExtensionModule module, string pageId);` | P | 返回自有 IClientPresentationSession；主线程打开、使用、关闭。 | `Client/PresentationContracts/UI/IClientPresentationService.cs` |
| 发布/输入 | `IClientPresentationSession.Publish(ClientPresentationPage page, IClientActionSet actions, Func<bool> isCurrent); Draw(ClientPresentationBounds bounds); TryDequeueInput(out ClientPresentationInput input);` | P | Publish 成功后转移动作集所有权；失败仍由调用者释放。每次新建集合；只读页可传 null。输入先更新状态再发布，验证选择重建后仍存在。 | `Client/PresentationContracts/UI/IClientPresentationService.cs` |
| 受控自绘 | `IClientPresentationRegion.Measure(float width, IClientThemeReader theme); Draw(IClientPresentationDrawContext context);` | P | Measure 返回 float。借用 context 只在当前 Repaint 有效；仅被动绘制，不处理输入、订阅或原生资源。 | `Client/PresentationContracts/UI/IClientPresentationService.cs` |
| 动作 | `IClientExtensionPointService.ForModule(IPhinixExtensionModule module); IClientExtensionPointScope.DefineActions<TContext>(string pointId) where TContext : class;` | P | 返回 scope 自有；ContributeActions<TContext>(string pointId, Func<TContext,IEnumerable<ClientActionDefinition>> factory, float order = 0) 贡献动作。 | `Client/PresentationContracts/Framework/IClientExtensionPoints.cs` |
| 收集/确认 | `IClientActionPoint<TContext>.Collect(TContext context, Func<bool> isContextCurrent = null); IClientContributedAction.TryExecute();` | P | Collect 返回自有 IClientActionSet。将其中动作包装为 ClientPresentationAction，附 ClientPresentationConfirmation；Completed 不是业务回执。 | `Client/PresentationContracts/Framework/IClientExtensionPoints.cs` |
| 主线程任务 | `IClientTaskService.ForModule(IPhinixExtensionModule module); IClientTaskScope.Submit(Action action, CancellationToken cancellation = default);` | A | scope 自有；Submit 返回 IClientTaskHandle，Capture() 观察状态。它不是 Task.Run，已开始回调不强制中止或回滚。 | `Client/ClientExtensionAbstractions/Framework/IClientTaskService.cs` |
| 本地化 | `IClientLocalizationService.ForModule(IPhinixExtensionModule module); IClientLocalizer.Text(string key, string fallback = null); Format(string key, params object[] arguments);` | A | Activate 获取并拥有 localizer，Shutdown 释放；按当前语言解析，不在 Compose 固化标签。 | `Client/ClientExtensionAbstractions/Framework/IClientLocalizationService.cs` |
| 主题读取/注册 | `IClientThemeReader.GetColor(string key); GetFloat(string key, float defaultValue = 0f); IClientThemeRegistry.RegisterModule(IPhinixExtensionModule module, string keyNamespace = null, string ownerLabel = null);` | P | GetColor 返回 ClientRgba；Changed 在 IClientTheme。注册句柄自有，RegisterColor/RegisterFloat 后 Commit，停止时释放。 | `Client/PresentationContracts/UI/IClientTheme.cs` |
| 编辑/导出主题 | `IClientThemeCatalog.Edit(string themeId); Apply(ClientThemeDraft draft); Export(ClientThemeDraft draft, string path); IClientThemeWindowService.OpenThemeWindow();` | P | Edit 返回草稿，Apply 才应用；Host 维护编辑、热应用和导出，插件不另建主题系统。 | `Client/PresentationContracts/UI/IClientTheme.cs` |
| 主题包管理 | `IClientThemePackageService.Inspect(string absolutePath); Install(ThemePackage package, string expectedInstalledArtifact); Uninstall(string packageId, string expectedInstalledArtifact);` | P | 借用服务，主线程调用。Inspect 返回 ThemePackage；安装不自动应用；expectedInstalledArtifact 防止覆盖过期安装。 | `Client/PresentationContracts/UI/IClientThemePackages.cs` |
| 环境/路径 | `IClientEnvironmentService.Capture(); ClientEnvironmentPaths.GetExtensionDataDirectory(string extensionId);` | A | Capture 返回 ClientEnvironmentSnapshot，主线程读取；使用宿主路径服务，不硬编码存储根目录。 | `Client/ClientExtensionAbstractions/Framework/ClientEnvironmentSnapshot.cs` |
| 原生安全区 | `UiScreenSafeArea.ClampWindow(Rect windowRect, Vector2 minimumSize); ClampWindow(Rect windowRect, Rect safeRect, Vector2 minimumSize);` | A | 命名空间 PhinixClient；没有 margin 重载。Rect/Vector2 来自 UnityEngine。 | `Client/ClientExtensionAbstractions/UI/UiScreenSafeArea.cs` |

## 不存在或不属于公共 API 的入口

不要使用 `Compose(ContainerBuilder, IClientExtensionContext)`、`IClientApiDirectory.TryResolveApi/GetRequiredApi/ResolveApis`、`IClientTaskScope.Run`、`IClientLocalizer.Translate` 或 `IClientPresentationRegionContext`。当前分别使用上表的 Compose、Capture/TryGet、Submit、Text/Format 和 IClientPresentationDrawContext。

`ManagedStorePlanner`、`UiTheme`、`ClientThemePackageService` 是内部实现，不作为第三方插件引用。主题值在原生边界经 `ClientColorValues.ToUnityColor(ClientRgba)` 转换，扩展方法位于 A 的 `Game/ClientColorValues.cs`。

## 最小示例与完整签名

- [可编译的原生页签模板](../templates/minimal-plugin/ExampleMinimalExtension.cs)；项目和本地验证见 [local-loop.md](local-loop.md)。
- 公共呈现、动作和完整注册示例：同工作区 `Phinix-Example-Plugin/example/ExamplePresentation.cs`、`ExampleActions.cs`、`ExampleTheme.cs`。不存在 `ExampleState.cs` 独立文件时，在 `ExampleExtension.cs` 查类声明。
- `RegisterColor(string key, ClientRgba defaultColor, string label = null, string fallbackKey = null, string group = null, string description = null)`。
- `RegisterFloat(string key, float defaultValue, float minimum = 0f, float maximum = 1f, string label = null, string group = null, string description = null)`。
- `ClientPresentationAction(string id, IClientContributedAction action, ClientButtonKind kind = ClientButtonKind.Normal, ClientPresentationConfirmation confirmation = null)`。
- `ClientPresentationConfirmation(string title, string text, string acceptLabel, string cancelLabel)`。

库存、消息和游戏业务另查 [capabilities.md](capabilities.md)，不把商店内部规划器当作业务 API。
