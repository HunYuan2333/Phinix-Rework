# Phinix-Rework

Phinix RimWorld 1.6 客户端，包含客户端宿主、游戏相关抽象及官方客户端插件。

RimWorld 1.6 client host, game-facing abstractions and official client extensions. Shared source is pinned at `Dependencies/Phinix.Common`; the neighboring standalone Common checkout is not required to build.

> 当前为本地 F6 迁移候选，正式远端尚未切换。以下构建命令在独立 checkout 中验证通过；构建不等于游戏验收。迁移未改变网络协议、存档格式或物品所有权规则。
> Local F6 migration candidate; remote publication/cutover is pending. Builds were verified in independent checkouts and do not certify in-game behavior. Wire identities, persistence formats and item-ownership rules remain unchanged.

## 获取源码 / Checkout

正式远端就绪后 / Once the reviewed remote commits are published:

```bash
git clone --recurse-submodules https://github.com/HunYuan2333/Phinix-Rework.git
cd Phinix-Rework
```

已有 checkout 可执行 / For an existing checkout:

```bash
git submodule update --init --recursive
```

正常获取使用固定 gitlink，不使用 `update --remote`。protobuf 为嵌套子模块，需递归初始化。
Acquisition uses pinned gitlinks; do not use `update --remote`. Initialize nested protobuf recursively.

## 编译 / Build

需要 .NET 10 SDK、.NET Framework 4.7.2 编译支持、项目 NuGet 依赖及本地 RimWorld 1.6/Unity 编译引用。游戏引用放在 `GameDlls/`，Harmony 按工程 HintPath 准备在 `.nuget/Lib.Harmony.2.3.6/lib/net472/`；这些私有编译输入不入 Git 或安装包。
Requires .NET 10 SDK, .NET Framework 4.7.2 targeting support, project NuGet dependencies and local RimWorld 1.6/Unity references under GameDlls. Provision Harmony at the declared .nuget HintPath. Never commit or distribute private game-reference inputs.

在仓库根目录运行 / From the repository root:

```bash
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

`Phinix.sln` 是保留的客户端构建入口；过渡期 `PhinixClient.sln` 与它内容相同，二者均只包含客户端图，修改时保持一致。
Phinix.sln retains the familiar build entry. During transition, PhinixClient.sln is its identical rehearsal/test alias; keep their contents equal. Both contain only the client graph.

## 输出 / Output

```text
Output/phinix-rework/
```

该完整文件夹即客户端模组包。更新时替换整个旧模组文件夹，避免混入旧 DLL；玩家存档和托管插件数据单独保留。编译不会迁移或删除玩家数据，也不会自动启动游戏。
The complete folder is the client mod package. Replace the old mod folder as a unit rather than mixing DLLs. Preserve player saves/managed-plugin data separately; compilation neither migrates/deletes player data nor starts the game.

## 说明 / Notes

- 普通构建会按 `nuget.config` 还原依赖。离线验证依赖本机已准备的 SDK/框架包与 NuGet 缓存，不保证空机器完全离线。
- NU1900 表示 NuGet 漏洞信息获取失败；不等于编译错误，也不代表已完成漏洞审计。
- 保留原有相关历史，不擅自修改 protobuf 的 SDK 固定文件。根 LICENSE 在原仓缺失，迁移候选未自行添加新的许可证；正式发布说明仍需明确。

Normal builds restore through nuget.config. Offline validation requires preinstalled SDK/framework packs and cached packages. NU1900 reports unavailable vulnerability data, not a completed audit. Relevant history is preserved; do not patch vendored protobuf SDK pins. No new root license was invented during extraction.
