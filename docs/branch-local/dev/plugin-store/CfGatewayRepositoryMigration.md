# CF gateway repository and release migration

[中文](CF网关独立仓库与发布迁移方案.md) · 2026-10-06 · Assessment and unexecuted plan.

Priorities follow [store completion](StoreCompletionOrder.md): Gateway is store work, followed by RedPacket/Talent extraction and production finishing, all before revisiting main splitting or other projects.

2026-10-06: [repository, production takeover and main-source removal accepted](CfGatewayTakeoverAcceptance.md). Original design/gates below are retained as history, not pending implementation. Permanent PoC deletion remains separate.

This plan interprets “release with main Phinix” as source ownership, builds and release cadence. The store gateway should have its own repository and deployment while remaining an official Phinix service. Players do not deploy it; it does not need a deployment for every Mod release. This change only documents the plan: no repository creation, source migration, deployment or cloud resource deletion.

## 1. Recommendation and tradeoffs

Create a dedicated `Phinix-Plugin-Gateway` repository (proposed name), migrating `Extensions/PluginStore/RepositoryWorker/`. Keep the client store in Phinix and catalog admission/publication in `Phinix-Plugin-Index`.

| Option | Benefit and cost | Recommendation |
| --- | --- | --- |
| Main repository, independent deployment | Fewer repositories and easy coordinated edits; game and Node/cloud maintenance inputs remain mixed | Suitable during transition |
| Index repository | One fewer infrastructure repository; combines catalog-writing automation and cloud deployment credentials, permissions and incident handling | Not preferred |
| Dedicated gateway repository | Independent tests, deployments, rollback and credentials; one more repository and pinned cross-repository fixtures | Preferred |

There remains one authoritative GitHub source. CF adapts access and accelerates delivery; it does not create separate catalog or publication rules. Origin/logging/rate-limit fixes usually need no player Mod update. Catalog/manifest changes still require client, bot and gateway validation.

Moving source does not increase player requests, payload sizes or CF traffic. Reuse the existing service without another online deployment or enabling R2/DO; verification adds a few bounded probes. This is a structural assessment, not a pricing/free-tier guarantee.

## 2. Verified local boundaries

- `Phinix.sln` includes the .NET `PluginStore.Client` project, not a Worker project.
- Its csproj copies only the store DLL and language resources into `Output/phinix-rework`; it does not distribute Worker source, Node dependencies or cloud configuration.
- Main `docker.yml` publishes the server image and has no CF deployment step.
- Dockerfile nevertheless copies `Extensions/`; `.dockerignore` currently has no specific exclusion for the Worker or its `node_modules`. Those files can enter the build context/stage. The final image copies only .NET publication output, so this is unnecessary build input, not a Worker running inside the server image.
- Production uses `src/read-only.mjs` with Node/Wrangler and no RimWorld/.NET assembly dependency.
- Tests still depend on main: `tests/helpers.mjs` reads `Tests/PluginStoreRuntimeTests/Fixtures/chain.catalog.json`, and `tests/localized-display.test.mjs` reads its `localization-display/cases.json`. Moving the directory alone breaks tests.
- `git ls-files Extensions/PluginStore/RepositoryWorker` returns no files: the current store source is untracked. Cloning/extracting committed history alone would omit the actual current implementation.

The [production migration record](ProductionCfMigration.md) confirms `https://plugins.hunyuan2333.com` and the former staging domain are aliases of the existing `phinix-plugin-repository-staging` service. Production has no R2/DO/management RPC bindings. Its aggregate limiter applies per CF location, not as an account-wide billing cap.

## 3. Ownership

| Content | Owner | Treatment |
| --- | --- | --- |
| Production adapter, GitHub origin, protocol checks, streaming verification, audits and metadata cache | Gateway | Move source without changing behavior |
| Package/lockfile, Wrangler configuration, Node/Python tests | Gateway | Pin dependencies; remove main-relative reads |
| Network/audit probes and internal cache operations tools/runbooks | Gateway operations | Keep separate from game artifacts and public management routes |
| Historical R2/DO/PoC code and recovery tests | Gateway, marked experimental/historical | Preserve paths initially; production selects only its configuration |
| Store UI, client adapters, install/update and local validation | Main, later Client repository | Remain with the client |
| Managed extension runtime and generic host contracts | Existing framework projects | No gateway code dependency |
| Catalog, admission, trusted validation and automatic publication | Index | Preserve permissions, historical evidence and immutable assets |
| Example/Playtest source | Author repositories | Do not copy into production gateway catalogs |

Gateway versions describe service implementation, Mod versions describe client/framework and catalog snapshots describe published content. They need not match. No Gateway submodule in Client/Common/Server, new protocol-coordination repository or SDK publication pipeline is needed.

## 4. Migration stages and exit conditions

### M0: Freeze reviewable input

1. Export an allowlisted snapshot into an isolated directory with per-file SHA-256 and provenance, including untracked implementation, lockfile, required fixtures, license and bilingual runbooks.
2. Exclude dependencies, `.wrangler`, `.dev.vars`, `.env`, logs, account authentication, private backups, build output and game DLLs. Review account/domain identifiers as deployment metadata; they are not credentials.
3. Record current service version, entry point, both domains, secret names, bindings and limiter namespace without exporting secret values.
4. Run the narrow baseline at the original location. Previously passed tests are not post-migration evidence. Preserve the dirty workspace; no bulk commit, cleanup or forced history rewrite.

Exit: complete byte-level provenance and unchanged live deployment.

### M1: Standalone source and CI

1. Establish the candidate repository from reviewed files. Record untracked-file provenance rather than inventing Git history; extract committed history from a temporary clone only if needed.
2. Import the two external JSON fixtures into `tests/fixtures/` and update reads. Keep the current main protocol tests as their initial canonical owner, later Client. Gateway and Index consume versioned/hash-pinned copies with explicit synchronization records rather than independent edits.
3. A clean checkout installs locked dependencies and runs Node tests, Python tool tests, production dry-run and native workerd tests. It needs no sibling checkout, private local paths, game DLLs or cloud account.
4. Preserve historical cache/RPC coverage: `test:native` currently builds default, operations and production bundles. Removing experimental configurations without adjusting this graph loses coverage.
5. Make local-development intent clear and restrict production deployment to its explicit configuration. Old PoC/bridge configurations must not become accidental default cloud deployments.

Exit: independently runnable tests with no deployment credentials or live writes in this stage.

### M2: Own deployment of the existing service

1. Protect production deployment: trusted branch/fixed commit, validation before maintainer-triggered deployment. Author Issues, external PRs and Example releases must not deploy CF.
2. Separate CF deployment credentials from the service's read-only GitHub origin secret. Restrict permissions to deployment needs; do not copy Index bot credentials. Maintainer bootstrap establishes deployment identity without exposing values in chat/source/ordinary logs.
3. Target the same existing Worker name, preserving `GITHUB_TOKEN`, both domains during transition, limiter namespace and bindings. Source relocation needs no DNS change, new service name or token rotation.
4. Record source commit, bundle/configuration hashes, Worker version, BUILD_ID, checks and operator. Keep the previously verified version and original source during the first deployment, prepare an executable rollback runbook and perform bounded live verification.

Exit: new repository owns the service; the former entry stops releasing to prevent competing configurations. Source migration, service renaming and resource destruction are separate operations.

### M3: Protocol and live acceptance

1. Check stable/published/catalog/ZIP identity, digest chain, lengths and trusted static validation. Compare GitHub and CF using the same fixed snapshot; concurrent catalog publication must not look like adapter drift.
2. Cover ETag/304, unknown sources, invalid methods/query/Range, throttling, timeout and interrupted streams. Public management remains rejected.
3. Match request IDs, BUILD_ID and deployed version; redact credentials and signed asset URLs.
4. Run actual .NET/Mono download checks from an explicitly pinned client checkout. These tools remain client tests, not a whole-client prerequisite for Gateway local builds.
5. Human game check: CF refresh, matching GitHub listing after switching, retained installed Example/settings, and install/update/restart when applicable. Source relocation alone does not require all gameplay/save scenarios; protocol, client or binding changes expand acceptance.

Exit: separate automated and human evidence. On failure restore the previously verified production read-only version/configuration, not PoC.

### M4: Remove source from main and finish

1. Only after the new deployment owner and rollback are verified, remove migrated source from main. Leave a short link to repository, fixtures and runbooks; fix old relative-path documentation.
2. Review Docker context, solution/csproj, artifact checks and links. Mod builds need no Node/Wrangler or cloud deployment files; the server image publishes only its own graph.
3. Main/Index may run credential-free protocol integration checks but do not hold gateway deployment authority. Mod/catalog releases do not implicitly deploy CF.
4. Retain reversible commits for main-source removal and Gateway publication. Reverting source ownership does not roll back installation receipts, user settings or catalog assets.

Exit: one production deployment owner and no main build/test path dependency.

## 5. Separate work and risks

Permanent PoC retirement still requires concrete authorization. Public access is disabled, but Worker/R2/DO/secret copies remain. Automatic approval rejected the earlier deletion; the complete DO ledger backup is unavailable. Repository migration does not authorize deletion. Follow the separate [retirement record](ProductionCfMigration.md).

Keep the transitional domain until formal-origin acceptance; it is not a separate staging environment. A future real staging service needs isolated resources/credentials and another assessment. Do not combine this move with R2 activation, cache redesign, schemas, source identity, installation paths, plugin boundaries or wholesale README rewrites.

Deferred documentation cleanup remains assigned by the user to another model. AI author tools follow their existing plan; plugin authors need no CF deployment authority.

Main risks are protocol drift, missing fixtures, accidental route replacement through old configurations and competing deployment owners. Source relocation alone does not affect saves, player data, trade acknowledgements or item ownership. Irreversible cloud-state deletion belongs to retirement.

## 6. Relationship to the main repository split

The [Client/Common/Server plan](../Repo-Split-Plan.md) remains a three-repository source-consumption design. Gateway is distribution infrastructure, not the multiplayer Server or Common. Its independent toolchain allows migration first, without adding another mutually referenced consumer.

Implement M0/M1 first to provide reviewable standalone source and tests, then M2/M3 and finally M4. Confirm publication scope, repository name and protected deployment setup during implementation; none has been performed in this documentation change.

This assessment checked source/configuration and documentation links only. No compilation, automated runtime tests or game verification was rerun; stage checks above are future implementation requirements.
