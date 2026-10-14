# 红包页紧凑布局与物品图标（2026-10-14）

用户截图授权提高红包页信息密度，并恢复收到红包的物品图标。只改插件呈现及其公共模型转换，发送/领取确认、状态机、物品聚合/扣取/返还、协议和存档不改。

- `Client/RedPacketPresentation.cs`：去除重复页标题和默认1/1分页；搜索/刷新和领取/详情/设置横向工具栏。左右列表采用公共紧凑行；物品来源和可用量行内显示，剩余数、领取状态及时间连续排列。保留实际到账核对提醒、待处理发送/领取和异常提示；数量/红包个数仍成双列，发送仍经公共确认核对物品及数量。
- 收到红包按 `packet.Template.DefName` 查本地 `ThingDef`，优先本地图标，缺失用 `UnknownItem.uiIcon`。Host `Client/Defs/ThingDefs/UnknownItem.xml` 的 `Things/UnknownItem` 为问号资源；极端Def/贴图缺失再走既有 `BaseContent.BadTex` 兜底。沿用旧 `RedPacketTab.DrawPacketRowText` 与 `RedPacketDetailWindow.DrawItemIcon` 的本地Def/UnknownItem逻辑，不物化Thing、不请求远端贴图。
- `Client/Compatibility/PresentationAdapter.cs`：通过同一公开 `WithImage` 显示行图标；`WithCompactRows`/`WithInlineToolbar` 提供密度。行图标不再只依赖来源徽章，因此没有徽章的收到红包仍有图标。没有私有renderer、新主题/动作系统或新增旧客户端分支。

**源码检查通过**：源码摘要与资源声明、无游戏引用入包、ZIP CRC、diff空白检查。当前变化只用于本地API3包，正式Host pin/API2发布配置未为此改动。

**编译通过**：保留1.6引用下完整客户端及三个独立插件；实际1.7.4977 rev1248引用下26项目net10图。原1.6基线/已有候选保留，未修改protobuf pins。

**白盒通过**：红包34、人才84、商店109真实SDK/模型绑定检查；红包追加公共紧凑行/工具栏绑定检查。双版CLI真实引用预检与37项安装/版本选择/禁用重启/卸载检查通过。这些不是实机领取或GUI执行结论。

本地双版红包0.0.3：`local-compatibility-analysis/redpacket-density/packages/phinix.legacy.redpacket-dual-local-0.0.3.zip`。

SHA256：`bdf4e8802abfdefcc8ca0fd4d97f6ffe1e6e8aa129e6dacfb2beaac94d497d59`。

配合现有Host12（公开紧凑能力），本地导入更新红包并重启；不更新正式商店目录。仅API3版本号本身不能证明早期本地Host已包含这两项新能力，不能与候选09混用。

**游戏待测**：两版页面行密度、UI缩放/长名称、选择与数量编辑、正常/缺失Mod物品图标、公共确认、真实领取到账；未知物品的图标只作展示，不放开原未知模板领取限制。无提交、推送、发布或玩家数据修改。
