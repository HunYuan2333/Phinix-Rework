# Local development loop: the primary route

Read for compilation, packaging, preflight, sideloading, diagnostics, and development revisions. This loop requires [environment.md](environment.md) to confirm modern DI, current tools, and sideload support. Identify versions first for unknown/older release hosts.

For new projects, choose a minimal layout through [lightweight engineering](engineering.md). Reorganize existing projects only when responsibilities/state are mixed; do not start with a broad refactor.

## 1. Give an Example-derived plugin its own identity

Copy a verified fixed modern Example snapshot into an independent directory, preserving the original repository's changes. Check `.csproj` AssemblyName, PluginPackageId, PluginDisplayName, version, source namespace, PhinixExtension attribute/ExtensionId, settings prefixes/SectionId, localization, and dependencies together. Do not rename only the ZIP or edit generated manifests. Current Example explicitly lists compile inputs: update Compile Include when splitting files, and pack.py project lookup when renaming the project; new source files are not necessarily compiled automatically. Prefer a development ID distinct from formal packages. When preparing formal identities, revisit resources and persistence migration; changing package names alone cannot conceal compatibility problems.

## 2. Configure compatibility and dependencies

Current Example `pack.py` reads `example/package-config.json` and passes `--config` to the tool. It has no general `--config` CLI option of its own. Other plugin pack.py interfaces differ; read their script/help first. Current configuration example: ranges illustrate this iteration's capabilities and must be verified/narrowed for the actual target.

```json
{
  "compatibility": {
    "rimWorldVersions": ["1.6"],
    "phinixRange": ">=0.9.7 <1.0.0",
    "abstractionsRange": ">=1.9.0 <2.0.0"
  },
  "dependencies": [],
  "externalMods": []
}
```

Package dependency: `{"packageId":"author.provider","versionRange":">=1.0.0 <2.0.0","optional":false}`; external mod: `{"packageId":"author.mod"}`. The same manifest parser rejects unknown/duplicate fields, invalid ranges, and other bad inputs. Explain degradation for optional dependencies; reject activation when required capabilities are missing rather than silently delivering partial success.

## 3. Independent build, canonical ZIP, offline preflight

Prepare host `Common/Assemblies` public DLLs once, or built Utils/ClientExtensionAbstractions from a pinned client. Game references remain compilation-only. Current Example targets net472/C# 7.3; the tool targets net10.0. These are POSIX examples: substitute actual absolute paths. On Windows adapt argument passing as described in environment.

```sh
python3 pack.py --phinix-root "/path/to/Phinix-Rework" \
  --host-assemblies "/path/to/host/Common/Assemblies" \
  --game-references "/path/to/RimWorld/Managed" \
  --packager "/path/to/ManagedPackageTool.dll" \
  --output "/path/to/build-1.zip"
```

pack.py builds only this plugin, packages it, and preflights automatically; it recognizes numerically prefixed host DLLs. Without `--host-assemblies`, it uses built client public DLLs; existing tools are reused. To prepare the source tool in a pinned client root, run `dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj -c Release -m:1`, with complete pinned submodules and no game references required. If game references lack mscorlib, Example can use a unique local net472 reference cache or explicit `--framework-references`; missing caches do not imply success.

Without Python, run `dotnet build -c Release -m:1` for the independent plugin project, supplying actual legal files through `-p:GameReferences=...`, `-p:PhinixUtilsAssemblyPath=...`, and `-p:PhinixAbstractionsAssemblyPath=...`, then invoke the tool:

```sh
dotnet "/path/to/ManagedPackageTool.dll" \
  --assembly "/path/to/My.Plugin.dll" --package-id author.plugin \
  --name "My Plugin" --version 1.0.0 --config "/path/to/package-config.json" \
  --language-file "/path/to/en-US.json" --language-file "/path/to/zh-CN.json" \
  --output "/path/to/build-1.zip"
dotnet "/path/to/ManagedPackageTool.dll" --validate "/path/to/build-1.zip"
```

`--assembly`, `--language-file`, and `--host-assembly` are repeatable; adjust languages to actual resources. Preflight performs no network, installation, or DLL execution. Supply repeated `--host-assembly` inputs for all actual CLR references: host, game/Unity, framework, and required dependencies, to verify identities/conflicts. Reference files do not enter the ZIP. Without host inputs, report static validation only; even with them, this does not prove game loading, actual support for the version range, or enabled dependencies.

## 4. Developer sideload and diagnostics

Enable RimWorld developer mode → Phinix Store “dev: Install local plugin ZIP” (overflow menu in narrow windows) → absolute ZIP path or explicitly chosen directory to browse → verify package ID/version/SHA-256 → confirm → restart. Browse only the explicit directory, not the whole filesystem. Import freezes validated bytes in host temporary storage; confirmation/execution recheck mode. Canceling uncommitted intent releases temporary input; withdrawing mode does not roll back committed transactions.

After restart, verify local source, module discovery/activation, dependencies/reason codes, current-session versus installed digests, and current versus next-start states in extension management. Use “Copy summary” for troubleshooting, not a full log containing private data. If installed digest changes while current digest is old, restart first; do not claim new code is running.

## 5. Iterate and manage

| Situation | Behavior and next step |
| --- | --- |
| Same local ID/version, different ZIP digest | Development revision; old package must be enabled/trusted; build to a new path, import, then restart |
| Duplicate digest | Reject repeat import; check that a new build was actually created |
| Version downgrade | Reject; do not bypass by editing receipts |
| Same ID from a formal source | Reject replacement; use a distinct development ID, or uninstall normally and restart to finish removal before switching sources |
| Developer mode disabled | Installed local packages remain manageable; new confirmation/execution is rejected; no hot replacement |
| Enable/disable/uninstall | Store and extension management synchronize next-start intent; current loaded DLLs remain unchanged |
| Cancel uninstall | Retain disabled state and module selections; do not automatically restore previous enabled state |

Local sourceKind is `local-development`; schema 2 local receipts do not impersonate Index approval, while formal schema 1 receipts remain readable. This is separate from public Index catalog versions or protocol retirement. Before downgrading to a schema-1-only host, normally uninstall local packages on the newer host and restart to complete removal. Do not carry unfinished transactions across incompatible versions or manually delete journals/receipts. Business data follows existing retention rules; enabled consumers' dependencies must not be broken.

Complete game acceptance in [verification.md](verification.md) before formal publication. Local sideloading does not automatically create a formal catalog package.
