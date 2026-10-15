# 主题包：创建、验证与安装

只改配色时使用纯数据主题包，不创建 DLL、游戏变体或第二套主题服务。工具和源码定位均相对 Host 仓库；命令中的路径需替换为实际绝对路径。

## 文件与 XML

复制 [主题模板](../templates/theme-package/theme-config.json) 及 [XML](../templates/theme-package/Themes/sample.xml)，保留其相对目录。配置字段为 `packageId`、`name`、`author`、`version`、`phinixRange`、`themes`；每个主题含 `id`、`name`、`file`。`file` 相对配置文件解析，打包器会生成包内 `Themes/<id>.xml`，不必与输入文件名相同。

```xml
<PhinixTheme version="3" name="Sample" author="Author">
  <color key="ui.background" r="0.08" g="0.12" b="0.10" a="1" />
  <color key="ui.text.primary" r="0.90" g="0.95" b="0.92" a="1" />
  <param key="chat.nameColor.saturation" value="0.65" />
  <inherit key="ui.surface" />
</PhinixTheme>
```

- 根元素为 `PhinixTheme`，支持 `version="2"` / `"3"`，可选 `name`、`author`。不是 `<theme>`，颜色不是十六进制字符串。
- `color` 必须有 key/r/g/b/a；通道为有限的 0–1 浮点数。`param` 为 key/value；参数需为有限数，业务可用范围还取决于该键注册。
- `inherit` 只有 version 3 支持。不使用 DTD、外部实体、额外元素或重复键。
- 已知旧别名如 `primaryText`、`background`、`nameColor.*` 被拒绝，使用 `ui.text.primary`、`ui.background`、`chat.nameColor.*`。
- 合法自定义键可能被保留但尚未注册，静态验证不保证可见效果。检查主题编辑器的未使用键及当前注册；缺省值/回退由同一 Host 主题服务管理。

## 打包与静态验证

```sh
dotnet /path/to/ManagedPackageTool.dll --pack-theme /path/to/my-theme/theme-config.json /path/to/artifacts/my-theme-1.0.0.zip
dotnet /path/to/ManagedPackageTool.dll --validate-theme /path/to/artifacts/my-theme-1.0.0.zip
```

使用新的输出路径。工具运行在 .NET 10，不需要运行游戏；仅构建主题工具也不需要游戏 DLL。源码为 `Extensions/PluginStore/Tools/ManagedPackageTool/ThemePackageCommand.cs`。

成功命令退出码为 0，输出 JSON 中包含 `management="phinix-theme"`、`validation="static-data-only"`、`installed=false`，不是 `Package validation successful.`。ZIP 仅含根 `manifest.json`（schemaVersion 1）和清单声明的 `Themes/*.xml`。包体及展开内容各不超过 8 MiB；单份 XML 上限 1 MiB；不添加 DLL、图片或运行时 variants。

模板以 `>=0.10.4 <1.0.0` 覆盖本轮修正后的在线交互；这不是主题格式首次出现的版本。根据实际所需能力和已测 Host 设置最低范围，不凭产品名称猜测。

## 游戏验收

本地主题导入不要求开发者模式，也不要求重启。使用当前 UI 的本地主题包入口（如商店“更多”），检查包名、版本、摘要和来源，再安装。主题身份为 `package/<packageId>/<themeId>`。安装不会自动应用；打开主题选择/编辑窗口选择草稿并应用，验证热切换、编辑、导出及重新导入。

在线安装按商店主题筛选进入详情；验证点击后选择仍保留、安装按钮可用、确认后库存更新。本地导入和在线包同 ID 时，不静默认领来源；必要时先导出并卸载本地包，再在线安装。卸载当前主题时验证回到默认主题且没有遗留窗口底色。浅色/深色均检查文字、输入框、选中态、滚动条和整个窗口边框。

格式、范围或来源校验失败时保留输入，不跳过验证。主题刷新、安装、选择、应用、导出和卸载分别记录结果。工具通过不等于游戏通过。

## 发布与依据

主题目前使用独立 schema4 feed，由维护者审核并发布；不套用 DLL 的小时自动更新策略。作者仓库可属于任意公共用户/组织，需核查分发许可。Index 校验原包并托管摘要一致的分发副本；CF 的缓存与凭据由网关维护者管理，主题作者不提供服务端令牌。见 [publication.md](publication.md)。

核查源码：`Common/Utils/Framework/Themes/{ThemePackage.cs,ThemePackageBuilder.cs,ManagedThemePackageStore.cs}`（位于 Common 子模块）、`Client/Source/UI/{UiTheme.cs,ClientThemePackageService.cs}`、`Client/PresentationContracts/UI/{IClientTheme.cs,IClientThemePackages.cs}`。
