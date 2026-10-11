# T5 存储与恢复接续设计

日期：2026-10-11。状态：设计已用于本批本地实现，游戏/异常中断未验收。下文是实施前边界，当前细节以 Theme-Package-T5-Implementation.md 末节为准；使用 t5-local 同批演示包。

## 1. 当前核对结果

实际 DLL 托管安装是 Common/Utils/Framework/ManagedExtensions 的 ManagedExtensionInstallPackage、Runtime 与 InstallationRecovery，而不是商店目录下历史 ManagedInstallation 的完整 Mod 安装器。LocalImport 调用 IManagedExtensionInstallationService，Host 提供服务，商店提交冻结载荷与来源事实。

当前 InstallPackage 在构造时硬解析 ManagedExtensionManifest，并要求文件数为 Assemblies+Resources；InstallationRecovery 的 row、receipt 和旧 manifest 校验也绑定 DLL 模型。不能直接把纯主题 XML 声明为 resources、塞一个空模块，或调用旧 Mod 安装器绕开这些条件。

现有恢复标准可复用：所有权检查、lease、冻结输入、staging 校验、preparing → committing 持久决定、提交后完成恢复、摘要/长度/精确目录树校验。当前 preparing 可取消，File.Replace 写入 committing 后不能把取消当回滚；这一点对主题同样成立。

## 2. 选定边界

抽取/扩展中立包存储事实，类型校验 adapter 提供已验证清单、文件声明和安装策略；不再让存储引擎要求程序集类型。DLL adapter 保留当前 CLR/模块/依赖校验和启动 gate，theme adapter 只验证数据清单及 Host 范围。普通主题运行时接收当前主题来源/定义，不读 receipt/journal 或判断旧 schema。

包 ID 与来源身份进入共同冲突投影，kind 不能成为替换别人同 ID 的借口。已安装本地同类型可按现有同版本不同摘要规则修订，降级/重复摘要拒绝；正式包替换依赖冻结目录/批准输入，不由作者自报 official。

数据主题目标为 SaveData/Phinix 下独立受管主题根，staging/receipt/transaction 由 Host 所有。不会复制到用户 Themes、RimWorld Mods 或 DLL 运行时扫描根；只读已验证收据/文件才进入来源目录。原则与现有事务共用，不另造一套忽略旧故障恢复的“主题安装成功”路径。

Host 负责中立存储服务与来源生命周期，Store 负责来源锁、候选、确认和管理 UI。Host 不引用 PluginStore implementation/Contracts；无 Store 时当前 Host 默认/用户主题仍能使用。

## 3. 持久化兼容

- 旧 DLL receipt schema 1/2 与 install journal schema 1/2 继续由输入/恢复 adapter 读取，旧期望启停和模块状态语义保留。
- 新 kind 字段及 theme 所需记录不能无版本复用旧格式。新增版本明确存储 kind、目标根/键、来源与包身份、manifest/artifact digest、文件所有权和操作 ID，不包含 activation 成功伪标记。
- 旧记录类型判断在恢复输入边界；新业务只接收当前中立包状态。无法确认所有权的记录保留并报告，不猜目录名、不自动删未知文件。
- 不在此评估更新 Common gitlink。实际实现需协调中立源码、客户端消费与工具输入；固定依赖和游戏制品同批编译，线上 Index 输入延期。

## 4. 安装、更新与主题目录

| 路径 | 提交前 | 持久决定后 | Host 发布 |
| --- | --- | --- | --- |
| 安装 | 来源/范围/冲突/载荷校验；stage 全部变体 | 完成文件与收据，取消不再回滚 | 发布已验证来源集合，保持原选择 |
| 更新 | expected receipt/digest 校验，保存旧文件事实，stage 新全包 | 按 journal 完成旧/新位置与收据；未完成不发布混合目录 | 同 ID 变体刷新；已移除变体按缺主题规则回退显示且保留意图 |
| 卸载 | 用户确认使用影响，冻结目标与当前选择事实 | 仅删除已验证自有文件，完成记录与选择后处理 | 显式卸载选中变体转默认；保留用户 profile |
| 意外缺失/损坏 | 检查并报告该来源，不信任任意路径 | 不擅自写“已卸载”或清空配置 | 显示默认，保留原选择意图；无关合法主题继续 |

文件提交与 theme-settings 持久化不是一个跨目录原子操作，必须有可恢复的选择后处理。显式卸载决定须保存预期主题身份/选择版本与“需要转默认”的意图；应用前核对用户当前选择，不能覆盖用户之后选的其他主题。设置写失败保留未完成标记和诊断，重试只完成既有决定，不重发包删除，也不报告所有后处理成功。普通 UI 不承担恢复 schema 分支。

刷新在安全的主线程发布不可变来源/主题快照；文件检查在加载/刷新阶段，绘制不扫描目录、不逐帧读 XML。同次绘制固定 palette，注册和部分主题回退仍走既有解析规则。

## 5. 分批实现与验收依据

1. 中立存储 DTO/类型 adapter、共用来源/所有权事实与旧记录输入边界。
2. 数据包安装/更新/卸载、journal 恢复、lease/取消/选择后处理。
3. Host 来源服务、商店本地确认/管理、目录刷新和状态。
4. 目录 v4 与可信 Validator/工具固定源码；线上发布继续延期。

每批至少编译/源码核对，实际游戏与恢复验收另记录。当前未进行故障注入或游戏测试；不能把本文件设计等同于恢复验证通过。
