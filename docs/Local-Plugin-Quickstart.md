# 本地插件开发闭环 / Local Plugin Quickstart

本地开发不要求 GitHub、token、Release 或 Index。准备一次宿主公开 DLL，之后只编译插件。示例仓库位于 `HunYuan2333/Phinix-Example-Plugin`，入口为 `example/ExampleExtension.cs` 的 `ClientExtensionModule.Compose`。

## 1. 准备引用

使用已安装宿主的 `Common/Assemblies`，或已构建客户端中的 `Utils.dll` 与 `ClientExtensionAbstractions.dll`。示例不再用 ProjectReference 重编宿主。游戏引用只参与编译，不进入 ZIP。

从宿主源码准备工具时，保持客户端固定版本和 Common/protobuf gitlink：

```sh
git submodule update --init --recursive
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj -c Release -m:1
```

这只构建无游戏依赖的打包工具。不要使用 `git submodule update --remote`，不新增 Phinix NuGet SDK。

## 2. 修改独立身份

复制 Example 到独立目录，修改 `Example.csproj` 中的 `AssemblyName`、`PluginPackageId`、`PluginDisplayName`，以及源码命名空间、`PhinixExtension` 特性、`ExtensionId`、设置键前缀和设置 SectionId。不要编辑构建后的 `manifest.json`；由工具生成。

`example/package-config.json` 声明完整兼容范围、插件依赖和外部 Mod：

```json
{
  "compatibility": {
    "rimWorldVersions": ["1.6"],
    "phinixRange": ">=0.9.7 <1.0.0",
    "abstractionsRange": ">=1.9.0 <2.0.0"
  },
  "dependencies": [],
  "externalMods": []
}
```

依赖条目使用 `{"packageId":"author.provider","versionRange":">=1.0.0 <2.0.0","optional":false}`；外部 Mod 条目使用 `{"packageId":"author.mod"}`。配置字段和值由安装同一套清单解析器校验。现代 DI 示例需要客户端 abstractions 1.9。

## 3. 编译、打包和离线预检

在 Example 目录执行：

```sh
python3 pack.py \
  --phinix-root /absolute/path/to/Phinix-Rework \
  --host-assemblies /absolute/path/to/phinix-rework/Common/Assemblies \
  --game-references /absolute/path/to/RimWorld/Managed \
  --output /absolute/path/to/my-plugin-build-1.zip
```

`--host-assemblies` 可识别宿主发行目录中的数字前缀 DLL。不提供时引用客户端已构建的公开 DLL。`--packager /path/ManagedPackageTool.dll` 可复用工具产物；已有工具不会每次重建。游戏引用目录缺少 `mscorlib.dll` 时，工具优先使用本地 net472 编译引用缓存；也可指定 `--framework-references /path/to/net472/references`。

`pack.py` 只构建 Example，随后打包并自动预检。直接调用通用工具：

```sh
dotnet /path/ManagedPackageTool.dll \
  --assembly /path/My.Plugin.dll --package-id author.plugin \
  --name "My Plugin" --version 1.0.0 --config /path/package-config.json \
  --output /path/build-1.zip
dotnet /path/ManagedPackageTool.dll --validate /path/build-1.zip
```

`--validate` 无网络、无安装、无 DLL 执行。输出包 ID、版本、ZIP/清单摘要、模块、依赖及兼容范围。可重复传 `--host-assembly DLL` 检查实际 CLR 引用。未提供宿主程序集时只声明静态校验通过；游戏安装仍检查实际宿主、启用依赖、程序集/模块冲突和文件所有权。首次构建可能需要恢复普通编译工具依赖，游戏内导入不依赖网络。

## 4. 侧载与诊断

1. 在 RimWorld 开启开发者模式，进入 Phinix 商店，选择“dev: 安装本地插件 ZIP”；窄窗口从溢出菜单进入。
2. 输入绝对 ZIP 路径，或输入目录并点击“浏览”后选择 ZIP。只浏览显式指定的目录，Windows/Linux 使用相同交互。
3. 核对包 ID、版本和 SHA-256 后确认。文件先复制到 Phinix 临时区域，后续安装使用冻结的已校验内容。
4. 重启游戏。在扩展管理确认“本地开发”来源、当前/下一启动状态与实际构建摘要。点击“复制摘要”导出包、宿主、模块发现/激活、依赖和拒绝原因码；不复制日志、路径、凭据、玩家或存档数据。
5. 测试示例 Tab、设置、翻译和主线程回调。关闭开发者模式后，已安装开发包仍可启停、卸载。

导入和最终确认检查开发者模式，执行前也在主线程重新捕获模式。取消/关闭模式不撤销已提交事务，重启恢复仍遵守提交决定。

## 5. 迭代和卸载

同本地包 ID、同版本但不同 ZIP 摘要是新开发修订。相同摘要拒绝重复导入，版本降级拒绝；替换前现有包必须启用且内容可信。正式来源同 ID 包拒绝覆盖，需正常卸载并重启后再导入，或为开发构建使用独立 ID。

重新编译，使用新输出文件路径打包，导入并重启。管理摘要同时给出已安装 ZIP 和当前会话 ZIP 摘要：它们可以在重启前不同。已加载程序集不会热替换。修订沿用现有事务，在提交前中断恢复旧包，在提交后中断完成新包。

商店和扩展管理的“安装包”页均可发起卸载，重启后移除包；插件自己的持久化数据遵守现有保留规则。取消卸载会保留停用状态及各模块选择。依赖插件仍启用时不能破坏它的依赖。本地包在商店使用珊瑚红终端图标，与正式托管包区分；目录撤下的已安装包也保留管理入口。

## 6. 游戏验收与正式发布

自动化回归不能替代游戏验收。发布前实际检查：开发模式开关、窄窗口、重启加载新构建、五轮切档/断线后无重复订阅、翻译和设置保留、启停/卸载与进程中断恢复。

本地收据使用 `schemaVersion: 2`、`sourceKind: local-development`，不记录伪造的目录审批证据。既有正式 `schemaVersion: 1` 收据保持可读。正式发行另走 main → Release → Index；本地 ZIP 不自动成为正式包。上述游戏验收完成前，skill 和新的作者工具仍保持阻塞。

降级限制：仅支持旧 schema 1 的宿主不能管理本地 schema 2 收据。降级前应在新宿主卸载本地包并重启完成移除，不要携带未完成事务降级。同步修订不改变公共接口或记录格式；完整说明及验收项见 [Store-Management-Synchronization.md](branch-local/dev/plugin-store/Store-Management-Synchronization.md)。

## English

Build the modern DI Example against prepared public host DLLs, with game references used only for compilation. Rename package/module/assembly identities and settings prefixes in an independent copy. Configure compatibility, dependencies and external mods in `example/package-config.json`. Run `pack.py` to build only the plugin, package and preflight it; or run `ManagedPackageTool --validate ZIP` offline. Optional repeatable `--host-assembly` inputs check CLR references, while actual installation still validates the live host and dependencies.

Enable RimWorld developer mode, choose Store → dev: Install local plugin ZIP (overflow on narrow windows), select an absolute ZIP path, review its ID/version/digest and restart. Extension management shows local provenance and offers a copied summary with both installed and current-session digests. Same-version different-digest revisions are supported; duplicate digests, downgrades and replacement of another source are rejected. Existing local packages remain manageable in both the store and package manager after developer mode is disabled. Local packages have a coral terminal badge; delisted installed packages retain management actions. Canceling uninstall keeps the package disabled and preserves module choices. Before downgrading to a schema-1-only host, remove local packages with the newer host and restart to complete removal; do not downgrade with unfinished transactions. No DLL hot reload, additional Phinix NuGet SDK, GitHub credentials or Index approval is needed for local iteration. Complete the game acceptance steps before unblocking skills or publishing through main → Release → Index.
