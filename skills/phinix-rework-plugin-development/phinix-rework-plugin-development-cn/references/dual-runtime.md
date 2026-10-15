# 双版本构建与包结构

同一插件产品版本交付一份 ZIP，内含两套独立编译产物：1.6 使用 net472，1.7 使用 net10.0。安装方按实际游戏版本选择变体；不是把同一 DLL 放到两个目录。

## 三种结构不要混淆

| 产物 | 选择者 | 结构 |
| --- | --- | --- |
| 主模组 | RimWorld 的 About/LoadFolders 及 Host 自有加载边界 | 使用当前主模组打包脚本，不仿造托管 ZIP |
| 托管插件 | Common 包解析器与 Host 安装/启动 | 根 schemaVersion 2 manifest，variants 为完整的 schema1 声明，文件分别在 `1.6/`、`1.7/` |
| 数据主题 | 主题包读取器 | schemaVersion 1，manifest + Themes XML，无运行时变体 |

```text
my-plugin.zip
  manifest.json
  1.6/Assemblies/Author.Minimal.Plugin.dll
  1.7/Assemblies/Author.Minimal.Plugin.dll
```

若有本地化，资源在相应游戏前缀下。变体内部声明仍用 `Assemblies/...` 和 `Resources/...`；读取器加上游戏前缀。外层 JSON 仅有 `schemaVersion`、`variants`；不要创建 `variants/net472/` 目录或读取不存在的顶层 `manifest.version`。

schema2 的 `variants` 包含包 ID、名称、版本、targetFramework、compatibility、modules、assemblies 等完整 schema1 声明。用真实打包工具生成，勿手填虚构 length、sha256 或省略号。C# 解析对象可用 `ForGame(game).Version`；读取原始 JSON 时先核对所有变体版本一致。

## 构建与预检

准备合法且版本准确的两版游戏引用、各自 Host 公共引用以及匹配的 Harmony。1.7 常见引用为 Assembly-CSharp、UnityStubs、Waylith 等，以本地 DLL 和构建脚本为准，不固定猜测 Harmony 版本。

独立插件的项目位于 `Client/` 或 `example/`。最小模板放入 `example/` 后，可调用 Host 当前维护的辅助脚本：

```sh
python3 /path/to/Phinix-Rework/tools/compatibility/build-managed-dual.py --host /path/to/Phinix-Rework --plugin /path/to/my-plugin --game16 /path/to/rw16-references --game17 /path/to/rw17-references --harmony17 /path/to/rw17/0Harmony.dll --version 0.0.1 --output /path/to/artifacts/my-plugin-0.0.1.zip
```

输出和工作目录置于源码/引用树之外，并使用新路径。若插件已经有 `pack.py`，先核对该脚本实际参数；维护插件目前提供 `--phinix-root` 等包装参数，但它不是所有新项目自带的通用命令。

**辅助脚本当前的限制**：它按维护插件布局查工程/身份，并生成固定 `>=0.10.1 <1.0.0`、API `>=3.0.0 <4.0.0`、空包依赖和 externalMods 的配置。不能假定它自动读取作者 package-config 或发现所有第三方私有库。有非默认能力范围、包依赖、外部 Mod、额外程序集时，先核对/调整自己的打包流程或显式使用 ManagedPackageTool，不用空依赖包冒充完整交付。

```sh
dotnet /path/to/ManagedPackageTool.dll --validate /path/to/artifacts/my-plugin-0.0.1.zip --game-version 1.6
dotnet /path/to/ManagedPackageTool.dll --validate /path/to/artifacts/my-plugin-0.0.1.zip --game-version 1.7
```

工具输出 `staticValidation="passed"`；只有显式提供所需的 `--host-assembly DLL`（可重复）时，`hostReferencesChecked` 才为 true。未传引用的结构检查不能证明引用闭包。维护辅助脚本包含两版引用审计与预检，但仍不等于游戏通过。


构建工具本身可在 Host 根目录运行 `dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj -c Release`，默认输出到该工程的 `bin/Release/net10.0/`。它依赖 Common 与 vendored protobuf；保留精确子模块版本，缺缓存时按授权恢复依赖，不改 pins 绕过。评估或保留基线时在隔离副本构建。

## 契约与依赖边界

`ManagedExtensionManifestReader.SameVariantContract` 检查包 ID/名称/版本、Host/API 范围、程序集完整身份以及模块/依赖/externalMods 声明一致。两版 DLL 字节可不同，公开契约不可随意分叉。

不打包游戏、Unity、Harmony、Host/契约或 Host 已提供的库。`ProtectedAssembly` 等校验以当前 Common 受保护名称规则为准，不复制过期白名单。插件私有库需合法分发、显式声明并由两版输出/闭包审计确认，工具不会凭想象补齐依赖。

游戏差异集中到窄 adapter；删去 1.6 支持时再移除对应产物和选择分支，不让业务层依赖旧渲染实现。安装/更新/移除 DLL 后重启进程，是宿主加载/绑定策略，不是“CLR 完全不能动态加载 DLL”。分别验证两版的实际安装和功能。

源码：`tools/compatibility/build-managed-dual.py`、`Extensions/PluginStore/Tools/ManagedPackageTool/{VariantPackageCommand.cs,OfflinePreflight.cs}`、Common `ManagedExtensionManifestReader.cs` / `ManagedExtensionZip.cs`。
