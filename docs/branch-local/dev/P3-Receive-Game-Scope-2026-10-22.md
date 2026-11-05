# P3 交易接收范围：入队前捕获（2026-10-22）

关联 SV-32/33、TQ-30/31、FR-12 的客户端交付子集。客户端业务工作区 `Phinix-Rework-P3-Business` / `codex/p3-trade-business`，HEAD `53c92ce8eb314dcb8ca422ae4b6891a007931128` 加未提交变更。依赖方向：Trade 使用 ClientExtensionAbstractions 的中立游戏生命周期令牌，Host 提供实现；Host 不引用 Trade，不增加业务启动/恢复分支。客户端 Common `c165f61`、Server Common `67f243d`、protobuf gitlink 均不变，无新增外部包。

## 问题与产品行为

原实现将完成/取消通知排入主线程队列后，才构造游戏范围并读取当前 Game。确定性顺序 `接收于 A → 切换 B → 执行队列` 会绑定 B 的存档并开始交付。新增真实行为组件回归先捕捉此问题：预期 Received，实际已进入交付尝试。旧失败日志保留在本轮证据目录。

新增通用 `IClientGameScopeService` / `IClientGameScope`。Host 仅在主线程发布 Game 生命周期；网络线程 Capture 只读已发布令牌，不调用 Verse。主线程验证同时检查令牌寿命和实时 Game 引用，即使尚未来得及发布下一帧也能拒绝切换；失效令牌不能复活。菜单不发布有效令牌，宿主关闭清除发布并终止后续发布。

正式 Trade 统一完成入口在枚举载荷、文件 IO、解码及回调之前捕获令牌，由插件内部事件子类携带到实际交付 owner。owner 在存档绑定和持久 attempt 前验证。缺少宿主支持或令牌失效时保留原始 inbox，不绑定后来打开的游戏、不调用交付、不发 ACK。空批次无需 Game，但主体与 endpoint 仍须有效。当前 Shared wire/event 基类、持久日志 schema 和 Legacy Adapter 没有变化；普通插件通过同一中立服务获得同等能力。

## 实际检查

SDK 10.0.113 / runtime 10.0.13。构建环境：

```sh
export NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages
export DOTNET_CLI_HOME=/tmp/phinix-dotnet-113
export MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks
```

- L1/文件组件：`dotnet build Tests/TradeCompletionInboxTests/TradeCompletionInboxTests.csproj --no-restore -m:1 -nr:false -p:BuildInParallel=false`；`dotnet Tests/TradeCompletionInboxTests/bin/Debug/net10.0/TradeCompletionInboxTests.dll`。80 项通过，包括原接收/attempt 强杀检查以及后台 Capture 不读取游戏、后台验证/发布拒绝、切换/菜单/停止/失效不复活、空批次及缺少服务。
- L2：在已有实际 1.7 源码图 DLL 闭包上运行 `dotnet run --project /tmp/phinix-p3-query-composition-20261022/Tests.csproj --no-restore -m:1 -p:BuildInParallel=false`。真实服务验证在输入枚举和 IO 前捕获令牌；实际交付行为验证切换期间 Received/Unbound/无 ACK，稳定 Game 仍进入真实 codec 整批拒绝并保留 ReturnedFailure。最终断言数见本轮 manifest。Portable runner 没有原生语言服务，通知展示抛出的特定 `Verse.Find.get_ActiveLanguageWorker` 空引用在测试中被明确识别，继续独立检查持久状态；业务或其他异常不吞掉。
- 1.7：已有 `build-net10-graph.py` 生成的 26 项目源码图新增两个编译输入，执行 `dotnet build /tmp/phinix-p3-query-product17-20261022/Joint.csproj --no-restore -c Release -m:1 -nr:false -p:BuildInParallel=false`，76 个既有警告、0 错误。包含维护插件编译闭包。
- 1.6：向原隔离源码副本刷新本切片 9 个产品输入，执行 `dotnet build PhinixClient.sln --no-restore '-p:Configuration=Release 1.6' -p:BuildInParallel=false -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1 -nr:false`，24 个既有警告、0 错误。未重新构建外部插件；未更改其 API 调用入口。
- 两版实际 Trade DLL 的 wire runner 各 34 项通过；使用 synthetic FrameworkV2，不涉及游戏到账。命令分别为 `dotnet run --project /tmp/phinix-p3-query-wire{,16}-20261022/Wire.csproj --no-restore -m:1 -p:BuildInParallel=false`（1.7 的目录名没有 16）。
- 对照首次冻结 manifest，原 UI 工作区 28 个 UI 与 9 个未分类文件共 37 项 hash 全部一致。本分支 `git diff --check` 仍报告先前 NetClient 等 CRLF 尾空格；未清洗无关旧修改，本切片输入另行检查。

首次 runner 指定产品路径因已加载程序集位于复制闭包而被路径校验拒绝，改用默认闭包 DLL；首次完整完成事件的组合测试触发现有 fixture 缺少 Unity main-thread 身份，最终分别验证真实统一服务的原范围传递与真实交付 owner 的队列范围。未放松产品线程约束。首次默认并行 MSBuild 在子项目协商处无诊断失败，单节点且 BuildInParallel=false 构建通过；无重复绕过测试失败。上述失败日志随最终通过证据一起保留。

## 仍未完成

这是同一进程的 Game 生命周期 fence，不是持久存档归属、游戏已保存回执或资产守恒证明。同一 Game 对象内 Save As/同路径旧存档回滚仍需独立语义；在两个发布/验证点之间发生且恢复为相同对象的完整隐藏切换也不由此令牌证明。进程退出后不会恢复游戏令牌，不自动采纳当前存档或重投。

现代 recipient/deliveryId receipt、持久去重后的安全交付恢复、按原结果结算未知库存预留以及服务端冷归档/总磁盘预算仍待完成。未重跑 L3 网络联合检查，因本切片未改变 wire 或服务端；此前网络证据不等同于本轮完整游戏运行证明。未执行真实 RimWorld/Mono/1.7.4981 的物化、存读档或双客户端联机验收。P3 完整目标与 P6 不标通过。无安装/发布、部署 ECS、提交或推送。
