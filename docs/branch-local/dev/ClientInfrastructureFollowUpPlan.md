# Client Infrastructure and Follow-up Implementation Plan

Updated: 2026-10-08. Current branch: `dev`. [中文](后续实施计划.md).

This document is the baseline schedule and technical record for Phinix Rework infrastructure refactoring, physical repository separation, and follow-up deliverables.

---

## 1. Stage Acceptance Status Overview

| Stage | Scope & Key Objectives | Acceptance Status & Outcome |
| :---: | :--- | :--- |
| **F0** | Baseline and Dependency Inventory | ✅ **Accepted**. Fixed input baseline, documented target frameworks and artifact responsibilities. |
| **F1** | Lifecycle and Contract Decoupling | ✅ **Accepted**. Delivered `ClientExtensionRuntime`, separated side-effect-free construction from explicit main-thread `Start`, enforced reverse teardown order and exception safety. |
| **F2** | DI Environment and Chat Pilot | ✅ **Accepted**. Pinned Autofac 8.4.0 in client composition layer, migrated Chat to constructor injection and protected scope disposal, game acceptance passed. |
| **F3** | Stateless Operation Flow Pilot | ✅ **Accepted**. Pinned Stateless 5.20.1, migrated Store asynchronous operation transitions while preserving original transaction and recovery boundaries. |
| **F4** | DI Adoption and Old Entry Deprecation | ✅ **Accepted**. Built-in modules (Chat, Inventory, Trade, Store, LegacyAdapter) and samples migrated to `ClientExtensionModule.Compose`. Legacy client entry marked `[Obsolete]`, scheduled for removal in host 1.0. |
| **F5** | Shared Source Consumption Rehearsal | ✅ **Accepted**. Verified shared layer extraction in clean checkouts; both endpoints pinned the same gitlink, multi-targeting builds and nested protobuf tests passed. |
| **F6** | Physical Three-Repository Split & Delivery | ✅ **Accepted**. Completed independent publication and acquisition verification; Server Docker image published to `hunyuan2333/phinix-rework:dev`; Index and Example branches merged to main. |
| **F6-M** | Management and Store State Synchronization | ✅ **Accepted**. Introduced `IClientExtensionControlService`, separated package intent from module settings commands, revision-driven bidirectional refresh, passed 1000 runtime assertions and game acceptance. |
| **F6-S** | Mandatory Plugin Signing Admission | ❌ **Canceled**. Explicitly canceled by the user on 2026-10-08; mandatory signature gating is not implemented, retaining existing hash and integrity checks. |
| **F7** | Author Experience & Automated Builds | 🔄 **In Progress**. Documentation consolidation is underway (this batch); plugin Actions automation and thin Skill remain scheduled. |

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
- **Pre-Split Backup Branch**: Preserved locally and remotely on `codex/pre-split-20261008` (commit `0b17036a7ca6edddb8991305deec0718bcca4347`).
- **Unsplit Full Directory**: `/home/hunyuan2333/Phinix/Phinix-Rework-unsplit-backup-20261008`.
- **Server Docker Image**: The canonical publication destination is Docker Hub `hunyuan2333/phinix-rework:dev` (verified via Server commit `42516ec` and Actions Run 37778078886).

---

## 3. Current Stage: F7 Author Experience & Automated Plugin Builds

Stage F7 is currently active. This round focuses on **documentation consolidation** (streamlining specifications, eliminating dead links, providing a stable Plugin Development Guide). Code implementations, GitHub Actions workflows, and AI Skills are paused until this round is accepted.

### 3.1 Automated Build Requirements for Standalone Plugins
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
   - Evaluate a reproducible mechanism for GitHub runners to acquire RimWorld, Unity, mscorlib, and Harmony (for TalentTrade) references.
   - Reference DLLs and private credentials must never enter Git, plugin ZIPs, public Actions artifacts, or logs.
5. **Retain Testable Artifacts**:
   - Successful main builds upload compliant plugin ZIPs and safe build summaries as Actions artifacts.
   - Automated creation of GitHub Releases, tags, or Index submissions is not enabled implicitly. Formal publication follows new versions and the standard Index review workflow.
6. **Individual Behavior Acceptance**:
   - Verify for each repository: main push triggers and completes cloud build and packaging; dev push does not trigger compilation; ZIP contains no game/host DLLs and passes static checks.

---

## 4. Pending Deliverables & Known Issues

The following items remain open work and are not considered closed by this documentation round:

1. **Plugin Actions Implementation**: Authoring, runner reference acquisition, and testing of GitHub Actions workflows in the three standalone plugin repositories.
2. **Red Packet Defect Repairs** (see [RedPacket Repair Plan](plugin-store/RedPacket-Stack-Selection-Optimization-Plan.md)):
   - Sending 200 steel across multiple physical stacks triggers an incompatible-stacks exception (unifying grouping and send validation).
   - Claim completion summaries encounter a `NullReferenceException` when triggered without active game world context (decoupling notification scheduling from domain commits).
   - Once repaired, increment plugin version, publish an immutable Release, and submit a regular Index update.
3. **Talent Trade Known Constraints**:
   - Relies on Harmony patches for pawn lifecycle interception.
   - Active marketplace listings and rental records are tracked in a save-local `GameComponent`. Uninstalling while transactions are in-flight can corrupt save data.
4. **Standalone Plugin DI Migration**: Example, Red Packet, and Talent Trade currently run via legacy `IPhinixExtensionModule.Register`. Migration to `ClientExtensionModule.Compose` is deferred to subsequent iterations.
5. **Client Legacy Entry Removal Gate**: Planned for host 1.0 / abstractions 2.0. Prerequisites: all maintained plugins migrated, rollback pathways verified, and game acceptance completed.
