# Composition, lifecycles and ownership

Read when implementing module entry points or managing resources. See [api-cheat-sheet.md](api-cheat-sheet.md) for declarations and assembly locations.

## Composition entry point

Current client entries derive from `ClientExtensionModule`, overriding `ExtensionId` and `Compose(IExtensionBuilder builder)`. Match `ExtensionId` to `[PhinixExtension("id")]`. For activation and cleanup, implement `IActivatablePhinixExtensionModule.Activate(ExtensionHostContext)` and `Shutdown(ExtensionHostContext)`. Common still defines the shared Register contract, but a Register-only entry does not satisfy current client discovery rules.

Keep constructors passive. In Compose, obtain `IClientCompositionFactory` from `builder.HostContext` and create a private scope with `CreateScope(local => ...)`: Borrow host services, Register private implementations, Resolve instances and publish endpoints with `builder.RegisterApi<T>(instance)`. Do not reference Autofac or the host container implementation.

On Compose failure, clean up acquired resources before rethrowing. Shutdown tolerates partial initialization and repeated calls. See the [minimal template](../templates/minimal-plugin/ExampleMinimalExtension.cs).

## Cleanup ownership

| Object | Owner and cleanup |
| --- | --- |
| Borrowed host settings, theme, API directory and similar services | Host-owned; plugins do not Dispose them. Borrow transfers no ownership |
| Private composition scope | Plugin-owned; Dispose during Shutdown |
| Instances registered in that private scope | Resource-owning instances provide synchronous IDisposable cleanup. Resource-free State classes need no IDisposable; async-only disposal is unsuitable |
| Module localizer, action scope and task scope from ForModule | Caller-owned; Dispose according to their contracts without double disposal |
| Theme registration from RegisterModule | Caller-owned; Commit publishes, Dispose withdraws |
| Event subscriptions | Subscribers unsubscribe symmetrically; do not wait for game exit |
| Presentation session and action set | Caller disposes session. Publish transfers action-set ownership only on success; caller cleans up after failure |

A task scope submits synchronous main-thread actions. Closing it can cancel queued work, but cannot abort a running callback or undo business effects. It does not automatically cancel all background tasks.

## Four separate lifecycles

- Module: discovery, Compose, Activate and Shutdown. Failed initialization can also require cleanup.
- Connection: disconnect is not module shutdown. Clear connection-local state while retaining sent operations with unknown results.
- Savegame: release old Thing/Pawn/Map references on return to menu or save changes. Delayed callbacks recheck the current world and generation.
- Business operation: advance through stable operation IDs and authoritative acknowledgements. Closing a view does not cancel business or return items automatically.

Acquire cross-plugin services through the current provider's `TryGet(out api)` rather than retaining expired instances. Callback completion, task completion and business confirmation are distinct; see [verification.md](verification.md).
