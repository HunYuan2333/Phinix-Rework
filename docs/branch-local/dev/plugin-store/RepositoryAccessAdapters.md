# GitHub direct access and CF adapter plan

2026-10-05 delivered: profile identity, GitHub/CF adapters, shared validation/cache, default GitHub and manual source buttons. See [implementation/validation and development storage boundary](RepositoryAccessImplementation.md). Initial audit below is historical; catalog v3 remains pending.


[中文](GitHub直连与CF适配器计划.md). 2026-10-05, `dev`. This decision supersedes the earlier mandatory-CF access design. The user requests GitHub by default and an explicit quick switch to CF, with one repository protocol. This is an implementation plan; the direct client adapter and buttons are not delivered yet.

## Current audit

| Concern | Current implementation | Required change |
| --- | --- | --- |
| Client metadata | RepositoryEndpoint constructs CF `/v1/sources/...` routes. RepositoryTransport requires these routes and rejects redirects. | Provide a GitHub adapter producing the same verified stable/published/catalog byte chain. A GitHub URL cannot be substituted into the current endpoint field. |
| GitHub access | Worker GitHubOrigin checks fixed repository/owner IDs, publication branch, commit/tag, release/asset membership, lengths and digests. | Implement equivalent direct-client origin checks against the public GitHub API. Keep credentials out of candidate files and CDN requests. |
| ZIP transfer | Client calls CF fixed package route, requires octet-stream and no redirects; Worker handles GitHub release redirects. | GitHub adapter owns API headers and bounded approved asset-CDN redirects/content types. Feed verified bytes to the existing shared ZIP/PE/language validators. Do not globally relax CF transport rules. |
| Cache and ownership | CacheKey hashes Origin + SourceId; cache envelopes bind Origin. Planner compares installed RepositoryEndpointSha256 to that key. | Separate stable repository identity from access method. Changing transport must not create another plugin identity or reject an existing installation. |
| Settings/UI | Defaults to CF staging; free-text origin/source fields; no quick switch. | Default GitHub for a supported repository profile, with a manual CF switch and saved choice after both adapters pass verification. |
| Installation layout | Package-owned manifest/Assemblies/Resources directory under SaveData; bundled host supports immediate plugin folders under Common/Extensions. | Preserve per-plugin folders. Access choice changes neither layout nor native RimWorld Mod management. |

The two routes share published content today because CF reads GitHub assets, but only CF is a working client access path. Equal SHA-256 values do not establish the authenticity of a whole chain if the gateway itself is compromised: the current model trusts configured HTTPS endpoints. Publisher signatures are a separate, unimplemented decision; do not describe cache acceleration as independent cryptographic proof.

## Contract

1. Define a repository profile with stable source ID, approved index repository/owner IDs and publication branch, plus approved optional CF endpoint. Separate this from `accessMethod = github | cloudflare`. Author package repositories remain subject to each locked artifact's fixed IDs/policy. Labels and hostnames are not repository identity.
2. Define one client repository-access interface for stable, immutable published/catalog and locked package bytes. GitHub maps these operations to its API/Release facilities; CF maps them to existing fixed routes. Share schema readers, hash/size validation, continuity checks, dependency planning, payload validation and installation transactions. Equal protocol means equal publication objects and invariants, not identical HTTP URLs or headers.
3. Use the stable profile identity for package ownership and content caches. Retain access method/actual host as audit provenance. Provider-specific ETags and freshness evidence must not cross providers. A cached catalog remains browse-only until the selected provider completes the required fresh chain checks.
4. Switching providers cancels work before the installation commit decision, invalidates its plan/download state and refreshes through the chosen provider. During a committed installation, finish existing transaction semantics before switching. Never combine a pointer from one provider with an unrelated descriptor/payload from another. Retain last observed source continuity across switches.
5. Default to GitHub for new profiles; remember user selection. Offer “Use CF acceleration” / “Use GitHub direct” in the source panel, with current method, outcome and retry feedback. No silent fallback, no automatic plugin download/update. A different repository profile is a genuine source change and retains separate identity/trust rules.
6. Bound GitHub request counts, timeouts, cancellation and metadata cache use. Public direct access must work without asking every player for a token. Rate limiting or blocked access should expose an actionable manual switch. Server-only credentials remain on CF.

## Revised order and acceptance

1. Publish the already verified Playtest 1.3.0 language ZIP using the current catalog v2 test chain; test package discovery/translations/install/uninstall. This is not a v3 compatibility layer.
2. Implement profile identity, common access interface and GitHub direct adapter before changing defaults or adding switching buttons. Replace development ownership/cache contracts and their fixtures directly; no old endpoint-identity compatibility branch. Existing on-disk test installations must fail clearly if re-created state is required, without silent adoption/deletion.
3. Integrate catalog v3 localized display/publication once for both adapters, CF and trusted bot validators. Update development readers/writers/test sources together; no dual-v2/v3 compatibility requirement.
4. Deliver the source buttons with the store UI batch, then the bot's A2 → controlled A4 → A3 and remaining first-release work.

Automated acceptance: identical locked publication/ZIP bytes through both adapters; one ownership identity across switches; no shared ETags; mismatched repository IDs/tags/releases/assets/hash/length rejected; permitted redirects only; freshness/withdrawal changes rejected before commit; provider switching during refresh/download/cancellation cannot produce mixed state; offline caches cannot authorize installation; rate-limit/timeouts have bounded retries and useful audits. Run relevant tests on .NET 10 and net472/Mono.

Game acceptance after both adapters and buttons: default GitHub without proxy, manual CF switch and persistence across restart, browse/download/install through both, installed plugin recognized after switch, cancellation/blocked-network feedback, withdrawal/version change before commit, and localization/resource ownership. A successful CF check does not prove GitHub direct works for the game.
