# Finishing order and AI plugin-author workflow plan

2026-10-06 user priorities follow [store completion](StoreCompletionOrder.md): remaining acceptance, Gateway extraction, resource/save prerequisites, RedPacket, Talent and production finishing first. AI tools and reader prose remain deferred projects outside the current line; do not restart completed early store items below.

Updated 2026-10-06. The user confirmed the current game path works. Repository prose cleanup and AI author tooling are deferred projects: record requirements now, complete existing store work first.

## Existing delivery order

1. Real package and cumulative download bytes plus reading/planning/downloading/validating/committing/completed/restart stages. Throttle updates, reject stale callbacks and never show cancellation/failure as success.
2. Nonblocking startup update notices from the official metadata and selected adapter, with version/changelog and no automatic download. Version replacement requires owned transactions, dependency checks and interruption recovery before enabling an update button.
3. A3 approved-source release monitoring using trusted checks and publication, immutable accepted versions and explicit policies; changed source/module/dependency scope pauses for review. Player updates remain voluntary.
4. Build and incremental game acceptance, followed by the existing production domain/environment and old Worker/storage inventory plan. Red-packet/talent separation retains its resource/save prerequisites.

Do not redo accepted basic installation or label publication. Record exact tests and human checkpoints per batch; pending work is not complete.

## Repository documentation cleanup for another model

The user will assign this later. Scope: Phinix-Rework, Phinix-Plugin-Index, Phinix-Example-Plugin and the independent developer-only Phinix-PluginStore-PoC.

Write audience-specific README entry points for players, authors and maintainers. Lead with actual capabilities and the shortest working route. Replace repetitive AI-style prose, phase acronyms, daily status dumps and promises with one current guide; move history/evidence to maintenance archives. Keep real limits and recovery instructions. Official tutorials use Example Plugin; Playtest remains developer-only. Keep English/Chinese aligned, verify links/current JSON/commands, and never describe pending features as delivered.

Documentation-only, reviewable PRs per repository: do not modify code/workflows/permissions, approval evidence, immutable locks/assets or audit history. Never delete Releases/source tags merely to tidy prose. Acceptance: clear entry points, a working current tutorial, accurate status and no private paths or credentials.

## AI author experience

A user with GitHub connectivity through a proxy describes a simple plugin in a DeepSeek-style harness, Codex or another agent. The agent checks capabilities, scaffolds, implements, builds, tests, packages, publishes the authorized author repository and immutable Release, submits the index application and reports its actual status. Git and gh are optional; harness selection is not an architectural dependency.

Deliver a short repository AGENTS.md covering contract paths, main-thread/lifecycle rules, profile versus save data, language resources, legal compiler inputs, validation, publication scope and recovery; link detailed stable guides. Add thin optional skills for development/testing/publication backed by the same versioned, tested tools. Tools take structured parameters and support preflight, retry and explicit results; skills should not contain ad hoc shell orchestration. Provide a human first-login/proxy/reference setup guide.

Detect OS, runtime, .NET SDK, user-provided RimWorld 1.6 references, connectivity, optional Git/gh and existing authentication. Without Git, fetch fixed source and use GitHub APIs to create source commits/tags; without gh, use the API backend for repositories, Releases/assets and Issues. Both paths must retain the exact source commit and asset evidence. Missing SDK or legal game references stops at reviewable source/setup instructions; never claim a successful build/game check or fetch game DLLs from third parties. Tool installation follows user authorization, uses reversible locations and avoids blind sudo/global changes.

Use one explicit network configuration for API/Git/gh/build dependencies: direct, system/environment or explicit HTTP(S)/SOCKS proxy. Diagnose API/source/asset/dependency routes separately, distinguish DNS/connect/TLS/403/429 and compare routes only when configured. Do not assume TUN or classify every 403 as proxy failure. Reuse authentication; otherwise guide official interactive login/device authorization or minimum-scope credentials. Login is a necessary human bootstrap step. Never put proxy secrets/tokens in chat, argv, source, artifacts or logs; use secure storage/hidden input and redact diagnostics.

Authorized author publication may run end to end: fixed source, build from that source, validation, fixed tag, Release upload, independent asset verification, exact candidate and index Issue. Retry verifies existing state instead of overwriting accepted bytes or duplicating assets/Issues. Public disclosure of private source requires an explicit reviewable publication scope and authorization.

First official admission still requires a maintainer approval label. An author agent cannot approve itself, impersonate maintainers or weaken checks. Submitted is not published. Later versions run unattended only within an explicitly approved update policy; otherwise report awaiting review. Bound polling and report source/version/hashes/Issue/failure stage without secrets.

Acceptance covers Git/gh present and absent, SDK/reference availability, direct/HTTP/SOCKS/broken proxy, existing/missing/insufficient auth, English/Chinese wishes, build failure, interrupted publication, existing/conflicting assets, invalid candidates, waiting/approved admissions and idempotent retry. Demonstrate a real settings/tab plugin; human game testing remains distinct from agent validation.

## Scope now

Plan only: no personal Codex skill, reader-facing repository rewrite or new credentials in this batch. Build a resumable no-Git/no-gh API/CLI prototype before writing thin AGENTS/skill adapters; do not document tools that do not exist as available.

2026-10-06 execution status: steps 1–3 are implemented and automatically verified; Example 1.0.1 was automatically published. Step 4 needs human game acceptance before domain migration. [Batch evidence](StoreFinishingAcceptance.md). Public prose and AI author tooling remain plans only.

2026-10-06 latest status: the user accepted the 1.0.1 → 1.0.2 game upgrade. Production CF domain/environment migration and owned PoC inventory are next; earlier pending-game statements are superseded. Cross-platform/broad-network coverage remains separate.

2026-10-06 deployment: [production origin migration](ProductionCfMigration.md) is delivered and built, with a transitional alias. Old PoC public access is disabled; irreversible retirement was rejected by automatic approval and needs concrete authorization. AI author tools/prose cleanup remain deferred.
