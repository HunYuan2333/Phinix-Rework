# Command Capability Relay

本功能为客户端插件提供通过 Framework Server 通信的公共服务。服务端依据显式能力声明选择接收连接，业务处理由目标插件实现。

## 处理模式与生命周期

CommandRoute.Mode 为 Server 时由服务端执行业务，为 Relay 时由目标客户端插件执行业务。历史报文缺少 CommandRoute 时按 Server 处理。Trade 的权威交易操作和 Chat 历史查询继续使用现有服务端处理流程。

Relay 经过认证、资源限制、Command Interceptor、最终报文校验、能力 Router、Outbound Interceptor、发送前资格检查、投递及 Observer。服务端插件可按 CommandRoute.PluginId、CommandType、Version 和目标玩家匹配。Continue 继续，Replace 重新校验，Blocked 返回拒绝，Handled 返回 Status 为 Handled。处理模式切换被拒绝。服务端处理异常终止当前请求，Observer 异常隔离。

Observer 可通过 ServerFrameworkContext.CommandRelayResult 获取路由结果。旧 Observer 的 action 参数继续可用。Outbound Interceptor 可以缩小接收集合，最终报文必须符合授权与接收能力，编码大小须处于本次已预留预算内。

## 声明与接收

插件从 ExtensionHostContext 获取 IClientCommandCapabilities。ownerId 使用模块的 ExtensionId，插件持有并释放声明句柄。模块停止、激活失败或注册回滚也会按 ownerId 清理声明。

```csharp
private IDisposable declaration;

public void Activate(ExtensionHostContext host)
{
    declaration = host.GetRequiredService<IClientCommandCapabilities>()
        .Declare(ExtensionId, "author.research", "request", version: 1);
}

public void Shutdown(ExtensionHostContext host)
{
    declaration?.Dispose();
    declaration = null;
}
```

Handler 按现有 builder.AddClientCommandHandler 注册，独立匹配报文。

```csharp
public bool CanHandleIncomingCommand(FrameworkPacket packet)
{
    var route = packet.CommandRoute;
    return route?.Mode == CommandProcessingMode.Relay &&
        route.PluginId == "author.research" &&
        route.CommandType == "request" && route.Version == 1;
}
```

框架按声明路由，插件负责兑现声明。Handler 匹配和执行异常都受客户端分发边界保护。

PluginId 和 CommandType 使用小写 ASCII 字母、数字、点、下划线与连字符，首字符为字母或数字，各最多 128 字符。版本为正整数并精确匹配。core、builtin、framework 及其点分命名空间保留给 Framework。PluginId 不具有认证权限。

同一能力的多个声明句柄独立持有，最后一个释放后撤销。声明可以先于连接建立。重复释放和撤销无声明的 owner 幂等。

## 发送与结果

```csharp
var relay = host.GetRequiredService<IClientCommandRelay>();
string payload = FrameworkSerialization.SerializePayload(request);

// 玩家单播会向该玩家的每个合格连接投递一次。
RelayResult direct = await relay.SendRelay(
    "author.research", "request", payload,
    targetPlayerUuid: targetUuid, version: 1);

// 省略目标即按能力广播，默认排除发送连接。
RelayResult broadcast = await relay.SendRelay(
    "author.research", "request", payload);
```

每个具体 CommandType 均需接收方显式声明，例如 announcement 需要单独声明。声明 request 不会获得 announcement 的接收资格。

Task 完成提供本地拒绝或服务端路由结果。Status 为 Submitted 时表示网络层接受本次接收集合的提交。Handled 表示服务端插件消费请求。Partial 保留部分提交或不确定结果，Unknown 表示客户端等待结果超时、发送期间异常或连接断开。Deliveries 逐连接记录 Submitted、NotSent 或 Unknown。调用方应同时检查 Status、Error 和 Deliveries。

零匹配广播返回 NoRecipients。单播目标离线返回 TargetOffline，在线目标没有 Relay 协议支持返回 UnsupportedRelay，支持 Relay 的连接均缺少能力返回 NoCapability。其余错误包括 InvalidCommand、InvalidCapability、NotAuthenticated、Forbidden、PayloadTooLarge、RateLimited、CapabilitiesNotSynced、Blocked、ProcessingFailed、DeliveryFailed 和本地 NotSent。

业务处理成功由插件自定义 ACK、超时及幂等机制表达。Framework 不自动重发已提交或结果未知的业务 Command。Task continuation 使用异步调度，修改游戏状态时必须通过 IClientMainThreadDispatcher 回到主线程。

## 能力同步

Hello 协商 core.command-relay.v1，服务端返回当前连接的 relay_epoch。客户端随后发送带 epoch、revision 的完整声明快照。完整验证后服务端原子替换索引并 ACK。

相同版本相同内容可重发，相同版本内容冲突拒绝，较旧版本不会覆盖较新状态。失败保留上一份有效快照。客户端重试最新快照，间隔逐步增长至 10 秒，持续失败通过 SyncState 与 SyncError 暴露。声明变化由 500 ms 周期合并发送。

IClientCommandRelay.SyncState 提供 Disconnected、Pending、Synced、Failed，SyncError 提供发送、ACK 超时或服务端拒绝原因。初次快照确认前 SendRelay 返回 CapabilitiesNotSynced。断线保留本地声明并终结待决请求为 Unknown，重连获取新 epoch 后全量恢复。会话令牌轮换沿用连接 epoch。

## 预算与授权

| 项目 | 当前值 |
| --- | --- |
| 单连接能力 | 最多 256 项，本地最多 4096 个持有句柄 |
| 本地能力载荷 | 保留信封余量，最多约 60 KiB |
| 快照和 Relay 信封 | 编码后最多 64 KiB |
| 快照控制流 | 每连接每秒 4 次，突发 4 次，超额丢弃等待客户端重试 |
| 快照状态替换 | 至少间隔 250 ms，幂等重发受控制流预算约束 |
| Relay 请求 | 每连接每秒 10 条，突发 20 条 |
| 广播接收连接 | 最多 128 个，超限拒绝整次路由 |
| 发送者扇出 | 每秒 1 MiB，突发 2 MiB |
| 全局扇出 | 每秒 8 MiB，突发 16 MiB |
| 客户端待决请求 | 最多 64 个，15 秒等待结果 |
| UDP Relay 排队 | 单接收连接可靠有序队列 2048 包，全局 8192 包水位 |

服务端组合根可在创建 Framework 前注册 RelayLimits 和 ICommandRelayAuthorization。默认允许已认证玩家在预算内单播与广播，声明本身只参与接收资格匹配。服务器始终从 Ready 认证连接取发送者身份，并使用接收连接自己的 SessionId 构造投递信封。

UDP 队列水位在新增 Relay 之前检查，最后一次提交和可靠窗口内数据有额外余量。历史业务可继续使用其现有发送入口。TLS 使用既有有界出站队列。具体数字构成准入预算，进程峰值内存仍需实测。

## 协议兼容与安全限制

| 组合 | 行为 |
| --- | --- |
| 新客户端与新服务端 | 协商 Relay 并同步插件能力 |
| 旧客户端与新服务端 | 使用既有业务协议，排除 Relay |
| 新客户端与旧服务端 | 使用既有业务协议，SendRelay 返回 UnsupportedRelay |
| 旧客户端与旧服务端 | 使用既有协议 |

外层使用 DataContractJsonSerializer，CommandRoute 是可选新增字段，缺省时沿用 Server 模式。新增控制消息只在 Relay 协商后发送。内层 protobuf Command 定义保持原格式。回归包含冻结的旧外层报文类读写测试。

服务端既有固定 LiteNetLib 收包保护继续生效，包含分片重组与事件队列水位。客户端现已接入相同的共享收包预算，在原始 UDP 回调进入重组前检查分片与合包，并限制不完整分片组和事件队列。两个固定 DLL 分别校验哈希与反射布局。64 KiB 校验发生在重组后，收包预算负责重组前保护。队列水位仍允许最后一次重组与可靠窗口带来的短时额外占用。UDP/ClientKey 的既有身份与链路安全边界也继续适用。

本次测试覆盖组件和本机网络，游戏内联机、长期洪峰、慢接收者压力和真实新旧发行版交叉部署仍需独立验收。Common 的协议改动存在客户端、服务端两个子模块工作树，发布时需提交共享改动并统一精确 gitlink。

## 客户端收包保护与示例验证

共享实现为 Common/Connections/PinnedUdpReceiveBudget.cs，客户端通过 PinnedClientUdpReceiveGuard 安装，服务端通过既有同步入口复用。客户端轮询异常或收包保护异常由当前轮询工作线程关闭本次传输，后续重连先确认旧线程退出并重置保护状态。

实际 net35 DLL 的客户端测试已验证有效 1 MiB 可靠载荷、直接及合包异常分片、未完成分片组预算、512 事件水位、32 MiB 载荷水位、排空恢复、轮询故障和再次重连。服务端固定 netstandard DLL 的既有分片与队列测试通过。两端共享预算内容保持一致。

可编译独立插件见 [Command Relay 示例](../Samples/CommandRelay/README.md)。示例编译通过，展示当前 ClientExtensionModule、声明生命周期、单播与广播、异步路由结果和主线程接收。
