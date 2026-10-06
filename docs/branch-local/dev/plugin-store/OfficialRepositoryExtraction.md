# Independent RedPacket and TalentTrade repositories

2026-10-06 latest user instruction: [RedPacket/TalentTrade are removed from the bundled main solution/package](OptionalPluginsUnbundlingAcceptance.md), with stale-output cleanup and build/fixture checks passed. This supersedes earlier bundled-retention status, not the pending game/save/security gates. Independent admission remains separate: Talent Issue #22 is unapproved; RedPacket has no published asset.

2026-10-06 current: both repositories are public after the user changed visibility. [Talent v1.0.1 safeguards and normal intake](TalentTradeInputGuardsAcceptance.md) passed local/independent regressions and online source/intake CI; Issue #22 is open and unapproved. RedPacket has no published asset: original-operator credential clarification and a reviewed generic host-module dependency policy remain pending. Bundled removal, durable purchase recovery and standalone game/save acceptance remain gated. Earlier status paragraphs below are historical.

2026-10-06 latest: the user selected **private** source repositories, superseding the public-upload approval request. Both reviewed snapshots are now privately pushed and source CI passed; see [private source and distribution record](LegacyPrivateSourceDistribution.md). The public proposal below records the earlier blocked step.

[中文](官方插件独立仓库准备与验收.md) · 2026-10-06 · Independently built candidates; public upload awaiting explicit scope approval.

The user accepted the incremental 800-silver repair and resumed extraction. The earlier repository/release deferral is superseded. The user also confirmed that another maintainer operates the legacy RedPacket relay and explicitly required retaining its existing connection parameters. No legacy transport source was changed in this batch.

## Prepared source and packages

| Owner | Source snapshot | Owned source | Managed package |
| --- | --- | --- | --- |
| RedPacket | `/tmp/phinix-legacy-redpacket-20261006` | 37 files; 23 C# sources | `/tmp/phinix-legacy-redpacket-1.0.0-candidate.zip`, 59796 bytes |
| TalentTrade | `/tmp/phinix-legacy-talenttrade-20261006` | 46 files; 32 C# sources | `/tmp/phinix-legacy-talenttrade-1.0.0-candidate.zip`, 60081 bytes |

Each source allowlist has a reviewable `source-manifest.json`. Talent's local source commit is `08ac779` (no main-tree commit). RedPacket's source is not committed pending specific approval to retain its old embedded client access parameter in public source. No credentials other than that explicitly scoped legacy parameter are accepted by the exporter. Neither service access parameters nor serialized business payloads are logged by this tool.

Both repositories build solely against compile-only DLL references from the existing Phinix Mod, legitimate RimWorld 1.6 references and, for Talent, Harmony 2.3.6. No main source tree/project reference is required. Each ZIP contains exactly its own Contracts DLL, Client DLL, manifest and two language JSON files. ZIP SHA-256:

- RedPacket: `a3dd4e1b23939f95d0d3cdbaa80a48db43607bb2edf47a2b495b08d59dede95c`.
- TalentTrade: `2e0fa21f4b31148797dcf4643f75ef193ad02bee2b34b3a14c8f19267ebe96a3`.

Local unpacked bundles and multilingual display projections are adjacent, named `*-1.0.0-candidate/` and `*-1.0.0-display.json`. Their resources follow the requested per-plugin folder layout. These are candidates, not catalog entries or evidence of managed in-game installation. Preserve assembly versions/types, module IDs, dependencies, settings/codec/storage identities and existing Scribe fields. Do not load a candidate alongside its same bundled DLLs.

## Validation performed

Both independent SDK builds passed with zero warnings/errors. The real packager checked exact CLR identity closure, declared modules, ZIP payload and scoped localization. The pack wrapper checked the complete ZIP file set, excluding shared/game/Unity/Harmony DLLs, logs, private configuration, other credentials and unrelated files. Source checks passed for both snapshots. Four exporter regressions passed: explicit legacy-key exception, exception confinement, portable/owned sources and altered snapshot rejection, and symlink rejection. Official Actions tags `actions/checkout@v5` and `actions/setup-python@v6` were verified through GitHub's API.

```sh
python3 -m unittest discover -s Extensions/PluginStore/Tools/OfficialPackageSplit -p test_export.py -v
python3 /tmp/phinix-legacy-redpacket-20261006/check-source.py
python3 /tmp/phinix-legacy-talenttrade-20261006/check-source.py

python3 /tmp/phinix-legacy-talenttrade-20261006/pack.py --phinix-package "$PWD/Output/phinix-rework" --game-references /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed --harmony-references "$PWD/.nuget/Lib.Harmony.2.3.6/lib/net472" --packager "$PWD/Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll" --output /tmp/phinix-legacy-talenttrade-1.0.0-candidate.zip --bundle-output /tmp/phinix-legacy-talenttrade-1.0.0-candidate --display-output /tmp/phinix-legacy-talenttrade-1.0.0-display.json
python3 /tmp/phinix-legacy-redpacket-20261006/pack.py --phinix-package "$PWD/Output/phinix-rework" --game-references /mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed --packager "$PWD/Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll" --output /tmp/phinix-legacy-redpacket-1.0.0-candidate.zip --bundle-output /tmp/phinix-legacy-redpacket-1.0.0-candidate --display-output /tmp/phinix-legacy-redpacket-1.0.0-display.json

dotnet exec --runtimeconfig Tests/RedPacketInventoryRuntimeTests/runtimeconfig.json /tmp/phinix-independent-redpacket-tests/RedPacketInventoryRuntimeTests.exe
mono /tmp/phinix-independent-redpacket-tests/RedPacketInventoryRuntimeTests.exe
dotnet exec --runtimeconfig Tests/TalentReturnRuntimeTests/runtimeconfig.json /tmp/phinix-independent-talent-tests/TalentReturnRuntimeTests.exe
mono /tmp/phinix-independent-talent-tests/TalentReturnRuntimeTests.exe
```

Build logs are `/tmp/phinix-independent-redpacket-build.log` and `/tmp/phinix-independent-talent-build.log`. Packaging commands reject an existing output; use new paths for reproduction. Isolated harness directories copy existing local test dependencies and replace the owned DLLs with these independently compiled assemblies. They are private local test directories, not source/export artifacts. RedPacket passed all 10 runtime scenarios and Talent all 31 integration assertions under both .NET and Mono. These tests do not claim game/Scribe/drop-pod/network validation. The unchanged main/server builds and PowerShell packaging check were not repeated; PowerShell is unavailable, so exact ZIP contents and source ownership were checked directly.

## Earlier public proposal and remaining gates

Automatic approval rejected creation/push of `HunYuan2333/Phinix-Legacy-TalentTrade`, because “continue extraction” did not explicitly approve public disclosure of these specific sources; the reviewer also noted unresolved licensing and missing-package save gates. No new remote repository was created. Do not bypass the rejection. Proposed source-only approval covers the above 83 files in `HunYuan2333/Phinix-Legacy-RedPacket` and `HunYuan2333/Phinix-Legacy-TalentTrade`, retaining upstream author rights and RedPacket's existing embedded client parameter, and running read-only source CI. It excludes release assets, catalog admission, human approval labels, service changes and bundled-DLL removal.

After source approval, publish these reviewed snapshots and confirm source CI. Then address RedPacket's pending/unknown sends and complete-history restart reconciliation without optimistic refunds; address Talent late GameComponent discovery and missing-plugin resave retention through generic host behavior. Genuine standalone game testing follows those repairs, using backed-up test saves. The already accepted bundled-language/basic-flow and 800-silver incremental feedback cannot substitute for those gates. Only then publish immutable packages, use the normal reviewed index Issue process, and remove bundled products. Source extraction does not complete store finishing.
