# Plugin store runtime regression harness

2026-10-05: 896 assertions passed. RepositoryAdapterTests covers real net472 payload metadata, GitHub/CF byte equivalence, verified installed ownership across switches, per-provider ETags, shared continuity/cache boundaries, identity/redirect/content failures, cancellation and bounded stalls. The adapter fixture builds against the repository Utils/framework references; no game assemblies or credentials are needed. [Exact commands and live evidence](../../docs/branch-local/dev/plugin-store/RepositoryAccessImplementation.md).

Run from the repository root:

```bash
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release
```

This net10 executable compiles the actual net472/C# 7.3 planning, metadata transport/cache, browser and payload validation sources from
`Extensions/PluginStore/Client`. It needs no RimWorld assemblies, external packages,
network, proxy, GitHub credentials or game installation. Failures return a nonzero
exit code; successful runs report their assertion count.

The fixtures are synthetic local inputs, not published catalogs or downloadable
releases. Both fixed fixtures and generated mutations exercise strict catalog and
manifest validation, version constraints, backtracking, cycles, local ownership,
runtime compatibility, Workshop boundaries and immutable plans. The browser
controller regressions cover terminal snapshots without UI callbacks, stale-result
isolation, cancellation, watchdog timeouts and bounded unfinished reads.

The harness also links the general environment snapshot contract and the store's
environment adapter. It verifies absolute paths, ownership boundaries, working
directory independence, disabled mods and unknown local package versions. Actual
game API reads are compiled in the host and still require in-game acceptance.

Payload regressions build a trusted local fixture assembly with a module initializer
sentinel, without loading it. They cover real ZIP/PE structure, actual DLL identity,
protected names, manifest/About mismatches, traversal, links, expansion quotas,
non-seekable chunked input and cancellation. The fixture targets net10 only to
test metadata; it is not a RimWorld-compatible release. No author project is built.

The harness does not validate Steam Overlay, Unity/Mono networking, third-party
code safety, complete IL/API compatibility, filesystem installation/recovery, save
migration or in-game behavior.
Compile-only net472 compatibility is checked separately with:

```bash
dotnet build Extensions/PluginStore/Client/PluginStore.Client.csproj --configuration Release -m:1
```

Repository regressions use an in-process HTTPS `HttpMessageHandler` behind the actual
production HttpClient transport. They cover canonical origins/paths, strict separate
stable/published schemas, raw digest/length/snapshot/source checks, redirects, response
types and byte limits, valid/missing-body 304, immutable observed snapshots/versions,
withdrawal, atomic complete cache replacement, endpoint/source isolation, corruption,
commit failure, restart/offline expiry, cancellation and late watchdog completion.
They do not contact a DNS/TLS/socket endpoint. Fixed draft metadata links the bootstrap
empty catalog; its release/asset IDs are synthetic and must never be used for origin fetches.

The local metadata generator has a separate executable regression check:

```bash
python Tests/PluginStoreRuntimeTests/RepositoryMetadataGeneratorTests.py
```

`Fixtures/RepositoryMonoSmoke/Program.cs` supports a separate Mono metadata/mock
transport/cache smoke. The exact mcs/mono command is in
[the batch validation record](../../docs/branch-local/dev/plugin-store/远端目录协议与浏览缓存实现.md).
It covers normal atomic replacement on this local filesystem and is not in-game networking
or a crash/durable installation transaction test.
