# Admission and controlled publication

Pre-launch cleanup: the official index does not list Playtest. Maintainers own `catalog-exclusions.json` on main; it excludes listings from new catalogs without changing accepted versions, ZIPs, approvals or publication locks. Publication still rechecks all approved records/artifacts and then verifies the complete visible dependency closure; an empty catalog is valid. Exclusions cannot authorize unpublished versions. Keep the developer fixture only in its standalone repository. The player client offers only GitHub/CF access to the official index. Keep historical snapshots for audit.

[中文](ControlledPublication.zh-CN.md). 2026-10-05. The normal path is one maintainer approval label followed by automatic A2/A4. A3 version monitoring remains disabled.

Workshop candidates follow the same human label and evidence flow. Their reports, policies and locks bind only listing metadata and the Workshop/Mod IDs; they have no ZIP/PE checks or DLL auto-update policy. Publication includes only the latest approved metadata revision for each fixed Workshop identity and retains all prior evidence.

## Maintainer action

1. Review the submission, source and Plugin intake report. Static checks do not prove code safety or source/binary correspondence.
2. On that open submission Issue, add **`plugin-approved`** as a repository **admin or maintainer**. This is the only routine human approval action.
3. The trusted **Plugin label admission** workflow binds the webhook body to a canonical candidate fingerprint and the specific label event/reviewer IDs. It rechecks public origin, ZIP, PE and localization without executing author code, creates an evidence-only PR and merges it automatically with an exact head SHA.
4. On successful completion, **Plugin controlled publication** automatically rechecks approval provenance, PR contents, artifact bytes and complete package/module dependency closure, then publishes an immutable catalog and updates stable atomically. Issue comments link to both runs. Successful publication verifies the locked receipt/current catalog and unchanged body, then closes the Issue and clears `plugin-error`. Failed admission/publication adds `plugin-error` and leaves it open. No additional human review, PR merge or workflow dispatch is required.

Example human test:

```sh
gh issue edit ISSUE --repo HunYuan2333/Phinix-Plugin-Index --add-label plugin-approved
gh run list --repo HunYuan2333/Phinix-Plugin-Index --workflow plugin-label-admission.yml --limit 5
gh run list --repo HunYuan2333/Phinix-Plugin-Index --workflow plugin-publish.yml --limit 5
```

If the candidate changes, the Issue closes, or the label is removed/re-added before publication, pending approval is invalidated. Fix the application and remove/re-add the label to authorize a new run; **re-running an old admission attempt is rejected**. Labels applied by a write-only collaborator or bot, arbitrary comments/labels, and stale events never authorize publication. Once published, the accepted version and approval locks remain immutable; later Issue edits or label changes do not revoke historical releases.

Static failures mention the submitter on the unchanged Issue with the code, corrective guidance, example and run link, and add `plugin-error`. Edits trigger a new check; the error label clears only on successful publication. Notification/closure failures also attempt an error tag/comment and never undo committed publication.

## Evidence and permissions

New versions add exactly four metadata files: `packages/ID_HASH/CANDIDATE_HASH.json`, `reviews/...`, `policies/...`, and `label-approvals/RUN_ID.json`. The review binds source, canonical candidate and Issue body hashes, actor/numeric ID, label name/event ID/time, trusted workflow/run/commit/attempt, static report and policy hashes. The policy pins repository/owner IDs, channel, management, assembly/module identities and dependency IDs; **manual-only** means each version still needs approval, not that post-approval automation is disabled. A3 automatic version admission requires separate implementation.

An identical already published candidate creates only a new label receipt. It rechecks the same artifact and republishes a snapshot with one catalog entry, preserving the original review, version lock and DLL asset. A changed candidate for an accepted ID/version is rejected. `approval-locks/RUN_ID.json` permanently binds the receipt hash after publication. This supports testing the new label path with Playtest 1.3.0 without replacing an accepted version.

Default workflow permissions remain read-only. Validation reads only; admission requests contents/PR write solely for the evidence PR and automatic merge; publication requests contents write solely for catalog/locks/stable; notifications request issues write. No approving PR review is submitted. GitHub's combined create-and-approve setting is enabled to allow PR creation; no new PAT/App/server is required. CF uses its independent read-only origin token. [GitHub settings](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository).

The publisher uses `workflow_run` for the completed successful trusted label workflow, verifies the live run identity and requires its own receipt in the input tree. It checks out only the fixed default-main commit, never author code or upstream artifacts. [GitHub workflow_run](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run).

## Atomic publication and recovery

Admission and publication share `index-metadata` concurrency without cancellation. Admission checks the current main head and latest approval immediately before automatic merge. Publication builds the complete v3 closure, creates a fixed draft `catalog-v3-SNAPSHOT` Release, uploads without clobbering, verifies downloaded bytes/hash/size and source identity, then exposes the Release. One final non-forced Git update commits immutable published metadata, version/approval locks and `stable.json`. Concurrent changes stop stale writes; uploaded unreferenced snapshots do not replace the player entry.

The publisher reuses matching partial uploads. A retry after a lost successful pointer response verifies the exact direct-child publication commit and asset, then reports `publication.already_complete` without writing. A best-effort comment failure does not fail an already committed operation. A failed admission may leave an unmerged evidence PR; check its run before retrying. A merged but unpublished receipt whose Issue/label changed blocks publication until that exact pending approval is resolved; do not bypass the rejection or edit accepted locks.

Manual recovery still exists, but is not a routine approval step:

```sh
gh workflow run plugin-publish.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f check_only=true
# Only after that verification succeeds:
gh workflow run plugin-publish.yml --repo HunYuan2333/Phinix-Plugin-Index --ref main -f check_only=false
```

The former exact-fingerprint `Plugin admission` dispatch remains available for recovery of the already accepted manual path; those records still require a human-merged exact three-file PR. Label records allow only the fixed GitHub Actions bot to create and merge the PR. Neither path grants approval authority to arbitrary bot merges.

Pilot limits: eight version records, eight label receipts, 400 Issue history events, 512 MiB aggregate ZIP bytes, 512 publisher API calls and a 25-minute API deadline, plus existing per-file/package limits. Expansion is a separate task. All downloaded author DLLs are inspected statically, never loaded. Client-side host/game/CLR compatibility checks still apply. Official output and the player client default are `phinix.official`.

## Validation

```sh
dotnet build Extensions/PluginStore/RepositoryAutomation/Validator/Validator.csproj --configuration Release --no-restore
python3 -m unittest discover -s Extensions/PluginStore/RepositoryAutomation/tests -v
```

In the index repository use `Validator/Validator.csproj` and `tests`. 52 regressions cover exact manual and label approval, actor/event identity, edits/removal/readdition, automatic merge scope/head, successful-run and bot proof, immutable version/receipt locks, workflow-run provenance, rechecking after upload, notifications and atomic publication recovery. Remote human labeling is a distinct acceptance step; console/Actions checks are not in-game validation.
