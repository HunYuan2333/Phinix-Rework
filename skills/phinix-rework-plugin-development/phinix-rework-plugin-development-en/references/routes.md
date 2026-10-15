# Requirements and delivery routes

Identify the observable result, state owner and acceptance check. Implement a verifiable slice for small features; include rejection and unknown-result retention for network/item features.

| Route | When to use | Delivery |
| --- | --- | --- |
| Managed DLL plugin | Public-contract tabs, actions, messages or inventory interaction | Local ZIP or approved Release/Index; current dual-runtime envelope is schema2 |
| Data theme | Colors, parameters and inheritance expressible through existing keys | schema1 data package shared by both runtimes; no DLLs |
| RimWorld Mod | Game-managed Defs, native assets, startup lifecycle or substantial Harmony integration | Standard Mod / Steam Workshop; not a complete Mod installed as managed ZIP |

Choose from actual assets and loading requirements, not complexity alone. Preserve an already selected route. Explain a concrete capability gap before changing it. Read/modify server implementation only when the feature requires server support.

For managed plugins, read [engineering.md](engineering.md), [composition.md](composition.md) and [local-loop.md](local-loop.md); for themes, [theme-package.md](theme-package.md). Simple plugins need no additional engineering skill installation. If the user chooses a full Mod, consult additional engineering material as needed and authorized; a skill or repository name does not authorize installing/enabling it.
