# 双版本自动发行流程 — 2026-10-15

## 当前结果

本地实现完成，未提交、推送、发布、上传引用或执行线上工作流。红包、人才贸易和 Example 保持 main push 自动发行模式；每次发行一个产品版本、一个 schema2 variants ZIP，含 1.6/net472 与 1.7/net10.0，API >=3.0.0 <4.0.0。旧客户端组合不在承诺范围。

## 实现位置与边界

- Host `tools/compatibility/build-managed-dual.py`：同一 Host 和单个插件源码的隔离双版构建，逐版真实 Host/游戏 DLL 引用绑定检查，再合包；插件 ZIP 只包含自有 DLL、语言资源和 manifest。`build-graph-isolated.py`、`build-net10-graph.py` 接受显式插件工程，不要求 CI checkout 另外两个插件仓库。
- 三仓库 `pack.py`、`ci/build.py`、`ci/fetch_references.py`、`ci/publish.py` 和 `.github/workflows/release.yml`：配置/API 能力/源码预检、固定私有引用来源与 SHA256、同版本双包合成、CRC/路径/文件长度与摘要/禁止游戏和 Host DLL 检查、发行前重验。保留原 patch 分配、同 source 重跑版本复用、不可覆盖 Release 资产和 Index 通知。
- 配置与 Host 能力、私有引用检查先于正式版本预留；缺项不会预留空版本。私有引用下载只读取已有资产，不会上传游戏 DLL。当前 `references17=null` 为明确上线缺项；不捏造来源、哈希或 Host SHA。
- Common `ManagedExtensionZip.cs` 是纯静态读取/检查；原两个安装转换方法移到 `ManagedExtensionZipInstallation.cs`，仍属同一 partial 类型，签名和安装行为不变。静态读取使用已有 `ManagedExtensionDigest`，不依赖文件系统 inventory/安装替换状态。Host 中立、插件业务/Thing/Pawn/账本/确认规则未改。

## Index 需要更新的最小范围

catalog 仍是 schema3，资产名称和下载路由不变；包内 manifest 是 schema2。`scripts/manifest_profiles.py` 提供共同身份、版本、语言路径和逐版兼容范围，供 bot/admission/catalog/source_updates 使用。保留整个 envelope，不只检查选中的 DLL。

旧单版/API2 到双版/API3 的第一次扩展需要一次新的范围审核。后续已审核双版范围内的 patch 可自动更新；新增/移除 runtime、改变 API/Host 兼容范围等仍会阻断自动范围扩展。源码更新 policy 不绕过审核。

`build-current-validator.py` 只编译明确提供的当前可信工作树，用于本地验证；不更新正式 provenance。`validator_snapshot.py refresh` 支持从真实最终提交冻结纯 ZIP reader，仍检查 Host 的 Common gitlink、来源 SHA 和源码字节。正式 snapshot 保持历史来源，不能用本地 dirty 源码替代正式 pin。

本轮还修复一个既存的冻结文件摘要不一致：`Validator/Production/ManagedExtensions/ManagedExtensionMetadata.cs` 从其 provenance 指定的 Common 提交 `67f243d9edced77dc3fe669f8a435b1f14b59199` 恢复精确字节，SHA256 `84ca86e3600d9db5f233a0b7d2ee96a8ef9728e541fb1942b45ca7a67fee6c78`。未更改 provenance/hash 来迁就内容。旧冻结校验器不会因此获得双版能力；部署仍须正确刷新全部快照。

## 验证状态

| 项目 | 状态与证据 |
| --- | --- |
| 源码检查 | 红包/人才 check-source、语言摘要与编译 allowlist 通过；三 workflow YAML 和版本预留前 gates 通过 |
| 编译通过 | 最新源码完整 1.6 Host+三个插件隔离构建通过；1.7 的 26 工程源码图通过；当前源码静态 Index Validator 构建 0 error/0 warning |
| 白盒通过 | 三个插件 CI 回归分别 22/22/20 项；Index 110 项通过、1 项原有跳过；托管元数据/安装/启动 3396 断言通过 |
| 真实包检查 | 红包 0.0.4、人才 0.0.1、Example 0.0.1 的双版 ZIP 已构建且逐版实际绑定检查通过；当前发布前校验再通过；新版 Index 校验真实 DLL/两语言投影通过；每个包 37 项安装/换游戏选择/重启/卸载白盒通过，不激活业务 |
| 正式 provenance | 历史冻结快照 14 文件摘要检查通过；尚未换为 API3 最终提交 |
| 游戏通过 | 本轮没有新增游戏测试，先前反馈仅证明当时执行路径 |
| 待测 | Actions 在线 restore/下载/Release/Index 联动，以及正式最终产品组合的两版游戏安装、业务与存档验收 |

本地验证用引用：保留的 1.6 GameDlls 与 1.7.4977 rev1248 缓存、匹配 Harmony。不是最新游戏构建的自动认证。验证包保留于工作区 sibling 的 local-compatibility-analysis/dual-ci-validation，不是正式发行版本，不覆盖之前提供的候选。

## 上线顺序与缺项

1. 核对两版最终产品组合的游戏验收；形成真实 Common 提交，再由 Host 固定对应 gitlink 并形成真实最终 Host 提交。
2. 以该最终 Host SHA 同步三个插件 `ci/config.json.hostCommit`；登记已存在且获准使用的私有 1.7 引用资产 repository/tag/asset/SHA256。当前仅核对到远端私有引用库有 rw16 v1/v2/v3，未找到 rw17 资产。本轮不上传或分发游戏 DLL。
3. 用实际最终 Host/Common SHA 正确刷新 Index trusted snapshot，并通过摘要、离线 payload 和 publication closure 检查；做第一次双版/API3 范围审核。
4. 授权推送后才触发 main 自动发行和商店联动；上线验证两版同一条目、版本比较、自动升级、本地安装与卸载，检查无 Host/游戏 DLL 入包。

尚未具备上述真实 pins/私有引用，不宣称“现在 push main 就能正式发布”。先修好和验证本地流程，保留阻断检查。

## 后续用户授权：仅发布基础源码和私有引用

用户已授权提交/推送 Common 与 Host，并把 1.7 编译引用上传私有仓库；明确要求暂不推送三个插件实际更新，故不触发插件 Release 或 Index 联动。此前“未提交/推送”的记录描述的是本地验证阶段。

Common 双版本支持提交：`9d0710a`，推送至其默认 `dev`。Host 固定该真实 gitlink。私有引用目标 `HunYuan2333/Phinix-Rework-Build-References`，tag `refs-rw17-v1`，asset `references-rw17-v1.zip`，SHA256 `41ddc7bc1dbf5bfe39059e549dd77310823621cbd46171214b43652ad10226bf`，239 个已核验 DLL，ZIP 内只有 GameDlls17/ DLL。反编译文本、日志、本地路径记录不上传。

三个插件的 Action 配置随后在本地绑定最终 Host SHA 和该引用摘要，仍不推送。Index 正式 provenance 的刷新/提交/部署不属于此次两仓库推送；首次 API3 双版范围审核仍待进行。
