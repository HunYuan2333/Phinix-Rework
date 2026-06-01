# Business Boundary Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the first wave of Chat/Trade business leakage from framework-facing code while keeping the project buildable.

**Architecture:** Keep framework contracts generic and move business service interfaces into Chat/Trade-owned namespaces. Preserve compatibility only where needed, route outbound extension commands through the existing command pipeline, and add lifecycle/error isolation around framework runtime boundaries.

**Tech Stack:** C#/.NET 10 and .NET Framework 4.7.2 multi-target projects, existing console runtime tests in `Tests/Phase35RuntimeTests`.

---

## File Structure

- Modify: `Tests/Phase35RuntimeTests/Program.cs`
  - Add regression checks for server pipeline exception isolation.
- Modify: `Extensions/Chat/Contracts/ChatDomainContracts.cs`
  - Move Chat domain interfaces to `Phinix.ChatExtension.Client`.
- Modify: `Extensions/Trade/Contracts/TradeDomainContracts.cs`
  - Move Trade domain interfaces to `Phinix.TradeExtension.Client`.
- Modify: Chat/Trade client files that currently import `PhinixClient.Framework` for moved business contracts.
- Modify: `Extensions/Chat/Client/PhinixFrameworkChatService.cs`
  - Make history request creation available for command pipeline use.
- Modify: `Extensions/Chat/Client/BuiltInChatClientExtension.cs`
  - Implement outgoing command handler for history request if needed.
- Modify: `Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs`
  - Return snapshot request packets instead of sending directly.
- Modify: `Extensions/Trade/Client/BuiltInTradeClientExtension.cs`
  - Route snapshot request through `IFrameworkClientCommandTransport`.
- Modify: `Client/Source/Framework/PhinixFrameworkClient.cs`
  - Add `Shutdown()`/`Dispose()` and avoid anonymous event handlers where cleanup is needed.
- Modify: `Server/ServerRuntime/ServerPipelineRunner.cs`
  - Catch exceptions from outbound interceptors and observers.

---

## Task 1: Add Pipeline Isolation Regression Tests

**Files:**
- Modify: `Tests/Phase35RuntimeTests/Program.cs`

- [ ] **Step 1: Add failing tests**

Add tests named:

```csharp
AssertOutboundInterceptorExceptionDoesNotBlockDelivery();
AssertMessageObserverExceptionDoesNotBlockLaterObservers();
AssertCommandObserverExceptionDoesNotBlockLaterObservers();
```

Each test constructs a `ServerPipelineRunner`, registers one throwing interceptor/observer and one normal delivery/observer, then asserts later delivery/observer still happens.

- [ ] **Step 2: Run red verification**

Run:

```powershell
dotnet run --project Tests\Phase35RuntimeTests\Phase35RuntimeTests.csproj
```

Expected: fail before implementation because exceptions escape from outbound/observer paths.

- [ ] **Step 3: Implement isolation**

Wrap `ServerPipelineRunner.DispatchOutbound`, `observeMessage`, and `observeCommand` extension callbacks in `try/catch (Exception ex)` and log through `context.Log`.

- [ ] **Step 4: Run green verification**

Run the same command. Expected: all runtime tests pass.

---

## Task 2: Move Business Contracts Out Of Framework Namespace

**Files:**
- Modify: `Extensions/Chat/Contracts/ChatDomainContracts.cs`
- Modify: `Extensions/Trade/Contracts/TradeDomainContracts.cs`
- Modify: Chat/Trade client consumers of moved interfaces.

- [ ] **Step 1: Add compile-failing boundary check**

Before moving contracts, run:

```powershell
rg -n "namespace PhinixClient\.Framework" Extensions\Chat\Contracts Extensions\Trade\Contracts
```

Expected: matches exist.

- [ ] **Step 2: Move namespaces**

Move Chat interfaces to `namespace Phinix.ChatExtension.Client` and Trade interfaces to `namespace Phinix.TradeExtension.Client`. Leave generic framework types in `PhinixClient.Framework` only when they are truly non-business.

- [ ] **Step 3: Update using directives**

Update Chat files to import `Phinix.ChatExtension.Client` and Trade files to import `Phinix.TradeExtension.Client` where needed.

- [ ] **Step 4: Verify boundary**

Run:

```powershell
rg -n "namespace PhinixClient\.Framework" Extensions\Chat\Contracts Extensions\Trade\Contracts
```

Expected: no matches.

---

## Task 3: Route Extension Requests Through Command Pipeline

**Files:**
- Modify: `Extensions/Chat/Client/PhinixFrameworkChatService.cs`
- Modify: `Extensions/Chat/Client/BuiltInChatClientExtension.cs`
- Modify: `Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs`
- Modify: `Extensions/Trade/Client/BuiltInTradeClientExtension.cs`

- [ ] **Step 1: Find direct sends**

Run:

```powershell
rg -n "SendFrameworkPacket" Extensions\Chat Extensions\Trade
```

Expected: direct sends exist in Chat and Trade services.

- [ ] **Step 2: Refactor services**

Change service methods so they create or return `FrameworkPacket` command packets, but do not call `SendFrameworkPacket()` directly.

- [ ] **Step 3: Use command transport**

Call `IFrameworkClientCommandTransport.TryHandleOutgoingCommand(packet)` from extension activation/lifecycle code. Let existing outgoing command handlers send or translate the packet.

- [ ] **Step 4: Verify no direct sends**

Run:

```powershell
rg -n "SendFrameworkPacket" Extensions\Chat Extensions\Trade
```

Expected: no direct service calls remain except XML/comments if any are intentionally retained with explanation.

---

## Task 4: Add Framework Client Shutdown

**Files:**
- Modify: `Client/Source/Framework/PhinixFrameworkClient.cs`

- [ ] **Step 1: Identify cleanup targets**

Check constructor subscriptions for `negotiationTimer.Elapsed`, `netClient.RegisterPacketHandler`, and `netClient.OnDisconnect`.

- [ ] **Step 2: Implement shutdown**

Make `PhinixFrameworkClient` implement `IDisposable`. Add `Shutdown()` that stops/disposes the timer, unregisters the framework packet handler, unsubscribes `netClient.OnDisconnect`, and calls `PhinixExtensionRegistry.ShutdownExtensions(...)`.

- [ ] **Step 3: Make cleanup idempotent**

Guard with a `disposed` bool so repeated calls are harmless.

---

## Task 5: Build Verification

**Files:**
- All modified files.

- [ ] **Step 1: Runtime tests**

Run:

```powershell
dotnet run --project Tests\Phase35RuntimeTests\Phase35RuntimeTests.csproj
```

Expected: exit code 0 and `All framework runtime tests passed.`

- [ ] **Step 2: Solution build**

Run:

```powershell
dotnet build Phinix.sln
```

Expected: exit code 0. If external game DLL references are unavailable, record the exact failure instead of claiming success.

- [ ] **Step 3: Boundary scan**

Run:

```powershell
rg -n "namespace PhinixClient\.Framework" Extensions\Chat\Contracts Extensions\Trade\Contracts
rg -n "SendFrameworkPacket" Extensions\Chat Extensions\Trade
```

Expected: no business contract namespace pollution; no extension service direct-send bypasses.
