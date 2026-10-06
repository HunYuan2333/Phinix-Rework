# Single-label admission implementation and acceptance

[中文](标签准入实现与验收.md). 2026-10-05.

## Delivered

[Implementation PR #7](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/7) was merged at `ca018573c417f30ecc6a9d2546238247a21c2b22`. Deployment is `e1d00e51c937c2ef069d138f5ef7ac494ee58e64`, including the corrected folded YAML conditions. Both the label-admission and controlled-publication workflows are registered under their intended names. `plugin-approved` exists. Default workflow permissions remain read-only; the previously authorized combined create/approve setting is unchanged.

Normal path: maintainer label -> trusted exact candidate/static checks -> audit metadata PR -> automatic exact-head merge -> successful-workflow trigger -> provenance/origin/ZIP/dependency rechecks -> immutable catalog -> atomic stable/locks -> Issue feedback. No further manual PR approval or publication action. The old dispatch path remains for recovery only; A3 and game defaults/UI are outside this delivery.

The label event captures the original webhook body rather than approving whatever content is current later. Actor permission/numeric ID, per-Issue event ID/time, trusted run/commit/attempt and exact bot PR contents are independently verified. Edits, closing, removal or readdition invalidate pending approval. Published records/locks remain immutable. Identical accepted 1.3.0 can be retested through a receipt-only PR; its original candidate/review/asset is preserved and the catalog remains one entry.

## Validation actually run

```sh
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
python3 -m py_compile Extensions/PluginStore/RepositoryAutomation/scripts/admission.py Extensions/PluginStore/RepositoryAutomation/scripts/label_admission.py Extensions/PluginStore/RepositoryAutomation/scripts/publisher.py Extensions/PluginStore/RepositoryAutomation/tests/test_label_admission.py
git diff --check

gh workflow run plugin-intake.yml --repo HunYuan2333/Phinix-Plugin-Index --ref codex/label-admission -f issue_number=0
gh workflow run plugin-publish.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f check_only=true
gh workflow run plugin-intake.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f issue_number=8
```

Local Validator build succeeded with one NU1900 warning because the package vulnerability feed was unavailable. 47 regressions passed. Final branch self-check [37324619962](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37324619962) and main self-check [37324746620](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37324746620) passed. Real read-only publication preflight [37325232360](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37325232360) passed. Real candidate check/report is [37325238465](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37325238465).

Initial publication workflow conditions containing `#` were truncated by YAML comments; GitHub rejected the workflow before any job ran. Folded conditions fixed this in `e1d00e...`. We checked parsed condition contents, actual workflow registration and the real successful publication preflight, rather than treating the passing Python suite as workflow validation. Existing stable blob remains `4607e80c591278649d7cbf1e4787d33628112827`; no release or version was replaced during deployment/preflight.

## Real human label acceptance completed

Open [Issue #8](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/8) and add `plugin-approved`, or:

```sh
gh issue edit 8 --repo HunYuan2333/Phinix-Plugin-Index --add-label plugin-approved
```

This uses the already accepted 1.3.0. Do not manually merge its generated PR or dispatch publication. Expected: label workflow completes, one receipt-only PR merges automatically, controlled publication runs automatically, Issue links report success, new stable/catalog snapshot preserves one unchanged package. Then verify GitHub/CF through the existing official-profile CLI download checker. The user added the approval label on Issue #8. Admission run 37325345900 passed, PR #9 merged automatically at 570edb19d38788318971ea132be97e899990b62a, and automatic publication run 37325481455 passed. Stable committed at b2cfabcf408cebbc8909a79cf7cea6806024006c; catalog SHA-256 89a7a6e4faa195a062b6f1af35c225d9f0b86e103537e5c5d1f087157c810d54, Release 403814071, asset 612813064, 2364 bytes. One unchanged 1.3.0 package remains. This is a GitHub test; no client rebuild or in-game validation is being claimed.

## Persistence and limits

New receipts are locked atomically with stable. Removing/changing accepted version or approval locks is rejected. A pending receipt already merged before an edit/withdrawal blocks publication; recovery must remove only the uncommitted-to-stable receipt and any exclusively associated unpublished version metadata through an audited maintainer change, then authorize a fresh label event. Do not alter published locks. No ordinary manual review is added to the successful path.

Pilot caps remain eight versions and now eight label receipts, bounded 400-event Issue history and existing API/ZIP/time limits. Scaling and automatic pending-withdrawal cleanup are subsequent operations work. CF protocol/cache and client ownership have not changed; existing installs remain under their original source identity.

## Success closing and failure feedback follow-up

User requested automatic successful closure, error tags on failure and corrective static feedback to authors. [PR #10](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/10) is deployed at `af8eb731ca9c2af460a7b7ae2faa59e9cd67f5fa`; 52 local and remote regressions passed ([37326880332](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37326880332)). Closure verifies the specific receipt lock/current stable snapshot and unchanged issue body before closing as completed. Successful publication removes `plugin-error`; failed intake/admission/publication leaves the Issue open and tags it. Static rejection mentions the author with format guidance, canonical example and run link. Comment/closure failures are logged and attempt an error tag without undoing publication.

Real intentionally malformed [Issue #11](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/11) was rejected with InvalidJson, remains open, has plugin-error and a corrective @author comment; [run 37327214312](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37327214312) is an expected failed intake with successful reporting. [Issue #12](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/12) uses the exact already accepted candidate to test the full new closing path. Automatic approval review rejected agent-applied approval labels for #12 because that specific Issue approval lacked explicit user authorization. The user then added both labels personally. The rejected action applied no labels. Admission [37327504586](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37327504586) succeeded; receipt-only [PR #13](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/13) merged automatically to 706f53dcfec3db55be001d97141ca8402cf9e0b3; automatic publication [37327639326](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37327639326) verified, published and notified successfully. The bot removed plugin-error and closed Issue #12 as completed at 2026-10-05T14:49:31Z, retaining plugin-approved. The unrelated label run 37327546755 and its downstream publication 37327643140 were skipped, confirming an error label does not authorize publication. Normal reviews add only plugin-approved; both labels were used solely to test successful error cleanup.

Live reads of the auto-published snapshot passed on GitHub/.NET 10 and CF/Mono, with unchanged package hash 5a8e14105639e0180b10cbf82923d91fe64e2e6a5a14ae6728a6851ba7575f88. The first GitHub attempt timed out at origin; one independent retry passed. Commands actually run:

```sh
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-label-live-github-net10-retry --official-github
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.official /tmp/phinix-label-live-cf-mono --official-cf
```

Final stable commit: `355302b5c1316fc843bf5d2a1ba3e9984fce2b78`; snapshot `706f53dcfec3db55be001d97141ca8402cf9e0b3`; catalog hash `ff0e27b8385abf1512bf88e198a07fc5ee72bfc8d5a167758fc68ddb143ed5eb`; Release 403830429 / asset 612851475. The catalog retains one unchanged package. Fourteen deployed implementation/test/workflow/document files match local bytes. Original successfully published Issue #8 was also closed through the new guarded feedback function using a CLI-backed API adapter; the replay did not add approval labels or publish. All requested acceptance is complete; Issue #11 deliberately remains open as error-report evidence.
