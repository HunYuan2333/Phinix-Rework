# 依据、版本与能力状态

需要决定目标版本、取得官方资料或审阅 skill 时读。核对日期：2026-10-09（Asia/Singapore）。以下是本轮工作区与保存的 main 快照证据，不代表所有已安装宿主或最新发行包。

发行通知/Index 状态以本文件末尾“正式发行通知与队列交付复核”为最新证据；此前网络失败和旧快照仅保留历史依据。本轮不扩大新本地基础能力的游戏验收范围。

## 仓库基线（首轮历史记录）

| 仓库 | 本轮分支 / HEAD | 工作区与依据 |
| --- | --- | --- |
| `Phinix-Rework` | `dev` / `7c2b4978c57e33a4f2cbd3f45743e7aa1c531fac` | 存在未提交/未跟踪修改，读取实际内容 |
| `Phinix-Rework/Dependencies/Phinix.Common` | `detached` / `67f243d9edced77dc3fe669f8a435b1f14b59199` | 存在未提交/未跟踪修改，读取实际内容 |
| `Phinix-Rework-Common` | `dev` / `85ecb18278cf98c0e4fa9ab6c49f4d7db635b413` | 本轮初始工作区干净 |
| `Phinix-Example-Plugin` | `codex/f6-repository-split` / `3d4b46d411c823418a62b71eb799824ea2aa16ca` | 存在未提交/未跟踪修改，读取实际内容 |
| `Phinix-Legacy-RedPacket` | `dev` / `fc1f3c6b20b659f88852949a668883cc1112e893` | 本轮初始工作区干净 |
| `Phinix-Legacy-TalentTrade` | `codex/docs-user-guide` / `70cf37e9445d304b31a315d0e654171dea4150bf` | 本轮初始工作区干净 |
| `Phinix-Plugin-Index` | `codex/f6-repository-split` / `227df1118c1ab04cd8de4329ae6d7ecdccfb8476` | 存在未提交/未跟踪修改，读取实际内容 |

客户端 Common gitlink 为 `67f243d9edced77dc3fe669f8a435b1f14b59199`，protobuf gitlink/实际 HEAD 为 `4b0c3aacf0657fbf38253b38918d3358dd4319ec`，protobuf 初始无修改。同级 Common HEAD `85ecb18278cf98c0e4fa9ab6c49f4d7db635b413` 不能替代客户端子模块的未提交托管 ZIP/收据实现。客户端工作区 host assembly 0.9.7、abstractions 1.9；相同程序集版本也不能证明相同发行字节。

## 取得目标版本资料

规范公开仓库入口：[客户端](https://github.com/HunYuan2333/Phinix-Rework)、[Common](https://github.com/HunYuan2333/Phinix-Rework-Common)、[Example](https://github.com/HunYuan2333/Phinix-Example-Plugin)、[Index](https://github.com/HunYuan2333/Phinix-Plugin-Index)。仅确需服务端时使用 [Server](https://github.com/HunYuan2333/Phinix-Rework-Server)。

已提交材料可按完整 SHA 获取，如 [DI 公共契约](https://github.com/HunYuan2333/Phinix-Rework/blob/7c2b4978c57e33a4f2cbd3f45743e7aa1c531fac/Client/ClientExtensionAbstractions/Framework/IClientComposition.cs)、[Inventory 契约](https://github.com/HunYuan2333/Phinix-Rework/blob/7c2b4978c57e33a4f2cbd3f45743e7aa1c531fac/Extensions/Inventory/Contracts/InventoryContracts.cs)、[Example main CI 配置快照](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/e3cec0d1dbf7e8f4be7f6dc6cfc42daa3421c2ba/ci/config.json)。这些链接按本地对象确认路径存在，本轮未确认网络可取。

本 skill 已内含新闭环操作/接口语义；新 Quickstart、Delivery Plan、新 ZIP 工具与现代 Example 的未提交内容**不能从上列旧 SHA 链接取得**。需要执行这些能力时取得维护者交付的可信完整源码/工具快照，核对宿主/Common/protobuf 及文件摘要；或在能力正式提交后以新的完整 SHA 获取。没有快照或对应已发行工具时停止新闭环，不虚构最新版下载链接，不改用浮动 main 凑齐依赖。skill 文件随目录整体分发，不依赖维护者机器目录。

客户端、其 Common 子模块与同级 Common 的 AGENTS.md 已读取。Example、两个 Legacy 插件及 Index 仓内未发现 AGENTS.md；没有据此修改其配置。未读取服务端业务源码，本任务不需要服务端配合。

## 读取范围与问题

| 决策 | 实际查阅材料（仓库内路径）与结论 |
| --- | --- |
| 作者指南 | 客户端 `docs/Plugin-Development.md`、`docs/插件开发指南.md`；`docs/Design-Philosophy.md`、`docs/Compatibility-Boundaries.md`。API 以公开源码核对，传统文档 Register 描述不能覆盖新 Compose 推荐 |
| 基础交付 | `docs/branch-local/dev/Developer-Foundation-Delivery-Plan.md`、`docs/Local-Plugin-Quickstart.md`。D0–D4 实现/自动化记录已存在，游戏 UI、重启/修订加载、五轮切档/断线/退出、模式撤销与窄窗口仍待验收 |
| 安装与诊断 | 客户端 `ManagedLocalImport.cs`、工具 Program/PackageConfiguration/OfflinePreflight；Common 子模块 `ManagedExtensionInstallation.cs`、管理/恢复及 `ManagedExtensionZip.cs`；诊断与同步文档。schema 2 本地来源不伪造正式证据，schema 1 正式收据可读 |
| DI Example | `example/ExampleExtension.cs`、Example.csproj、package-config.json、pack.py、Tests/Program.cs：现代 Compose、共享 state、Borrow、localizer Dispose、generation。源码基础版本 1.0.2 不表示改写了旧 1.0.2 Release |
| 能力与恢复 | IClientComposition、IClientExtensionAbstractions、UI 公共接口，Common FrameworkTypes，InventoryContracts、docs/Inventory.md、Trade 出站注册/宿主管线实现 |
| 红包业务 | `fc1f3c6...` 的 publication.json 标识 1.0.2，README.zh-CN/CHANGELOG 明确用户确认本轮本地游戏测试通过，17 项算法回归等为原交付记录。保留确认，无需重问；不扩大到新侧载 UI 或全部第三方物品 |
| 人才贸易 | `70cf37e...` publication 1.0.1、README.zh-CN，传统入口，独立 Pawn/内存购买恢复限制；AssemblyVersion 1.0.0.0 与包版本不同，不混用 |
| Index | `227df111...` 工作区 README/ControlledPublication/SourceUpdates、模板与 workflows；未当最新 main。ControlledPublication 有并行未提交修改，其“尚未启用 A3”与来源更新实现不一致，按 workflow/SourceUpdates 和维护者信息限定结论 |

开发指南中红包仍写 1.0.0，与实际 publication/CHANGELOG 的 1.0.2 不一致，skill 使用后者并保留程序集身份。交付计划与 Quickstart 仍有“游戏验收前阻塞 skill”措辞；用户本轮明确授权编写草案和静态/场景验证，因此只交付草案，不修改文档或解除实机验收门槛。

## main 与远端状态（首轮历史记录，见下方最新复核）

只读 `git ls-remote` 查询四仓 main 均因代理连接失败，最新远端未知。保存的 origin/main 仅作历史源码证据：

| 仓库 | 已保存 main SHA |
| --- | --- |
| Example | `e3cec0d1dbf7e8f4be7f6dc6cfc42daa3421c2ba` |
| RedPacket | `fc1f3c6b20b659f88852949a668883cc1112e893` |
| TalentTrade | `c14f3f6d490106affe0d1878ff285b3b2d867f48` |
| Index | `a77c68d5ad1688f36fc599f3994c1064269b06c3` |

三个 main 快照已有 main push 自动正式 Release，固定宿主 `403cea6c207a630fae391c6dc29bbce5a5a17b3b`（该宿主 Common gitlink 亦为 `67f243d...`），未含客户端子模块新未提交实现。Example 当前新工具闭环不能据此宣称 main 云端已验证。需先交付 Common，再固定客户端 gitlink；实机验收后另批更新 CI pin。没有本轮 Release 资产/商店当前版本查询，不宣称修复包正式上架。

维护者明确提供 Index 远端已按小时调度并按获批策略批量检查；本地 workflow 仍 `17 */6 * * *`，旧 SourceUpdates 单次一个版本。记录两者依据；未来执行以只读核实后的目标版本 workflow/参数为准，不能固定为三个来源或保证中间每版入库。

## 工作区新能力内容指纹

以下 SHA-256 对应本轮读取内容，帮助识别未提交快照；不是官方发行包证明。其他并行工作可能继续变化，应重核对。

| 仓库内文件 | SHA-256 |
| --- | --- |
| `Phinix-Rework/docs/Local-Plugin-Quickstart.md` | `578c51c35f7647d31b1fd02b843e15898e60bb5f07f18efaebd4b246d99a04bc` |
| `Phinix-Rework/docs/branch-local/dev/Developer-Foundation-Delivery-Plan.md` | `abf4db67e84a773ec15d2aae51e06f76fb5804dba755fa133c70ed3553b491dc` |
| `Phinix-Rework/Client/ClientExtensionAbstractions/Framework/IClientComposition.cs` | `ec2548e0776ebf2e1bf47da7050adbffc6d23ab3918fd01b719e21a34b094162` |
| `Phinix-Rework/Extensions/PluginStore/Client/ManagedLocalImport.cs` | `4997960ca7dde506ed70df9c5def6215cf0393d9c3b38be2f5fd1688ef6ae664` |
| `Phinix-Rework/Extensions/PluginStore/Tools/ManagedPackageTool/PackageConfiguration.cs` | `607e2be3fd5d162145420f49dc0dfa3fe7bc3296dd4078e015920859cebd5743` |
| `Phinix-Rework/Extensions/PluginStore/Tools/ManagedPackageTool/OfflinePreflight.cs` | `1ee6b87861ae9ed058f24e015ef75ec5c107f36956221abe6ffa106b58f21542` |
| `Phinix-Rework/Dependencies/Phinix.Common/Common/Utils/Framework/ManagedExtensions/ManagedExtensionZip.cs` | `f17542c0b5dfb89b2eca1368f1568950e02d916abad24fbf76f9cdd8dbf064a9` |
| `Phinix-Example-Plugin/example/ExampleExtension.cs` | `d16c6319b80f781ef87d3a2796ab5c8e8f9906aa3c877a90c47f6d5e8d9355ac` |
| `Phinix-Example-Plugin/pack.py` | `22399e30f1b8de025595c554e32110975916ac4050012e82ff4cbee9f179714d` |

## 简单 DLL 工程组织补充依据

2026-10-09 按用户授权成功克隆并查阅 `HunYuan2333/rimworld-mod-engineering-skills`，实际 HEAD 为 `7b766c43f8cd5462cb8ab0b44d7421c3a1fd608e`，clone 后工作区干净，仓内未发现 AGENTS.md。仅下载查阅，没有安装/启用。此成功获取不改变上一轮 Phinix 仓库查询失败的历史记录，也不证明其最新远端/Release/Index 状态。

阅读其中文 SKILL、engineering-philosophy、fundamentals、engineering-review、performance-ui-threading、testing-release、observability-developer-tools；提炼并重新表述单一状态来源、功能内聚、显式所有权、按切片实现、风险匹配验证与准确产物。用当前 ExampleExtension/Example.csproj 核对共享 State、单项目、显式 Compile Include 与 pack 定位。

架构选择：保留本 skill 的阶段与能力参考，新增 [engineering.md](engineering.md) 承接简单托管 DLL 的代码组织；必要知识放在对应原参考，未复制完整 Mod 手册、脚本或大型 Mod 工程要求。简单 DLL 自给自足；Mod 路线继续联合外部 skill，下载查阅授权不等于安装/启用授权。

## 正式发行通知与队列交付复核

检查日期：2026-10-09，约 16:45（Asia/Singapore）。本轮遵循客户端 `docs/branch-local/dev/Plugin-Skill-Authoring-Prompt.md` 及通知更新提示词，只更新 skill。读取最新交付克隆的实际文件、差异和元数据；旧维护者 checkout 的脏文件/未跟踪项与 Common 子模块修改保持，未切分支/同步覆盖。四个交付克隆工作区干净；其插件目前在已同步的 dev，不能只按当前分支名称误称未部署 main。

| 仓库 | 通知/队列部署 main | 同步 dev |
| --- | --- | --- |
| Index | `5b1dbf2eb653a539f573bd3cc5f5305b82362c39` | `—` |
| Example | `fd246747ea5a44f066d375f077d671834ae093bf` | `78336cfb1ecf9c605b5c5bb192bd86b61548c194` |
| RedPacket | `cadd91210215f02830c668109430311a45a9386e` | `8bcef1581b51b76fd74f5d81ee846c3fe926a52d` |
| TalentTrade | `36939655e4fc0d9672ceb10db1764a22788ff23c` | `364e3b23923390a57c9a599c67c228cd4601cc5f` |

只读 GitHub API 复核三个 main/dev 均与上表相符；Index 最新 main 已因准入/发布推进到 `198227bb45f034077465d5f21120324afd08bf02`。当前四个写入 workflow、source_updates.py 与 ControlledPublication 的 Git blob 与 `5b1dbf2...` 审阅内容相同。这些提交是本轮证据，不是未来永久 pin。三个插件 main/dev 的 release workflow、通知脚本与说明字节一致。

三个仓的 secret 名称列表均无 `INDEX_UPDATE_TOKEN`，成功发行日志各有缺凭据 warning；只查名称/存在性，没有读任何值。**代码已部署/通知待接入**：已验证脚本及真实队列，但未完成配置插件 secret 后的端到端通知验收。不能用 notify job success 宣称已上线。

已只读复核三个 main push 构建 success，正式 `v1.0.4` 均非 draft/prerelease：

- [Example 37905382656](https://github.com/HunYuan2333/Phinix-Example-Plugin/actions/runs/37905382656)。
- [RedPacket 37905680323](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket/actions/runs/37905680323)，此前遗漏 workflow 摘要的 37905376685 失败。
- [TalentTrade 37905694769](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/actions/runs/37905694769)，此前同类 37905379141 失败。

实际队列证据（历史真实运行，本轮只读复核，不新触发）：

- [扫描 37905776190](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905776190)：success，匹配真实 runId 的 changed=true/errors=0；一次准入三个 1.0.4。
- [扫描 37905780510](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905780510)：准入推进 main 后过期，拒绝。
- [新扫描 37905950068](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905950068)：目录发布再次推进 main，过期拒绝。
- [第三次新扫描 37906197744](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37906197744)：success，真实 changed=false/errors=0；不误报又准入三个版本。
- [受控发布 37905904430](https://github.com/HunYuan2333/Phinix-Plugin-Index/actions/runs/37905904430)：success；远端 stable 的快照 `723778ea1de6d911bda2e8018b8df9cf1f336d08`、catalog 大小 28172 与 SHA-256 `5881736d4dd947c15eec5ae62e567a4684da8524fa8687240bc04294f6b32717` 匹配已保存目录字节，该目录包含三个 1.0.4。没有本轮游戏商店 UI 验收。

当前 main 的 ci/config.json 与 workflow 仍固定宿主 `403cea6c207a630fae391c6dc29bbce5a5a17b3b`，工具从该 pin 构建；新版通知发行成功不等于现代工作区 DI/配置/侧载在旧 pin 云端通过。原 Common/gitlink 交付、CI pin 更新和游戏验收待办仍保留。红包/人才贸易 manifest 已登记 workflow、通知/测试/说明，已逐项核对登记文件摘要一致；不对未审阅文件自动刷新。

版本化源码入口（链接依据实际 Git 对象，通知行为细节见 actions/publication）：
- [release workflow](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/fd246747ea5a44f066d375f077d671834ae093bf/.github/workflows/release.yml).
- [notifier](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/fd246747ea5a44f066d375f077d671834ae093bf/ci/notify_index.py).
- [notification integration](https://github.com/HunYuan2333/Phinix-Example-Plugin/blob/fd246747ea5a44f066d375f077d671834ae093bf/ci/INDEX-NOTIFICATION.md).
- [Index scanner](https://github.com/HunYuan2333/Phinix-Plugin-Index/blob/5b1dbf2eb653a539f573bd3cc5f5305b82362c39/.github/workflows/plugin-source-updates.yml).
- [controlled publication](https://github.com/HunYuan2333/Phinix-Plugin-Index/blob/5b1dbf2eb653a539f573bd3cc5f5305b82362c39/ControlledPublication.zh-CN.md).
- [RedPacket source manifest](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket/blob/cadd91210215f02830c668109430311a45a9386e/source-manifest.json).
- [TalentTrade source checker](https://github.com/HunYuan2333/Phinix-Legacy-TalentTrade/blob/36939655e4fc0d9672ceb10db1764a22788ff23c/check-source.py).
