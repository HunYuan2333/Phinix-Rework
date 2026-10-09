# 需求与路线

需求未定或涉及 Defs、资源、原生生命周期、Harmony 时读。

先把用户期望写成可观察行为：谁操作、何时触发、影响哪些数据、失败时保留什么、交付给谁。一个计数 Tab 可以直接从 Example 改名；涉及库存转移/联机应明确模块、连接、存档和每次操作的状态与所有者，拆分最小可验收步骤，避免先写长报告。

| 路线 | 文件管理者与适合范围 | 交付 |
| --- | --- | --- |
| Phinix 托管 DLL ZIP | Phinix 安装/管理插件自有 DLL、manifest、包内翻译。适合公开扩展点的 Tab、设置、消息、库存等 | 本地侧载；正式 GitHub Release → Index |
| RimWorld Mod | RimWorld/Steam 管理 About、Defs、贴图/音频等资源和程序集；可通过公开契约接入 Phinix | Steam 工坊发布 → 工坊索引申请 |

涉及游戏原生生命周期、Defs、资源或较多 Harmony 修改时建议评估 Mod：这些文件与启动时机由游戏管理，托管 ZIP 不能代替完整 Mod 内容管理。不是按“复杂”强制选择；明确能力限制、维护成本和发布方式，让用户决定。简单托管插件无需搭 About 临时试用，优先本地侧载闭环。托管路线没有已取消的商店整 Mod ZIP 安装功能。

选择托管路线后，按 [轻量工程组织](engineering.md) 用最小单项目实现；简单 DLL 的组织、验证与诊断指导已随本 skill 分发，不需要外部 skill。

用户选择 Mod 后，可建议联合使用 [RimWorld 工程 skills](https://github.com/HunYuan2333/rimworld-mod-engineering-skills)。区分下载查阅与安装/启用的授权，分别在尚未获授权的动作前询问；已有对应授权不重复问；授权后核对真实内容、适用范围与安装方法，不能只按仓库名信任它。外部 skill 负责游戏工程，本 skill 负责 Phinix 接口、契约与发布边界。未授权则继续本地设计与 Phinix 接口准备，停在下载/启用前。

只在需求确需服务端配合时读 `Phinix-Rework-Server` 的 AGENTS、固定契约和相关代码。已有公开客户端能力可完成的需求不先加载服务器。连接旧服务器时由 adapter 明确拒绝不支持能力或说明降级，不让 UI 自动换协议冒充成功。
