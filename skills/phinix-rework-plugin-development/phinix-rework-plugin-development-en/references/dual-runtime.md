# Dual-runtime builds and package layout

Deliver one ZIP per plugin product version with independent 1.6/net472 and 1.7/net10.0 builds. Installation selects the actual game profile; copying one DLL into two directories is not a dual build.

## Keep three formats separate

| Artifact | Selector | Layout |
| --- | --- | --- |
| Main mod | RimWorld About/LoadFolders and Host-owned loading boundaries | Use the actual Host packager, not a managed ZIP layout |
| Managed plugin | Common parser and Host installation/startup | Root schemaVersion 2 manifest with complete schema1 variants; files under `1.6/` and `1.7/` |
| Data theme | Theme reader | schemaVersion 1, manifest + Themes XML, no runtime variants |

```text
my-plugin.zip
  manifest.json
  1.6/Assemblies/Author.Minimal.Plugin.dll
  1.7/Assemblies/Author.Minimal.Plugin.dll
```

Localization resources use the matching game prefix. Paths inside each variant remain `Assemblies/...` and `Resources/...`; the reader adds the game prefix. The outer JSON has only `schemaVersion` and `variants`. Do not create `variants/net472/` directories or read nonexistent top-level `manifest.version`.

Each schema2 variant is a full schema1 declaration containing package identity, version, targetFramework, compatibility, modules and assemblies. Generate it using the real tool; do not invent lengths/digests or use ellipses as JSON values. Parsed C# objects support `ForGame(game).Version`; raw JSON consumers must first verify agreement across variants.

## Build and preflight

Use lawful, version-accurate game references, matching Host public references and Harmony for each runtime. Common 1.7 references include Assembly-CSharp, UnityStubs and Waylith; verify local DLLs and scripts instead of guessing Harmony versions.

Independent projects live in `Client/` or `example/`. After placing the minimal template in `example/`, the current maintained Host helper can be used:

```sh
python3 /path/to/Phinix-Rework/tools/compatibility/build-managed-dual.py --host /path/to/Phinix-Rework --plugin /path/to/my-plugin --game16 /path/to/rw16-references --game17 /path/to/rw17-references --harmony17 /path/to/rw17/0Harmony.dll --version 0.0.1 --output /path/to/artifacts/my-plugin-0.0.1.zip
```

Keep work/output outside source and reference trees and use fresh paths. If the plugin already has `pack.py`, check its actual arguments. Maintained wrappers currently offer `--phinix-root` and related options; new projects do not automatically have that wrapper.

**Current helper limits**: it uses the maintained project/identity layout and generates fixed Host `>=0.10.1 <1.0.0`, API `>=3.0.0 <4.0.0`, empty package dependencies and externalMods. Do not assume it reads author package-config files or discovers all private libraries. For nondefault capability ranges, dependencies, external Mods or additional assemblies, adapt the plugin's packaging workflow or use ManagedPackageTool explicitly. An empty dependency declaration is not complete delivery.

```sh
dotnet /path/to/ManagedPackageTool.dll --validate /path/to/artifacts/my-plugin-0.0.1.zip --game-version 1.6
dotnet /path/to/ManagedPackageTool.dll --validate /path/to/artifacts/my-plugin-0.0.1.zip --game-version 1.7
```

The tool emits `staticValidation="passed"`. `hostReferencesChecked` is true only when actual required `--host-assembly DLL` arguments are supplied (repeatable). Structural validation without references does not establish dependency closure. The maintained helper performs both runtime audits/preflights; this still does not establish game acceptance.


Build the tool from the Host root with `dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj -c Release`; its default output is that project's bin/Release/net10.0/. It depends on Common and vendored protobuf. Preserve exact submodule pins, restore missing dependencies within authorization and do not change pins to bypass failures. Use an isolated copy when preserving a baseline.

## Contract and dependency boundaries

`ManagedExtensionManifestReader.SameVariantContract` checks package ID/name/version, Host/API ranges, full assembly identities and module/dependency/externalMods declarations. Runtime DLL bytes can differ; public contracts must not casually diverge.

Do not package game, Unity, Harmony, Host/contracts or libraries already supplied by Host. Current Common protected-name rules govern `ProtectedAssembly` validation; do not freeze an outdated allowlist. Private dependencies require redistribution rights, explicit declarations and verified runtime output/closure. The tool does not invent missing dependencies.

Keep runtime differences in narrow adapters. Future removal of 1.6 should remove its artifacts and selection paths rather than untangle business logic from rendering. DLL install/update/removal requires process restart under Host's loading/binding policy, not because CLR cannot dynamically load DLLs at all. Verify actual installation and behavior on each game version.

Source: `tools/compatibility/build-managed-dual.py`, `Extensions/PluginStore/Tools/ManagedPackageTool/{VariantPackageCommand.cs,OfflinePreflight.cs}`, Common `ManagedExtensionManifestReader.cs` / `ManagedExtensionZip.cs`.
