# UI/UX R00 / R01 源码基线与能力保留

2026-10-11，Asia/Singapore。本记录以工作区源码及 roadmap §16 为准，审计是历史快照。原始 dirty/HEAD 与构建前 DLL 大小/SHA256 见同目录 `UI-UX-Baseline-2026-10-11.json`；不是正在游戏内加载的 DLL 证明。

## 环境和文件所有权

- Client About 声明 RimWorld 1.6、Harmony；客户端 net472、IMGUI/Verse/Unity 引用。构建工具 dotnet 10.0.112 不是游戏 runtime。
- `GameDlls` 为本地引用布局（目录内忽略文件不由 rg 普通列表显示），实际游戏 patch/Unity/Mono 版本、加载目录、DLL 加载批次均待测；不存在 Client/LoadFolders.xml，不虚构版本加载规则。
- 应用聊天列表显示此前 API/主题会话和审计会话为空闲；本会话串行接管 ClientWidgets、UiTheme、ServerTab、SettingsWindow、presentation 契约/renderer、Client.cs、Example、roadmap。不启动并行写入，不 reset/checkout/rebase，不覆盖远端版本。
- R10–R12 的 page/section/summary/status/action/confirmation、actual-module lifetime、theme snapshot 和 Example proof 已存在，直接扩展。

## 信息架构与保留表

| 表面 / owning code | 当前真实能力与入口 | 新壳层 / 保留要求 | 源码证据 / 游戏证据 |
| --- | --- | --- | --- |
| 主入口 / ServerTabButtonWorker、ServerTab | 主按钮、badge 聚合、任意主 tab；打开/关闭/拖动/resize、尺寸保存、Enter handler | provider 导航、溢出菜单；owner+local metadata ID，旧 provider 使用注册身份；不按翻译排序/识别 | 源码已核对；0/32 tabs、长翻译、不同 UI scale 待测 |
| 侧栏 / IServerSidebarProvider | Chat 成员、Notice 和第三方侧栏；偏好宽度、可折叠声明 | 所有 provider 保留；窄窗始终有 drawer，宽窗全部通过 sidebar overflow 可达；0 provider 隐去 | 原窄窗不可折叠 provider 可能不可达；新路径待游戏验证 |
| 公告 / INoticeBannerProvider | owner 给 CurrentHeight 和 Draw；多个 provider | 有界滚动区域、可收起再展开；失败隔离；无 owner 数据不制造内容 | 原总高度截断后续公告；新路径待测 |
| 设置 / SettingsWindow、Client.DoSettingsWindowContents | 连接快捷表单、完整 mod 设置；任意 quick/full panel；不同保存边界 | 常驻菜单：连接/快捷、完整客户端、主题、插件；完整窗口复用现 contents 和关闭 WriteSettings，不合并保存规则 | 连接/显示名/disabled providers、各保存时机待测 |
| Chat / ChatMainTabProvider、ChatMessageList、UserList | 消息流、回复、@、URL、图片、失败草稿；子串成员搜索、屏蔽与取消、trade 等 owner 菜单 | 保留 rich text/hash 配色/过滤口径/屏蔽组折叠；公共列表字段及 typed owner actions；不加 presence/回执/私信 | 源码核对；发送/回复/@/断线/焦点待测 |
| Inventory / InventoryTab、SettingsPanel、DeliveryChoice | 存储、选择、数量编辑、交付目的地、恢复提示/只读保护、预留 | UI projection 不接管 Thing/账本，不自动确认恢复或归还未知记录 | 整批/codec/恢复失败/重启待测 |
| Store / ManagedPluginStoreView、LocalThemePackagesWindow | 来源、详情、安装/更新/启用意图/运行/待重启、ZIP 管理 | 状态分开；不把安装当启用或应用；现在线功能保持，不新发布 | 当前 T5 实现存在；本轮故障恢复/包事务待测 |
| 主题 / UiTheme、ThemeSettingsWindow、ExportWindow | keys、自定义配色、注册、fallback、unknown、inherit、热应用、draft/preview/apply、Catalog import/export、ZIP 导出 | 独立 Host 编辑入口；同 resolver/registry，新增 role 缺省继承旧 key；不加 Host ZIP 管理页 | 本轮 partial/inherit/unknown/plugin key 待游戏与包测试 |
| Trade / TradeList、TradeWindow、service | 发起、报价更新/确认、库存预留、真实回执；关闭不等于业务取消 | 安全语义门槛先行；托管、Reset、结果未知、交付/恢复风险仍单列；未达门槛不得声称迁移完成 | 历史 runtime 证据仅历史；窗口托管/Reset/断线需独立修复与实机证明 |
| 红包 / Phinix-Legacy-RedPacket | 发送/领取/历史、库存移交与中继确认、内部 1.9 adapter | 维护 adapter；关闭不退款、task/callback 不确认业务 | 未迁移；1.9/本批 Host、接受/拒绝/未知待测 |
| 人才贸易 / Phinix-Legacy-TalentTrade | 人才清单、详情与确认、Pawn 操作、内部 1.9 adapter | Pawn 所有权保留插件，受控自绘不接管业务 | 未迁移；1.9/本批 Host、确认/关闭/失败待测 |
| Example / ExamplePresentation | Host renderer 窗口、普通插件 typed action 贡献、task callback 状态、reset 确认 | 公共契约扩展同批给 Example 消费；不靠私有官方 renderer | 基础历史编译通过；游戏与第二插件真实贡献仍待测 |

## 状态和验收口径

状态只能由 owner 提供：真空列表与零命中、未连接与未登录、缺能力与 provider 故障、已派发等待与拒绝/未知、callback 完成与权威确认分别展示。不从设计图补数据。Host 只提供布局/控件/导航/确认与 view lifetime。

R00 源码、入口、dirty、DLL 基线记录完成；游戏版本/截图/网络流程/实际 DLL 待测。R01 源码信息架构与能力保留表完成；游戏可达性待测。未获得游戏证据不阻塞无业务语义变化的壳层和公共 renderer 实施。后续迁移顺序仍为 Chat → Inventory → Store/主题 → Trade → 红包 → 人才贸易。
