# TalentTrade input safeguards and normal intake

2026-10-06 status correction: [Issue #22](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/22#issuecomment-6015241465) was approved by the user, published successfully and automatically closed; both business plugins are removed from the main package. Unapproved/bundled-retention notes below are historical. Talent business bugs are deferred; standalone game/save/cross-restart recovery gates remain separate from listing and basic user feedback.

2026-10-06 latest user instruction: [RedPacket/TalentTrade are removed from the bundled main solution/package](OptionalPluginsUnbundlingAcceptance.md), with stale-output cleanup and build/fixture checks passed. This supersedes earlier bundled-retention status, not the pending game/save/security gates. Independent admission remains separate: Talent Issue #22 is unapproved; RedPacket has no published asset.

[中文](人才贸易输入保护与准入验收.md) · 2026-10-06 · Development validation; business/save acceptance remains pending.

The user made both independent repositories public and requested the normal publication route, with approval labels added personally. TalentTrade v1.0.1 is published at [its fixed release](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/releases/tag/v1.0.1); [index Issue #22](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/22) submits the candidate without `plugin-approved`. Main bundled DLLs/resources remain until standalone acceptance. No main dirty-tree commit, relay probing, service migration or credential modification occurred.

## Implemented protections

- HTTP bodies: 16 MiB; compressed pawn bytes: 8 MiB; GZip output/XML: 32 MiB. Reject before overflow, use strict UTF-8, prohibit XML DTD/external resolution. These are byte/character limits, not a complete parsing-time or authenticated-service boundary.
- Incoming transport retains queued messages under count/character pressure and holds its event cursor. Server page-head IDs no longer prove consumption. Reject oversized/invalid events explicitly; later parts of a legitimate blob are no longer deduplicated as the first part. Bound blobs, parts, retained characters, IDs and processed keys. No blob expiry was added.
- Register local purchase intent before sending. Require the expected listing, seller, buyer, save token and exact Game instance before delivery. Retain the first raw payload on deferral/queue failure and prevent changed payload substitution or replay after uncertain handoff. Remove intent only after the real delivery returns successfully. Cosmetic notification failure cannot cause another delivery.
- Timeout preserves intent/listing, allowing a correlated late response; it does not prove remote failure. Bounded structured `talent.purchase` audits contain IDs/outcomes, not pawn payloads. Existing program/type/module/storage identities and legacy wire format remain.

**Remaining gates:** the purchase record and its retained payload are process-local, not durable. Switching/clearing/quitting can lose them; no cross-restart ownership guarantee is claimed. Legacy HTTP, unauthenticated message origin, trust-based payment, outgoing queue eviction/retry ordering and limited history remain. Matching UUIDs does not authenticate the sender. Missing-package resaving, late GameComponent discovery, native Scribe/drop pods and standalone save recovery need actual game evidence. Do not remove bundled products or call this production-ready solely because static checks pass.

## Fixed evidence

| Evidence | Result |
| --- | --- |
| Independent source | `d8caafa77e3caf5f14d0cac66c4c99a9483c7f19`, 48 allowlisted files / 34 C# files |
| Source CI | [37452791509](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/actions/runs/37452791509) and [37452703167](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/actions/runs/37452703167): success |
| Asset | release `404601412`, asset `615244724`, `phinix-legacy-talenttrade-1.0.1.zip`, 62835 bytes |
| ZIP SHA-256 | `d0ef4ee574b1a15c02d6485bd326d7d3770df71cddf8a1367b0e5ff7019ad70c` |
| Manifest SHA-256 | `dd04c6b41f48e01ecaf79896991ebfc0228da84adf2be6231687acaa3fee096f` |
| Local trusted intake | `StaticCandidateVerified`, candidate `6629e2ba8567958de07970f5bd51b4ccc32a3689a792652d63648604fd7d13ed` |
| Index automation | [37453368551](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37453368551): validate/report success; [robot report](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/22#issuecomment-6014796302) matches the local candidate; Issue open, no labels |
| Contents | Exactly manifest, two owned DLLs and two language JSON files; no host/game/Unity/Harmony DLLs |

The first local metadata check correctly rejected a separately written changelog as `CatalogLocalizationMismatch`. The final submission uses the exact language projection from the immutable ZIP; release notes/Issue describe this increment. The published package was not overwritten. A future author version can add its changelog to the package language files. Upstream rights are retained; no new MIT grant was invented.

## Validation performed

From the main repository:

```sh
dotnet build Tests/TalentReturnRuntimeTests/TalentReturnRuntimeTests.csproj --configuration Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/TalentReturnRuntimeTests/runtimeconfig.json Tests/TalentReturnRuntimeTests/bin/Release/TalentReturnRuntimeTests.exe
mono Tests/TalentReturnRuntimeTests/bin/Release/TalentReturnRuntimeTests.exe
python3 /tmp/phinix-legacy-talenttrade-input-guards-20261006/check-source.py
python3 /tmp/phinix-legacy-talenttrade-input-guards-20261006/pack.py --phinix-package "$PWD/Output/phinix-rework" --game-references /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed --harmony-references "$PWD/.nuget/Lib.Harmony.2.3.6/lib/net472" --packager "$PWD/Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll" --output /tmp/phinix-legacy-talenttrade-1.0.1-candidate.zip --bundle-output /tmp/phinix-legacy-talenttrade-1.0.1-candidate --display-output /tmp/phinix-legacy-talenttrade-1.0.1-display.json
dotnet exec --runtimeconfig /tmp/phinix-talent-input-guards-independent-tests/TalentReturnRuntimeTests.runtimeconfig.json /tmp/phinix-talent-input-guards-independent-tests/TalentReturnRuntimeTests.exe
mono /tmp/phinix-talent-input-guards-independent-tests/TalentReturnRuntimeTests.exe
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-restore -p:BuildInParallel=false -m:1
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0 --no-build
python3 .github/scripts/update-bundled-localization.py --check
python3 Extensions/PluginStore/RepositoryAutomation/scripts/bot.py check --input /tmp/phinix-talenttrade-release-1.0.1/submission.json --validator Extensions/PluginStore/RepositoryAutomation/Validator/bin/Release/net10.0/Validator.dll --output /tmp/phinix-talenttrade-intake-1.0.1-verified
```

Outputs are immutable: choose new paths when repeating packaging/intake. Talent harnesses passed 68 assertions under .NET and Mono, both with main DLLs and independently built owned DLLs. They cover exact/oversize input, invalid UTF-8, expanded GZip limits, prohibited XML DTD, real incoming queue/cursor handling, unsolicited market delivery rejection, correlation/late/uncertain outcomes, multipart bounds and dedup retention. Managed harness passed 1636 parent assertions and existing startup child checks. Talent/independent builds had 0 warnings/errors; managed build retained one existing protobuf `net50` trimming warning. PowerShell was unavailable, so exact ZIP/source/hash checks substitute for this package increment's artifact check. Full solution/server tests were not repeated for unchanged server code. Game/Unity/Scribe behavior is not inferred from these harnesses.

## Human game acceptance and publication

1. Exit the game, back up saves/settings and copy the complete newly built main output, including language resources. First test bundled Talent on disposable saves: refresh the market, initiate a legitimate purchase, receive exactly one pawn and retain its state. Use two clients and record the listing ID.
2. Delay or disconnect delivery: timeout must leave “awaiting confirmation”, not encourage another purchase/refund. Reconnect in the same running Game and verify one correlated late receipt. Test queuing followed by switching saves; nothing may materialize in the other save. Record `talent.purchase` codes and IDs; do not paste raw pawn data.
3. Test direct trade, rental expiry/manual return and actual save/restart separately. Market-purchase restart recovery is currently a known gap; do not place valuable pawns into an unconfirmed in-flight purchase and treat restarting as recovery.
4. For managed installation use an isolated test Mod copy and a disposable save. Its bundled Talent Contracts/Client DLLs and Talent-owned sidecars/resources must be absent, while shared host dependencies remain. Never load two copies. Install/restart, settings/language switch, disable/uninstall/reinstall and missing-package resave need separate results. Preserve untouched save backups for the missing-package experiment.
5. Read the automatic static report on Issue #22. The user alone adds `plugin-approved` when accepting the candidate and its recorded limitations; automation then rechecks, merges/publishes and closes on success or labels failure. Do not approve through the agent or imply the label completes game/save gates.

RedPacket is also public after the user's own visibility change, but no RedPacket release/intake was made in this increment. Automatic approval previously rejected publishing its embedded relay credential; original-maintainer clarification is still outstanding. Its host-module dependency closure also needs an explicit, reviewed generic policy before normal admission. No credential-bearing binary upload or weakened validator is used to bypass these blockers.
