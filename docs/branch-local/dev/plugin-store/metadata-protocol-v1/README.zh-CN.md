# 仓库元数据协议 v1 草案固定向量

[English](README.md)

本目录用于客户端解析和本地草案生成器回归。`catalog.json` 来自已有的 `phinix.official` 空 bootstrap 索引，repository/owner ID 是已核实的初始化身份；**Release ID 和 asset ID `1` 是合成数据**。本目录没有对应的实际发行或线上端点，不能用于 GitHub 回源、审核、安装或公网 PoC。

stable 通过原始 SHA-256 和精确字节数绑定 catalog 及 `published/<snapshotId>.json`。snapshot 指生成 catalog 的输入提交，不是后写生成文件的提交。客户端使用以下固定路径：

- `/v1/sources/{source}/stable`
- `/v1/sources/{source}/snapshots/{snapshot}/published/{publishedSha256}`
- `/v1/sources/{source}/snapshots/{snapshot}/catalog/{catalogSha256}`

stable/published 的 `schemaVersion` 独立版本化，`catalogSchemaVersion=1` 复用现有严格清单，不向其添加未知字段。发行描述绑定索引 repository、repository/owner/release/asset ID 和固定的 `assetName=catalog.json`；客户端校验这些字段，只向配置的 HTTPS 端点发请求。后续 Worker 仍须与获准 source 配置和实际 GitHub 资产核对。

未签名草案信任配置端点及正常 TLS。同链 hash 证明字节一致，不独立防止 gateway 伪造批准记录。正式签名模式和发布端核验仍待完成。

可在另一本地目录重建，命令见[英文说明](README.md)。生成器检查 envelope、绑定原始目录字节，以不可覆盖方式创建 published 描述，最后替换本地 stable。它不检查全部包记录/载荷，不下载或核实真实 Release，不批准或发布文件；正式发布前须由可信发布器补齐这些校验。相同输入可重复运行，已有快照的不同描述被拒绝。
