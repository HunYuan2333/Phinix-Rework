# RedPacket publication status

[中文](红包发行当前状态.md) · 2026-10-06

The user now defers RedPacket publication and both business bug tracks, prioritizing Store finishing. Original-author consent is not an unmet condition. PR #24 merged at `048c8637a64efefe64ff4d95264dd9e7300dc302`; post-merge [self-check 37468314971](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37468314971) passed. Its separately-reviewable/not-deployed wording below is historical. Talent Issue #22 was approved, published and closed. Follow the [Store plan](StoreFinishingAndDocumentationHandoff.md) and [coupling audit](StoreCouplingAudit.md); no rejected RedPacket upload is retried.

The user explicitly states that the original author consented. Do not repeatedly request the same permission or describe it as missing consent. This does not establish the legacy service's technical authentication/rate-limit guarantees.

An attempted upload of the existing RedPacket ZIP was rejected by automatic approval for publishing its embedded relay API key. A read-only comparison had verified that the same field already exists in the user-made-public source; automatic review still rejected further distribution as non-overridable credential exposure. No release or intake Issue was created, and no alternate upload or manual bypass instructions were provided. Main RedPacket connection/business code remains unchanged. Moving a client-required shared key to local settings does not fix server authentication or rate limiting and must not be sold as such a fix.

Unaffected work: [index PR #24](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/24) replaces the missing-host-provider blocker with maintainer-owned, version-scoped module profiles discovered from real main-package PE module declarations. No named business exceptions are coded in the validator. Eight main DLL hashes/five modules were discovered; standalone validator build and 78 regression methods (including 11 configurable-host scenarios) passed. [Read-only CI 37462932656](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37462932656) succeeded. PR remains separately reviewable; this is not plugin approval or completed deployment.

A post-extraction Design-Philosophy coupling audit of Store/runtime/index/Gateway is now required in both completion plans. Main dirty-tree changes are uncommitted; no secret/config rewrite, main business endpoint change, legacy-service probing or game acceptance is claimed.
