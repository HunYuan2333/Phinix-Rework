# Phinix 仓库拆分迁移规划（三仓 + Git submodule）

2026-10-07 排期更新：商店主体已交付。本文件保留详细评估，执行顺序以[当前客户端基础设施与三仓计划](后续实施计划.md)为准；Server 功能暂缓，Stateless 主仓首个试点改为商店操作。旧红包源码路径及历史商店前置不是当前实施清单。


> 更新日期：2026-10-05。
> 当前方案：一个 Phinix 共享源码仓库，由公开的 Client、Server 仓库通过 Git submodule 固定提交引用。
> 共享代码只维护 submodule 这一种消费方式，不建立并行的 NuGet SDK 发布管线。
> 状态：迁移规划，尚未执行拆仓、创建远端仓库或修改项目引用。

总体顺序与跨方案依赖见 [后续实施计划](./后续实施计划.md)。先验证生命周期、共享源码消费与旧聊天所需的通用契约，再物理拆仓；不等待全部 DI/HTTP/状态机迁移。

2026-10-06 用户排期更新：先完成[商店全部相关工作](./plugin-store/商店专项收尾顺序.md)，包括 CF 拆仓与红包/人才贸易独立发行。本方案保留为后续候选，不先于官方业务插件拆包执行；商店只补其必需的通用能力。

2026-10-06 补充：[CF 网关独立仓库与发布迁移方案](./plugin-store/CF网关独立仓库与发布迁移方案.md)建议将插件商店 Worker 归独立分发基础设施仓库。它不归多人游戏 Server 或 Common，不加入三仓的 submodule 消费图，可先迁移；当前仅计划。索引及审核机器人继续归现有 Index 仓库。

## 1. 选择与适用前提

当前契约与框架接口仍在调整，主要由同一维护者联动开发客户端、服务端。采用 submodule 可以直接查看、修改、调试共享源码，不需要先发布包再验证两端。三个仓库公开后，获取源码通常不需要跨私有仓库访问凭据；贡献者仍需能访问 Git 托管服务。

这不是“submodule 永远优于 NuGet”的结论。若契约长期稳定、独立消费者明显增加，公开 NuGet 单渠道也有价值。但本轮不预设第二条发布渠道，不要求同时维护源码消费和包消费。

“一个 submodule”指一个 Phinix 自有共享仓库：Client、Server 各有一个指向该仓库的子模块入口，各自固定提交。已有的 protobuf 供应商子模块保留在共享仓库内，因此技术上仍有嵌套子模块；本轮不通过删除或改写 vendored protobuf 来减少层数。

## 2. 对方案评估的修正

| 原判断 | 核对后的结论 |
| --- | --- |
| 不需要包发布基础设施 | 成立。不需要自建 NuGet 源、GitHub Packages 凭据或 Phinix 包发布工作流 |
| 不需要改 ProjectReference | 引用类型可保留，但共享目录迁移后需要更新路径；SolutionDir、链接源码和复制目标也需调整 |
| 源码可见，调试体验接近当前 | 成立，前提是初始化子模块，并将对应工程纳入解决方案 |
| 三仓可以原子变更 | 不成立。每个仓库提交独立；只能用关联提交、PR 和明确升级顺序组织一个逻辑变更 |
| 改协议必定三个 PR，漏了就 CI 爆 | 不一定。兼容性新增不必让两端同时升级；编译通过也可能隐藏协议不兼容，不能只靠 CI 判断 |
| 子模块必须追踪远端最新版本 | 不成立。消费者记录具体 commit；正常初始化不使用 update --remote |
| detached HEAD 一定是异常 | 不成立。按固定提交检出时是正常状态；修改共享源码前显式创建或切换开发分支 |
| 嵌套子模块让复杂度翻倍 | 会增加初始化与构建配置工作，但不宜量化为翻倍；递归初始化覆盖获取流程，版本管理仍需明确 |
| 完成一次 update 就完全离线 | 只对已获取的 Git 对象和子模块成立；首次获取、升级，以及外部 NuGet 包、SDK、protoc 等仍可能需要网络 |
| submodule 无法显式管理版本 | commit 固定是精确的；用发布标签、升级提交说明和构建版本信息补足可读性 |
| NuGet 可以靠 protobuf 自动保证两端兼容 | 不成立。源码/包版本和网络协议兼容是不同问题；字段、能力、业务确认语义都需要独立验证 |
| GitHub Packages 只有私有包需要认证 | 不成立。GitHub 的 NuGet 源安装公开包也要求认证；这不代表所有 NuGet 托管方式都有相同限制 |

参考：[Git 子模块说明](https://git-scm.com/docs/gitsubmodules)、[GitHub NuGet 认证说明](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)。

## 3. 三个仓库的职责

仓库名以 phinix-common、phinix-client、phinix-server 为规划占位名称，不在本轮变更命名空间、程序集名或持久化身份。

```text
phinix-common
  Common/                          共享协议、契约、现有共享运行时
  Extensions/<Plugin>/Contracts/   经边界整理后的共享插件契约
  libs/                            共享构建所需的现有依赖
  Dependencies/protobuf/           保留固定提交的供应商子模块
  .gitmodules
  Directory.Build.props / targets
  共享代码测试与构建说明

phinix-client
  Dependencies/Phinix.Common/       submodule → phinix-common 的指定 commit
  Client/                          宿主、客户端 endpoint、游戏相关抽象
  Extensions/<Plugin>/Client/      客户端插件实现与游戏相关适配
  Tests/                           客户端测试
  GameDlls/                        本地编译引用，不提交或分发游戏 DLL

phinix-server
  Dependencies/Phinix.Common/       submodule → phinix-common 的指定 commit
  Server/                          宿主与服务端 endpoint
  Extensions/<Plugin>/Server/      服务端插件实现
  Tests/                           服务端测试
  Dockerfile / .dockerignore / docker-compose.yml
  .github/workflows/docker.yml
```

phinix-common 是共享源码仓库，当前包含运行时实现，并非全部都是接口。共享契约与共享运行时在工程层保持职责区别，不为了“契约仓库”这个名称拆出第四个仓库。

- 共享网络、认证、用户和插件协议，以及两端需要的无游戏依赖契约，只有一个源码归属。
- NetClient、NetServer、ClientAuthenticator、ClientUserManager 等 endpoint 实现归各自消费者，不能继续依赖跨仓库链接源码。
- ClientExtensionAbstractions 中的游戏 UI、Verse/Unity 类型归客户端。共享项目不能反向引用 Client 或 Server 仓库。
- 当前 Chat/Trade Contracts 的 net472 部分引用客户端抽象，Trade 还引用游戏程序集。先分离共享协议/API 与游戏相关接口，再移动共享部分；不能直接照目录整包搬迁。
- IFrameworkChatServerApi、IFrameworkTradeServerApi 等供其他插件消费的服务接口归各自插件 Contracts，宿主仍不依赖业务插件契约。
- 其他插件按同一规则分类，不能因为只有官方插件当前使用某个接口就赋予其特殊宿主路径。

## 4. 阶段 0：建立基线和依赖清单

在独立临时副本中实施拆分，保留当前脏工作区与未跟踪源码。git clone 或历史提取只包含已提交内容，不能把缺失未提交文件误认为完整迁移。

先确认迁移输入快照，再列出：

- 所有 ProjectReference、HintPath、Compile Include/Link、生成源和子模块依赖。
- SolutionDir、GameDlls、libs、Output、编号 DLL 复制及 Extensions 发布路径。
- 每个测试实际引用的生产代码、平台和游戏程序集。
- 共享运行时与契约 DLL 的身份和分发所有者。
- 当前已存在的构建失败；拆分不能把旧失败当成新回归，也不能直接宣称基线成功。

按当前布局执行合适的基线命令，记录真实结果：

```powershell
dotnet build Server/Server.csproj --configuration Release
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release
```

客户端完整构建需要游戏引用程序集和仓库规定的工具链，仍为手动检查。以上命令不代表拆分后测试路径已经确定，也不替代游戏内验证。

## 5. 阶段 1：先整理边界与构建路径

在 monorepo 内先完成以下工作，每步保留可验证的行为：

1. 迁出 endpoint 专属源码，更新所有显式 Compile 项与测试引用。
2. 消除共享工程对客户端宿主、游戏程序集和服务端宿主的反向依赖。
3. 将共享插件契约整理为单一来源；游戏相关接口继续由客户端维护。
4. 共享工程内部引用相对共享仓库定位，不依赖父仓库的固定目录名。
5. 共享项目独立构建时只生成自身产物；客户端编号 DLL、资源复制和服务端 Extensions 发布由消费者的打包目标组织。
6. 保持现有 AssemblyName、协议模块名和持久化身份，避免将重命名混入拆仓。

子模块内最近的 Directory.Build.props/targets 可能改变 MSBuild 的导入边界，不能假设父仓库配置自动应用到所有共享项目。共享构建规则放在共享仓库，消费者显式导入必要规则并维护自身打包目标，核对最终求值与复制结果。

protobuf 的 SDK 兼容处理继续放在仓库构建配置；不修改 vendored global.json 或生成源码。保留现有 LiteNetLib 版本与目标资产，不能把源码拆分顺带变成传输库升级。

## 6. 阶段 2：提取共享仓库并验证消费

使用经过审核的路径清单，在临时副本提取历史。清单必须包含共享源、插件共享契约、libs、protobuf gitlink、.gitmodules、构建规则、许可证与必要测试。

不要用旧方案只保留 Common 和 protobuf 的命令直接执行：它会遗漏依赖和契约；移动目录时还要更新 .gitmodules 的 path，并检查原本的链接与相对路径。

在两个消费者的迁移副本加入同一公开共享仓库，规划入口为 Dependencies/Phinix.Common。保留 ProjectReference 方式，更新到实际子模块内工程路径。两端最终可固定不同共享提交，但首轮迁移优先用同一基线减少变量。

在原消费者项目已经完成边界整理后，分别验证：

- 服务端构建仅选择所需 net10.0 项目；不构建游戏相关客户端工程或共享解决方案的所有 TFM。
- 客户端选择 net472 及其游戏工具链；共享子模块能在客户端环境生成相同身份的依赖 DLL。
- 消费者不引用旁边另一个仓库，也不依赖迁移前留在磁盘上的旧目录。
- 关闭或移走旧共享源码副本后仍可构建，防止假成功。
- 构建产物能包含所需运行时依赖；第三方插件不另外分发宿主已提供的契约 DLL。

第三方插件可以引用宿主发布物中的契约程序集，或使用共享源码；本方案不为此追加 NuGet SDK 发布渠道。

## 7. 阶段 3：物理拆离 Client、Server

Client 拆离是三仓目标的必要阶段，不再标为可选。现有远端保留哪一端的历史与身份，在执行前明确；本方案不擅自创建、重命名或推送仓库。

提取时保留端点源码、该端插件、资源、构建配置与发布文件。不要在两端各保留一份共享 Contracts 来临时绕过路径问题。

测试按实际责任归属，而不是按当前目标框架归属：

- Phase35RuntimeTests 目前链接客户端环境服务、NetClient 和 ClientAuthenticator，需要拆分共享、客户端、服务端场景后再迁移。
- ResponsiveUiGeometryTests 归客户端；net10 可执行不意味着它属于服务端。
- 跨端协议测试可在测试工作区按两个消费者的明确提交运行，避免为测试给生产服务端引入客户端源码依赖。
- 必须保留现有场景语义与失败退出码，不能通过删场景获得独立构建。

GitHub Actions 继续只承担服务端 Docker 镜像构建与 Docker Hub 发布，保留当前 docker.yml 的触发、权限、固定 action 版本与标签规则。不恢复客户端 Actions 构建，不新增 NuGet 发布或独立 build/release 双工作流。

工作流递归检出子模块；Dockerfile 的 COPY 路径、构建上下文、.dockerignore 与发布复制目标适配子模块布局。最初的干净 checkout 必须能够打出并启动服务器镜像，官方插件产物完整且不含游戏/Unity 引用 DLL。

## 8. 日常获取与升级流程

以下为规划命令，路径与提交在实际迁移完成后替换。

首次获取：

```sh
git clone --recurse-submodules <CLIENT_OR_SERVER_REPOSITORY_URL>
```

已有克隆在切换分支或拉取包含新子模块指针的提交后：

```sh
git submodule sync --recursive
git submodule update --init --recursive
git submodule status --recursive
```

这会检出消费者记录的提交。不要在日常初始化、CI 或发布构建中使用 git submodule update --remote。

修改共享代码前，在子模块内创建开发分支，而不是将修改留在 detached HEAD：

```sh
git -C Dependencies/Phinix.Common switch -c <COMMON_CHANGE_BRANCH>
```

升级遵循以下顺序：

1. 在共享仓库提交并推送改动，确认引用提交在远端可获取。
2. 消费者检出指定的共享提交并初始化其嵌套子模块。
3. 在客户端或服务端验证实际使用路径与适用的兼容场景。
4. 消费者提交子模块指针及自身实现变更，提交说明写明共享标签/commit、接口变化和关联 PR。
5. 另一端按兼容性需要升级；增加可选字段或其他兼容性改动不要求两端同时发布。

共享 API 破坏性改动先用过渡接口或并存协议保持旧消费者可用，再逐端迁移。不把“两个指针提交已合并”当作全部在线客户端已升级。

## 9. 版本、兼容与回滚

共享仓库使用可读发布标签，消费者记录精确 commit；发布信息写明 client/server/shared 三者的版本或提交。标签提供可读性，gitlink 提供精确锁定，不能靠浮动分支确定发布输入。

分别管理三种兼容性：

- 程序集 API/ABI：已有第三方插件是否仍能加载，新增接口是否需要兼容桥梁。
- 网络协议：protobuf 字段、枚举、模块名、Any descriptor、能力协商及业务确认语义。
- 持久记录：游戏存档、配置、托管与交付记录的读取和恢复。

protobuf 已使用的字段号不能随意改变或重用，删除字段保留编号；提升共享标签不能让旧客户端自动接受新语义。submodule 与 NuGet 都不会自动证明协议兼容。
参考：[Protobuf 字段编号规则](https://protobuf.dev/programming-guides/proto3/#assigning-field-numbers)。

回滚应撤回整个消费者迁移提交，使宿主实现与共享指针成对恢复；不能只切换共享 hash。已发生持久格式或协议迁移时，先确认旧版本能读取和处理新数据，不能把源码回滚当作数据回滚。

旧客户端服务端兼容仍由一个普通插件承接，通用接口放入所属契约。原版旧客户端的交付去重、业务 ACK 和完整物品状态限制，遵循独立兼容评估；拆仓与源码锁定不能消除这些限制。

## 10. 验收清单

以下是待执行检查，不表示已经通过：

- [ ] 共享工程没有对客户端/服务端宿主的反向依赖，游戏接口与共享契约归属清晰。
- [ ] 两端共享契约只有一个源码来源，第三方插件二进制身份保持兼容。
- [ ] 所有 endpoint 链接源码、HintPath、ProjectReference 和显式 Compile 项已更新。
- [ ] 共享仓库保留所需 libs、protobuf 固定提交、.gitmodules、构建规则与许可证。
- [ ] 全新递归克隆能构建各端；不存在依赖未推送共享提交或外部兄弟目录的情况。
- [ ] 服务端仅构建所需 net10.0 项目，并保留一个 Docker Hub 发布工作流。
- [ ] 客户端在实际 net472/Mono 环境及游戏引用条件下验证编译、加载与分发。
- [ ] 测试按责任迁移，原有场景未因拆分而丢失。
- [ ] 适用的新旧端协议组合经过验证；未完成游戏内验证的部分明确记录。
- [ ] Docker 启动与 Chat/Trade 插件加载正常，产物检查符合各端实际布局。
- [ ] 客户端产物内容检查适配新路径，游戏/Unity 引用 DLL 未进入分发包。
- [ ] 每个发布能追溯消费者提交、共享提交和供应商子模块提交。
- [ ] 无第二条 Phinix NuGet 发布渠道、重复契约源码或新增的统一协调仓库。

## 11. 参考

- [设计哲学](../../Design-Philosophy.md)
- [兼容与恢复边界](../../Compatibility-Boundaries.md)
- [服务端 Docker CI](../../CI.md)
- [旧客户端服务端兼容评估](./老客户端服务端兼容插件评估.md)
- [客户端四项基础设施迁移方案](./客户端四项基础设施迁移方案.md)

本次文档调整不代表已经完成拆仓、构建验证、包发布或联机验证。
