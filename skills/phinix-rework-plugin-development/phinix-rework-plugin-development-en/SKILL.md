---
name: phinix-rework-plugin-development-en
description: Guide or develop Phinix Rework managed plugins and RimWorld mods integrating with Phinix, including requirement and route selection, public APIs and DI, independent builds, local ZIP sideloading and iteration, compatibility and recovery, and Release/Index delivery. Use for new plugins, tabs, messaging or inventory features, and build or release troubleshooting.
---

# Phinix Rework Plugin Development

Understand the user first, then read references for the current stage. Use the same technical and acceptance standards for beginners and experienced developers. Explain necessary terms and give beginners a manageable set of executable steps; discuss interfaces, constraints, and tradeoffs directly with technical users.

## Start and choose a route

When information is missing, ask a few short questions: guided steps or direct implementation; feature, scenario, and intended recipients; RimWorld/host version and local trial or public release. Do not repeat questions already answered, permissions already granted, or acceptance confirmations already recorded. A simple tab needs only a short scope and acceptance criteria. For stateful, item, or multiplayer features, identify dependencies, data owners, recovery, and acceptance before splitting the work.

The default path for a simple managed DLL is: confirm requirements and host capabilities → organize a minimal feature in one project → build independently and preflight → developer sideload and verify after restart. This skill includes the engineering guidance needed for small plugins; an external mod skill is unnecessary. When choosing the mod route, combine skills within the user's authorization.

Read on demand; do not load every repository first:

| When to read | Reference |
| --- | --- |
| Requirements are unclear or involve native game features | [Requirements and routes](references/routes.md) |
| Starting a build, uncertain versions, missing tools, or preparing a release | [Environment and versions](references/environment.md) |
| Organizing new managed-plugin code, adding a feature, or clarifying responsibilities | [Lightweight engineering](references/engineering.md) |
| Implementing modules, services, or lifecycle | [DI and ownership](references/composition.md) |
| Implementing the relevant UI, messaging, or inventory capability | [Capability entry points](references/capabilities.md), relevant section only |
| Local compilation, packaging, preflight, sideloading, or revision | [Local development loop](references/local-loop.md), the primary route |
| Acceptance, upgrades, recovery, or troubleshooting | [Verification and recovery](references/verification.md) |
| Formal DLL Release/Index, notification or store-update troubleshooting, and Workshop delivery | [Formal publication](references/publication.md) |
| Main CI, post-release notification, or failure retries | [Actions practice](references/actions.md) |
| Assessing evidence and current delivery limits | [Evidence and status](references/basis.md) |
| Maintaining this skill or reviewing guidance scenarios | [Validation record](references/validation.md) |

A public Release, successful notification/scan, admission/catalog publication, and store visibility are separate outcomes. Read publication/Actions references for release or Index troubleshooting; a green job does not establish every update succeeded.

## Essential boundaries

- This is a development-guidance draft. Confirm actual support for new DI, configuration/preflight, and sideloading in the target host. Record workspace implementation, commits, CI pins, release packages, and game acceptance separately; none substitutes for another.
- Read applicable AGENTS.md, branches, and complete changes, including untracked files and submodule modifications. Preserve parallel work. Pin the host and Common/protobuf gitlinks; never use `submodule update --remote` or infer current remote state from stale origin refs.
- The project is **Phinix Rework**. Canonical repositories are `Phinix-Rework`, `Phinix-Rework-Common`, and `Phinix-Rework-Server`. Naming cleanup does not change protocols, assemblies, namespaces, IDs, persisted keys, or `Dependencies/Phinix.Common`.
- Prefer `ClientExtensionModule.Compose` and neutral DI for new plugins. Keep constructors passive and partial failures cleanable. Do not dispose borrowed services; pair cleanup with owned resources/subscriptions. Operate on game objects and GUI on the main thread; delayed actions recheck their current context.
- Depend only on public contracts, not host internals or a concrete container. Do not tailor the host to one plugin or scan/manage third-party mods. API discovery does not imply that a dependency is enabled or active.
- Preserve authoritative acknowledgement, whole-batch delivery, and idempotency. After dispatch with an unknown outcome, retain custody records and reconcile; never automatically refund or replay rewards.
- Local development needs no GitHub account, token, Release, or Index. Prefer independent builds and developer sideloading. Do not disguise a managed plugin as a traditional mod for trials or add hot reload, generators, signatures, dedicated host APIs, or another Phinix NuGet SDK.
- Finish local preparation first. Pushes, Issues/PRs, publication, software installation, and external skills require the user's authorization; loading this skill does not grant it. Without permission, stop at reviewable artifacts and explain the specific reason. Do not ask again while existing authorization remains valid.

Deliver completed behavior/guidance, verification evidence and its limits, target versions, and remaining work. State missing game or remote verification; do not declare formal acceptance yourself. Record required host enhancements as separate follow-up work.
