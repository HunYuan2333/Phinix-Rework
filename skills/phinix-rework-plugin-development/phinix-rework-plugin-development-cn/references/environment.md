# 环境、版本与构建引用

按当前任务准备工具；主题包不需要完整游戏环境。版本快照见 [basis.md](basis.md)，它不能代替现场核查。

## 区分版本

| 版本 | 声明 | 用途 |
| --- | --- | --- |
| 产品/文件版本 | `Client/Source/Properties/AssemblyInfo.cs` | 给用户识别构建，不单独决定包兼容性 |
| Host 能力版本 | `Client.CompatibilityVersion` | 与 manifest 的 phinixRange 比较 |
| 客户端 API | `ClientAbstractionsCompatibility.Version` | 与 abstractionsRange 比较，当前 API3 |
| CLR AssemblyVersion | 各 csproj / AssemblyInfo | 程序集绑定身份，不能用全局替换跟随产品号 |
| 插件/主题包版本 | 实际 ZIP manifest 和 Release | 不用 csproj 初始值代替 CI override 后的最终版本 |

本地工作区和引用 DLL 可能不同。先核对 HEAD、status、Common gitlink 及子模块 status，再读取实际 DLL 身份/TFM/能力常量与最终 manifest。工作区修改不会自动进入远端 main；远端发行也不保证玩家已下载更新。

保留现有修改；固定所选 Common 提交，不执行 update --remote。若 vendored protobuf 的 SDK 固定阻止评估，在本次命令进程设置 `MSBuildSDKsPath` 到已安装 SDK 的 Sdks 目录，不修改 pins/global.json。

## 按阶段准备

| 任务 | 所需输入 |
| --- | --- |
| 主题打包/验证 | .NET 10 SDK/运行时、源码或已构建 ManagedPackageTool、配置/XML，无游戏 DLL |
| 模板单版编译 | 对应 Host 公共引用、对应游戏引用、net472 targeting pack 或 .NET 10 SDK |
| 维护脚本双版打包 | Python（版本按脚本实际语法）、.NET 10 SDK、两版游戏引用、匹配 Harmony、固定 Host/Common |
| GitHub 发布 | 用户授权及相应仓库权限；本地开发不要求 gh 或远端凭据 |

先用 `dotnet --list-sdks` / `--list-runtimes` 查实际安装。net472 编译可在具备合法引用/targeting pack 的环境执行，不意味着在 .NET Framework 运行 1.7 DLL。游戏引用与私有编译资产只用于构建，不打包或公开上传。

`ClientPresentation.Contracts` 存放中立呈现/主题/动作契约；`ClientExtensionAbstractions` 仍提供游戏桥、布局和原生控件。它们物理分开，不假设所有 API 类型都在同一 DLL。插件不依赖内部 `Client.csproj` / PhinixClient 实现。

命令示例使用 POSIX shell；PowerShell 使用单行或数组参数，不直接粘贴反斜杠续行。缺少必要引用时报告缺失目标，继续可离线进行的编辑或验证，不下载不明 DLL 冒充引用。
