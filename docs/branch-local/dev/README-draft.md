<h1 align="center">Phinix Rework</h1>
<h4 align="center"><i>A RimWorld multiplayer mod — chat, trade, and extensible plugin framework</i></h4>

> Draft README. Will be moved to repository root later. 中文版：[README-draft.zh-CN.md](./README-draft.zh-CN.md)

# About

Phinix Rework adds multiplayer chat and item trading to RimWorld via a dedicated external server. It is a ground-up rebuild of the original Phinix mod, built on a plugin-oriented framework — Chat and Trade themselves are plugins, not built-in special cases.

- In-game chat between colonies (rich text support, colourable names and messages)
- Asynchronous item trading (no simultaneous online required)
- Dedicated server with authentication and user management
- Extensible plugin system — third-party submods have the same status as official extensions

# Quick Start

## Client

1. Download the client package from [Releases](https://github.com/HunYuan2333/Phinix-Rework/releases).
2. Extract into RimWorld's `Mods` directory.
3. Enable Phinix in the in-game Mods menu, restart RimWorld.
4. Load a save, click the Phinix button in the bottom toolbar, go to Settings, enter the server address and port, then Connect.

Currently only supports RimWorld 1.6.

## Server (Docker, recommended)

```bash
docker pull hunyuan23333/phinix-rework:latest
docker run -d \
  --name phinix \
  --restart unless-stopped \
  -p 16200:16200/udp \
  -v ./server_data:/data \
  hunyuan23333/phinix-rework:latest

# View logs
docker logs -f phinix
```

Or use [docker-compose.yml](../docker-compose.yml):

```bash
docker compose up -d --build
```

## Server (manual build)

Requires .NET 10 SDK. Server project is in `Server/`, shared projects in `Common/`.

```bash
dotnet build Server/Server.csproj -c Release -o out
dotnet out/PhinixServer.dll
```

Configure via `server.conf` (defaults: port 16200, auth type ClientKey). Console commands include `help`, `version`, `exit`.

# Known Issues

- **Translation errors in log**: RimWorld's `Translate()` may produce `No active language` red-text errors when called from network callback threads. This does not affect functionality — failed translations fall back to the key name itself (e.g. `Phinix_framework_systemDisplayName`). This is a thread-safety issue and will be resolved in a future release by marshalling calls to the main thread. These log red-text lines are harmless and can be ignored.

# Architecture

Phinix Rework uses a layered, plugin-first architecture:

```
Plugins (Extensions/Chat, Extensions/Trade, third-party)
  → Shared contracts (ClientExtensionAbstractions)
    → Host (Client / Server)
      → Infrastructure (Common: networking, auth, user management)
```

See [设计哲学.md](./设计哲学.md) for the full design guide. Key principles:

- **Plugin parity** — Chat and Trade are just plugins. Third-party submods use the exact same discovery → registration → activation path.
- **Three pipelines** — All communication flows through `message` (display), `command` (control), and `item` (payload) lanes.
- **Dynamic UI** — Tabs, sidebars, and badges are contributed by plugins via `IMainTabProvider` / `IServerSidebarProvider` / `IBadgeProvider`.
- **Server Pipeline** — Inbound messages go through a five-stage flow (IngressValidation → PreHandle → DefaultProcess → Observation → Outbound). Extensions can intercept at any stage.

# Roadmap

The current release covers the core foundation: one-click Docker deployment, stable chat and trading on the client. Future work is about opening up the system:

- **Short term**: Fix remaining thread-safety issues and UI performance bottlenecks.
- **Medium term**: True plugin system — Chat and Trade move from "built-in" to "officially shipped plugins". Third-party authors can plug their own extensions into the same system without touching any host code.
- **Long term**: Hot-loadable Mods directory (drop in to enable), plugin marketplace, web admin panel.


# Developers

## Environment Setup

The client project depends on RimWorld assemblies. Place the required DLLs in `GameDlls/`: `Assembly-CSharp.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.IMGUIModule.dll`, `UnityEngine.InputLegacyModule.dll`, `UnityEngine.TextRenderingModule.dll`. Version-specific subdirectories under `GameDlls/` are supported.

## Building

`Phinix.sln` contains client, common, server, and extension projects. A `TravisCI` build profile is available for builds that don't need game assemblies.

- **Client**: .NET Framework 4.7.2 (Unity/Mono ecosystem)
- **Common projects**: multi-target `net472;net10.0`
- **Server**: .NET 10.0

## Extension Development

Plugins implement `IPhinixExtensionModule` and register handlers, renderers, and APIs through `IExtensionBuilder`:

```csharp
public class MyExtension : IPhinixExtensionModule
{
    public string ExtensionId => "my.extension";

    public void Register(IExtensionBuilder builder)
    {
        builder.AddCapability("my.extension");
        builder.AddClientMessageHandler(this);
        builder.RegisterApi<IMyService>(this);
    }
}
```

Full guide at [设计哲学.md](./设计哲学.md).

# Credit

Special thanks to the original Phinix creators and contributors, and to [Longwelwind's Phi mod](https://github.com/longwelwind/phi) for the earlier foundation.
