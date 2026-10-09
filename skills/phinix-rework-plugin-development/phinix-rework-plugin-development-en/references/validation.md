# Skill validation record

Read when maintaining the skill or assessing guidance scenarios. 2026-10-09. Records describe static checks and manual scenario walkthroughs of the draft, not real-user trials, independent-agent tests, RimWorld execution, or cloud publication.

## Initial static checks

skill-creator `scripts/quick_validate.py` passed frontmatter/name/description and unfinished-scaffold checks. Additional checks covered relative links, full-SHA source paths in local Git objects, UTF-8/Markdown fences, JSON examples, POSIX shell syntax, and arguments against inspected source. Example paths/identities are replaceable inputs; no maintainer absolute paths, TODO scaffolds, or calls to unbundled skill scripts are included.

Initial results: 20 relative links, three fixed-SHA source paths, one JSON fence, and three POSIX command fences passed. Remote reachability was not established. The initial snapshot's 4310 existing file digests were unchanged; branches/HEADs/existing status entries were preserved. Only the 11 skill files were added, and client git diff --check passed.

The initial layout had SKILL.md and ten references; engineering.md increased it to twelve Markdown files. No scripts, placeholder assets, or installation actions were introduced. Required SDK/game references come from the user's environment; missing inputs have stop points rather than claims that this skill bundles tools.

## Scenario walkthroughs

Each request was followed through SKILL routing and compared with source/delivery records and stop points. “Passed” refers to guidance correctness, not executed compilation, installation, or game testing.

| Simulated request/state | On-demand reading and route | Stop point/result |
| --- | --- | --- |
| Beginner: a local counter tab | environment → composition/UI → local-loop → verification; versions, independent assembly/package/module/settings, renamed Example, ZIP build/preflight, developer sideload, restart/digest | No GitHub prerequisite; stop for missing supporting host snapshot; real-game acceptance remains environmental. Passed |
| Developer: inventory reward/reservation transfer | composition + inventory capabilities + verification; module/package identities, activation/capabilities, scoped cleanup, DepositId idempotency, authoritative ack/unknown custody | Reject missing codec/dependency/writability; no commit without actual ack; game delivery needs testing. Passed |
| Developer: chat/command capability | messaging capabilities + composition + verification; actual TryHandleOutgoing/Common handlers; discovered module implements outgoing command handler | No invented AddClientOutgoingCommandHandler/direct transport; server read only if needed. Passed |
| Complex Def/assets/native lifecycle/Harmony | routes → environment/public APIs; explain route ownership/limits; user chooses | Stop before unauthorized external download/activation; continue Phinix design; complexity alone does not force mods. Passed |
| Python absent | environment → local-loop; independent dotnet build/tool CLI | Missing SDK/references stops compilation; no unauthorized install. Passed |
| Git/network absent | environment + basis; verified fixed snapshots/references/complete caches only | Do not guess missing contracts; initial restore may need network; no invented offline downloader. Passed |
| gh/login absent | environment; local work continues; guide release auth or authorized browser later | No local-build block or token reading. Passed |
| Game/Unity/mscorlib/Harmony references absent | environment; legal references; check Harmony only if used, net472 cache/explicit framework path | Stop before build for required missing inputs; no reference DLLs in ZIP/public assets. Passed |
| Older host lacks Compose/sideload | environment + basis; actual public DLL/tool/UI, supporting version/fixed snapshot, or distinct older-route assessment | Assembly version alone does not prove all capabilities; no mod disguise; stop unsupported new loop. Passed |
| Same-version new digest, duplicate, downgrade | local-loop; old package enabled/trusted, restart new revision; reject duplicate/downgrade | No receipt editing/output overwrite; distinguish current/installed. Passed |
| Formal package with same ID | local-loop; distinct dev ID or normal uninstall/restart removal before switching source | No forged approval/direct replacement. Passed |
| DevMode withdrawn before confirmation/cancel | local-loop + verification; confirmation/execution recheck, release uncommitted input, follow committed decision | Installed packages remain manageable; no hot reload; actual UI test pending. Passed |
| Installed digest new, loaded digest old | local-loop; summary/source/discovery/activation, explain session DLL, restart | Do not claim new code loaded or request full private logs. Passed |
| Store/manager enable/uninstall/cancel sync | local-loop + verification; next-start state and synchronization | Cancel remains disabled, no automatic restoration; uninstall/restart before schema-1 downgrade. Passed |
| Formal DLL versus Workshop delivery | publication; DLL Release→candidate→approval→Index; mod Workshop→metadata Index | Stop at reviewable preparation without permission; no complete-mod ZIP store installation. Passed |
| Old main tool, new local tool | actions + basis; old 403cea... pin versus dirty Common; Common first then client gitlink, separate CI update | No workflow edits/runs or new cloud-verification claim. Passed |
| Main build failure, same-commit retry/published assets | actions; exact-SHA draft, reserved version, identical upload reuse, skip published | Stop on byte mismatch; no clobber; new source needs delivery confirmation. Passed |
| Release exists, Index invisible | publication + actions; initial review, approved policy, batch detection failures/wait, controlled publication/stable | No immediate/every-release promise, skipped review, or lock edit; distinguish new Index run from main rerun. Passed |

## Existing acceptance and remaining work

Foundation documentation records ManagedExtensionRuntimeTests 3319 assertions, Example 45 assertions, packaging CLI 16 checks, two layout checks, and host/Store/tool compilation. These are **prior delivery records**, not rerun in this skill work or claimed as its tests. Retain user-confirmed RedPacket 1.0.2 local game acceptance within its scope.

The new loop still needs actual restart/revision loading, five save/disconnect/exit cleanup cycles, mode withdrawal, narrow-window UI, management synchronization, and interruption recovery per delivery records. Formal-release game acceptance is not claimed. Deliver Common, pin the client's exact gitlink, then update/verify CI separately. Initial Phinix network queries failed, so latest main/Release/Index remain unknown; hourly batch updates are separately attributed to maintainer evidence.

Initial work added only the skill directory, with no external/current skill install, business/Actions edits, commits/pushes, or Release/Index publication. Check original contents/status before finishing; preserve and record parallel changes rather than rolling them back to force baseline equality.

## Additional lightweight-engineering validation

On 2026-10-09, the reference repository was cloned/read with user authorization, not installed/activated. skill-creator passed; checks of twelve Markdown files, 28 relative links, five fixed-SHA source paths, JSON, and shell fences passed. The snapshot's 695 files outside the skill directory were unchanged. Edits were limited to SKILL/routes/local-loop/basis/validation plus new engineering. The reference clone was clean. These were skill checks, not DLL builds, package installation, or game execution.

After reassessment, retain phase routing and existing DI/capability/local-loop references; include lightweight organization in engineering.md. Do not import another game framework or require an external skill for simple DLLs. Additional manual walkthroughs, not real-game execution:

| Request | Route/result |
| --- | --- |
| Beginner wants a counter tab without another skill | Managed default route → engineering single project/few classes → composition/local-loop; shared State/settings authority; no external loading or mandatory extra docs/Contracts/test project |
| Add settings UI to an existing tab | engineering responsibilities → composition; providers share State rather than duplicate counters; no gratuitous IState |
| Split ExampleExtension.cs | engineering/local-loop explain EnableDefaultCompileItems=false; update Compile Include, and pack.py lookup if renaming csproj; independent identity rules still apply |
| Add messaging/inventory inside one DLL | Feature grouping, narrow adapters, public contracts, no host tailoring; recovery included in the slice; simple packaging does not weaken confirmation; separate pure-rule/game-boundary checks |
| UI list becomes slow | Inspect actual Draw/data scale; avoid premature caches for small data; define data/filter/language/world invalidation, no blanket LINQ ban, per-frame I/O, or background game access |
| Feature expands into Def/assets/native hooks | Reevaluate mod route and combine engineering skill; download/read permission does not grant installation/activation; stop before unauthorized action |

Only engineering decisions/organization were adapted and checked against actual Phinix Example layout; no full external manual or scripts were copied. Existing game-acceptance/CI follow-ups remain; a successful reference clone does not establish current Phinix publication state.

## Bilingual distribution and translation validation

On 2026-10-09, the user's requested layout retains outer distribution directory `phinix-rework-plugin-development/`, containing independent `phinix-rework-plugin-development-cn/` and `phinix-rework-plugin-development-en/` skills. No duplicate outer SKILL.md remains. Each variant contains SKILL.md and eleven references with matching filenames; frontmatter name matches its directory.

Original Chinese prose was preserved, with only the skill name changed and this section added. Every file was translated into English, not just the entry point or a summary. Per-file comparisons verified matching link targets/order, commit/file hashes, CLI options, and identical JSON/executable shell examples; only organization-tree comments were translated. Manual review covered routes, DI/ownership, engineering, sideloading, recovery, publication, and historical verification limits without expanding acceptance or permissions.

Both quick_validate runs passed. Checks passed for 24 Markdown files, 56 relative links, ten fixed-SHA source paths, two JSON blocks, and six shell blocks. The outer folder contains only language folders, with no placeholder scripts, installed copies, or new tools. The 695 baseline files outside the skill directory were unchanged. No installation, business/Actions changes, commits/pushes, game tests, or publication occurred; translation checks do not alter game-acceptance/CI follow-ups.

## Release notification and Index concurrency update validation

2026-10-09, approximately 16:45 (Asia/Singapore). Changes are limited to each variant's SKILL.md, actions.md, publication.md, basis.md, and validation.md. Other requirements/engineering/DI/local-loop/compatibility references remain intact. skill-creator/frontmatter, links, and bilingual-data checks validate this draft without cloud dispatch or publication.

Read-only GitHub API/log review checked historical real runs; basis.md records exact SHA/run/catalog evidence. Fixture runId=123 output was distinguished from actual scan runIds. All three notifier secrets remain absent; logs show missing-credential warnings. Status is “code deployed / notification integration pending”; script/real-queue evidence is not end-to-end acceptance with configured plugin secrets.

Additional manual walkthroughs, not execution:

| Scenario | Reading, correct route, and stop point |
| --- | --- |
| Three plugins release almost together | actions → publication: independent notifiers; four Index writers share queue:max/cancel-in-progress:false. Queue is bounded, no zero-loss promise. Fresh events retry stale scans within three dispatches/twenty minutes, then warn; hourly scans provide fallback |
| Third party lacks INDEX_UPDATE_TOKEN | actions credential scope → publication: initial candidate/admission and approved-policy scheduled updates continue; no requesting/distributing maintainer tokens or blocking third-party updates |
| Configured token owner is not an Index maintainer | actions/publication: dispatch ability is not admission permission. Preserve rejection and maintainer checks without weakening them; notification failure does not withdraw Release |
| Release public, notification/API/wait failed | actions: retain exact Release/assets; warnings direct to scan URL/report/controlled publication. No reissue/overwrite; without authorization stop at reviewable diagnosis |
| Scan success, changed=false, or one source rejected | publication layers: inspect actual run/report changed/errors. No-new-version is normal, not proof of listing. Diagnose packageId/reason individually; green does not prove all sources succeed. Verify admission/publication/store separately |
| Main changes while queued | publication: TrustedHeadChanged is protection. Fresh workflow_dispatch ref main gets a new snapshot; no old-SHA rerun, GITHUB_SHA rewrite, force-push, or lock deletion. For admitted/unpublished work, validate current main check_only before authorized recovery |
| Upstream prepared but unpushed/unverified in cloud | basis/actions: report only source preparation or proven layers; do not reuse this batch's success as proof. Check actual SHA/run/secret, keeping modern local DI separate from old CI pin |
| CI-only main change and omitted digest | actions: main push still allocates a release. Register only reviewed changed/new CI digests; never disable source checks or re-sign the entire unreviewed workspace |

Foundation game acceptance, Common/gitlink delivery, and CI-pin updates remain pending. Maintainer secret configuration/end-to-end notification acceptance are separate follow-ups, not third-party author prerequisites. No skill installation, business/manifest/workflow/system edits, commits/pushes, dispatch, admission, or publication occurred; reading existing runs is not a newly executed acceptance test.

Final checks: both quick_validate runs passed; 24 Markdown files, 66 relative links, 24 fixed-SHA source paths, four JSON blocks, and six shell fences passed. Per-file link targets, full commit/file hashes, CLI flags, and executable examples match across languages. No scripts were added. Only ten existing skill files changed; the other 1079 baseline files are unchanged, as are repository HEADs/branches/status, including Common and all four delivery clones. Existing untracked files were preserved.
