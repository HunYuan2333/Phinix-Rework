# 商店下载 / Chat 最小修复交付

2026-10-07；dev 工作区，未提交、未推送。用户要求立刻可核查的最小版本；完整主包含已验收 F4-A，不包含独立构建的 F4-B 示例候选包。

## 实施边界

删除 ManagedStoreLocalGate 及各项目 Compile 引用，规划器不再读取普通模组的 LoadFolders.xml 或磁盘 DLL。保留已加载 CLR 名称、受控托管包及模块重复检查，版本、包/模块/外部模组依赖检查，以及下载载荷严格校验、安装凭据和事务保护。未改 MetadataReader 的 DLL 标记限制，无按模组 ID 特判。

EasyUpgrades 作者仓库 1.1/1.2 DLL 身份读取失败可复现，1.6 DLL 正常。新规划器不读取这些磁盘文件。回归覆盖启用/未启用第三方模组、损坏 DLL、错误 XML、外部 DTD/条件/历史目录文本均不影响规划且文件不变；真实已加载同名程序集拒绝，关键运行环境缺失拒绝；实际 controller 下载和持久安装成功。旧 --local-identities 磁盘 CLI 明确退出 2，防止把离线磁盘扫描称为当前运行环境兼容证明。

Chat：完整消息转换和通知转到现有主线程 dispatcher；Stop 递增 generation，过期排队回调不执行；逐个订阅者异常隔离并记警告。兼容模式 Chat 回调先排队，再访问 Unity Time/游戏对象；Host 的兼容模式事件逐个隔离，失败日志观察者也不能阻止后续协议接收器注册。

用户本机能看到消息，对方暂无日志。上述为已查到的线程/异常传播缺陷，不宣称已复现或完全解决对方的消息故障。协议报文、物品所有权、存档、安装日志格式不变；Chat 通知变为主线程队列投递，需游戏核查时序。全局加载器、旧整模组安装器及最新环境重新采集仍按审查分批处理，本轮不宣称 F4-F2 全部完成。

English: ordinary mod disk files no longer veto Phinix plugin planning. Actual loaded identity conflicts, managed dependencies, payload validation and transactional ownership remain enforced. Chat UI conversion and mode callbacks are dispatched to the main thread, stale work is rejected, and subscriber failures are isolated. The remote chat report has no log and is not reproduced; the user reports chat works locally. No wire format or item ownership changes. Broader loader and fresh-environment work remain pending.

## 验证

- Managed runtime：net10 / Mono net472 主断言各 3108，启动子场景及 2137 条 Store operation 回归通过。
- Client composition：62 条通过，覆盖实际 Chat adapter 排队、订阅者隔离和清理。
- 真实 Host/Chat 二进制：17 场景通过，新增兼容模式事件先失败仍调用后续注册者，日志失败也隔离。
- 完整 clean + Release 1.6 构建：0 错误，29 警告；最终 no-restore 构建 0 错误，15 警告，包含既有代码/目标框架及网络不可达的 NuGet audit 警告。
- 等价 Python 分发检查：31 必需文件、runtime 唯一且与构建匹配、无游戏 DLL、ZIP 完整性及商店 DLL 字节匹配。PowerShell 不可用。
- git diff --check 通过；审查修改范围并保留 F4-B、接口/计划排版及其他并行修改。未进行游戏/真实线上安装核查。

实际命令（仓库根目录；/tmp/phinix-boundary-*.log）：

```sh
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Tests/ClientCompositionRuntimeTests/ClientCompositionRuntimeTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:BuildInParallel=false -m:1
mono Tests/ClientCompositionRuntimeTests/bin/Release/net472/ClientCompositionRuntimeTests.exe
dotnet clean Phinix.sln --configuration 'Release 1.6' -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration 'Release 1.6' -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/ChatRegressionTests/ChatRegressionTests.csproj -c Release --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/ChatRegressionTests/runtimeconfig.json Tests/ChatRegressionTests/bin/Release/ChatRegressionTests.exe
python3 /tmp/phinix-boundary-package.py
git diff --check
```

## 包及游戏步骤

Output/phinix-rework-store-chat-fix-20261007.zip；SHA256 abef1c0ec7cb68a0b4e20f3d332b11ff50273c05aea8dd25c14b97b37cbe269a。

1. 退出游戏；备份当前 Phinix 主包，再整体更新 ZIP 中 phinix-rework 内容，避免旧/新 DLL 混用。保留玩家数据及托管插件目录。
2. 保留 EasyUpgrades / 黑洞天使及原模组列表，刷新商店下载并安装红包；不应再出现针对这些第三方文件的 LocalIdentityUncertain。
3. 重启，检查红包插件只激活一次、界面可见；本轮不修改红包领取/库存逻辑。
4. 两个客户端互发聊天消息；验证双方接收、断线重连后接收、历史消息和新消息。测试存档内与主菜单状态。如对方仍失败，收集该次完整启动/登录/复现日志。
5. 真实缺失依赖或名称冲突继续拒绝；不要以关闭检查绕过。自动测试通过不代表服务器、网关及用户模组组合已经游戏验收。

## 读档历史反馈及接收时序追加修复

用户明确：同一进程重新读档，实时接收正常。对约 169 KB 的 Player (2).log 先关键字定位，再读取局部上下文；没有整份展开或提交玩家日志。rg 行号：框架初始化 571 仅一次；加载存档 1217/1930；Legacy 接收器注销 1802/2461，之后重新连接 1874/2531、登录/协商并回落 Legacy。因此排除本日志中重复构造框架，确认重连走了清空路径。一次资产清理约 63 秒，超过默认 30 秒连接超时，是断线可能诱因；日志无明确断线原因，不宣称已经证明超时因果或 F4-A 引入此行为。

继续核对同级原版 Common/Chat/ServerChat.cs:loginHandler：登录时立即发送 ChatHistoryPacket；原版 ClientChat 从构造期注册 Chat。Rework 原实现只在 3 秒协商超时进入 Legacy 后注册，登录历史可能先到而被 NetCommon 丢弃。该时序缺陷可以确定，具体用户历史包的到达时间没有日志佐证；迁移前版本也包含相同晚注册规则，不错误断言 F4-A 新增它。

实施：Legacy Adapter 在 Unknown 期间提前准备只读 Chat 接收器，激活及重连进入 Unknown 均可接收登录历史；Legacy 切换幂等注册；FrameworkV2/Shutdown 撤销 Chat。Trading 仍仅在 Legacy 下注册；不改变交易确认、协议、缓存清空或账号/服务器隔离。不能保证离线服务器一定提供历史，也不把聊天记录新增到存档。

English: this log shows one framework initialization and repeated reconnects around save loading. A long asset collection may contribute to a timeout but no disconnect reason proves that. The original server sends history on login while Rework previously waits for Legacy negotiation before registering Chat. The repair prepares the read-only Chat receiver during Unknown, removes it for FrameworkV2/Shutdown, and keeps Trade registration gated by Legacy. This closes a reproducible receive gap; the precise user packet timing and regression attribution remain unproven.

验证：完整 Release 1.6 no-restore 构建 0 错误/19 警告；真实 adapter 10 场景通过（原 9 交易场景加登录前历史/重连/模式切换/关闭场景）；31 分发资产及 ZIP 检查通过，git diff --check 通过。未运行真实游戏。测试初次构建缺 targeting pack，Mono 全图 override 又导致 protobuf net45 facade 错误；先按正常解决方案构建，再仅为测试顶层使用 Mono 引用并禁用依赖重建，新增可选 netstandard 编译 facade 引用，未改 vendored protobuf。

成功命令：

```sh
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet build Tests/LegacyTradeRuntimeTests/LegacyTradeRuntimeTests.csproj -c Release --no-restore -p:BuildProjectReferences=false -p:FrameworkPathOverride=/usr/lib/mono/4.7.2-api -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
dotnet exec --runtimeconfig Tests/LegacyTradeRuntimeTests/runtimeconfig.json Tests/LegacyTradeRuntimeTests/bin/Release/LegacyTradeRuntimeTests.exe
python3 /tmp/phinix-history-package.py
git diff --check
```

新整包 Output/phinix-rework-store-chat-history-fix-20261007.zip，SHA256 517bd719f0107eca46aec68825317d7a5d78e4a2d91faee1a4e2adfa27154780。包含上节商店门禁修复，不包含 F4-B 示例；旧交付 ZIP 保留。

复测：更新整包并启动 → 连接原服务器确认已有历史 → 不退出游戏重新读同一存档 → 若断线，重新连接后检查历史恢复及新消息收发；再重复一次并测试正常交易路径。若历史仍为空，下一步需确认服务器实际发送历史和客户端收到的包计数，不能直接删除断线缓存/所有权隔离。

最终再次构建整包：0 错误/15 警告；重新验证 ZIP 中 LegacyAdapter.Client.dll 与实际最新编译字节一致。交付副本保存在 /tmp/phinix-rework-store-chat-history-fix-20261007.zip，避免 Output 后续清理影响交付。

2026-10-07 用户反馈“没问题，继续我们的修复”：本最小包游戏核查已接受。没有逐步日志，不扩大范围；后续 F4-F2b 加载边界单独核查。
