# F6 final verification / F6 最终核查

2026-10-08. F6 implementation is being closed; registry publication is verified, and independent Index/Example main merges await the user decision. F6-M and F7 have not started.

## Current workspaces / 当前工作区

Client `/home/hunyuan2333/Phinix/Phinix-Rework`, Common `../Phinix-Rework-Common`, Server `../Phinix-Rework-Server`; all dev. Original dirty unsplit checkout is intact at `../Phinix-Rework-unsplit-backup-20261008`. Remote pre-split branch `codex/pre-split-20261008` remains available. Runtime/persistence identifiers are unchanged.

## Implementation / 实施

- Index fixed validator schema 2 identifies Client `77cdbbaa9614485af53c414159b02342b70bdfb5` and Common `67f243d9edced77dc3fe669f8a435b1f14b59199`. Per-file origins, canonical remotes, HEAD, exact Client Common gitlink and committed source bytes are verified; only explicit trusted split roots can refresh. Source/path/tamper/partial-input checks remain. Three stale source files were refreshed. Historical catalogs/releases/receipts are untouched.
- Independent Example csproj and pack script now use the Client pinned Common checkout for Utils. This fixes real pre-split paths; existing package/assembly versions and public pack arguments remain. Generated ZIP stays local, without replacing any immutable release.
- Three existing ManagedPackageTool sources had been omitted during extraction. Restore them from the pre-split tracked files and update only the Common project reference. It builds independently; no new author tooling feature was added.
- Re-exported Index host profile from actual split client distribution: 8 assemblies and the unchanged 5-module graph. New hashes trace current artifacts. This is maintainer configuration, not candidate authority or a game-availability bypass.
- Old Rework Docker workflow `281519567` was disabled manually. New Server has both expected secrets and variable `SERVER_IMAGE_PUBLISH_ENABLED=true`. Run `37777113964` logs in and builds, but registry push to `hunyuan23333/phinix-rework:dev` fails `insufficient_scope`. The user's confirmed account is `hunyuan2333`. Public registry lookup confirms old `hunyuan23333/phinix-rework` exists, while `hunyuan2333/phinix-rework` returns 404. The user then explicitly created/selected `hunyuan2333/phinix-rework`; Server commit `42516ec` updates the destination. Run `37778078886` succeeded in actual publication. `docker manifest inspect hunyuan2333/phinix-rework:dev` verifies a linux/amd64 manifest `sha256:944e910e371525efd4bf32380bcbe89e40e80c5e08834fd9db93ab9434967f5c`. No running server was deployed or restarted. Old image addresses require an explicit deployment update; the dev tag is published, not latest.

## Executed validation / 已运行验证

```sh
# Index
python3 scripts/validator_snapshot.py check --client-root /home/hunyuan2333/Phinix/Phinix-Rework --common-root /home/hunyuan2333/Phinix/Phinix-Rework-Common
dotnet build Validator/Validator.csproj --configuration Release -p:NuGetAudit=false -p:RestoreSources=/tmp/phinix-f5-local-20261008-gslqn8eh/empty-feed -m:1
python3 -m unittest discover -s tests -v
dotnet Validator/bin/Release/net10.0/Validator.dll publication test.f6 /tmp/phinix-f6-example-catalog.json host-module-profiles.json
# Example
python3 pack.py --phinix-root /home/hunyuan2333/Phinix/Phinix-Rework --game-references /tmp/phinix-f6-game-references --output /tmp/phinix-f6-example.zip
# Client; existing tool builds as part of pack.py
dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll --export-host-profile Output/phinix-rework/Common/Extensions --phinix-range '>=0.9.7 <1.0.0' --output /tmp/phinix-f6-host-profile.json
```

- 14 frozen sources match specified trusted committed inputs.
- 107 Index Python tests completed, 1 legacy standalone-source availability check skipped, all other tests pass. Logs `/tmp/phinix-f6-index-tests-final.log`.
- Trusted validator builds with zero errors/warnings.
- Example and restored tool compile successfully; known protobuf warnings remain. First example attempt used nonexistent `GameDlls/1.6` and failed missing Verse/Unity references; corrected actual flat GameDlls input plus Mono `mscorlib.dll` was provisioned into the temporary reference directory. This is compile-only local provisioning, not a distributed DLL or source workaround.
- Example ZIP `/tmp/phinix-f6-example.zip` SHA-256 `4876166a07d439f949e50065a9d3f71b4c07bf6225a733a3e7d82307c4559c62`; refreshed trusted validator accepted manifest/ZIP/CLR references/localization, no host/game DLLs, and publication dependency closure against the exported profile. Temporary candidate uses inherited fixture artifact IDs, explicitly not a real publishable release/submission.
- Host export reads metadata only, never executes candidate code. 8 assembly / 5 module graph comparison passed.

No in-game acceptance is invented. Incremental game check: launch main package, load existing save, verify chat after another save load, open Store/download a managed plugin, restart and enable/disable the existing example. No save schema, wire format, ACK or item-ownership semantics changed. Registry publishing does not deploy or restart an existing server.

本批源码/静态回归与真实镜像发布已通过。Client 工具恢复 77cdbba、Server 发布目标修正 42516ec、Index 固定来源 df31c54、Example 路径修正 af0ed15 均已提交推送。Index/Example 仍在原有 codex/docs-user-guide 分支，包含此前独立文档提交；尚未擅自合并到 main，已请求用户选择 PR 审阅或直接合并。因此不能把这两仓默认分支交付伪称完成。保留既有游戏验收记录，不补造本轮游戏实测；F6-M/F7未开始。完整发布证据在 /tmp/phinix-f6-final-publication-evidence.json。
