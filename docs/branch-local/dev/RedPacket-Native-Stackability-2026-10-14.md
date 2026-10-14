# 红包按游戏堆叠规则分组与单模板补齐

用户最终明确：游戏中可堆叠的物品按游戏规则处理；游戏中不可堆叠的即使同品质/同物品仍单独。数量不足时允许用数量最多的状态模板补齐指定发送数量；武器护甲按实际不可堆叠规则保护。本切片替代之前地图分组的组件/准确类型白名单与完整状态分行规则。

## DLL依据与判定

两版真实Assembly-CSharp IL确认：

- `Verse.Thing.CanStackWith(Thing)`检查有效Item、非遗物、同定义和同材质。
- `Verse.ThingWithComps.CanStackWith(Thing)`继续调用组件`AllowStackWith`；品质及第三方组件限制来自游戏自己的virtual/Harmony路径。
- `CanStackWith`没有检查`def.stackLimit`，不能单独用于认定装备可以实际堆叠。最终条件是双方容量>1并且双向CanStackWith成立。容量为1始终单独，即使CanStackWith因同品质返回true。

`RedPacketStackTemplate.GameCanGroup`统一这个条件。CoarseKey只用定义/材质缩小候选；不再拿准确类型、额外耐久/品质字段或组件白名单提前排除。品质等条件交给游戏；不按WoodLog/Food/Medicine名单实现。

## UI与业务

`GroupPhysicalSteps`把游戏允许互相堆叠的物理来源合为一行，保留全部Thing引用。完整Scribe状态仍用于识别数量最多的模板，哈希只是索引，实际状态字符串继续比较，防碰撞。成组时逐来源双向检查，复杂组件不采用只看代表堆的捷径；比较分片yield，保留单帧工作预算。

`PreparePhysical`发送时重新读取当前数量/状态并复查实际选中来源的游戏堆叠条件。按完整状态累计数量，选合计最多的状态（不是单个最大物品堆）；相同合计按既有稳定顺序选择。同状态来源优先，充足时不用其他状态；不足时从其余兼容来源补齐。完整可用总量不足则拒绝，不创造额外数量。每份只使用一个现有Scribe模板，最终指定数量统一恢复为所选模板属性。

原始Thing在准备阶段不改属性、不吸收/销毁；实际扣取仍是原PopSelectedWithOrigins。StoredThings持有实际扣取来源，失败/取消/超时的原物品返还路径保留。接收方的状态统一是用户明确授权的行为，不宣称保留补充来源状态。

混合状态行标记“补齐数量时按数量最多的状态发送”。不新增Host私有renderer、业务权威或第二套动作系统。

严格`Capture`及库存opaque条目格式暂保留，区别于这里的地图来源发送策略；不把此切片宣称为已重构Inventory存储格式。UI解析失败/超出检查预算的来源仍单独列出，不静默丢行。单包跨来源上限1000及展开状态预算保留。

## main协议与远端历史

已在/tmp裸仓库读取GitHub main，未更新工作区分支：主仓库远端HEAD c6988e5da255c40d201a5db35d40fe7bdbc3d4d1；独立红包远端HEAD 3aeedd7e21c7698d4cf32a78a7c7623b3607e691。

2026-05的最早RedPacketClientExtension是示例消息，不是真实物品红包。真实旧红包入口为主仓库[1f729d0](https://github.com/HunYuan2333/Phinix-Rework/commit/1f729d00fc7556d614ca6640e7bbb2aca4b6ee07)：UI用StackedThings.GroupThings/CanStackWith合计，发送取第一来源基础快照、接收按该基础模板创建物品，完整组件状态没有传输，不能直接照搬状态丢失行为。

[2026-09-28 a24bf55](https://github.com/HunYuan2333/Phinix-Rework/commit/a24bf551830a1af96abc5d18999a8d129a6795e8)开始有StateRequired/Scribe状态分片、codec/hash/stackCount校验。独立仓库[2026-10-06 e29262f](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket/commit/e29262feb2c2b76fbd6f0c1489b0a3f062a5cfed)首次提取时已经包含状态传输。[ae5d95f](https://github.com/HunYuan2333/Phinix-Legacy-RedPacket/commit/ae5d95fbafdd3460a5fbcd7dbc47bb92941a1d7e)进一步改为拆分前抓取完整来源状态。

用户随后明确以当前main为基线，不必重点兼容最早老插件。本实现沿用一个红包/一个模板/现有CREATE与状态codec字段，未引入多模板或多个子红包、未改协议与确认规则。网络main互通尚需实际两端测试；不恢复旧Host API兼容分支。

## 验证与候选

- 42项修复用例：容量1同品质装备单独；native veto不可绕过；药品派生类/食物组件进入通用分组；最大合计状态优先、跨堆补齐、同状态优先、原数量/状态不变、预算/指纹碰撞/单堆选择/重新检查通过。
- 性能分片用例通过；这是stub算法证据，游戏实际AllowStackWith开销仍需实测。
- 1.6红包真实引用隔离编译、1.7完整26项目图编译通过。
- 双版包静态身份/实际Host/Game引用检查通过；双版安装/选择/禁用重启/卸载37项通过。
- source/localization摘要与check-source通过。无GameDLL、反编译源码、玩家日志、私有编译引用进入包或Git。

Host继续候选09；独立红包本地版本0.0.2，保留0.0.0/0.0.1旧制品：
`/home/hunyuan2333/Phinix/local-compatibility-analysis/redpacket-native-stack/packages/phinix.legacy.redpacket-dual-local-0.0.2.zip`
SHA256 `ef17add1affc02bf893cd970483106cb76692d70fabe1563867d2c5670d86c52`。

游戏待测：本地导入更新红包并重启，刷新物品列表；食品/药品跨堆发送50，核对原地图准确扣50、只出现一个红包、领取共50；两种状态例如A20+A20+B35选择50应使用A模板（40>35），来源不发送时仍保留原状态。容量1武器/护甲同品质仍分别一项。测试取消/超时返还及1.6/1.7切换。使用测试存档，未修改正式存档、未提交推送或发布。
