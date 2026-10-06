# Phinix 插件商店

2026-10-05：目录 v3 多语言名称/简介/更新日志、ZIP 文案一致性、GitHub/CF 通道与 staging 发布已交付。[实现与游戏验证](../../docs/branch-local/dev/plugin-store/目录v3实现与验证.md)。Playtest 1.3.0 包不变；后续为 A2 准入与正式交互。覆盖下方历史首版状态。

[English](README.md)。2026-10-05 本地首版候选：商店已随主体发行，catalog v2 的联网浏览、简介/标签、依赖计划、DLL 下载校验、宿主安装、启停/卸载和恢复已接通。新来源和 1.2.0 测试包已发布并接入 staging，游戏验收待做；详见[交付记录与测试清单](../../docs/branch-local/dev/plugin-store/商店首版交付与验收.md)。

在根目录构建主体与商店：

```sh
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

使用 `Output/phinix-rework`；自身 DLL 位于 `Common/Extensions/17-PluginStore.Client.dll`，翻译随主体。客户端抽象 1.6.0，保留模块 ID `phinix.plugin-store` 与用户设置，仍按通用发现/注册/激活/关闭运行。宿主不引用商店业务实现。旧独立预览请取消启用/移走后重启；开发者可显式加 `-p:BuildPluginStorePreview=true` 生成预览。

完整 Mod 仅提供创意工坊链接，Steam/RimWorld 管理。DLL 插件由 Phinix 装到 SaveData 下，重启后加载，不创建 Mod 壳。包/模块开关各自独立，卸载保留设置、业务数据和存档。内置商店可停用；设置中的宿主扩展管理仍可恢复，已装插件加载不依赖商店启用。

离线缓存只可浏览。安装确认前后重新读取在线目录并绕过 Worker 元数据缓存；只接受锁定的路径、长度和摘要。新格式不重解释旧 Mod ZIP，旧安装记录仍可通过高级清理入口核对与卸载。简介随目录缓存，无远端图片/README。

staging 为 `https://plugins-staging.hunyuan2333.com`，来源 `phinix.managed`。新版 [Playtest 1.2.0](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.2.0) 只含自身 DLL 和 manifest；用户明确授权后已发布和部署，.NET 10/Mono 无代理生产下载检查通过。旧 `phinix.poc` 来源保留，未推送主体仓库；红包、人才贸易拆包继续按后续计划。
