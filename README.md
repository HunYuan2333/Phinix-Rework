<h1 align="center">Phinix Rework</h1>
<h4 align="center"><i>A RimWorld 1.6 multiplayer mod client — cross-colony chat, asynchronous trade, and managed plugin framework</i></h4>

<p align="center">
  English · <a href="./README.zh-CN.md">简体中文</a>
</p>

---

## About

Phinix Rework adds multiplayer communication and economic interaction to RimWorld via an external dedicated server. It connects distinct player colonies while keeping the core game engine independent.

This repository is the **Phinix Rework Client** (`Phinix-Rework`), containing the RimWorld client mod host, in-game UI, and official client plugins.

- **In-Game Chat**: Cross-colony text messaging with rich text formatting, customizable colors, and channel support.
- **Asynchronous Trading**: Trade items and silver between colonies without requiring both players to be online simultaneously.
- **Unified Inventory**: Local staging pipeline supporting trade settlements and item management.
- **Plugin Store & Extension Management**: First-party and third-party extensions share identical runtime lifecycles. Browse, install, toggle, and uninstall managed plugins directly in game.

> [!NOTE]
> Phinix Rework provides chat, trade, and plugin interoperability between independent colonies. It does **not** synchronize world map simulation, in-game ticks, or lockstep pawn construction across players.

---

## Repository Index & Architecture

This project is named **Phinix Rework**, distinct from the original Phinix project. The project is split into three repositories:

| Repository | Role | Responsibility |
| :--- | :--- | :--- |
| [Phinix-Rework](https://github.com/HunYuan2333/Phinix-Rework/tree/dev) | Client (this repository) | RimWorld 1.6 client mod host, in-game UI, and client plugins |
| [Phinix-Rework-Common](https://github.com/HunYuan2333/Phinix-Rework-Common/tree/dev) | Shared Layer | Game-independent networking, encryption, user management, and neutral Chat/Trade contracts |
| [Phinix-Rework-Server](https://github.com/HunYuan2333/Phinix-Rework-Server/tree/dev) | Dedicated Server | Standalone dedicated server host, server plugins, and Docker publication |

Both the Client and Server repositories pin the Shared layer via a Git submodule at `Dependencies/Phinix.Common` and reference its projects directly during compilation. To preserve ecosystem, save, and network compatibility, repository names do not alter existing assembly names, namespaces, wire protocol identifiers, mod package IDs, persisted storage keys, or the `Dependencies/Phinix.Common` submodule directory name.

---

## Installation

### Client Installation (RimWorld 1.6)

#### Option A: Steam Workshop (Recommended)

1. Subscribe to [Phinix Rework on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3735269431). Steam will automatically handle download and updates.
2. Launch RimWorld, navigate to **Mods** in the main menu, enable **Phinix Rework**, and restart the game.

#### Option B: Manual Installation

1. Obtain the release package from [GitHub Releases](https://github.com/HunYuan2333/Phinix-Rework/releases) (or build from source following [Developer Guide](#developer-guide)).
2. Extract the mod directory into your RimWorld `Mods` folder, for example:
   `<path-to-RimWorld>/Mods/Phinix-Rework/`
3. Launch RimWorld, navigate to **Mods**, enable **Phinix Rework**, and restart the game.

### Dedicated Server Deployment

Phinix Rework client connects to a dedicated Phinix Rework server. If you want to host your own dedicated server, refer to the documentation in [Phinix-Rework-Server](https://github.com/HunYuan2333/Phinix-Rework-Server/tree/dev). It provides a prebuilt Docker container image (`hunyuan2333/phinix-rework:dev`) as well as .NET 10 SDK build instructions.

---

## First Connection

1. Load an existing save or start a new colony.
2. Click the **Phinix** button in the bottom navigation toolbar.
3. In the Phinix window, open **Settings** (`设置`).
4. Enter the server IP address, port (default `16200`), and your authentication key or display name.
5. Click **Connect** (`连接`).
6. Upon successful connection, the status bar displays connection health, and Chat, Trade, Inventory, and Store tabs become available.

### Connection Troubleshooting

- **Connection timed out**: Ensure UDP port `16200` is open on the server host and firewall.
- **Authentication failed**: Verify that your client key matches server registration records.
- **Protocol version mismatch**: Ensure client and server run compatible builds of Phinix Rework.

---

## Plugin Store & Extensions

Phinix Rework includes an in-game plugin store allowing players to extend features without manually editing mod directories.

1. **Browsing the Store**:
   - Open the Phinix window and switch to the **Store** (`商店`) tab.
   - Browse reviewed packages from the official catalog.
2. **Installing Plugins**:
   - Select a plugin, click **Install** (`安装`).
   - **Restart RimWorld** for the game engine to load newly installed assemblies.
3. **Managing Installed Extensions**:
   - Open **Extension Manager** (`扩展管理`) to inspect installed plugins.
   - Toggle plugins **Enabled** or **Disabled**, or click **Uninstall** (`卸载`).
   - Status changes take effect upon the next full game restart.
4. **Network Acceleration Switch**:
   - In Phinix settings, toggle between **GitHub Direct** (`GitHub 直连`) and **CF Acceleration** (`CF 加速`).
   - Both routes fetch the exact same immutable metadata and release assets from the official index; switch to Cloudflare acceleration if direct GitHub access is slow or restricted.

---

## Bundled Features vs. Managed Plugins

Phinix Rework distinguishes between core built-in features and independently managed plugins:

| Category | Modules | Delivery Route | Lifecycle |
| :--- | :--- | :--- | :--- |
| **Bundled Core** | Chat, Trade, Inventory, PluginStore | Shipped directly inside the main mod package (`Common/Extensions/`) | Managed with main mod updates |
| **Managed Plugins** | Talent Trade, Red Packet, third-party plugins | Installed via Plugin Store into `SaveData/Phinix/ManagedExtensions/packages/` | Managed, enabled, or uninstalled in game |
| **Workshop Mods** | External RimWorld submods | Subscribed via Steam Workshop or placed in `Mods/` | Handled by RimWorld's native mod manager |

> [!NOTE]
> Legacy Talent Trade (`Phinix-Legacy-TalentTrade`) and Red Packet (`Phinix-Legacy-RedPacket`) have been decoupled from the core mod. They are distributed as standalone managed plugins and can be installed on demand through the Plugin Store.

---

## Developer Guide

### Environment Requirements

- **.NET 10 SDK** (build runner and tests)
- **.NET Framework 4.7.2** targeting pack (classic client compilation; enabled on all platforms via `Microsoft.NETFramework.ReferenceAssemblies`)
- **RimWorld 1.6 Managed Assemblies**: Place references under `GameDlls/1.6/` (`Assembly-CSharp.dll`, required `UnityEngine*.dll` modules including `UnityEngine.ImageConversionModule.dll`, and `com.rlabrecque.steamworks.net.dll`). For external reference paths, specify both `-p:GameReferenceDirectory=<path>` and `-p:RimWorldDepDir=<path>`. Never redistribute game DLLs.

### Source Acquisition & Submodules

Clone the repository and recursive submodules:

```bash
git clone --branch dev --recurse-submodules https://github.com/HunYuan2333/Phinix-Rework.git
cd Phinix-Rework
```

For an existing checkout, recursively initialize and update submodules at their pinned commits:

```bash
git submodule update --init --recursive
```

> [!IMPORTANT]
> The repository pins dependencies using exact gitlinks; **do not use** `git submodule update --remote`. protobuf is a nested submodule inside the shared layer and requires `--recursive`.

### Automated Tests

Run the headless automated regression suites (note: automated tests verify runtime logic and do not replace in-game testing in RimWorld):

```bash
# Core framework runtime regression
dotnet run --project Tests/Phase35ClientRuntimeTests/Phase35ClientRuntimeTests.csproj --configuration Release

# Responsive layout geometry tests
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release

# Managed extension runtime regression
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0

# Plugin store logic regression
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release
```

### Client Build

Build the full client solution from the repository root:

```bash
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

> [!NOTE]
> `Phinix.sln` and `PhinixClient.sln` contain identical project graphs; either can be used as the build entry.

### Output Location & Package Structure

Build artifacts are assembled into `Output/phinix-rework/`:

```text
Output/phinix-rework/
├── About/               # Mod metadata and version manifests
├── Defs/                # RimWorld Def XMLs
├── Languages/           # Localization files
├── Textures/            # UI textures and icons
├── LoadFolders.xml      # Version load folder configurations
├── Common/
│   ├── Assemblies/      # Pinned shared assemblies and composition runtime
│   └── Extensions/      # Bundled extension assemblies (Chat, Trade, Inventory, PluginStore)
└── 1.6/
    └── Assemblies/      # RimWorld 1.6 client entry assembly (13-PhinixClient.dll)
```

This output folder is the complete, installable RimWorld mod. Copy it directly to `<path-to-RimWorld>/Mods/Phinix-Rework/` for local testing.

### Documentation & References

- [Plugin Development Guide](docs/Plugin-Development.md) — Comprehensive technical guide covering plugin architecture, lifecycle, DI composition, packaging, and publication. *(Phinix plugin development skill is currently in preparation; integration links will be added once ready.)*
- [Architecture & Design Philosophy](docs/Design-Philosophy.md) — Plugin boundaries, communication pipelines, and lifecycle models.
- [Compatibility Boundaries & Recovery Constraints](docs/Compatibility-Boundaries.md) — Network contracts and error recovery boundaries across server versions.
- [Client Inventory & Recovery Boundaries](docs/Inventory.md) — Inventory staging, ledger, and journal recovery constraints.
- [Phinix-Example-Plugin](https://github.com/HunYuan2333/Phinix-Example-Plugin) — Official minimal plugin example featuring tabs, settings, and dual-language localization.
- [Phinix-Plugin-Index](https://github.com/HunYuan2333/Phinix-Plugin-Index) — Official plugin catalog and submission repository.

---

## Credits & License

Phinix Rework builds upon the vision of the original Phinix mod by the Phinix Team and draws inspiration from Longwelwind's [Phi mod](https://github.com/longwelwind/phi).
