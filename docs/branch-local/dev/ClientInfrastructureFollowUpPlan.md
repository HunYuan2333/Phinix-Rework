# Client Infrastructure and Follow-up Implementation Plan

Updated: 2026-10-09. Current branch: `dev`. [中文](后续实施计划.md).

This document is the baseline schedule and technical record for Phinix Rework infrastructure refactoring, physical repository separation, and follow-up deliverables.

---

## 1. Stage Acceptance Status Overview

| Stage | Scope & Key Objectives | Acceptance Status & Outcome |
| :---: | :--- | :--- |
| **F0** | Baseline and Dependency Inventory | ✅ **Accepted**. Fixed input baseline, documented target frameworks and artifact responsibilities. |
| **F1** | Lifecycle and Contract Decoupling | ✅ **Accepted**. Delivered `ClientExtensionRuntime`, separated side-effect-free construction from explicit main-thread `Start`, enforced reverse teardown order and exception safety. |
| **F2** | DI Environment and Chat Pilot | ✅ **Accepted**. Pinned Autofac 8.4.0 in client composition layer, migrated Chat to constructor injection and protected scope disposal, game acceptance passed. |
| **F3** | Stateless Operation Flow Pilot | ✅ **Accepted**. Pinned Stateless 5.20.1, migrated Store asynchronous operation transitions while preserving original transaction and recovery boundaries. |
| **F4** | DI Adoption and Old Entry Deprecation | ✅ **Accepted**. Built-in modules (Chat, Inventory, Trade, Store, LegacyAdapter) and bundled test samples (not the standalone Example repository) migrated to `ClientExtensionModule.Compose`. Legacy client entry marked `[Obsolete]`, removal targets host 1.0 / abstractions 2.0 only after migration/rollback acceptance gates. |
| **F5** | Shared Source Consumption Rehearsal | ✅ **Accepted**. Verified shared layer extraction in clean checkouts; both endpoints pinned the same gitlink, multi-targeting builds and nested protobuf tests passed. |
| **F6** | Physical Three-Repository Split & Delivery | ✅ **Accepted**. Completed independent publication and acquisition verification; Server Docker image published to `hunyuan2333/phinix-rework:dev`; Index and Example branches merged to main. |
| **F6-M** | Management and Store State Synchronization | ✅ **Accepted**. Introduced `IClientExtensionControlService`, separated package intent from module settings commands, revision-driven bidirectional refresh, passed 1000 runtime assertions and game acceptance. |
| **F6-S** | Mandatory Plugin Signing Admission | ❌ **Canceled**. Explicitly canceled by the user on 2026-10-08; mandatory signature gating is not implemented, retaining existing hash and integrity checks. |
| **F7** | Author Experience & Automated Builds | 🔄 **Developer foundations first**. Plugin/Index Actions are live and verified. Local sideloading leads the next batch; skills and new author tooling are blocked until the minimum development loop passes acceptance. |

---

## 2. Three-Repository Architecture & Rollback Baseline

### 2.1 Repository Architecture & Pinned Gitlink Rules
The project is canonically named **Phinix Rework**, organized into three separate source repositories:
- **`Phinix-Rework`**: RimWorld 1.6 client mod host, in-game UI, and bundled client extensions.
- **`Phinix-Rework-Common`**: Shared contracts, wire networking, encryption, user directory models, and neutral extension runtime.
- **`Phinix-Rework-Server`**: Standalone dedicated server executable and official server extensions.

**Submodule Gitlink Rules**:
- Both Client and Server repositories pin the Shared repository as a Git submodule at `Dependencies/Phinix.Common` and reference its projects directly during compilation.
- Acquisition and builds must use pinned gitlinks (`git submodule update --init --recursive`). **Never use** `git submodule update --remote`.
- Repository renames do not alter existing assembly names, namespaces, wire protocol identifiers, mod package IDs, persisted storage keys, or the `Dependencies/Phinix.Common` submodule directory name.

### 2.2 Rollback Baseline & Key Commits

Accepted runtime checkpoints: Client `403cea6`, Server `42516ec`, both pinning Common `67f243d`; Common pins protobuf `4b0c3aa`. Roll back endpoint source, its gitlinks and runtime dependencies together. Source rollback is not persisted-data rollback; documentation commits do not replace these runtime checkpoints.
- **Pre-Split Backup Branch**: Preserved locally and remotely on `codex/pre-split-20261008` (commit `0b17036a7ca6edddb8991305deec0718bcca4347`).
- **Unsplit Full Directory**: `/home/hunyuan2333/Phinix/Phinix-Rework-unsplit-backup-20261008`.
- **Server Docker Image**: The canonical publication destination is Docker Hub `hunyuan2333/phinix-rework:dev` (verified via Server commit `42516ec` and Actions Run 37778078886).

---

## 3. Current Stage: F7 Author Experience & Automated Plugin Builds

On 2026-10-09 the user reprioritized F7: implement developer-mode local sideloading first in the next development batch (tomorrow), then complete the minimum third-party development loop. **Skill authoring, template generators and other new author tooling are blocked until these foundations pass acceptance.** This round changes plans only. Existing deployed plugin and Index Actions continue running.

See [Developer foundation delivery plan](Developer-Foundation-Delivery-Plan.md).

### 3.1 Delivered Automated Build Rules for Standalone Plugins
The user explicitly scheduled branch management and automated compilation for `Phinix-Example-Plugin`, `Phinix-Legacy-RedPacket`, and `Phinix-Legacy-TalentTrade` under F7:

1. **Establish Consistent main / dev Branches**:
   - Recheck remote branches and concurrent changes before implementation. Preserve existing branches; create missing branches from confirmed mainline tips.
   - `main` is used for accepted delivery; `dev` is used for active development.
2. **Compile Automatically Only on main Push**:
   - Workflows trigger exclusively on `push.branches: [main]`.
   - Pushes to `dev` must not compile or package. Pull Requests do not compile automatically. Any manual dispatch must also be restricted to `main`.
3. **Consistent Build & Packaging Entry Points**:
   - Use .NET 10 SDK, existing `pack.py`, and trusted `ManagedPackageTool`.
   - Declare an exact client commit and honor its Common/protobuf gitlinks rather than building against a floating host branch.
   - The build summary records plugin commit, host/shared identities, package version, and ZIP SHA-256 digest.
4. **Lawful Game Reference Acquisition**:
   - Maintained repositories use privately provisioned compile references with a pinned digest. Third-party authors provide their own lawful references; access to the maintainer's private repository is not required.
   - Reference DLLs and private credentials must never enter Git, plugin ZIPs, public Actions artifacts, or logs.
5. **Publish Official Releases After Successful main Builds**:
   - A successful main build and package verification publishes a new official GitHub Release with ZIP, digest and build provenance. Failed builds retain an unpublished draft; retries reuse the commit's reserved version and never overwrite released assets.
   - Index scans approved sources hourly, admits eligible updates in batches and publishes one catalog snapshot. New sources and changes outside the approved scope retain the review requirement.
6. **Individual Behavior Acceptance**:
   - Verify for each repository: main push triggers and completes cloud build and packaging; dev push does not trigger compilation; ZIP contains no game/host DLLs and passes static checks.

---

### 3.2 Retained scope decisions

- F4 was closed at the user's accepted scope; this does not invent individual F4-G/F4-H game-step results.
- Whole-mod ZIP installation was canceled as a direction. That scope decision is not evidence that legacy installer code was removed or its boundary defects repaired. Steam/RimWorld manages Workshop mods; the Store lists their metadata.
- Third-party theme selection and Store theme downloads remain later enhancement evaluation, without a new selector, payload kind or download capability.
- The first stable client entry deprecation release was planned as host 0.9.8. A target version or local change is not a published Release; recheck actual versions when publishing.

### 3.3 Optional Release Event Discovery (Deferred Evaluation)

Keep the current system running. A future GitHub App may add immediate stable Release discovery while preserving existing main release Actions, post-release notification Actions, manual entry points, admission/controlled publication and scheduled scanning. Authors need not replace their publishing tools or install the App to qualify. Multiple channels must deduplicate work and retain approval/identity boundaries. See [the event discovery enhancement plan](plugin-store/Release-Event-Discovery-Enhancement-Plan.md). This entry records the plan only.

## 4. Pending Deliverables & Known Issues

The following items remain open work and are not considered closed by this documentation round:

1. **Developer Foundation Delivery**: Local sideloading, a modern DI example, configurable packaging/offline validation, copyable diagnostics and an end-to-end acceptance run. Plugin Actions have passed cloud verification; see [the delivery plan](Developer-Foundation-Delivery-Plan.md).
2. **Red Packet Defect Repairs** (see [RedPacket Repair Plan](plugin-store/RedPacket-Stack-Selection-Optimization-Plan.md)):
   - Sending 200 steel across multiple physical stacks triggers an incompatible-stacks exception (unifying grouping and send validation).
   - A claim summary NullReferenceException was observed in a Root_Entry call chain; its root cause remains unconfirmed (decoupling notification scheduling from domain commits).
   - Repairs are on RedPacket dev `ae5d95f`; 17 algorithm cases and production compilation/packaging passed. Game acceptance and official 1.0.2 publication remain pending.
3. **Talent Trade Known Constraints**:
   - Relies on Harmony patches for pawn lifecycle interception.
   - Active marketplace listings and rental records are tracked in a save-local `GameComponent`. Uninstalling while transactions are in-flight can corrupt save data.
4. **Standalone Plugin DI Migration**: Example, Red Packet, and Talent Trade currently run via legacy `IPhinixExtensionModule.Register`. The Example migration belongs to the developer baseline; RedPacket/TalentTrade migration is separate from defect repair.
5. **Client Legacy Entry Removal Gate**: Planned for host 1.0 / abstractions 2.0. Prerequisites: all maintained plugins migrated, rollback pathways verified, and game acceptance completed.

## 5. Developer Foundation Implementation Entry

- **Local plugin ZIP import in developer mode**: Gate the Store action with RimWorld's `Verse.Prefs.DevMode` for development and game acceptance. Reuse managed package validation and installation transactions, mark local development provenance, and keep installed packages manageable after developer mode is disabled. Planning only; this now leads the next development batch and blocks skills and other new author tooling. See [Local development package installation plan](plugin-store/Local-Development-Package-Install-Plan.md).
