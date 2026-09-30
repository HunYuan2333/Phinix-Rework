# RimWorld Mod 迁移到高版本 .NET 可行性简析

> 评估日期：2026-09-17  
> 范围：广义上的 RimWorld 1.6 游戏内 Mod，不限于 Phinix  
> 本文只讨论可行性，不提供迁移路线图

## 结论

**普通 RimWorld Mod 无法仅靠修改项目目标框架，整体迁移到 .NET 6/8/10 并继续作为游戏内 DLL 运行。**

原因不是编译工具，而是游戏进程决定了运行时：RimWorld 1.6 使用 Unity 2022.3 的 Mono 脚本运行时，不是 .NET 6+ 的 CoreCLR。Unity 2022.3 官方支持的托管插件目标是 .NET Standard 和 .NET Framework profile，并明确把 .NET Core 目标列为不支持。

不过，“使用更现代的 .NET/C# 能力”仍有几种不同程度的可行性：

- **提高 C# 语言版本：高度可行。**可以继续输出 `net472` DLL，同时使用较新的 Roslyn 编译器和一部分新语法。
- **改成 `netstandard2.0`：通常可行。**适合纯逻辑库，但不代表运行时升级。
- **改成 `netstandard2.1`：有条件可行。**RimWorld 1.6 随游戏携带 `netstandard.dll 2.1.0.0`，但 Mod 生态、引用包和其他程序集通常仍以 `net472` 为共同基线。
- **改成 `net48`：可能加载，但收益有限。**它仍属于旧式 .NET Framework/Unity Mono 范畴，不是现代 .NET；使用 Unity 没实现或游戏没携带的 API 仍会在运行时失败。
- **直接改成 `net6.0`、`net8.0` 或 `net10.0`：普通游戏内 Mod 不可行。**这类程序集依赖 CoreCLR 和对应 BCL，Unity Mono 不会因为 Mod 的 TFM 改变而变成新运行时。

总体判定：**可以现代化编译方式、语言版本和部分基础库目标，但不能由单个 Mod 完成真正的运行时迁移。真正把游戏内 Mod 迁移到现代 .NET，需要 RimWorld/Ludeon 先升级 Unity 或脚本运行时。**

## 本机 RimWorld 1.6 的实际环境

对当前安装目录 `D:\SteamLibrary\steamapps\common\RimWorld` 的只读检查结果：

| 组件 | 当前值 |
|---|---|
| UnityPlayer | `2022.3.35` |
| `Assembly-CSharp.dll` | RimWorld `1.6.9676.17735` |
| `mscorlib.dll` | `4.6.57.0` |
| `netstandard.dll` | `2.1.0.0` |
| `System.Runtime.dll` | `4.0.0.0` |
| 脚本运行时目录 | `MonoBleedingEdge` |

这说明游戏确实拥有较新的 Unity Mono API 表面和 .NET Standard 2.1 facade，但关键运行时仍是 Mono。`netstandard.dll` 是 API 合约/转发层，不是 CoreCLR，也不会提供 .NET 8 的 GC、JIT、程序集解析器或完整 BCL。

## 不同“升级”的可行性

| 所谓升级 | 可行性 | 实际含义 |
|---|---:|---|
| `net472` + 新版 SDK/Roslyn | 高 | 构建工具现代化，运行时保持兼容 |
| `net472` + 较新 C# 语法 | 高但需筛选 | 大部分纯编译期语法可用；依赖新运行时的特性不可用 |
| `netstandard2.0` | 中高 | 更小、可移植的公共 API 面，适合游戏无关逻辑库 |
| `netstandard2.1` | 中 | Unity 2022.3 支持，但会离开传统 .NET Framework 兼容交集，生态引用需要实测 |
| `net48` | 中低 | 仍运行在 Unity Mono 上；可能增加 API 误用和第三方 Mod 冲突，不等于现代 .NET |
| `net6.0` / `net8.0` / `net10.0` 游戏内 DLL | 低，通常不可行 | Unity Mono 不支持 .NET Core 目标插件 |
| 现代 .NET 外部进程 + 旧目标游戏桥接 DLL | 技术可行 | 是双进程架构，不是把普通 Mod DLL 迁移到高版本 .NET |
| 在 Mod 内嵌/启动 CoreCLR | 理论可研究，工程上不现实 | 平台相关、部署复杂，并存 GC/线程/程序集边界，不属于正常 Mod 兼容范围 |

## 为什么“能编译”不代表“能加载”

TFM 同时影响编译引用和 NuGet 资产选择。把项目写成 `net8.0` 后，编译器会允许代码引用 .NET 8 的 `System.Runtime`、类型和方法；生成的程序集也会携带相应引用。RimWorld 进程中没有这些 .NET 8 运行时组件，结果通常是：

- 程序集加载失败；
- 找不到 `System.Runtime` 或其他框架程序集的正确版本；
- 类型加载失败；
- 某个方法首次执行时出现 `MissingMethodException` / `TypeLoadException`；
- NuGet 为 `net8.0` 选择的依赖使用 Mono 不认识的运行时能力。

即使一个简单的 `net8.0` DLL 恰好只包含基础 IL，也不能据此宣称兼容；它没有稳定、受支持的游戏运行时契约。

## C# 语言版本可以与目标框架分开

这是最容易混淆、也最有现实价值的一点。

`TargetFramework=net472` 不等于只能使用旧 C#。许多新语法只是由编译器转换成普通 IL，例如部分模式匹配、表达式成员、局部函数等，因此可以使用新 SDK/Roslyn 编译后继续在 Unity Mono 中运行。

但以下功能仍受运行时或 BCL 限制：

- `record` / `init` 需要 `IsExternalInit`，Unity 2022.3 官方文档也明确说明需要自行补类型，而且 Unity 序列化不支持 record。
- covariant returns、module initializers、部分 unmanaged function pointer calling conventions 在 Unity 2022.3 中不受支持。
- default interface methods、依赖新 JIT 的功能、仅存在于 .NET 6+ BCL 的 API 不能因为提高 `LangVersion` 就获得。
- `Span<T>`、async streams、channels 等能力是否可用，取决于游戏自带 API 与额外托管依赖组合，不能只看语法能否通过编译。

因此“现代 C# + 旧兼容 TFM”是现实做法，但需要逐项判断生成 IL 和运行时依赖。

## 其他 Mod/生态实例

### HarmonyRimWorld

HarmonyRimWorld 的公开项目当前使用：

```xml
<TargetFramework>net472</TargetFramework>
<LangVersion>latest</LangVersion>
```

这是很有代表性的组合：保持 RimWorld 运行时共同基线，同时使用最新编译器语言能力。它没有通过改成 `net8.0` 来获得现代语法。

### RimRef

RimWorld 常用引用程序集包 `Krafs.Rimworld.Ref` 的官方说明也建议使用现代 .NET SDK 项目格式，但把目标框架改为 `net472`。这进一步说明“使用新 SDK 构建”与“输出面向新 .NET 运行时的 DLL”是两件事。

### Phinix Rework

本仓库本身也体现了合理边界：游戏内 Client/Extension 保持 `net472`，独立 Dedicated Server 使用 `net10.0`，共享项目按需要多目标编译。服务器能升级到 .NET 10，是因为它自己启动 .NET 10 运行时；游戏内 DLL 没有这个控制权。

## 广义可行性判断

### 可以做到

- 使用 SDK-style 工程、新版 MSBuild、NuGet 和 Roslyn。
- 在验证生成代码与依赖后，使用相当一部分现代 C# 语法。
- 把游戏无关的算法或协议库编译为 `netstandard2.0`，或同时提供多个 TFM。
- 让同一套源码分别服务于旧目标游戏客户端和现代 .NET 工具/服务器。
- 使用兼容目标的新版第三方库，或者选择其 `netstandard2.0` / `net472` 资产。

### 单个普通 Mod 做不到

- 让 RimWorld 的 Unity Mono 原地变成 .NET 8/10 CoreCLR。
- 直接加载只发布 `net6+` 资产的库并假定它能运行。
- 通过更改 csproj 获得 .NET 8 的 GC、JIT、线程池和 BCL。
- 保证 `net48` 或 `netstandard2.1` 中每个 API 都在所有 RimWorld 平台和 Mod 组合中正常实现。
- 在不改变部署模型的情况下，把游戏 UI、Verse/Harmony 补丁和主线程逻辑移到外部现代 .NET 进程。

## 最终评价

从广义上说，RimWorld Mod 可以进行明显的“.NET 现代化”，但需要区分两层：

1. **开发与源码层现代化：可行。**新版 SDK、Roslyn、较新 C# 语法、SDK-style 工程、多目标共享库都可以采用。
2. **游戏内运行时升级：单个 Mod 基本不可行。**普通 Mod 的上限由 RimWorld 所带 Unity/Mono 决定，而不是由 Mod 的 csproj 决定。

所以，如果“迁移到高版本 .NET”指把游戏内程序集整体改为 `net8.0` 或 `net10.0`，答案是 **否**；如果指在保持 Unity 兼容输出的前提下使用现代工具、语言和多目标共享代码，答案是 **是，而且社区已有成熟实例**。

## 资料来源

- Unity 2022.3 .NET profile 支持：支持 .NET Standard 2.1 / .NET Framework profile，不支持 .NET Core 托管插件  
  <https://docs.unity3d.com/2022.3/Documentation/Manual/dotnetProfileSupport.html>
- Unity 2022.3 C# 编译器：Roslyn C# 9 与不支持功能列表  
  <https://docs.unity3d.com/2022.3/Documentation/Manual/CSharpCompiler.html>
- Unity 2022.3 升级说明：.NET Standard 2.1 API 及预编译程序集冲突  
  <https://docs.unity3d.com/2022.3/Documentation/Manual/UpgradeGuide2021LTS.html>
- HarmonyRimWorld 项目文件：`net472` + `LangVersion=latest`  
  <https://github.com/pardeike/HarmonyRimWorld/blob/master/Source/HarmonyRimWorld.csproj>
- Krafs.Rimworld.Ref：建议现代项目仍将 RimWorld Mod 目标设为 `net472`  
  <https://github.com/krafs/RimRef/blob/main/README.md>

