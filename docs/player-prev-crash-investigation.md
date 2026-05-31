# Player-prev.log 崩溃排查与修复方案

> 2026-05-31，基于 main 分支 Player-prev.log 排查。
> **状态：排查完成，修复方案已确认，等待实施。**

---

## 1. 现象

玩家 mod 启动后 UI 只剩灰色主框，聊天、交易扩展全部注册失败。

日志关键行 [Player-prev.log:321-337]：

```
Error while instantiating a mod of type PhinixClient.Client:
  System.Reflection.TargetInvocationException
  ---> System.ArgumentException: Invalid path
  at System.IO.Path.GetDirectoryName (System.String path)
  at PhinixClient.Client+<GetExtensionProbeDirectories>d__83.MoveNext ()
```

## 2. 根因

### 2.1 直接原因

[Client.cs:398-414] 的 `GetExtensionProbeDirectories()` 中：

```csharp
string clientAssemblyDirectory = Path.GetDirectoryName(typeof(Client).Assembly.Location);
```

`typeof(Client).Assembly.Location` 返回了**空字符串 `""`**，`Path.GetDirectoryName("")` 直接抛出 `ArgumentException: Invalid path`。

下面 L403 的 `string.IsNullOrEmpty` 防护来不及执行——异常在 L400 就发生了。

### 2.2 为什么 Assembly.Location 会是空字符串

在 RimWorld mod 加载环境中，以下情况会导致 `Assembly.Location` 返回 `""`：

- **Prepatcher**（该玩家已启用）—— 将程序集反序列化、打补丁、再序列化加载，程序集脱离原始文件路径
- **`Assembly.Load(byte[])`** —— 某些 mod 加载器用字节数组方式加载程序集
- **自定义 AssemblyLoadContext** —— RimWorld 自身的 mod 加载在某些边缘情况下会使 Location 变为空

这些在 RimWorld mod 生态中属于已知环境特征，不是罕见 corner case。

### 2.3 崩溃链路

```
Client 构造函数 (L87)
  → L98-126: netClient / authenticator / userManager / extensionHostContext 初始化 ✅
  → L127: GetExtensionProbeDirectories() 💥 BOOM (L400)
  → 构造函数终止
  → L128: ExtensionAssemblyLoader.LoadAssemblies()  ❌ 未执行
  → L137: frameworkClient = new PhinixFrameworkClient() ❌ frameworkClient = null
  → L141: ApiRegistry.RegisterApi("builtin.host") ❌ ExtensionManagerTab 未注册
  → L144-191: 所有事件订阅 ❌ OnAuthenticationSuccess 等全部未绑
  → MainTabProviders / SidebarProviders 返回 Array.Empty<>() ❌ 空界面
```

Chat 和 Trade 扩展是**编译成独立 DLL** 存放于 `Common/Extensions/` 下，由 `ExtensionAssemblyLoader.LoadAssemblies` 按需加载进 AppDomain。此步骤未执行 → 这些 DLL 从未被加载 → `PhinixExtensionRegistry.DiscoverExtensions` 扫描 AppDomain 时找不到任何扩展类型 → `frameworkClient` 构造出来也会发现零个扩展，Chat/Trade 功能仍然缺失。

**结论：`GetExtensionProbeDirectories` 崩溃 = mod 完全不可用，不存在"部分降级"。**

## 3. 修复方案

### 3.1 核心思路

用 `content.RootDir`（RimWorld 官方稳定入口）替代 `Assembly.Location`（在 Prepatcher 等环境下不可靠）作为路径来源。

构造函数已有 `content` 参数（[Client.cs:87]）：

```csharp
public Client(ModContentPack content) : base(content)
```

`content.RootDir` 是 `DirectoryInfo`，由 RimWorld mod 加载器在构造函数执行**之前**设置好，始终指向 mod 根目录。发布后的文件布局证实了这一推导：

```
phinix-rework/                          ← content.RootDir
├── 1.6/Assemblies/13-PhinixClient.dll  ← Assembly.Location 正常时指向这里
├── Common/
│   ├── Assemblies/                      ← 依赖 DLL
│   └── Extensions/                      ← Chat/Trade 扩展 DLL
```

`GetExtensionProbeDirectories` 用 `Assembly.Location → Path.GetDirectoryName → ../..` 的唯一目的就是**推导出 mod 根目录**。既然 `content.RootDir` 直接提供根目录，无需绕路。

### 3.2 具体改动

**文件：[Client/Source/Client.cs]**

**改 `GetExtensionProbeDirectories`：**

```csharp
// 当前（主路径）:
private static IEnumerable<string> GetExtensionProbeDirectories()
{
    string clientAssemblyDirectory = Path.GetDirectoryName(typeof(Client).Assembly.Location); // ← 💥
    // ...

// 修复后:
private static IEnumerable<string> GetExtensionProbeDirectories(string modRootDir = null)
{
    // Assembly.Location 在 Prepatcher / AssemblyLoadContext 等环境下可能返回空字符串
    string clientAssemblyDirectory = null;
    try { clientAssemblyDirectory = Path.GetDirectoryName(typeof(Client).Assembly.Location); }
    catch (ArgumentException) { }

    if (!string.IsNullOrEmpty(clientAssemblyDirectory))
    {
        // 正常路径：行为完全不变
        yield return clientAssemblyDirectory;
        yield return Path.GetFullPath(Path.Combine(clientAssemblyDirectory, "..", "..", "Common", "Assemblies"));
        yield return Path.GetFullPath(Path.Combine(clientAssemblyDirectory, "..", "..", "Common", "Extensions"));
    }
    else if (!string.IsNullOrEmpty(modRootDir))
    {
        // 降级路径：从 ModContentPack.RootDir 直接推导
        yield return Path.Combine(modRootDir, "Common", "Assemblies");
        yield return Path.Combine(modRootDir, "Common", "Extensions");
    }

    if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory))
    {
        yield return AppDomain.CurrentDomain.BaseDirectory;
    }
}
```

**改构造函数调用（L127-129）：**

```csharp
// 当前:
Verse.Log.Message($"[Phinix] Loading extensions, probe dirs: {string.Join("; ", GetExtensionProbeDirectories())}");
ExtensionAssemblyLoader.LoadAssemblies(
    GetExtensionProbeDirectories(),

// 修复后:
Verse.Log.Message($"[Phinix] Loading extensions, probe dirs: {string.Join("; ", GetExtensionProbeDirectories(content.RootDir?.FullName))}");
ExtensionAssemblyLoader.LoadAssemblies(
    GetExtensionProbeDirectories(content.RootDir?.FullName),
```

### 3.3 为什么安全

| 维度 | 评估 |
|---|---|
| **正常路径不受影响** | `Assembly.Location` 可用时走原逻辑，行为零变化 |
| **`content.RootDir` 可靠性** | RimWorld 在调用 `Mod` 构造函数**之前**就设好，不存在为 null 的情况 |
| **路径结构确定性** | mod 的 `Common/Extensions/` 目录结构由构建脚本决定，不会变 |
| **不会重复加载** | `ExtensionAssemblyLoader.tryLoadAssembly` 已有去重逻辑（检查 AssemblyName 是否已存在于 AppDomain，[ExtensionAssemblyLoader.cs:97-101]） |
| **方法签名兼容** | 参数带默认值 `= null`，现有调用方不受影响 |
| **Server 端无此问题** | Server 端已使用 `AppContext.BaseDirectory`（[Server.cs:129]），不依赖 `Assembly.Location` |

### 3.4 为什么不是"降级"而是"修复"

两个路径推导出的 `Common/Extensions/` 目录是**同一个物理目录**。降级路径（`content.RootDir`）和正常路径（`Assembly.Location → ../..`）最终枚举的 DLL 完全一致。唯一区别是前者不依赖运行时程序集的加载方式，更健壮。

## 4. 与设计哲学的对照

### 4.1 符合性

| 设计哲学条目 | 结论 | 说明 |
|---|---|---|
| §1.1 插件平权 | ✅ 无影响 | 不改变插件发现/注册/激活路径 |
| §1.2 host 不依赖插件 | ✅ 无影响 | 不加插件引用 |
| §1.3 host 只做通用服务 | ✅ 符合 | 引导启动是宿主自身职责 |
| §2.1 松耦合 | ✅ 强化 | 从脆弱的 `Assembly.Location`（reflection）迁移到稳定的 `content.RootDir`（框架约定） |
| §2.2 层次化 | ✅ 符合 | `ModContentPack` 是 RimWorld 宿主层参数，用于宿主层自身定位，不跨层 |
| §2.3 减少硬编码 | ✅ 符合 | 用参数化的 `content.RootDir` 替换隐式的 `Assembly.Location → ../..` 推导 |
| §3.5 错误隔离 | ⚠️ 有缺口 | 见 §5 |
| §6 渐进式迁移 | ✅ 符合 | 正常路径零变化，仅追加 fallback |

### 4.2 §3.5 的盲区

设计哲学 §3.5 "错误隔离与重试机制" 只覆盖了**管线层**的错误隔离（消息 handler、PipelineRunner、Protobuf 解析），但此 bug 的爆炸点**早于任何管线存在**——它在 `Client()` 构造函数里就炸了，mod 连构造都没完成。

设计哲学缺少对 **"宿主启动/引导阶段的韧性"（bootstrap resilience）** 的规定。详见下方 §5 的设计哲学更新建议。

## 5. 设计哲学更新建议

建议在 `设计哲学.md` 的 **§3 关键设计决策** 下新增一条，或在 `§3.5 错误隔离与重试机制` 开头增加引导阶段相关描述：

### 建议新增：§3.9 启动期文件定位与引导韧性

**启动期文件定位：**

宿主定位自身运行时目录时，优先使用 RimWorld 框架提供的稳定入口（`ModContentPack.RootDir`），而非依赖 .NET 运行时的 `Assembly.Location`。后者在 Prepatcher、自定义 AssemblyLoadContext 等环境下可能返回空字符串或无效路径，导致构造函数崩溃。

- Client 端：`content.RootDir` → `Common/Extensions/`、`Common/Assemblies/`
- Server 端：`AppContext.BaseDirectory` → `Extensions/`、`UserExtensions/`

**反模式**：仅以 `Assembly.Location` 作为唯一路径来源，无 fallback。

**引导阶段韧性：**

构造函数（引导阶段）的失败同样受 §3.5 错误隔离原则约束——路径解析失败不应导致整个 mod 不可用。引导代码应提供降级路径：`Assembly.Location` 不可用时，回退到 `content.RootDir` 推导。引导阶段的任何可恢复错误都不应使 mod 进入僵尸状态（实例存在但功能不可用）。

**反模式**：构造函数中无 try-catch 的隐含假设（如"程序集一定有文件路径"）；引导失败后 mod 静默退化而非显式报告。

## 6. 实施计划

| 步骤 | 文件 | 说明 |
|---|---|---|
| 1 | [Client/Source/Client.cs:398-414] | 重构 `GetExtensionProbeDirectories`：加 `modRootDir` 参数、加 try-catch、加 fallback |
| 2 | [Client/Source/Client.cs:127-129] | 调用处传入 `content.RootDir?.FullName` |
| 3 | [docs/设计哲学.md] | 在 §3 下新增 §3.9 启动期文件定位与引导韧性（或合并入 §3.5） |
| 4 | 验证 | 用 Prepatcher 环境测试；正常环境回归测试 |
