# Client Infrastructure and Follow-up Implementation Plan

Updated: 2026-11-17 (aggregated through `6c30256`). [中文](后续实施计划.md) | [Completed Milestones Summary](Completed-Milestones-Summary.md).

This document is the baseline schedule and technical record for Phinix Rework infrastructure refactoring, physical repository separation, and follow-up deliverables.

---

## 1. Stage Acceptance Status Overview

| Stage | Scope & Key Objectives | Acceptance Status & Outcome |
| :---: | :--- | :--- |
| **F0** | Baseline and Dependency Inventory | ✅ **Accepted**. Fixed input baseline, documented target frameworks and artifact responsibilities. |
| **F1** | Lifecycle and Contract Decoupling | ✅ **Accepted**. Delivered `ClientExtensionRuntime`, separated side-effect-free construction from explicit main-thread `Start`, enforced reverse teardown order and exception safety. |
| **F2** | DI Environment and Chat Pilot | ✅ **Accepted**. Pinned Autofac 8.4.0 in client composition layer, migrated Chat to constructor injection and protected scope disposal, game acceptance passed. |
| **F3** | Stateless Operation Flow Pilot | ✅ **Accepted**. Pinned Stateless 5.20.1, migrated Store asynchronous operation transitions while preserving original transaction and recovery boundaries. |
| **F4** | DI Adoption and Old Entry Deprecation | ✅ **Accepted**. Built-in modules (Chat, Inventory, Trade, Store, LegacyAdapter) and bundled test samples migrated to `ClientExtensionModule.Compose`. Legacy client entries were subsequently removed in F8. |
| **F5** | Shared Source Consumption Rehearsal | ✅ **Accepted**. Verified shared layer extraction in clean checkouts; both endpoints pinned the same gitlink, multi-targeting builds and nested protobuf tests passed. |
| **F6** | Physical Three-Repository Split & Delivery | ✅ **Accepted**. Completed independent publication and acquisition verification; Server Docker image published to `hunyuan2333/phinix-rework:dev`; Index and Example branches merged to main. |
| **F6-M** | Management and Store State Synchronization | ✅ **Accepted**. Introduced `IClientExtensionControlService`, separated package intent from module settings commands, revision-driven bidirectional refresh, passed 1000 runtime assertions and game acceptance. |
| **F6-S** | Mandatory Plugin Signing Admission | ❌ **Canceled**. Explicitly canceled by the user on 2026-10-08; mandatory signature gating is not implemented, retaining existing hash and integrity checks. |
| **F7** | Author Experience & Automated Builds | ✅ **Completed** (`fa17ae4` / `c63688c` / `5366dc7`). Delivered developer-mode local sideloading, copyable diagnostics, configurable packaging/offline preflight, and bilingual plugin guides/skills; plugin and Index Actions are live. |
| **F8** | Theme System, Shared UI & API 3 | ✅ **Completed** (`c51f8a6` / `783fcb0` / `2c228fe` / `81059c4` / `53c92ce` / `6fe404e`). Delivered local & online theme stores, shared presentation contracts, RimWorld 1.6 UX rework, API 3 migration, and 1.6/1.7 dual-runtime support. |
| **P3–P6** | Paired Server Transactions, Receipts & Legacy | ✅ **Completed** (`99bcef8` / `57e6d1c` / `b00862a` / `750a91a` / `075c929`). Delivered Trade/Inventory journaling, recovery choices, P4/P5 credentials & diagnostics, and Legacy integration fixes. |
| **Relay** | Command Capability Relay (R0–R5) | ✅ **Completed** (`9c0c5ae` / `5b2bbb3` / `311fdd6`). Delivered client capability Relay APIs, receive guards, R5 delivery, and verified runtime/plugin tests. |
| **SQLite / E0–E4** | SQLite Infrastructure & Error Reporting | ✅ **Completed** (`e00872e` / `6c30256`). Delivered reusable client SQLite storage and E0–E4 client error reporting pipeline (E5 verification & SQLite inventory migration tracked in their active docs). |

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
- On 2026-10-10, the user requested F8 theme/API planning, then clarified that no third-party plugins require indefinite old API support. New business and UI use one current contract without legacy information; adapters cover all old interfaces, not just the protocol plugin. Migrate maintained consumers and assess deletion; preserve historical data and supported external behavior. See the [stage plan](Theme-and-Plugin-Api-Stage-Plan.md) and [boundary audit](Compatibility-Interface-Boundary-Audit.md). Steam clients update together; this is documentation only.
- The first stable client entry deprecation release was planned as host 0.9.8. A target version or local change is not a published Release; recheck actual versions when publishing.

### 3.3 Optional Release Event Discovery (Deferred Evaluation)

Keep the current system running. A future GitHub App may add immediate stable Release discovery while preserving existing main release Actions, post-release notification Actions, manual entry points, admission/controlled publication and scheduled scanning. Authors need not replace their publishing tools or install the App to qualify. Multiple channels must deduplicate work and retain approval/identity boundaries. See [the event discovery enhancement plan](plugin-store/Release-Event-Discovery-Enhancement-Plan.md). This entry records the plan only.

## 4. Remaining Follow-ups & Known Constraints

Items below reflect remaining constraints or active tracks as of 2026-11-17 (completed F7, F8, UI/UX, P3–P6, and Relay items are summarized in [Completed Milestones Summary](Completed-Milestones-Summary.md)):

1. **Active Recent Tracks** (tracked in their own recent docs, unmodified here):
   - **Client Error Reporting E5**: Candidate packaging and end-to-end verification (`Client-Error-Reporting-Roadmap-2026-11-15.md`).
   - **Inventory SQLite Migration**: Migrating legacy XML/Journal inventory storage to shared SQLite (`Inventory-SQLite-Migration-Roadmap-2026-11-10.md`).
2. **Red Packet Independent Defect & Release Tracking** (see [RedPacket Repair Plan](plugin-store/RedPacket-Stack-Selection-Optimization-Plan.md)):
   - In-game acceptance and store publication status are tracked in the standalone RedPacket repository.
3. **Talent Trade Known Constraints**:
   - Relies on Harmony patches for pawn lifecycle interception.
   - Active marketplace listings and rental records are tracked in a save-local `GameComponent`. Uninstalling while transactions are in-flight can corrupt save data.
4. **Remaining Legacy Interface / Adapter Cleanup**:
   - Maintained plugins are migrated to API 3 and dual 1.6/1.7 runtimes; remaining boundary cleanups follow the [boundary audit](Compatibility-Interface-Boundary-Audit.md).

> Note: Developer-mode local plugin ZIP sideloading (former Section 5) was delivered in F7 (`fa17ae4`).
