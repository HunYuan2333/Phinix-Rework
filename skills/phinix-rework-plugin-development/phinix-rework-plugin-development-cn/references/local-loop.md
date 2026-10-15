# 本地开发与验证

独立 DLL 的默认路线。纯数据主题无需 C# 工程，直接读 [theme-package.md](theme-package.md)。

## 建立最小工程

1. 按 [environment.md](environment.md) 准备 SDK、两套游戏引用和同一 Host/API3 的公共 DLL。
2. 将 [minimal-plugin](../templates/minimal-plugin/Minimal.csproj) 整个目录复制到独立插件根目录的 `example/`。修改包 ID、模块 ID、程序集名、命名空间与 settings 键前缀；不要与正式包共用测试身份。
3. 模板是 net472 的原生 Tab 示例。用 PhinixRoot 或三个 Phinix*AssemblyPath 属性指定公共引用，GameReferences 指向 1.6 DLL 目录。不要引用 Client.csproj，不把宿主或游戏 DLL 随包分发。
4. 修改功能时按 [composition.md](composition.md) 管理所有权。完整中立页面、动作和本地化可参考实际 Example 项目；模板不假装覆盖全部公共能力。

```sh
dotnet build example/Minimal.csproj -c Release   -p:PhinixRoot="/absolute/path/to/Phinix-Rework"   -p:GameReferences="/absolute/path/to/rw16-references"   -o "/absolute/path/to/scratch/minimal16"
```

输出到新的临时目录，不覆盖已安装 DLL。模板 package-config.json 只声明该 net472 构建的 1.6 能力；不能仅给单 DLL 多写一个游戏版本就称为双版本。

## 构建双版本包

使用 [dual-runtime.md](dual-runtime.md) 的实际 Host helper 命令。helper 从 example/ 或 Client/ 找到项目，隔离生成 net10 项目并分别构建，最后产出包含两个变体的 ZIP；模板不自带 pack.py。仅当插件仓库已有经过核对的 wrapper 时才用它。

当前 helper 不读取模板 package-config.json，而且固定生成默认兼容范围和空依赖。存在额外程序集、模块依赖、外部 Mod 或更高最低 Host 要求时，需要明确的自定义打包配置和闭包检查，不能省略声明。此配置只描述兼容范围和依赖；包身份、输入 DLL 与输出路径还需通过 CLI 参数提供。

## 离线预检

对最终 ZIP 分别执行以下命令，将占位路径换为实际文件。Tool 的 net10 构建方法见 dual-runtime.md。

```sh
dotnet /absolute/path/to/ManagedPackageTool.dll   --validate /absolute/path/to/plugin.zip --game-version 1.6

dotnet /absolute/path/to/ManagedPackageTool.dll   --validate /absolute/path/to/plugin.zip --game-version 1.7
```

两次退出码 0 且 JSON 的 staticValidation 为 passed 才算静态包检查通过。未提供 `--host-assembly` 时，hostReferencesChecked 为 false，不能宣称引用闭包通过。需要闭包检查时为对应变体提供完整的合法游戏与公共 Host 依赖；参照 helper 的实际预检参数，不混用两个运行时的 DLL。

## 游戏侧载

在对应游戏打开 Phinix 扩展管理器，启用开发者模式，从本地 ZIP 安装，再重启游戏。重启是当前 Host 绑定与启用策略要求，不是 CLR 完全无法动态加载程序集。检查已安装版本/摘要、实际加载版本、模块状态与功能，不把“安装成功”当作“激活成功”。

分别验证 1.6 和 1.7；包括重复启停、设置持久化、退出存档、切档、关闭窗口与过期回调。只涉及外观也要看真实 UI。纯数据主题不需要开发者模式或重启，安装后仍需选择并应用。

## 失败定位

| 情况 | 首先核对 |
| --- | --- |
| CandidateHostIncompatible | 当前 CompatibilityVersion、游戏版本及包范围；不要为绕过检查随意改常量 |
| ProtectedAssembly | ZIP 混入 Host/游戏/受保护依赖；只打包明确私有依赖 |
| VariantContractMismatch | 两个变体的模块、身份、程序集完整标识和依赖是否一致 |
| ReflectionTypeLoadException | LoaderExceptions、实际引用闭包与运行时，不仅看顶层异常 |
| 已安装但无 Tab | 发现/激活状态、入口契约、程序集绑定及是否重启 |

同版本不同摘要、降级和来源冲突按实际安装规则处理，不改回执、不直接替换已安装文件。正式包与开发包切换前保留配置/导出，再按正常卸载与重启流程切换。
