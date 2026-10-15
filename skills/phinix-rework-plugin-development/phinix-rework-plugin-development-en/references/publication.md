# Local distribution, Releases and Index

Follow the user's chosen route. Project creation or local packaging does not authorize uploads, pushes, releases, Index writes or deployment. Do not ask again when the session already provides the relevant authorization.

## Distribution choices

- Local/friend distribution: deliver the exact ZIP, digest, target Host/API/game versions and tested/pending scope. Local DLL installation requires developer mode and restart; theme import requires neither and supports hot application.
- Manual Release: locally build/verify, then publish a public non-draft/non-prerelease version and matching source Tag. Author repositories need no Actions workflow.
- Automated Release: useful with existing CI or frequent iteration; see [actions.md](actions.md). Whether main pushes publish depends on that repository's actual workflow.

## DLL plugins

Use Index's current submission/review flow to establish a candidate and source policy. A label alone does not complete approval. Policies pin package ID, repository/owner, asset naming and update scope. `plugin-source-updates.yml` currently schedules `17 * * * *`; this is an hourly schedule, not guaranteed punctual execution or admission within one hour.

Both manually and automatically produced Releases can be discovered under an approved policy. Public status, Tag/commit, asset digests, compatibility, dependencies and policy scope remain checked. `UpdateScopeChanged` can block updates outside approval. A green scan may mean changed=false or contain individual-source errors. Verify admission records, controlled publication, stable/published/catalog and client installation separately.

Package schema1/schema2 and catalog schema3 are different layers. Maintained plugins currently ship dual-runtime packages; do not falsely state that other candidate forms supported by the parser are impossible. Never overwrite public versions/assets or use --clobber to hide digest conflicts.

## Theme packages

Themes use the separate `phinix.themes` schema4 feed; the package remains schema1. Current admission is maintainer review of `themes/packages/*.json` followed by `theme-publish.yml`, not DLL automatic source scanning. Verify any public author's source, fixed Release/asset, license, manifest/XML and hashes. There is no official-organization restriction.

Index retains author provenance and hosts `theme-<ZIP SHA256>.zip` copies in the same snapshot Release. CF serves/caches approved copies; GitHub direct uses original assets. Both must return identical bytes. Authors do not provide Index write tokens, CF credentials or per-organization download tokens. Gateway cache/deployment is separate operations work, not part of ordinary theme authoring.

## Online acceptance

Check Release assets, real Index records/current snapshot, both download routes and client list clicking/details/confirmation/installation. Installation is not theme application. Successful CLI download does not establish usable UI installation. For exhausted anonymous GitHub quota, diagnose response headers and wait for reset or let the user select CF. Do not repeatedly refresh or embed server credentials in clients.

Source: Index `themes/README.md`, `scripts/theme_publication.py` / `theme_release.py`, `scripts/source_updates.py` and related workflows; Host `ThemeStoreController.cs` / `ManagedStorePresentation.cs`. Evidence locations: [basis.md](basis.md).
