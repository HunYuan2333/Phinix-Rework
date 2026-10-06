# Repository metadata protocol v1 draft vectors

[中文版](README.zh-CN.md)

These fixed bytes exercise the client parser and local draft generator. `catalog.json`
is the existing empty bootstrap catalog (`phinix.official`); the index repository and
owner IDs are known bootstrap identities. **Release ID and asset ID `1` are synthetic**.
No release or endpoint represented by these files has been published. They must not
be used for GitHub origin fetching, approval, installation or public PoC deployment.

The stable pointer binds the raw catalog and `published/<snapshotId>.json` through
SHA-256 and exact byte lengths. Snapshot ID means the catalog's input commit; the
later commit writing these generated files is not its snapshot. Client requests use:

- `/v1/sources/{source}/stable`
- `/v1/sources/{source}/snapshots/{snapshot}/published/{publishedSha256}`
- `/v1/sources/{source}/snapshots/{snapshot}/catalog/{catalogSha256}`

`schemaVersion` versions stable/published independently; `catalogSchemaVersion=1`
keeps the existing strict catalog intact. The descriptor binds the index repository,
repository/owner/release/asset IDs, and `assetName=catalog.json`. The client validates
these fields and uses only its configured HTTPS origin for requests. The future Worker
must compare them against the approved source configuration and actual GitHub asset.

This unsigned draft trusts the configured endpoint and normal TLS validation. Hashes
from the same endpoint provide byte consistency, not protection from a forged gateway
approval. Signatures and production publication checks remain future decisions.

Regenerate in a separate local directory, with the intentionally fake IDs:

```bash
python docs/branch-local/dev/plugin-store/index-repository-bootstrap/scripts/build-repository-metadata.py \
  --catalog docs/branch-local/dev/plugin-store/index-repository-bootstrap/catalog.json \
  --source docs/branch-local/dev/plugin-store/index-repository-bootstrap/source.json \
  --output /tmp/phinix-repository-vectors \
  --repository-id 1402564805 --owner-id 64630568 --release-id 1 --asset-id 1
```

The script validates the envelope, binds raw catalog bytes, creates an immutable
published descriptor and replaces the local stable pointer last. It does not validate
all package records/payloads, fetch a live release, approve candidates or publish files.
The trusted publisher must do these checks before official publication. Re-running
identical inputs is allowed; different bytes for an existing snapshot are rejected.
