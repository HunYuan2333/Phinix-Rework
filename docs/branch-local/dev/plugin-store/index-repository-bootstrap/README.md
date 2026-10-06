# Phinix Plugin Index

[中文说明](README.zh-CN.md)

Development bootstrap for the public Phinix plugin catalog. This repository stores
reviewed metadata and catalog snapshots. Authors publish source and binary packages
in their own repositories. Workshop entries link to Steam.

**Current status: initialization only, zero approved packages.** The client currently
supports local catalog preview and static payload validation. GitHub transport,
installation, Steam actions and automated review/publication are still being built.
Creating this repository does not make the store ready for online installation.

| Path | Purpose |
| --- | --- |
| `source.json` | Development source identity: `phinix.official` |
| `packages/` | Approved package metadata; currently empty |
| `reviews/` | Candidate fingerprints and approval records; currently empty |
| `scripts/build-empty-catalog.py` | Generate an empty schema-v1 catalog and its SHA-256 |
| `catalog.json` | Initial empty catalog; client-readable, not an index Release |
| `catalog.json.sha256` | SHA-256 of the exact initial catalog bytes |

The bootstrap generator is intentionally restricted to an empty package collection.
It fails if any package JSON exists; it is not the final package/review validator.

```bash
python3 scripts/build-empty-catalog.py --snapshot <index-source-commit-sha>
```

Generated files go into ignored `dist/`. Run this after committing the source inputs;
`snapshotId` identifies that source commit, not the generated file's self-referential
commit. The checked-in initial catalog is a preview fixture. No `stable.json` is
published until a real catalog Release succeeds.

The planned release flow is: validate an approved source commit, generate
`catalog.json` and its checksum, publish a fixed catalog Release, verify the uploaded
assets, then update `stable.json`. Clients will consume released snapshots. Branch
updates and CI artifacts do not constitute approval or publication.

Do not upload credentials, game/Unity assemblies, server state, or author binaries to
this index. Listing and metadata validation are not a certification of code safety.
Client/framework development lives in [Phinix Rework](https://github.com/HunYuan2333/Phinix-Rework).
