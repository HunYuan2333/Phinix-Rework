# Local development and verification

Default route for independent DLL plugins. Data-only themes need no C# project; use [theme-package.md](theme-package.md).

## Create a minimal project

1. Prepare SDKs, both game-reference sets and matching Host/API3 public DLLs using [environment.md](environment.md).
2. Copy the entire [minimal-plugin](../templates/minimal-plugin/Minimal.csproj) directory into an independent plugin root's example/ folder. Rename package ID, module ID, assembly, namespace and settings prefix. Keep test identity separate from production packages.
3. This is a net472 native-tab sample. Set PhinixRoot or the three Phinix*AssemblyPath properties for public references, and GameReferences for 1.6 DLLs. Do not reference Client.csproj or distribute Host/game DLLs.
4. Manage ownership through [composition.md](composition.md). Consult the actual Example project for full neutral pages, actions and localization; the minimal template does not cover every public capability.

```sh
dotnet build example/Minimal.csproj -c Release   -p:PhinixRoot="/absolute/path/to/Phinix-Rework"   -p:GameReferences="/absolute/path/to/rw16-references"   -o "/absolute/path/to/scratch/minimal16"
```

Use a new temporary output directory rather than installed DLLs. Template package-config.json declares only the net472 build's 1.6 support. Listing a second game version does not turn one DLL into a dual-runtime package.

## Build a dual-runtime package

Use the actual Host helper command in [dual-runtime.md](dual-runtime.md). It locates a project in example/ or Client/, generates an isolated net10 project, builds both variants and packages one ZIP. The template does not include pack.py; use a wrapper only when it exists and its arguments have been checked.

The current helper does not read template package-config.json. It emits default compatibility ranges and empty dependencies. Additional assemblies, module dependencies, external Mods or higher minimum Host versions require explicit custom packaging configuration and reference-closure checks. Do not omit declarations. This configuration covers compatibility and dependencies; package identity, input DLLs and output paths still come from CLI arguments.

## Offline preflight

Run both commands on the final ZIP, replacing placeholder paths. See dual-runtime.md for building the net10 Tool.

```sh
dotnet /absolute/path/to/ManagedPackageTool.dll   --validate /absolute/path/to/plugin.zip --game-version 1.6

dotnet /absolute/path/to/ManagedPackageTool.dll   --validate /absolute/path/to/plugin.zip --game-version 1.7
```

Both exits must be 0 with staticValidation=passed in JSON. Without --host-assembly, hostReferencesChecked is false and does not prove reference closure. For closure checks, supply complete lawful game and public Host dependencies for the selected variant, following the helper's actual preflight arguments. Do not mix runtime DLLs.

## Game sideloading

Open Phinix extension management in the matching game, enable developer mode, install the local ZIP and restart. Restart follows current Host binding/activation policy, not a claim that CLR cannot load assemblies dynamically. Inspect installed version/digest, loaded version, module state and behavior. Installation success does not imply activation success.

Test 1.6 and 1.7 separately: repeated enable/disable, persisted settings, save exit/change, view closure and stale callbacks. Appearance changes also need visual inspection. Data-only themes need neither developer mode nor restart; installation still requires selection and apply.

## Failure diagnosis

| Symptom | First check |
| --- | --- |
| CandidateHostIncompatible | Current CompatibilityVersion, game version and package ranges; do not change constants just to bypass checks |
| ProtectedAssembly | Host/game/protected dependencies accidentally included in ZIP; package only declared private dependencies |
| VariantContractMismatch | Matching module/assembly identities, full assembly names and dependencies across variants |
| ReflectionTypeLoadException | LoaderExceptions, actual reference closure and runtime, not just the outer exception |
| Installed without a tab | Discovery/activation state, entry contract, assembly binding and restart |

Handle same-version changed digests, downgrades and source conflicts through actual installation rules. Do not edit receipts or replace installed files directly. Preserve settings/exports before switching production and development sources through normal uninstall/restart flows.
