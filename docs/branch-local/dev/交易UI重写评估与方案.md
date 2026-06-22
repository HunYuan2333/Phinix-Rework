# 交易 UI 重写评估与方案

> 2026-06-21，基于 Trade 客户端代码审计 + 聊天 UI 重构经验。
>
> **本文档是评估文档，不是已实施记录。**

---

## 0. 现状评估

### 0.1 代码概况

| 文件 | 行数 | 职责 | 问题 |
|------|------|------|------|
| `TradeWindow.cs` | 656 | 主交易窗口（`Window` 子类） | 纯手算 Rect，布局硬编码，唯一的 `Color.red` 硬编码 |
| `TradeList.cs` | 270 | 活动交易列表（Tab 内容） | 隔行高亮用 `DrawHighlight`，显示名变更有 bug（`tradesChanged = matchIndex >= 0`） |
| `TradeMainTabProvider.cs` | 28 | Tab 注册壳 | 无问题 |
| `TradeSettingsPanelProvider.cs` | 64 | 设置面板 | 无问题，已用 `Listing_Standard` |
| `PhinixDefaultTradeBehaviour.cs` | 249 | 交易 UX 行为（信件/空投） | 非文件 UI，不改 |
| `StackedThings.cs` | 235 | 物品分组/选择 | `DeleteSelected` 有潜在 bug（减 0），自标注 hack |
| `TradeItemConverter.cs` | 173 | Verse ↔ 快照转换 | 非文件 UI，不改 |

### 0.2 硬编码颜色

| 位置 | 代码 | 用途 |
|------|------|------|
| `TradeWindow.cs:301` | `GUI.color = Color.red` | 取消按钮红色 |

**仅此一处。** 其余全部依赖 RimWorld 默认主题（`Widgets.DrawHighlight`、`Widgets.DrawMenuSection`、`Widgets.DrawOptionBackground`、`WidgetsWork.WorkBoxCheckTex`）。

### 0.3 布局方式

`TradeWindow` 用纯手算 Rect——几十个常量 + 嵌套 `new Rect(...)`。没有用 `Listing_Standard` 或容器组件。窗口固定 `1000×750`，两列 400px 报价栏 + 中间按钮列 + 底部搜索+可用物品。

### 0.4 已知 bug / 技术债

| 问题 | 文件:行 | 严重度 |
|------|---------|--------|
| `tradesChanged = matchIndex >= 0` 用循环后的 matchIndex，可能导致用户名变更后列表不刷新 | `TradeList.cs:266` | 中 |
| `StackedThings.DeleteSelected` 减 0（`remainingThings` 已被置 0 后才减） | `StackedThings.cs:164` | 低（未被 UI 调用） |
| `LegacyProtoThingPayload` DataContract 在两个文件里重复定义 | `FrameworkTradeItemPayloadCodec` + `DefaultLegacyTradeItemCodec` | 低（可维护性） |
| `Phinix_trade_sortByLabel` 翻译键定义但从未使用（死键） | 语言文件 | 低 |
| `Phinix_tabs_trades` / `Phinix_modSettings_*` 不在 TradeExtension.xml 里 | 语言文件 | 低 |

### 0.5 与聊天 UI 重构的差异

| 维度 | 聊天 | 交易 |
|------|------|------|
| 硬编码颜色 | 多处 `new Color(...)` | 仅 1 处 `Color.red` |
| 布局复杂度 | 单列消息流 | 双列报价 + 中间按钮 + 底部物品列表 |
| 交互复杂度 | 低（只读+右键菜单） | 高（增减物品、确认、搜索、六按钮调数量） |
| 主题迁移收益 | 高（大量颜色需统一） | 低（只有一处红色） |
| 重写风险 | 低（渲染逻辑独立） | 中（涉及物品操作、网络同步、确认状态机） |

---

## 1. 重写目标

### 1.1 必须改的

1. **`Color.red` → 主题化**：取消按钮用 `IUiTheme.Error` 色，注册到 `TradeTheme`
2. **`TradeList` 的用户名 bug**：修复 `tradesChanged` 逻辑
3. **死键清理**：移除 `Phinix_trade_sortByLabel`
4. **交易主题色注册**：Trade 扩展在 `Register()` 阶段注册自己的业务颜色到 `IUiTheme`

### 1.2 值得改的

5. **报价栏视觉层次**：当前两列报价栏是白板+标题+列表，缺乏视觉分区。加面板背景 + 顶部色条区分己方/对方
6. **确认状态可视化**：当前用 work-tab 复选框，不直观。改为状态徽章（✓ 已确认 / ○ 未确认），己方用按钮、对方用标签
7. **物品列表行间距**：当前 30px 行高 + ±100/±10/±1 六按钮挤在一行，小屏幕溢出。改为图标+名称+数量输入框+滑块或右键菜单
8. **中间按钮列**：更新/重置/取消三个按钮从底部堆叠，视觉重心偏下。改为底部水平排列
9. **搜索栏**：当前在底部可用物品上方，位置不显眼。移到可用物品列表顶部内嵌
10. **空状态**：报价栏为空时显示引导文字（"点击下方物品添加到你的报价"）

### 1.3 不改的

- **物品编码/解码逻辑**（`TradeItemConverter`、`StackedThings` 的 `PopSelected`/`GroupThings`）
- **网络通信**（`FrameworkClientTradeServiceAdapter`、`PhinixFrameworkTradeClientService`）
- **交易状态机**（`PhinixFrameworkTradeClientRepository`、服务端 `PhinixFrameworkTradeStore`）
- **Legacy 适配**（`FrameworkLegacyTradeClientAdapter`）
- **`PhinixDefaultTradeBehaviour`**（信件/空投逻辑）
- **`TradeSettingsPanelProvider`**（已用 `Listing_Standard`，无硬编码颜色）

---

## 2. 主题色设计

### 2.1 Trade 业务颜色

| key | 用途 | 默认 RGBA | 对应聊天色 |
|-----|------|-----------|-----------|
| `trade.ourOfferAccent` | 己方报价栏顶部色条 | `(0.30, 0.65, 0.35, 0.70)` | 和 Success 绿呼应——"你的东西" |
| `trade.theirOfferAccent` | 对方报价栏顶部色条 | `(0.45, 0.75, 1.00, 0.70)` | 和 MentionText 蓝呼应——"对方的东西" |
| `trade.ourOfferBg` | 己方报价栏背景 | `(0.30, 0.65, 0.35, 0.04)` | 极淡绿 |
| `trade.theirOfferBg` | 对方报价栏背景 | `(0.45, 0.75, 1.00, 0.04)` | 极淡蓝 |
| `trade.cancelButton` | 取消按钮文字色 | `(0.85, 0.30, 0.25, 1.00)` | 和 Error 一致 |
| `trade.acceptedBadge` | "已确认"徽章色 | `(0.30, 0.65, 0.35, 1.00)` | 绿 |
| `trade.pendingBadge` | "未确认"徽章色 | `(0.55, 0.52, 0.48, 0.80)` | 暖灰 |
| `trade.rowHoverBg` | 物品行 hover 背景 | `(1.00, 1.00, 1.00, 0.04)` | 和聊天一致 |
| `trade.panelBg` | 面板背景 | `(1.00, 1.00, 1.00, 0.03)` | 极淡白叠加 |
| `trade.searchPlaceholder` | 搜索框空提示色 | `(0.55, 0.52, 0.48, 0.50)` | 暖灰半透明 |

### 2.2 设计理念

- **己方=绿**：和 RimWorld 绿信、聊天 Success 一致——"这是你的，安全的"
- **对方=蓝**：和 RimWorld 蓝信、聊天 Mention 一致——"这是外来的，需要注意"
- **取消=红**：和 RimWorld 红信、聊天 Error 一致——"危险操作"
- **背景 alpha ≤ 0.04**：不遮挡底层游戏画面，和聊天主题保持一致的透明度层次

### 2.3 TradeTheme 静态缓存

和 `ChatTheme` 同构——`internal static class TradeTheme`，`Refresh(IUiTheme)` 从 `IUiTheme` 读一次存入 static 字段，Draw 路径零分配。

---

## 3. UI 重设计

### 3.1 目标布局

```
┌──────────────────────────────────────────────────────────┐
│                    与 Player1 交易                        │ ← 标题
├────────────────────┬──────────┬──────────────────────────┤
│ ▌ 己方清单          │          │ ▌ 对方清单                │
│                    │          │                          │
│  [图标] 钢铁  x100  │   ←→     │  [图标] 银矿  x50         │
│  [图标] 组件  x20   │          │  [图标] 草药  x10         │
│                    │          │                          │
│  [✓ 你已确认]       │          │  [○ Player1 未确认]       │
├────────────────────┴──────────┴──────────────────────────┤
│ [搜索物品...]           [更新] [重置]    [取消]          │
├──────────────────────────────────────────────────────────┤
│  [图标] 钢铁    [-] [0] [+]    / 500                      │
│  [图标] 银矿    [-] [0] [+]    / 200                      │
│  [图标] 木材    [-] [0] [+]    / 1000                     │
│  ...                                                     │
└──────────────────────────────────────────────────────────┘
```

### 3.2 报价栏改进

**当前**：白板背景 + 居中标题 + 物品列表 + 底部复选框

**改为**：
- 顶部 3px 色条（己方绿/对方蓝）+ 极淡背景
- 标题左对齐，小字
- 物品列表行高 28px（略压缩）
- 空报价显示引导文字（灰色居中）
- 确认状态改为底部徽章：
  - 己方：可点击按钮 `[✓ 你已确认]` / `[○ 你未确认]`
  - 对方：纯标签 `[✓ Player1 已确认]` / `[○ Player1 未确认]`

### 3.3 物品选择改进

**当前**：每行 6 个按钮（-100/-10/-1/+1/+10/+100）+ 文本框 + `/ 总数`，一行挤 7 个交互元素

**改为**：
- 图标 + 名称 + `[输入框]` + `/ 总数` + 右键菜单（快速选择全部/一半/清零）
- 输入框支持滚轮调整（IMGUI 的 `TextField` 不原生支持滚轮，但可以用 `Event.current.scrollDelta`）
- 或保留 ±1/±10/±100 但改为右键菜单选项

**推荐**：保留按钮但精简为 `-10 -1 [输入框] +1 +10`，去掉 ±100（右键菜单提供"全选""+100""清零"）

### 3.4 底部操作栏

**当前**：更新/重置从中间列底部往上堆叠，取消在最底（红色）

**改为**：底部水平栏 `[搜索框.........]  [更新] [重置] [取消]`，取消用 `trade.cancelButton` 色

### 3.5 TradeList 改进

- 隔行高亮改为 `trade.rowHoverBg` 统一
- 显示名用 `ChatTheme.FormatDisplayName`（复用聊天那套：有富文本保留，纯文本 hash 色）
- 修复 `tradesChanged` bug
- 空状态居中引导文字

---

## 4. 代码改动清单

### 4.1 新增文件

| 文件 | 说明 |
|------|------|
| `Extensions/Trade/Client/TradeTheme.cs` | Trade 业务颜色 static 缓存 |

### 4.2 改动文件

| 文件 | 改动 |
|------|------|
| `BuiltInTradeClientExtension.cs` | `Register()` 注册 10 个 `trade.*` 颜色；`Activate()` 调 `TradeTheme.Refresh` |
| `TradeWindow.cs` | `Color.red` → `TradeTheme.CancelButton`；报价栏加色条+背景；确认状态改徽章；底部按钮水平排列；物品行精简；空状态引导 |
| `TradeList.cs` | 修复 `tradesChanged` bug；显示名用 `FormatDisplayName`；隔行高亮用 `TradeTheme.RowHoverBg` |
| `TradeExtension.Client.csproj` | 加 `TradeTheme.cs` 编译项 |
| `Client/Themes/default.xml` | 加 `trade.*` 颜色定义 |
| `Client/Languages/*/Keyed/TradeExtension.xml` | 加新翻译键（引导文字、徽章文字）；删 `Phinix_trade_sortByLabel` |

### 4.3 不改的文件

| 文件 | 原因 |
|------|------|
| `TradeMainTabProvider.cs` | 纯壳，28 行 |
| `TradeSettingsPanelProvider.cs` | 已用 `Listing_Standard`，无硬编码 |
| `PhinixDefaultTradeBehaviour.cs` | 非渲染 UI（信件/空投） |
| `StackedThings.cs` | 物品分组逻辑，非 UI 渲染 |
| `TradeItemConverter.cs` | 转换逻辑，非 UI |
| 所有 Server 文件 | 服务端不改 |
| 所有 Contracts 文件 | 契约层不改 |

---

## 5. 设计哲学合规

### 5.1 §1.1 插件平权

- Trade 通过 `IUiTheme.RegisterColor` 注册自己的颜色，和 Chat 平权
- `TradeTheme` 是 `internal static`，和 `ChatTheme` 同构

### 5.2 §1.3 host 只做通用服务

- Trade 的业务颜色（`trade.ourOfferAccent` 等）由 Trade 自己注册
- host 的 `IUiTheme` 不知道 `ourOfferAccent` 是什么意思

### 5.3 §2.3 减少硬编码

- 新增一个插件 = 注册自己的颜色，host 代码改动为零
- `Color.red` 硬编码消除

### 5.4 §6 增量更新

- 只新增 `TradeTheme.cs`，不删不改现有接口
- `TradeWindow` 内部重构，外部行为不变

### 5.5 §8 UI 性能

- `TradeTheme` static 字段读 = 零分配
- 报价栏色条/背景用预计算 Rect
- 物品列表的 `filteredAvailableItems` 已有缓存机制，不改

---

## 6. 风险评估

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| 物品行布局改动导致小屏幕溢出 | 中 | 中 | 保留 ScrollView，测试 1280×720 |
| 确认徽章交互改变用户习惯 | 低 | 低 | 己方徽章仍是可点击的切换按钮，只是视觉变了 |
| `TradeTheme.Refresh` 时机和 Chat 不一致 | 低 | 低 | 都在各自 `Activate()` 里调，顺序由 Priority 决定（Chat=1000 先于 Trade=1100） |
| 颜色和 RimWorld 原生交易 UI 不协调 | 低 | 低 | 绿/蓝/红对应 RimWorld 信件体系，和聊天一致 |

---

## 7. 分阶段实施

| 阶段 | 内容 | 依赖 | 风险 |
|------|------|------|------|
| **1. Trade 主题迁移** | `TradeTheme` + 颜色注册 + `Color.red` 替换 + `default.xml` | 聊天 Phase 2（IUiTheme 已就绪） | 低 |
| **2. TradeList 修复** | `tradesChanged` bug + `FormatDisplayName` + 空状态 | 阶段 1 | 低 |
| **3. TradeWindow 视觉** | 报价栏色条+背景 + 确认徽章 + 底部按钮水平排列 | 阶段 1 | 中 |
| **4. 物品行精简** | 按钮精简 + 右键快速选择 + 空报价引导 | 阶段 3 | 中 |
| **5. 翻译键清理** | 删死键 + 加新键 | 阶段 3 | 低 |

---

## 8. 验证清单

### 8.1 主题系统

- [ ] `TradeTheme.CancelButton` 返回正确的 Error 色
- [ ] XML 覆盖 `trade.cancelButton` 后 `GetColor` 返回 XML 值
- [ ] Draw 路径无 `new Color()`、无 `GetColor` 调用

### 8.2 TradeWindow

- [ ] 己方报价栏顶部有绿色色条
- [ ] 对方报价栏顶部有蓝色色条
- [ ] 报价为空时显示引导文字
- [ ] 确认状态显示为徽章而非复选框
- [ ] 己方徽章可点击切换
- [ ] 取消按钮红色来自主题
- [ ] 底部按钮水平排列

### 8.3 TradeList

- [ ] 用户名变更后列表正确刷新
- [ ] 纯文本用户名有 hash 色
- [ ] 含富文本用户名保留自定义颜色
- [ ] 空交易列表显示引导文字

### 8.4 设计哲学合规

- [ ] host 工程未新增对 Trade 的引用
- [ ] `TradeTheme` 是 internal
- [ ] 新增颜色不改 host 代码

---

## 9. 相关文档

- [设计哲学.md](../../设计哲学.md) — §1.1/§1.3/§2.3/§6/§8
- [聊天UI重构与主题配色方案.md](./聊天UI重构与主题配色方案.md) — 分层主题架构参考
