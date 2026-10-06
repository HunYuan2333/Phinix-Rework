# First gateway extraction batch

[中文](CF拆仓首批实现与验收.md) · 2026-10-06 · S1 / Gateway M0–M1.

Original 143 Worker tests passed before freezing 46 original source files and two external fixture hashes. Source provenance is the reviewed untracked working tree, not unrelated Git history. Tests now read imported byte-identical local fixtures with provenance and an additional hash assertion. Ignore rules exclude Python test caches.

Standalone candidate `/tmp/phinix-plugin-gateway-20261006`, commit `e31bfcce71bdce4317175b0ccff2275432734d7e`, contains 62 reviewed tracked files. Production JS and Wrangler configuration are byte-identical; changes are fixture paths/provenance, necessary standalone documentation/history, read-only CI and ignores. No new license grant was invented where the original repository lacks a root license. This is not the deferred multi-repository prose/AI tooling rewrite.

[Phinix-Plugin-Gateway](https://github.com/HunYuan2333/Phinix-Plugin-Gateway) was created but remains empty (API size=0). Actions defaults are read-only and cannot approve PRs. Prepared CI pins actions and Node 26.10.0/Python 3.13 and has no cloud deployment credential/job; it has not been uploaded or run.

## Checks actually run

Clean `npm ci --ignore-scripts --cache /tmp/phinix-gateway-npm-cache --no-audit --no-fund` installed locked dependencies without main's node_modules. Candidate commands:

```sh
cd /tmp/phinix-plugin-gateway-20261006
npm test
python3 tests/ops-cli.test.py
python3 tests/network-check.test.py
python3 tests/live-probe.test.py
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-candidate-build.log npm run test:native
git diff --check HEAD
```

144/144 Worker tests, Python 5+5+4 and native anonymous adapter/limiter/SQLite/R2/internal RPC/lost-ack recovery passed. Native builds three dry-run bundles without cloud deployment. Intentional corrupt-stream faults emit runtime stacks but assertions/final exit succeed. Initial operator tests hit sandbox socket EPERM; all five passed with an ephemeral localhost server outside the sandbox. Native also ran with local-listener permission.

Additional checks compare tracked files to the 61-item manifest plus manifest itself, verify content hashes/clean tree/unchanged runtime/configuration, exclude credentials/private-key patterns and prohibited binaries/logs, and resolve current standalone entry/runbook links. These are bounded checks, not general security or game acceptance.

## Review and pending work

Review candidate `migration-manifest.json` and bilingual DEPLOYMENT guides. Durable ignored backups are `Output/gateway-migration-20261006/gateway-source-e31bfcc.tar` and `.bundle`, outside game distributables. The original input manifest is `/tmp/phinix-gateway-input-before-migration.json`, SHA-256 `54e7badfda94cce1b97c3abad35b6b80d426ba0b13af63490c1695c50fdb1969`.

Automatic review initially misclassified repository creation as source pushing. Local CLI help proved commits upload only with `--push`; the same creation command, without that flag, was re-reviewed and approved. The repository is empty. Review explicitly requires authorization to publish this concrete unpublished source/cloud-location configuration/operational history. No source push was attempted and that requirement has not been bypassed.

After authorization, upload the fixed 62-file commit and verify remote CI. Then adopt the existing service through M2/M3 with actual deployment/rollback/protocol/download/audit evidence and applicable game smoke. Do not remove main's Worker or existing usable deployment entry beforehand.

No live Worker version/routes/bindings/token/player state were changed. No immediate Mod rebuild/game test is required for this offline extraction; the separate formal-origin S0 check remains pending. Old PoC permanent deletion still needs separate concrete approval, not source-publication permission.

Identity/format/save/item/ACK behavior is unchanged. Publication exposes account/domain location metadata, so review its scope. Production takeover/transport acceptance is pending; M0/M1 is not all S1 or official-package extraction complete.
