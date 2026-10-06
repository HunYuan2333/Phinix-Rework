# Private legacy source and package distribution

2026-10-06 superseded: the user reverted to normal public-source admission. Talent is public; RedPacket remains private because automatic approval rejected embedded credential exposure, pending original-maintainer clarification. No private-source admission exception or binary-only public origin was implemented. See [current audit/status](TalentTradeSecurityAudit.md). The remainder records the earlier private proposal.

[中文](旧插件私有源码与发行方案.md) · 2026-10-06 · Private source repositories established; binary distribution pending.

The user chose private RedPacket and TalentTrade repositories because the legacy service belongs to another maintainer. This replaces the earlier request for public source approval. Preserve existing connection parameters and upstream author rights; no MIT license is invented. This is a source-visibility change, not a legacy protocol/service change.

## Completed source transfer

| Repository | Visibility checked through GitHub | Commit | Source CI |
| --- | --- | --- | --- |
| [Phinix-Legacy-RedPacket](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket) | private | `e29262feb2c2b76fbd6f0c1489b0a3f062a5cfed` | [37433286730 passed](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket/actions/runs/37433286730) |
| [Phinix-Legacy-TalentTrade](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade) | private | `fded01f092c5e76cb6929570d1c816fd5060de2a` | [37433233261 passed](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/actions/runs/37433233261) |

The reviewed 37/46-file snapshots were transferred to these private destinations. Read-only CI validates source ownership, compile-only references, fixed-file hashes and scoped language declarations. No binary release, index entry, service change or bundled product removal occurred. No personal/GitHub/CF credentials, game references, generated binaries, logs or runtime state were uploaded. RedPacket retains its single pre-existing legacy client interoperability parameter under the user's preserve-transport and private-destination instructions; the exception does not cover other credentials or a public source upload.

`publication.json` now declares `sourceVisibility: private`; source-manifest hashes were refreshed for the visibility/README changes. Production C# source remains byte-identical to the main checkout, including original module/assembly/type/storage/save identities. Candidate ZIP bytes and existing build/runtime evidence in [extraction acceptance](OfficialRepositoryExtraction.md) are unchanged. The main dirty worktree was not committed or pushed.

Checks executed:

```sh
python3 /tmp/phinix-legacy-talenttrade-20261006/check-source.py
python3 /tmp/phinix-legacy-redpacket-20261006/check-source.py
python3 -m unittest discover -s Extensions/PluginStore/Tools/OfficialPackageSplit -p test_export.py -v
gh repo view HunYuan2333/Phinix-Legacy-RedPacket --json nameWithOwner,isPrivate,url
gh repo view HunYuan2333/Phinix-Legacy-TalentTrade --json nameWithOwner,isPrivate,url
gh run list --repo HunYuan2333/Phinix-Legacy-RedPacket --limit 2 --json databaseId,status,conclusion,headSha,url
gh run list --repo HunYuan2333/Phinix-Legacy-TalentTrade --limit 2 --json databaseId,status,conclusion,headSha,url
```

Both source checks and all four exporter tests passed. GitHub reports both repositories private and both fixed-commit runs successful. Business code did not change, so compilation/game checks were not repeated for this visibility-only batch. No game acceptance is inferred from CI.

## Current distribution constraints

`GitHubRepositoryAccess.OriginOperation.Repository` requires a public repository and anonymous access. `RepositoryAutomation/scripts/bot.py::GitHub.verify_origin` also requires a public artifact repository, exact repository/owner/release/tag/commit/asset identity and at least one actual C# source file at the pinned commit. Source monitoring enforces public origins too. Private source repositories cannot simply replace current release origins; a binary-only public repository also needs an explicit admission-policy change. Do not add a dummy `.cs` to bypass the source check or give game clients repository credentials.

GitHub's [release API documentation](https://docs.github.com/en/rest/releases/releases) and [token permission reference](https://docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens) confirm the access boundary. Making the cache's origin token more privileged does not make anonymous GitHub direct access equivalent, and does not by itself satisfy current repository/source checks.

Private source limits new source exposure. It does not conceal hardcoded service addresses or client parameters in a publicly distributed DLL. Existing copies/history in the public main repository do not disappear when a new repository is private; no visibility/history rewrite of the main repository is included.

## Planned release route and order

1. Keep both source repositories private and retain existing legacy service behavior. Complete RedPacket pending-send/history reconciliation and Talent late-loading/missing-package save retention first.
2. Design a separate **public binary distribution origin** for these two legacy packages, containing only release metadata, changelog, explicit provenance and immutable ZIP assets. Creation and public asset upload are separate publication actions, not authorized by private source transfer.
3. Add an explicit reviewed private-source/legacy admission policy, limited to these maintainer-approved packages. Preserve the current public-source path for ordinary publishers. Distinguish the public artifact tag/commit identity from the private build source commit and record manifest/payload/source-snapshot hashes. Define who verifies the private source and build provenance; do not describe static DLL validation as proof of source-to-binary equivalence.
4. Keep the index, GitHub direct reader and CF adapter on the same catalog/asset identity protocol. Game clients read the public distribution origin without tokens; CF caches those same approved public bytes. Private source access is confined to maintainers/controlled build work, separate from downloader credentials.
5. Validate admission, provenance/tag/hash mismatch rejection, source update monitoring and matching GitHub/CF downloads before normal Issue approval and catalog publication. Test genuine managed installation/restart/uninstall/reinstall and save/ownership recovery before removing bundled products.

If the user later chooses private binary assets too, anonymous GitHub direct downloads will no longer work. That requires an explicitly different server-mediated access design and user-visible source behavior; it is not the selected default and must not be silently introduced by CF. This document records the intended route; the admission/provenance changes and public binary origin are not yet implemented. No other project migrations are advanced ahead of these store gates.
