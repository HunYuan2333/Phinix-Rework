# Official plugin source export

The exporter follows each project's explicit source list, replaces main-checkout project references with compile-only references to a Phinix Mod package, and copies only the owner's language files. It never commits the main worktree, edits legacy transport, creates a repository or publishes a release. Its templates become independently buildable source repositories.

```sh
python3 -m unittest discover -s Extensions/PluginStore/Tools/OfficialPackageSplit -p test_export.py -v
python3 Extensions/PluginStore/Tools/OfficialPackageSplit/export.py talent --output /tmp/talent-source
python3 Extensions/PluginStore/Tools/OfficialPackageSplit/export.py redpacket --output /tmp/redpacket-source --preserve-legacy-client-key
python3 /tmp/talent-source/check-source.py
python3 /tmp/redpacket-source/check-source.py
```

The RedPacket switch is an explicit exception for the existing `RelayApiKey` field in `Client/RedPacketRelay.cs`, required by the user's 2026-10-06 instruction to preserve interoperability with another maintainer's relay. The latest user decision explicitly makes both reviewed repositories public and uses normal admission, superseding the temporary private destination. This exception never permits other credentials. Do not log its value, change the endpoint/room/key, or replace the existing service as part of extraction.

Build/package commands and reference requirements are in each candidate README. The trusted public packager validates exact CLR references and module/language metadata without executing payload DLLs. The wrapper verifies the final ZIP's exact owned file set. Existing output paths are rejected to keep candidates immutable. Source checks preserve a fixed export manifest; if later maintenance changes that snapshot, refresh the recorded file hashes deliberately as part of the reviewed change.

No upstream license was found locally or through GitHub's license endpoint. Extraction preserves author rights and does not invent MIT licensing. Independent source CI and managed catalog admission are separate stages. Source visibility is explicit (`--source-visibility public|private`); the current user-selected route is public. The maintainer adds approval labels; automation must not approve on the user's behalf. Retain standalone ownership/restart/save gates before removing bundled products. See [current evidence and scope](../../../../docs/branch-local/dev/plugin-store/OfficialRepositoryExtraction.md).
