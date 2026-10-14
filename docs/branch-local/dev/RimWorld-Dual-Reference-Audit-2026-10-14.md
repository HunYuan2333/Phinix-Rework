# 双版本引用静态审计与启动绑定修复

用户在候选07上已安装/启用红包与人才贸易，但顶部页签未出现，并要求整体审计类似依赖问题、先修阻断正常功能的引用。

## 已确认阻断

实际日志明确游戏1.7.4977 rev1248，三个包均有ManagedPayloadVerified和CandidatePreflightPassed，但只有Example有ManagedAssemblyLoaded/ManagedEntriesReady。最终ACTIVE六模块不含红包/人才；不是普通商店tags显示问题，也不是业务完成回执问题。

`ManagedExtensionRuntime.Start`在预检之后重新构造actualHost，只接受已在AppDomain的程序集。候选07补充的可信框架元数据尚未对应实际Assembly，因此两个包被内部标记ManagedHostAssemblyUnavailable，没有加载；旧分支未Emit该拒绝，日志只显示总启动完成。这是候选07遗留的第二层漏检，已修。

新`ClientRuntimeAssemblyFacts.ResolveFrameworkReference`只接受运行时TPA、核心运行时目录中、完整身份完全匹配的框架声明，使用Assembly.Load请求运行时绑定，并重验实际身份及目录。Common启动绑定在原已加载Host选择失败时，调用Host提供的框架实体化能力；添加到冻结绑定与baseline，再加载插件。插件自己的resolver仍只处理声明过的冻结依赖，不探目录或任意装载。缺失/错误身份继续拒绝，现会Emit完整引用诊断。保留原三参数构造函数，新增四参数重载，避免无必要改变原方法ABI。无官方插件专用分支，无业务或protobuf pins修改。

## 引用审计结果

实际候选Host/内置扩展与三个独立包的两版DLL均读取PE元数据、完整CLR引用身份；供应方使用原1.6 GameDlls/Mono框架与私有1.7游戏引用。工具源码`tools/compatibility/AssemblyReferenceClosureAudit.cs`；逐条结果保存在仓库外候选09目录。输入为JSON的game、owned、providers路径数组；工具仅将1.7的框架跨版本绑定作为当前CoreCLR观察，不将其当作Mono证据。

| 范围 | 静态引用 | 结论与依据 |
|---|---|---|
| 1.6 Host+内置+3插件，36 DLL | 249条，243条满足严格身份/现有Host规则 | 其余6条属于既有第三方/框架桥接，见下方；不能按managed包同主版本规则误判整个原Host必坏 |
| LiteNetLib旧mscorlib2/System2/System.Core3.5引用 | 3条 | 系统Mono实际Assembly.Load分别绑定4.0，白盒确认；不改库 |
| Protobuf/System.Memory/Tasks.Extensions需要Unsafe4.0.4.1，现Host带Unsafe6.0.0.0 | 3条 | 直接在无相邻探测目录中Assembly.Load旧身份会失败；真实owned loader、真实制品目录下Protobuf fixed64解码成功并加载该Unsafe6文件。证明已测路径可绑定，不宣称全部Tasks/Memory路径或游戏Mono通过；列为1.6专项回归项，不改vendor pins |
| 1.7 Host+内置+3插件，29 DLL | 317条，307条严格满足，10条框架桥接 | Autofac net8依赖System8；系统.NET10实际按CLR框架规则绑定对应签名System10，均观察通过。用户日志亦显示Host/商店正常激活；Waylith所有Autofac功能仍不能仅靠静态宣布通过 |
| 红包/人才直接框架引用 | 新版DLL在游戏根目录存在，完整签名相符 | 实机确认安装预检通过；启动实体化缺口有新进程负例/正例，已修 |
| 包内Contracts→Client依赖与共享Host/API3 | 实际PE身份与构建图审查 | 依赖顺序仍先Contracts后Client；只选当前游戏payload。两插件不相互引用，Host不引用其Contracts或业务 |
| 受控resolver、所有权与失败传播 | 源码检查 | 依赖声明、冻结bytes、Host身份规则、失败传播和禁止外来文件探测保留；新增缺失引用审计 |

未发现新的“当前1.7 DLL中不存在的直接程序集依赖”。此结论只覆盖程序集引用集合，反射字符串目标、Harmony方法签名、类型静态初始化、业务调用与存档行为仍按迁移测试清单验证。

## 验证

- 两版Host编译、1.7完整26项目图：编译通过。
- 新真实net10 fixture包含WebHeaderCollection与XmlDictionaryReader字段，在独立进程中：原无框架实体化路径拒绝且不加载（3项）；新路径成功加载、条目验证、实际registry扫描（5项），并验证框架authority不装载PhinixClient等任意Mod身份。
- 真实游戏字段/业务不在fixture中执行；这不是红包/人才实机激活通过。
- 原managed 3396项、商店子集2225项及实际启动子进程回归：白盒通过。
- 原插件三个ZIP哈希不变；formal store、正式插件固定Host提交与发布记录不动。

## 候选09及复测

候选08为内部中间物，未交付；候选09保留旧构造函数ABI并包含最终修复。

`/home/hunyuan2333/Phinix/local-compatibility-analysis/dual-package/candidate-09/phinix-rework-dual-managed-local-host.zip`
SHA256 `b19e7abd49f62fa6b9be435d6a4f289b9198de784019a8b36de5094fe38b3cfc`。

测试包About.xml声明supportedVersions 1.6/1.7，并写明Local双版测试/API3及尚在游戏验证。已直接检查ZIP内XML。仅测试打包脚本整理声明，不提前修改正式发行支持结论。

退出游戏后替换完整测试Host文件夹，保留已经安装的三个插件与数据，不重新导入。启动1.7确认两模块出现ManagedAssemblyLoaded、ManagedEntriesReady及ACTIVE，顶部红包/人才页签出现；再测基本打开/确认/交付。随后在测试配置中切1.6，重点登录/网络消息(Protobuf)、异步商店操作、物品往返，再切1.7。新候选实机待测，原始日志不提交，不修改玩家配置或正式存档。
