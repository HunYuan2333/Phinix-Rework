# Formal delivery and Index

Read after local verification when preparing user delivery. For cloud workflows see [actions.md](actions.md); version evidence is in [basis.md](basis.md).

## Git and authorization

Recommend main/dev for new projects: develop on dev; main is the formal release entry after maintainer delivery confirmation. Inspect existing branches, parallel changes, and workflows instead of rewriting history. Pin host commits and Common/protobuf gitlinks. Prepare reviewable ZIPs, digests, validation records, release notes, and candidate metadata before using existing authorization to push, create Releases, or submit Issues/PRs. Without authorization stop at concrete artifacts. Do not use main as a temporary compilation trigger.

## Two routes

| Route | Sequence and boundaries |
| --- | --- |
| Managed DLL | Canonical ZIP/preflight and game verification → formal non-draft/non-prerelease GitHub Release → Index candidate/admission review → update policies/source updates → controlled Index publication → store visibility |
| RimWorld mod | Complete mod game verification → Steam Workshop publication → Workshop entry/admission/Index publication; game/Steam owns files, no complete mod ZIP store installation |

Versioned Index materials: README.zh-CN.md, ControlledPublication.zh-CN.md, SourceUpdates.zh-CN.md, `examples/managed-submission.json`, `examples/workshop-submission.json`, and actual Issue forms/workflows. Acquire the target Index version; do not infer current policies from older workspaces. Check that templates exist and follow their actual schemas.

DLL candidates include public source identity, formal Release/tag/commit, asset ID/name/size/SHA-256, manifest identical to the ZIP, and multilingual descriptions/changelogs. Exclude game/Unity/host/Harmony DLLs, credentials, and personal paths. Static checks prove neither DLL safety nor automatic source-to-bytes correspondence. Workshop candidates bind fixed Workshop/mod identities and metadata; admission is metadata-only, without DLL ZIP/PE validation or DLL update policies.

New plugins require an application and maintainer/admin review with `plugin-approved`; authors must not grant themselves approval. Trusted workflows generate exact candidate evidence PRs, admit candidates, then revalidate assets/provenance/dependency closure, publish immutable catalogs, and atomically update stable. A Release does not mean admission, automatic Index membership, or immediate visibility.

## Approved source updates

Only DLL packages with approved update-policy can update automatically; this is not a fixed three-plugin list. Policies may constrain stable three-part versions, same-major, asset prefixes, author/repository/assembly/module/dependency/external-mod identities, and descendant tags. Out-of-policy identities/major versions, missing assets, byte changes, failed validation, or previous publication still pending stop the flow for manual review/policy approval.

The newly deployed configuration was checked on 2026-10-09: `plugin-source-updates.yml` schedules hourly scans (currently `17 * * * *`), traverses approved policies, and admits eligible updates in batches; no three-plugin list is hard-coded. Before execution verify target-version workflow/source_updates.py ordering, limits, and inputs. Manual `check_only=true` only discovers/validates; false enters admission. Post-release notifications call that entry without granting approval. New plugins still need candidate/manual admission; version/source/contract-policy violations pause updates.

Hourly scans remain fallback coverage, but GitHub schedules may be delayed or dropped, with no punctual hourly guarantee. Optional immediate notifications accelerate discovery; neither route guarantees every intermediate Release is admitted or immediate listing. See [actions.md](actions.md) for parameters, optional maintainer credentials, and waiting. Third parties without them retain ordinary admission and scheduled updates.

On failures, read the matching run and `Update blocked: <packageId>` issue/application feedback. Never overwrite older versions; metadata corrections do not permit replacing approved bytes. Renew expired policy/label evidence through the actual new event/run process; never bypass review, edit locks, or directly change stable. When Index evidence requires a first attempt, source-update failure needs a new run rather than rerunning the old attempt. This differs from plugin-main same-commit retries.

Catalog updates only notify players: confirmed download and restart load new code; installed DLLs are not silently changed. Troubleshoot GitHub/CF caching through formal stable and immutable evidence. Report network failure honestly. When awaiting review/publication, identify the stage and next responsible action rather than claiming listing.

## Assess each success layer

Check in order: plugin build passed → formal Release public → notification received → scan completed → candidate admitted → controlled catalog published → version visible in store. Notification is an optional acceleration layer; third parties can wait for scheduled scans and need not complete notification integration.

| Signal | Further evidence required |
| --- | --- |
| release job/build success | Non-draft/non-prerelease Release, target commit, exact ZIP/digest |
| notify-index job success | Missing-secret warning versus actual returned scan run ID; matching workflow/main/event |
| Scan success | That run's actual `updates.scan_complete` changed/errors and source-updates artifact `updates/report.json`; do not mistake fixture runId=123 output for the real scan |
| changed=false | No eligible new version can be normal; it does not establish the newly released package is visible. Inspect errors/policies/existing catalog |
| One source rejected while others succeed | Diagnose its packageId/reason; overall green does not prove every source updated |
| changed=true / propose success | Candidate/batch evidence, exact admission commit/approval records, then controlled publication |
| Controlled publication success | Formal stable snapshotId, catalog digest/size, target package version, then client source refresh/store display |

## Index metadata concurrency and recovery

Four writers, `plugin-admission.yml`, `plugin-label-admission.yml`, `plugin-source-updates.yml`, and `plugin-publish.yml`, share `index-metadata` with `queue: max` and `cancel-in-progress: false`. Pending work is retained under a bounded queue, currently up to 100 waiting tasks per group; excess triggers are not guaranteed. It is not unlimited/lossless. The older cancel-in-progress:false-only configuration still replaces pending work and should not be recommended. [Concurrency rules](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency), [queue limits](https://docs.github.com/en/actions/reference/limits).

While queued, admission/catalog publication can advance main. Scans first compare current main with their original GITHUB_SHA; stale snapshots fail with `TrustedHeadChanged`. Preserve all source/evidence/commit/immutable-publication checks. Never rewrite GITHUB_SHA, force-push, delete locks/receipts, or bypass validation. A fresh event on current main creates a new snapshot; rerunning the old event does not refresh it.

Authorized manual recovery starts by inspecting failure/current state, then dispatching a fresh current-main `plugin-source-updates.yml` scan (check_only=false for admission), not rerunning an old attempt. If admission completed but publication did not, inspect evidence/validation, then use current-main `plugin-publish.yml`: check_only=true first, and only after passing checks and authorization use check_only=false. Do not alter old assets/locks or bypass policy rejection as if it were a queue fault. See [basis.md](basis.md) for verified status/concurrency evidence.
