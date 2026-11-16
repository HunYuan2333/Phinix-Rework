# Client Compatibility Boundaries and Recovery Constraints

> **中文版**：[兼容边界与故障恢复约束.md](./兼容边界与故障恢复约束.md)

## 2026-11-10: Shared SQLite and Bounded Recovery

Because this is a game mod, rare interrupted operations may be abandoned with a visible warning rather than attempting financial-grade automatic recovery. Inventory authority has moved to a cross-save local SQLite database; legacy saves are imported once and their old fields are cleared on normal save. Per-path journal files, save-balance rollbacks, and mandatory per-incident recovery are no longer required for the new write path. An unreadable database fails visibly without fabricating success; a corrupted historical trade record isolates only that operation.

Inventory contract v7 adds `Abandoned`/`Abandon`: explicit abandonment consumes the reserved quantity, never asserts remote success, and never authorizes automatic refunds. Normal active reservations are not abandoned automatically. Existing wire protocols and receipt correlations remain intact, and physical `Game` lifecycle ownership cannot be transferred to a different world via cross-save sharing. `IInventorySavedReceiptApi` keeps its historical name while returning committed SQLite deposit receipts; `SaveIdentity`/`JournalIdentity` map to the database scope/identity, and the live `Game` is validated by an independent scope.

> P3 business branch (2026-11-04): The server may opt into `trade_basic_terms_v1` for trades involving legacy peers. On the first encounter per connection, the client displays a main-thread notice about property loss and legacy delivery limits without switching protocols or confirming trades.

This document constrains the Rework client when connecting to legacy servers (`Phinix/Common/Trading/ClientTrading.cs` and `ServerTrading.cs`) and Framework servers.

## Responsibility Boundaries

| Layer | Owns | Must Not Own |
| :--- | :--- | :--- |
| Legacy protocol adapter | Legacy modules/packet types, field directions, default UUIDs, legacy item formats, request/receipt correlation, capability limits | Prematurely confirming trades, silently dropping items, altering core success/failure semantics |
| Framework transport adapter | New protocol packets, item references, mapping send results to domain outcomes | UI state or map item ownership |
| Trade core | Escrow of pending items, operation state, post-confirmation commit/recovery, idempotent delivery | Parsing legacy packets or changing business success criteria based on Legacy/V2 |
| Item core | Codecs, preserving undecodable data, integrity checks | Silently filtering out unsupported items to continue trading |
| UI | Displaying state, submitting user intent, exception-safe cache/GUI cleanup | Deciding protocol, treating local prediction as server confirmation |

Compatibility code belongs in explicit interfaces, data readers/migrations, and external system adapters (not limited to `Extensions/LegacyAdapter/Client`). New business logic and UI must consume current contracts without legacy awareness (see [Compatibility Interface Boundary Audit](branch-local/dev/Compatibility-Interface-Boundary-Audit.md)).

## Invariants

1. Routine snapshot updates, optimistic previews, and operation acknowledgements are distinct events; writing to a repository cannot confirm a request.
2. Only an authentic, request-correlated success receipt commits escrowed items; duplicate or stale receipts never re-commit or refund.
3. Explicit rejection or confirmed non-dispatch may restore items; a sent request with a lost receipt is `Unknown` and must not auto-refund (to prevent duplication), instead remaining recorded for reconciliation.
4. Item conversion is all-or-nothing; unsupported codecs, corrupt payloads, or missing references cannot become partial success.
5. In legacy `UpdateTradeItemsResponsePacket`, `Success/Token/Items` represent the offer operation result; `UpdateTradeItemsPacket` is the receiver view (`Items` = self, `OtherPartyItems` = peer), and empty lists are valid empty offers.
6. Send failures must propagate explicitly to callers; `Handled` means processed, not succeeded.
7. Unsupported legacy server features are explicitly rejected or downgraded with notice by the adapter; UI never silently switches protocols.
8. Stale caches are cleared only after rebuild succeeds; locks, scroll regions, and GUI state must restore on exception paths.
9. Async exception handling lives where the action executes; logs do not replace pending delivery records or recovery.

## Legacy Integration, Inventory & Delivery Boundaries

- **Legacy Trade & Item State**: Removed optimistic local success after send; legacy `Success`/`Failure` receipts normalize into core operation outcomes. State payloads (`core.item.scribe-v1`) are preserved in legacy `InnerProtoThing` sentinel nodes inside `Extensions/LegacyAdapter/Client` and must match visible previews without falling back to blank `ThingMaker.MakeThing` items.
- **Inventory Reservation & RedPacket**: Trade reserves inventory atomically via `IInventoryReservationApi`, materializes unspawned temporary copies, converts them, and destroys the temporaries immediately. LegacyRedPacket reserves selected units or whole stateful entries and transfers custody only on HTTP `create` acceptance or matching relay history echo.
- **Legacy Journal Recovery**: Per-save-path journals tolerate historical sequence gaps before the loaded snapshot while validating checksums, ledger state, and strictly increasing sequences; newer records (`snapshot + 1` onward) require user approval before restoration, and crash-truncated tails are backed up and isolated after snapshot verification. See [Inventory.md](./Inventory.md).
- **RimWorld 1.7 / API 3 & Presentation Boundaries**: Theme (`IClientTheme`, `ClientRgba`), shared presentation (`ClientPresentation.Contracts`), and Trade/Inventory game-object boundaries remain isolated in their respective adapters. Host stays neutral and never references Trade/Inventory domain internals.
- **Trade Completion Inbox, Attempt Journal & Scopes**: Terminal trade events persist to `ExtensionData` before delivery callbacks (keyed by endpoint/recipient/trade). `Attempted` is appended before calling local delivery, and `ReturnedSuccess`/`ReturnedFailure` afterward; Game and Inventory scopes captured at receive time are re-verified before delivery so items are never delivered into a different game or save. See [P3–P6 Closeout](branch-local/dev/P3-Closeout-2026-10-29.md).

## Verification (`Tests/LegacyTradeRuntimeTests`)

Verified 9 in-memory transport scenarios against compiled adapters, trade services, and protobuf packages (built for `net472` and executed under `.NET 10`):

```powershell
MSBuild Tests/LegacyTradeRuntimeTests/LegacyTradeRuntimeTests.csproj /t:Build /p:Configuration=Release /p:MSBuildEnableWorkloadResolver=false /p:SolutionDir=<repository-root>/
dotnet exec --runtimeconfig Tests/LegacyTradeRuntimeTests/runtimeconfig.json Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
```

Do not modify vendored protobuf `global.json`; override `MSBuildSDKsPath` in the current process if needed.
