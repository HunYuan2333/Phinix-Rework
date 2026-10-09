# 本地开发闭环（主要路线）

编译、打包、预检、侧载、诊断和开发修订时读。此流程需要 [environment.md](environment.md) 确认现代 DI 与新工具/侧载能力；未知/旧发行宿主先停在版本定位。

新项目先按 [轻量工程组织](engineering.md) 选最小布局；已有项目只在职责/状态混杂时整理，不先做大规模重构。

## 1. 从 Example 建立独立身份

使用已验证的现代 Example 固定快照独立复制，保留原仓修改。一起核对 `.csproj` 的 AssemblyName、PluginPackageId、PluginDisplayName、版本，源码 namespace、PhinixExtension 特性与 ExtensionId、设置键前缀/SectionId、翻译资源/依赖。不要只改 ZIP 名或手改生成 manifest。当前 Example 显式列出编译文件，拆分源码时同步 Compile Include；改项目文件名时同步 pack.py 定位，不假定新增文件自动入 DLL。开发 ID 优先与正式包不同；准备正式身份时重新核对资源和持久化迁移，不能只换包名掩盖兼容问题。

## 2. 配置兼容与依赖

当前 Example `pack.py` 读取 `example/package-config.json` 并向工具传 `--config`，它自身没有通用 `--config` CLI 参数；其他插件的 pack.py 参数不同，先读脚本/help。当前配置格式示例（范围只作本轮能力示例，按实际目标验证后收窄）：

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

包依赖条目：`{"packageId":"author.provider","versionRange":">=1.0.0 <2.0.0","optional":false}`；外部 Mod：`{"packageId":"author.mod"}`。未知/重复字段、坏范围等由同一清单解析器拒绝。声明可选依赖时明确缺失后的降级；必需能力缺失就拒绝激活，不能隐式变成部分成功。

## 3. 独立编译、规范 ZIP、离线预检

准备一次宿主 `Common/Assemblies` 公开 DLL，或固定客户端已构建 Utils/ClientExtensionAbstractions；游戏引用只编译使用。当前 Example 是 net472/C# 7.3，工具是 net10.0。下面是 POSIX 示例，路径必须替换为用户实际绝对路径；Windows 按 environment 的参数传递方式改写。

```sh
python3 pack.py --phinix-root "/path/to/Phinix-Rework" \
  --host-assemblies "/path/to/host/Common/Assemblies" \
  --game-references "/path/to/RimWorld/Managed" \
  --packager "/path/to/ManagedPackageTool.dll" \
  --output "/path/to/build-1.zip"
```

pack.py 只编译本插件，再打包并自动预检，可识别宿主数字前缀 DLL。无 `--host-assemblies` 时用客户端已构建公开 DLL；已有工具不重建。需要源码工具时在固定客户端根执行 `dotnet build Extensions/PluginStore/Tools/ManagedPackageTool/ManagedPackageTool.csproj -c Release -m:1`（固定子模块完整，不需要游戏引用）。游戏目录无 mscorlib 时 Example 可使用唯一的本地 net472 引用缓存，也可显式 `--framework-references`；缓存不存在不虚构成功。

没有 Python时：对独立插件 `.csproj` 执行 `dotnet build -c Release -m:1`，通过 `-p:GameReferences=...`、`-p:PhinixUtilsAssemblyPath=...`、`-p:PhinixAbstractionsAssemblyPath=...` 指定合法实际文件，再调用工具：

```sh
dotnet "/path/to/ManagedPackageTool.dll" \
  --assembly "/path/to/My.Plugin.dll" --package-id author.plugin \
  --name "My Plugin" --version 1.0.0 --config "/path/to/package-config.json" \
  --language-file "/path/to/en-US.json" --language-file "/path/to/zh-CN.json" \
  --output "/path/to/build-1.zip"
dotnet "/path/to/ManagedPackageTool.dll" --validate "/path/to/build-1.zip"
```

`--assembly`、`--language-file`、`--host-assembly` 可重复；单语言插件按真实资源调整。预检无网络、无安装、无 DLL 执行；可重复附 `--host-assembly` 提供实际全部 CLR 引用（宿主、游戏/Unity、framework、所需依赖），核对程序集身份和冲突。引用文件不被打入 ZIP。未提供 host assembly 只能报告静态校验；提供后仍不能证明游戏可加载、版本范围真实受支持或依赖已启用。

## 4. 开发者侧载与诊断

开启 RimWorld 开发者模式 → Phinix 商店“dev: 安装本地插件 ZIP”（窄窗口走溢出菜单）→ 绝对 ZIP 路径，或明确目录后浏览选择 → 核对包 ID、版本与 SHA-256 → 确认 → 重启游戏。只浏览明确目录，不扫描全盘。导入复制到宿主临时区冻结已验证字节，确认和执行重新检查模式。取消未提交意图释放临时输入；模式撤销不回滚已提交事务。

重启后在扩展管理核对本地来源、模块发现/激活、依赖/原因码、当前会话与已安装构建摘要、当前与下一启动状态。“复制摘要”用于排障，不要求分享包含私人内容的整份日志。已安装摘要变化但当前摘要旧时先重启，不据此宣称新代码已运行。

## 5. 重复迭代与管理

| 情况 | 行为与下一步 |
| --- | --- |
| 同本地 ID/版本、不同 ZIP 摘要 | 开发修订；旧包须启用且可信；新路径构建/导入，再重启加载 |
| 重复摘要 | 拒绝重复导入，核对是否确实重新构建 |
| 降级版本 | 拒绝；不修改收据绕过 |
| 正式来源同 ID | 拒绝覆盖；用独立开发 ID，或正常卸载并重启完成移除后换来源 |
| 关闭开发者模式 | 已安装本地包仍可管理；新的确认/执行拒绝，无热替换 |
| 启停/卸载 | 商店与扩展管理应同步；保存的是下一启动意图，当前已加载 DLL 不改变 |
| 取消卸载 | 保留停用状态及模块选择，不自动恢复原启用状态 |

本地 sourceKind 是 `local-development`，本地收据 schema 2 不伪装 Index 审批；正式 schema 1 仍可读。这不是公共 Index catalog 版本或协议退役。降级到只支持 schema 1 的宿主前，在新宿主正常卸载本地包并重启完成移除；未完成事务不跨不兼容版本降级。不手删 journal/receipt。业务数据依现有保留规则，依赖消费者仍启用时不能破坏依赖。

在 [verification.md](verification.md) 完成游戏验收后再准备正式发行；本地侧载不会自动变成正式目录包。
