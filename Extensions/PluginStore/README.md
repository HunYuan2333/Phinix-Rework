# Phinix plugin store

2026-10-05: catalog v3 localized names/summaries/changelog, strict ZIP display projection, GitHub/CF access and staging publication are delivered. [Implementation and game handoff](../../docs/branch-local/dev/plugin-store/CatalogV3Implementation.md). Playtest 1.3.0 is unchanged; A2 admission and formal interaction remain next. This supersedes the historical first-release status below.

[中文](README.zh-CN.md). 2026-10-05 local first-release candidate: bundled store, catalog v2 online browsing, summaries/tags, dependency plans, DLL download validation, host installation, state/removal and recovery are connected. Playtest 1.2.0 is published and staging is deployed; game acceptance remains pending. See [delivery/acceptance](../../docs/branch-local/dev/plugin-store/StoreFirstRelease.md).

Build host and store together from the repository root:

```sh
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

Use `Output/phinix-rework`: `Common/Extensions/17-PluginStore.Client.dll` and bilingual resources are bundled. Client abstractions are 1.6.0. The preserved `phinix.plugin-store` identity/settings use ordinary discovery/Register/Activate/Shutdown with no host reference to store implementation. Disable/remove the old standalone preview first; preview output requires explicit `-p:BuildPluginStorePreview=true`.

Full mods use Workshop links and Steam/RimWorld management. DLL packages go under SaveData and load after restart, without a Mod shell. Package/module switches are separate; removal retains settings/business data/saves. Installed-package loading is independent of store activation, and host settings retain manager/re-enable recovery.

Offline cache authorizes browsing only. Pre/post-download online checks bypass Worker metadata cache; locked routes, sizes and hashes remain mandatory. V2 does not reinterpret old Mod ZIPs; legacy records retain explicit verification/removal tools. Descriptions are cached catalog text; no remote image/README traffic.

Staging is `https://plugins-staging.hunyuan2333.com`, source `phinix.managed`. The new [Playtest 1.2.0](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.2.0) contains only its DLL and manifest. User-authorized publication and staging deployment are complete, with no-proxy production download checks on .NET 10/Mono. The old `phinix.poc` source is preserved. No main-repository push occurred; RedPacket/TalentTrade splitting remains later work.
