# Design Philosophy Compliance Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the highest-priority host/business boundary leaks called out in the 2026-06-01 compliance audit, while keeping plugin-owned behavior wired through extension APIs.

**Architecture:** Push Trade and Chat behavior back into their extensions, keep host settings generic, and stop the server/client host layers from naming specific business behavior in login/save/build flows. Reuse the existing extension host context and settings abstraction instead of inventing a new configuration system.

**Tech Stack:** C# projects targeting .NET Framework 4.7.2 and .NET 10, RimWorld client host, existing extension registration/runtime infrastructure.

---

## File Structure

- Modify: `Client/Source/Client.cs`
  - Remove host-side Chat/Trade business hooks and reuse a shared settings context instance.
- Modify: `Client/Source/Settings.cs`
  - Stop hardcoding Chat/Trade defaults in host settings and migrate legacy Chat sound settings into extension settings.
- Modify: `Client/Source/Framework/ClientSettingsContextAdapter.cs`
  - Keep change notifications useful for extension subscribers.
- Modify: `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs`
  - Remove Chat-specific `PlayNoiseOnMessageReceived` from the generic settings contract.
- Modify: `Extensions/Chat/Client/BuiltInChatClientExtension.cs`
  - Read notification sound preference from Chat-owned settings.
- Modify: `Extensions/Chat/Client/ChatSettingsPanelProvider.cs`
  - Own the Chat notification sound setting in the Chat panel.
- Modify: `Extensions/Trade/Client/BuiltInTradeClientExtension.cs`
  - Subscribe to settings/login events and sync `trade.acceptingTrades` from the extension side.
- Modify: `Extensions/Trade/Client/ClientTradeUiHostContext.cs`
  - Move drop-pod behavior into the Trade extension.
- Modify: `Common/UserManagement/ClientUserManager.cs`
  - Remove Trade-specific login parameter from the client login API.
- Modify: `Server/UserManagement/ServerUserManager.cs`
  - Stop consuming `LoginPacket.AcceptingTrades` during login.
- Modify: `Server/Server.csproj`
  - Replace hardcoded official extension copy paths with wildcard-based discovery.

---

## Task 1: Remove Host-Owned Chat/Trade Login And Save Behavior

**Files:**
- Modify: `Client/Source/Client.cs`
- Modify: `Common/UserManagement/ClientUserManager.cs`
- Modify: `Server/UserManagement/ServerUserManager.cs`
- Modify: `Extensions/Trade/Client/BuiltInTradeClientExtension.cs`

- [x] Remove `Client.CanUseFrameworkChat` and host-owned Trade sync from authentication and settings save flows.
- [x] Make `ClientUserManager.SendLogin` send only generic login fields.
- [x] Make the server ignore login-time `AcceptingTrades` state.
- [x] Let the Trade extension subscribe to login/settings changes and push `trade.acceptingTrades` through `UpdateSelf`.

## Task 2: Move Chat-Owned Sound Preference Out Of Generic Settings

**Files:**
- Modify: `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs`
- Modify: `Client/Source/Client.cs`
- Modify: `Client/Source/Settings.cs`
- Modify: `Client/Source/Framework/ClientSettingsContextAdapter.cs`
- Modify: `Extensions/Chat/Client/BuiltInChatClientExtension.cs`
- Modify: `Extensions/Chat/Client/ChatSettingsPanelProvider.cs`

- [x] Remove `PlayNoiseOnMessageReceived` from the generic settings contract.
- [x] Move the UI for the notification sound into the Chat settings panel.
- [x] Preserve backward compatibility by mapping the legacy top-level setting into `chat.playNoiseOnMessageReceived`.

## Task 3: Remove Remaining Host Hardcoding In Low-Risk Places

**Files:**
- Modify: `Client/Source/Client.cs`
- Modify: `Extensions/Trade/Client/ClientTradeUiHostContext.cs`
- Modify: `Server/Server.csproj`

- [x] Move Trade drop-pod behavior into the Trade extension.
- [x] Replace hardcoded server extension copy entries with wildcard-based copy items.
- [x] Keep the host settings store generic by removing built-in Chat/Trade default seeding.

## Follow-Through Beyond The Initial Plan

- [x] Extract server-side user management consumption behind `IServerUserManager`.
- [x] Split Chat client handler/render responsibilities out of the module entry class.
- [x] Remove Chat client's compile-time dependency on Trade contracts by moving `ITradeRequestApi` to a neutral contract.
- [x] Move server legacy config migration knowledge behind extension-owned migrators.
- [x] Pull legacy Trade repository/completion methods out of `PhinixFrameworkTradeClientService` into a dedicated adapter.

## Deferred By Audit

- [ ] `CM-1` / `FrameworkPacket.MessageType` rename remains deferred to the audit's explicit Phase 6 compatibility pass.
