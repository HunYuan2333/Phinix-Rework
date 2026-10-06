# A2 and controlled A4 implementation / acceptance

2026-10-05 acceptance completed: the maintainer merged PR #6; controlled publication and real idempotent retry passed. Official-source GitHub/CF downloads passed on both runtimes. [Current evidence](ControlledPublicationAcceptance.md) supersedes the pending-acceptance statements below.


[中文](准入与受控发布实现及验收.md). 2026-10-05, `dev`.

The catalog-v3 validator [PR #3](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/3) and admission/publisher [PR #4](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/4) are merged. Current deployed main: `7987b9d59b45d07a6c062c8f766d55cfc0eb4307`. [Operations and policy](../../../../Extensions/PluginStore/RepositoryAutomation/ControlledPublication.md) describes exact approval evidence, static revalidation, immutable releases/version locks, bounded closure and stable-last recovery.

Repository default workflow permissions remain read-only. The user explicitly authorized GitHub's combined PR creation/approval setting after automatic approval review rejected the initial configuration attempt; it is now enabled. Our workflows do not approve reviews and require a human admin/maintainer merger. Active ruleset `main-history-integrity` (`24505270`) prevents main deletion and non-fast-forward changes. No mandatory-PR rule is configured because the controlled publisher must fast-forward the pointer commit; code/policy changes still use reviewed PRs.

## Completed validation

```sh
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
```

Validator build: zero errors, one NU1900 warning because local vulnerability feed access was unavailable. Python: **34 tests passed**, including real trusted-validator dependency/module closure, malformed/stale/unauthorized approval, exact merged PR proof, static/policy tampering, published-version immutability, upload retry/replacement rejection, concurrent-main/failure preservation, atomic pointer commit and lost-success read-only retry. Dispatch-only YAML, fixed Action commits and non-cancelling concurrency parsed/checked. Production source snapshot hashes remain unchanged. No client/game packaging changes, so no new full game build or artifact packaging test was needed.

Remote final-code [self-check](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37314681882) passed. Main push [check](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37314956703) passed. Real Playtest 1.3.0 [intake](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37314984153) passed public repository/tag/source/asset and actual ZIP/PE/language projection checks (four files, expected immutable asset SHA-256).

An intentional all-zero fingerprint [negative test](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37315067077) failed specifically with `CandidateFingerprintMismatch`. Main remained unchanged and no admission branch was created. This red run is expected, not a configuration failure. Actual positive approval-PR creation and live controlled publication are pending the human test below; do not call the entire remote publication flow accepted yet.

## Human test now

[Submission #5](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/5) contains the existing public Playtest 1.3.0. Its verified canonical fingerprint is `882116b63fe14641300bb9d89b97be49bbb1375e127de90ace21591bdb1baf9f`.

```sh
gh workflow run plugin-admission.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f issue_number=5 -f candidate_sha256=882116b63fe14641300bb9d89b97be49bbb1375e127de90ace21591bdb1baf9f
```

Expected: a successful Plugin admission run and a three-file metadata PR. Read the fixed version/hash/source/policy/static report and merge that PR yourself. If this fails, provide the run URL; no need to repeat game tests. After merging, run controlled publication `check_only=true`, then `false` following the operations guide. The publisher verifies actual human approval/merger identities and generates the official source. Retain logs/run IDs to resolve failures.

This batch changes no game DLLs, download defaults, installed ownership or save data. `phinix.managed` remains the current Playtest source; first official publication does not automatically redirect clients or the CF gateway. After the human flow, next acceptance is actual GitHub/CF downloads through an explicitly configured isolated official source, then source/UI game testing. A3 unattended monitoring remains disabled. Pilot limits (eight approved versions, no host-module allowlist, accepted record removal blocked) must be addressed before a large production catalog. These checks are not in-game validation.
