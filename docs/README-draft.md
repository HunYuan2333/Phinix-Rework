<h1 align="center">phinix-rework</h1>
<h4 align="center"><i>A reworked RimWorld chat and trading mod built on top of Phinix</i></h4>
<p align="center"><img src="../Client/About/Preview.png" alt="phinix-rework preview"></p>
<br><br>

> Draft README for review only.  
> The production README in `.github/README.md` is intentionally left unchanged for now.

中文版本: [README-draft.zh-CN.md](./README-draft.zh-CN.md)

# About
`phinix-rework` is a continuation and rework of **Phinix**, the RimWorld chat and trading mod originally created by the Phinix team.

Like the original project, it allows players to:

- chat with each other in-game
- trade items between colonies
- use an external dedicated server
- handle asynchronous trade flow without requiring both sides to respond instantly

This rework keeps that foundation, but puts more emphasis on long-term maintainability and extension support.

Compared with the original mod, this rework is not just a rename. It is gradually restructuring Phinix into a cleaner framework-driven architecture so future features and submods can plug in more naturally.

Current rework direction includes:

- a framework-oriented message pipeline on top of the existing client/server flow
- capability negotiation between client and server
- reflection-based extension discovery
- clearer separation between default built-in behavior and extension behavior
- legacy fallback behavior when framework support is unavailable
- groundwork for custom message types, renderers, item codecs, and trade completion handlers

In short, the project is moving from a fixed built-in implementation toward an extensible base that still preserves the original Phinix experience.

# What's New In The Rework
The original Phinix already offered a strong gameplay base, including:

- chat message timestamps
- unread message alerts
- two-way trading GUI
- asynchronous trades
- configurable chat presentation

`phinix-rework` builds on that and focuses on architectural improvements such as:

- moving chat and trading behavior toward framework-managed pipelines
- introducing negotiated feature support between client and server
- preparing the codebase for submods and custom protocol extensions
- improving the boundary between legacy compatibility and newer framework features
- reducing the amount of hardcoded core-only behavior

This makes the project easier to evolve without repeatedly rewriting the same central systems.

# Installation
## Client
1. Build or obtain the client package for `phinix-rework`.
2. Extract it into your `RimWorld/Mods` folder.
3. Install the required Harmony dependency.
4. Start RimWorld and enable the mod.
5. Restart the game if RimWorld requests it.

The repository currently declares support for RimWorld:

- `1.3`
- `1.4`
- `1.5`
- `1.6`

## Server
Server configuration is stored in `server.conf`.

The default server values currently include:

- port `16200`
- max connections `1000`
- authentication type `ClientKey`

To run the server:

1. Open `server.conf` and adjust settings as needed.
2. Build the server project.
3. Run `PhinixServer.exe`.
4. Optionally use the server console commands such as `help` and `version`.

## Docker
This repository still contains Docker files for server deployment:

- `Dockerfile`
- `docker-compose.yml`

That means container-based server hosting remains part of the intended workflow for the project.

# Usage
## Client
1. Load a save or create a new colony.
2. Open the `Chat` tab in-game.
3. Open `Settings`.
4. Enter the server address and port.
5. Connect to the server.

When connected successfully, the chat UI should become active and the user list should update to show online players.

## Server
The server is designed to:

- accept client connections
- authenticate users
- synchronize chat
- synchronize trade state
- expose framework capabilities for newer clients when supported

The rework also aims to keep graceful fallback behavior when newer framework features are not available on both sides.

# Developers
## Setting Up Your Environment
### Game DLLs
The client project depends on RimWorld assemblies from the game install directory, typically under:

`<RimWorldDir>/RimWorldXXX_Data/Managed/`

The required DLLs should be placed in `GameDlls/`.

Common examples include:

- `Assembly-CSharp.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `UnityEngine.IMGUIModule.dll`
- `UnityEngine.InputLegacyModule.dll`
- `UnityEngine.TextRenderingModule.dll`

If you keep multiple RimWorld versions, version-specific DLL folders can also be used under `GameDlls/`.

### Building
The repository includes:

- `Phinix.sln`
- client, common, and server C# projects
- a `TravisCI` build profile for builds that do not require the RimWorld game assemblies

If you only need to build the shared libraries and server side, the non-client profile remains the easier route.

### Protocol Work
This project uses Protobuf for packet definitions and generated packet code.

If packet structures are changed, you will also need an appropriate `protoc` toolchain with C# support.

## Rework Focus For Developers
If you are reading this repository as a developer, the most important difference in `phinix-rework` is the shift toward framework extension points.

Ongoing architecture work is centered around:

- framework envelopes
- capability negotiation
- extension discovery
- client/server message handlers
- renderers and interceptors
- item pipelines and trade completion contracts

The long-term goal is for future gameplay additions to integrate through these interfaces instead of patching the core mod repeatedly.

# Credit And Thanks
Special thanks to the original **Phinix** creators and contributors for building the codebase this rework is based on.

This project also acknowledges the earlier lineage behind Phinix itself, including [Longwelwind's Phi mod](https://github.com/longwelwind/phi).

The original work made this rework possible, and that foundation deserves clear credit.

# Draft Notes
This version is meant to help shape the final README before replacing `.github/README.md`.

Possible next improvements:

- add release/download links once the new distribution target is finalized
- add screenshots for chat UI and trade UI if you want a more visual README
- continue polishing the Chinese companion README and keep both versions in sync
