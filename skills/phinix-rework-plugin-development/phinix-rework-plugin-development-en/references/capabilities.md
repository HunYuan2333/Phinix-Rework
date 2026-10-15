# Selecting public capabilities

This reference explains composition; see [api-cheat-sheet.md](api-cheat-sheet.md) for precise call shapes.

## Presentation and controlled drawing

Create a session with `IClientPresentationService.Open(module, pageId)`. Consume TryDequeueInput, update plugin state, then Publish changed projections and actions. Draw uses current bounds. Do not rebuild action sets on every IMGUI Repaint. Publish advances input revisions; updates or closure can invalidate earlier input, which is not current business authorization.

The public renderer owns layout, theming, actions and confirmations. A controlled region implements `IClientPresentationRegion.Measure(float, IClientThemeReader)` and `Draw(IClientPresentationDrawContext)`, wrapped by `ClientPresentationRegion(id, content)`. Borrow the draw context only for the current draw, using neutral Bounds, Theme and Progress. It is not a backdoor to native events, arbitrary GUI, input or actions.

For native Window/IMainTabProvider implementations, use public ClientWidgets and ClientGuiScope to restore GUI state. Clamp native windows through `UiScreenSafeArea.ClampWindow(Rect, Vector2)` or its explicit-safeRect overload, not every child control. Custom native scroll areas can use ScrollbarDecoration after EndScrollView; do not redecorate regions already handled by the public renderer. Evaluate shared renderer capabilities before adding new presentation; no official-only renderer.

## Actions and confirmations

Use `IClientExtensionPointService.ForModule(module)` to define or contribute typed actions. ClientPresentationAction references a contributed action and can include `ClientPresentationConfirmation(title, text, acceptLabel, cancelLabel)` for unified Host confirmation.

ClientActionExecutionResult.Completed reports local action execution, not server or peer acknowledgement. Buttons, confirmation dialogs and view closure do not own plugin transactions, escrow or ledgers.

## Themes and localization

Read semantic IClientTheme values; convert with ToUnityColor() only at native drawing boundaries. Register custom tokens through IClientThemeRegistry.RegisterModule, RegisterColor/RegisterFloat and Commit; Dispose the registration at shutdown. Use a unique namespace and shared fallbacks rather than a second theme system.

IClientLocalizationService.ForModule(module) returns a module localizer. Use Text/Format, handle LanguageChanged and release the handle. Do not freeze translations during Compose. See [theme-package.md](theme-package.md) for data-only packages; retain Host editing, live apply and export capabilities.

## Messages, commands and items

Register message handlers through IClientMessageHandler and builder.AddClientMessageHandler; send with IFrameworkClientTransport.TryHandleOutgoingMessage. Commands use discovered IClientOutgoingCommandHandler implementations and IFrameworkClientCommandTransport.TryHandleOutgoingCommand. Items use TryHandleOutgoingItem. Do not bypass framework pipelines through raw transports.

true/Handled indicates handling or routing, not guaranteed enqueueing, persistence or business confirmation. Interpret the actual handler result.

## Inventory boundaries

Declarations are in `Extensions/Inventory/Contracts/InventoryContracts.cs`:

| Need | Entry and constraint |
| --- | --- |
| Read inventory | IInventoryReadApi.GetSnapshot/GetStatus/GetCapabilities; InventoryChanged and AvailabilityChanged events |
| Deposit | IInventoryDepositApi.CheckDeposit/TryDeposit; stable DepositId supports idempotency, so retries retain the ID |
| Reservation and delivery | IInventoryReservationApi.TryReserve, MaterializeReservation and ResolveReservation; authoritative business acknowledgements decide commit/release, while unknown results remain reconcilable |

Translate and deliver items as an entire batch or fail the entire batch. Missing Defs, codecs or dependencies preserve the original payload and report rejection. Do not filter invalid items, create blank Things or transfer offer/Pawn business ownership to Host.
