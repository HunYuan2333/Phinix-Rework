# 环境与版本定位

进入构建、能力不明、缺工具或准备发布时读。先参考 [依据与状态](basis.md) 确认本 skill 的能力前提。

## 定位目标

请用户提供或从现有上下文查明宿主安装版本、RimWorld 版本、公开 DLL 的程序集版本/来源与摘要、插件源码/工具版本。“abstractions 1.9”只能证明 DI 契约版本，不能单凭它判断侧载 UI 或工具也已发行。查看目标版本 Quickstart、工具参数与实际公开接口。旧宿主没有 Compose/侧载时停止该闭环：提供升级到已确认支持版本或取得维护者已验证固定开发快照的路线；用户必须保留旧宿主时，单独核对该版本传统入口/测试方式，不假称新功能存在。

有源码仓时先读各仓及父目录适用 AGENTS，再只读检查：

```sh
git branch --show-current
git rev-parse HEAD
git status --short --untracked-files=all
git diff --stat
git diff --cached --stat
git ls-files --stage Dependencies/Phinix.Common
git -C Dependencies/Phinix.Common rev-parse HEAD
git -C Dependencies/Phinix.Common status --short --untracked-files=all
git -C Dependencies/Phinix.Common ls-files --stage Dependencies/protobuf
```

按需读取具体 diff/未跟踪源码；优先客户端实际引用的 Common 子模块，不假设同级 Common 相同。进一步核对 protobuf 实际 HEAD/状态。不要 reset、覆盖切分支或清理未跟踪文件。仓库外的发行 DLL 与源码 HEAD 可能不同，分开记录。需要远端现状时用只读查询验证；失败就报告未知，不把 origin/main 称作最新远端。

已具备 Git/网络且在安全的独立源码目录获取固定宿主时，按核实后的完整提交 checkout，再用 `git submodule update --init --recursive` 获取该提交 gitlink（包括 protobuf），不用浮动分支追踪。现有脏工作区不执行覆盖切换。无 Git/网络只能复用已验证的固定源码快照或已有引用；普通源码 ZIP 未必含子模块，必须核验来源、提交和完整性。

## 阶段检查

| 阶段 | 现在需要 | 缺失时的实际路线 |
| --- | --- | --- |
| 理解需求 | 已有文档/源码 | 无网络仍可设计，未知能力先列出 |
| 编译插件 | 对应 .NET SDK/MSBuild；公开宿主 DLL；游戏/Unity、net472 mscorlib 编译引用；用 Harmony 才需对应引用 | 优先复用合法已安装/已构建宿主。缺 DLL 则停在编译前，指导从用户自己的游戏安装/合法引用准备 |
| 使用 Example pack.py | Python 3 与上列环境 | 验证 `python3 --version` 或 Windows `py -3 --version`；无 Python 可独立 `dotnet build` 后直接调用 ManagedPackageTool，不虚构 pack 参数 |
| 获取固定源码 | Git；首次获取需要网络 | 验证 `git --version`；无 Git 可使用已验证且含固定子模块的维护者快照，不能从缺契约的 ZIP 猜 API |
| 工具/预检 | 当前工具需要 .NET 10；插件目标 net472 | 验证 `dotnet --list-sdks`/`--list-runtimes`，或复用可信已构建工具；不能把工具框架改成游戏插件目标 |
| 正式发布 | Git、gh 登录与 GitHub/所用来源端点 | 此时才查 `gh --version`、`gh auth status` 及可达性。没有 gh 可用获授权的浏览器发布；无需因此阻塞本地开发 |

获取指导用 [Python 官方下载](https://www.python.org/downloads/)、[Git 官方安装](https://git-scm.com/downloads)、[.NET 官方下载](https://dotnet.microsoft.com/download)、[gh 安装与认证](https://cli.github.com/manual/)。先指导用户选择系统对应安装方式及以上验证命令；未授权不安装软件、修改系统或登录。Windows 用 PowerShell 的参数数组/单行命令或反引号续行，不能直接执行 POSIX 反斜杠续行；路径含空格要作为一个参数传递。命令示例中的路径是待替换参数，不是维护者固定目录。

首次 .NET 依赖恢复可能联网；离线仅在所需缓存、SDK 和固定引用齐全时成立。protobuf vendored SDK pin 不修改，按对应官方开发文档采用命令进程级 SDK 路径替代。没有额外 Phinix NuGet SDK 或离线下载器。

维护者三个插件 main 云端引用私有编译仓与 `BUILD_REFERENCES_TOKEN`。第三方自行提供合法编译引用与安全获取方式，不要求访问该私仓或索取该 token；不读取/打印/收集凭据。游戏、Unity、宿主、Harmony DLL 仅参与编译，不进入插件 ZIP 或公开引用资产。
