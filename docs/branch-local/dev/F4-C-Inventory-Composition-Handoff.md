# F4-C Inventory 装配 / Inventory composition

2026-10-08，dev。用户接受前一批并要求 DI 前后行为一致。本批实现/回归完成，待游戏核查；未提交，保留 F4-B、商店及其他并行修改。

## 所有权与变化 / Ownership and changes

- BuiltInInventoryClientExtension 改为 ClientExtensionModule.Compose，通过普通 IPhinixExtensionModule 内部桥接进入相同发现/注册/停止路径；没有客户端宿主特殊注册。
- scope 拥有 InventoryTab 和 InventorySettingsPanel。设置页/快捷设置仍共用同一个 panel，九个发布 API 及顺序不变。六个库存 API 仍引用模块同一个 facade，供 Trade/红包等借用；未替换 API 身份。
- 模块本身、settings、dispatcher、shell event stream、window service 为 Borrow。宿主依赖在 Compose 解析，Activate 仍负责原来的 Harmony、shell 订阅及排队绑定。被禁用模块由普通注册器在构造前排除。
- ledger、journal、GameComponent、存档身份、codec/source presenter 注册表仍由原模块/游戏生命周期管理。原 OnMainWindowOpened 之后所有方法源码完全一致，未改变预约、确认、批次、提取、调度、codec 或持久化算法。Contract Version 仍为 5。
- InventoryTab 增加幂等 IDisposable，scope 停止或构造/注册失败时移除两项库存事件订阅；原 UI 构造时的被动失效订阅时间不变。原版缺少停止解绑，这是本批明确的清理修复。
- 模块原 Shutdown 操作顺序保留，scope 在 finally 释放。原 journal/Harmony 清理若抛异常仍可能中止后面的原模块清理，本批不声称已经重写这些恢复行为；F4-D 专门审查存档生命周期及失败清理。

No new runtime library or contract version is introduced. Ordinary prepared-host behavior is preserved; missing host services are now rejected during passive composition rather than later activation. External code must use the published inventory contracts and ordinary module lifecycle, not call a concrete implementation's old Register method directly.

## DI 前后实际 DLL 对照 / Before and after evidence

在编辑 Inventory 前保存实际已构建 DLL（没有重新实现一套旧业务逻辑）。同一探针在独立进程中加载旧/新 DLL，避免同名程序集互相遮盖。

| 核查 | 迁移前 | 迁移后 |
| --- | --- | --- |
| 实际 facade/API 注册、共享 panel、初始状态、codec 重复拒绝和 scoped token 事件次数 | .NET 10、Mono 各 24 assertions | .NET 10、Mono 各 52 assertions |
| FACT 输出：九项映射、全部能力与容量、Inactive 状态、4 次 codec 通知 | 与新版本逐行一致 | 与旧版本逐行一致 |
| 原库存预约/分组/组件快照/journal 场景 | 两运行环境各 13 场景通过 | 两运行环境各 13 场景通过 |
| ledger 对象身份与 host/codec Borrow 不释放 | 通过 | 通过 |
| 构造中失败、注册发布失败、缺 host 服务、普通注册器回滚、scope 单次清理 | 原版本没有 DI scope | 新版本通过 |
| 禁用状态、普通发现/注册、停止 API 撤销 | 通过 | 通过 |

两套 UI 服务重复 Resolve 为原发布的唯一实例；注册阶段不入队、不打开窗口、不订阅 shell activation 事件。无界面探针没有调用 Harmony/完整 Activate 或绘制 Unity UI，不能替代实际游戏行为验收。

迁移前 DLL SHA256：`5ddfc2bd3dfeb73d040aa066fd07bd4e0f9da8328bf09c0d45d71a9a93c0fd06`
迁移后 DLL SHA256：`1858e77c6a519be4331670f091e000d4919d5198bb98077343197a99db94dfa8`
旧 DLL/临时日志在 `/tmp/phinix-inventory-before/` 等路径，仅用于本次验证，不入仓库。

## 构建与环境 / Build and environment

- 全量 Release 1.6：0 errors / 9 warnings。新增装配测试：0 errors，最终增量构建 5 个既有 NuGet 警告。
- 原 InventoryRuntimeTests 旧式工程在当前 SDK 的 GetTargetFrameworks 阶段失败，输出 0 errors 但退出码为 1；也尝试了 Mono FrameworkPathOverride 和 MSBuildSDKsPath，没有把它写成测试通过。
- 新增 Portable SDK 入口，保持原 Program.cs 与 InventoryJournalScenarios.cs 不变、AssemblyName 相同，复用真实库存 DLL；构建 0 errors / 11 warnings。它保留原 Windows 工程，不修改 vendored protobuf。
- Mono 运行 13 场景时，编译用游戏 DLL 提示缺 Unity.Burst 属性依赖；迁移前后均如此，场景仍退出 0。没有补游戏运行库到分发包或声称完整 Unity 环境。
- 主包产物 31 项、唯一运行库、无游戏引用 DLL、Utils/Client/Store/Inventory 构建字节匹配及 ZIP 校验通过。pwsh 不可用，使用 Python 等价检查。
- git diff --check 通过。新源码均在 SDK 项目自动发现；Portable 测试显式复用原两个源码，未增加一套领域实现。

### 实际命令 / Commands

```sh
dotnet build Tests/InventoryCompositionRuntimeTests/InventoryCompositionRuntimeTests.csproj -c Release -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/InventoryCompositionRuntimeTests/runtimeconfig.json Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe --legacy /tmp/phinix-inventory-before/InventoryExtension.Client.dll
dotnet exec --runtimeconfig Tests/InventoryCompositionRuntimeTests/runtimeconfig.json Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe
mono Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe --legacy /tmp/phinix-inventory-before/InventoryExtension.Client.dll
mono Tests/InventoryCompositionRuntimeTests/bin/Release/net472/InventoryCompositionRuntimeTests.exe
dotnet build Tests/InventoryRuntimeTests/Portable/InventoryRuntimeTests.Portable.csproj -c Release -p:BuildInParallel=false -m:1 -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet exec --runtimeconfig Tests/InventoryRuntimeTests/runtimeconfig.json Tests/InventoryRuntimeTests/Portable/bin/Release/net472/InventoryRuntimeTests.exe
mono Tests/InventoryRuntimeTests/Portable/bin/Release/net472/InventoryRuntimeTests.exe
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
python3 /tmp/phinix-f4c-package.py
git diff --check
```

13 场景的旧版本对照：仅在测试 bin 中暂时放入保留旧 DLL，分别启动 .NET 10/Mono 独立进程，finally 恢复当前 DLL；未替换实际主包。四组输出保存在 `/tmp/phinix-f4c-domain-{before,after}-{net10,mono}.log`。FACT 对照四行完全相同。

## 游戏步骤 / In-game checks

1. 替换完整本批主包并重启，加载已有测试存档，确认库存数量/来源/预约信息没有变化；打开库存、设置页与快捷设置，检查接收方式保持原选择。
2. 存入和提取一批普通物品，再核对数量；用库存发/领红包或已有交易路径做小额核查，确认依赖仍取得同一库存 API。
3. 保存并读档，返回主菜单再进入，确认库存及设置保持；本批没有改变存档/journal 格式。
4. 禁用 Inventory 后按界面要求重启，应没有库存入口；重新启用并重启应恢复原数据和入口，不能重复弹接收方式窗口或多次处理同一动作。

游戏核查待完成；本批不承诺所有第三方 codec 的运行时行为已经验证。后续 F4-D 明确存档生命周期，不改动权威确认、预约所有权或批次原子性；F4-E 再推进 Trade 装配。

主包：`/tmp/phinix-rework-f4c-inventory-di-20261008.zip`
SHA256：`f42486b04e0aa3f645c1625c4fcb4092853f9656be736c2febfa56f80d5ad836`
本批无新的持久化格式/协议变更；已有 schema 2 安装事务的恢复/回退限制继续有效。
