# Requirements and routes

Read when requirements are unclear or involve Defs, assets, native lifecycle, or Harmony.

Express the desired behavior observably: who acts, what triggers it, which data changes, what survives failure, and who receives the result. A counter tab can start directly from a renamed Example. Inventory transfer or multiplayer needs explicit module, connection, save, and operation state/ownership; split it into small verifiable steps without a long preliminary report.

| Route | File owner and suitable scope | Delivery |
| --- | --- | --- |
| Phinix managed DLL ZIP | Phinix installs/manages plugin-owned DLLs, manifest, and package localization. Suitable for public extension points such as tabs, settings, messages, and inventory | Local sideload; formal GitHub Release → Index |
| RimWorld mod | RimWorld/Steam manages About, Defs, textures/audio and other assets, and assemblies; public contracts can integrate with Phinix | Steam Workshop publication → Workshop index application |

Recommend evaluating the mod route for native game lifecycle, Defs, assets, or substantial Harmony work: game-managed files and startup timing cannot be replaced by managed ZIP installation. Do not force this choice merely because a feature is “complex.” Explain capabilities, maintenance costs, and publication options, then let the user decide. Simple managed plugins do not need a temporary About layout; prefer the sideload loop. The retired store installation route for complete mod ZIPs is unavailable.

After selecting the managed route, use [lightweight engineering](engineering.md) for a minimal single-project implementation. Its organization, verification, and diagnostics guidance ships with this skill; no external skill is required for simple DLLs.

After selecting the mod route, suggest combining this with [RimWorld engineering skills](https://github.com/HunYuan2333/rimworld-mod-engineering-skills). Distinguish permission to download/read from permission to install/activate; ask before an action lacking authorization, and preserve existing authorization for that action. Inspect actual contents, scope, and installation instructions rather than trusting the repository name. The external skill owns game engineering; this skill owns Phinix APIs, contracts, and publication boundaries. Without authorization, continue local design and Phinix interface preparation, stopping before download/activation.

Read `Phinix-Rework-Server` instructions, pinned contracts, and relevant source only when server cooperation is required. Do not load server code for requirements covered by public client capabilities. For older servers, adapters must reject unsupported capabilities or explain degradation; UI must not silently switch protocols and report success.
