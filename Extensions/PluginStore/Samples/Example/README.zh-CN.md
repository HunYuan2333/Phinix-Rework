# Phinix 示例插件

[English](README.md)。由 Playtest 改成的正式开发示例，已删除全部白银/物品生成能力，走普通托管 DLL 插件的审核与发布路线。

- 公开 API 注册多语言 Tab 和设置组。
- 持久计数、“显示说明”选项即时影响 Tab，设置键使用插件 ID 前缀。
- 确认窗口重置计数；旧激活周期的回调不能修改设置。
- 中英文 JSON、参数格式化、语言变化事件与停用时清理。
- 绘制后恢复 GUI 状态，提供响应式布局提示。

只修改自身设置，无地图、殖民地、物品、网络或存档操作。设置属于当前游戏用户配置，跨存档共用，卸载重装保留；不作为按存档持久化示例。

## 体验

正式商店安装“Phinix 示例插件”并重启，打开“示例”点击计数。在 Phinix 设置切换说明，确认 Tab 即时变化；测试重置取消/确认、中英文切换和重启保留设置。扩展管理停用/启用或卸载后重启。编译与静态验证不能替代游戏验收。

## 构建与打包

需要 .NET 10、含本地化支持的 Phinix-Rework 开发检出、自备 RimWorld 1.6 参考。1.0.0 基于 Assembly-CSharp 1.6.9676.18020 / ClientExtensionAbstractions 1.7.0，不分发参考 DLL。

```sh
python3 pack.py --phinix-root /absolute/Phinix-Rework --game-references /absolute/RimWorld/Managed --output /absolute/new-output/phinix-example-basic-1.0.2.zip
```

可选 --bundle-output /absolute/new-folder 生成开发文件夹。商店安装前移走手工副本避免重复。包/模块 ID 为 phinix.example.basic，程序集 Phinix.Example.Basic，不迁移旧 Playtest 身份和计数。

## 正规发布演示

提交源码、发布固定 Release ZIP、向[正式索引](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/new/choose)提交准确候选。静态检查通过后维护者添加 plugin-approved；机器人生成并合入证据 PR、复核、发布目录，成功关闭 Issue。作者不自我批准、不手工合入元数据、不向 index 上传 DLL。参见[发布者指南](https://github.com/HunYuan2333/Phinix-Plugin-Index/blob/main/GitHubBotGuide.zh-CN.md)。

旧 Playtest 留在独立测试仓库，不进入正式目录。本仓库存源码及资产，index 只保存校验后的元数据。

## 已发布示例

1.0.0 已在正式商店上架，完整正规流程为[申请 #15](https://github.com/HunYuan2333/Phinix-Plugin-Index/issues/15) → [证据 PR #16](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/16) → [自动发布成功](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37335979507)。GitHub 与 CF 下载已核对相同 SHA-256。尚需按照上方清单进行人工游戏验收。

设置区内部 ID 用于注册，示例通过自身本地化服务显示标题；新版 host 不会将未翻译的内部 ID 当作玩家标题。
