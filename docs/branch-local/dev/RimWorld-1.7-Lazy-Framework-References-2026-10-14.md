# 候选06本地安装阻断：按需加载框架引用

用户实机反馈：Example可安装，红包与人才贸易失败。提供的日志片段并非完整启动日志，不能仅凭片段确认所有加载版本；同startupId的本地Logs/Player.log确认RimWorld 1.7.4977 rev1248，实际探测目录为测试Host的1.7/Assemblies与1.7/Extensions。安装审计的两个artifact SHA与候选06独立包完全相符，版本均为0.0.0；Example启动审计验证其对应同一双版包。原始日志仅本地分析，不提交。

## 顺序与依据

1. 06:46:29 UTC，人才贸易安装请求后第一次有效拒绝是management-preflight `CandidateAssemblyReferenceUnavailable`：`LegacyTalentTradeExtension.Client`需要`System.Runtime.Serialization.Xml, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a`，availableReferences为空。
2. 后面的install同码和商店operation_failed是同一拒绝的转发，非另一根因。
3. 06:47:04 UTC，红包同样在预检失败：`LegacyRedPacketExtension.Client`需要`System.Net.WebHeaderCollection, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a`，availableReferences为空。
4. 两个DLL实际都存在于本地游戏根目录，PE元数据与上述完整身份一致，TFM均为.NETCoreApp v10.0。不存在“游戏删掉这些API”的证据。

源码根因是`Client/Source/Client.cs`原Host facts只来自`AppDomain.GetAssemblies`，将未加载的框架程序集误判为不存在。此前六组离线绑定检查主动提供了真实框架文件，且初版独立包探针按插件引用构造facts，因此覆盖不到“文件可用但还没有加载”的实机状态。这里修正验证方法，不将旧白盒通过冒充实机通过。

## 最小修复

- 新`Client/Source/Framework/ClientRuntimeAssemblyFacts.cs`从运行时`TRUSTED_PLATFORM_ASSEMBLIES`中读取框架完整身份，限制在核心运行时目录与System等框架名称；不扫描mod/插件文件，不执行插件，不复制游戏或框架DLL。
- `Client.cs:400`使用loaded+runtime framework去重身份作为Host facts；现有公共身份/签名/版本选择规则保持。
- `ClientEnvironmentCapture.cs:70`同步提供框架可用性快照，框架不声明为插件或Mod所有权。缺失/非法身份仍按诊断失败处理。
- Mono没有TPA，保留原已加载程序集事实；实际系统Mono探针确认此行为。
- 不改红包/人才业务或ZIP，不硬编码对两个官方插件的例外；其他插件使用同样Host基础设施。

## 验证与范围

| 检查 | 状态 |
|---|---|
| 当前游戏两DLL的完整CLR身份/TFM | 源码与DLL检查通过 |
| 1.6 Host隔离构建 | 编译通过 |
| 1.7完整26项目图 | 编译通过 |
| RuntimeFrameworkFactsTests | 白盒通过：两个实机缺失依赖、未加载可用性、身份去重、拒绝非运行时目录、缺TPA回退；未加载两个目标框架DLL |
| 真实双版红包/人才/Example安装探针 | 白盒通过：每包37项，1.7框架facts改用实际production collector，不再从插件引用虚构框架可用性 |
| 原托管/商店回归 | 白盒通过：3396项，商店子集2225项 |
| 实际Mono collector | 白盒通过：无TPA时loaded-only |
| 候选07游戏安装/重启激活 | 待测 |

.NET10正常运行时TPA探针通过仍不代替Waylith实际游戏进程验证；候选07的实机复测需确认相同预检拒绝消失，再看真正初始化/业务是否有后续异常。

## 候选07与复测

Host：`/home/hunyuan2333/Phinix/local-compatibility-analysis/dual-package/candidate-07/phinix-rework-dual-managed-local-host.zip`
SHA256：`972a85cf559dd9475e05be0ec670780b1aa574cb3fb7d13ed8845e7ba7d8a8f0`。

独立插件三个ZIP与候选06逐字节相同，可继续使用原下载；不需要卸载成功安装的Example。退出游戏后只替换测试Host的完整文件夹，启动1.7重新导入红包/人才原ZIP；成功后重启，检查实际激活和Player.log。失败日志显示预检阶段拒绝，没有安装提交证据，无需先清理玩家目录或安装记录。进一步冲突应按新诊断处理，不手动删除业务数据。

源码、测试与记录可审查；未提交、推送、更新正式商店、改变存档或玩家配置。
