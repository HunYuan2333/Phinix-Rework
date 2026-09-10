# Continuous integration

`Full project build` runs for pull requests targeting `dev` or `main`, pushes to
those branches, and manual dispatches. Feature branches are checked through their
PRs to avoid duplicate push/PR builds. No repository secrets are required.

| Runner | Build | Executed tests |
| --- | --- | --- |
| Windows | Full `Phinix.sln`, `Release 1.6` (client, common libraries, all official extensions and server) | Framework runtime and responsive UI geometry |
| Linux | .NET 10 server and its official extensions | Framework runtime and responsive UI geometry |

## Toolchain and references

- .NET SDK **10.0.401** for the current `net10.0` server and shared projects.
- Client assemblies continue to target **.NET Framework 4.7.2** using the Windows
  runner's targeting pack. The supported game version is **RimWorld 1.6**.
- [Krafs.Rimworld.Ref](https://github.com/krafs/RimRef) **1.6.4871** provides public,
  compile-only game/Unity references in `GameDlls/1.6`. The obsolete
  `RIMWORLD_DLLS_PREFIX` secret is no longer used, including for fork PRs.
- Windows installs **protoc 34.1** with a checked SHA-256, matching the pinned
  protobuf submodule. The existing project targets regenerate protocol sources.
- `MSBuildSDKsPath` selects the installed SDK for the build process without editing
  protobuf's vendored `global.json` or removing its SourceLink dependency.
- NuGet **7.0.0** restores the legacy client HintPath dependencies. SDK projects
  restore through `dotnet build`.

When updating the SDK, game reference package, or protobuf submodule, update the
corresponding workflow versions (and protoc checksum) together and check both jobs.
Action versions are pinned by commit with their major versions in comments.

## Artifacts and validation limits

The Windows job uploads the generated `Output/phinix-rework` client, excluding
copied source directories. Its `LoadFolders.xml` selects `Common` and `1.6`.
Both jobs upload `Server/bin/Release/net10.0`, including the four official
Chat/Trade contract and server extension DLLs in `Extensions/`. Artifacts are
retained for 14 days. The server requires a .NET 10 runtime.

Before upload, CI checks required host/extension files and the client load-folder
XML, and rejects game/Unity reference DLLs in either artifact. Missing build
outputs fail the job rather than producing an empty or incomplete download.

The framework runtime harness links the game-independent client endpoint sources
because their production projects target `net472`, while the harness runs on
.NET 10. The geometry harness uses its existing Unity stubs. Reference assemblies
cannot execute game code: passing CI proves compilation and these isolated tests,
not in-game chat, trade, save/load, or multiplayer behavior. Game-dependent runtime
tests such as `LegacyTradeRuntimeTests` still require real game assemblies and
are not executed by this workflow.
