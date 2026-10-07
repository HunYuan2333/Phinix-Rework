# F1 生命周期交接 / Lifecycle handoff

日期 / Date: 2026-10-07. 分支 / Branch: dev.

## 中文

输入基线为 `f312c38940c8026f3f680089cf99b039402dc025`；本批直接提交 dev。F2 独立候选探针、其他规划/审计草稿、原有客户端抽象文件工作区状态、IDE 与构建输出不属于本次提交。protobuf gitlink 为 `4b0c3aacf0657fbf38253b38918d3358dd4319ec`，未修改供应商源码。

交付：framework 构造只保存依赖，宿主服务齐备后在主线程显式 Start；ClientExtensionRuntime 协调普通模块发现/注册/激活和幂等终结关闭。注册/激活失败回收本次资源；正常退出通过 Unity 主线程事件清理。消费者先停止，清理异常不会阻止其他模块；撤销模块 API、handler、持久化登记和本 framework 的服务槽。借用服务/API 不由消费方释放。生产 runtime 的测试覆盖构造无副作用、禁用不构造、部分失败、重复启停、错误线程、依赖顺序与清理异常。

| 归属 | 项目与资源 |
| --- | --- |
| 中立共享 | Common/Utils（net472/net10.0）持有 registry、上下文、协议；客户端 03-Utils.dll 和服务端 Utils.dll 各由对应端分发 |
| 客户端宿主 | Client/Source（net472）持有 framework、计时器、网络登记及服务槽；Client 负责本地化和托管运行时；13-PhinixClient.dll 由主包分发 |
| 客户端抽象 | ClientExtensionAbstractions（net472）有 Verse/Unity 类型，不能整包视作游戏无关 Shared；本次不编辑原有并行接口文件 |
| 端点链接源码 | Common 排除 NetClient/NetServer、ClientAuthenticator、ClientUserManager；客户端 endpoint 项目仍从 Common 链接客户端源码，后续拆仓需按真实归属迁移 |
| 连接、存档与操作 | 连接由现有 net/auth/user 服务持有；Inventory 负责存档身份和 journal；Store 持有单次操作及取消令牌；全局设置由宿主持有，不归入存档作用域 |

公开影响：新增 ExtensionHostContext.RemoveService<T>(ownedInstance)，按引用身份撤销且不 Dispose 服务。作者接口签名未破坏；Utils 仍为 0.9.7.0，客户端抽象仍为 1.8.0.0。新宿主调用新 Utils 方法，部署与回退须成组处理 03-Utils.dll、13-PhinixClient.dll。第三方 Shutdown 可能在 Register/Activate 未完成时被调用，必须容忍部分初始化；已核对官方客户端/服务端的空值清理。未改变设置/存档格式、协议、交易 ACK、物品所有权、安装事务或打包布局。

验证在仓库根执行（属性展开为下列绝对路径），均通过；完整解决方案包含客户端主包和服务端。完整编译 0 错误、9 个既有警告。Phase35 全部通过；Managed 在 .NET 10 和 Mono 各通过 937 个主 harness 断言及六个子进程场景。

```sh
phinix_repo_root=/home/hunyuan2333/Phinix/Phinix-Rework/
dotnet build Phinix.sln --configuration "Release 1.6" -p:SolutionDir="$phinix_repo_root" -p:RimWorldDepDir="${phinix_repo_root}GameDlls/" -p:GameReferenceDirectory="${phinix_repo_root}GameDlls/" -p:BuildInParallel=false -m:1
dotnet build Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet Tests/Phase35RuntimeTests/bin/Release/net10.0/Phase35RuntimeTests.dll
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release -p:SolutionDir="$phinix_repo_root" -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
python3 /tmp/phinix-f1-check-artifacts.py
git diff --check
```

限制：游戏引用使用已有 GameDlls/，不存在 GameDlls/1.6。单独以 Release 1.6 构建 Client.csproj 曾因传统抽象项目配置映射缺 OutputPath 失败；solution 映射下成功，未改项目配置。NuGet 审计网络请求曾产生 NU1900，构建/回归仍通过。没有 pwsh，未执行 PowerShell 打包脚本；临时 Python 按当前规则等价只读检查 21 个必需文件、语言、加载目录及游戏 DLL/退休插件禁入，结果通过。临时脚本和 /tmp 日志不提交。未改布局或交易业务，未追加 geometry/LegacyTrade harness；没有服务器部署或助手亲自进游戏验证。

用户在游戏反馈后回复“没问题，准备F2”，随后授权直接提交 dev。据此接受首批；未将每个重连/换档/退出步骤补写为单独确认。日志中的红包中继 HTTP 500 自行恢复，其与最终入库的因果证据不完整；另有公告/Legacy Chat 通知 NRE，仍单独跟踪，不宣称全日志无错误或公告已验收。

复测使用测试配置和存档：部署匹配的整套主包 → 启动检查 Chat/Store/示例 → 发收消息 → 断线重连再发收 → 返回主菜单进入另一测试存档 → 正常退出；另验证示例禁用/启用后重启，无重复 Tab、消息、订阅、迟到回调或清理错误。后续 F2 集成另交付。

## English

Input baseline: f312c38940c8026f3f680089cf99b039402dc025, committed directly on dev. F2 candidate probes, other planning drafts, the pre-existing abstraction-file working-tree state and generated/IDE files are excluded. Vendor protobuf source/gitlink is unchanged.

Construction is passive; the host explicitly starts after preparing services on the game main thread. ClientExtensionRuntime uses the ordinary module discovery/registration/activation path and terminal idempotent cleanup. Partial startup failures and normal Unity quitting release owned resources; consumers stop first, cleanup errors are isolated, and module registrations/framework service slots are revoked. Borrowed host services and cross-plugin APIs are not disposed. Tests link the production runtime and exercise construction, disabled modules, failure, repeats, thread rejection, dependency order and cleanup exceptions.

Common/Utils owns neutral registry/context/protocol code for net472/net10.0. Client/Source owns the net472 framework and its timer/network/service registration; Client owns localization/managed host resources. Client abstractions retain game-dependent contracts. Client endpoint projects still link source physically under Common; those files are excluded from shared compilation and require ownership-based relocation later. Connection, save and operation lifetimes remain independently owned by existing endpoint, Inventory and Store services; global settings stay host-owned.

RemoveService<T>(ownedInstance) is additive and removes only the matching owner without disposal. Existing author-interface signatures and versions remain: Utils 0.9.7.0, client abstractions 1.8.0.0. Deploy/roll back matching 03-Utils.dll and 13-PhinixClient.dll. Third-party Shutdown must tolerate incomplete Register/Activate; official client/server partial cleanup was checked. Settings/save formats, protocol, ACK/item ownership, installation transactions and packaging layout did not change.

The exact commands above passed: full solution build (client/server, zero errors, nine existing warnings), Phase35 and Managed harnesses on .NET 10/Mono (937 main assertions plus six child scenarios each). Existing GameDlls/ references were used; GameDlls/1.6 is absent. Direct Client.csproj Release 1.6 configuration mapping failed, while solution mapping succeeded. NU1900 audit network warnings did not prevent validation. PowerShell was unavailable; an equivalent temporary read-only packaging check passed 21 required files/languages, load folders and game-DLL/retired-plugin exclusions. Temporary scripts/logs are not committed. Unchanged geometry/LegacyTrade harnesses, server deployment and agent-operated game testing were not performed.

After game feedback the user accepted continuation to F2 and authorized this dev commit. Individual reconnect/save/quit steps were not separately confirmed. Relay HTTP 500 recovered without code changes; its relation to eventual deposit remains unproven. Notice/Legacy Chat notification NREs remain separate known observations. Retest with isolated settings/saves: matching package, startup/chat/store/example, send/receive, reconnect/repeat, another save, normal quit and example disable/enable; check duplicates, stale callbacks and cleanup errors. F2 integration is a separate delivery.
