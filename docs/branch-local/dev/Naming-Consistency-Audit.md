# Naming Consistency Audit — Phinix Rework

> **Goal**: Unify all user-visible and metadata branding to **"Phinix Rework"** / **"phinix-rework"**.  
> **Scope**: Code identifiers (namespaces, class names, assembly names) are intentionally left as-is  
> unless they are *directly exposed to end users*. This document tracks display names and metadata only.

---

## Decision Reference

| Context | Correct form | Rationale |
|---------|-------------|-----------|
| RimWorld mod button label (UI) | `Phinix Rework` | User-visible; should reflect the mod's actual name |
| RimWorld mod settings category | `Phinix Rework` | Shown in the in-game Mod Settings list |
| User-facing error/status messages mentioning the product | `Phinix Rework` | Consistency |
| Server default name / description | `Phinix Rework Server` / `A Phinix Rework server.` | Shown in any server listing or logs |
| Server startup log line | `Phinix Rework server version …` | Operator-facing; should be unambiguous |
| `About.xml` `<name>` | `Phinix Rework` (currently lowercase `phinix-rework`) | Steam / RimWorld shows this as the mod name |
| `AssemblyProduct` metadata | `Phinix Rework` | Shown in DLL properties |
| C# namespaces (`PhinixClient`, `Phinix.Chat`, …) | **Do not change** | Changing breaks binary compatibility and is an API change |
| RimWorld translation key prefixes (`Phinix_…`) | **Do not change** | Internal identifiers; renaming breaks existing translation files |
| Legacy adapter names (`PhinixLegacy`, `LegacyAdapter`) | **Do not change** | Intentionally reference the *original* Phinix protocol |
| Save-data XML element name (`<Phinix>`) | **Do not change** | Would break existing player save files |
| File/directory names in the repository | **Do not change** | No user impact |

---

## Category 1 — RimWorld Mod Metadata (`Client/About/`)

### `Client/About/About.xml`

| Field | Current value | Required value | Priority |
|-------|--------------|---------------|----------|
| `<name>` | `phinix-rework` | `Phinix Rework` | **HIGH** — RimWorld displays this in the Mods list |

> [!NOTE]
> The `<packageId>` (`hunyuan2333.phinixrework`) and `<url>` are fine as-is; they are identifiers,
> not display names.

---

## Category 2 — In-Game UI Strings

### 2a. RimWorld bottom-bar button (main tab)

**English** — `Client/Defs/MainButtonDefs/PhinixClient.xml` line 6:
```xml
<description>Open the Phinix chat</description>
```
→ `Open the Phinix Rework chat`

**Chinese** — `Client/Languages/ChineseSimplified (简体中文)/DefInjected/MainButtonDef/PhinixClient.xml`:
```xml
<Chat.label>Phinix</Chat.label>                   <!-- line 5 -->
<Chat.description>打开Phinix会话框</Chat.description>  <!-- line 7 -->
```
→ `Chat.label`: `Phinix Rework`  
→ `Chat.description`: `打开 Phinix Rework 会话框`

### 2b. Mod settings category name

**`Client/Source/Client.cs` line 32:**
```csharp
public override string SettingsCategory() => "Phinix";
```
→ `"Phinix Rework"`

### 2c. Save-data theme folder path

**`Client/Source/UI/ThemeLoader.cs` line 34:**
```csharp
string configThemeDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "Phinix", "Themes");
```
→ **Do NOT change.** This is a file-system path used for save data. Changing it would lose existing
users' theme configurations.

### 2d. Internal debug string (not user-facing production text)

**`Client/Source/Client.cs` line 443:**
```
"DISABLED (not integrated — user disabled it; enable in Phinix mod settings → Extensions)"
```
→ `"… enable in Phinix Rework mod settings → Extensions"`  
*(Low priority — only visible in developer/diagnostic output)*

---

## Category 3 — Language File Strings (User-Facing)

These appear as in-game notifications, letters, and tooltips shown to players.
All occurrences of bare `"Phinix"` in message bodies should become `"Phinix Rework"`.

### English — `Client/Languages/English/Keyed/`

| File | Key | Current text excerpt | Change |
|------|-----|---------------------|--------|
| `Core.xml` | `Phinix_framework_connectedFrameworkServer` | `Connected to Phinix framework server.` | → `Connected to Phinix Rework server.` |
| `Core.xml` | `Phinix_framework_legacyExtensionUnavailable` | `This feature requires a Phinix framework server.` | → `…a Phinix Rework server.` |
| `Core.xml` | `Phinix_error_authFailedMessage` | `…your display name in Phinix mod options…` | → `…in Phinix Rework mod options…` |
| `Core.xml` | `Phinix_error_connectionFailedMessage` | `…the address/port in Phinix mod options are correct.` | → `…in Phinix Rework mod options…` |
| `ChatExtension.xml` | `Phinix_chat_legacyModeLetter_description` | `…connected to an older Phinix server…` | → `…older Phinix server…` (**keep as-is** — refers to the legacy Phinix server, not this mod) |
| `LegacyTalentTradeExtension.xml` | `Phinix_legacyTalentTrade_pleaseLogIn` | `Please log in to Phinix chat first.` | → `…Phinix Rework chat first.` |
| `LegacyTalentTradeExtension.xml` | `Phinix_legacyTalentTrade_versionText` | `…through the Phinix chat interface.` | → `…Phinix Rework chat interface.` |
| `TradeExtension.xml` | `Phinix_trade_tradeReceivedLetter_description` | `…in the Phinix chat panel…` | → `…Phinix Rework chat panel…` |
| `TradeExtension.xml` | `Phinix_trade_tradeCompletedInventoryDescription` | `…in your Phinix inventory.` | → `…Phinix Rework inventory.` |
| `TradeExtension.xml` | `Phinix_trade_tradeCancelledInventoryDescription` | `…in your Phinix inventory.` | → `…Phinix Rework inventory.` |
| `InventoryExtension.xml` | `Phinix_inventory_rewardStored` | `Reward stored in Phinix inventory.` | → `…Phinix Rework inventory.` |
| `InventoryExtension.xml` | `Phinix_inventory_pawnStored` | `Pawn stored in Phinix inventory.` | → `…Phinix Rework inventory.` |
| `InventoryExtension.xml` | `Phinix_inventory_pawnsStored` | `Pawns stored in Phinix inventory.` | → `…Phinix Rework inventory.` |
| `InventoryExtension.xml` | `Phinix_inventory_recoveryWarning` | `…before this save. …Phinix will not merge…` | → `…Phinix Rework will not merge…` |
| `InventoryExtension.xml` | `Phinix_inventory_deliveryChoiceIntro` | `…Phinix can drop them onto a map…` | → `…Phinix Rework can drop them…` |
| `InventoryExtension.xml` | `Phinix_inventory_deliveryChoiceFooter` | `…can be changed at any time in Phinix settings. …before disabling or removing Phinix.` | → `…in Phinix Rework settings. …before disabling or removing Phinix Rework.` |
| `InventoryExtension.xml` | `Phinix_inventory_saveOnly` | *(not in English file)* | See Chinese below |

### Chinese — `Client/Languages/ChineseSimplified (简体中文)/Keyed/`

| File | Key | Current text excerpt | Change |
|------|-----|---------------------|--------|
| `Core.xml` | `Phinix_framework_connectedFrameworkServer` | `已连接至 Phinix 框架服务器。` | → `已连接至 Phinix Rework 服务器。` |
| `Core.xml` | `Phinix_framework_legacyExtensionUnavailable` | `此功能需要 Phinix 框架服务器。` | → `此功能需要 Phinix Rework 服务器。` |
| `Core.xml` | `Phinix_error_authFailedMessage` | `…Phinix 模组设置中…` | → `…Phinix Rework 模组设置中…` |
| `Core.xml` | `Phinix_error_connectionFailedMessage` | `…Phinix 模组设置里…` | → `…Phinix Rework 模组设置里…` |
| `ChatExtension.xml` | `Phinix_chat_legacyModeLetter_description` | `…旧版 Phinix 服务器…` | **Keep as-is** — refers to the legacy Phinix server |
| `LegacyTalentTradeExtension.xml` | `Phinix_legacyTalentTrade_pleaseLogIn` | `请先登录 Phinix 聊天室。` | → `请先登录 Phinix Rework 聊天室。` |
| `LegacyTalentTradeExtension.xml` | `Phinix_legacyTalentTrade_versionText` | `…通过 Phinix 聊天界面访问…` | → `…通过 Phinix Rework 聊天界面访问…` |
| `TradeExtension.xml` | `Phinix_trade_tradeReceivedLetter_description` | `打开Phinix内的'交易'标签…` | → `打开 Phinix Rework 内的'交易'标签…` |
| `TradeExtension.xml` | `Phinix_trade_tradeCompletedInventoryDescription` | `…存入 Phinix 库存。` | → `…存入 Phinix Rework 库存。` |
| `TradeExtension.xml` | `Phinix_trade_tradeCancelledInventoryDescription` | `…存入 Phinix 库存。` | → `…存入 Phinix Rework 库存。` |
| `InventoryExtension.xml` | `Phinix_inventory_saveOnly` | `停用或移除 Phinix 前请先提取物品。` | → `停用或移除 Phinix Rework 前请先提取物品。` |
| `InventoryExtension.xml` | `Phinix_inventory_rewardStored` | `奖励已存入 Phinix 库存。` | → `…Phinix Rework 库存。` |
| `InventoryExtension.xml` | `Phinix_inventory_pawnStored` | `Pawn 已存入 Phinix 库存。` | → `…Phinix Rework 库存。` |
| `InventoryExtension.xml` | `Phinix_inventory_pawnsStored` | `Pawn 已存入 Phinix 库存。` | → `…Phinix Rework 库存。` |
| `InventoryExtension.xml` | `Phinix_inventory_deliveryChoiceFooter` | `停用或移除 Phinix 前请先提取。` | → `停用或移除 Phinix Rework 前请先提取。` |

---

## Category 4 — Server-Side Strings

### `Server/Config.cs`

> Planning correction, 2026-10-05: ServerName is also used as a client credential lookup/storage key in Common/Authentication/ClientAuthenticator.cs (TryGetCredential and AddOrUpdateCredential). The two ServerName changes below are conditional proposals for newly initialized configurations, not a blanket rename of existing servers or a cosmetic fallback repair. Preserve existing configured names; decide empty-name recovery separately so a new fallback does not select a different credential entry.

| Line | Current value | Required value |
|------|--------------|---------------|
| 84 | `ServerName = "Phinix Server"` | Conditional new-config default: `"Phinix Rework Server"`; preserve existing identity |
| 90 | `ServerDescription = "A Phinix server."` | `"A Phinix Rework server."` |
| 196 | `ServerName = "Phinix Server"` (fallback) | Preserve fallback until credential/identity behavior is explicitly decided |
| 197 | `ServerDescription = "A Phinix server."` (fallback) | `"A Phinix Rework server."` |

### `Server/Server.cs`

| Line | Current value | Required value |
|------|--------------|---------------|
| 96 | `"Phinix server version {0} listening on {1}:{2}"` | `"Phinix Rework server version {0} listening on {1}:{2}"` |

---

## Category 5 — Assembly Metadata

### `Server/Connections.Server/AssemblyInfo.cs`

| Field | Current value | Required value |
|-------|--------------|---------------|
| `AssemblyProduct` | `"Phinix"` | `"Phinix Rework"` |

### `Server/Properties/AssemblyInfo.cs`

| Field | Current value | Required value |
|-------|--------------|---------------|
| `AssemblyTitle` | `"PhinixServer"` | `"Phinix Rework Server"` |
| `AssemblyProduct` | `"PhinixServer"` | `"Phinix Rework"` |
| `AssemblyCopyright` | `"Copyright © 2018"` | Update year (low priority) |

### `Client/Source/Properties/AssemblyInfo.cs`

| Field | Current value | Required value |
|-------|--------------|---------------|
| `AssemblyTitle` | `"PhinixClient"` | `"Phinix Rework"` |
| `AssemblyProduct` | `"PhinixClient"` | `"Phinix Rework"` |

> [!NOTE]
> `AssemblyName` and `RootNamespace` in `.csproj` files remain `PhinixClient` / `PhinixServer` —
> these are code identifiers, not display names, and changing them is a breaking API change.

---

## Category 6 — Intentionally Correct / Do Not Change

These use bare "Phinix" and are **correct by design**:

| Location | Why it stays |
|----------|-------------|
| All C# namespaces (`PhinixClient`, `Phinix.Chat`, `Phinix.TradeExtension`, …) | API identifiers; changing breaks binary compat |
| All RimWorld translation key names (`Phinix_…`) | Internal identifiers; renaming breaks existing translations |
| `LegacyAdapter`, `PhinixLegacy` references | Intentionally refer to the *original* Phinix server protocol |
| `<Phinix>` XML element in save data (`LegacySettings.cs` line 76) | Changing breaks existing player saves |
| `Path.Combine(…, "Phinix", "Themes")` (`ThemeLoader.cs` line 34) | Filesystem path tied to existing user data |
| `Chat.label` (English) — currently `"chat"` | Already generic; the button uses the sidebar which shows the tab title, not this label in practice |
| `packageId` in `About.xml` (`hunyuan2333.phinixrework`) | RimWorld package identifier; must not change after publication |

---

## Summary: Change Count by Priority

| Priority | Count | Locations |
|----------|-------|-----------|
| **HIGH** (user-visible mod name, settings category) | 5 items | `About.xml`, `Client.cs`, `PhinixClient.xml` (×2), Defs |
| **MEDIUM** (in-game notifications/letters) | ~25 strings | Language XML files (English + Chinese) |
| **IDENTITY-SENSITIVE** (server name) | 2 locations | Config initializer and empty-name fallback; require a credential policy |
| **LOW** (descriptions, logs, assembly metadata) | Remaining items; recount against current code before execution | Descriptions in `Config.cs`, `Server.cs`, `AssemblyInfo.cs` (×3) |
| **Do not change** | — | Namespaces, key names, save paths, legacy references |

---

## Execution Order

1. `Client/About/About.xml` — `<name>` field ← most visible, do first
2. `Client/Source/Client.cs` — `SettingsCategory()` return value
3. `Client/Defs/MainButtonDefs/PhinixClient.xml` + `DefInjected/MainButtonDef/PhinixClient.xml` — button label and description
4. Language files (English then Chinese) — work through the table in Category 3
5. `Server/Config.cs` — description strings; handle ServerName separately after deciding identity/credential behavior
6. `Server/Server.cs` — startup log message
7. `AssemblyInfo.cs` files — assembly metadata (lowest impact)

## Follow-up scope and acceptance (2026-10-05)

Execution order is coordinated by [the follow-up plan](./后续实施计划.md). Naming is a separate change from repository extraction, DI adoption and legacy protocol work. Preserve assembly identity, wire identifiers, package IDs, save paths and existing configured server names.

Test both fresh configuration and an existing server/client credential file, including empty-name recovery and old-client login. A rename must not silently merge credentials based on equal display names or assume that a branding string identifies the same server. If a future stable server identifier is introduced, version that contract and design migration separately.

The counts and line numbers above are audit references, not evidence that changes have been implemented. This revision updates planning only; no strings, credentials or server state were changed.
