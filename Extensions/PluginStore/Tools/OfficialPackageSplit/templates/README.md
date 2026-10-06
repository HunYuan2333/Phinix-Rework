# Phinix legacy plugin

[中文](README.zh-CN.md)

The maintainer selected the normal **public source and GitHub Release** route. Index admission uses static checks followed by the maintainer's own `plugin-approved` label. Do not put GitHub access tokens in the game client. Legacy connection parameters are retained as explicitly requested by the maintainer.

This repository contains one legacy plugin's own Contracts, Client and language resources. See `publication.json` for its identity. It is a managed Phinix DLL package, loaded through the same extension lifecycle as third-party plugins. It is not a RimWorld Workshop Mod.

## Building a candidate

Install Python 3.10+ and .NET SDK 10. Supply a built/installed Phinix Mod and your own RimWorld 1.6 managed assemblies. TalentTrade also requires Harmony 2.3.6. References are compile-only and must never be committed or distributed. Git and GitHub CLI are not needed for a local build.

```sh
python check-source.py
python pack.py --phinix-package /path/to/phinix-rework \
  --game-references /path/to/RimWorldLinux_Data/Managed \
  --harmony-references /path/to/Harmony/Assemblies \
  --packager /path/to/ManagedPackageTool.dll \
  --output /tmp/plugin-candidate.zip \
  --bundle-output /tmp/plugin-candidate \
  --display-output /tmp/plugin-display.json
```

Use the trusted `ManagedPackageTool` built from Phinix's `Extensions/PluginStore/Tools/ManagedPackageTool` project. The ZIP contains only `manifest.json`, two owned DLLs, and language files. It excludes Host/Trade/Inventory/Harmony/game DLLs. Assembly, module, type, settings, codec and storage identities are retained.

## Release boundaries

Source CI checks only source ownership, compile-only references, language declarations and the fixed snapshot. It neither compiles against uploaded game assemblies nor calls a live service. A successful build or static package validation does not prove game behavior.

Both plugins still ship with the main Mod until standalone game acceptance. Do not install this candidate alongside the bundled version: duplicate assemblies/modules must be avoided. RedPacket depends on the existing Trade and Inventory modules and an externally maintained legacy relay; its existing client access parameter remains unchanged by explicit maintainer instruction. TalentTrade uses its existing legacy service, component types and save fields. No new source license is granted by this extraction; upstream author rights are retained.

Publication requires a fixed source commit, an immutable package, a reviewed index Issue and the maintainer's `plugin-approved` label. RedPacket pending-send/restart reconciliation and TalentTrade late GameComponent loading/missing-package resaving are release gates. Until these are passed, candidates are not admitted to the official catalog and bundled DLLs remain.
