# DLL 语言文件 Playtest 1.3.0（已公开测试版）

上线前清理：Playtest 仅作为独立开发测试插件，不出现在正式 index。下文旧测试源说明属于历史记录，新开发测试仅使用 Common/Extensions 直接子文件夹中的完整 bundle；已有托管安装仍由扩展管理操作。

目录补充：支持 `Common/Extensions/<任意插件目录>/DLL + 伴随清单 + Resources`，可以直接复制整个样例文件夹；只发现一级插件目录，不递归载入 Resources/deep 下的 DLL。Resources 中按包 ID 分隔的现有路径保持原样。已展开的旧样例文件不要同时保留。

[English](README.md)。1.3.0 已改用宿主 `IClientLocalizationService`，不再内嵌中英词典。包含 DLL 和 `Resources/Localization/en-US.json`、`zh-CN.json`：文件中的 display 供下一批发布工具提取，strings 已用于 Tab/按钮/确认框/计数结果。任一语言单独存在也合法，缺译按宿主统一规则回退。游戏切换语言时，已有 Tab 使用新语言，无须重装。

1.3.0 已公开发布并更新 staging 的 phinix.managed 目录；ZIP 从 GitHub 回读核对通过，远端链路验证见发布记录。原公开 1.2.1 游戏安装到卸载已获用户验收；1.3.0 的游戏语言验证仍待进行。契约程序集已升为 1.7.0，必须重编主体与随包插件；旧 1.2.x 的 1.6.0 CLR 引用不会自动改写。语言文件完整纳入清单长度/SHA-256，安装和启动都校验，单独修改已安装 JSON 会被拒绝。

## 构建和打包

下面是本机实际游戏引用目录。换机器须改路径。输出已存在时请选择新的 ZIP/目录名，工具不会覆盖。

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework
PHINIX_LOCALIZATION_GAME_REFS=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed

dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:BuildInParallel=false -m:1 -p:RimWorldDepDir="$PHINIX_LOCALIZATION_GAME_REFS" -p:GameReferenceDirectory="$PHINIX_LOCALIZATION_GAME_REFS"
dotnet build Extensions/PluginStore/Samples/Playtest/Playtest.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1 -p:GameReferences="$PHINIX_LOCALIZATION_GAME_REFS"
dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1

dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll \
  --assembly Extensions/PluginStore/Samples/Playtest/bin/Release/net472/Phinix.Store.Playtest.dll \
  --package-id phinix.poc.playtest --name 'Phinix Store Playtest' --version 1.3.0 \
  --language-file Extensions/PluginStore/Samples/Playtest/Resources/Localization/en-US.json \
  --language-file Extensions/PluginStore/Samples/Playtest/Resources/Localization/zh-CN.json \
  --output /tmp/phinix-managed-playtest-1.3.0.zip \
  --bundle-output /tmp/phinix-playtest-localization-bundle-1.3.0 \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/mscorlib.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/Assembly-CSharp.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/UnityEngine.CoreModule.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/UnityEngine.TextRenderingModule.dll" \
  --host-assembly "$PHINIX_LOCALIZATION_GAME_REFS/UnityEngine.IMGUIModule.dll" \
  --host-assembly Common/Utils/bin/Release/net472/Utils.dll \
  --host-assembly Client/ClientExtensionAbstractions/bin/Release/ClientExtensionAbstractions.dll
```

`manifest.json` 仍为 schemaVersion=1，新增可选 localization；每个文件同时在 resources 声明。`--default-locale` 可选。ZIP 只有清单、插件 DLL、两份语言文件，游戏/框架 DLL 不进入包。`--bundle-output` 另外生成普通随包发现所用的 DLL、`Phinix.Store.Playtest.dll.localization.json` 和按包 ID 分隔的资源目录。所有作者走同一套宿主服务，不依赖商店。旧 `Package/`、`pack.py` 仅保留 1.1.0 本地 Mod 历史，不用于本候选。

## 先做本地游戏验证

完整退出游戏。通过扩展管理卸载旧托管 Playtest 并重启确认移除，取走之前的本地 Mod/随包样例；同一模块不能同时存在两份。保持计数/存档数据，无须删除。远端 phinix.managed 现提供 1.3.0。先取走整个手动随包测试目录并重启，再从商店安装，不要保留重复 DLL。

把完整新主体 Output/phinix-rework 部署到实际使用的 Mod 目录。若游戏直接使用这个 Output，以下命令将已准备的普通随包候选复制进去（文件存在就停止）：

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework
test ! -e Output/phinix-rework/Common/Extensions/Phinix.Store.Playtest.dll && \
  test ! -e Output/phinix-rework/Common/Extensions/phinix-playtest-localization-bundle-1.3.0 && \
  cp -R /tmp/phinix-playtest-localization-bundle-1.3.0 Output/phinix-rework/Common/Extensions/
```

1. 中文启动后见“商店测试”，按钮/简介/确认框/计数为中文，计数和 100 白银仍正常；仅用测试存档。
2. 切英语后见 Store test，已有页面文字变英文，计数保持；切没有提供翻译的日语/法语，应回退英语，不露出 key/空白。
3. 禁用商店后重启，Playtest 仍可注册并显示翻译，证明宿主没有商店依赖。
4. 在扩展管理禁用 Playtest 后重启，Tab 消失；重新启用后重启恢复，计数保持。关闭后没有旧语言回调。随包候选的移除只取走本次 DLL、伴随文件与 Resources/phinix.poc.playtest 目录，不删除设置/存档。

这里测普通随包发现与语言服务；托管安装/卸载路径已有回归，待目录 v3/远端候选发布后仍需按托管路线重复游戏验收。

## Mono 注册检查

```sh
mkdir -p /tmp/phinix-playtest-localization-test-extensions
cp -R /tmp/phinix-playtest-localization-bundle-1.3.0 /tmp/phinix-playtest-localization-test-extensions/
dotnet build Extensions/PluginStore/Samples/Playtest/tests/RegistryCheck.csproj --configuration Release -p:BuildInParallel=false -p:NuGetAudit=false -m:1
MONO_PATH=/usr/lib/mono/4.5:/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed \
  mono Extensions/PluginStore/Samples/Playtest/tests/bin/Release/net472/RegistryCheck.exe \
  /tmp/phinix-playtest-localization-test-extensions
```

系统 Mono 先用系统 BCL；不能直接把游戏的 mscorlib 优先放入 Mono 搜索路径。检查会真实发现/注册/激活样例，读取中英 JSON、切换并回退、关闭资源；不调用游戏 UI 或白银操作，不能代替游戏验收。详见 [实现验证记录](../../../../docs/branch-local/dev/plugin-store/本地化实现与验证.md)。

## 远端 1.3.0 测试

[Release](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.3.0)。入口 https://plugins-staging.hunyuan2333.com，sourceId phinix.managed，刷新后选择 1.3.0。当前目录仍为 v2，商店多语言简介/changelog 的 v3 展示尚未交付；包内 UI 语言已经通过宿主应用。安装后检查每插件文件夹中的 DLL/两份语言，再重启检查中文/英文/缺译回退、关闭商店后翻译、启停/卸载。
