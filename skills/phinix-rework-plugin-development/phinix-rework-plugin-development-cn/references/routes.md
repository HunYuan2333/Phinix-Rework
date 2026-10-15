# 需求与交付路线

先明确玩家可观察的结果、状态所有者和验收方式。小功能直接做可验证切片；联机/物品功能同时设计拒绝和未知结果的保留方式。

| 路线 | 使用条件 | 交付 |
| --- | --- | --- |
| 托管 DLL 插件 | 通过公共契约增加页签、动作、消息或库存交互 | 本地 ZIP，或经批准的 Release/Index；当前双版为 schema2 |
| 纯数据主题 | 配色、参数及继承规则，能由现有主题键表达 | schema1 数据包，两版共用，无 DLL |
| RimWorld Mod | 需要游戏管理的 Defs、原生资源、启动生命周期或较多 Harmony 接入 | 标准 Mod / Steam 工坊；不把完整 Mod 当托管 ZIP 安装 |

由实际内容和加载时机判断，不仅按“复杂度”判断。已选路线不反复要求确认；更换路线时说明具体缺口。只有确需服务端配合才读服务端实现，不为普通客户端功能预先引入服务端修改。

托管插件先读 [engineering.md](engineering.md)、[composition.md](composition.md)、[local-loop.md](local-loop.md)；主题读 [theme-package.md](theme-package.md)。简单插件无需另装工程 skill。若用户选择完整 Mod，可按其需要和授权查阅额外工程资料；外部 skill 或仓库名称不是安装/启用授权。
