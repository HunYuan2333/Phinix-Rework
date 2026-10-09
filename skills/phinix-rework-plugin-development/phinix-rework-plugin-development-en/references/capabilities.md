# Choose public capabilities by feature

Read only the section relevant to the feature being implemented. Locate the full manuals/source through [basis.md](basis.md). This is a navigation and semantics guide, not a complete API catalog.

## UI, settings, and localization

`PhinixClient.IMainTabProvider`: `TabLabel`, `TabOrder`, `Draw(Rect)`; publish through builder.RegisterApi. Add `IResponsiveMainTabProvider.LayoutHints` for responsive layout. Settings use `PhinixClient.Framework.IClientSettingsPanelProvider`: SectionId, Order, IsVisible, DrawSettings; verify signatures against the target interface. For sidebars, badges, banners, or Enter handling, inspect `IServerSidebarProvider`, `IBadgeProvider`, `INoticeBannerProvider`, and `IUiAcceptKeyHandler` respectively.

Use public `UiScreenSafeArea.ClampWindow`, `ResponsiveFormLayout`, `ResponsiveSplitLayout`, `ResponsiveToolbarLayout`, and `VirtualListLayout`. Normalize is internal and cannot be called. Toolbar computes geometry; callers draw controls and build the overflow menu. Draw must not create many Things or retain old world caches. Restore shared font, alignment, wrapping, GUI.color/GUI.enabled, and similar state in finally. Verify narrow windows, scrolling, language switching, and settings retention.

Package-scoped `Resources/Localization/*.json` is declared by the packaging tool. Follow the target Example's key structure and `--language-file`. Obtain the localizer via `IClientLocalizationService.ForModule(this)` and clean it up according to [ownership](composition.md).

## Message, command, and item pipelines

Common `Utils.Framework.IClientMessageHandler` handles outgoing text/incoming messages; register with `IExtensionBuilder.AddClientMessageHandler`. Items use AddClientItemHandler/`IClientIncomingItemHandler` and AddClientOutgoingItemHandler/`IClientOutgoingItemHandler`. Incoming `IClientCommandHandler` and outgoing `IClientOutgoingCommandHandler` are orthogonal. Use AddClientCommandHandler for incoming commands. The current host selects IClientOutgoingCommandHandler from discoveredExtensions.Extensions, so the discovered module must implement outgoing handling, as Trade does. There is no AddClientOutgoingCommandHandler; registering a service/API alone does not guarantee pipeline participation. Check discovery logic in the target version.

Public outgoing entry points:

- `IFrameworkClientTransport.TryHandleOutgoingMessage(string)`;
- `IFrameworkClientTransport.TryHandleOutgoingItem(FrameworkItemPayload)`;
- `IFrameworkClientCommandTransport.TryHandleOutgoingCommand(FrameworkPacket)`.

Handled/true is not authoritative domain confirmation. Verify actual results, send-failure propagation, and correlated acknowledgements. Do not directly use `SendFrameworkPacket` or raw Legacy transport to bypass Priority, interception, replacement, and fallback. Special legacy inbound handling belongs to the Legacy adapter, not ordinary business-plugin templates. Inspect public interfaces for compatibility mode and remote capabilities separately; do not invent a server-address field on `IClientSessionContext`. Read server code/pinned Common contracts only when adding a protocol that requires it.

## Inventory

Contracts are in client `Extensions/Inventory/Contracts/InventoryContracts.cs`; documentation is `docs/Inventory.md`. Module dependency `builtin.inventory` and managed packageId dependencies belong to different identity layers. Declare them according to actual manifests/module attributes; do not arbitrarily place module IDs in package dependencies. After API resolution, check extension activation and `GetStatus()/GetCapabilities()`. Successful TryResolveApi does not prove writability.

| Purpose | Public API/calls |
| --- | --- |
| Read status | `IInventoryReadApi.GetSnapshot/GetStatus/GetCapabilities`; pair subscription/unsubscription for InventoryChanged/AvailabilityChanged |
| Register codecs/source presentation | `IInventoryRegistrationApi.RegisterCodecScoped/RegisterSourcePresenter`; retain/dispose handles |
| Atomic deposit | `IInventoryDepositApi.CheckDeposit/TryDeposit(InventoryDeposit)` |
| Extract/schedule | `IInventoryExtractionApi.TryExtract/SetDailySchedule/RemoveDailySchedule` |
| Outgoing business custody | `IInventoryReservationApi.GetAvailableSnapshot/TryReserve/MaterializeReservation/ResolveReservation`; preview with `CreatePreview` |

Retrying the same stable DepositId/content returns AlreadyCommitted; the same ID with changed ownership content is Conflict. Release producer custody only after Committed/AlreadyCommitted; CheckDeposit does not commit. New games must be saved first. Reject writes through public capability checks for old snapshot/journal conflicts and other protected states; never bypass read-only protection.

Outgoing reservations are atomic and use persisted stable operation IDs. Commit only on matching authoritative success, restore only on proven rejection, and retain unknown outcomes locked for reconciliation. Materialize temporary copies as a whole batch, destroy them after conversion, and never spawn them or treat them as new ownership. Missing codec/Def/mod, corrupt payload, or impossible lossless restoration rejects the whole batch while retaining data; do not recreate blank Things. Handle indivisible state as whole entries, not payload rewriting based on display groups. `IInventoryItemPresentationCodec` v5 is presentation-only; its absence falls back to separate rows, without persistence/protocol changes.

Business references: RedPacket relay publication acceptance/history matching is not a Trade-server ack; HTTP queueing is not acceptance. Automatic reconciliation of interrupted history after disconnect/save changes/restart is not implemented, so no automatic refund/replay. TalentTrade uses its independent Pawn path, has no Inventory dependency, and cannot automatically recover unsettled in-memory purchases after a crash. See [basis.md](basis.md) for exact versions and confirmed game acceptance. Legacy entry points and limitations are not recommended new designs.
