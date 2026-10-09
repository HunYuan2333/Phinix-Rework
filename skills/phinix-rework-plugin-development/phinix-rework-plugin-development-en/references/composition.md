# Neutral DI, lifecycle, and resource ownership

Read while implementing modules or services. See [basis.md](basis.md) for signature evidence. Check target-version `IClientComposition.cs`, `FrameworkTypes.cs`, and the modern Example before implementation; do not recommend legacy entry points to new authors.

## Minimal composition

Public entry: `PhinixClient.Framework.ClientExtensionModule`, with `ExtensionId` and `Compose(Utils.Framework.IExtensionBuilder)`. For activation/shutdown, implement `Utils.Framework.IActivatablePhinixExtensionModule` with `Activate(ExtensionHostContext)` and `Shutdown(ExtensionHostContext)`. The `PhinixExtension` attribute ID matches ExtensionId.

In Compose, obtain `IClientCompositionFactory` through `builder.HostContext.GetRequiredService<IClientCompositionFactory>()` and create a scope. `CreateScope(Action<IClientCompositionBuilder>)` exposes `Borrow<T>(instance)` and `Register<TService,TImplementation>()`. Resolve with `scope.Resolve<T>()` at the composition boundary, then publish UI/contracts through `builder.RegisterApi<T>(instance)`. Business/UI classes use constructor injection, not scattered Resolve calls; do not reference Autofac or concrete host implementations.

The verified Example borrows `IClientSettingsContext`, `IClientMainThreadDispatcher`, and the logging delegate; registers one `ExampleState`; and injects that state into both tab and settings providers. Constructors only retain dependencies. Dispose the created scope on composition failure. Activate obtains `IClientLocalizationService.ForModule(this)` and starts the state. Shutdown clears retained fields before disposing its owned scope. Cleanup tolerates incomplete activation and repeated calls; complex cleanup must not mask the original error with a cleanup exception.

## Ownership

| Object | Owner and cleanup |
| --- | --- |
| HostContext services, cross-plugin APIs, DI Borrow values | Host/provider-owned; consumers do not Dispose |
| Plugin-created DI scope and disposable registered instances | Plugin-owned; synchronous scope disposal, no async-only ownership resources |
| `IClientLocalizer` returned by `ForModule(this)` | Owned by the plugin's scoped object; unsubscribe LanguageChanged, then Dispose; do not dispose the host localization service |
| IDisposable returned by inventory scoped codec/source registration | Registrant retains/disposes the handle at shutdown, not the inventory API |
| Plugin subscriptions, threads, timers, streams, cancellation sources | Pair exit/release; stop new callbacks before releasing dependencies |

## Four lifecycles

- Module: discovery → Compose → Activate → Shutdown. Shutdown may follow partial Compose/Activate failure.
- Connection: Disconnected and similar events are not module Shutdown. End connection work/subscriptions while retaining unknown-outcome records.
- Save: switching saves/returning to menu does not rebuild every module. Release old world references and reacquire capabilities/recovery state for the current save.
- Operation: independent stable ID, context, and commit state; closing a window must not discard custody of items.

Example `Stop()` increments generation, unsubscribes localization, and disposes the localizer. Queued callbacks and confirmation dialogs capture generation, then require Active and matching generation before changing settings. World/network features must also verify current world/save/account/session/connection; one generation counter does not automatically cover all these dimensions.

`IClientMainThreadDispatcher.Enqueue(Action)` only queues work. Game objects, inventory operations, and GUI require main-thread actions that recheck context and handle exceptions at execution. Expired callbacks perform no side effects after shutdown. Settings `Get<T>/Set<T>` is not automatically isolated; prefix keys and SectionId uniquely by package/module.

Legacy `IPhinixExtensionModule.Register` is a transitional compatibility entry. Removal at host 1.0/abstractions 2.0 remains subject to migration gates. The Example workspace has migrated; RedPacket/TalentTrade retain legacy entry points, which are not templates for new plugins.
