# Store finishing and documentation handoff

[中文](商店收尾与多仓文档交接计划.md) · 2026-10-06 · dev

**Handoff update:** The [detailed Chinese task brief](多仓库文档更新任务书.md) specifies repository outlines, writing style, CLI steps and review requirements. Local C1/C2/C3 fixes are complete; the independent index has not synchronized this batch. See [acceptance](StoreClientCouplingAcceptance.md). A new read-only query found a public RedPacket v1.0.0 release; earlier deferred-publication notes below are historical. Recheck assets and catalog admission before rewriting public claims. Interpret the original audit's unimplemented recommendations against the newer local/remote acceptance status.

## Scope and order

Audit coupling in Store, managed runtime, index automation and Gateway against Design-Philosophy first. Follow with evidence-based local changes, ordinary-user UI and package acceptance. The user defers RedPacket publication and both legacy business bug tracks. Main repository splitting, broad library replacement and AI author tools/skills remain deferred. Preserve accepted installation, removal, upgrade and access-switching evidence. RedPacket's absence does not block other store deliveries but the full two-plugin release track remains incomplete.

Talent Issue #22 was approved by the user, published and automatically closed. Host-profile PR #24 merged at `048c8637a64efefe64ff4d95264dd9e7300dc302`; post-merge [self-check 37468314971](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37468314971) passed. This does not require repacking plugins or a dedicated game-host update. Earlier unapproved/unmerged notes are historical.

1. Audit actual compilation references and call sites: dependency direction, ordinary discovery/lifecycle parity, policy/data ownership, GitHub/CF adapters, main-thread capture/dispatch, persistence and validator snapshots. Report file/line evidence, impact, priority and minimal remedies. Separate real coupling from maintainer configuration and legitimate deployment bindings; do not fix legacy business code as part of this audit.
2. Address demonstrated coupling locally without weakening dependencies, ownership, digests or recovery. Avoid broad refactors for superficial uniformity.
3. Improve Store information hierarchy, version selection, installation/restart status, progress and retry. Hide development fields but retain actionable diagnostics. No remote images. Human checks cover Chinese/English, long text, small windows, scaling, cancellation and failures.
4. Verify host-profile maintenance, immutable assets, approval errors and clean output including retired duplicates and language resources. Provide incremental game checks; compilation/automation does not establish game or business recovery acceptance.

## Documentation work for the user's other models

This agent records scope and verified audit facts, not the public prose rewrites. Each model first presents its repository outline for user review, then makes approved changes and delivers a separate documentation PR per repository.

**Use current code, project files, workflows, fixed release assets and actual online state as authority. Phinix-Rework README has not been maintained and must not be treated as a current baseline or merely polished.** Current entry files are `.github/README.md` and `.github/README.zh-CN.md`; do not assume a root README. Cross-check architecture/plans too; report conflicts rather than changing implementation to match stale prose. Distinguish implemented, automatically checked, human-tested and planned behavior. Refer first to the [code audit](StoreCouplingAudit.md); its recommendations are not implemented yet.

| Repository | Content |
| --- | --- |
| Phinix-Rework | Player installation/connection/store use, bundled versus independent plugins, developer entry points; verify platform/build/release claims against actual projects/workflows |
| Phinix-Plugin-Index | Current submission format, Issue approval/rejection, automatic-update scope, failures and maintainer host profiles |
| Phinix-Plugin-Gateway | Gateway purpose, GitHub/CF protocol parity, real deployment configuration, secret injection, checks and rollback |
| Phinix-Example-Plugin | Reproducible Tab/settings/language/lifecycle example through build, packaging and submission |
| Phinix-Legacy-TalentTrade | Dependencies, usage, actual protocol/save limitations and disable/uninstall notes; listing does not establish complete business safety |
| Phinix-Legacy-RedPacket | Purpose, dependencies, legacy service relationship and deferred publication; no unimplemented release/recovery promises |
| Phinix-PluginStore-PoC | Developer testing only and current methods, not a normal-player source or official Playtest listing |

Lead README with purpose, the shortest working path and help. Replace slogans and repeated phase/status claims with concrete behavior. Move dated narratives, assertion counts, commit dumps and retired proposals into retained maintenance/history evidence. Removing AI-style prose must not erase limitations, recovery instructions, attribution or format boundaries.

Keep Chinese/English aligned; check commands/links, use path placeholders and exclude credentials. AGENTS.md stays a concise engineering guide linking stable references. AI author tools/skills remain future work. Documentation tasks do not change code, workflows, permissions, immutable assets/tags, approval history or audits.

Acceptance: unfamiliar players/authors can complete the shortest path, claims match code/online evidence, and no contradictions, obsolete entry points, false success or sensitive information remain. Deliver a fact-check table and documentation diff per repository.
