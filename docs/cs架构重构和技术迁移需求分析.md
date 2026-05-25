# Phinix C/S Extension 架构迁移需求分析与建议

## 0. 核心结论

当前架构需要从：

```text
Common 里放大量共享实现
Client/Server 共同依赖 Common 实现
Server 默认只做 relay
Client 负责主要业务逻辑
```

迁移为：

```text
Common 只保留 protobuf + interface + abstraction
Client 单独实现客户端行为
Server 单独实现服务端行为
Server 提供默认 relay
Server Extension 可以覆盖默认 relay，实现服务器业务逻辑
```

关键思想：

> 我们使用的是 protobuf，不是 RPC。protobuf 只描述消息格式，不应该把客户端和服务端的具体行为都塞进 common。

---

## 1. 当前问题分析

### 1.1 Common 已经变成“共享实现垃圾桶”

当前 common 里可能存在：

```text
- pipeline 具体实现
- util 工具类
- socket helper
- console helper
- logger helper
- server relay 逻辑
- client receive 逻辑
- protobuf message
- extension 相关逻辑
- runtime-specific dependency
```

这会导致 common 越来越像“万能层”。

问题是：

```text
Client 引 common
Server 也引 common
```

于是任何放进 common 的东西，都会同时污染客户端和服务端。

典型例子：

```xml
<package id="Pastel" version="1.3.1" targetFramework="net472" />
```

Pastel 本来只是服务端用来给 stdout 上色的库，但因为它进了 common/util，最后 Unity 客户端打包也会带上它。

这说明 common 的边界已经失控。

---

### 1.2 服务端不应该继续被 Unity/Mono 生态绑定

客户端因为 RimWorld/Unity 的原因，继续留在 Mono/net472 环境里是可以理解的。

但服务端不应该继续被这些限制绑定。

服务端应该迁移到现代 .NET，例如：

```text
net10.0
```

或者如果工具链暂时不方便，至少先迁移到：

```text
net8.0+
```

服务端不应该依赖：

```text
- UnityEngine
- RimWorld
- Verse
- Harmony
- Mono-specific behavior
- net472-only package
```

---

### 1.3 现有“三管道”设计本身可以保留，但实现位置要改

之前设计的：

```text
Message Pipeline
Control Pipeline
Item Pipeline
```

这个概念不用直接废掉。

问题不是“三管道”错了，而是：

> 不应该把三管道的具体实现放在 common 里让 C/S 共享。

应该改成：

```text
Common.Abstractions:
  IMessagePipeline
  IControlPipeline
  IItemPipeline
  IPipelineHandler
  IPipelineContext

Client:
  ClientMessagePipeline
  ClientControlPipeline
  ClientItemPipeline

Server:
  ServerMessagePipeline
  ServerControlPipeline
  ServerItemPipeline
```

也就是：

```text
Common 只定义“有什么”
Client/Server 各自决定“怎么做”
```

---

## 2. 新架构目标

### 2.1 Common 只保留契约

Common 应该收缩为两个核心包：

```text
Phinix.Common.Protocol
Phinix.Common.Abstractions
```

---

### 2.2 Phinix.Common.Protocol

只允许包含：

```text
- proto 文件
- protobuf generated types
- message DTO
- enum
- protocol constants
```

不允许包含：

```text
- socket 实现
- relay 实现
- logger 实现
- console helper
- file IO helper
- Unity/RimWorld/Verse 引用
- server-only NuGet
- client-only NuGet
```

Protocol 的职责只有一个：

> 定义 C/S 之间传什么消息。

---

### 2.3 Phinix.Common.Abstractions

只允许包含：

```text
- interface
- abstract contract
- pipeline contract
- extension contract
- handler contract
- context contract
```

例如：

```csharp
public interface IMessagePipeline
{
    Task<PipelineResult> ProcessAsync(
        IMessageEnvelope message,
        IPipelineContext context,
        CancellationToken cancellationToken = default);
}
```

```csharp
public interface IMessageHandler
{
    bool CanHandle(IMessageEnvelope message);

    Task<HandleResult> HandleAsync(
        IMessageEnvelope message,
        IMessageContext context,
        CancellationToken cancellationToken = default);
}
```

这里不要写具体实现。

---

## 3. Client / Server 职责划分

### 3.1 Client 负责什么

客户端负责：

```text
- Unity/RimWorld mod entry
- UI
- 游戏内行为
- 玩家操作
- 发出请求
- 接收服务端结果
- 客户端 extension 加载
- 客户端 pipeline 实现
```

客户端 extension 典型行为：

```text
玩家点击按钮
  ↓
构造请求消息
  ↓
发送给服务端
  ↓
收到服务端结果
  ↓
更新 UI / 游戏状态
```

客户端不应该负责服务器权威状态。

例如涉及服务器裁决的业务中，客户端不应该最终裁决：

```text
- 玩家资源是否足够
- 当前权威状态是多少
- 操作是否成功
- 公共状态是否合法
```

这些应该交给服务端 extension。

---

### 3.2 Server 负责什么

服务端负责：

```text
- 连接管理
- 鉴权
- 房间管理
- 默认 relay
- 服务端 pipeline 实现
- 服务端 extension 加载
- 服务端业务插件
- 数据持久化
- 定时任务
- 服务端主动广播
```

服务端默认行为是 relay，但不是只能 relay。

服务端应该支持：

```text
收到消息
  ↓
鉴权
  ↓
进入服务端 pipeline
  ↓
Server Extension Dispatch
  ↓
如果 extension 接管，则不走默认 relay
  ↓
如果 extension 不处理，则走默认 relay
```

---

## 4. 服务端默认转发与覆盖机制

朋友说的：

> 服务端提供默认转发，可以覆盖成自定义逻辑，客户端写发出和接收动作。

这个可以具体落成以下机制：

```text
Client Message
  ↓
Server Receive
  ↓
Decode
  ↓
Auth
  ↓
Build Context
  ↓
Server Extension Dispatch
  ↓
根据返回结果决定：
    NotHandled -> 默认 relay
    Handled    -> extension 已处理，不转发
    Blocked    -> 拦截，记录/丢弃/断开连接
```

建议定义：

```csharp
public enum HandleResult
{
    NotHandled,
    Handled,
    Blocked
}
```

含义：

```text
NotHandled:
  当前 extension 不处理这个消息，继续交给后续 handler 或默认 relay。

Handled:
  当前 extension 已经处理完这个消息，不再执行默认 relay。

Blocked:
  当前消息非法，终止处理，可以记录日志、丢弃消息或断开连接。
```

这个设计非常关键。

没有这个返回值，服务端插件只能“旁听”，不能真正“接管业务”。

---

## 5. 服务端 Extension 需求

服务端 extension 至少需要支持以下能力：

```text
1. 注册消息处理器
2. 接管指定类型消息
3. 阻止默认 relay
4. 主动给玩家发消息
5. 主动向房间广播消息
6. 维护服务端状态
7. 读取配置
8. 持久化数据
9. 注册定时任务
10. 使用服务端日志
```

建议接口：

```csharp
public interface IServerExtension
{
    string Id { get; }
    string Name { get; }
    Version Version { get; }

    void Configure(IServerExtensionBuilder builder);
}
```

```csharp
public interface IServerExtensionBuilder
{
    void HandleMessage<TMessage>(
        Func<TMessage, IServerMessageContext, CancellationToken, Task<HandleResult>> handler);

    void RegisterScheduledTask(
        string name,
        TimeSpan interval,
        Func<IServerServiceProvider, CancellationToken, Task> task);
}
```

```csharp
public interface IServerMessageContext
{
    string PlayerId { get; }
    string RoomId { get; }
    string ConnectionId { get; }

    Task SendToPlayerAsync(
        string playerId,
        object message,
        CancellationToken cancellationToken = default);

    Task BroadcastToRoomAsync(
        string roomId,
        object message,
        CancellationToken cancellationToken = default);

    Task BroadcastAllAsync(
        object message,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        string playerId,
        string reason,
        CancellationToken cancellationToken = default);
}
```

---

## 6. 客户端 Extension 需求

客户端 extension 负责：

```text
- UI
- 玩家交互
- 构造请求
- 发送消息
- 接收服务端消息
- 修改本地显示状态
- 和 RimWorld/Verse/Unity 交互
```

建议接口：

```csharp
public interface IClientExtension
{
    string Id { get; }
    string Name { get; }
    Version Version { get; }

    void Configure(IClientExtensionBuilder builder);
}
```

```csharp
public interface IClientExtensionBuilder
{
    void HandleMessage<TMessage>(
        Func<TMessage, IClientMessageContext, CancellationToken, Task> handler);

    void RegisterSendAction<TMessage>(
        string actionName,
        Func<IClientMessageContext, CancellationToken, Task<TMessage>> action);
}
```

客户端 extension 不应该：

```text
- 决定服务端权威状态
- 伪造最终处理结果
- 维护公共服全局权威数据
- 直接引用服务端实现
```

---

## 7. Docker 与服务端插件目录

服务端迁移到 modern .NET 后，应支持 Docker 部署。

建议服务端插件目录：

```text
/app/Mods
```

Docker volume：

```bash
-v ./Mods:/app/Mods
```

服务端启动时：

```text
扫描 /app/Mods
  ↓
加载 server extension dll
  ↓
注册 extension
  ↓
注册 handler / scheduler
```

暂时不要求热加载。

第一阶段只要求：

```text
放入插件
  ↓
重启 container
  ↓
插件生效
```

---

## 8. NuGet 依赖清理原则

### 8.1 server-only dependency

只能存在于 server project。

例如：

```text
- Pastel
- server logger
- database driver
- docker/config helper
- server scheduler
```

不能被 common/client 引用。

---

### 8.2 client-only dependency

只能存在于 client project。

例如：

```text
- UnityEngine
- RimWorld
- Verse
- Harmony
- UI helper
```

不能被 common/server 引用。

---

### 8.3 common dependency

必须非常克制。

只有真正双端都需要，且不绑定 runtime 的依赖才能进 common。

例如：

```text
- protobuf runtime
- 极少量纯协议相关 dependency
```

---

## 9. 迁移步骤建议

### Phase 1：先让当前版本可编译可运行

目标：

```text
- 主线能编译
- 服务端能跑
- 客户端能跑
- README 基本可信
- Docker 构建可以使用
```

不要一上来直接推翻全部代码。

---

### Phase 2：拆 common

把 common 拆成：

```text
Phinix.Common.Protocol
Phinix.Common.Abstractions
```

清理：

```text
- util
- concrete pipeline
- socket
- console
- logger
- file IO
- server relay
- Unity helper
```

这些实现分别迁移到：

```text
Phinix.Client.*
Phinix.Server.*
```

---

### Phase 3：服务端迁移 modern .NET

服务端目标：

```text
net10.0
```

如果当前工具链有问题，先落到：

```text
net8.0
```

但架构上要保证未来能升 net10。

---

### Phase 4：实现服务端 Extension Dispatch

这是关键阶段。

服务端 pipeline 必须支持：

```text
NotHandled -> 默认 relay
Handled    -> 不 relay
Blocked    -> 拦截
```

---

### Phase 5：实现 Mods 目录加载

服务端支持：

```text
/app/Mods
```

Docker 支持：

```text
./Mods:/app/Mods
```

重启加载插件。

---

### Phase 6：用示例插件验证

建议至少写一个 example extension：

```text
Echo Extension
  验证消息处理、主动回复、阻止 relay。
```

后续可以再补更复杂的示例插件，用于验证服务端状态、定时任务、持久化、广播等能力。

---

## 10. 对当前架构的具体建议

三管道设计不用废。

但是需要改变定位。

以前：

```text
三管道 = framework 里的具体共享实现
```

现在：

```text
三管道 = C/S 双端都遵守的抽象契约
```

具体来说：

```text
Common 里只放：
  IMessagePipeline
  IControlPipeline
  IItemPipeline

Client 里放：
  ClientMessagePipeline
  ClientControlPipeline
  ClientItemPipeline

Server 里放：
  ServerMessagePipeline
  ServerControlPipeline
  ServerItemPipeline
```

服务端三管道中要加入 extension dispatch。

客户端三管道中要加入 client extension dispatch。

这样可以同时保留已有架构成果，又满足“common 只留 interface，C/S 两边单独做实现”的方向。

---

## 11. 最终推荐架构

```text
/src
  /Phinix.Common.Protocol
    - proto
    - generated messages
    - protocol constants

  /Phinix.Common.Abstractions
    - interfaces
    - extension contracts
    - pipeline contracts
    - context contracts

  /Phinix.Client
    - RimWorld/Unity entry
    - client extension loader
    - client pipeline implementations
    - send actions
    - receive handlers

  /Phinix.Server
    - modern .NET server host
    - connection/session management
    - auth
    - room management
    - default relay
    - server extension loader
    - server pipeline implementations

  /Phinix.Server.Infrastructure
    - logging
    - persistence
    - scheduler
    - config
    - docker-specific runtime
```

---

## 12. 一句话需求总结

> 请将当前架构重构为“Common 只保留 protobuf 和 interface，Client/Server 各自实现行为，Server 提供默认 relay，但 Server Extension 可以接管消息并覆盖默认转发”的 C/S 双端扩展架构。服务端需要从 Mono/net472 迁移到 modern .NET，并支持 Docker 下通过 Mods 目录加载服务端插件。

---

## 13. 最重要的落地建议

不要把这次重构理解成：

```text
把代码拆得更细
```

而应该理解成：

```text
把 common 从“共享实现层”降级成“共享契约层”
```

朋友真正想表达的是：

```text
common 不应该拥有行为
common 只应该定义协议和能力边界
行为应该由 client/server 各自实现
server 默认 relay
server extension 可以覆盖 relay
client extension 负责发出和接收动作
```

这个方向工作量确实大，但它能解决后续最关键的问题：

```text
- 服务端脱离 Mono
- NuGet 不再污染
- Docker 部署更干净
- 服务端可以做权威业务
- C/S 边界清楚
- 未来维护更容易
```
