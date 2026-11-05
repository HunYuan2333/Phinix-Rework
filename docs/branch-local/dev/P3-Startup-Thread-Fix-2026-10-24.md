# P3 客户端加载线程回归修复（2026-10-24）

范围：修复 P3 接收范围宿主接入错误，关联 SV-30～38 / TQ-30/31 的客户端前置。没有 UI 升级或服务端产品改动。

## 实际证据与分支对照

本机 Player.log 462～487 行：`Error while instantiating a mod of type PhinixClient.Client`，内部异常 `InvalidOperationException: Game scope validation requires the main thread`，栈 `ClientGameScopeService.MainThread → Publish → Client..ctor`；外层为 `LoadedModManager.CreateModClasses → LongEventHandler.RunEventFromAnotherThread`。只记录异常和方法，不复制原日志或凭据。

业务分支 `codex/p3-trade-business` 和冻结 UI 分支 `codex/uiux-release-main` 同以 53c92ce8eb314dcb8ca422ae4b6891a007931128 为 HEAD。业务 P3 新增了构造期 Publish；原分支没有。构造中断使后续延迟 InitializeExtensions 无法注册，Theme 永远未发布。此前 ServerTab 判空仅跳过绘制，不能修复这个初始化失败。

对照 ServerTab：P3-02 增加的三处 UI 防护全部撤回，文件与业务 HEAD 字节一致。冻结 UI 分支另有独立的 PHINIX_RIMWORLD_17 drawShadow=false 候选，本轮没有合并。原 UI 工作区 28 个 UI 与 9 个未分类冻结文件 hash 均保持一致；没有切换或重置其分支。

## 最小修复与不变量

移除构造函数的 Publish，保留纯服务构造/注册。既有 Client.Update 在主线程 Publish 后才 DrainPendingActions，初始化与交付回调都读取已发布的游戏范围。加载/网络线程不能读取 Current.Game；空游戏不给令牌，切游戏/回菜单旧令牌失效。没有放宽线程校验、自动 ACK、退款或跨存档重绑。

## 回归范围

HostStartupTests 消费实际打包 DLL，解码构造和 Update IL，禁止构造直接 Publish、要求 Update 的 Publish 在 dispatcher drain 前。用实际 scope service 与 dispatcher 在独立加载线程构造/入队，验证无游戏读取、错误线程 Publish 仍拒绝；主线程发布/执行以及回菜单失效。不是完整 Mod 构造或原生 GUI 测试。P3-02 DLL 的负对照在构造调用约束上失败，符合实际日志。

原 shell-readiness 探针只证明跳过绘制，已撤回，其历史证据保留，不当作 UI 可用性证据。最终构建/探针结果与命令见本轮 Deliverables manifest。真实 Waylith 1.7.4981 窗口、连接及交易存读档仍待人工验收；不关闭 P3/P6。


最终候选：`Deliverables/Phinix-Rework-P3-Test-2026-10-24-03/`。实际双版 Host 启动探针各 10 检查、wire 各 34 检查通过；双版完整产品/维护插件构建及 94 文件包审计通过。1.6 首次探针因缺少显式 Harmony 运行时引用失败，使用项目既定 2.3.6 net472 引用后通过，失败输出保留。测试未绕过产品校验。构建初次操作路径有误启动了修复前的 03 目录构建，未使用其制品；最终是全新 03b 修正后构建，实际 DLL 回归检查通过。
