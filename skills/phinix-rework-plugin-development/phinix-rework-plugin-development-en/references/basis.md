# Evidence and maintenance

Reviewed on 2026-10-15 (Asia/Singapore) against actual local source and delivery artifacts. Working changes are not automatically pushed main; earlier phase records are not the current implementation.

## Primary evidence

| Topic | Repository location |
| --- | --- |
| Architecture and business boundaries | Phinix-Rework/docs/Design-Philosophy.md and docs/Compatibility-Boundaries.md |
| Composition and services | Client/ClientExtensionAbstractions/Framework/ and Dependencies/Phinix.Common/Common/Utils/Framework/FrameworkTypes.cs |
| Neutral presentation | Client/PresentationContracts/ and Client/Source/UI/ |
| Native UI boundaries | Client/ClientExtensionAbstractions/UI/, UI/ClientWidgets.cs and Game/ |
| Inventory | Extensions/Inventory/Contracts/InventoryContracts.cs |
| Dual-runtime builds | tools/compatibility/build-managed-dual.py; actual plugin pack.py, example/*.csproj and ci/config.json |
| Package and theme validation | Extensions/PluginStore/Tools/ManagedPackageTool/ and Common/Utils/Framework/Managed*.cs |
| Index publication and theme admission | Phinix-Plugin-Index workflows, sources and publication scripts; verify actual repository layout |

Current working product/CompatibilityVersion is 0.10.4 and API is 3.0.0. This is a dated snapshot, not a future minimum or latest-remote claim. Check product, compatibility, API, CLR identity and CI pins independently. CI can override source versions; final published ZIPs/manifests take precedence.

This review did not re-query remote release state. Publication, tokens, Action runs and live caches require contemporaneous SHA/run/HTTP/artifact evidence, not conclusions drawn from skill text. Historical skill checks in [validation.md](validation.md) retain their original dates and scope.

## Future maintenance

For API changes, update declarations, quick reference, copyable templates and both languages together. For packaging changes, check actual CLI arguments and output JSON. For publication policy changes, inspect DLL and theme flows separately. Keep compile references and decompiled game material local, outside the skill and public packages.

The fixed external engineering snapshot in [engineering.md](engineering.md) is design rationale, not a skill installation/execution prerequisite. Historical guidance does not override current user authorization or actual source.
