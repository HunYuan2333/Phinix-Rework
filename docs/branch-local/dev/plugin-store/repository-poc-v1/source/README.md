# Phinix Plugin Store controlled PoC

This public repository contains an **inert distribution test**, not an approved production extension or the official plugin index. `phinix.poc` is a separate source. The official index and game mod remain unchanged.

`PocMarker.cs` declares one net472 assembly, `Phinix.Store.Poc` version `1.0.0.0`. It has no initializer, game hook, networking, storage, installation scripts or Phinix module. The ZIP contains About metadata, a manifest and that assembly. It is only for a controlled gateway/checksum test; it has not passed in-game loading or installation acceptance.

Build on Mono using `mcs -sdk:4.7.2 -target:library -out:/tmp/Phinix.Store.Poc.dll PocMarker.cs`. Binary output belongs in a GitHub Release, not Git source. A catalog release locks repository/owner/commit/tag/release/asset IDs and SHA-256; the published descriptor is committed before the stable pointer. No production approval or automatic publisher workflow is supplied here.

The original source and metadata in this test repository are licensed under MIT. No RimWorld, Unity, host framework or third-party assemblies are distributed.
