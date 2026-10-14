# R17-11 / R17-12：Inventory 游戏实现归属与 Trade 接入审查

2026-10-13。源码与隔离检查已完成；游戏待测，未部署或发布。

## 边界整理

`InventoryDropDelivery` 原在 `InventoryContracts.cs` 中直接执行 RimWorld 落点选择、投递和 custody 判定。将这段实现原样移到 `Extensions/Inventory/GameAdapter/InventoryDropDelivery.cs`，编译为一份 `InventoryExtension.GameAdapter.dll`。它仅依赖 Inventory Contracts 与游戏，不依赖 Host、Inventory.Client、Trade.Client 或红包。没有把业务物化/返还权威搬到 Host，也没有增加 provider 注册或新业务服务。

选择独立程序集是因为 Inventory、Trade 与独立红包仓库共同调用这段游戏代码：留在 Contracts 会继续混合定义与实现；移入 Inventory.Client 则会引入对插件客户端实现的额外依赖。只拆这段已共享的游戏实现，不把所有业务对象抽象成 Host DTO，也不拆整套账本。

`InventoryDeliveryResult`、`InventoryMaterializationResult`、`IInventoryCodec` 归到单独的 `Contracts/InventoryGameContracts.cs`，仍编译进原 `InventoryExtension.dll`，保留原类型身份与签名。`Thing`、`Map`、`IntVec3` 在这里是插件自己的游戏边界；普通 Host 呈现仍不能因此拥有这些对象。中立库存投影与账本契约文件不再含投递算法。

helper 类体迁移前后逐字一致，SHA256：`4fdf790a09f440166c023e857057b1647b3b8b333b810bf9eaf7f982d479b64e`。实际调用、落点/gravship 检查、整批预检、异常后 custody 检查和 Delivered/Rejected/Uncertain 规则没有改写。`InventoryContract.Version` 保持 6；wire、存档键和确认标准未改。

## 消费者与权威

| 消费者 / 源码位置 | 接入与保留规则 | 状态 |
|---|---|---|
| Inventory `InventoryBatchExtraction.cs:41`、`BuiltInInventoryClientExtension.cs:608` | adapter 选择落点；`:653` 仍调用 entry codec 的 DeliverVerified；`:677` 异常时检查 custody；结果不明保留原锁定状态 | 源码检查、两版编译通过、现有非 GUI 回归通过 |
| Trade `TradeInventoryIntegration.cs:65`、`:220`、`:225` | codec 与整批直投共享 adapter；Rejected 才丢弃未被接管对象，Uncertain 不当作成功或自动恢复 | 源码检查、两版编译通过 |
| Trade `ClientTradeUiHostContext.cs:134` | Trade 自己决定地图并调用 adapter；Host 不选择业务目标或管理 Thing | 源码检查、两版编译通过 |
| 红包 `RedPacketInventoryCodec.cs:140`、`RedPacketStateMachine.cs:1436`、`:1442` | 物化与余额仍属红包；只新增 compile-only adapter 引用 | 源码检查、两版编译通过、源码摘要检查通过；红包业务游戏待测 |
| 人才贸易、Example | 没有此 helper 的调用，不新增引用 | 源码检查、两版编译通过 |

主包提供 adapter 单一副本。同步两个 solution、Inventory/Trade ProjectReference 与主包 artifact 检查。红包 Reference 设置 `Private=false`，不得把主包共享 DLL 再打进插件 ZIP；更新其 csproj 的 source-manifest 摘要，没有改正式 Host pin、版本或发布记录。

红包新 `RequireInventoryGameAdapter` 构建门槛：完整本地包有 adapter 时通过，旧正式 Output 缺失时明确失败。避免仅出现“引用未找到”的 MSBuild warning 后，编译器悄悄绑定旧 Contracts 中同名 helper。当前固定正式 Host 还没有 adapter，迁移源码不能沿用该旧 pin 发布；最终 pin 与 API/包范围在联合冻结阶段处理，不伪造提交或绕过检查。

## R17-12 源码结论

`Trade/Contracts/ClientTradeContracts.cs:62` 的 `ITradeUiHostContext` 由 Trade Contracts 定义、Trade.Client 的 `ClientTradeUiHostContext` 实现；名称中的 Host 不是 Host 主程序集依赖证据。`DropPods(IEnumerable<Thing>)` / `LookTargets` 是 Trade 自身的游戏接入，不搬到通用 Host、不强行改成丢失 custody 的 DTO。

`PhinixClient.Trade` 的领域类型来自链接到 Trade Contracts 的 `Dependencies/Phinix.Common/Extensions/Trade/Contracts/TradeDomainContracts.cs`。历史 namespace 也不代表 Host 主程序集拥有交易。真实编译 DLL 的 Host 没有 Trade/Inventory 业务引用，已有白盒继续检查这一点。

`TradeThingStateSerializer.cs:85`～`:111` 的 LoadingVars、crossRefs、PostLoadInit、FinalizeLoading 仍属于 Trade 的物化路径；`TradeOfferEscrow.cs:14` 的原 Game scope 仍属于 Trade owner。本切片不改它们。1.7 编译只能证明所调用签名仍存在，不能证明任意 ThingComp/Pawn 往返或切档交付行为一致。R17-12 边界归属审查完成；行为兼容仍交给 P6 的真实游戏检查，R44 后续增强未纳入。

## 验证证据与限制

引用指纹与 R17-10 记录一致：1.6 GameDlls `5b8c8e…`；当前 1.7.4977 rev926 Assembly-CSharp `be2d5a…`、UnityStubs `49badc…`；本地 Harmony 3 `94dd92…`。验证后再次核对游戏 DLL 哈希未变化。系统 .NET 10 白盒不等于 Unity/游戏 Waylith CLR 验收。

| 检查 | 1.6 | 1.7.4977 | 范围 |
|---|---|---|---|
| 20 个选定项目隔离重编 | 编译通过 | 编译通过 | R17-10 的 16 项 + GameAdapter、其测试、Inventory 原运行测试和 composition 测试；基础通信/protobuf 等仍使用已有 Output，不是完整发布构建图 |
| TradeCompositionRuntimeTests | 白盒通过，204 断言 | 白盒通过，204 断言 | 原切片与 Trade 生命周期/关联/原 Game 边界回归 |
| InventoryGameAdapterRuntimeTests | 白盒通过，19 断言 | 白盒通过，19 断言 | 真实程序集归属/引用；缺失目标、无 custody、缺失对象、持有对象但非目标地图、部分 custody 的拒绝/不确定判定 |
| InventoryRuntimeTests | 白盒通过，19 组场景 | 白盒通过，19 组场景 | 批量提取、预留、分组、组件快照、journal 等原场景 |
| InventoryCompositionRuntimeTests | 白盒通过，84 断言 | 白盒通过，84 断言 | 真实模块、呈现归属、注册失败清理、生命周期与隔离 journal 测试 |
| 原 MSBuild adapter 复制 target | 白盒通过 | 待测 | 在隔离目录复制当前 1.6 分析产物，SHA 与来源相同；不是完整包验收 |
| 主包目录清理测试 | 白盒通过，2 测试 | 同一无游戏检查 | 保留共享 adapter 与其他所有者文件 |
| 红包 source check / 缺失引用门槛 | 白盒通过 | 同一无游戏检查 | manifest 校验；旧包拒绝、新本地输入允许 |
| 实际落舱、Rejected 返还、Uncertain 保留、gravship、Scribe、切档 | 待测 | 待测 | 游戏通过：无 |

新 custody 夹具使用真实 Thing / ThingOwner 类型，但以反射设置 holdingOwner，避免 TryAdd 通知所需的游戏 tick manager；ThingDef 使用未初始化实例避免 Unity 资源加载。它测试 InspectCustody，不测试真实转移、native drop-pod、GPU、Def 初始化或 Delivered 正向落舱。第一次夹具运行的资源加载失败属于已定位的无游戏环境限制；未吞异常、未修改生产代码来通过测试。

复现沿用 `Tests/TradeCompositionRuntimeTests/run-isolated.py` 的 R17-10 命令，只把输出改为新的 `/tmp/phinix-r17-11/repro-1.6`、`/tmp/phinix-r17-11/repro-1.7`。当前工具逐项运行四套测试，生成 source/input fingerprints、build logs、每套 test log 与 verification.json。它不会执行正式项目 Build/发布 targets；验证原复制 target 的独立操作也只写 `/tmp/phinix-r17-11/package-target`。

## 交付门槛与下一步

helper 的 namespace/方法保持，但程序集身份从 InventoryExtension 改为 InventoryExtension.GameAdapter，仍是 ABI 破坏性变更。新契约、所有调用方、新 DLL 与 API/包身份须联合部署；没有旧客户端兼容分支或 forwarder。当前不交付中间 DLL 作游戏测试。

本程序集是 Inventory 共享游戏接入，**不是已经实现的 1.6 renderer adapter**。当前两版编译同一段经检查仍存在的游戏调用；只有实际发现游戏行为差异时才分具体版本实现。

P1 的源代码归属与非 GUI 检查现在闭合，游戏与完整包仍待测。下一切片进入 P2，收束实际公共呈现的 Rect/Color/image/input 边界，并统一决定新 API 身份；再接入 1.7 后端和专门的 1.6 renderer adapter。届时提供可运行联合候选，才请用户开展首轮窗口/输入/主题游戏验收。
