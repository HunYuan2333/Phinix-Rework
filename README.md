<h1 align="center">Phinix Rework</h1>
<h4 align="center"><i>A RimWorld 1.6 multiplayer mod — cross-colony chat, asynchronous trade, and managed plugin framework</i></h4>

<p align="center">
  English · <a href="./README.zh-CN.md">简体中文</a>
</p>

---

## About

Phinix Rework adds multiplayer communication and economic interaction to RimWorld via an external dedicated server. It connects distinct player colonies while keeping the core game engine independent.

- **In-Game Chat**: Cross-colony text messaging with rich text formatting, customizable colors, and channel support.
- **Asynchronous Trading**: Trade items and silver between colonies without requiring both players to be online simultaneously.
- **Unified Inventory**: Shared item staging pipeline supporting trade settlements and item management.
- **Dedicated Server**: Lightweight standalone server supporting user authentication and permission management.
- **Plugin Store & Extensible Runtime**: First-party and third-party extensions share identical runtime lifecycles. Browse and install managed plugins directly in game.

> [!NOTE]
> Phinix Rework provides chat, trade, and plugin interoperability between independent colonies. It does **not** synchronize world map simulation, in-game ticks, or lockstep pawn construction across players.

---

## Repository Index

| Repository | Responsibility |
| :--- | :--- |
| [Phinix-Rework](https://github.com/HunYuan2333/Phinix-Rework/tree/dev) | RimWorld client host and client plugins; this repository |
| [Phinix-Rework-Common](https://github.com/HunYuan2333/Phinix-Rework-Common/tree/dev) | Game-independent shared source and contracts |
| [Phinix-Rework-Server](https://github.com/HunYuan2333/Phinix-Rework-Server/tree/dev) | Dedicated server, server plugins and Docker publication |

The mod is **Phinix Rework**, distinct from the original Phinix. Client and Server pin Common at `Dependencies/Phinix.Common`; initialize nested submodules with `git submodule update --init --recursive`. Build instructions for Common and Server are in their respective READMEs.

---

## Installation

### Client (RimWorld 1.6)

#### Option A: Steam Workshop (Recommended)

1. Subscribe to [Phinix Rework on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3735269431). Steam will automatically download and update the mod.
2. Launch RimWorld, navigate to **Mods** in the main menu, enable **Phinix Rework**, and restart the game.

#### Option B: Manual Installation

1. Obtain the release package from [GitHub Releases](https://github.com/HunYuan2333/Phinix-Rework/releases) (or build from source following [Developer Build](#developer-build)).
2. Extract the mod directory into your RimWorld `Mods` folder, for example:
   `<path-to-RimWorld>/Mods/Phinix-Rework/`
3. Launch RimWorld, navigate to **Mods**, enable **Phinix Rework**, and restart the game.

### Server (Dedicated)

The server can be hosted via Docker (recommended) or compiled with .NET 10 SDK.

#### Option A: Docker (Recommended)

```bash
docker pull hunyuan2333/phinix-rework:dev

docker run -d \
  --name phinix-server \
  --restart unless-stopped \
  -p 16200:16200/udp \
  -v ./server_data:/data \
  hunyuan2333/phinix-rework:dev

# View server logs
docker logs -f phinix-server
```

Or deploy with `docker-compose.yml` from the [Server repository](https://github.com/HunYuan2333/Phinix-Rework-Server/tree/dev):

```bash
docker compose up -d
```

#### Option B: Manual Build (.NET 10 SDK)

```bash
git clone --branch dev --recurse-submodules https://github.com/HunYuan2333/Phinix-Rework-Server.git
cd Phinix-Rework-Server
dotnet build Server/Server.csproj --configuration Release -p:BuildInParallel=false -m:1
dotnet Server/bin/Release/net10.0/PhinixServer.dll
```

Configuration defaults to port `16200` (UDP) and `ClientKey` authentication via `server.conf`. The interactive console supports `help`, `version`, and `exit` commands.

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
| **Bundled Core** | Chat, Trade, Inventory, PluginStore | Shipped directly inside the Phinix Mod package (`Common/Extensions/`) | Managed with main mod updates |
| **Managed Plugins** | Talent Trade, Red Packet, third-party plugins | Installed via the Plugin Store into `SaveData/Phinix/ManagedExtensions/packages/` | Managed, enabled, or uninstalled in-game |
| **Workshop Mods** | External RimWorld submods | Subscribed via Steam Workshop or placed in `Mods/` | Handled by RimWorld's native mod manager |

> [!NOTE]
> Legacy Talent Trade (`Phinix-Legacy-TalentTrade`) and Red Packet (`Phinix-Legacy-RedPacket`) have been decoupled from the core mod. They are distributed as standalone managed plugins and can be installed on demand through the Plugin Store.

---

## Developer Guide

### Environment Requirements

- **.NET 10 SDK** (server and test harnesses)
- **.NET Framework 4.7.2** targeting pack (client compilation)
- **RimWorld 1.6 Managed Assemblies**: Place references in `GameDlls/1.6/` (`Assembly-CSharp.dll`, the required `UnityEngine*.dll` modules including `UnityEngine.ImageConversionModule.dll`, and `com.rlabrecque.steamworks.net.dll`). For a full build against another directory, set both `-p:GameReferenceDirectory=<path>` and `-p:RimWorldDepDir=<path>`. Never redistribute game DLLs.

### Developer Build

```bash
# Initialize pinned shared source and nested protobuf
git submodule update --init --recursive

# Run core framework regression tests
dotnet run --project Tests/Phase35ClientRuntimeTests/Phase35ClientRuntimeTests.csproj --configuration Release

# Run responsive layout regression tests
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release

# Run managed extension runtime tests
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0

# Run plugin store tests
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release

# Full client build (requires RimWorld 1.6 reference DLLs)
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

The installable client package is `Output/phinix-rework/`. Replace the complete old mod folder and preserve player data separately.

### Documentation & References

- [Architecture & Design Philosophy](docs/Design-Philosophy.md) — Plugin boundaries, communication pipelines, and lifecycle models.
- [Plugin Authoring Example](https://github.com/HunYuan2333/Phinix-Example-Plugin#readme) — Public plugin entry points, packaging and lifecycle example.
- [Phinix-Example-Plugin](https://github.com/HunYuan2333/Phinix-Example-Plugin) — Official minimal plugin example featuring tabs, settings, and dual-language localization.
- [Phinix-Plugin-Index](https://github.com/HunYuan2333/Phinix-Plugin-Index) — Official plugin catalog and submission repository.

---

## Credits & License

Phinix Rework builds upon the vision of the original Phinix mod by the Phinix Team and draws inspiration from Longwelwind's [Phi mod](https://github.com/longwelwind/phi).
