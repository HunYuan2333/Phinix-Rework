# 聊天 UI 重构与主题配色方案

> 2026-06-21，基于《聊天UI重构与美化方案.md》§3 + 《聊天主题配色方案.md》合并细化，加入分层主题架构与 Steam 独立 mod 评估。
>
> **本文档是方案文档，不是已实施记录。** 阶段 0（业务 bug）和阶段 1（5 个 UI bug）已完成。

---

## 0. 目标

三件事合并为一份方案：

1. **分层主题系统**：平台层提供通用 UI 语义颜色，插件层注册自己的业务颜色。不积累技术债务
2. **UI 重构**：消息渲染（左右分流/分组/时间戳移位/@高亮/notice 特殊样式/回复引用卡片）+ 输入区重做（回复预览卡片/@补全浮窗）+ 空状态
3. **Steam 独立 mod 注入**：第三方通过 Steam 创意工坊发布的独立 mod 能注入主题颜色

**核心约束**：设计哲学 §1.1 插件平权、§1.3 host 只做通用服务、§2.3 减少硬编码、§6 增量更新、§8 UI 性能。

---

## 1. 分层主题架构

### 1.1 设计原则

```
┌─────────────────────────────────────────────┐
│  平台层 IUiTheme（通用 UI 语义颜色）          │  ← ClientExtensionAbstractions
│  primaryText / secondaryText / background    │     所有插件平等使用
│  separator / hoverHighlight / pending        │
│  error / success / warning                   │
├─────────────────────────────────────────────┤
│  Chat 扩展 ChatTheme（Chat 业务颜色）         │  ← Extensions/Chat/Client
│  mentionText / replyQuoteBorder / noticeAccent│     internal，Chat 专属
├─────────────────────────────────────────────┤
│  Trade 扩展 TradeTheme（Trade 业务颜色）      │  ← Extensions/Trade/Client
│  tradeCompleted / tradeCancelled / ...       │     internal，Trade 专属
├─────────────────────────────────────────────┤
│  第三方 submod 主题                            │  ← 独立 mod
│  自己注册颜色，自己的 XML                      │     独立实现
└─────────────────────────────────────────────┘
```

**§1.3 合规**：平台层只有通用 UI 语义颜色（`primaryText`、`background`），不含业务颜色（`mentionText`、`tradeCompleted`）。host 不关心当前有哪些业务。

**§1.1 合规**：所有插件平等使用 `IUiTheme`。Chat 不享受特权。

**§2.3 判断标准**：新增一个插件（如邮件），host 代码改动为零——邮件插件自己注册 `MailTheme`，不需要改平台层。

### 1.2 平台层 IUiTheme

**文件**：`Client/ClientExtensionAbstractions/UI/IUiTheme.cs`（新增）

```csharp
namespace PhinixClient.Framework
{
    /// <summary>
    /// 平台层通用 UI 主题。提供与业务无关的 UI 语义颜色。
    /// 所有插件通过 ExtensionHostContext.GetService<IUiTheme>() 获取。
    /// 设计哲学 §1.3：host 只做通用服务；§1.1：插件平权。
    /// </summary>
    public interface IUiTheme
    {
        Color PrimaryText { get; }
        Color SecondaryText { get; }
        Color Background { get; }
        Color Surface { get; }
        Color Separator { get; }
        Color HoverHighlight { get; }
        Color Pending { get; }
        Color Error { get; }
        Color Success { get; }
        Color Warning { get; }

        /// <summary>注册插件自定义颜色。插件在 Register() 阶段调此方法注册业务颜色。</summary>
        void RegisterColor(string key, Color defaultColor);

        /// <summary>读取已注册的颜色。插件用此方法读自己的业务颜色。</summary>
        Color GetColor(string key);

        /// <summary>尝试读取已注册颜色，不存在返回 default。</summary>
        bool TryGetColor(string key, out Color color);

        /// <summary>重新加载所有主题文件（用户点"重载主题"时调用）。</summary>
        void Reload();
    }
}
```

**关键设计**：
- 平台层只定义**通用语义**（`PrimaryText`、`Error`、`Separator` 等 ~10 个），直接作为接口属性
- `RegisterColor` / `GetColor` 用字符串 key 存取**插件业务颜色**——平台层不知道 key 的含义
- `Reload` 重新加载所有主题文件（平台 + 各插件）

### 1.3 平台层实现

**文件**：`Client/Source/UI/UiTheme.cs`（新增，host 层）

```csharp
namespace PhinixClient
{
    internal sealed class UiTheme : IUiTheme
    {
        // 通用颜色——接口属性直接返回
        public Color PrimaryText { get; private set; } = Color.white;
        public Color SecondaryText { get; private set; } = new Color(0.6f, 0.6f, 0.6f);
        // ... 其余通用颜色 ...

        // 插件注册的业务颜色——字典存储
        private readonly Dictionary<string, Color> customColors = new Dictionary<string, Color>();
        private readonly Dictionary<string, Color> defaultColors = new Dictionary<string, Color>();

        public void RegisterColor(string key, Color defaultColor) { /* ... */ }
        public Color GetColor(string key) { /* ... */ }
        public bool TryGetColor(string key, out Color color) { /* ... */ }
        public void Reload() { /* 读 XML 覆盖所有颜色 */ }
    }
}
```

**host 注册**（`Client.cs` 构造函数，增量追加）：

```csharp
IUiTheme uiTheme = new UiTheme();
extensionHostContext.AddService<IUiTheme>(uiTheme);
```

### 1.4 插件层注册业务颜色

**Chat 扩展**在 `Register()` 阶段注册自己的业务颜色：

```csharp
// BuiltInChatClientExtension.Register()
IUiTheme theme = builder.HostContext.GetRequiredService<IUiTheme>();
theme.RegisterColor("chat.mentionText", new Color(0.4f, 0.7f, 1.0f));
theme.RegisterColor("chat.replyQuoteBorder", new Color(0.3f, 0.5f, 0.8f, 0.7f));
theme.RegisterColor("chat.replyQuoteBg", new Color(1f, 1f, 1f, 0.04f));
theme.RegisterColor("chat.noticeAccent", new Color(0.9f, 0.7f, 0.2f, 0.9f));
// ... 其余 Chat 业务颜色 ...
```

**Trade 扩展**同样注册自己的：

```csharp
theme.RegisterColor("trade.completed", new Color(0.3f, 0.8f, 0.3f));
theme.RegisterColor("trade.cancelled", new Color(0.94f, 0.28f, 0.28f));
```

**读取**：

```csharp
Color mentionColor = theme.GetColor("chat.mentionText");
// 或在 Draw 路径缓存到 static 字段
```

### 1.5 ChatTheme 简化为 static 缓存

`ChatTheme` 不再是颜色定义的权威来源，而是 **Draw 路径的 static 缓存层**——在主题加载时从 `IUiTheme` 读一次，存入 static 字段：

```csharp
internal static class ChatTheme
{
    public static Color MentionText;
    public static Color ReplyQuoteBorder;
    // ...

    internal static void Refresh(IUiTheme theme)
    {
        MentionText = theme.GetColor("chat.mentionText");
        ReplyQuoteBorder = theme.GetColor("chat.replyQuoteBorder");
        // ...
    }
}
```

`BuiltInChatClientExtension.Activate` 里调 `ChatTheme.Refresh(theme)` + `theme.Reload()` 后再 Refresh 一次。

**§8 合规**：Draw 路径读 `ChatTheme.MentionText` static 字段 = 零分配。

### 1.6 主题 XML 文件格式

```xml
<?xml version="1.0" encoding="utf-8" ?>
<PhinixTheme version="1">
  <!-- 通用颜色（平台层） -->
  <color key="primaryText"      r="1.00" g="1.00" b="1.00" a="1.00" />
  <color key="secondaryText"    r="0.60" g="0.60" b="0.60" a="1.00" />
  <color key="background"       r="0.00" g="0.00" b="0.00" a="0.00" />
  <color key="surface"          r="0.10" g="0.10" b="0.10" a="0.50" />
  <color key="separator"        r="1.00" g="1.00" b="1.00" a="0.08" />
  <color key="hoverHighlight"   r="1.00" g="1.00" b="1.00" a="0.10" />
  <color key="pending"          r="1.00" g="1.00" b="1.00" a="0.80" />
  <color key="error"            r="0.94" g="0.28" b="0.28" a="1.00" />
  <color key="success"          r="0.30" g="0.80" b="0.30" a="1.00" />
  <color key="warning"          r="0.90" g="0.70" b="0.20" a="1.00" />

  <!-- Chat 业务颜色（chat. 前缀） -->
  <color key="chat.mentionText"       r="0.40" g="0.70" b="1.00" a="1.00" />
  <color key="chat.mentionSelfBg"     r="0.30" g="0.30" b="0.15" a="0.15" />
  <color key="chat.selfMessageBg"     r="0.15" g="0.25" b="0.40" a="0.15" />
  <color key="chat.replyQuoteBorder"  r="0.30" g="0.50" b="0.80" a="0.70" />
  <color key="chat.replyQuoteBg"      r="1.00" g="1.00" b="1.00" a="0.04" />
  <color key="chat.replyQuoteText"    r="0.60" g="0.60" b="0.60" a="1.00" />
  <color key="chat.noticeAccent"      r="0.90" g="0.70" b="0.20" a="0.90" />
  <color key="chat.noticeBg"          r="0.20" g="0.15" b="0.08" a="0.15" />
  <color key="chat.noticeBannerBg"    r="0.10" g="0.15" b="0.20" a="0.85" />
  <color key="chat.noticeProgress"    r="0.30" g="0.60" b="1.00" a="0.80" />
  <color key="chat.inputReplyBorder"  r="0.30" g="0.50" b="0.90" a="0.80" />
  <color key="chat.inputReplyBg"      r="0.20" g="0.30" b="0.50" a="0.10" />

  <!-- Trade 业务颜色（trade. 前缀，Trade 重新设计 UI 时加） -->
  <!-- <color key="trade.completed"  r="0.30" g="0.80" b="0.30" a="1.00" /> -->
  <!-- <color key="trade.cancelled"  r="0.94" g="0.28" b="0.28" a="1.00" /> -->
</PhinixTheme>
```

**key 命名约定**：`{插件前缀}.{语义名}`。通用颜色无前缀。Chat 用 `chat.`，Trade 用 `trade.`，第三方用自己的前缀。

### 1.7 文件位置与加载顺序

```
Mod 分发（只读）：
  Client/Themes/default.xml          ← 随 Phinix mod 发布

用户自定义（可读写）：
  {GenFilePaths.SaveDataFolderPath}/Phinix/Themes/
    custom.xml                       ← 用户自定义
    dark.xml                         ← 用户下载的暗色主题
```

**加载顺序**：
1. `UiTheme` 构造时设置通用颜色代码默认值
2. 各插件 `Register()` 时 `RegisterColor()` 注册业务颜色默认值
3. `Activate` 阶段调 `IUiTheme.Reload()` → 读 XML 覆盖所有颜色
4. 先读 mod 目录 `default.xml`，再读 Config 目录 `Phinix/Themes/*.xml`（字典序，后者覆盖前者）
5. `Reload` 后各插件调 `ChatTheme.Refresh(theme)` 刷新 static 缓存

---

## 2. Steam 独立 mod 注入评估

### 2.1 RimWorld mod 加载机制

RimWorld 的 mod 加载流程：
1. `ModLister.AllInstalledMods` 扫描 `Mods/` 目录和 Steam 订阅目录
2. 每个 mod 有 `ModContentPack`，包含 `RootDir`、`PackageId`、`assemblies` 等
3. mod 的 `About/About.xml` 声明依赖关系和加载顺序
4. mod 的 `Assemblies/*.dll` 自动加载
5. 带 `[HarmonyPatch]` 或继承 `Mod` 的类在 mod 加载时实例化

### 2.2 Phinix 扩展发现机制

当前 `Client.cs` 的扩展发现路径（`GetExtensionProbeDirectories`）：

```
1. Client.dll 所在目录（clientAssemblyDirectory）
2. ../../Common/Assemblies
3. ../../Common/Extensions
4. AppDomain.CurrentDomain.BaseDirectory（RimWorld 根目录）
```

**问题**：这些路径全是 Phinix mod 自己的目录。Steam 独立 mod 的 DLL 不在这些路径里。

### 2.3 Steam 独立 mod 如何注入

**场景**：用户在 Steam 创意工坊订阅了一个 "Phinix Chat Dark Theme" mod，它只包含一个 `dark.xml` 主题文件。或者订阅了一个 "Phinix MyExtension" mod，它包含一个 DLL 插件。

#### 方式 A：DLL 插件（完整扩展）

第三方 mod 结构：
```
Phinix-MyExtension/
  About/
    About.xml          ← 声明依赖 Phinix
  Assemblies/
    MyExtension.dll    ← 引用 ClientExtensionAbstractions.dll，实现 IPhinixExtensionModule
```

`About.xml` 声明依赖：
```xml
<ModMetaData>
  <packageId>author.phinix.myextension</packageId>
  <supportedVersions><li>1.6</li></supportedVersions>
  <modDependencies>
    <li>
      <packageId>Thomotron.Phinix</packageId>
      <displayName>Phinix Rework</displayName>
    </li>
  </modDependencies>
  <loadAfter>
    <li>Thomotron.Phinix</li>
  </loadAfter>
</ModMetaData>
```

**问题**：Phinix 的 `ExtensionAssemblyLoader.LoadAssemblies` 只扫描 `GetExtensionProbeDirectories()` 列出的路径，不扫描其他 mod 的目录。Steam 独立 mod 的 DLL 会被 RimWorld 自动加载到 AppDomain，但 Phinix 不会发现它。

**解法**：扩展 `GetExtensionProbeDirectories` 套加入"依赖 Phinix 的其他 mod 的 Assemblies 目录"：

```csharp
private static IEnumerable<string> GetExtensionProbeDirectories(string modRootDir = null)
{
    // ... 现有路径 ...

    // 新增：扫描所有依赖 Phinix 的 mod 的 Assemblies 目录
    foreach (ModMetaData mod in ModLister.AllInstalledMods)
    {
        if (mod == null || !mod.Active) continue;
        // 跳过自己
        if (string.Equals(mod.PackageId, PackageId, StringComparison.OrdinalIgnoreCase)) continue;
        // 检查是否依赖 Phinix
        bool dependsOnPhinix = false;
        foreach (var dep in mod.ModMetaData?.modDependencies ?? Enumerable.Empty<ModDependency>())
        {
            if (string.Equals(dep.packageId, PackageId, StringComparison.OrdinalIgnoreCase))
            {
                dependsOnPhinix = true;
                break;
            }
        }
        if (dependsOnPhinix)
        {
            string asmDir = System.IO.Path.Combine(mod.RootDir?.ToString() ?? "", "Assemblies");
            if (System.IO.Directory.Exists(asmDir)) yield return asmDir;
        }
    }
}
```

**§6 合规**：增量新增路径，不删不改现有路径。现有行为不变。

**§1.1 合规**：任何声明依赖 Phinix 的 mod 平等被发现，无"官方超级公民"。

#### 方式 B：纯主题文件（无 DLL）

第三方 mod 结构：
```
Phinix-DarkTheme/
  About/
    About.xml          ← 声明依赖 Phinix
  Themes/
    dark.xml           ← 主题文件
```

**问题**：`ThemeLoader` 只扫描 Phinix mod 自己的 `Themes/` 目录和 Config 目录。第三方 mod 的 `Themes/` 目录不在扫描范围。

**解法**：`ThemeLoader` 加入扫描依赖 Phinix 的 mod 的 `Themes/` 目录：

```csharp
// ThemeLoader.Load()
// 1. 读 Phinix mod 自己的 default.xml
// 2. 读 Config 目录 Phinix/Themes/*.xml
// 3. 新增：扫描依赖 Phinix 的 mod 的 Themes/*.xml
foreach (ModMetaData mod in ModLister.AllInstalledMods)
{
    if (!isActiveDependentOfPhinix(mod)) continue;
    string themeDir = Path.Combine(mod.RootDir?.ToString() ?? "", "Themes");
    if (Directory.Exists(themeDir))
    {
        foreach (string path in Directory.GetFiles(themeDir, "*.xml").OrderBy(p => p))
        {
            LoadFile(path);
        }
    }
}
```

**加载优先级**：
```
Phinix mod default.xml（基线）
  → 第三方 mod Themes/*.xml（mod 提供的主题包）
    → Config/Phinix/Themes/*.xml（用户自定义，最高优先级）
```

### 2.4 Steam 独立 mod 注入可行性评估

| 维度 | DLL 插件（方式 A） | 纯主题文件（方式 B） |
|------|---------------------|----------------------|
| 可行性 | ✅ 需扩展 `GetExtensionProbeDirectories` | ✅ 需扩展 `ThemeLoader` 扫描路径 |
| §1.1 合规 | ✅ 平等发现 | ✅ 平等加载 |
| §6 增量 | ✅ 加路径不改现有路径 | ✅ 加扫描不改现有扫描 |
| 用户门槛 | 中——需懂 C# 开发 | 低——只需写 XML |
| Steam 创意工坊发布 | ✅ RimWorld 原生支持 | ✅ RimWorld 原生支持 |
| 版本兼容风险 | 高——Phinix API 变化可能导致编译过的 DLL 不兼容 | 低——XML 只是颜色值，不会因 API 变化失效 |
| 安全性 | DLL 可执行任意代码（和所有 RimWorld mod 一样） | XML 只解析颜色，安全 |

### 2.5 推荐实施

**MVP 先做方式 B（纯主题文件）**——风险低、用户门槛低、§8 无性能影响。方式 A（DLL 插件）的扩展发现路径改动可以后续做，当前已有 `GetExtensionProbeDirectories` 的 AppDomain fallback 路径，第三方 DLL 如果被 RimWorld 加载到 AppDomain，Phinix 的 `PhinixExtensionRegistry.DiscoverExtensions` 扫描 `AppDomain.CurrentDomain.GetAssemblies()` 时已经能发现。

---

## 3. 第三方插件接入评估

### 3.1 主题系统接入

第三方 submod 的接入路径：

```csharp
[PhinixExtension("mymod.chataddon")]
public class MyExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
{
    public void Register(IExtensionBuilder builder)
    {
        // 注册自己的业务颜色
        IUiTheme theme = builder.HostContext.GetRequiredService<IUiTheme>();
        theme.RegisterColor("mymod.customHighlight", new Color(1f, 0f, 1f));
    }

    public void Activate(ExtensionHostContext hostContext)
    {
        IUiTheme theme = hostContext.GetRequiredService<IUiTheme>();
        // 读取自己的颜色
        Color myColor = theme.GetColor("mymod.customHighlight");
        // 或用 TryGetColor 做防御性读取
    }
}
```

用户在主题 XML 里覆盖：
```xml
<color key="mymod.customHighlight" r="0.5" g="0.2" b="0.8" a="1.0" />
```

**评估**：
- ✅ 第三方不需要引用 `ChatTheme` 或 `TradeTheme`
- ✅ 颜色 key 用自己的前缀（`mymod.`），不会和 Chat/Trade 冲突
- ✅ 主题文件统一一个 XML，用户一处改所有插件的颜色
- ✅ 第三方不需要自己写 ThemeLoader——平台层统一加载

### 3.2 UI 组件接入

| 接入方式 | 可行性 | 说明 |
|----------|--------|------|
| 注册 `IMainTabProvider` 加自己的 Tab | ✅ 已有 | 所有插件平等 |
| 注册 `IServerSidebarProvider` 加侧栏 | ✅ 已有 | |
| 注册 `INoticeBannerProvider` 加 banner | ✅ 已有 | |
| 在 Chat 消息流里渲染自定义消息样式 | ❌ | `ChatMessageList` 是 Chat internal，未来可通 `IMessageRenderer` 路由 |
| 用 `IUiTheme` 通用颜色画自己的 UI | ✅ 本方案新增 | |

---

## 4. UI 重设计——消息渲染

### 4.1 目标布局

```
┌───────────────────────────────────────────┬───────────────┐
│  ▌ 公告：服务器将在 10 分钟后重启     [×] │  ⚙ 设置        │ ← banner
├───────────────────────────────────────────┤───────────────┤
│  Chat  Trade                              │  🔍 搜索...    │
├───────────────────────────────────────────┤               │
│                                           │  在线用户      │
│  Player1                          14:23   │  ─────────    │
│  ┌─ ↩ Player2: 原消息内容...             │  ● Player1    │
│  │  你好 @Player2                         │  ● Player2    │
│                                           │  ○ Player3    │
│                          Player2   14:24  │               │
│                          收到，谢谢        │  已屏蔽 (2)   │
│                                           │  ─────────    │
│  Player1                          14:25   │  Player4      │
│  再见                                     │  Player5      │
│                                           │               │
│  ▌ [公告] 服务器将于今晚 22:00 维护       │               │
│                                           │               │
├───────────────────────────────────────────┤               │
│  ┌─ 回复 Player2: 原消息...          [×] ─│               │
│  └──────────────────────────────────────── │               │
│  [输入消息...]                    [发送]  │               │
└───────────────────────────────────────────┴───────────────┘
```

### 4.2 消息左右分流

**规则**：
- `SenderUuid == 本地 UUID` → 右对齐
- `SenderUuid == SystemSenderUuid` → 居中（notice/系统消息）
- 其他 → 左对齐

**实现**：`recalculateMessageRects` 里算 `IsSelf` / `IsSystem` 存入 `CachedMessageDisplay`。Draw 路径只读 bool。

### 4.3 消息分组

**规则**：同一 `SenderUuid` 且 `Timestamp` 差 < 60 秒 → 只在第一条显示名字，后续缩进。

**实现**：`recalculateMessageRects` 维护 `lastSenderUuid` + `lastTimestamp`，算 `IsGrouped` 存入 cache。

### 4.4 时间戳移位

**当前**：`[14:23] Player1: 你好`

**目标**：`Player1: 你好         14:23` — 时间戳移到消息末尾右侧，用 `IUiTheme.SecondaryText` 颜色。

### 4.5 @艾特高亮

- 消息中 `@昵称` 染 `ChatTheme.MentionText`
- 含 @自己的消息背景染 `ChatTheme.MentionSelfBg`
- `MentionRegex`（`static readonly` 预编译）在 cache 时处理一次

### 4.6 notice 消息特殊样式

- 左侧 `ChatTheme.NoticeAccent` 竖条 + `ChatTheme.NoticeBg` 背景 + 居中
- 不参与分组，不显示发送者

### 4.7 回复引用卡片

- 左侧 `ChatTheme.ReplyQuoteBorder` 竖条 + `ChatTheme.ReplyQuoteBg` 背景 + 缩进
- 引用文本用 `ChatTheme.ReplyQuoteText` 颜色
- 引用文本读 `CachedMessageDisplay.ReplyQuoteText`（已实现）

---

## 5. UI 重设计——输入区

### 5.1 回复预览卡片

- 左侧 `ChatTheme.InputReplyBorder` 竖条 + `ChatTheme.InputReplyBg` 背景
- 两行：第一行"回复 {displayName}"，第二行原文截断

### 5.2 @补全浮窗

- 输入 `@` 弹在线用户 `FloatMenu`
- 选中后替换为完整 `@displayName`
- 事件驱动，不在 Draw 路径

---

## 6. UI 重设计——空状态

- 未连接时聊天区居中显示引导文字
- `ChatMessageList.Draw` 里 `if (messages.Count == 0)` 显示空状态

---

## 7. CachedMessageDisplay 扩展

| 字段 | 类型 | 说明 |
|------|------|------|
| `IsSelf` | bool | 自己发的（左右分流） |
| `IsSystem` | bool | 系统消息 |
| `IsNotice` | bool | 公告 |
| `IsGrouped` | bool | 与上一条分组 |
| `ReplyQuoteText` | string | 回复引用（已实现） |
| `TimestampText` | string | 格式化时间戳 |

---

## 8. 现有代码改动清单

### 8.1 新增文件

| 文件 | 层 | 说明 |
|------|-----|------|
| `Client/ClientExtensionAbstractions/UI/IUiTheme.cs` | 平台层 | 通用主题接口 |
| `Client/Source/UI/UiTheme.cs` | host 层 | `IUiTheme` 实现 + XML 加载 + 插件颜色注册 |
| `Client/Source/UI/ThemeLoader.cs` | host 层 | XML 解析 + 文件扫描（含第三方 mod 目录） |
| `Client/Themes/default.xml` | mod 资源 | 默认主题 |
| `Extensions/Chat/Client/ChatTheme.cs` | 插件层 | Chat 业务颜色 static 缓存 |

### 8.2 改动文件

| 文件 | 改动 |
|------|------|
| `Client/Source/Client.cs` | 构造 `UiTheme` 并注册到 `ExtensionHostContext`；`GetExtensionProbeDirectories` 加依赖 Phinix 的 mod 目录 |
| `Client/ClientExtensionAbstractions/ClientExtensionAbstractions.csproj` | 加 `IUiTheme.cs` 编译项 |
| `ChatMessageList.cs` | 颜色引用改 `ChatTheme` + `IUiTheme`；`CachedMessageDisplay` 加 6 字段；渲染逻辑重做 |
| `ChatMainTabProvider.cs` | 回复预览卡片 + @补全浮窗 + 颜色引用改 `ChatTheme` |
| `NoticeBannerProvider.cs` | `new Color()` 改 `ChatTheme` 字段 |
| `ChatSettingsPanelProvider.cs` | 加"重载主题"按钮 |
| `BuiltInChatClientExtension.cs` | `Register` 注册 Chat 业务颜色；`Activate` 调 `ChatTheme.Refresh` |
| `ChatExtension.Client.csproj` | 加 `ChatTheme.cs` 编译项 |
| `Client/Languages/*/Keyed/ChatExtension.xml` | 加主题翻译键 |

### 8.3 不改的文件

| 文件 | 原因 |
|------|------|
| `ServerTab.cs` | host UI 壳，不改（§6） |
| `FrameworkTypes.cs` | 框架类型，不改（§6） |
| `ChatDomainContracts.cs` | 契约层，不改（§6） |

---

## 9. 设计哲学合规

### 9.1 §1.1 插件平权

- `IUiTheme` 是平台层通用接口，所有插件平等使用
- Chat/Trade/第三方 submod 都通过 `RegisterColor` 注册自己的业务颜色
- 没有"Chat 主题"享受特权

### 9.2 §1.3 host 只做通用服务

- `IUiTheme` 只提供通用 UI 语义颜色（`PrimaryText`、`Error`、`Separator`）
- 业务颜色（`chat.mentionText`、`trade.completed`）由插件自己注册
- host 不知道 `mentionText` 是什么意思

### 9.3 §2.3 减少硬编码

- 新增一个插件 = `RegisterColor` 注册自己的颜色，host 代码改动为零
- 主题 key 用 `{插件前缀}.{语义名}` 命名约定，无硬编码

### 9.4 §6 增量更新

- `IUiTheme` 是新增接口，不删不改现有接口
- `GetExtensionProbeDirectories` 加路径不改现有路径
- `Client.cs` 构造函数加 `AddService<IUiTheme>` 是增量
- 每阶段独立可编译、可验证

### 9.5 §8 UI 性能

| 检查项 | 合规 |
|--------|------|
| 通用颜色 `IUiTheme` 属性读 | ⚠️ 接口方法调用——需在 `ChatTheme.Refresh` 时缓存到 static |
| 业务颜色 `ChatTheme` static 字段读 | ✅ 零分配 |
| 正则 `static readonly` 预编译 | ✅ |
| 布局/分组/分流/@高亮在 cache 时做 | ✅ |
| Draw 路径零 `new`、零 LINQ | ✅ |
| 主题加载不在 Draw 路径 | ✅ |

### 9.6 §3.5 错误隔离

- 主题文件 `try-catch`，单文件坏不阻止其他
- 未知 key 忽略
- 颜色值 `Clamp01` 钳制

---

## 10. 分阶段实施

| 阶段 | 内容 | 依赖 | 风险 |
|------|------|------|------|
| ~~0. 修业务 bug~~ | ~~已完成~~ | | |
| ~~1. 修 5 个 UI bug~~ | ~~已完成~~ | | |
| **2. 平台层主题** | `IUiTheme` 接口 + `UiTheme` 实现 + `ThemeLoader` + `default.xml` + `Client.cs` 注册 | 阶段 1 | 低 |
| **3. Chat 主题迁移** | Chat 颜色注册到 `IUiTheme` + `ChatTheme` 缓存层 + 现有颜色引用迁移 | 阶段 2 | 低 |
| **4. 消息渲染美化** | 左右分流 + 分组 + 时间戳移位 + @高亮 + notice 样式 + 回复卡片 | 阶段 3 | 中 |
| **5. 输入区美化** | 回复预览卡片 + @补全浮窗 | 阶段 4 | 中 |
| **6. Steam mod 注入** | `GetExtensionProbeDirectories` 扫描依赖 mod + `ThemeLoader` 扫描第三方主题 | 阶段 3 | 低 |
| **7. 细节打磨** | 空状态 + 侧栏颜色主题化 + Trade UI 主题化 | 阶段 5 | 低 |

---

## 11. 验证清单

### 11.1 主题系统

- [ ] `IUiTheme.PrimaryText` 返回正确默认值
- [ ] Chat `RegisterColor("chat.mentionText", ...)` 后 `GetColor("chat.mentionText")` 返回默认值
- [ ] XML 覆盖后 `GetColor` 返回 XML 值
- [ ] 第三方 submod `RegisterColor("mymod.xxx", ...)` 不影响 Chat 的颜色
- [ ] 点"重载主题" → 所有颜色刷新
- [ ] Draw 路径无 `new Color()`、无 `GetColor` 调用（profiler 确认）

### 11.2 Steam mod 注入

- [ ] 订阅依赖 Phinix 的 mod → mod 的 DLL 被发现并加载
- [ ] 订阅含 `Themes/dark.xml` 的 mod → 主题文件被加载
- [ ] 第三方主题文件不覆盖用户 Config 目录的主题
- [ ] 第三方 mod 不在时 → 不崩溃，用默认主题

### 11.3 消息渲染

- [ ] 自己发的靠右，别人发的靠左
- [ ] 同一人连续消息分组
- [ ] 时间戳在右侧淡灰色
- [ ] @昵称 高亮
- [ ] notice 有金色竖条
- [ ] 回复有蓝色竖条卡片

### 11.4 设计哲学合规

- [ ] host 工程未新增对插件的强引用
- [ ] `IUiTheme` 只有通用颜色，无业务颜色
- [ ] 新增插件不改 host 代码（§2.3 判断标准）
- [ ] host 公开接口未删除/修改
- [ ] `ChatTheme` / `TradeTheme` 是 internal
- [ ] 第三方 submod 不受影响

---

## 12. 默认配色方案

RimWorld 的 UI 基调是深色半透明面板 + 暖灰文字 + 金属/木质感边框。配色应融入这个环境，不突兀，同时保持聊天的可读性和信息层次。

### 设计理念

- **底色**：跟随 RimWorld 面板——深灰蓝半透明，不抢眼
- **文字**：暖白主文本 + 冷灰次要文本，和 RimWorld 的 `Widgets.Label` 默认色一脉相承
- **强调**：蓝→交流、金→公告、绿→成功、红→错误——和 RimWorld 信件颜色体系对齐（蓝信=中性通知、金信=正面、红信=威胁）
- **透明度**：所有背景色 alpha ≤ 0.2，不遮挡底层游戏画面

### 通用颜色（平台层 IUiTheme）

| 颜色 | 用途 | RGBA | 视觉 |
|------|------|------|------|
| PrimaryText | 主文本（消息正文、名字） | `(1.0, 0.95, 0.88, 1.0)` | 暖白，和 RimWorld 标签色一致 |
| SecondaryText | 次要文本（时间戳、引用原文） | `(0.55, 0.52, 0.48, 0.8)` | 暖灰，退后一步不抢眼 |
| Background | 聊天区底色 | `(0.08, 0.08, 0.06, 0.0)` | 透明——让 RimWorld 面板底色透出 |
| Surface | 卡片/引用条底色 | `(1.0, 1.0, 1.0, 0.04)` | 微白叠加，区分层次但不突兀 |
| Separator | 分隔线 | `(1.0, 1.0, 1.0, 0.06)` | 极淡白线，若隐若现 |
| HoverHighlight | 鼠标悬停高亮 | `(1.0, 1.0, 1.0, 0.08)` | 微亮，和 RimWorld `Widgets.DrawHighlight` 一致 |
| Pending | 待确认状态 | `(1.0, 1.0, 1.0, 0.6)` | 灰白半透明——"发了但还没确认" |
| Error | 错误/被拒 | `(0.85, 0.3, 0.25, 1.0)` | 暖红，和 RimWorld 红信一致 |
| Success | 成功 | `(0.3, 0.65, 0.35, 1.0)` | 柔绿，和 RimWorld 绿信一致 |
| Warning | 警告/公告强调 | `(0.9, 0.72, 0.25, 1.0)` | 暖金，和 RimWorld 金信一致 |

### Chat 业务颜色（ChatTheme）

| 颜色 | 用途 | RGBA | 视觉 |
|------|------|------|------|
| MentionText | @昵称 文字 | `(0.45, 0.75, 1.0, 1.0)` | 天蓝——醒目但不刺眼，和蓝信呼应 |
| MentionSelfBg | @自己的消息背景 | `(0.35, 0.35, 0.15, 0.12)` | 极淡暖黄——暗示"有人叫你" |
| SelfMessageBg | 自己发的消息背景 | `(0.15, 0.25, 0.4, 0.1)` | 极淡蓝灰——和别人的消息区分 |
| ReplyQuoteBorder | 回复引用竖条 | `(0.3, 0.5, 0.75, 0.6)` | 蓝灰竖条——引用感 |
| ReplyQuoteBg | 回复引用背景 | `(1.0, 1.0, 1.0, 0.03)` | 几乎透明的白底 |
| ReplyQuoteText | 回复引用文字 | `(0.55, 0.52, 0.48, 0.7)` | 暖灰，和 SecondaryText 一致 |
| NoticeAccent | notice 竖条 | `(0.9, 0.72, 0.25, 0.9)` | 金色——和 Warning 一致，公告感 |
| NoticeBg | notice 消息背景 | `(0.25, 0.2, 0.08, 0.12)` | 极淡暖棕——金色竖条的底色延伸 |
| NoticeBannerBg | 顶部 banner 背景 | `(0.12, 0.1, 0.06, 0.9)` | 深暖棕——比聊天区更深，突出 banner |
| NoticeProgress | banner 进度条 | `(0.9, 0.72, 0.25, 0.7)` | 金色渐隐 |
| InputReplyBorder | 输入区回复条竖条 | `(0.3, 0.5, 0.9, 0.7)` | 亮蓝——和 ReplyQuoteBorder 近似但更亮（输入区是焦点区） |
| InputReplyBg | 输入区回复条背景 | `(0.15, 0.25, 0.45, 0.08)` | 极淡蓝 |

### 视觉效果总览

```
消息区：
  别人的消息：暖白文字，透明背景，左侧对齐
  自己的消息：暖白文字，极淡蓝灰背景，右侧对齐
  @昵称：天蓝加粗
  @自己的消息：暖白文字 + 极淡暖黄背景
  时间戳：暖灰小字，右侧

回复引用：
  蓝灰竖条 | 暖灰引用文字 | 微白底
  ─────────────────────────────
  正文：暖白

notice 消息：
  金色竖条 | 暖白正文 | 极淡暖棕底，居中

notice banner：
  深暖棕底 | 金色竖条 | 暖白文字 | 金色进度条

输入区回复条：
  亮蓝竖条 | 暖白"回复 X" + 暖灰原文 | 极淡蓝底
  ──────────────────────────────────────
  输入框 | 发送按钮
```

### 和 RimWorld 信件体系的呼应

| RimWorld 信件 | 颜色 | Phinix 对应 |
|---------------|------|-------------|
| 蓝信（中性通知） | 蓝色 | @艾特蓝色信封、MentionText 天蓝 |
| 金信（正面事件） | 金色 | notice 公告金色竖条、Warning 暖金 |
| 绿信（利好） | 绿色 | Success 柔绿（交易完成等） |
| 红信（威胁） | 红色 | Error 暖红（消息被拒等） |

这套配色让 Phinix 的 UI 元素和 RimWorld 原有信件/通知体系视觉一致——玩家看到蓝色信封就知道是"有人@我"，看到金色 banner 就知道是"公告"，不需要额外学习。

---

## 13. 相关文档

- [设计哲学.md](../../设计哲学.md) — §1.1/§1.3/§2.3/§3.3/§3.5/§6/§8
- [聊天UI重构与美化方案.md](./聊天UI重构与美化方案.md) — 原始粗方案
- [聊天主题配色方案.md](./聊天主题配色方案.md) — 配色独立方案（本文档替代）
- [聊天增强功能实施方案.md](./聊天增强功能实施方案.md) — 三项功能原始实施方案
- [Phinix附属Mod开发者指南.md](../../Phinix附属Mod开发者指南.md) — 插件挂载点完整列表
