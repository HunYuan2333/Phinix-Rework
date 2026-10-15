# 本地分发、Release 与 Index

按用户选定的路线交付。创建项目或本地打包不自动授权上传、推送、发版、Index 写入或部署；已有相应授权不重复询问。

## 分发选择

- 本地或好友分发：提供准确 ZIP、摘要、目标 Host/API/游戏版本、已测与待测项。DLL 本地安装需要开发者模式并重启；主题导入不要求开发者模式且可热应用。
- 手动 Release：可在本地构建验证后上传公开、非草稿/非预发布的固定版本及对应源码 Tag。作者仓库不必配置 Actions。
- 自动 Release：适合已有 CI 或高频迭代，见 [actions.md](actions.md)。推送 main 是否发版取决于该仓实际 workflow，不假定所有仓库相同。

## DLL 插件

首次通过 Index 当前提交/审核流程建立候选与来源策略；不要把一个标签等同整套审批完成。策略锁定包 ID、仓库/owner、资产命名及更新范围。`plugin-source-updates.yml` 当前计划为 `17 * * * *`，这表示按小时安排，不承诺准时执行或一小时内收录。

手动和自动生成的 Release 都可被已批准策略发现。后续仍核对公开状态、Tag/commit、资产摘要、兼容范围、依赖和策略边界；超范围更新可能被 `UpdateScopeChanged` 阻止。扫描成功可能是 changed=false 或含单来源错误，不能只看绿色状态。声明/审核记录、受控发布、stable/published/catalog 和客户端安装分别确认。

单变体 schema1 与当前双变体 schema2 不能和目录 schema3 混为一谈。维护插件当前交付双版，不据此把解析器支持的其他候选形态写成不存在。不要覆盖已公开版本/资产或使用 --clobber 掩盖摘要冲突。

## 主题包

主题使用独立 `phinix.themes` schema4 feed；包自身仍为 schema1。当前流程是维护者审核 `themes/packages/*.json` 并运行 `theme-publish.yml`，不是 DLL 的自动来源扫描。核对任意公共作者的源码、固定 Release/asset、许可、manifest/XML 和摘要；不限定官方组织。

Index 保存原作者来源，并在同一快照 Release 托管 `theme-<ZIP SHA256>.zip` 副本。CF 从已批准副本取包并可缓存，GitHub 直连使用原作者资产；两条路径必须字节一致。作者不提供 Index 写令牌、CF 凭据或逐组织下载令牌。网关缓存/部署是独立运维任务，不是创建主题的默认步骤。

## 上线验收

检查 Release 资产、实际 Index 记录、当前快照、两条下载路径以及客户端列表点击/详情/确认/安装。安装成功不代表主题已应用，目录工具下载通过也不证明 UI 可安装。GitHub 匿名额度耗尽时按响应诊断等待重置或由用户切换 CF，不反复刷新、不把服务端令牌放进客户端。

实现依据：Index `themes/README.md`、`scripts/theme_publication.py` / `theme_release.py`、`scripts/source_updates.py` 及相关 workflows；Host `ThemeStoreController.cs` / `ManagedStorePresentation.cs`。仓库地址见 [basis.md](basis.md)。
