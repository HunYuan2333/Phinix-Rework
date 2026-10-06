# Phinix 插件索引

[English](README.md)

这是 Phinix 公开插件索引的开发初始化仓库，保存审核元数据及索引快照。作者在自己的仓库发布源码和二进制包；工坊条目链接到 Steam。

**当前仅完成初始化，没有已批准的插件。** 客户端已有本地索引预览和静态载荷校验；GitHub 传输、安装、Steam 与自动审核/发布仍在实现。建立仓库不代表商店已经可以联网安装。

| 路径 | 用途 |
| --- | --- |
| `source.json` | 开发源身份：`phinix.official` |
| `packages/` | 已批准包的元数据；当前为空 |
| `reviews/` | 候选指纹及批准记录；当前为空 |
| `scripts/build-empty-catalog.py` | 生成 schema v1 空索引与原始字节 SHA-256 |
| `catalog.json` | 可供客户端读取的初始空索引，不是索引 Release |
| `catalog.json.sha256` | 初始索引原始字节的 SHA-256 |

初始化脚本只处理空包集合。发现任何包 JSON 就失败，不会忽略条目，也不冒充最终包/审核校验器。

```bash
python3 scripts/build-empty-catalog.py --snapshot <索引输入提交的完整SHA>
```

输出位于忽略的 `dist/`。先提交源输入，再使用该提交生成索引；`snapshotId` 指向输入提交，避免生成文件引用自身提交的循环。仓库内的初始索引用于预览；真实索引 Release 成功之前，不发布 `stable.json`。

后续发布流程：校验已批准输入提交 → 生成 `catalog.json` 和校验文件 → 发布固定索引 Release → 核实上传资产 → 更新 `stable.json`。客户端消费已发布快照；分支修改和 CI artifact 不等同于批准或发布。

索引仓库不上传凭据、游戏/Unity DLL、服务器状态或作者二进制。收录与元数据校验不等于代码安全认证。客户端和框架代码位于 [Phinix Rework](https://github.com/HunYuan2333/Phinix-Rework)。
