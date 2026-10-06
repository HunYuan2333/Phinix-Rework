# Store client coupling acceptance

[中文](商店客户端耦合收口验收.md) · 2026-10-06

The user approved C1 and client-local remedies. C1/C3 client changes and C2 local static validation/snapshot tooling are delivered. Independent index snapshots/tests are not updated; C4 Gateway and C5 index owner configuration remain. No legacy business fix, RedPacket upload, public prose rewrite, commit or deployment was performed.

- C1 removes fixed business assembly reservations; game/CLR/framework names stay protected and legacy CatalogReader delegates to the same rule. Identity and filename aliases are checked inside each manifest and against actual occupancy in Store planning, installation/startup, local Mod scanning, supplied packaging host facts and local publication closure. Comparison is case-insensitive. Disabled modules still occupy loaded CLR identities. Digest/PE/dependency/ownership gates remain. The old remote parser may reject future extracted business identities until separately synchronized.
- C2 separates static ZIP inspection from installation input, language dictionaries from filesystem loading, and diagnostics/digests from inventory/planning. Public types, namespaces, assembly ownership, old APIs, storage keys and SHA behavior remain. Local snapshots shrink from 24 to 14 needed files: twelve unused runtime/transaction/transport copies retired and two static helpers added. Candidate DLLs are never executed.
- Snapshot tooling checks pinned set/hashes and optionally main-source parity. Explicit refresh validates/reads all sources before replacing files and saves hashes last; it rejects tampering, extra source, missing source, unsafe paths and links. Refresh is not a multi-file atomic transaction: interrupted writes fail subsequent digest checks. Six tests cover integrity/parity/idempotence/rejection. Standalone source comparison explicitly skips when main source is absent.
- C3 removes ManagedInstallation.cs from normal compilation but preserves historical source/tests and player records/data. DLL installation remains generic, Workshop subscription-only. The compiled Store no longer contains the retired installer type-name string in metadata.

.NET 10 and net472/Mono each passed 1693 assertions plus six actual startup/registry children (18/18/18/16/16/20). Store passed 902 assertions, index 84 methods, package-layout two real MSBuild tests. Main Release 1.6 built with zero errors/seven existing warnings; final Store built with zero warnings/errors. Validator, packaging tool and both test projects built successfully.

PowerShell is unavailable; native check-artifacts.ps1 was not run. Equivalent checks passed for 21 required artifacts, load folders, retired business/game-DLL absence and exact rebuilt Utils/Store matching. Evidence: `/tmp/phinix-store-coupling-fix-artifacts.json`; Store digest `895b82a38c250becd91bf268efc396934f8aa15938913ea2da3b90326fecff39`.

An existing fixed language-key count initially failed after other work added RedPacket translations. The regression now retains all keys from immutable RedPacket `d641476d868c57c9509e82144ec7141090ce2e34` (71) and Talent `d8caafa77e3caf5f14d0cac66c4c99a9483c7f19` (114), allowing additions while preserving complete bilingual/scope/fallback checks. Source hashes are in fixture provenance.json. Legacy connection/language/business source was not changed.

Exact build/test commands are in the [Chinese companion](商店客户端耦合收口验收.md). Output/phinix-rework is rebuilt. Deploy the complete package with matched Utils/Store; new Store calls the new helper and cannot use old Utils. Existing plugin ZIPs/data/save formats need no rewrite or repacking.

No in-game, real-network, CF or remote-publication acceptance was performed. Combine incremental Example Tab/settings/language, GitHub/CF listing, host recovery after Store disable and unbundling checks with the next Store UI batch. Do not infer legacy business recovery acceptance.
