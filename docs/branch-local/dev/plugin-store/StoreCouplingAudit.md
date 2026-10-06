# Store coupling audit

[中文](商店耦合性审查.md) · 2026-10-06 · Audit only; runtime code unchanged

Follow-up: [client coupling acceptance](StoreClientCouplingAcceptance.md) delivers C1/C3 client remedies and C2 local tooling/static boundaries. The independent index is not updated; C4/C5 remain. Below is pre-remedy evidence, not online deployment proof.

## Scope and confirmed boundaries

Review actual project references and call sites against Design-Philosophy: Store registration/controller/adapters, Host startup, managed runtime/installation/management, index validators/host profiles and Gateway. Compatibility-Boundaries governs persistence interpretation. Legacy business bugs/publication remain deferred; no RedPacket upload was retried.

Main is the dirty dev worktree. Independent index was inspected at `048c8637a64efefe64ff4d95264dd9e7300dc302`, Gateway at `92aea4808c794b4a52aaa21d14830feac18d7076`. Temporary checkouts are read-only remote inputs for offline review/tests, not deployments. This is neither a full security audit nor game acceptance.

- `Client/Source/Client.csproj:204` has no concrete Store/business project reference. Store csproj `:57` references only abstractions and Utils. A PhinixClient namespace alone does not prove a host-implementation reference.
- `PluginStoreClientExtension.cs:18` uses ordinary builder registration; `Activate:25` obtains generic services and `Shutdown:49` cleans providers/controller/localizer. Host supplies a generic manager/recovery entry, not a dedicated Store Tab.
- `Client/Source/Client.cs:323` derives host module facts from actual declarations/assemblies; `:378` applies the general activation policy. Managed services do not reference Store, Unity or Verse.
- `ClientEnvironmentService.cs:16` enforces main-thread capture before `ManagedStoreController.Run:204` background work. Snapshot/progress/lifecycle checks were read statically, not stress-tested in game.
- `ManagedRepositoryAccess.cs:17` separates GitHub/CF wire behavior through the same contract. Fixed official identity is legitimate maintainer trust configuration, not an excuse to accept arbitrary player-supplied origins.
- Controller `:147` / `:173` rechecks fresh metadata/state before generic installation. No legacy item/pawn state is owned by Store.
- PR #24 discovers real PE module declarations and uses version-scoped maintainer profiles; there is no named RedPacket exception.
- Gateway `protocol.mjs:97` reads configured sources. Production chooses `src/read-only.mjs`, with R2/DO disabled. Retained historical code does not establish active paid caching.

## Findings

### C1 · P2 · Business assembly reservation in shared manifest parsing

`Common/Utils/Framework/ManagedExtensions/ManagedExtensionManifestReader.cs:15` hardcodes ChatExtension, TradeExtension, InventoryExtension and LegacyAdapter.Client in protectedNames; `:63` / `:78` reject both declared names and filename aliases. Current bundled distribution policy is embedded in the shared format parser; a future ordinary independent release would require changing generic source.

Keep game/CLR/framework reservations. Supply business occupancy through actual host facts and maintainer profiles. CandidatePlanner `:130`, installation runtime `:118` and publication closure already reject actual conflicts. Add alias, host-conflict, unknown-module and third-party parity coverage before changing this boundary. Do not remove all protection. This is coupling, not a demonstrated current installation failure.

### C2 · P2 · Shared validator snapshots have unclassified divergence

Both provenance manifests list 24 frozen files; all copies match their recorded hashes. Five local and seven index-main copies differ from current main source. The extra two online differences retain the old host-reference rule in CandidatePlanner/Metadata; other differences concern replacement/transactions and staging versus official configuration.

Pinned trusted snapshots are legitimate: candidate code or an online latest-Common fetch must not control production validation. Current `Validator/Program.cs:18` publication calls PublicationClosure, payload calls ManagedStorePayloadValidator; neither invokes the old CandidatePlanner. Do not claim current index publishing rejects Harmony upgrades based on these unused methods. Host transaction differences likewise do not establish ZIP-validation divergence.

Define the actual validator call closure, retain only needed source, classify shared format rules versus host implementation/deployment configuration, and provide explicit refresh/provenance/consistency tooling. Keep immutable reviewed inputs and regressions. Address this first so C1 can be synchronized reliably without copying an entire runtime.

### C3 · P2 · Retired full-Mod installer is compiled into the normal Store

Store csproj `:43` includes ManagedInstallation.cs. That class `:82` owns installation-v1 and `:419` / `:470` handles About.xml/phinix-package.json. Normal registration, View and controller have no calls to it; DLL installation uses the generic service, Workshop opens subscription links.

Remove this installer from production compilation or isolate it in an explicit developer tool, retaining needed tests/evidence. Trace helpers first: current v3 still consumes some CatalogReader/PayloadValidator boundaries. Do not delete all old-looking files or player records. This is package/maintenance cleanup, not renewed legacy-route acceptance.

### C4 · P3 · Read-only Gateway still shares PoC/R2 handler branches

`read-only.mjs:6` uses createGateway; gateway `:13` authorizes PoC and `:66`–`:83` handles R2/DO. Production guards reject these bindings/configuration and all 144 offline tests passed. No evidence establishes production execution of caching branches.

Prefer explicit cache/auth/operations adapter composition while retaining recovery evidence. Lower priority than C1–C3; this does not require cloud-resource deletion, immediate redeployment or paid caching.

### C5 · P3 · One redundant pinned index owner literal

bot.py `:16` defines INDEX and admission.py `:15` derives PREFIX, but publisher.py `:77` repeats HunYuan2333 in the old workflow_dispatch proof path. Owner migration may miss that literal. Label proofs return through label_admission.proof first; normal label publication is not shown broken.

Derive owner from the same trusted INDEX, retaining repository/owner identity locks. Never substitute candidate-controlled configuration. Handle with a narrow index maintenance change.

## Sequence and validation

Address C2's common validation provenance first, then C1 with the same verification set; isolate C3; handle C5 with index maintenance. C4 can wait and is not a UI prerequisite. Continue [Store/UI/package plan](StoreFinishingAndDocumentationHandoff.md) without claiming deferred business recovery complete.

Commands executed:

```sh
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
# Independent Gateway checkout
npm test
```

Results: Validator build succeeded with zero errors and one existing NU1900 vulnerability-feed warning; all 78 index regression methods passed after rebuilding, and all 144 Gateway tests passed. Project-reference/SHA-256/copy/remote-head evidence: `/tmp/phinix-store-coupling-evidence-20261006.json`. Full outputs: `/tmp/phinix-store-coupling-validator-build-20261006.log`, `/tmp/phinix-store-coupling-index-tests-20261006.log`, `/tmp/phinix-store-coupling-gateway-tests-20261006.log`. No main-game build, native PowerShell package checks, in-game/online/Cloudflare-deployment acceptance was run. Changes in this batch are working documents only; existing dirty source was not committed. Future format/runtime/package changes require the relevant .NET/Mono/package/game checks.

## Instructions for documentation authors

Use actual code and fixed inspected versions. Current Rework GitHub entry documents are `.github/README.md` and `.github/README.zh-CN.md`, not a presumed root README. First present a fact/evidence table and outline for user review. Describe Talent listing, PR #24 merge and deferred RedPacket separately. Prose-only models do not implement C1–C5 or describe recommendations as completed fixes.
