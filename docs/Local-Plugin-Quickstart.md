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
    "abstractionsRange": ">=2.0.0 <3.0.0"
  },
  "dependencies": [],
  "externalMods": []
}
```

依赖条目使用 `{"packageId":"author.provider","versionRange":">=1.0.0 <2.0.0","optional":false}`；外部 Mod 条目使用 `{"packageId":"author.mod"}`。配置字段和值由安装同一套清单解析器校验。当前源码使用客户端 abstractions 2.0，只有 Compose 作者入口。

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

## 7. 当前主题与公共控件

新 API 2.0 插件从宿主取得 `IClientTheme`。颜色键通过 `IClientThemeRegistry.RegisterModule(this)` 在激活期取得暂存作用域，注册完毕后 `Commit()` 一次发布，停止时释放。注册所有者绑定实际模块实例；默认键前缀是完整 module ID，内置模块保留原有短前缀。颜色读取及注册使用游戏主线程；文件版本和历史键由宿主输入边界处理，业务无需判断兼容模式。过渡支持 1.9 的维护插件继续将差异留在内部 adapter。

`ClientWidgets` 接受 `IClientThemeReader`，可传入 `theme.Capture()` 得到的不可变绘制快照，提供主题化 Label、LabelEllipses、Button、TextField、Border、Surface、RadioButton，以及带标题/说明/整卡点击区的 RadioOption。Fill 和显式颜色 Label 可消费插件自己的规范色键；其他控件默认读取通用主题。每个调用恢复原 GUI/Text 状态，嵌套安全，禁用按钮不会重新启用父区域。

```csharp
var palette = theme.Capture();
using (new ClientGuiScope(true))
{
    Text.Font = GameFont.Small;
    ClientWidgets.LabelEllipses(titleRect, title, palette);
    string edited = ClientWidgets.TextField(inputRect, text, palette);
    if (ClientWidgets.Button(applyRect, "Apply", palette,
        ClientButtonKind.Primary, enabled: canApply))
        Apply(edited);
    if (ClientWidgets.RadioOption(optionRect, optionTitle, description, selected, palette))
        SelectOption();
}
```

注册示例（`this` 是正在激活的模块实例；默认 namespace 为模块 ID）：

```csharp
registration = hostContext.GetRequiredService<IClientThemeRegistry>().RegisterModule(this);
registration.RegisterColor(ExtensionId + ".bodyText", Color.white,
    label: "正文文字", fallbackKey: "ui.text.primary");
registration.Commit();
// Shutdown: registration.Dispose();
```

主题缺少 bodyText 时继承已解析的主文字，显式插件值优先；未知主题键保留，但未注册时不能读取。失败或未提交作用域不会发布部分颜色。每模块最多 128 个颜色/参数，全局最多 4096 个；主题文件仍最多 512 条显式覆盖。导出保留显式值与未知键，不把全部继承值展开为永久覆盖。

几何仍由现有 ResponsiveFormLayout / ResponsiveToolbarLayout 和虚拟列表负责，滚动/裁剪区域仍须调用方以 try/finally 配对。主题变化只改变显示，不应清空编辑内容、滚动状态或触发业务提交。库存的 InventoryTheme 与交付设置面板是普通内置插件消费示例。

## English

Build the modern DI Example against prepared public host DLLs, with game references used only for compilation. Rename package/module/assembly identities and settings prefixes in an independent copy. Configure compatibility, dependencies and external mods in `example/package-config.json`. Run `pack.py` to build only the plugin, package and preflight it; or run `ManagedPackageTool --validate ZIP` offline. Optional repeatable `--host-assembly` inputs check CLR references, while actual installation still validates the live host and dependencies.

Enable RimWorld developer mode, choose Store → dev: Install local plugin ZIP (overflow on narrow windows), select an absolute ZIP path, review its ID/version/digest and restart. Extension management shows local provenance and offers a copied summary with both installed and current-session digests. Same-version different-digest revisions are supported; duplicate digests, downgrades and replacement of another source are rejected. Existing local packages remain manageable in both the store and package manager after developer mode is disabled. Local packages have a coral terminal badge; delisted installed packages retain management actions. Canceling uninstall keeps the package disabled and preserves module choices. Before downgrading to a schema-1-only host, remove local packages with the newer host and restart to complete removal; do not downgrade with unfinished transactions. No DLL hot reload, additional Phinix NuGet SDK, GitHub credentials or Index approval is needed for local iteration. Complete the game acceptance steps before unblocking skills or publishing through main → Release → Index.


## 主题元数据与编辑语义（T2）

RegisterModule 可提供 ownerLabel；RegisterColor / RegisterFloat 可提供 label、group、description。它们可以是可读文字或由 Verse 翻译资源提供的键，编辑器按当前语言显示；元数据由插件声明，Host 不按插件业务 key 分支。名称最多 256 字符、类别 160、说明 2048。

IClientThemeCatalog.Parameters 提供数值定义与范围；PreviewColor / PreviewFloat 提供来源。草稿 Floats 与 InheritedKeys 只在 Apply 后发布，服务验证参数范围、冲突和有限数值。对 key 设置显式覆盖前须删除同 key 的 InheritedKeys；恢复继承前须移除 Colors/Floats 中同 key 的值，避免歧义。

清除用户覆盖会恢复文件值；恢复继承会跳过该键的文件层。主题 v3 以 inherit 元素保留意图，仅含原有 color/param 的导出仍用 v2；不得依赖旧解析器忽略新指令。历史主题/设置格式归一化仍只存在于 ThemeInputAdapter。接口为未发布 API 2.0 的同批演进，当前内置调用方与 fixtures 同步；维护插件的 1.9 adapter 不静态引用本批主题接口。


## 可运行的普通主题示例（T4 本地候选）

Phinix-Example-Plugin/example/ExampleTheme.cs 展示借用 IClientTheme/IClientThemeRegistry、在 Activate 注册实际模块实例、批量 Commit、作用域释放注册及绘制时 Capture。ExampleTab/ExampleSettingsPanel 使用公共控件，并实际消费模块 panel/counter 键；详见该仓库 Theme-Example.md。本地 Example 1.0.3 要求当前 API 2.0。

红包/人才贸易 1.1.2 是维护插件兼容样例：新接口发现仅在内部 ThemeAdapter，按 1.9 编译、旧端默认配色、当前端普通注册。普通新插件不复制历史版本判断，也不依赖 Host 内部 UI。缺少专属主题键正常回退，未知键保留不等于可以直接读取。候选编译和静态包检查通过，运行时与游戏验收仍待进行。


## 主线程任务结果（A1 本地阶段）

当前 Example 1.0.4 使用 IClientTaskService.ForModule(this) 在 Activate 获取独立 owned task scope，Submit 返回可观察 handle；Shutdown 释放 scope，Host 服务保持 borrowed。Capture 提供任务状态，成功仅指回调执行完成。满队列明确拒绝、排队取消/模块停止撤销、回调失败可观察；不在主线程等待。旧 void dispatcher 保留为 adapter，共用一份队列，不再挤掉最旧动作。

必须使用本批完整未发布 API 2.0 客户端，不混装早期 SDK/Host；普通示例不带 1.9 兼容分支。说明及尚未执行的游戏步骤见 branch-local/dev/Client-Task-Api-Implementation.md。


## 强类型操作接入点（实验规范）

当前 IClientExtensionPointService 在 Activate 以实际模块实例返回 owned scope。定义方使用自己的模块前缀和 public Contracts 上下文；贡献方用同一个点名/类型追加 factory。菜单、工具栏、行按钮复用操作集合，不依赖具体 private UI。

```csharp
var scope = host.GetRequiredService<IClientExtensionPointService>().ForModule(this);
// Defining plugin:
var point = scope.DefineActions<MyContext>(ExtensionId + ".actions");
// Contributing plugin (a different module may reference the owner's Contracts):
scope.ContributeActions<MyContext>("owner.module.actions", context => new[] {
    new ClientActionDefinition("open", "Open details", () => OpenDetails(context.Id))
});
// View: own and dispose the collected set on close/replacement.
var actions = point.Collect(snapshot, () => IsStillCurrent(snapshot));
// Use action metadata to draw; clicks call TryExecute. Completed != server confirmation.
// Shutdown: scope.Dispose();
```

ExampleActions.cs 是可操作范例，定义自己的 context/点，示范计数、禁用和重置确认；其 owned FloatMenu 在关闭时释放 set，模块停止撤销贡献。尚未表示所有内置菜单已暴露；只要声明方接入公开 point，第三方就能按同一规范贡献。缺 optional point 可等待定义，已经定义的错误类型立即拒绝。更多限额/结果见 branch-local/dev/Plugin-Api-Integration-Standard.md。每项新增接口不再要求内置消费者，先用 Example 实验。
