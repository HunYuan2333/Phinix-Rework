# AGENTS.md

## Repository overview

Phinix Rework is a RimWorld 1.6 multiplayer mod with a dedicated server and a plugin-first architecture. The repository contains:

- `Client/`: the RimWorld client host and client-side abstractions; game-facing projects target .NET Framework 4.7.2.
- `Server/`: the dedicated server; targets .NET 10.
- `Common/`: shared networking, authentication, user-management, and utility code; most projects target both `net472` and `net10.0`.
- `Extensions/`: official Chat and Trade plugins plus legacy protocol/feature adapters.
- `Tests/`: executable regression-test harnesses rather than a conventional unit-test framework.
- `Dependencies/protobuf/`: a vendored git submodule. Do not edit it for repository-specific build fixes.
- `Output/`: generated client packages. Treat it as build output unless a task explicitly concerns packaging.

Read `docs/Design-Philosophy.md` before changing extension boundaries or framework APIs. Read `docs/Compatibility-Boundaries.md` before changing legacy protocol, trade state, item ownership, acknowledgements, or recovery behavior.

## Architecture rules

- Preserve plugin parity: official extensions must use the same discovery, registration, activation, and shutdown path available to third-party extensions.
- Keep shared extension contracts in the appropriate Contracts or abstraction project. Do not make extensions depend on host implementation details when an abstraction belongs in `ClientExtensionAbstractions` or a shared contract.
- Keep endpoint-specific networking out of shared Common projects; client and server adapters belong in their respective endpoint projects.
- Keep legacy wire-format and capability differences inside explicitly named legacy adapters. Do not leak protocol-specific behavior into domain services or UI code.
- Treat server acknowledgements as authoritative. A send attempt or optimistic UI update is not a successful trade operation.
- Preserve all-or-nothing item conversion and delivery semantics. Never silently drop unsupported or malformed items.
- RimWorld/Unity APIs and translation facilities are generally main-thread-sensitive. Marshal callback-driven UI/game work through the existing dispatcher patterns and restore GUI global state on every exit path.

## Editing conventions

- Follow the style of the surrounding code. Much of the client uses classic namespace blocks and C# syntax compatible with its .NET Framework/Unity toolchain; do not introduce newer language/runtime APIs without confirming all target frameworks support them.
- Several legacy-format csproj files disable implicit source discovery by listing Compile items explicitly. When adding, moving, or renaming a `.cs` file, verify that every affected project includes it.
- Keep protobuf compatibility overrides in `Directory.Build.props` or `Directory.Build.targets`; do not modify the vendored protobuf `global.json` or source solely for local SDK compatibility.
- Do not edit generated protobuf sources directly. Change the source `.proto` and use the repository's existing generation/build path.
- Avoid broad formatting or cleanup unrelated to the requested change. Preserve existing user changes in a dirty worktree.
- Never commit credentials, server state, logs, RimWorld assemblies, build output, or local IDE files.

## Build and validation

Use the narrowest validation that exercises the changed area, then expand when practical.

```powershell
# Server and server-side dependencies
dotnet build Server/Server.csproj --configuration Release

# CI runtime regression harness
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release

# Responsive layout regression harness
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release

# Full Windows client/server build; requires .NET 10, .NET Framework 4.7.2
# targeting support, restored legacy NuGet packages, protoc, and RimWorld 1.6
# reference assemblies in GameDlls/1.6.
dotnet build Phinix.sln --configuration "Release 1.6" --no-incremental
```

The full client build depends on compile-only RimWorld/Unity assemblies described in `.github/workflows/mono.yml` and `docs/CI.md`. Do not claim in-game validation from a successful compile or from the game-independent test harnesses.

Tests are console applications and report failures through a non-zero exit code. When changing geometry/layout helpers, add deterministic assertions to `ResponsiveUiGeometryTests`. When changing framework runtime behavior, update the closest runtime regression harness. Game-dependent legacy trade tests have additional MSBuild/runtime instructions in `docs/Compatibility-Boundaries.md`.

## Documentation and generated artifacts

- Stable architecture and compatibility documentation belongs under `docs/`.
- Planning, migration notes, audits, and other branch-specific working documents belong under `docs/branch-local/<branch>/`; see `docs/branch-local/README.md` for merge behavior.
- Update English and Chinese documentation together when both variants cover the changed behavior.
- If packaging behavior changes, validate expected contents with `.github/scripts/check-artifacts.ps1` and ensure game/Unity reference DLLs are not copied into distributable artifacts.

## Before handing off

- Review `git diff` and `git status`; do not include unrelated or generated changes.
- Report the exact validation commands run and any validation that could not be run because game assemblies, SDKs, or external tools were unavailable.
- Call out compatibility, persistence, protocol, or item-ownership risks explicitly; never hide them behind a successful build.
