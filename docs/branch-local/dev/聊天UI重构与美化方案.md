# 聊天 UI 重构与美化方案

> 2026-06-21，基于《设计哲学.md》§6/§8 与代码库实测编写。
>
> **本文档是方案文档，不是已实施记录。**

---

## 0. 背景

聊天增强三项功能（notice / @艾特 / 回复）已实现，但存在两类问题：

1. **业务 bug**：@艾特无信封、回复引用发出去不显示——服务端 `AddMessage` 丢弃了新字段
2. **UI bug + 美观不足**：5 个布局 bug + 原 IRC 风格聊天界面缺乏视觉层次

本文档分两大部分：先修业务 bug（阶段 0），再做 UI 重构与美化（阶段 1-4）。

---

## 1. 业务 Bug — @艾特无信封 / 回复引用丢失

### 1.1 根因

`Extensions/Chat/Server/BuiltInChatServerExtension.cs:90`：

```csharp
global::Phinix.Framework.BuiltInChatMessagePayload storedMessage =
    chatApi.AddMessage(context.SenderUuid, incomingPacket.Message);
```

`AddMessage` 只接收 `string message`，创建**全新 payload**，只填 4 个原始字段（message_id / sender_uuid / message / timestamp）。客户端发来的新字段全部被丢弃：

| 客户端发送的字段 | 服务端 AddMessage 后 | 广播出去 |
|------------------|---------------------|----------|
| `mentioned_uuids = ["uuid_of_111"]` | ❌ 空 | ❌ 空 |
| `reply_to_message_id = "msg-abc"` | ❌ 空 | ❌ 空 |
| `reply_to_snippet = "原消息..."` | ❌ 空 | ❌ 空 |

客户端收到广播后：
- `MentionedUuids` 为空 → `chatNotificationHandler` 不触发蓝色信封
- `ReplyToMessageId` 为空 → `drawChatMessage` 不渲染引用条

### 1.2 数据流（修复前 vs 修复后）

**修复前**（当前）：
```
客户端发 payload {message, mentioned_uuids, reply_to_*}  ✅
  → 服务端 ParseFrom → incomingPacket.MentionedUuids 有值  ✅
  → AddMessage(senderUuid, incomingPacket.Message)  ← 只传 .Message 字符串！
  → storedMessage = 新 payload，新字段全空  ❌
  → BroadcastPacket(storedMessage)  → 广播新字段全空  ❌
  → 客户端 MentionedUuids 为空 → 不弹信封  ❌
```

**修复后**：
```
客户端发 payload {message, mentioned_uuids, reply_to_*}  ✅
  → 服务端 ParseFrom → incomingPacket.MentionedUuids 有值  ✅
  → AddMessage(senderUuid, incomingPacket.Message,
               incomingPacket.MentionedUuids,
               incomingPacket.ReplyToMessageId,
               incomingPacket.ReplyToSnippet)  ← 透传新字段
  → storedMessage = 新 payload，新字段保留  ✅
  → BroadcastPacket(storedMessage)  → 广播新字段保留  ✅
  → 客户端 MentionedUuids 有值 → 弹信封  ✅
  → 客户端 ReplyToMessageId 有值 → 渲染引用条  ✅
```

### 1.3 修复方案

**文件**：`Extensions/Chat/Server/PhinixFrameworkChatService.cs`

`AddMessage` 加重载（§6 增量，保留原方法）：

```csharp
// 原方法保留不变
public global::Phinix.Framework.BuiltInChatMessagePayload AddMessage(string senderUuid, string message);

// 新增重载
public global::Phinix.Framework.BuiltInChatMessagePayload AddMessage(
    string senderUuid,
    string message,
    IEnumerable<string> mentionedUuids,
    string replyToMessageId,
    string replyToSnippet);
```

新重载实现：创建 payload 时填入 `MentionedUuids` / `ReplyToMessageId` / `ReplyToSnippet`。

**文件**：`Extensions/Chat/Server/BuiltInChatServerExtension.cs:90`

`HandleIncomingMessage` 改调新重载：

```csharp
global::Phinix.Framework.BuiltInChatMessagePayload storedMessage = chatApi.AddMessage(
    context.SenderUuid,
    incomingPacket.Message,
    incomingPacket.MentionedUuids,
    incomingPacket.ReplyToMessageId,
    incomingPacket.ReplyToSnippet);
```

### 1.4 @匹配的次要问题

当前 `ChatMainTabProvider.ParseMentions` 用 `text.Contains("@" + displayName)` 匹配。可能的问题：

| 问题 | 原因 | MVP 处理 |
|------|------|----------|
| displayName 含富文本标签 | `StripRichText` 未彻底清除 | 已调 `StripRichText`，基本够用 |
| 用户名含空格 | `@Player Name` 只匹配到 `@Player` | 阶段 3 用 @补全浮窗解决 |
| 大小写敏感 | `Contains` 区分大小写 | 可加 `StringComparison.OrdinalIgnoreCase` |
| 部分匹配 | `@Player1` 也会匹配 `@Player` | 阶段 3 用 @补全浮窗解决精确匹配 |

MVP 阶段先修服务端透传 bug，@匹配保持现状。补全浮窗在阶段 3 做。

---

## 2. UI Bug — 5 个布局问题

### 2.1 Bug 清单

| # | 文件 | 位置 | 问题 | 修复 |
|---|------|------|------|------|
| 1 | `ChatMainTabProvider` | `Draw` 底部布局 | 文本框和引用条位置算反：`BottomPartPixels(50).TopPartPixels(30)` 取的是引用条位置而非文本框 | 从下往上取：底部 30px=输入区，其上 20px=引用条 |
| 2 | `ChatMainTabProvider` | 引用条 Label | Label 占满 `replyBarRect` 全宽，关闭按钮 `RightPartPixels(20f)` 叠在 Label 文字上 | Label 宽度减去 28px 给关闭按钮留空间 |
| 3 | `NoticeBannerProvider` | `CurrentHeight` | 返回 `lastFrameHeight`（上一帧 Draw 里才更新），第一帧收到 notice 时 =0，banner 区域不分配空间 → 画在 Tab 内容上面重叠 | getter 里实时算 `activeNotices.Count * BANNER_HEIGHT`（加锁），过期清理也在 getter 里做 |
| 4 | `ChatMessageList` | `drawChatMessage` 正常+fallback 路径 | 算了 `mainRect`（引用条下方），但正常路径和 fallback 路径全用 `inRect` 画 Label → 引用条和正文重叠 | 正常路径和 fallback 路径的 Label / ButtonInvisible 全改用 `mainRect` |
| 5 | `ChatMessageList` | `drawChatMessage` 引用文本 | `TryGetMessage` + `StripRichText` + 截断每帧执行，违反 §8.3 | 缓存到 `CachedMessageDisplay.ReplyQuoteText`，在 `recalculateMessageRects` 里一次性算好 |

### 2.2 Bug 1 详解 — 底部布局算反

**当前代码**：
```csharp
float replyBarHeight = hostContext.ReplyTarget != null ? REPLY_BAR_HEIGHT : 0f;
Rect sendButtonRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT).RightPartPixels(CHAT_SEND_BUTTON_WIDTH);
Rect messageBoxRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT + replyBarHeight).TopPartPixels(CHAT_TEXTBOX_HEIGHT)...;
Rect replyBarRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT + replyBarHeight).TopPartPixels(replyBarHeight);
Rect chatRect = inRect.TopPartPixels(inRect.height - (CHAT_TEXTBOX_HEIGHT + replyBarHeight + DEFAULT_SPACING));
```

**问题**：`BottomPartPixels(50).TopPartPixels(30)` = 底部 50px 的**上方** 30px → 这是引用条位置，不是文本框。`TopPartPixels(replyBarHeight)` = 上方 20px → 这是文本框位置，不是引用条。两者算反了。

**正确布局**（从下到上）：
```
┌─────────────────────────────┐
│ 聊天消息区                    │  ← TopPartPixels(剩余)
├─────────────────────────────┤
│ ↩ Player: 原消息...      [×] │  ← 引用条 20px（在文本框上方）
├──────────────────────┬──────┤
│ 输入框                │ 发送 │  ← 文本框 30px（在最底部）
└──────────────────────┴──────┘
```

**修复代码**：
```csharp
float replyBarHeight = hostContext.ReplyTarget != null ? REPLY_BAR_HEIGHT : 0f;
float bottomHeight = CHAT_TEXTBOX_HEIGHT + replyBarHeight;

Rect sendButtonRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT).RightPartPixels(CHAT_SEND_BUTTON_WIDTH);
Rect messageBoxRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT).LeftPartPixels(inRect.width - (CHAT_SEND_BUTTON_WIDTH + DEFAULT_SPACING));
Rect replyBarRect = Rect.FromEdges(inRect.xMin, inRect.yMax - bottomHeight, inRect.xMax, inRect.yMax - CHAT_TEXTBOX_HEIGHT);
Rect chatRect = inRect.TopPartPixels(inRect.height - (bottomHeight + DEFAULT_SPACING));
```

### 2.3 Bug 2 详解 — 引用条 Label 遮挡

**当前代码**：
```csharp
Widgets.Label(replyBarRect, "↩ " + displayName + ": " + snippet.Colorize(Color.grey));
Rect closeRect = replyBarRect.RightPartPixels(20f);
if (Widgets.ButtonText(closeRect, "×")) { ... }
```

Label 文字画满 `replyBarRect` 全宽，关闭按钮 `RightPartPixels(20f)` 叠在文字末尾上。

**修复**：Label 宽度减去关闭按钮宽度：
```csharp
Rect labelRect = Rect.FromEdges(replyBarRect.xMin, replyBarRect.yMin, replyBarRect.xMax - 28f, replyBarRect.yMax);
Widgets.Label(labelRect, ...);
Rect closeRect = replyBarRect.RightPartPixels(24f);
```

### 2.4 Bug 3 详解 — banner 时序

**当前代码**：
```csharp
private float lastFrameHeight;
public float CurrentHeight => lastFrameHeight;  // 上一帧 Draw 才更新

public void Draw(Rect inRect) {
    // ...
    lastFrameHeight = activeNotices.Count > 0 ? BANNER_HEIGHT * activeNotices.Count : 0f;
}
```

`ServerTab.DoWindowContents` 在调 `Draw()` **之前**读 `CurrentHeight` 分配空间。第一帧收到 notice 时 `CurrentHeight = 0` → banner 区域不分配 → banner 画在 Tab 内容上面。

**修复**：getter 里实时算，不依赖 `lastFrameHeight`：
```csharp
public float CurrentHeight {
    get {
        float currentTime = Time.realtimeSinceStartup;
        lock (noticesLock) {
            activeNotices.RemoveAll(n => (currentTime - n.StartTime) >= n.DurationSeconds);
            return activeNotices.Count > 0 ? BANNER_HEIGHT * activeNotices.Count : 0f;
        }
    }
}
```

### 2.5 Bug 4 详解 — drawChatMessage 用错 Rect

**当前代码**：
```csharp
Rect mainRect = inRect;
if (有回复) {
    mainRect = new Rect(inRect.x, inRect.y + quoteHeight, inRect.width, inRect.height - quoteHeight);
    // 画引用条...
}
// 但下面全用 inRect！
Widgets.Label(inRect, formattedText);              // ← 应该是 mainRect
if (Mouse.IsOver(inRect)) { ... }                  // ← 应该是 mainRect
if (Widgets.ButtonInvisible(inRect, false)) { }    // ← 应该是 mainRect
```

正常路径（line 276/312/317/319/324）和 fallback 路径（line 271/276/279/281/288）全用 `inRect`。引用条和正文画在同一 y 坐标 → 重叠。

**修复**：所有 `inRect` 改为 `mainRect`（正常路径 + fallback 路径全改）。timestampRect 和 displayNameRect 也基于 `mainRect` 而非 `inRect`。

### 2.6 Bug 5 详解 — 引用文本每帧重算

**当前代码**（`drawChatMessage` 内）：
```csharp
if (!string.IsNullOrEmpty(chatMessage.ReplyToSnippet)) {
    quoteText = chatMessage.ReplyToSnippet;
} else if (hostContext.ChatService.TryGetMessage(chatMessage.ReplyToMessageId, out UIChatMessage original)) {
    string origText = TextHelper.StripRichText(original.Message ?? "");
    quoteText = origText.Length > 50 ? origText.Substring(0, 50) + "..." : origText;
}
```

每帧执行 `TryGetMessage` + `StripRichText` + 截断，违反 §8.3。

**修复**：`CachedMessageDisplay` 加 `ReplyQuoteText` 字段，在 `recalculateMessageRects` 里一次性算好：
```csharp
private struct CachedMessageDisplay {
    // ... 现有字段 ...
    public string ReplyQuoteText;  // 新增
}
```

`recalculateMessageRects` 里：
```csharp
string replyQuote = null;
if (!string.IsNullOrEmpty(chatMessage.ReplyToMessageId)) {
    if (!string.IsNullOrEmpty(chatMessage.ReplyToSnippet)) {
        replyQuote = chatMessage.ReplyToSnippet;
    } else if (hostContext.ChatService.TryGetMessage(chatMessage.ReplyToMessageId, out UIChatMessage original)) {
        string origText = TextHelper.StripRichText(original.Message ?? "");
        replyQuote = origText.Length > 50 ? origText.Substring(0, 50) + "..." : origText;
    }
}

displayCache[chatMessage.MessageId] = new CachedMessageDisplay {
    // ... 现有字段 ...
    ReplyQuoteText = replyQuote,
};
```

`drawChatMessage` 里直接读 `cached.ReplyQuoteText`，不再每帧查。

---

## 3. UI 重设计方案

### 3.1 目标布局

```
┌───────────────────────────────────────────┬───────────────┐
│  ▌ 公告：服务器将在 10 分钟后重启     [×] │  ⚙ 设置        │ ← banner
├───────────────────────────────────────────┤───────────────┤
│  Chat  Trade                              │  🔍 搜索...    │
├───────────────────────────────────────────┤               │
│                                           │  在线用户      │
│  Player1                          14:23   │  ─────────    │
│  │ ↩ Player2: 原消息内容...              │  ● Player1    │
│  你好 @Player2                            │  ● Player2    │
│                                           │  ○ Player3    │
│                          Player2   14:24  │               │
│                          收到，谢谢        │  已屏蔽 (2)   │
│                                           │  ─────────    │
│  Player1                          14:25   │  Player4      │
│  再见                                     │  Player5      │
│                                           │               │
├───────────────────────────────────────────┤               │
│  ┌─ 回复 Player2: 原消息...          [×] ─│               │
│  └──────────────────────────────────────── │               │
│  [输入消息...]                    [发送]  │               │
└───────────────────────────────────────────┴───────────────┘
```

### 3.2 设计要点

| # | 改动 | 说明 | 涉及文件 |
|---|------|------|----------|
| 1 | 消息左右分流 | 自己发的靠右，别人发的靠左，名字/时间在对应侧 | `ChatMessageList` |
| 2 | 消息分组 | 同一人 60 秒内连续消息只显示一次名字和时间，后续消息缩进 | `ChatMessageList` |
| 3 | 时间戳右对齐淡色 | 不再占消息行首 `[HH:mm]`，移到消息末尾右侧淡灰色小字 | `ChatMessageList` |
| 4 | 回复引用卡片 | 带左侧蓝色竖条的缩进卡片，非纯文本 `↩` | `ChatMessageList` |
| 5 | @艾特高亮 | 消息中 `@昵称` 染软蓝色加粗，收到 @自己 时整条消息背景微黄 | `ChatMessageList` |
| 6 | notice 消息特殊样式 | notice 在聊天流里：金色左边框 + 暖色底，和顶部 banner 遥相呼应 | `ChatMessageList` |
| 7 | 输入区回复预览卡片 | 输入框上方显示引用卡片（头像+名字+原文截断+关闭），非一行文本 | `ChatMainTabProvider` |
| 8 | @补全浮窗 | 输入 `@` 弹在线用户列表，方向键选择，Tab/Enter 确认插入 | `ChatMainTabProvider` |
| 9 | 消息悬停高亮 | 鼠标悬停时整条消息背景微亮（已有 `DrawHighlight`，保留） | `ChatMessageList` |
| 10 | 空状态 | 未连接时聊天区显示引导文字而非空白 | `ChatMessageList` |

### 3.3 颜色方案（§8：全部 `static readonly`）

| 用途 | 颜色 | RGBA |
|------|------|------|
| 自己消息背景 | 淡蓝 | `(0.15, 0.25, 0.4, 0.15)` |
| @艾特文字高亮 | 软蓝 | `(0.4, 0.7, 1.0, 1.0)` |
| @自己消息背景 | 微黄 | `(0.3, 0.3, 0.15, 0.15)` |
| 回复引用竖条 | 蓝灰 | `(0.3, 0.5, 0.8, 0.8)` |
| 回复引用背景 | 微白 | `(1.0, 1.0, 1.0, 0.05)` |
| notice 消息竖条 | 金色 | `(0.9, 0.7, 0.2, 0.9)` |
| notice 消息背景 | 暖色 | `(0.2, 0.15, 0.08, 0.15)` |
| 时间戳 | 淡灰 | `(0.6, 0.6, 0.6, 0.7)` |
| 输入区回复条竖条 | 蓝色 | `(0.3, 0.5, 0.9, 0.8)` |
| 输入区回复条背景 | 微蓝 | `(0.2, 0.3, 0.5, 0.1)` |

---

## 4. 实施约束（§6/§8）

### §6 渐进式迁移

- 重构不改行为——消息内容不变、发送/接收逻辑不变、右键菜单功能不变。只改渲染和布局归属
- 每阶段保持可编译、可运行、可验证
- Host/Core 仅做增量更新——`ServerTab` / `INoticeBannerProvider` 接口不变，全部在 Chat 扩展内

### §8 UI 渲染性能

- 所有颜色 `static readonly`，不在 Draw 路径 `new Color()`
- 布局计算在 `recalculateMessageRects` 缓存，Draw 路径只读缓存
- 消息分组判断在 cache 时算好，存入 `CachedMessageDisplay`
- @高亮文本在 cache 时正则处理一次，Draw 路径不每帧 `Regex.Replace`
- Draw 路径零 `new` 对象、零 LINQ `.Where()` `.Select()` `.ToList()`
- 引用文本缓存到 `CachedMessageDisplay.ReplyQuoteText`

---

## 5. 分阶段实施计划

| 阶段 | 内容 | 依赖 | 风险 |
|------|------|------|------|
| **0. 修业务 bug** | `AddMessage` 加重载，服务端透传 mentioned_uuids / reply_to_* | 无 | 低 |
| **1. 修 5 个 UI bug** | 底部布局 / 引用条遮挡 / banner 时序 / mainRect / 引用缓存 | 阶段 0 | 低 |
| **2. 消息渲染美化** | 左右分流 + 分组 + 时间戳移位 + @高亮 + notice 特殊样式 | 阶段 1 | 中（核心渲染路径） |
| **3. 输入区美化** | 回复引用卡片 + @补全浮窗 | 阶段 2 | 中 |
| **4. 细节打磨** | 空状态 + 消息悬停 + notice banner 美化 | 阶段 3 | 低 |

每阶段独立可验证。阶段 0-1 完成后三项功能完全可用；阶段 2-4 是纯美化，不影响功能。

---

## 6. 验证清单

### 6.1 业务验证（阶段 0）

- [ ] 输入 `@昵称 你好` 发送 → 被艾特者收到蓝色信封
- [ ] 右键消息 → 回复 → 发送 → 接收方看到引用条
- [ ] 控制台 `notice hello` → 全体客户端 banner 显示
- [ ] 控制台 `notice 30 hello` → banner 持续 30 秒
- [ ] 老客户端收到新消息不崩溃（新字段未知字段自动保留）

### 6.2 UI 验证（阶段 1）

- [ ] 回复引用条在输入框上方，不遮挡输入框
- [ ] 引用条文字不被关闭按钮遮挡
- [ ] notice banner 第一帧就显示，不重叠 Tab 内容
- [ ] 回复消息的引用条和正文不重叠
- [ ] 长时间运行引用文本不每帧重算（用 profiler 确认）

### 6.3 设计哲学合规

- [ ] host 工程未新增对插件的强引用
- [ ] 未硬编码业务类型于 host
- [ ] host 公开接口未删除/修改（§6 增量）
- [ ] 所有颜色 `static readonly`（§8.3）
- [ ] Draw 路径无 `new` 分配（§8.3）
- [ ] 布局计算已缓存（§8.3）
- [ ] Proto 字段编号未重用（§5.3）

---

## 7. 相关文档

- [设计哲学.md](../../设计哲学.md) — 架构原则、边界规则、反模式
- [聊天增强功能实施方案.md](./聊天增强功能实施方案.md) — 三项功能原始实施方案
- [聊天图片功能可行性评估.md](./聊天图片功能可行性评估.md) — Chat 扩展增量功能参考
