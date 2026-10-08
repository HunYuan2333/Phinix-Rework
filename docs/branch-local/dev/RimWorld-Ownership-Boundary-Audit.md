# RimWorld / Phinix 职责边界审查

2026-10-07；静态审查，未修改运行代码或运行游戏。保留当前 F4-B 和其他并行修改。This is a static boundary review, not a compatibility certification.

## 判定原则 / Ownership rule

RimWorld 决定普通模组的启用、有效加载目录和游戏程序集加载。Phinix 管理自己声明的插件载荷、受控目录、登记状态及事务；通过游戏/CLR 已提供的事实检查实际冲突。明确针对 Phinix 的主题和模块契约可以消费，不代表授权接管提供方整个模组。必要的存档桥接和主线程回调按最小范围保留。

RimWorld owns ordinary mod selection and loading. Phinix owns its declared plugin payloads and recorded installation transactions. Runtime facts may establish actual conflicts; unrelated mod files must not become universal installation vetoes.

## 发现 / Findings

### P1：全盘身份门禁 / Global disk identity veto

`Extensions/PluginStore/Client/ManagedStoreLocalGate.cs`：遍历所有 InstalledMods，解析所有 LoadFolders li，扫描根/Common/所有版本目录。把未启用或非当前版本文件读取失败变成安装失败；当前不按游戏条件选目录。EasyUpgrades 公开仓库 1.1/1.2 DLL 可复现拒绝，而 1.6 可正常读取。

建议移除普通模组磁盘身份预留，使用已加载 CLR 身份和受控托管包清单。第三方历史文件未知不否决安装；启动时实际冲突拒绝受影响 Phinix 插件执行。插件载荷严格校验继续保留。Move this F4-F2 work before further plugin migrations; do not introduce per-mod exceptions.

### P1：替游戏主动加载第三方 DLL / Loading ordinary mods on behalf of the game

`Client/Source/Client.cs:GetExtensionProbeDirectories`：加入游戏程序根目录及所有活跃模组根 Assemblies；`Common/Utils/Framework/ExtensionAssemblyLoader.cs:LoadAssemblies` 对目录内所有 DLL 尝试 LoadFrom。根目录并非游戏当前选定的有效目录，可能执行游戏没有选择加载的 DLL；“发现 Phinix 模块”不需要先主动加载无关程序集。

建议普通 submod 仅从游戏已经加载的程序集发现明确 Phinix 模块。主动加载限于 Phinix 明确拥有/登记的 bundle、运行库和托管包；移除第三方目录及程序根目录通配探测。保留第三方作者经游戏正常加载后使用同一模块发现/注册/激活通道的能力。Plan migration for any author relying on the old undocumented probing behavior.

### P1：全局依赖解析缺少请求者边界 / Unscoped global resolver

`Common/Utils/Framework/ExtensionAssemblyLoader.cs`：全局 AssemblyResolve 按简单文件名搜索，未要求 RequestingAssembly 是 Phinix 管理的程序集，未核对完整身份；同名缓存可覆盖。当前 managed resolution guard 只阻止托管插件回落到此旧搜索，不保护第三方请求者。

建议统一明确来源/身份索引，仅处理自己负责的请求者和已声明依赖；其他请求返回 null 交回游戏/CLR。支持库处理须覆盖初次加载阶段，但不扩大到整个 AppDomain。启动及停止配对撤销解析器，取消通配目录回落。现有 ManagedExtensionRuntime.Resolve 已有请求者归属及依赖声明检查，可作为边界参考，不能声称 net472 的共享 AppDomain 因此成为沙箱。

### P2：旧整模组安装器重复解析第三方 About / Legacy whole-mod metadata reimplementation

`Extensions/PluginStore/Client/ManagedInstallation.cs:EnsureNew/CheckExternalDependents`：遍历本地/创意工坊 About.xml；无关错误会阻断自身操作，依赖检查汇总所有版本/未启用依赖，未遵循当前游戏条件。此路径属于 rimworld-mod-zip，StoreBrowserController 仍引用；不要与当前 phinix-dll 安装器混为一谈。

建议安装身份/有效依赖取游戏事实；明确是否保留整模组安装产品能力。若保留，只写用户选择且安装凭据证明归属的目标；不擅自启用 ModsConfig，不删除第三方目录。对直接目标的所有权不确定仍须拒绝写入/删除。未知依赖应独立报告，不宣称无依赖或让无关 XML 成为全局否决。第三方依赖提醒与托管插件真实依赖保护分开。

### P2：主题发现缺少明确选用与有效资源边界 / Theme selection boundary

`Client/Source/UI/ThemeLoader.cs:LoadThirdPartyThemes`：扫描所有活跃模组根 Themes/*.xml 并按遍历顺序覆盖颜色/参数。PhinixTheme 根节点表明它消费的是自身资源格式，不能等同于 DLL 加载越界；但有效目录、选择及覆盖优先级需要明确，避免普通同名目录影响主题。

建议显式主题提供声明/用户选择，使用游戏有效资源或插件声明资源；解析仅针对选定主题，故障仅影响该主题。不把此项扩大为移除全部第三方主题能力。

## 已查范围内应保留的边界 / Retain

- ClientEnvironmentCapture 读取游戏模组列表、CLR 已加载身份和来源：属于环境采集，不接管加载；安装前需要主线程重新采集，当前 controller 复核重复使用传入快照。
- ManagedExtensionRuntime.Resolve 仅处理自己的请求者和声明依赖，返回已批准实例；Dispose 撤销订阅。它的全局事件入口本身不等于越权。
- 安装/卸载凭据、受控目标、哈希、链接/路径越界和事务恢复检查：属于自身文件所有权保护。此次没有发现可以据以断言“已擅自删除普通第三方模组”的证据；并非全仓库删除代码形式化证明。
- Inventory 的 FillComponents/精确 InventoryGameComponent 类型解析及存档身份补丁、Root.Update 回调：作用于自己组件/生命周期，不能仅因使用 Harmony 判越权；F4-D 继续核查禁用、清理与存档兼容，不随本次审查移除。
- 模块契约发现、依赖与用户禁用策略：管理 Phinix 模块，不等于管理整个 RimWorld 模组。

## 批次建议 / Proposed batches

1. F4-F2a：移除第三方全盘安装门禁，明确安装与启动激活结果，补当前环境复核和真实冲突回归。
2. F4-F2b：主动加载范围、请求者归属、完整身份和解析器生命周期收敛；普通 submod 通过游戏已加载程序集发现。
3. F4-F2c：旧整模组安装路径的产品取舍及元数据边界；主题声明/选择另立小批次。

Each batch must check real collisions, unrelated malformed/inactive/historical mod files, third-party dependency resolution remaining untouched, unchanged transaction recovery, and in-game loading/disable/restart behavior. Signing/trust remains before public author tooling; signature does not grant authority over ordinary mods or provide runtime isolation.

本轮验证：阅读上述代码并交叉检索 ModLister/LoadFolders/AssemblyResolve/加载和删除入口；git diff --check。无实现变动，因此未构建或执行测试。Audit only; no runtime behavior changed or tested.

## 实施进展（2026-10-07）

商店磁盘门禁移除及登录历史接收空窗修复已获用户接受。客户端第三方/游戏程序目录主动加载、旧全局解析器使用已改为自身根目录及可释放的请求者/完整依赖身份范围，见 F4-F2b-Owned-Loader-Handoff.md，待游戏核查。服务端旧 API 不在本批切换范围；旧整模组 About 元数据、主题与最新环境采集仍未完成。Implemented candidate boundaries do not imply every finding has been resolved.

## 用户范围决定 / User scope decision（2026-10-08）

整模组 ZIP 安装方向及 F4-F2c 收窄方案取消；本轮没有删除/禁用旧安装器代码，原静态发现不因此消失。第三方主题选择、有效资源边界及商店下载主题转为后续增强评估，现有主题行为不改，见 Theme-Store-Future-Evaluation.md。以上两项不再作为 F4 结束门槛，用户明确要求文档更新后关闭 F4；F5 不自动启动。

The whole-mod ZIP installation direction/narrowing proposal is cancelled; no legacy installer code was removed or disabled, and the original finding is not claimed fixed. Theme selection/effective-resource boundaries and store theme downloads move to future enhancement evaluation. They no longer gate the user-directed F4 checkpoint closure. F5 needs further user authorization.
