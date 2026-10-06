# Package inputs / 包输入

No package has been approved. This directory intentionally contains no package JSON.
目前没有批准任何包，本目录故意不放示例或伪造的发行记录。

The schema-v1 client requires explicit IDs, channel, integration kind, compatibility,
dependencies, modules, assemblies and external mod requirements. GitHub records also
bind source commit, repository/owner/release/asset numeric identities, exact sizes,
raw package SHA-256 and manifest SHA-256. Numeric GitHub IDs are decimal strings.
Workshop records contain metadata and a Workshop ID; the store does not download
Workshop content or invent its package version/hash.

schema v1 需要显式包身份、渠道、集成类型、兼容范围、依赖、模块、程序集和外部模组要求。GitHub 条目还绑定源码提交、仓库/作者/Release/资产数字身份、精确大小及包/清单的原始 SHA-256；GitHub 数字 ID 使用十进制字符串。工坊只保存元信息和工坊 ID，不由商店下载，也不虚构版本或摘要。

Package manifests exclude catalog state and artifact metadata to avoid circular
self-hashing. Source and actual release assets must exist before an entry is accepted.
The final input layout, JSON Schema, reviewer tools and submission workflow are not
implemented in this bootstrap.

包清单不包含索引状态及 artifact 字段，避免自哈希循环。入库前必须有真实源码和发行资产。最终输入布局、JSON Schema、审核工具及申请工作流尚未在初始化仓库实现。
