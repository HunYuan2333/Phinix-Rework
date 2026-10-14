# 双版本独立托管包：本地候选06

用户确认候选05的显示与布局已无问题。此反馈只覆盖实际测试界面；不扩大为存档、交付、交易或所有输入路径通过。

## 实现与边界

主模组一个版本/一个双游戏 ZIP；红包、人才贸易和 Example 各自一个版本/一个独立 ZIP。候选06的 Host 没有这三个插件的普通 bundle，以便测试真实托管安装，不绕过重复程序集保护。插件业务代码沿用候选05，新增内容只涉及 Host/共享托管基础设施及打包工具。正式商店、发布记录、插件固定 Host 提交和 protobuf pins 未修改。

托管 manifest schema 2 是完整 schema 1 声明组成的 `variants` 信封。每个声明只对应一个游戏及 TFM：1.6/net472、1.7/net10.0。包身份、产品版本、公开兼容范围、程序集身份、模块与依赖声明必须一致。允许只剩一个 variant，以便未来删除1.6 payload，业务与商店条目无需分叉。

关键源码：

- Common `Common/Utils/Framework/ManagedExtensions/ManagedExtensionManifestReader.cs`：检查 variant profile/公共契约一致性，将实际文件定位到 `1.6/` 或 `1.7/`。
- 同目录 `ManagedExtensionManifest.cs`：显式 `ForGame`、选中后的 `SupportsGame`、两版所有权 `AllFiles`；解析不依赖当前 CLR。
- 同目录 `ManagedExtensionZip.cs`、`ManagedExtensionPayloadInspector.cs`：校验全部文件哈希和两版实际 DLL 元数据；只返回选中版的可执行 payload。
- Common `ManagedExtensionRuntime.cs`、`ManagedExtensionInstallationRuntime.cs`、`ManagedExtensionManagementRuntime.cs`：启动/安装/启用显式选择游戏；统一收据、内存预算、所有权和恢复检查覆盖两版。
- Common `ManagedExtensions/ManagedExtensionInstallationRecovery.cs`、`ManagedExtensionRemovalRecovery.cs`：事务与删除使用完整文件集合。
- Store `ManagedStorePlanner.cs`、`ManagedStoreUpdates.cs`、`ManagedLocalImport.cs`：包适配当前游戏；不新增第二条同版本条目。
- `ManagedPackageTool/VariantPackageCommand.cs`：从真实已校验的两版包生成确定性单 ZIP；`OfflinePreflight.cs` 的引用校验要求明确游戏。
- `tools/compatibility/stage-local-managed-dual.py`：隔离生成 Host 与三个独立包，不安装、不推送。

这是包格式升级；旧客户端不承诺能读取 schema 2。新包 API 范围仍是 >=3.0.0 <4.0.0，不新增旧客户端业务兼容分支。

## 已有证据

| 项目 | 状态 | 范围 |
|---|---|---|
| 包所有权、按游戏选择、恢复调用路径 | 源码检查 | 修改共享基础设施，无业务协议/报价/Thing/Pawn迁移 |
| 1.6 Host 与打包工具 | 编译通过 | 隔离快照，原 GameDlls 未覆盖 |
| 1.7完整26项目图 | 编译通过 | 私有1.7.4977 rev1248引用，游戏/Harmony DLL不进包 |
| 原托管插件回归 | 白盒通过 | 3396项，含实际启动子进程；商店子集2225项 |
| 3个真实独立双版ZIP | 白盒通过 | 每个在系统Mono/net472与.NET10各36项：两游戏单条目规划、完整信封摘要、全文件安装、按游戏选择、禁用重启、统一卸载；损坏未选中DLL被拒绝 |
| 6组真实程序集绑定 | 白盒通过 | 3包×2游戏，按游戏Host/Game/框架引用静态核验 |
| 候选05界面 | 游戏通过（用户反馈） | 用户确认显示与布局，未扩展到业务与存档 |
| 候选06托管导入与业务激活 | 待测 | 白盒未执行真实插件业务初始化；Steam游戏运行时需测 |
| 远程商店下载/升级/断电恢复双版包 | 待测 | 既有单包回归通过，双版故障注入与远程全流程尚需补齐；未更新正式商店 |

白盒 Host facts 在独立包探针中由真实 DLL references 构造，用于验证包装流程，不能据此证明实机所有依赖已加载。另有六组真实候选 Host 引用检查弥补静态绑定检查；仍不能代替游戏激活。

## 候选及测试

本地目录（不提交制品）：
`/home/hunyuan2333/Phinix/local-compatibility-analysis/dual-package/candidate-06/`

- `phinix-rework-dual-managed-local-host.zip`，SHA256 `aa2619cd449a5de407b21931eaf6c498ae82a54b2d990d2a7734eaade61e66d7`
- `phinix.legacy.redpacket-dual-local.zip`，SHA256 `c1617d90bdde169bb4e85e141e238179ef8ae812dddb07ca1824e3ceee2a8fc6`
- `phinix.legacy.talent-trade-dual-local.zip`，SHA256 `a3616fe9f27c3bd1bc3991dc4cd2a32e4c722b1804f613333a986797f245cd98`
- `phinix.example.basic-dual-local.zip`，SHA256 `726c4a165e699c67dd7032b479600d2d654fd54f400713f0fed9431ad6be48c5`

各插件本地临时产品版本0.0.0，不是正式新发行。使用独立测试 profile 与测试存档；保留原配置与数据。

1. 替换测试Mod为候选06 Host；确保其他位置无红包/人才/Example旧 DLL，包括候选05普通 bundle 和独立Mod。通过现有受支持管理流程移除旧测试安装，避免重复身份。
2. 启动1.7，打开游戏开发者模式，在商店“更多”中的本地ZIP导入分别选择三个独立ZIP，检查待重启提示；重启后检查插件激活、语言与日志实际程序集来源。
3. 测试红包/人才入口、动作/确认、关闭再打开；业务交付只在测试存档中验证。
4. 保留同一测试 profile 的托管安装记录，切回1.6启动，确认自动选择1.6 DLL且无需再导入，包版本仍相同；再切1.7复验。记录两次 Player.log，原始日志不提交。
5. 申请卸载并重启，确认两版代码目录一并删除；业务数据应按原插件所有权保留。重新导入后复验。

探针入口：`ManagedExtensionRuntimeTests dual-package ZIP`；真实DLL使用metadata分析与禁用重启，不运行业务。候选证据、临时日志与引用缓存均在Git仓库外；源码、测试和本记录可审查。
