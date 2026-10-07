# GitHub plugin submission and publication

[中文](GitHubBotGuide.zh-CN.md). Authors publish their source and a fixed managed DLL ZIP in their own public repository, then submit metadata to the official index. GitHub Actions performs checks and publication; no author token or separate bot server is needed.

## Workshop listings

Use Issues → New issue → **Workshop listing** and copy [workshop-submission.json](examples/workshop-submission.json). Provide `channel: steam-workshop`, `management: rimworld-mod`, `workshopId`, `rimWorldPackageId`, name, summary, author, license, tags and supported RimWorld versions. The example points to Phinix Rework **for listing tests only**; replace its fields for your own submission.

No GitHub repository, release, ZIP, DLL manifest or package language files are needed. The bot checks listing metadata only: it does not inspect Workshop mod code or certify future Steam updates. A maintainer adds `plugin-approved`; the existing evidence PR, automatic publication, error label and Issue closure apply. Steam and RimWorld own subscription, download, activation, dependencies and updates. GitHub and CF distribute the same catalog metadata.

Changes to listing metadata require a new submission and maintainer approval. The newest approved revision is visible; historical candidate bytes, reports and locks remain immutable. The same listing ID cannot silently switch its Workshop ID or Mod packageId. GitHub DLL source monitoring does not apply to Workshop listings.

Local metadata-only validation:

```sh
python3 scripts/bot.py check --input examples/workshop-submission.json --validator Validator/bin/Release/net10.0/Validator.dll --output workshop-check
python3 scripts/catalog.py project --candidate examples/workshop-submission.json --validator Validator/bin/Release/net10.0/Validator.dll --output workshop-record.json
```

## Submit a plugin

Use Issues → New issue → Plugin submission. Copy [the current candidate example](examples/managed-submission.json), replacing every identity, commit, version, asset ID, length and digest with your own release values. The schema-v3 catalog uses localized name, summary and changelog; language JSON for the plugin UI is included and hashed inside the ZIP.

The working example is [Phinix Example Plugin](https://github.com/HunYuan2333/Phinix-Example-Plugin), with [source and release v1.0.0](https://github.com/HunYuan2333/Phinix-Example-Plugin/releases/tag/v1.0.0). Its normal admission is demonstrated by [Issue #15](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/15), [evidence PR #16](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/16), and the [successful publication](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37335979507).

New, edited or reopened applications receive a static report. Checks cover strict metadata, public repository/owner identities, fixed tag/source commit, published release/asset identity, size, SHA-256, ZIP layout, PE references, manifest and resource declarations. Author DLLs are never loaded or executed. Correct invalid submissions using the report and update the Issue; a changed candidate invalidates its earlier approval.

## Review and publish

1. Review the exact candidate and report; static success does not prove source/binary correspondence or in-game behavior.
2. Add `plugin-approved` to approve that candidate. Only authorized maintainers can approve. Close an unapproved Issue to reject it.
3. Trusted automation rechecks the candidate, merges a metadata-only evidence PR and publishes the catalog. No second human approval is required.
4. Successful publication removes `plugin-error` and closes the Issue. Failures leave it open with `plugin-error` and diagnostic guidance. See [publication operations and recovery](ControlledPublication.md).

The official player source is `phinix.official`, using GitHub direct or CF acceleration for the same protocol, identity and immutable bytes. Playtest stays outside the player catalog; its historical approved records and regression fixture remain for audit. Later stable releases are automatically monitored inside an explicit approved-source policy. Changes outside that policy need a new fixed candidate and maintainer review; see [approved source updates](SourceUpdates.md).

## Retry and local validation

```sh
# Static trusted-tool self-check only.
gh workflow run plugin-intake.yml --repo HunYuan2333/Phinix-Plugin-Index -f issue_number=0
# Recheck and report an existing submission; this command alone does not approve it.
gh workflow run plugin-intake.yml --repo HunYuan2333/Phinix-Plugin-Index -f issue_number=123
gh run list --repo HunYuan2333/Phinix-Plugin-Index --limit 5
gh run view RUN_ID --repo HunYuan2333/Phinix-Plugin-Index --log-failed

dotnet build Validator/Validator.csproj --configuration Release
python3 -m unittest discover -s tests -v
python3 scripts/bot.py check --input examples/managed-submission.json --validator Validator/bin/Release/net10.0/Validator.dll --output /tmp/phinix-bot-example-check
```

The last command contacts GitHub and validates the real release; use a fresh output directory. Workflow reports expire after 14 days; permanent approval records, hashes and immutable publication snapshots live in the repository and Releases.

## Maintenance boundaries

Workflow permissions default to read-only; individual report/admission/publication jobs request the minimum writes needed. Workflows pin trusted code and action revisions, use explicit publication orchestration, and never build author scripts with publishing credentials. Cloudflare uses a separate read-only origin token. Do not submit tokens, private data, game reference DLLs or generated binaries to the index.

`Validator/Production` is an explicit snapshot of production validation code with origins and hashes in `production-provenance.json`; refresh deliberately and verify regressions plus real assets. Older Playtest regression input is under `tests/fixtures`, separate from the current author example. Preserve accepted version locks and audit snapshots; never overwrite published bytes. A separate GitHub App and AI review can be considered later when required.
