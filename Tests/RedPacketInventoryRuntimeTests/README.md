# Red-packet inventory regression harness

This game-independent executable uses compile references to RimWorld/Unity but does not initialize a game or contact the live relay. It covers atomic queue capacity rejection, FIFO retries with stable event IDs, main-thread acceptance callbacks, complete/drained history coverage, durable custody/checksum isolation, state-preserving legal-stack splits, actual candidate counts, accepted reservation commits, idempotent partial returns, uncertain disconnect custody, and persistence retry after an actual acceptance.

On the current Linux checkout, narrow builds use Mono MSBuild because `dotnet build` fails evaluating the mixed classic/SDK project graph before compilation. Existing restored packages and game reference assemblies are required. Contracts, Inventory UI, Trade and red-packet clients must be built together for the new optional item presentation capability.

Commands used for this change:

```bash
for task_project in Extensions/Inventory/Contracts/InventoryExtension.csproj Extensions/Inventory/Client/InventoryExtension.Client.csproj Extensions/Trade/Client/TradeExtension.Client.csproj Extensions/LegacyRedPacket/Client/LegacyRedPacketExtension.Client.csproj Tests/InventoryRuntimeTests/InventoryRuntimeTests.csproj Tests/RedPacketInventoryRuntimeTests/RedPacketInventoryRuntimeTests.csproj Tests/LegacyTradeRuntimeTests/LegacyTradeRuntimeTests.csproj; do
  msbuild "$task_project" /t:Build /p:Configuration=Release /p:BuildProjectReferences=false /p:ResolveNuGetPackages=false /p:MSBuildEnableWorkloadResolver=false /p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ /v:minimal || exit
done
mono Tests/InventoryRuntimeTests/bin/Release/InventoryRuntimeTests.exe
dotnet exec --runtimeconfig Tests/InventoryRuntimeTests/runtimeconfig.json Tests/InventoryRuntimeTests/bin/Release/InventoryRuntimeTests.exe
dotnet exec --runtimeconfig Tests/RedPacketInventoryRuntimeTests/runtimeconfig.json Tests/RedPacketInventoryRuntimeTests/bin/Release/RedPacketInventoryRuntimeTests.exe
dotnet exec --runtimeconfig Tests/LegacyTradeRuntimeTests/runtimeconfig.json Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release
xmllint --noout Client/Languages/English/Keyed/InventoryExtension.xml 'Client/Languages/ChineseSimplified (简体中文)/Keyed/InventoryExtension.xml' Client/Languages/English/Keyed/LegacyRedPacketExtension.xml 'Client/Languages/ChineseSimplified (简体中文)/Keyed/LegacyRedPacketExtension.xml'
git diff --check
```

The inventory harness adds display-only grouping assertions: actual counts, source/quality isolation, original entry action mapping, reservations excluded from totals, unknown presentation fallback, field-boundary identity collisions and sum overflow. These tests do not prove in-game layout, arbitrary third-party state restoration or real relay behavior. Interrupted sender history remains held for review, without automatic resend/refund; this is not full crash reconciliation.

Validation result: Inventory 7, red-packet inventory 8, legacy Trade 9 and responsive UI geometry scenarios passed; XML and whitespace checks passed. The responsive test emitted a NuGet vulnerability-data warning but still completed successfully. The attempted `dotnet build Extensions/Inventory/Client/InventoryExtension.Client.csproj --configuration Release --no-restore -p:BuildProjectReferences=false -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/` failed before compilation with no compiler errors; the Mono builds above succeeded. No full solution or in-game/live-relay validation was run. PowerShell (`pwsh`) was unavailable, so `.github/scripts/check-artifacts.ps1` was not run; a read-only file scan found no game/Unity reference DLLs under the client output. Output DLL hashes were matched against the newly built assemblies, and both language variants were synchronized.

The load-error fix adds exact-name resolver isolation and component snapshot fixtures retaining the lineage ID, quantity 2778, provenance and ledger sequence. These invoke the production prefix and component snapshot reader, not the complete Scribe/Unity loading pipeline. An attempted full managed `SaveableFromNode` test could not run with compile-only references: `Verse.ParseHelper` needs the absent Steamworks assembly; Unity native logging is unavailable, and this Harmony build cannot detour on .NET 10. Mono also lacks Unity.Burst/firstpass dependencies (the final isolated fixture passes, with an attribute warning). Full in-game save → restart → load remains required. The red-packet regression exercises both claim and assign wire messages for 20 retries with an unavailable receipt: one throttled recovery log, cursor held, no unsolicited warning and no reward; restoring the receipt then consumes history exactly once without another deposit.
