# Evidence, versions, and capability status

Read to choose target versions, acquire official material, or review the skill. Review date: 2026-10-09 (Asia/Singapore). These are workspace and saved-main evidence, not claims about every installed host or latest release.

Use the final release-notification/queue verification section for current delivery evidence; earlier network failures/old snapshots are historical. This update does not expand game acceptance of new local foundations.

## Repository baseline: initial historical review

| Repository | Reviewed branch / HEAD | Workspace and evidence |
| --- | --- | --- |
| `Phinix-Rework` | `dev` / `7c2b4978c57e33a4f2cbd3f45743e7aa1c531fac` | Uncommitted/untracked changes; actual contents inspected |
| `Phinix-Rework/Dependencies/Phinix.Common` | `detached` / `67f243d9edced77dc3fe669f8a435b1f14b59199` | Uncommitted/untracked changes; actual contents inspected |
| `Phinix-Rework-Common` | `dev` / `85ecb18278cf98c0e4fa9ab6c49f4d7db635b413` | Clean at the initial review |
| `Phinix-Example-Plugin` | `codex/f6-repository-split` / `3d4b46d411c823418a62b71eb799824ea2aa16ca` | Uncommitted/untracked changes; actual contents inspected |
| `Phinix-Legacy-RedPacket` | `dev` / `fc1f3c6b20b659f88852949a668883cc1112e893` | Clean at the initial review |
| `Phinix-Legacy-TalentTrade` | `codex/docs-user-guide` / `70cf37e9445d304b31a315d0e654171dea4150bf` | Clean at the initial review |
| `Phinix-Plugin-Index` | `codex/f6-repository-split` / `227df1118c1ab04cd8de4329ae6d7ecdccfb8476` | Uncommitted/untracked changes; actual contents inspected |

Client Common gitlink is `67f243d9edced77dc3fe669f8a435b1f14b59199`; protobuf gitlink/actual HEAD is `4b0c3aacf0657fbf38253b38918d3358dd4319ec`, initially clean. Sibling Common HEAD `85ecb18278cf98c0e4fa9ab6c49f4d7db635b413` cannot replace uncommitted managed-ZIP/receipt implementation in the client submodule. Workspace host assembly is 0.9.7, abstractions 1.9; identical assembly versions do not prove identical release bytes.

## Acquire material for the target version

Canonical public repositories: [client](https://github.com/HunYuan2333/Phinix-Rework), [Common](https://github.com/HunYuan2333/Phinix-Rework-Common), [Example](https://github.com/HunYuan2333/Phinix-Example-Plugin), [Index](https://github.com/HunYuan2333/Phinix-Plugin-Index). Use [Server](https://github.com/HunYuan2333/Phinix-Rework-Server) only when needed.

Committed material can be retrieved by full SHA, such as [public DI contracts](https://github.com/HunYuan2333/Phinix-Rework/blob/7c2b4978c57e33a4f2cbd3f45743e7aa1c531fac/Client/ClientExtensionAbstractions/Framework/IClientComposition.cs), [Inventory contracts](https://github.com/HunYuan2333/Phinix-Rework/blob/7c2b4978c57e33a4f2cbd3f45743e7aa1c531fac/Extensions/Inventory/Contracts/InventoryContracts.cs), and [Example main CI configuration snapshot](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/e3cec0d1dbf7e8f4be7f6dc6cfc42daa3421c2ba/ci/config.json). Paths were verified in local objects; network retrieval was not confirmed in the initial review.

This skill includes new-loop operations/interface semantics. Uncommitted Quickstart, Delivery Plan, ZIP tools, and modern Example contents **cannot be acquired through those old SHA links**. To execute them, obtain a trusted complete maintainer-delivered source/tool snapshot, verifying host/Common/protobuf and file digests; or acquire a newer full SHA after formal commits. Without a snapshot or corresponding released tool, stop the new loop. Do not invent download URLs or assemble dependencies from floating main. Distribute the whole skill directory; no maintainer-machine path is required.

Client, its Common submodule, and sibling Common AGENTS.md were read. No AGENTS.md was found inside Example, either Legacy plugin, or Index; their configuration was not changed. Server business source was unnecessary and not read.

## Reviewed material and discrepancies

| Decision | Inspected repository-relative material and conclusion |
| --- | --- |
| Author guide | Client `docs/Plugin-Development.md`, `docs/插件开发指南.md`, `docs/Design-Philosophy.md`, `docs/Compatibility-Boundaries.md`. Public source verifies APIs; older Register descriptions do not override the Compose recommendation |
| Foundation delivery | `docs/branch-local/dev/Developer-Foundation-Delivery-Plan.md`, `docs/Local-Plugin-Quickstart.md`. D0–D4 implementation/automation records exist; game UI, restart/revision loading, five save/disconnect/exit cycles, mode withdrawal, and narrow windows remain pending |
| Installation/diagnostics | Client ManagedLocalImport.cs, tool Program/PackageConfiguration/OfflinePreflight; Common submodule ManagedExtensionInstallation.cs, management/recovery, ManagedExtensionZip.cs; diagnostics/synchronization docs. Schema 2 local provenance does not fabricate approval; schema 1 formal receipts remain readable |
| DI Example | example/ExampleExtension.cs, Example.csproj, package-config.json, pack.py, Tests/Program.cs: modern Compose, shared state, Borrow, localizer disposal, generation. Source base version 1.0.2 does not imply replacing old 1.0.2 Release assets |
| Capabilities/recovery | IClientComposition, IClientExtensionAbstractions, public UI, Common FrameworkTypes, InventoryContracts, docs/Inventory.md, Trade outgoing registration/host pipeline |
| RedPacket business | fc1f3c6... publication.json declares 1.0.2; README.zh-CN/CHANGELOG records user-confirmed local game testing and prior 17 algorithm regressions. Preserve confirmation, without asking again or extending it to new sideload UI/all third-party items |
| TalentTrade | 70cf37e... publication 1.0.1, README.zh-CN; legacy entry, independent Pawn/in-memory purchase recovery limits. AssemblyVersion 1.0.0.0 differs from package version |
| Index | 227df111... workspace README/ControlledPublication/SourceUpdates, templates/workflows; not presumed latest main. ControlledPublication has parallel uncommitted edits and its “A3 not enabled” conflicts with source updates; qualify conclusions using implementation and maintainer evidence |

The guide still lists RedPacket 1.0.0, unlike publication/CHANGELOG 1.0.2. This skill uses the latter and preserves assembly identity. Delivery Plan/Quickstart still block skill work before game acceptance; explicit user authorization permits a draft and static/scenario checks only. Documents and real-game acceptance gates were not changed.

## Main and remote status: initial history, superseded below

Read-only `git ls-remote` queries for four main branches failed through the proxy; latest remotes remain unknown. Saved origin/main refs are historical source evidence only:

| Repository | Saved main SHA |
| --- | --- |
| Example | `e3cec0d1dbf7e8f4be7f6dc6cfc42daa3421c2ba` |
| RedPacket | `fc1f3c6b20b659f88852949a668883cc1112e893` |
| TalentTrade | `c14f3f6d490106affe0d1878ff285b3b2d867f48` |
| Index | `a77c68d5ad1688f36fc599f3994c1064269b06c3` |

All three main snapshots have automatic formal Releases on main push and pin host `403cea6c207a630fae391c6dc29bbce5a5a17b3b`, whose Common gitlink is also `67f243d...`. They do not contain new uncommitted client-Common implementation. This cannot establish main-cloud verification of the new Example/tool loop. Deliver Common first, pin the client's exact gitlink, complete game acceptance, then separately update CI pins. Current Release assets/store versions were not queried; no claim of shipped fixes is made.

The maintainer reports hourly remote Index scheduling and batch checks by approved policy. Local workflow still uses `17 */6 * * *` and old SourceUpdates admits one per run. Retain both sources of evidence; future execution must read-only verify the target workflow/options. Do not hard-code three sources or guarantee every intermediate release enters the Index.

## New workspace capability fingerprints

SHA-256 values identify inspected uncommitted contents, not official release packages. Parallel work may change them; recheck when needed.

| Repository-relative file | SHA-256 |
| --- | --- |
| `Phinix-Rework/docs/Local-Plugin-Quickstart.md` | `578c51c35f7647d31b1fd02b843e15898e60bb5f07f18efaebd4b246d99a04bc` |
| `Phinix-Rework/docs/branch-local/dev/Developer-Foundation-Delivery-Plan.md` | `abf4db67e84a773ec15d2aae51e06f76fb5804dba755fa133c70ed3553b491dc` |
| `Phinix-Rework/Client/ClientExtensionAbstractions/Framework/IClientComposition.cs` | `ec2548e0776ebf2e1bf47da7050adbffc6d23ab3918fd01b719e21a34b094162` |
| `Phinix-Rework/Extensions/PluginStore/Client/ManagedLocalImport.cs` | `4997960ca7dde506ed70df9c5def6215cf0393d9c3b38be2f5fd1688ef6ae664` |
| `Phinix-Rework/Extensions/PluginStore/Tools/ManagedPackageTool/PackageConfiguration.cs` | `607e2be3fd5d162145420f49dc0dfa3fe7bc3296dd4078e015920859cebd5743` |
| `Phinix-Rework/Extensions/PluginStore/Tools/ManagedPackageTool/OfflinePreflight.cs` | `1ee6b87861ae9ed058f24e015ef75ec5c107f36956221abe6ffa106b58f21542` |
| `Phinix-Rework/Dependencies/Phinix.Common/Common/Utils/Framework/ManagedExtensions/ManagedExtensionZip.cs` | `f17542c0b5dfb89b2eca1368f1568950e02d916abad24fbf76f9cdd8dbf064a9` |
| `Phinix-Example-Plugin/example/ExampleExtension.cs` | `d16c6319b80f781ef87d3a2796ab5c8e8f9906aa3c877a90c47f6d5e8d9355ac` |
| `Phinix-Example-Plugin/pack.py` | `22399e30f1b8de025595c554e32110975916ac4050012e82ff4cbee9f179714d` |

## Additional lightweight-DLL engineering evidence

On 2026-10-09, the user-authorized clone/read of `HunYuan2333/rimworld-mod-engineering-skills` succeeded at HEAD `7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e`. Its checkout was clean and no AGENTS.md was found. It was downloaded/read, not installed/activated. Success does not change the earlier failed Phinix queries or prove their latest remote/Release/Index state.

Reviewed Chinese SKILL, engineering-philosophy, fundamentals, engineering-review, performance-ui-threading, testing-release, and observability-developer-tools. Rephrased single authority, feature cohesion, explicit ownership, vertical slices, risk-matched checks, and exact artifacts for Phinix. Current ExampleExtension/Example.csproj confirms shared State, single project, explicit Compile Include, and pack lookup.

Architecture choice: keep phase/capability references and add [engineering.md](engineering.md) for simple managed DLL organization. Necessary details remain in existing topical references; no complete mod manual, scripts, or large-mod requirements were copied. Simple DLL guidance is self-contained; mods still combine the external skill. Download/read authorization does not authorize installation/activation.

## Release notification and queue delivery verification

Checked 2026-10-09, approximately 16:45 (Asia/Singapore). Followed client `docs/branch-local/dev/Plugin-Skill-Authoring-Prompt.md` and the notification-update request; edits are skill-only. Inspected actual latest delivery checkouts/files/diffs/metadata. Preserved older maintainer checkout dirty/untracked files and Common modifications without branch switches or overwrites. Four delivery clones were clean; plugin checkouts are now synchronized dev branches, which does not mean their main deployment is missing.

| Repository | Notification/queue deployment main | Synchronized dev |
| --- | --- | --- |
| Index | `5b1dbf2eb653a539f573bd3cc5f5305b82362c39` | `—` |
| Example | `fd246747ea5a44f066d375f077d671834ae093bf` | `78336cfb1ecf9c605b5c5bb192bd86b61548c194` |
| RedPacket | `cadd91210215f02830c668109430311a45a9386e` | `8bcef1581b51b76fd74f5d81ee846c3fe926a52d` |
| TalentTrade | `36939655e4fc0d9672ceb10db1764a22788ff23c` | `364e3b23923390a57c9a599c67c228cd4601cc5f` |

Read-only GitHub API confirmed all three main/dev pairs above. Index main advanced through admission/publication to `198227bb45f034077465d5f21120324afd08bf02`. Current four writer workflows, source_updates.py, and ControlledPublication match Git blobs inspected at `5b1dbf2...`. These are dated evidence, not permanent pins. Each plugin's main/dev release workflow, notifier, and integration notes match byte-for-byte.

Secret-name lists in all three repositories lack `INDEX_UPDATE_TOKEN`; successful release logs each contain its missing-credential warning. Only names/presence were checked, never values. **Code deployed / notification integration pending**: script/real queue evidence exists, but end-to-end integration with configured plugin secrets remains unaccepted. A green notify job is not proof it is online.

Read-only verification confirmed successful main-push builds and formal non-draft/non-prerelease `v1.0.4` Releases:

- [Example 37905382656](https://github.com/HunYuan2333/Phinix-Example-Plugin/actions/runs/37905382656).
- [RedPacket 37905680323](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket/actions/runs/37905680323); earlier 37905376685 failed after an omitted workflow digest.
- [TalentTrade 37905694769](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/actions/runs/37905694769); earlier 37905379141 had the same failure class.

Actual queue evidence: historical real runs inspected read-only, not triggered here:

- [Scan 37905776190](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905776190): success, actual matching runId changed=true/errors=0; three 1.0.4 versions admitted together.
- [Scan 37905780510](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905780510): rejected after admission advanced main and invalidated its snapshot.
- [Fresh scan 37905950068](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905950068): rejected after catalog publication advanced main again.
- [Third fresh scan 37906197744](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37906197744): success, actual changed=false/errors=0; do not report another three admissions.
- [Controlled publication 37905904430](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905904430): success. Remote stable snapshot `723778ea1de6d911bda2e8018b8df9cf1f336d08`, catalog size 28172, and SHA-256 `5881736d4dd947c15eec5ae62e567a4684da8524fa8687240bc04294f6b32717` match saved catalog bytes containing all three 1.0.4 versions. No game-store UI acceptance was performed here.

Current main ci/config.json/workflows still pin host `403cea6c207a630fae391c6dc29bbce5a5a17b3b` and build tools from it. Notification-release success does not validate modern workspace DI/configuration/sideloading against that old cloud pin. Common/gitlink delivery, CI-pin updates, and game acceptance remain pending. RedPacket/TalentTrade manifests register workflow/notifier/tests/notes, and registered digests were checked for consistency; no unreviewed files were re-signed.

Versioned source entries, verified in Git objects; behavior is explained in actions/publication:
- [release workflow](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/fd246747ea5a44f066d375f077d671834ae093bf/.github/workflows/release.yml).
- [notifier](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/fd246747ea5a44f066d375f077d671834ae093bf/ci/notify_index.py).
- [notification integration](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/fd246747ea5a44f066d375f077d671834ae093bf/ci/INDEX-NOTIFICATION.md).
- [Index scanner](https://github.com/HunYuan2333/Phinix-Plugin-Index/blob/5b1dbf2eb653a539f573bd3cc5f5305b82362c39/.github/workflows/plugin-source-updates.yml).
- [controlled publication](https://github.com/HunYuan2333/Phinix-Plugin-Index/blob/5b1dbf2eb653a539f573bd3cc5f5305b82362c39/ControlledPublication.zh-CN.md).
- [RedPacket source manifest](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket/blob/cadd91210215f02830c668109430311a45a9386e/source-manifest.json).
- [TalentTrade source checker](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/blob/36939655e4fc0d9672ceb10db1764a22788ff23c/check-source.py).
