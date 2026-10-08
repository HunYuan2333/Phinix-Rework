# Phinix Example Plugin

[中文](README.zh-CN.md). A small plugin-author example derived from Playtest with all silver/item generation removed. It uses the same reviewed distribution route as other managed DLL plugins.

- Register a localized tab and settings section with public host contracts.
- Persist a click counter and a “Show explanations” option using package-prefixed keys. Changing the option immediately affects the tab.
- Reset the counter through a confirmation guarded against callbacks from an earlier activation.
- Load English/Chinese JSON, format count placeholders, react to language changes and clean up subscriptions during shutdown.
- Restore GUI state after drawing and use responsive tab hints.

Only the plugin’s own settings change. No map, colony, item, network or save operations are performed. Settings belong to the current game profile, are shared across saves, and survive uninstall/reinstall. This is not a per-save persistence example.

## Try it

Install Phinix Example Plugin from the official store and restart. Open Example, count clicks, toggle explanations in Phinix settings, and test reset/cancel/confirm. Switch English/Chinese; restart and check retained values. Disable/re-enable or uninstall via Extension manager and restart. Compilation and static checks do not replace game acceptance.

## Build and package

Requires .NET 10, a Phinix-Rework development checkout with localization support and your own RimWorld 1.6 references. Release 1.0.0 targets Assembly-CSharp 1.6.9676.18020 and ClientExtensionAbstractions 1.7.0. Never distribute reference DLLs.

```sh
python3 pack.py --phinix-root /absolute/Phinix-Rework --game-references /absolute/RimWorld/Managed --output /absolute/new-output/phinix-example-basic-1.0.3.zip
```

Optional `--bundle-output /absolute/new-folder` produces a developer bundle. Remove manual duplicates before a store installation. Package/module ID: phinix.example.basic; assembly: Phinix.Example.Basic. Old Playtest settings/identity are not migrated.

## Normal publication flow

Commit source, publish a fixed GitHub Release ZIP, and submit exact candidate metadata to [the official index](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/new/choose). After static checks a maintainer adds plugin-approved. Actions create/merge an evidence PR, revalidate, publish the catalog and close the Issue. Authors do not approve themselves, merge metadata or upload DLLs to the index. [Author guide](https://github.com/HunYuan2333/Phinix-Plugin-Index/blob/main/GitHubBotGuide.md).

Playtest remains a separate developer fixture and is excluded from the official catalog. This repository owns example source and releases; the index stores validated metadata only.

## Published example

Version 1.0.0 is available from the official store. The complete normal route is [submission #15](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/15) → [evidence PR #16](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/16) → [successful automatic publication](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37335979507). GitHub and CF downloads were checked against the same SHA-256. Game acceptance remains a manual step; the checklist above covers it.

Technical settings-section IDs identify registrations. This example renders its title through its own localizer; a current host avoids showing untranslated IDs as headings.

## F4-B Compose candidate

This source now derives from ClientExtensionModule and overrides Compose. A module-owned scope registers ordinary services; constructors remain passive, Activate starts localization and Shutdown disposes the scope. Host settings/log services are borrowed. Minimum client abstractions: 1.9; update the complete matching host. Existing settings keys, callback guards and gameplay actions remain unchanged. Candidate versions: Example 1.0.3 / Playtest 1.4.0, not yet published. Historical Package/ files and fixed releases are unchanged. The packager accepts --abstractions-range; specify >=1.9.0 <2.0.0 for these new author entries.
