# Environment, versions and compile references

Prepare tools for the current task. Theme packages do not need a full game environment. [basis.md](basis.md) is a snapshot, not a replacement for checking the actual checkout.

## Distinguish versions

| Version | Declaration | Purpose |
| --- | --- | --- |
| Product/file | `Client/Source/Properties/AssemblyInfo.cs` | Identifies a build to users; does not alone determine package compatibility |
| Host capability | `Client.CompatibilityVersion` | Compared with manifest phinixRange |
| Client API | `ClientAbstractionsCompatibility.Version` | Compared with abstractionsRange; current API3 |
| CLR AssemblyVersion | Project/AssemblyInfo | Binding identity; do not globally replace it with the product version |
| Plugin/theme package | Actual ZIP manifest and Release | Do not substitute a project's initial value for the final CI-overridden version |

Source and reference DLLs may differ. Check HEAD, status, Common gitlink and submodule status, then actual DLL identities/TFMs/capability constants and final manifests. Working-tree edits are not automatically on remote main; remote publication does not prove a player downloaded it.

Preserve existing changes and the chosen Common commit; do not update --remote. If vendored protobuf's SDK pin blocks evaluation, set `MSBuildSDKsPath` to an installed SDK's Sdks directory for this process, without changing pins/global.json.

## Prepare by task

| Task | Inputs |
| --- | --- |
| Theme packaging/validation | .NET 10 SDK/runtime, source or built ManagedPackageTool, config/XML; no game DLLs |
| Single-runtime template build | Matching Host public references and game references, net472 targeting pack or .NET 10 SDK |
| Maintained dual-runtime helper | Python matching the actual script syntax, .NET 10 SDK, both game reference sets, matching Harmony, fixed Host/Common |
| GitHub publication | User authorization and repository permissions; local development requires neither gh nor remote credentials |

Use `dotnet --list-sdks` / `--list-runtimes` to check actual installation. Building net472 with lawful reference assemblies does not make 1.7 DLLs executable on .NET Framework. Game/private compile assets remain build inputs, never public package content.

`ClientPresentation.Contracts` contains neutral presentation/theme/action contracts. `ClientExtensionAbstractions` still provides game bridges, layout and native controls. They are separate assemblies; do not assume every API type lives in one DLL. Do not depend on internal `Client.csproj` / PhinixClient implementations.

Command examples use POSIX shell. Use single lines or argument arrays in PowerShell rather than copying backslash continuations. Report missing target references and continue independent editing/validation; do not fetch unidentified DLLs as substitutes.
