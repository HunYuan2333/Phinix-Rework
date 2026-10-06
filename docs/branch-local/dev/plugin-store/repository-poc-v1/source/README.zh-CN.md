# Phinix 插件商店受控 PoC

这里是公开的**无行为分发测试**，不是正式批准扩展，也不是官方插件索引。测试来源 `phinix.poc` 与官方来源隔离，正式索引和游戏模组不变。

`PocMarker.cs` 声明 net472 程序集 `Phinix.Store.Poc`，版本 `1.0.0.0`，没有初始化器、游戏 hook、网络、存储、安装脚本或 Phinix 模块。ZIP 仅含 About、manifest 和该程序集，用于 gateway/hash 验证，尚未通过游戏加载或安装验收。

Mono 构建：`mcs -sdk:4.7.2 -target:library -out:/tmp/Phinix.Store.Poc.dll PocMarker.cs`。二进制只上传 GitHub Release，不提交源码仓库。catalog Release 固定仓库/owner/commit/tag/release/asset ID 和 SHA-256；先提交 published，再更新 stable。本仓库不提供正式审批或自动发布流程。

本测试原创源码和元数据使用 MIT 许可；不分发 RimWorld、Unity、宿主框架或第三方 DLL。
