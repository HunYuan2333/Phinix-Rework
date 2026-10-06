# 托管商店目录 v3

[English](README.md)。2026-10-05。示例 JSON 的摘要/长度准确绑定实际文件；示例 DLL/ZIP 身份是占位数据，没有对应可下载插件。[实现与真实验证](../目录v3实现与验证.md)。

外层 stable/published 保持 schemaVersion=1，要求 catalogSchemaVersion=3；固定资源路由不变。托管客户端只读 v3，不映射 v2、不迁移旧浏览缓存。独立的旧 Mod 预览 v1 不会被解释为托管包。根字段只有 schemaVersion、sourceId、snapshotId、packages；保持 2 MiB、1024 条目、每包 32 个版本上限。未知/重复字段、来源/快照不符、非法 JSON 和身份冲突均拒绝。

条目共用 id、author、license、tags、state、channel、management。标签最多八个不重复的受限小写标识；状态为 active/withdrawn/unmaintained。没有图片/README 地址，也不额外下载语言数据。

- GitHub 托管条目：github-release / phinix-dll，包含 manifest、artifact、localization。禁止顶层 name/summary 及工坊字段。规范名称留在 [manifest v1](../managed-extension-protocol-v1/README.md) 内；packageId 与条目 id 相等。显示文本不参与 CLR、模块或插件身份。仓库/owner/commit/tag/release/asset、清单/ZIP 摘要及长度保持固定校验。
- 工坊条目：steam-workshop / rimworld-mod，仍有 name、summary、rimWorldPackageId、workshopId、rimWorldVersions。禁止 manifest、artifact、localization。这里只提供索引/链接，Mod 内部本地化继续由 RimWorld 和作者负责。

## 显示投影

格式见[英文示例](README.md#display-projection)：localization.translations[locale] 里只放 name、summary、可选 changelog，localization.defaultLocale 可选。与[插件语言文件](../插件语言文件与宿主应用契约.md)共用语言标识、规范化和回退规则。

只有一个语言合法，不强制英文/中文。最多 16 种语言，规范化后重复拒绝，显式默认语言必须存在。每种语言可缺字段，整体必须提供 name 和 summary；全部缺 changelog 也合法。只提供 UI 字符串的语言文件可有空 display 映射。

名称最多 160、简介 1024、更新日志 8192 个 UTF-16 单元，显示文本总和最多 32768。拒绝空字符串、两侧空白、非法代理字符、控制字符和标记；简介/更新日志允许换行和制表符。UI strings 不复制到目录。逐字段选择：精确语言 → 合适的同语言/文字变体 → 可用英文 → 作者默认 → 稳定排序的可用语言。切换游戏语言会重建搜索/列表/详情，无需联网。

发布器从已校验的 Resources 语言文件提取 display，保留清单 defaultLocale。声明语言文件的包必须通过严格投影一致性检查：目录语言集合、默认语言、每个显示字段及缺失情况必须和 ZIP 内容完全一致。不能只凭 ZIP 摘要正确就接受伪造文案。不声明语言文件的包可以提供目录专用多语言文案，但不会因此获得 UI 翻译服务；因此旧不可变 ZIP 可以只发布新元数据快照。

## 包与发布边界

ZIP 只包含 manifest.json、声明的 Assemblies DLL 和 Resources，以及必要的空祖先目录。禁止原生 Mod 外壳、未声明文件、链接、路径逃逸/设备名和大小写/路径冲突。保持 128 MiB 压缩、64 MiB 单文件、256 MiB 展开、4096 条目和原膨胀比限制。静态验证摘要、清单、资源和 PE/CLI 模块/引用，不执行候选代码。

GitHub/CF 在同一个逻辑仓库身份下提供相同字节。PCS4 浏览缓存替换旧格式，不修改安装记录、设置或存档。仅修改目录文案不改变安装归属、版本和包摘要；安装仍复核下载前后的实时链并走宿主事务。

catalog.py 的 project 提取并验证包内文案，build 先校验完整目录再创建不可覆盖的本地草稿。它们不批准候选、不上传远端、不修改 stable；外层元数据草稿工具也接受 v3。远端发布顺序仍是上传并回读验证不可变目录 Release → 写不可变 published → 最后非强制快进 stable。草稿生成/验证失败不触碰旧指针。候选批准策略和受控自动发布属于下一批机器人工作。
