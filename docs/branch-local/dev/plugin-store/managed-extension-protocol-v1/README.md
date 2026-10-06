# Managed extension format examples / 托管扩展格式示例

These examples document M1's static managed format, not a release or installable payload. Repeated-digit artifact/catalog/repository-identity/DLL hashes and IDs are placeholders. The manifest's actual bytes are consistently bound by the receipt and desired-state examples. No example DLL exists. They are not the shop's existing v1 catalog/manifest and must never be submitted to its old installer.

这些文件说明 M1 静态托管格式，不是已发布或可安装包。连续重复数字的载荷/目录/仓库身份/DLL 摘要和 ID 为示例值；凭据和期望状态绑定了示例清单的实际字节。示例 DLL 不存在。它们不是旧商店 v1 catalog/manifest，不能交给旧安装器。

- `manifest.example.json`: generic loader declarations; fixed `management: phinix-dll`, no `rimWorldPackageId`.
- `receipt.example.json`: source/repository-identity/catalog/artifact provenance, transaction and exact owned bytes. Local provenance fields are not signatures or new repository authority.
- `desired-state.example.json`: `enabled`, `disabled` or `pending-removal`, bound to source/package/manifest and an operation ID. Missing/corrupt state is unknown, never enabled.

Source/package path key / 来源与包的目录键：`pkg-<SHA256(UTF8(sourceId + "\n" + packageId))>`.

Resources are package-owned files; their declaration does not trigger RimWorld Def, Patch, translation or texture content registration. `ContentVerified` inventory validates owned bytes only; CLR references, module entry declarations, compatibility and runtime activation require the later M2 loader.

Resources 为包私有文件，其声明不触发 RimWorld 内容发现。库存中的 `ContentVerified` 仅验证归属文件字节；程序集引用、模块入口一致性、兼容性和实际激活仍需 M2 加载器验证。

[Implementation batches / 分步实现](../托管DLL分步实现.md) · [English](../ManagedDllImplementation.md)

2026-10-05 development contract: receipt field repositoryIdentitySha256 binds the repository profile, independent of GitHub/CF transport. The old endpoint field is no longer accepted; remove old test installations using the previous host before deploying this contract. Settings/save data are retained.

2026-10-05 开发期契约：receipt 的 repositoryIdentitySha256 绑定仓库 profile，与 GitHub/CF 通道无关。旧入口字段不再接受；部署新契约前在旧宿主正常卸载测试包，保留设置/存档。
