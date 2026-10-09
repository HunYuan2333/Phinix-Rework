# Main automatic releases and troubleshooting

Read when using/configuring CI or investigating main failures. Do not load for local-only trials.

All three plugins already have complete main compilation, packaging, and formal GitHub Release workflows. This update read-only verified deployed main commits, successful cloud runs, and public Releases; see [basis.md](basis.md). No new run was triggered for skill validation. Do not say “full Actions are not implemented.” Current workflows trigger only on main push; dev/PR do not publish, and no workflow_dispatch compilation entry exists.

Exact files: `.github/workflows/release.yml`, `ci/config.json`, `ci/release.py`, `ci/build.py`, `ci/publish.py`, pack.py, `ci/notify_index.py`, and `ci/INDEX-NOTIFICATION.md`; RedPacket/TalentTrade also have `source-manifest.json` and `check-source.py`. Some CI files are absent from Example/Talent working branches but present in saved main snapshots; branch-local absence does not prove missing workflows.

## Use and adaptation

Check repository/kind/baseVersion/assetPrefix, assembly/package/module identity, reference paths/digests, hostCommit, actual workflow checkout ref, and permissions for consistency. Do not copy maintainer private references/tokens; third parties supply their own legal references. Workflow modification/implementation needs corresponding task authorization; this skill does not add workflows by default.

Every main-push commit expresses formal release intent, including CI-only or documentation/workflow changes; releases are not limited to business changes. Explain to beginners that pushing main publishes a new version; use dev and local sideloading for trials. Reserve an immutable version/draft for the commit, build the pinned host/tool, package only plugin content, verify layout/digests, save build artifacts, then publish. Current scripts allocate the next patch from historical tags with the same major/minor and reuse a reserved version for the same source SHA. This is not simply the csproj version or a version increment on every rerun.

Successful artifacts are ZIP, `SHA256SUMS`, and `build-summary.json`. Summary records plugin/sourceCommit, hostCommit, commonCommit, version, size, and digest; Release notes also record source/host. Failure-stage Actions artifacts support diagnosis, not formal Release/admission evidence. Exclude game/Unity/host/Harmony DLLs.

## Current pin differences

The reviewed Example main snapshot pins host `403cea6c207a630fae391c6dc29bbce5a5a17b3b`; all three snapshot configs do likewise. This is historical status, not a permanent required pin. The new workspace Compose Example and `--config`/`--validate` have not thereby been verified on main in the cloud. Delivery requires committing/delivering Common implementation first, updating the client's exact gitlink, game acceptance, then separately updating CI hostCommit/checkout ref and validating the cloud workflow. Do not mix new pack.py with an old packager or promise successful builds after merging.

## Failure handling

1. Read the Actions logs, reserved draft/tag, and artifacts for the exact main source SHA. Distinguish reference retrieval, host/tool, plugin compilation, ZIP validation, and upload/publication failures. gh authentication is needed only at publication; do not collect tokens.
2. Compilation failure may leave an unpublished draft. Retry the original run for that SHA; reservation reuses its version. There is no manual compilation entry, so do not invent workflow_dispatch. Source fixes go through dev and delivery confirmation; a new main commit receives a new version.
3. Already-published same-commit releases skip subsequent publication. Partial uploads are matched by filename and byte digest; reuse identical files and upload only missing ones. Stop on differing bytes; do not use `--clobber`, delete tags/Releases, or replace old assets.
4. A formal Release absent from the store belongs to [publication.md](publication.md): application, update-policy, source updates, controlled Index publication, stable. Do not repeatedly publish or bypass approval; state delays/network failures honestly.

Retries preserve existing authorization; reading logs does not grant permission to edit source or publish. Without permission for external changes, deliver the reason, reviewable correction, and next step without triggering release.

## Registered source digests

RedPacket/TalentTrade source-manifest.json binds reviewed workflow/source bytes. check-source.py verifies digests, compile allowlists, and compilation-only references. Update only digests of changed files actually reviewed; register new CI files, including notifier/tests/documentation. Never disable validation or mark the whole unreviewed workspace trusted. Two builds in this delivery failed because the workflow digest was omitted, then passed after correction. This does not justify replacing/reissuing old assets; see exact runs in [basis.md](basis.md). Skill updates do not modify these source manifests.

## Optional Index notification after a formal Release

After successful publication, independent `notify-index` job (needs: release) calls `ci/notify_index.py`. Index credentials belong only to that job, not compilation/packaging; borrowed game/host/Harmony references remain outside the ZIP. Main push alone triggers release/notification, not dev/PR; no manual plugin-build entry was added.

GitHub workflow_dispatch calls existing `plugin-source-updates.yml` with fixed `ref=main`, `check_only=false`. There is no new repository_dispatch entry or notification-parameter approval. Actual request shape:

```json
{"ref":"main","inputs":{"check_only":false}}
```

### Credentials and integration scope: maintainers

Optional repository secret `INDEX_UPDATE_TOKEN` uses a fine-grained PAT selecting only Index, with Actions Read and write. Its owner must still satisfy existing Index maintainer/admin checks. Index Contents write is unnecessary; Actions write is not granular permission to trigger only one workflow. Never reuse `BUILD_REFERENCES_TOKEN` or read/print/request credential values; inspect secret names/presence only.

Do not distribute maintainer tokens to third parties or require every author to configure this secret. After initial candidate/admission, approved third-party policies can update through hourly scans. Maintainers arrange immediate notifications separately. Without the secret, report “code deployed / notification integration pending.” A successful notification job may simply exit after a missing-credential warning; it proves neither receipt nor end-to-end integration.

### Waiting, retries, and stop conditions

The script uses API header `X-GitHub-Api-Version: 2026-03-10` and the dispatch response's specific `workflow_run_id`. Verify its path `.github/workflows/plugin-source-updates.yml`, head_branch main, and event workflow_dispatch, then wait for its conclusion. Failed scans can cause at most two further fresh events: three dispatches total, twenty-minute aggregate waiting budget (job timeout twenty-three minutes). GitHub resolves ref main for each fresh event; never rerun an old SHA. Timeout, API/credential errors, context mismatch, or exhausted retries only warn. Never reissue, withdraw, or overwrite the already-public plugin Release.

Successful scanning establishes completion of that scan flow only; inspect changed/errors, admission, and publication evidence in [publication.md](publication.md) before reporting package outcomes. A token owner lacking maintainer permissions is still rejected by Index even if dispatch succeeds; honor rejection rather than weakening checks. Credential corrections require separately authorized work.

For an unconfirmed notification after successful release: preserve Release/digests → inspect warnings/exact run URL → inspect scan report/source rejections → inspect admission/controlled publication/stable. Fresh scans or publication recovery mutate remote state and need corresponding authorization. Prepare reviewable diagnosis first; do not republish a plugin to repair notification.
