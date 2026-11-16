# Building Dual RimWorld 1.6 / 1.7 Host

> **中文版**：[双版本构建指南.md](./双版本构建指南.md)

The dual-runtime source targets RimWorld 1.6 (`net472`) and 1.7 (`net10.0`) under API 3. A single build produces one mod directory/ZIP selected by `LoadFolders` per game version. The entry points below generate local release candidates without installing, pushing, publishing, or replacing in-game acceptance.

Requirements: .NET 10 SDK, Python 3, restored pinned source dependencies and NuGet packages, plus matching private game/Harmony compile references (never bundled into output ZIPs). Do not modify vendored protobuf pins.

Run from the repository root:

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework

python3 tools/compatibility/build-dual-host.py \
  --game16 ./GameDlls \
  --game17 ../local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony17 ../local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --output /tmp/phinix-dual-build-01
```

`--game17` points to a verified 1.7 assemblies directory (containing `Assembly-CSharp.dll` and `UnityStubs.dll`), and `--harmony17` points to the matching `0Harmony.dll`. Re-verify references when game builds update. The `--output` directory must be new and outside both the workspace and reference directories.

Primary outputs:

```text
/tmp/phinix-dual-build-01/phinix-rework-dual-release-candidate.zip
/tmp/phinix-dual-build-01/result.json
/tmp/phinix-dual-build-01/commands.json
/tmp/phinix-dual-build-01/staged/assembly-audit.json
/tmp/phinix-dual-build-01/staged/reference-closure.json
```

The ZIP contains `phinix-rework/` with shared static assets and separate `1.6`/`1.7` DLL directories (no cross-TFM Common mixing, no bundled standalone plugin duplicates, and no game/Harmony DLLs).

Build order: isolated full 1.6 source graph + standalone plugin compile checks; isolated full 1.7 source graph; fresh metadata validator build; TFM/identity/prohibited-assembly audit; directory merge and ZIP CRC verification. Workspace `bin`/`obj`/`Output` remain untouched.

The original command `dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1` still builds 1.6 only; see `branch-local/dev/Release-1.6-Command-Verification-2026-10-14.md`.

## Standalone Plugins and `main` Automated Releases

Standalone plugin workflows (RedPacket, TalentTrade, Example) support API 3 dual-runtime builds: pushing to `main` validates config, source, and pinned Host capabilities, downloads and verifies private references for both runtimes, assigns a patch version, publishes a single immutable dual-payload ZIP, and notifies Index. Game DLLs must never enter packages or releases.

Run inside a standalone plugin repository:

```sh
python3 pack.py \
  --phinix-root /home/hunyuan2333/Phinix/Phinix-Rework \
  --game-references /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls \
  --game17-references /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --version <local-candidate-semver> \
  --output /tmp/<plugin>-dual-candidate.zip
```

The output path must not exist yet. `pack.py` builds both runtimes in isolation, verifies Host/game bindings, and merges them into a single variants ZIP without calling GitHub or publishing.

Index catalog schema 3 and asset naming remain unchanged, while static validators and projections support schema 2 variants packages. See [Dual-Runtime Automatic Release](branch-local/dev/Dual-Runtime-Automatic-Release-2026-10-15.md).
