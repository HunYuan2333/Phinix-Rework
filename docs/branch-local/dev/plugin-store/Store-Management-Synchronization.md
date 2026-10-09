# Store and Extension Management Synchronization

Date: 2026-10-09. Implementation scope: host-owned state coordination and store presentation. In-game acceptance remains pending.

## State Ownership

- `ManagedExtensionRuntime` remains the sole owner of verified installation records, dependency gates, desired-state writes, installation transactions and restart recovery.
- `ClientExtensionControlService` publishes the shared inventory, module choices, activation facts, revision and command guard. Overlapping refreshes reuse one completed read; a later explicit refresh still reads disk. Newer settings or writes must not be overwritten by stale read completion.
- Both interfaces may retain their last inventory for inspection after failure, but mark it unconfirmed and disable package/module writes until refresh succeeds. A successful durable write followed by a failed read is reported as saved, not as a failed installation or uninstall.
- Current activation and next-start intent remain distinct. No hot unloading, automatic module enablement or clearing of individual module choices is introduced.

## Store Presentation

- `ManagedStoreEntry` is presentation-only. It wraps either an authorized remote record with a matching installation, or an installed-only package. It never adds local records to the downloaded catalog.
- Remote ownership matches package ID, source ID and repository identity. Installed-only selection uses the installation record key. Same-ID entries from different sources are not merged.
- Installed local packages and repository packages missing from the current catalog remain visible and manageable. Invalid records are visible with their existing blocking reasons, not granted unchecked deletion.
- Local development uses a coral terminal badge with a bundled PNG and editable SVG source. It is never an official-maintainer badge. Import is labeled `dev:` and still requires developer mode; management of an installed package does not.
- Canceling pending removal writes `Disabled`, matching extension management and retaining module choices.
- Inventory is read and published before remote metadata access. Remote/cache failure does not erase local facts. While a store operation is actively running, its existing cancellation and serialization rules still apply.

## Compatibility and Distribution

- This synchronization change does not alter public abstraction interfaces, their constructors, enum values, assembly identities, module IDs, settings keys, catalog schema, desired-state schema, receipt schemas or transaction/recovery formats.
- Existing schema-1 repository receipts remain readable and are not rewritten merely by listing or refreshing. Source identity remains authoritative; an endpoint change never adopts another source's installation.
- The earlier local-import implementation already introduced schema-2 local receipts. An older host that only understands schema 1 cannot manage those records and may conservatively block removal of other packages whose reverse dependencies are uncertain. This change does not claim downgrade compatibility or disguise local packages as remote installations.
- Deploy the matched host, Utils, abstractions and bundled store together. Optional control-service lookup alone does not guarantee that a newly compiled store can load against an older Utils/abstraction assembly. Do not cherry-pick only the store DLL into an older distribution.
- Before downgrading to a host without local-receipt support, remove local packages with the newer host and restart to complete removal. Do not downgrade with unfinished transactions. A dedicated older-version compatibility patch is still required for seamless downgrade with local packages retained.

## Verification

`StoreSynchronizationTests.cs` exercises actual installation records and the host control service: two-way package changes, retained module choices, offline listing, source separation, same-version ZIP revisions, stale actions, pending removal/cancel, failed post-commit reads, absent cache/network, concurrent refresh publication, legacy receipt preservation, withdrawn catalog visibility and damaged-record presentation.

Existing managed runtime/startup, store operation and extension-control regressions must pass. This is not a substitute for an old released binary compatibility matrix or RimWorld/Unity visual acceptance.

Verified locally: managed extension regression 3364 assertions (including the 2225-assertion store operation regression), plugin store regression 1000 assertions, and client package layout 2 tests. Host and bundled store Release builds succeeded; the local badge is embedded in the output DLL and both output language files contain the `dev:` label. Existing protobuf framework warnings and unavailable NuGet vulnerability-feed warnings remain unrelated to this change.

## In-Game Acceptance

1. Import the independent test ZIP, restart, and confirm it appears in both package lists with the coral local-development badge and package ID.
2. Disable/enable from each side and switch to the other. Current activation must remain unchanged; next-start intent and module choices must agree.
3. Turn developer mode off. The `dev:` import entry disappears, but installed local package management remains available.
4. Uninstall from the store, verify pending removal in extension management, cancel from either side, and verify the package remains disabled with its module choices intact. Repeat uninstall and restart to verify removal.
5. Test a failed index refresh/empty cache: installed packages remain visible and manageable once the read operation terminates or is canceled.
6. Test narrow windows, both languages and long package names: badges collapse safely, import actions remain reachable through toolbar overflow, and buttons/tooltips do not overlap.
