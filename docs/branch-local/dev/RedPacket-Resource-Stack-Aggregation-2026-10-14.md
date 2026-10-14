# 红包通用物品聚合：组件准入与跨堆选择

用户截图为原木47与75分别显示“仅此物品堆”，要求同时修复UI与业务聚合。本切片只改独立红包插件的分组/发送准入，使用既有Host公共呈现，不修改Inventory/Trade协议、账本或R44。

## 依据与修复

真实1.7 Core Def：WoodLog继承ResourceVerbBase，再继承ResourceBase；组件包含CompForbiddable与CompEquippable。原`Client/RedPacketStackTemplate.cs:IsKnownPerUnitStack`把带字段且有保存行为的CompEquippable视为未知组件，直接输出单堆行，未进入完整状态比较。

两版游戏DLL的CompEquippable均有VerbTracker，tracker的私有`verbs`字段为延迟初始化状态；PostExposeData保存该tracker，VerbTracker.ExposeData保存verbs。不能把有战斗状态的CompEquippable整体列为无状态，也不能删除verbs保存字段来强行聚合。实际1.7 DLL构造探针确认默认tracker未初始化；该组件没有重写PostSplitOff/PreAbsorbStack/AllowStackWith。

`RedPacketStackTemplate.cs:269`加入`IsUnusedEquippable`：仅接受准确的基础CompEquippable类型且其tracker未初始化verbs。反射只读私有字段，不调用会初始化的AllVerbs；字段缺失时拒绝聚合。准入后仍须完整规范化保存状态相同、双向CanStackWith成立。未知组件、已初始化战斗状态或保存属性差异仍保留单堆/分组。

UI分组和发送Capture/PreparePhysical使用同一准入规则。无新UI合计层，无业务数量捷径；PreparePhysical仍按原始Thing选择来源，PopSelectedWithOrigins原有扣取/返还路径保留。

## 验证

- 38项修复算法用例通过。新增跨定义通用矩阵覆盖WoodLog、Steel、Cloth、MedicineHerbal、RawRice、第三方资源名，以及无组件/通用无状态组件/未初始化装备组件共18组合；确认名称不影响准入，不同定义/耐久不跨组。原新增三项：47+75合为122；选择100覆盖两来源且准备阶段47/75不变；初始化战斗状态保留单堆；不同保存属性不合并。
- 原分片分组/发送性能用例通过，预算与1000堆上限保留。
- 1.6实际引用下红包Contracts/Client隔离编译通过；1.7完整26项目图编译通过。
- 两版真实DLL包身份/API3/全部引用校验通过；独立双版包37项安装/选择/禁用重启/卸载白盒通过。
- 两版DLL IL与Core Def仅本地分析，不提交游戏反编译代码或DLL。
- 游戏UI合计、实际发送/领取/失败返还仍待测；算法stub不能代替实机。

## 本地候选

Host继续候选09；人才/Example不用更新。红包临时产品版本升0.0.1，两版共用一个ZIP（DLL CLR身份维持当前系列），不覆盖原0.0.0制品：

`/home/hunyuan2333/Phinix/local-compatibility-analysis/redpacket-aggregation/packages/phinix.legacy.redpacket-dual-local-0.0.1.zip`

SHA256 `201a2803c1433ab59daaad9877f9ae7edd7edcfbfd4ad041962268c5cb8bf082`。

开发模式本地导入更新红包，确认后重启，再刷新可发送物品。用测试存档将属性完全相同的原木分为47/75，预期一行122；发送100，核对原堆共余22及领取到账100。不仅看UI合计，还核对库存/地图数量。另测不同耐久、品质、颜色/保存状态不会混合，失败或取消后原物品可正确返还。

本切片未提交、推送、发布或改正式存档；source-manifest同步当前源文件与测试摘要，check-source通过。

## 用户纠正：按通用规则处理

用户强调不只原木，不做物品特异性修复。实际实现没有WoodLog/defName名单、特定标签或物品专用发送分支；资源例子用于复现，组件准入适用于所有Thing定义。已有CoarseKey、完整ComparableState、双向CanStackWith和同一Capture检查是实际通用规则。通用矩阵扩充只改测试/记录，0.0.1运行DLL/ZIP不变，无需再下载。

可聚合属性包括定义、材质、耐久、品质、组件类型和完整保存状态；不把相同名称/外观视为等价。无法确认逐件状态/拆分语义的组件仍限制单堆；对第三方有状态组件的支持需要按组件语义验证，不能为满足UI合计而删除状态。库存条目的stateful opaque语义单独保留，不把地图堆聚合误说为已改库存codec存储格式。
