# 第三方插件开发者基础能力交付计划

2026-10-09。按用户要求，本轮已据此核对并实施基础设施补齐。侧载及以下最小开发闭环验收完成前，skill、模板生成器和其他新的作者工具全部阻塞。原始计划保留，实际交付与未完成验收见第 5 节。

## 1. 实施前基础与缺口

已有模块发现、DI 服务、生命周期、主线程调度、包内翻译、插件依赖与安装事务、结构日志、main 正式发行和 Index 自动更新。保留这些设施，基础交付以复用和补齐入口为主。

| 顺序 | 基础能力 | 代码核对结果与交付范围 |
| --- | --- | --- |
| D0，第一优先 | 本地 managed ZIP 侧载 | Store 当前没有文件导入入口；下载控制器绑定远端目录快照。提供 `Prefs.DevMode` 入口、明确的本地来源、现有校验与事务接入，以及重启加载。 |
| D1，与侧载一起验收 | 可反复迭代、可排错 | 已有启停、卸载、错误详情和结构日志。补齐本地构建版本/摘要、模块发现与激活状态、依赖失败原因的统一展示及可复制摘要。明确同版本不同哈希、官方包冲突、重启替换规则。 |
| D2 | 现代 DI 可运行示例 | 独立 Example 仍实现 `IPhinixExtensionModule.Register`；新文档推荐 `ClientExtensionModule.Compose`。迁移示例，覆盖 Tab、设置、翻译、借用服务和对称释放。让作者复制改名后可以直接编译、侧载和验收。 |
| D3 | 可配置打包、离线预检 | `ManagedPackageTool` 已校验清单、DLL 与翻译资源，但生成清单时依赖/外部 Mod 列表为空，游戏/宿主范围有固定值。提供受校验的配置输入与本地 ZIP 预检命令，使作者声明依赖和兼容范围，无须修改打包器源码或先发布 Release。 |
| D4，最终门槛 | 从源码到游戏的最小操作闭环 | 已有文档与 `pack.py`；需要按实际入口走通并精简快速入门。说明本地引用准备、只编译插件、打包预检、侧载、诊断、迭代和卸载；发布另走 main → Release → Index。 |

## 2. 实施顺序与边界

1. **先 D0**：按 [本地侧载计划](plugin-store/Local-Development-Package-Install-Plan.md) 实现。先核对收据/安装输入的来源模型：本地文件没有远端目录或审批证据，不能伪造官方快照来满足旧参数格式。来源适配须保持既有正式安装记录可读、事务恢复和物品业务行为兼容。
2. **D1 补最小开发循环**：安装、拒绝、重启生效、更新、卸载都可理解。开发来源和正式来源明确区分；不直接覆盖官方包或已加载 DLL。沿用下一启动启停语义。诊断摘要包含包 ID、版本、摘要、来源、模块状态、宿主/抽象版本及实际失败原因，不导出凭据、玩家信息或存档内容。
3. **D2 与 D3**：以 Example 作为唯一基础样例验证新 DI 路径及通用打包能力。第三方可引用已安装/已构建宿主中的公开程序集，游戏引用自行提供且仅参与编译；开发迭代不应要求每次重编整个宿主。打包工具可单独构建/调用，不新增 Phinix NuGet SDK 渠道。
4. **D4 实际走通后再定稿文档**：本地开发不要求 GitHub、私有引用 token 或 Release；这些属于选择云端发行时的后续准备。保留固定宿主版本与 Common/protobuf gitlink 规则。

D0/D1 的具体来源、版本修订和冲突设计先单独审查，再动安装底层。D3 先复用已有清单/载荷校验，不另建与商店规则不同的验证器。保持插件平权；宿主不添加专属红包、示例或其他业务插件安装分支。

## 3. 解除 skill 阻塞的验收门槛

- 在独立工作目录中，从 Example 创建一个独立 ID/程序集的插件，使用现代 DI，不改宿主代码即可编译、打包和侧载。
- 未开启开发者模式时没有导入入口；执行与最终确认都检查模式。关闭模式后已安装开发包仍可管理。
- 正确包能安装并在重启后出现；损坏包、缺失依赖、重复模块或程序集冲突给出准确原因，失败不留下半安装状态。
- 再次修改插件，按确定的开发修订规则安装并重启，能确认实际加载的是新构建；启停、卸载和中断恢复可用。
- 验证示例设置、翻译、主线程回调和资源释放；切档、断线及退出后不重复订阅、不持有旧世界对象。
- 作者能复制足够的诊断信息排错；无需从完整 Player.log 中猜测加载了哪个版本。
- 保持 main 正式 Release 与 Index 更新流程有效；本地测试包不会自动变成商店正式发行。

以上门槛满足后，才恢复 skill 编写和其他新的作者工具工作。红包 1.0.2 的游戏验收可作为侧载使用场景；其 DI 迁移与人才贸易 DI 迁移另行评估，不扩大本次基础能力门槛。

## 4. 当前暂缓范围

DLL 热加载、项目生成器、专用测试框架、开发者门户、额外 SDK 分发渠道及扩展业务 API 不列入本批必做项。强制插件签名仍按既有决定取消。已有 Actions 继续运行，本次暂停的是未交付的 skill 和其他新工具工作。

## 5. 本轮核对与实施记录

| 项目 | 实际补齐 | 验证与边界 |
| --- | --- | --- |
| D0 | DevMode 条件入口与溢出菜单、显式 ZIP/目录选择、宿主临时区冻结输入、执行/确认模式检查、通用安装事务 | 商店与宿主编译通过；无远端目录时导入控制器可运行。Windows/Linux 共用文件选择界面；游戏 UI 验收待完成。 |
| D1 | `local-development` 来源，独立 schema 2 本地收据；正式 schema 1 保持可读。同版本不同 ZIP 摘要可修订；重复摘要、降级、其他来源同 ID 拒绝；已加载代码继续使用启动快照 | ManagedExtensionRuntimeTests 覆盖本地来源/模式/输入冻结、冲突与依赖、9 个开发修订提交/恢复场景、暂存后模式撤销、确认去重/取消释放和诊断摘要。扩展管理复制包/当前构建摘要、宿主版本、发现/激活、依赖和原因码。 |
| D2 | 独立 Example 迁移 Compose/DI；Tab 与设置共享状态；借用设置/dispatcher；localizer 对称释放，回调带激活代次 | 独立复制并改名为 `test.foundation.independent` / `Foundation.Independent.dll`，仅编译插件，打包与宿主引用预检通过。实际 Example 五轮发现/激活/关闭 45 条断言通过，检查设置保留、翻译订阅、回调失效及借用服务不被释放。原发布制品未改写。 |
| D3 | `--config` 配置兼容范围、插件依赖、外部 Mod；`--validate ZIP` 离线预检，可提供实际宿主 DLL 检查 CLR 引用 | 本地、正式下载、离线工具共用 Common 的 ManagedExtensionZip；打包也在写输出前调用该校验器。CLI 16 项检查通过，包含有效依赖/兼容配置、坏 ZIP、危险路径、未知/重复配置、坏依赖以及宿主程序集冲突。静态通过不等于游戏加载许可。 |
| D4 | 新增中英文本地快速入门；Example pack.py 复用工具和已构建公开 DLL | 见 [Local-Plugin-Quickstart.md](../../Local-Plugin-Quickstart.md)。从独立源码到 ZIP 及预检已走通；实际重启、切档、断线、窄窗口与游戏退出仍待游戏验收。 |

实现审查结论：本地输入不构造假 catalog；本地收据不含 repository/catalog 字段。开发修订沿用原替换 journal 和提交决定，完整性、依赖、程序集/模块占用、启停/卸载不增加业务特例。临时导入文件不进入远端缓存；当前会话与已安装摘要分开展示，不承诺热加载。共同代码通过客户端固定 Common gitlink 获取，不使用浮动分支。

2026-10-09 提交交付：用户授权将侧载整批实现与 README/skill 一并交付，并同步客户端 dev/main。Common 先提交推送到 dev：`6c2ece225bba2616d2a68d3b093cc611cb9733b5`，客户端固定此 gitlink；protobuf gitlink 不变。完整客户端 `Release 1.6` 编译通过，0 错误、7 个现有弃用/目标框架或依赖审计网络警告。提交和编译不替代下列游戏验收，也不发布另一仓的现代 Example 工作区修改或更新插件 CI 固定宿主。skill 以注明能力前提的开发引导草案交付。

未解除门槛：没有把控制台测试记作 RimWorld 内验收。正确包游戏重启后出现、新修订实际加载、五轮切档/断线与退出释放、模式关闭时未提交意图取消、窄窗口溢出均须实机验收；完成前 skill 继续阻塞。

最终验证记录：ManagedExtensionRuntimeTests 3319 条断言通过；Example 生命周期 45 条断言通过；打包 CLI 16 项检查与发行目录布局 2 项检查通过；宿主、Store、工具均编译通过。四份中英文翻译 XML 解析通过，三个修改工作区 `git diff --check` 通过。独立 ID 示例 ZIP 与 main 的 `--version` 注入均已实际打包预检。

正式发行核对：读取了 Example `origin/main` 的 release workflow 与 `ci/build.py`，固定宿主并先构建宿主/工具的顺序仍有效；保留 `pack.py --version` 供 main 自动版本注入。本轮没有触发 Release/Index，也没有修改 main 分支。新 `--config`/`--validate` 需要本轮宿主工具；合并发布前必须把 CI 固定宿主提交更新到已通过验收的版本，不能用旧固定 packager 宣称新闭环已发布。

## English summary

Developer foundations take priority over skills and new author tools. Implement developer-mode local managed ZIP sideloading first, then a repeatable install/diagnostic loop, a working modern DI Example, configurable packaging/offline validation, and an exercised quick-start workflow. Reuse existing validation, transactions, lifecycle and publishing infrastructure. Local provenance must not impersonate Index approval; game references remain compile-only, and no extra Phinix NuGet SDK channel is introduced. Restore skill work only after an independently named sample completes compilation, validation, local installation, restart, iteration and removal without host changes.
