# 两版日志共同的 SDK 类型加载阻断

用户实机测试 `local-api3-20261014-03`：1.7日志确认1.7.4977 rev1248与Harmony3.0.0.0 rc.2；随后1.6日志确认为1.6.4871 rev600。当前安装目录Version.txt另显示1.6.4871 rev598，不能据此改写日志中的实际运行版本。1.7安装中的SDK、呈现合同和Host三个DLL摘要与候选03一致，未发现这三份文件被旧副本覆盖。

原始玩家日志仅本地读取，不复制到仓库或候选；以下为必要诊断摘要。

## 最早有效异常与根因

1.7日志第365行、1.6日志第326行首先出现 `ReflectionTypeLoadException getting types in assembly ClientExtensionAbstractions`，不能解析呈现合同程序集3.0.0.0中的 `IClientPresentationImage` / `IClientPresentationImageLease`。随后两日志分别第415/360行无法找到 `PhinixClient.UnknownItem`，Def报null thingClass；后期快速创建游戏在ReadingPolicyDatabase/GenTypes出现空引用。

真实两版 `Assembly-CSharp.dll` 的IL确认：

- `Verse.ModContentPack.GetAllFilesForModPreserveOrder` 使用Array.Sort整理文件。
- `Verse.ModAssemblyHandler.ReloadAll` 对每个文件执行Assembly.LoadFrom，然后立即调用AssemblyIsUsable。
- `AssemblyIsUsable` 立即执行Assembly.GetTypes；无法解析接口的程序集不进入可用类型集合。

候选03同时使用 `10-ClientExtensionAbstractions.dll` 与 `10-ClientPresentation.Contracts.dll`。SDK在字母排序中先被扫描，图像桥接类已实现新合同接口；CLR身份查找不能凭空找到尚未加载的编号文件名。因此编译/全依赖预加载的呈现白盒能通过，Verse初次类型扫描失败。UnknownItem定义仍位于SDK源码，不能因为这次日志缺类型而移动、改名或改保存类型。

Mono的新进程探针复现：保留旧顺序失败；先加载合同后SDK GetTypes返回100类型，包含UnknownItem。随后用独立进程在系统Mono和系统.NET10同时验证完整候选03失败、候选04通过。游戏私有运行时仍需要新的Player.log确认。

## 修复范围

- `Client/Source/Client.csproj` 将合同输出改为 `09-ClientPresentation.Contracts.dll`，保证在10号SDK前加载；不改变CLR身份、命名空间、协议或存档类型。
- 红包/人才客户端工程的公开引用HintPath与Host制品检查同步该文件名；源码摘要更新且check-source通过。
- `tools/compatibility/stage-versioned-host.py` 加入排序门槛，拒绝再次生成合同晚于SDK的候选。
- `Tests/GameAssemblyLoadOrderRuntimeTests` 按真实阶段逐个LoadFrom并立即对SDK GetTypes，旧候选为必要负例，不先预加载全部合同掩盖缺陷；检查UnknownItem和图像接口。
- 1.7日志另报InvalidMaintainerConfiguration。net10工程生成器遗漏原工程明确声明的EmbeddedResource，已保留所有显式资源及LogicalName，并加入资源源码指纹；探针核对 `Phinix.PluginStore.Assets.maintainers.json` 内嵌字节与实际源文件一致。没有改维护者规则来吞掉错误。

## 状态与候选04

| 项目 | 结果 | 状态 |
|---|---|---|
| DLL加载路径/顺序 | 两版真实游戏IL确认；两版日志及Mono负例相符 | 源码/DLL检查通过 |
| 1.6 Host及三插件 | locked全图与实际独立插件工程通过 | 编译通过 |
| 1.7完整源码图 | 26工程通过，内嵌资源恢复 | 编译通过 |
| 旧失败/新通过 | 系统Mono与系统.NET10各一对独立进程；SDK100类型、UnknownItem和资源 | 白盒通过 |
| 联合目录/语言 | 原61检查在候选04重验；六个payload预检通过，制品布局2测试通过 | 白盒通过 |
| UnknownItem Def、快速开局、呈现及后续空引用 | 修正版尚未启动游戏；不宣称全部后续异常已消失 | 待测，无游戏通过 |

新候选：`local-api3-20261014-04`。ZIP：`/home/hunyuan2333/Phinix/local-compatibility-analysis/shared-type-load/candidate-04/phinix-rework-local-api3-20261014-04.zip`。SHA256：`987b794c2cbbf25d53df0c1a0faf7835104fe88a9fc7752c31d43ea55e3b7ae8`。仍为单Mod测试ZIP、两版目录、共同API3和120个文件；无游戏/Harmony DLL，无玩家日志/数据或发布标识。

退出游戏后替换旧测试Mod目录，勿覆盖解压而保留旧10号合同DLL；不删除用户存档、配置或managed插件业务数据。先在1.6确认SDK反射异常/UnknownItem错误消失及新测试开局，再重复1.7。1.7日志中的CJK插件另缺 `GUI.HandleTextFieldEventForDesktop`，是独立外部补丁不兼容；其报错可能仍需停用旧CJK版本后复测。跨版本Prefs字段警告、Workshop目录缺失不当作这次SDK根因。

`StoreReleaseNoticeFailed`等后续提示本轮保留待测，不以主根因修复猜测其必然消失。旧商店红包1.2.2/人才1.2.1被API3兼容检查拒绝是预期结果，未加载旧业务DLL；双版本独立商店安装包尚未完成，先修本体阻断再继续。
