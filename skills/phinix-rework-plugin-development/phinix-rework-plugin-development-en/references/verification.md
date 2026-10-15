# Verification, diagnosis and recovery

## Separate evidence

| Status | What it establishes | What it does not replace |
| --- | --- | --- |
| Source checked | Declarations, call paths, configuration and boundaries match inspected source | Compilation or binding |
| Build passed | Specified inputs/runtime produce DLLs with exit 0 | Installation, activation or business compatibility |
| Whitebox passed | Explicit assertions, CLI checks and boundary cases pass | Real game rendering and multi-Mod environments |
| Game passed | A specific game build executed named scenarios | Untested versions or paths |

Pending marks an evidence gap, not a higher verification level. Record actual commands, inputs and results. Execute console test programs as programs; an empty successful dotnet test does not establish assertion counts. Clean logs cover only executed paths. Date historical records and retain their scope instead of presenting them as reruns.

## Find the earliest meaningful error

Record exact game build, loaded Host/API/plugin versions, source and digest. Start with the earliest LoaderException or rejection before analyzing cascading errors. Request only needed diagnostics and do not commit raw player logs.

| Error/symptom | Diagnosis |
| --- | --- |
| CandidateHostIncompatible | CompatibilityVersion, game variant and manifest ranges; CLR AssemblyVersion is not product version |
| PackageNotFound | Package ID, version, runtime routing, theme/DLL feed and published assets; not automatically rate limiting |
| RepositoryRateLimited | HTTP status, body, rate-limit headers, authentication and URL. Some current client 403 mappings are broad; the name alone does not prove exhausted quota |
| UpdateScopeChanged | Approved Index policy versus new package scope; retain old publication and review policy rather than bypass approval |
| UnknownField | Actual client/manifest schema, rejected field and selected variant; do not infer a platform-specific cause from the name |
| Installed version newer than loaded version | Current process still uses an old binding; check restart and startup summary |

## State and item recovery

Use stable operation IDs, explicit states and authoritative acknowledgements. Callback/task completion is not business confirmation. Closing views does not cancel transactions or return items. Preserve sent operations with unknown results; timeout alone does not justify another payment or return.

Keep item/Pawn conversion, reservation and delivery atomic as a batch. Missing Defs, codecs, dependencies or a valid map produce rejection rather than silent item loss. Save changes, disconnects, duplicate and out-of-order acknowledgements follow the actual business owner's rules. Host stays neutral and does not own ledgers, offers or Pawn authority.

Start with small deterministic rules, package and binding checks, then relevant game flows. Do not simulate the entire game for wording changes or skip delivery/recovery tests merely because compilation passed.
