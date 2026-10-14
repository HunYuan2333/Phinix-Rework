# R17-23 中立呈现程序集首切片（2026-10-14）

设计哲学 §1.1～1.3、§2.2 要求同一公共呈现能力与单向依赖。本轮先分离真实可中立的类型，保留 namespace、renderer、动作权威、主题 owner；没有搬动 Thing/Pawn、协议、报价或账本。R17-23 的完整 game/layout/API 3.0 绑定仍待后续闭合。

## 物理边界

新增 `Client/PresentationContracts/ClientPresentation.Contracts.csproj`，仅 ProjectReference Utils，无 Host、旧 Abstractions、游戏、Unity、Harmony 引用。原项目从 Compile 中移除下列定义，并引用新项目：

| 新源码位置 | 内容 | 本轮变动 |
| --- | --- | --- |
| `UI/IClientPresentationService.cs` | service/session、普通模型、Region context、菜单、确认 | 原源码逐字迁出，无新 renderer |
| `UI/ClientPresentationValues.cs` | Bounds、RGBA、ClientButtonKind | 原值保留；按钮枚举从原 native ClientWidgets.cs 移入，枚举项/数值不变 |
| `UI/IClientPresentationImage.cs` | image/lease/size | 原源码逐字迁出，无 native handle |
| `UI/IClientTheme.cs` | 主题注册、reader、catalog、snapshot、draft、preview | 原源码逐字迁出 |
| `UI/IClientThemePackages.cs` | 本地主题包与 exporter | 原源码逐字迁出，仍依赖 Utils 的主题包模型 |
| `UI/ClientThemePresentationActionContext.cs` | 第三方主题动作事实 | 原源码逐字迁出 |
| `Framework/IClientExtensionPoints.cs` | 同一贡献点/动作合同 | 原源码逐字迁出，不增加第二动作 registry |

与上一窗口切片的 source-fingerprints 对照，六个迁出文件 SHA 相同；Values 只增加原 ClientButtonKind 定义。内部 ID validator 仍是原实现：新程序集通过 `Properties/AssemblyInfo.cs` 的 InternalsVisibleTo 让既有通用 ClientPresentationMainTab 使用它，避免公开额外 validator 或复制验证规则。该权限只给游戏抽象程序集使用内部通用实现，没有业务 owner 白名单或官方私有 renderer；neutral metadata 无对该程序集的 AssemblyRef。

`ClientExtensionAbstractions` 保持原程序集名，并依赖新合同；Game bridge、widgets、原生 shell/settings/sound 和 UnknownItem 仍在该项目。这些仍有 Unity/Verse/Thing 依赖，后续须明确分层；不声称整个 SDK 已中立，不借 UI 迁移改变 UnknownItem 的 CLR/Scribe 类型归属。

## 消费者与绑定修正

Host、原抽象项目与相关客户端/合同/测试 ProjectReference 同步；两 solution 增加新项目与原合同对应的配置映射。红包、人才、Example 使用 Host-owned DLL 的 Reference，Private=false，插件不得自带共享 DLL。Host 的输出规则加入 `Common/Assemblies/10-ClientPresentation.Contracts.dll`、源码参考目录，artifact 检查同步；本轮没有执行生产 Copy target 或生成正式包。

两个插件原反射 adapter 假定 presentation/theme/Game 类型全部属于 IClientSessionContext.Assembly，移动后会在实际运行绑定时失效，即使生产工程编译通过。现已按真实归属绑定：

- presentation/theme/model 构造来自 `typeof(IClientPresentationService/IClientTheme).Assembly`；
- ClientTextureImage、ClientWidgets 与原生 provider wrapper 来自游戏抽象程序集；
- 颜色转换定位共享 ClientColorValues，参数使用新合同 ClientRgba；
- 没有 AppDomain 全扫描取首个同名类型，没有旧客户端新增 fallback。内部历史分支保留，但当前新源码/新包不承诺旧客户端加载。

Example pack.py 的公开引用获取、build 参数和 packager host-assembly 加入新 DLL；两个插件 pack.py 本来遍历 Host 公共 DLL，无需新专属分支。三仓库源码 member gate 改到新源路径，共享 DLL ZIP 禁入规则补新名字，测试 fixture 使用当前 WithImage。正式 Host pin 仍为原发布基线，成员 gate 对当前源通过不代表对旧 pin 的发布构建通过。两插件 source-manifest 同步本轮实际修改，校验通过。

## 框架、CLR 身份与加载布局决定

普通合同继续与 Utils/IPhinixExtensionModule 协作，不为本切片重复一套 owner/protocol 类型。新生产项目默认 net472；可通过 PhinixPresentationTargetFramework 明确选择框架，隔离检查按游戏引用分别编译 net472/net10.0。由于 Utils 当前基础设施目标与完整联合图尚未闭合，暂不把 netstandard2.0 或某个单 DLL 的双 runtime 可载入性写成事实。

本轮真实生成新 DLL 身份为 **ClientPresentation.Contracts 1.0.0.0**（SDK 默认新程序集版本，未发布），1.6/1.7 MVID 分别 `bdfee77d-d4f8-4fb5-b159-401cf319912a`、`ee37f270-49e5-4111-b797-39c497a246f7`。原 API/CLR 身份、产品版本、manifest 范围与 Host pin 未切；类型移入新程序集是 ABI 破坏性变化，依旧必须按 API 3.0 联合收口，不能以新程序集默认版本当作兼容承诺。原正式包与此中间 DLL 不可混装。

每个游戏进程只加载一份合同身份和对应 backend。当前 Host Common Copy 规则服务已有 1.6 工程；不能拿本轮 net10.0 DLL 覆盖 Common 并宣称两版均能加载。1.7 的正式版本目录/LoadFolders、两版公共 DLL 选择、游戏抽象与基础设施的框架/绑定都属于下一联合布局切片。SDK 项目新增依赖后，其现有 packages.lock.json 图也需要在隔离完整 restore 中核对/更新；本轮没有假写 lock 图或修改 Autofac/protobuf pins，也未运行正式 locked release build。

## 检查矩阵

当前安装仍是 1.6.4871 rev598。本轮 1.6 编译使用原 GameDlls 的 1.6.4633 rev1261；1.7 使用缓存 1.7.4977 rev926，精确引用 SHA/MVID 在本轮 input-fingerprints 中。不是当前 1.7 游戏运行。

| 范围 | 状态 | 证据/限制 |
| --- | --- | --- |
| 类型归属、消费者、原生残留 | 源码检查 | 无新 UI 业务语义；六文件逐字迁出；Game 边界仍待拆分 |
| 两版选定工程 | 编译通过 | 每版 23 项（新增合同）；新合同构建只提供 Utils/Protobuf 编译引用，0 warnings/0 errors；其余固定基础设施从原 Output seed |
| 实际 DLL 合同与六套回归 | 白盒通过 | 每版合同 62；Trade 204、Inventory adapter 19、原 Inventory 19 场景、Inventory composition 84、Example lifecycle 45；合同无 Verse/Unity/Host AssemblyRef，普通合同同一物理程序集，旧 SDK 无重复定义 |
| 实际插件 DLL 反射/呈现/主题 | 编译通过、白盒通过 | 两版每版红包 32、人才 84；fake Host/session/registry 配真实 model/bridge，包含真实跨程序集颜色转换、注册/释放；不执行原生 UI |
| 商店组合 | 编译通过、白盒通过 | 两版各 109；fake services 配真实商店 DLL，非真实下载/窗口 |
| renderer/主题 source + stub | 白盒通过 | 本轮重跑 308 / 73；不证明原生 GUI/字体/IME/GPU |
| CI 源码门禁/算法 | 白盒通过 | 红包/人才各 18、Example 16 模拟单测；当前源 member gate 通过；两插件 check-source 通过 |
| 完整 locked 构建、API 3.0 包/profile/LoadFolders | 待测 | 正式身份/pin/范围未切，没有发布产物或包验收 |
| RimWorld 内加载与 UI/业务路径 | 待测 | 游戏通过：无；不是新版后端完成结论 |

输出：`/tmp/phinix-r17-23-contracts/final-1.6`、`final-1.7`，source/input fingerprints、build logs、verification 保留。先前 `1.7` 与 `closed-1.7` 为暴露隐含依赖的未闭合尝试，不作为最终通过证据。合同检查同时捕获 native ButtonKind 的隐含依赖，解决后新程序集用最小引用编译；没有靠全量游戏引用掩盖它。

重跑入口：`Tests/TradeCompositionRuntimeTests/run-isolated.py` 按真实 version/game/harmony/autofac 指定新 output；`Tests/PresentationValuesRuntimeTests/run-consumer-probes.py --references <joint-output>/run --output <fresh>` 跑两个实际插件和商店；两个现有 source/stub runner 用新 output。consumer harness 的编译引用 Private=false，运行使用本轮 DLL 的本地 symlink、独立生成 runtimeconfig，移除自身生成 deps.json 以按既有 isolated harness 解析联合目录；不是游戏加载器行为证据。所有 probe 在系统 .NET 10 运行。

## 后续与游戏测试

下一切片先闭合完整工程 restore/锁图与版本加载布局，再拆游戏实现的物理依赖和两版 backend；正式 API 3.0 身份/profile 与候选包同批处理。不要为消除编译错误重写交易、codec、Pawn 返还或账本。

R17-33 首个可运行联合候选再主动通知用户，提供准确游戏/Harmony/Host/插件组合、hash、隔离安装与最小场景。当前不安装这些中间 DLL，原生焦点、窗口、IME、滚动、图像、切档及联机/保存仍待实机。无提交、推送、发布、原日志入库或正式存档修改。
