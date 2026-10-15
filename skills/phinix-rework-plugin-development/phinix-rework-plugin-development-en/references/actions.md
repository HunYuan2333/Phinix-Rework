# Optional automated releases

Manual upload of a locally verified Release and automated builds are both valid. Inspect workflows only when adopting automation or debugging existing CI; simple plugins do not require a cloud pipeline.

## Fixed inputs

Maintained plugins pin a Host commit in `ci/config.json`; Host's Common gitlink pins shared source. Private 1.6/1.7 compile references use fixed assets/digests. Read the actual files; a historical skill SHA is not the latest pin. Do not casually track main in CI. CI may override package versions; final Releases/ZIPs are authoritative.

Build both runtimes against API3 references. Stage outputs outside source, validate packages/reference closure and exclude game/Host DLLs. Maintained `release.py`, `build.py` and `publish.py` are repository-specific implementations, not automatically suitable for every third-party project.

## Publication and retries

The maintained flow reserves a version, builds/preflights both runtimes, verifies uploaded assets and then publishes. Preserve exact draft/run identities on failure. Determine whether publication already happened before retrying; notification failure does not justify another version, asset replacement or unlimited retries. A Tag binds exact package bytes.

Version allocation, source-digest checks, queues and private-reference access follow actual scripts/workflows. Push permission does not authorize deploying every component. Do not disable source checks to bypass failures caused by CI-only edits.

## Index notifications

`ci/notify_index.py` wakes DLL source scanning only; it does not approve packages or publish themes. Maintainers may configure `INDEX_UPDATE_TOKEN` with Index Actions dispatch permissions. Third-party authors do not need it. Missing/failed notifications leave the Release intact and produce warnings; scheduled scanning can still discover updates under approved policies.

The current notifier dispatches at most three fresh-main scans within a shared twenty-minute deadline. This is an implemented bound, not guaranteed zero loss or instant admission. On TrustedHeadChanged use a new snapshot, not an old GITHUB_SHA rerun. Verify notification, scan report, changed/errors, controlled publication and store results separately; see [publication.md](publication.md).

For theme automation, first evaluate the separate theme admission workflow. Do not advertise DLL hourly scanning as existing automatic theme updates.
