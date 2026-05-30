# Phinix Rework — 性能、并发与稳定性审计报告

> 审计日期：2026-05-31
> 审计范围：完整项目（Client/Source, Extensions/Trade, Extensions/Chat, Extensions/LegacyAdapter, Server, Common）
> 原始问题：原版 Phinix "收到交易时游戏卡死，必须断开重连"

---

## 目录

1. [总览](#1-总览)
2. [线程安全审计（主线程违规）](#2-线程安全审计)
3. [锁与同步审计](#3-锁与同步审计)
4. [错误处理审计](#4-错误处理审计)
5. [性能与内存分配审计](#5-性能与内存分配审计)
6. [修复优先级矩阵](#6-修复优先级矩阵)
7. [各问题详细修复方案](#7-各问题详细修复方案)
8. [架构优化建议](#8-架构优化建议)

---

## 1. 总览

### 项目架构简述

Phinix Rework 基于以下并发模型：

| 线程 | 职责 |
|------|------|
| **Unity 主线程** | RimWorld GUI 渲染、Verse API、`Client.Update()` / `DrainPendingActions()` |
| **LiteNetLib 轮询线程** | 网络数据包接收、反序列化、事件触发 |
| **LiteNetLib 探测线程** | `NetClient` 中的 NAT 探测 |

所有网络数据包处理器（通过 `NetCommon.packetHandlerCallbackWrapper`）在**LiteNetLib 轮询线程**上运行。网络线程到主线程的同步通过 `ClientMainThreadDispatcher.Enqueue()` 完成，每帧在 `Root.Update` 的 Harmony 后置补丁中排空。

### 审计发现统计

| 类别 | Critical | High | Medium | Low | 合计 |
|------|----------|------|--------|-----|------|
| 线程安全 | 6 | 1 | 2 | 0 | **9** |
| 锁/同步 | 2 (正确性) | 3 | 2 | 4 | **11** |
| 错误处理 | 0 | 5 | 11 | 10 | **26** |
| 性能/分配 | 5 | 7 | 10 | 10 | **32** |
| **总计** | **13** | **16** | **25** | **24** | **78** |

---

## 2. 线程安全审计

> 核心问题：**代码在网络轮询线程上运行，但调用了必须在 Unity 主线程上使用的 API。**

### 关键违规

#### C-1: Find.WindowStack.Add 在网络线程调用 — 认证失败

- **文件**: [Client/Source/Client.cs:166](Client/Source/Client.cs#L166)
- **调用链**: `ClientAuthenticator` 从轮询线程触发 `OnAuthenticationFailure` → `Client.cs` 匿名处理器 → `Find.WindowStack.Add(new Dialog_MessageBox(...))`
- **严重程度**: 🔴 Critical

#### C-2: Find.WindowStack.Add 在网络线程调用 — 登录失败

- **文件**: [Client/Source/Client.cs:181](Client/Source/Client.cs#L181)
- **调用链**: 同上模式，通过 `OnLoginFailure` 事件
- **严重程度**: 🔴 Critical

#### C-3: Find.WindowStack.Add 在网络线程调用 — 凭证请求

- **文件**: [Client/Source/Client.cs:485](Client/Source/Client.cs#L485)
- **调用链**: `ClientAuthenticator` 在握手期间从轮询线程调用回调 → `Find.WindowStack.Add(new CredentialsWindow { ... })`
- **严重程度**: 🔴 Critical

#### C-4: Find.WindowStack.Add 在后台线程调用 — 连接失败

- **文件**: [Client/Source/Client.cs:376](Client/Source/Client.cs#L376)
- **调用链**: `SettingsWindow` 通过 `new Thread(() => Client.Instance.Connect(...)).Start()` 创建新线程 → catch 块调用 `Find.WindowStack.Add`
- **严重程度**: 🔴 Critical

#### C-5: thing.Destroy() 在网络线程调用

- **文件**: [Extensions/Trade/Client/TradeWindow.cs:385](Extensions/Trade/Client/TradeWindow.cs#L385)
- **调用链**: `BuiltInTradeClientExtension.HandleIncomingCommand` (轮询线程) → `OnTradeUpdateSuccess` 事件 → `TradeWindow.OnTradeUpdated` → `thing.Destroy()`
- **风险**: `Thing.Destroy()` 从游戏世界中移除东西、从地图移除、Dispose — **与原版卡死同根同源**
- **严重程度**: 🔴 Critical

#### C-6: GenSpawn.Spawn() 在网络线程调用

- **文件**: [Extensions/Trade/Client/TradeWindow.cs:398](Extensions/Trade/Client/TradeWindow.cs#L398)
- **调用链**: 同 C-5，在 else 分支（服务器拒绝更新时）
- **风险**: 在多地图数据结构上执行变异操作
- **严重程度**: 🔴 Critical

#### C-7: Find.Maps / listerThings / haulDestinationManager 在网络线程访问

- **文件**: [Extensions/Trade/Client/TradeWindow.cs:400](Extensions/Trade/Client/TradeWindow.cs#L400) (`refreshAvailableItems()`)
- **调用链**: 同 C-5 的 else 分支
- **严重程度**: 🟠 High

#### C-8: ThingMaker.MakeThing 在网络线程调用 — onTradeCompleted

- **文件**: [Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs:135-138](Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L135-L138)
- **风险**: `ConvertThingFromSnapshotOrUnknown` 在 **dispatcher.Enqueue 之前** 调用，通过 `DefDatabase<ThingDef>.AllDefs.Single()` + `ThingMaker.MakeThing`
- **严重程度**: 🟡 Medium

#### C-9: ThingMaker.MakeThing 在网络线程调用 — onTradeCancelled

- **文件**: [Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs:166-169](Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L166-L169)
- **风险**: 同 C-8
- **严重程度**: 🟡 Medium

### 已正确调度的正面例子 ✅

| 文件 | 事件 | 做法 |
|------|------|------|
| [PhinixDefaultTradeBehaviour.cs:87-94](Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L87-L94) | onTradeCreationSuccess | `dispatcher.Enqueue(() => { Find.LetterStack.ReceiveLetter(...); })` |
| [PhinixDefaultTradeBehaviour.cs:115-118](Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L115-L118) | onTradeCreationFailure | `dispatcher.Enqueue(() => windowService.Open(...))` |
| [PhinixDefaultTradeBehaviour.cs:140-149](Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L140-L149) | onTradeCompleted | DropPods + ReceiveLetter 在 Enqueue 内 |
| [PhinixDefaultTradeBehaviour.cs:171-179](Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L171-L179) | onTradeCancelled | 同上 |
| [ClientSoundService.cs:15](Client/Source/Framework/ClientSoundService.cs#L15) | 音效 | 通过锁队列 + 主线程排空 |

---

## 3. 锁与同步审计

### 关键问题

#### L-1 (HIGH): TradeList.cs 锁错了对象

- **文件**: [Extensions/Trade/Client/TradeList.cs:230](Extensions/Trade/Client/TradeList.cs#L230)
- **问题**: `onUserDisplayNameChangedHandler` 中使用了 `lock (trades)`（锁在 List 实例上），而其他所有方法都使用 `lock (tradesLock)`（专用锁对象）
- **影响**: 两者使用不同的 Monitor 对象 → **零互斥** → 两个线程可以同时修改 `trades` 列表 → 数据竞争、列表损坏
- **严重程度**: 🔴 High

#### L-2 (HIGH): ClientAuthenticator.cs 锁错了对象

- **文件**: [Common/Authentication/ClientAuthenticator.cs:317](Common/Authentication/ClientAuthenticator.cs#L317)
- **问题**: `helloPacketHandler` 中 `lock (credentialStore)` 锁在 Protobuf 对象上，而其他所有地方都使用 `lock (credentialStoreLock)`
- **影响**: Protobuf 对象被并发读写 → 凭证数据损坏
- **严重程度**: 🔴 High

#### L-3 (HIGH): 跨线程不同步的 bool?

- **文件**: [Extensions/Trade/Client/TradeWindow.cs:93,202,362,410](Extensions/Trade/Client/TradeWindow.cs#L93)
- **问题**: `bool? pendingAccepted` 被 UI 线程 (`DoWindowContents`) 和网络线程 (`OnTradeUpdated`, `sendTradeStatusUpdate`) 同时访问，**无任何同步**
- **风险**: `Nullable<bool>` 非原子类型，可能撕裂读取
- **严重程度**: 🔴 High

#### L-4 (MEDIUM): 逆序锁获取（休眠死锁）

- **文件**: [Server/UserManagement/ServerUserManager.cs:108-130 vs 140-153](Server/UserManagement/ServerUserManager.cs#L108)
- **问题**: 路径 A: `connectedUsersLock` → `userStoreLock`；路径 B: `userStoreLock` → `connectedUsersLock`
- **当前状态**: 两条路径都在同一线程 → 可重入，不触发
- **风险**: 未来任何跨线程调用都会导致 ABBA 死锁
- **严重程度**: 🟠 Medium

#### L-5 (MEDIUM): 持有锁时触发事件

- **文件**: [Client/Source/Framework/PhinixFrameworkClient.cs:486](Client/Source/Framework/PhinixFrameworkClient.cs#L486)
- **问题**: `OnDisplayMessageReceived?.Invoke(...)` 在持有 `displayMessagesLock` 时调用
- **风险**: 若订阅者获取其他锁 → 潜在死锁
- **严重程度**: 🟠 Medium

#### L-6 (MEDIUM): 持有锁时触发事件

- **文件**: [Common/UserManagement/ClientUserManager.cs:285-311](Common/UserManagement/ClientUserManager.cs#L285-L311)
- **问题**: `OnUserCreated`/`OnUserLoggedIn`/`OnUserLoggedOut`/`OnUserDisplayNameChanged` 在持有 `userStoreLock` 时触发
- **严重程度**: 🟠 Medium

#### L-7 (LOW): 多余的嵌套锁

- **文件**: [Server/UserManagement/ServerUserManager.cs:251](Server/UserManagement/ServerUserManager.cs#L251) 和 [Server/Authentication/ServerAuthenticator.cs:234](Server/Authentication/ServerAuthenticator.cs#L234)
- **问题**: 同一方法内对同一锁对象调用两次 `lock()` — 功能无害，但代码异味
- **严重程度**: ⚪ Low

#### L-8 (LOW): DisconnectPeer 在持有锁时调用

- **文件**: [Common/Connections/NetClient.cs:341-351](Common/Connections/NetClient.cs#L341-L351)
- **问题**: `clearProbePeers` 在 `lock (probePeersLock)` 内调用 `DisconnectPeer()`
- **风险**: 未来 LiteNetLib 若改变回调语义 → 潜在死锁
- **严重程度**: ⚪ Low

#### L-9 (LOW): tradeUpdated 非 volatile

- **文件**: [Extensions/Trade/Client/TradeWindow.cs:62](Extensions/Trade/Client/TradeWindow.cs#L62)
- **问题**: 跨线程脏标志未标记 volatile
- **影响**: 弱内存模型下最多延迟一帧
- **严重程度**: ⚪ Low

#### L-10 (LOW): clearMessages 非 volatile

- **文件**: [Extensions/Chat/Client/ChatMessageList.cs:36](Extensions/Chat/Client/ChatMessageList.cs#L36)
- **问题**: 同 L-9 模式
- **严重程度**: ⚪ Low

---

## 4. 错误处理审计

### 关键问题

#### E-1 (HIGH): 服务器启动时无异常处理 — 配置文件损坏

- **文件**: [Server/Config.cs:132-157](Server/Config.cs#L132-L157) 和 [Server/Server.cs:41-42](Server/Server.cs#L41-L42)
- **问题**: 损坏的 `server.conf` 导致服务器以未处理的 SerializationException 崩溃
- **严重程度**: 🟠 High

#### E-2 (HIGH): 服务器启动时无异常处理 — 凭证/用户数据库损坏

- **文件**: [Server/Server.cs:76-77](Server/Server.cs#L76-L77)
- **问题**: `Authenticator.Load()` / `UserManager.Load()` 解析损坏的 Protobuf 文件 → 崩溃
- **严重程度**: 🟠 High

#### E-3 (HIGH): Config.GetConfig<T>() 静默吞没异常

- **文件**: [Server/Config.cs:293-296](Server/Config.cs#L293-L296)
- **问题**: `catch {}` 无日志、无回退提示 → 管理员不知道配置被丢弃
- **严重程度**: 🟠 High

#### E-4 (HIGH): BuiltInChatServerExtension 静默吞没异常

- **文件**: [Extensions/Chat/Server/BuiltInChatServerExtension.cs:83-86](Extensions/Chat/Server/BuiltInChatServerExtension.cs#L83-L86)
- **问题**: Protobuf 解析失败 → 无日志、返回"已处理"
- **严重程度**: 🟠 High

#### E-5 (MEDIUM): Client.Connect() 裸 catch

- **文件**: [Client/Source/Client.cs:371-377](Client/Source/Client.cs#L371-L377)
- **问题**: `catch {}` 捕获一切（包括 OutOfMemoryException、StackOverflowException）
- **严重程度**: 🟠 Medium

#### E-6 (MEDIUM): ProtobufPacketHelper.ValidatePacket 无异常处理

- **文件**: [Common/Utils/ProtobufPacketHelper.cs:32](Common/Utils/ProtobufPacketHelper.cs#L32)
- **问题**: `Any.Parser.ParseFrom()` 可能抛异常，但调用方只在顶层包装，导致错误信息误导
- **严重程度**: 🟠 Medium

#### E-7 (MEDIUM): int.Parse 无 TryParse

- **文件**: [Client/Source/GUI/Windows and panels/SettingsWindow.cs:224](Client/Source/GUI/Windows and panels/SettingsWindow.cs#L224)
- **问题**: 空端口字段 → FormatException → 客户端崩溃
- **严重程度**: 🟠 Medium

#### E-8 (MEDIUM): 布局容器除零

- **文件**: [Client/Source/GUI/Containers/VerticalFlexContainer.cs:57](Client/Source/GUI/Containers/VerticalFlexContainer.cs#L57) 和 [HorizontalFlexContainer.cs:57](Client/Source/GUI/Containers/HorizontalFlexContainer.cs#L57)
- **问题**: 所有子项为固定尺寸时 `fluidItems == 0` → DivideByZeroException
- **严重程度**: 🟠 Medium

#### E-9 (MEDIUM): SettingsWindow 静态字段初始化依赖 Client.Instance

- **文件**: [Client/Source/GUI/Windows and panels/SettingsWindow.cs:28-29](Client/Source/GUI/Windows and panels/SettingsWindow.cs#L28-L29)
- **问题**: 类型初始化时 `Client.Instance` 可能为 null
- **严重程度**: 🟠 Medium

#### E-10 (MEDIUM): ServerUserManager 字典键竞争

- **文件**: [Server/UserManagement/ServerUserManager.cs:636-637](Server/UserManagement/ServerUserManager.cs#L636-L637)
- **问题**: `connectedUsers[connectionId]` 在检查 `IsLoggedIn` 后可能被移除（时间窗口竞争）
- **严重程度**: 🟠 Medium

#### E-11 (LOW): Client.ResolveModPackageId 静默吞没异常

- **文件**: [Client/Source/Client.cs:436-440](Client/Source/Client.cs#L436-L440)
- **问题**: `catch {}` 在 Assembly.Location 可能抛异常时 → 静默返回 null
- **严重程度**: ⚪ Low

#### E-12 (LOW): ServerAuthenticator.TryGetConnectionId 静默吞没异常

- **文件**: [Server/Authentication/ServerAuthenticator.cs:141-145](Server/Authentication/ServerAuthenticator.cs#L141-L145)
- **问题**: `InvalidOperationException` 在重复 session ID 时被静默吞没
- **严重程度**: ⚪ Low

#### E-13 (LOW): ServerUserManager.TryGetConnection 静默吞没异常

- **文件**: [Server/UserManagement/ServerUserManager.cs:319-323](Server/UserManagement/ServerUserManager.cs#L319-L323)
- **问题**: 同 E-12，重复 UUID-连接映射被静默忽略
- **严重程度**: ⚪ Low

#### E-14 (LOW): UserManager.TryGetUserUuid 静默吞没异常

- **文件**: [Common/UserManagement/UserManager.cs:55-57](Common/UserManagement/UserManager.cs#L55-L57)
- **问题**: 多个匹配 -> 数据损坏，但 `InvalidOperationException` 被静默吞没
- **严重程度**: ⚪ Low

#### E-15 (LOW): SettingsWindow 静态字段陈旧

- **文件**: [Client/Source/GUI/Windows and panels/SettingsWindow.cs:28-29](Client/Source/GUI/Windows and panels/SettingsWindow.cs#L28-L29)
- **问题**: 静态 `serverAddress`/`serverPortString` 在所有实例间共享，可能与 mod 设置不同步
- **严重程度**: ⚪ Low

#### E-16 (LOW): NetServer 关闭时丢弃飞行中数据包

- **文件**: [Common/Connections/NetServer.cs:126-142](Common/Connections/NetServer.cs#L126-L142)
- **问题**: 轮询线程 1 秒超时后被放弃为后台线程，未处理的数据包丢失
- **严重程度**: ⚪ Low

#### E-17 (LOW): NetClient 探测线程清理竞争

- **文件**: [Common/Connections/NetClient.cs:249-263](Common/Connections/NetClient.cs#L249-L263)
- **问题**: `clearProbePeers()` 在 `clientNetManager.Stop()` 之后调用 → 可能的 DisconnectPeer 异常
- **严重程度**: ⚪ Low

#### E-18 (LOW): 服务端事件订阅在关闭时不取消

- **文件**: [Server/Server.cs:71-73](Server/Server.cs#L71-L73)
- **问题**: `Authenticator.OnLogEntry += ...` 等永远不移除
- **严重程度**: ⚪ Low

#### E-19 (LOW): Client 构造函数 lambda 事件永远不移除

- **文件**: [Client/Source/Client.cs:148-221](Client/Source/Client.cs#L148-L221)
- **问题**: 单例生命周期内无害，但如果热重载则会累积
- **严重程度**: ⚪ Low

#### E-20 (LOW): CredentialsWindow 关闭后凭证残留内存

- **文件**: [Client/Source/GUI/Windows and panels/CredentialsWindow.cs:31-32](Client/Source/GUI/Windows and panels/CredentialsWindow.cs#L31-L32)
- **问题**: 用户名/密码在窗口关闭后保留在实例字段中
- **严重程度**: ⚪ Low

---

## 5. 性能与内存分配审计

### 估算影响

- **UI 线程 GC 压力（每帧）**: 50 条可见消息的聊天 UI 约分配 10-15 KB/帧；交易 UI 约 2-5 KB/帧。在 60fps 下 ≈ 600-1200 KB/s
- **网络线程 GC 压力**: 每次交易同步（10 个交易）≈ 50-100 KB 从深层克隆和 LINQ 链
- **GC 暂停**: RimWorld 运行在 .NET Framework 4.7.2 上，使用旧 GC → Gen0 回收导致 1-5ms 的可见帧卡顿

### 关键问题

#### P-1 (CRITICAL): StackedThings.Count 每项每帧分配 LINQ

- **文件**: [Extensions/Trade/Client/StackedThings.cs:13](Extensions/Trade/Client/StackedThings.cs#L13)
- **问题**: `public int Count => Things.Sum(thing => thing.stackCount);` — 每次访问都分配 lambda + 枚举器
- **调用频率**: `drawItemStackList` 中每个物品堆每帧多次访问 → 50 个堆 = 50+ 个 lambda/帧
- **严重程度**: 🔴 Critical

#### P-2 (CRITICAL): ChatMessageList.drawChatMessage 每消息每帧分配多个字符串 + GUIContent

- **文件**: [Extensions/Chat/Client/ChatMessageList.cs:193-207](Extensions/Chat/Client/ChatMessageList.cs#L193-L207)
- **问题**: 每个可见消息每帧：2 个 `string.Format` + 2 个 `new GUIContent()` + 3-4 个 `StripRichText`（每个 4 个 Regex.Replace）
- **影响**: 50 条消息 = 250+ 个分配/帧
- **严重程度**: 🔴 Critical

#### P-3 (CRITICAL): tradeUpdated 路径遍历 DefDatabase 全表

- **文件**: [Extensions/Trade/Client/TradeWindow.cs:158-159](Extensions/Trade/Client/TradeWindow.cs#L158-L159)
- **问题**: `TradeItemConverter.ConvertThingFromSnapshotOrUnknown` 调用 `DefDatabase<ThingDef>.AllDefs.Single(...)` — 每次更新扫描所有 def
- **严重程度**: 🔴 Critical

#### P-4 (CRITICAL): PhinixFrameworkTradeClientRepository.Clone() 深层分配

- **文件**: [Extensions/Trade/Client/PhinixFrameworkTradeClientRepository.cs:79-104](Extensions/Trade/Client/PhinixFrameworkTradeClientRepository.cs#L79-L104)
- **问题**: 每次读取都触发带有 3 级 LINQ Select 的深层递归克隆（参与者 → 物品 → 元数据）
- **影响**: 10 个交易 x 2 参与者 x 20 物品 x 3 元数据条目 = 600+ 次分配
- **严重程度**: 🔴 Critical

#### P-5 (CRITICAL): ChatMessageList.recalculateMessageRects 重新计算全部

- **文件**: [Extensions/Chat/Client/ChatMessageList.cs:158-189](Extensions/Chat/Client/ChatMessageList.cs#L158-L189)
- **问题**: 每条新消息到达时重新计算所有消息的矩形（string.Format + StripRichText + Text.CalcHeight）
- **严重程度**: 🔴 Critical

#### P-6 (HIGH): TradeList.Draw 每帧 LINQ .Any()

- **文件**: [Extensions/Trade/Client/TradeList.cs:87](Extensions/Trade/Client/TradeList.cs#L87)
- **问题**: `if (!filteredTrades.Any())` — 每帧分配枚举器；应使用 `filteredTrades.Count == 0`
- **严重程度**: 🟠 High

#### P-7 (HIGH): TryGet* 中 new List<>() 空集合回退

- **文件**: [Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs:70,89,108,127](Extensions/Trade/Client/PhinixFrameworkTradeClientService.cs#L70)
- **问题**: `Participants ?? new List<>()` — `??` 总是求值，即使 Participants 不为 null 也分配 List
- **严重程度**: 🟠 High

#### P-8 (HIGH): TextHelper.StripRichText 4 次 Regex.Replace

- **文件**: [Common/Utils/TextHelper.cs:34-42](Common/Utils/TextHelper.cs#L34-L42)
- **问题**: 每个 `StripRichText` 调用运行 4 次 `Regex.Replace`（每次分配新字符串）
- **调用频率**: 聊天中每个显示名称、每条消息、每个交易标签
- **严重程度**: 🟠 High

#### P-9 (HIGH): Vertical/HorizontalFlexContainer 每帧 LINQ

- **文件**: [Client/Source/GUI/Containers/VerticalFlexContainer.cs:50,55](Client/Source/GUI/Containers/VerticalFlexContainer.cs#L50-L55) 和 [HorizontalFlexContainer.cs:51,56](Client/Source/GUI/Containers/HorizontalFlexContainer.cs#L51-L56)
- **问题**: `Contents.Where().Sum()` + `Contents.Count()` — 每帧 2 个 LINQ 枚举
- **严重程度**: 🟠 High

#### P-10 (HIGH): TypeUrl 每次构造新建 Regex

- **文件**: [Common/Utils/TypeUrl.cs:23](Common/Utils/TypeUrl.cs#L23)
- **问题**: `new Regex(...)` 每次 TypeUrl 解析 → 网络线程每次数据包都分配
- **严重程度**: 🟠 High

#### P-11 (MEDIUM): TradeWindow.Update 按钮 .ToArray() 不必要

- **文件**: [Extensions/Trade/Client/TradeWindow.cs:240](Extensions/Trade/Client/TradeWindow.cs#L240)
- **问题**: `PopSelected().ToArray()` — `PopSelected()` 已经返回具体集合，不需要复制到数组
- **严重程度**: 🟡 Medium

#### P-12 (MEDIUM): TradeList 每行每帧字符串拼接

- **文件**: [Extensions/Trade/Client/TradeList.cs:129](Extensions/Trade/Client/TradeList.cs#L129)
- **问题**: 翻译键每行每帧通过 `+` 拼接
- **严重程度**: 🟡 Medium

#### P-13 (MEDIUM): TradeList 事件处理中 FindIndex lambda 闭包

- **文件**: [Extensions/Trade/Client/TradeList.cs:217,233,256](Extensions/Trade/Client/TradeList.cs#L217)
- **问题**: 每次 `FindIndex` 调用都分配闭包
- **严重程度**: 🟡 Medium

#### P-14 (MEDIUM): PhinixFrameworkTradeClientRepository.GetAll() .OrderBy()

- **文件**: [Extensions/Trade/Client/PhinixFrameworkTradeClientRepository.cs:60](Extensions/Trade/Client/PhinixFrameworkTradeClientRepository.cs#L60)
- **问题**: `.OrderBy()` 为排序分配临时缓冲区
- **严重程度**: 🟡 Medium

#### P-15 (MEDIUM): TabsContainer 每帧 .Select().ToList() + .Single()

- **文件**: [Client/Source/GUI/Containers/TabsContainer.cs:72,77](Client/Source/GUI/Containers/TabsContainer.cs#L72-L77)
- **问题**: 每帧新建 List<TabRecord> + 枚举器
- **严重程度**: 🟡 Medium

#### P-16 (MEDIUM): StackedThings 属性 .First() 分配枚举器

- **文件**: [Extensions/Trade/Client/StackedThings.cs:15-18](Extensions/Trade/Client/StackedThings.cs#L15-L18)
- **问题**: `Label`, `ThingDef` 等使用 `.First()` — 应使用 `Things[0]`
- **严重程度**: 🟡 Medium

#### P-17 (MEDIUM): PhinixDefaultTradeBehaviour 每次事件新建 HashSet

- **文件**: [Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs:222](Extensions/Trade/Client/PhinixDefaultTradeBehaviour.cs#L222)
- **问题**: `new HashSet<string>(settingsContext.BlockedUsers)` 每次调用
- **严重程度**: 🟡 Medium

#### P-18 (MEDIUM): ChatUiHostContext.BlockedUsers 每次访问新建 HashSet

- **文件**: [Extensions/Chat/Client/ChatUiHostContext.cs:45](Extensions/Chat/Client/ChatUiHostContext.cs#L45)
- **问题**: 每次属性读取都 `new HashSet<string>()`
- **严重程度**: 🟡 Medium

#### P-19 (MEDIUM): SettingsWindow 每次点击新建 Thread

- **文件**: [Client/Source/GUI/Windows and panels/SettingsWindow.cs:228](Client/Source/GUI/Windows and panels/SettingsWindow.cs#L228)
- **问题**: 应使用 `ThreadPool.QueueUserWorkItem`
- **严重程度**: 🟡 Medium

#### P-20 (MEDIUM): PhinixFrameworkChatService 每条聊天消息新建 HashSet

- **文件**: [Extensions/Chat/Client/PhinixFrameworkChatService.cs:104,121,137](Extensions/Chat/Client/PhinixFrameworkChatService.cs#L104)
- **问题**: 每条消息分配 `new HashSet<string>(excludedUuids)`
- **严重程度**: 🟡 Medium

#### P-21 至 P-30 (LOW): 其余低严重性问题

详见审计原始报告。包括：字符串插值、重复字典查找、冗余 .Any()、Vector2/Rect 分配、扩展管理器 LINQ、Regex 缓存首次缺失等。

---

## 6. 修复优先级矩阵

### 第一优先级（立即修复 — 玩家可感知的卡死/崩溃）

| # | 问题 | 文件 | 类别 |
|---|------|------|------|
| 1 | Find.WindowStack.Add 离线调用 (4处) | Client.cs:166,181,376,485 | 线程安全 🔴 |
| 2 | thing.Destroy/GenSpawn.Spawn 离线调用 | TradeWindow.cs:385,398 | 线程安全 🔴 |
| 3 | TradeList 锁错对象 | TradeList.cs:230 | 锁 🔴 |
| 4 | ClientAuthenticator 锁错对象 | ClientAuthenticator.cs:317 | 锁 🔴 |
| 5 | pendingAccepted 跨线程无同步 | TradeWindow.cs:93 | 锁 🔴 |

### 第二优先级（尽快修复 — 稳定性与数据完整性）

| # | 问题 | 文件 | 类别 |
|---|------|------|------|
| 6 | 服务器启动文件损坏崩溃 (3处) | Server.cs + Config.cs | 错误处理 🟠 |
| 7 | 静默配置丢弃 | Config.cs:293-296 | 错误处理 🟠 |
| 8 | 聊天消息静默丢弃 | BuiltInChatServerExtension.cs:83-86 | 错误处理 🟠 |
| 9 | TradeWindow Find.Maps 离线访问 | TradeWindow.cs:400 | 线程安全 🟠 |
| 10 | 持有锁时触发事件 (2处) | PhinixFrameworkClient.cs, ClientUserManager.cs | 锁 🟠 |

### 第三优先级（下一轮迭代 — 性能与体验）

| # | 问题 | 文件 | 类别 |
|---|------|------|------|
| 11 | StackedThings.Count LINQ/帧 | StackedThings.cs:13 | 性能 🔴 |
| 12 | ChatMessageList 每帧分配 | ChatMessageList.cs:193-207 | 性能 🔴 |
| 13 | Repository Clone 深度分配 | PhinixFrameworkTradeClientRepository.cs:79-104 | 性能 🔴 |
| 14 | DefDatabase 全表扫描 | TradeWindow.cs:158-159 | 性能 🔴 |
| 15 | LINQ Any/Sum 热路径 (多处) | 多个文件 | 性能 🟠 |

---

## 7. 各问题详细修复方案

### 7.1 线程安全修复

#### 修复 C-1 至 C-4（Client.cs 离线 WindowStack）

```csharp
// 修复前 (Client.cs:166):
authenticator.OnAuthenticationFailure += (sender, args) =>
{
    Find.WindowStack.Add(new Dialog_MessageBox("..."));
};

// 修复后:
authenticator.OnAuthenticationFailure += (sender, args) =>
{
    mainThreadDispatcher.Enqueue(() =>
        Find.WindowStack.Add(new Dialog_MessageBox("...")));
};
```

在 Client 构造函数中搜索所有 `Find.WindowStack.Add` 调用并用 `mainThreadDispatcher.Enqueue` 包装。

#### 修复 C-5/C-6/C-7（TradeWindow.OnTradeUpdated）

TradeWindow 需要通过构造函数接收 `IClientMainThreadDispatcher`。最简洁的方式是修改 `ClientTradeUiHostContext` 或直接在 `TradeWindow` 构造函数中传递：

```csharp
// TradeWindow 构造函数新增参数
private readonly IClientMainThreadDispatcher dispatcher;

public TradeWindow(ClientTradeSnapshot trade, ITradeUiHostContext hostContext, 
                   IClientMainThreadDispatcher dispatcher)
{
    // ...
    this.dispatcher = dispatcher;
}

// OnTradeUpdated 修复:
private void OnTradeUpdated(object sender, TradeUpdateEventArgs args)
{
    // ... 前面的验证不变 ...
    
    // 更新交易数据（可留在网络线程）
    lock (updatedTradeLock)
    {
        updatedTrade = args.Trade;
        tradeUpdated = true;
    }
    
    // Thing 操作必须调度到主线程
    if (!string.IsNullOrEmpty(args.Token))
    {
        string token = args.Token;
        TradeFailureReason reason = args.FailureReason;
        dispatcher.Enqueue(() =>
        {
            lock (pendingItemStacksLock)
            {
                if (pendingItemStacks.TryGetValue(token, out PendingThings pending))
                {
                    if (reason == TradeFailureReason.None)
                    {
                        foreach (Thing thing in pending.Things)
                            if (!thing.Destroyed) thing.Destroy();
                        pendingItemStacks.Remove(token);
                    }
                    else
                    {
                        foreach (Thing thing in pending.Things)
                            GenSpawn.Spawn(thing, thing.Position, thing.Map, thing.Rotation, WipeMode.VanishOrMoveAside);
                        refreshAvailableItems(); // 现在安全了，在主线程
                    }
                }
            }
        });
    }
}
```

#### 修复 C-8/C-9（ThingMaker.MakeThing 离线）

将 `PhinixDefaultTradeBehaviour.onTradeCompleted` 和 `onTradeCancelled` 中的 Thing 构建移到 dispatcher 内部：

```csharp
// 修复前:
Thing[] verseItems = args.Items
    .Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown)
    .ToArray();
dispatcher.Enqueue(() => { /* 使用 verseItems */ });

// 修复后:
var itemsCopy = args.Items; // 捕获快照
dispatcher.Enqueue(() =>
{
    Thing[] verseItems = itemsCopy
        .Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown)
        .Where(thing => thing != null && thing.def != null && thing.def.defName != "UnknownItem")
        .ToArray();
    // ... DropPods + ReceiveLetter ...
});
```

### 7.2 锁修复

#### 修复 L-1（TradeList 锁对象）

```csharp
// TradeList.cs:230 — 将 lock (trades) 改为 lock (tradesLock)
private void onUserDisplayNameChangedHandler(object sender, UserDisplayNameChangedEventArgs args)
{
    lock (tradesLock)  // 之前是 lock (trades)
    {
        // ... 其余不变 ...
    }
}
```

#### 修复 L-2（ClientAuthenticator 锁对象）

```csharp
// ClientAuthenticator.cs:317 — 将 lock (credentialStore) 改为 lock (credentialStoreLock)
// 找到该代码块顶部的 lock(credentialStore) 并替换
```

#### 修复 L-3（pendingAccepted 同步）

```csharp
// TradeWindow.cs — 对 pendingAccepted 使用 Interlocked 或将所有访问包裹在 lock 中
// 最简单的选择：在 DoWindowContents 中使用 Monitor.TryEnter 从网络线程安全读取
// 或者添加一个专用的锁对象
private object pendingAcceptedLock = new object();
private bool? pendingAccepted;
```

### 7.3 错误处理修复

#### 修复 E-1/E-2（服务器启动时文件损坏）

```csharp
// Server.cs 启动时:
try
{
    Config.Load(CONFIG_FILE);
    Authenticator.Load();
    UserManager.Load();
}
catch (Exception ex)
{
    Verse.Log.Error($"服务器启动失败：{ex}");
    // 优雅退出或回退到默认值
}
```

#### 修复 E-3（Config 静默吞没）

将 `catch {}` 改为 `catch (Exception ex) { Verse.Log.Warning($"...: {ex.Message}"); }`

#### 修复 E-8（布局容器除零）

```csharp
// 在除法前检查 fluidItems > 0
if (fluidItems > 0)
{
    float heightPerFluid = remainingHeight / fluidItems;
    // ...
}
```

### 7.4 性能修复

#### 修复 P-1（StackedThings.Count）

```csharp
// 修复前:
public int Count => Things.Sum(thing => thing.stackCount);

// 修复后:
public int Count
{
    get
    {
        int total = 0;
        foreach (Thing thing in Things)
            total += thing.stackCount;
        return total;
    }
}
```

#### 修复 P-2（ChatMessageList GUIContent）

```csharp
// 修复前:
Vector2 timestampSize = Text.CurFontStyle.CalcSize(new GUIContent(timestamp));

// 修复后:
Vector2 timestampSize = Text.CalcSize(timestamp); // 使用字符串重载
```

#### 修复 P-4（Repository Clone）

替代方案：使用不可变数据结构，或锁定 + 返回引用而不克隆（调用方必须在锁外不修改）。

---

## 8. 架构优化建议

### 8.1 主线程调度器模式强化

当前 `dispatcher.Enqueue` 模式已正确实现但**执行不一致**。建议：

1. **强制约定**：所有 `Find.*`、`Verse.Thing`、`GenSpawn`、`Widgets` 调用必须在 dispatcher 的 Enqueue 回调内部或通过 `IClientWindowService`（内部使用 dispatcher）。
2. **运行时检测**（DEBUG 模式下）：使用 `Thread.CurrentThread.ManagedThreadId` 断言。在 `ClientMainThreadDispatcher` 中缓存主线程 ID，并在关键 API 上添加断言。
3. **为 TradeWindow 提供 dispatcher 引用**：通过 `ITradeUiHostContext` 接口暴露 `IClientMainThreadDispatcher`。

### 8.2 锁策略标准化

1. **始终使用专用的 `private readonly object` 锁对象**，永不锁在数据容器或 `this` 上。
2. **UI 线程代码路径**对所有共享状态访问使用 `Monitor.TryEnter`（从不阻塞）。
3. **永远不要持有锁时触发事件**——将事件数据复制到本地变量，释放锁，然后触发。
4. **记录锁获取顺序**——在所有锁对象上建立偏序关系，消除死锁风险。

### 8.3 内存分配减压方案

1. **热路径缓存**：为 `drawItemStackList` 和 `drawChatMessage` 中的每帧计算预分配缓冲区。
2. **Repository 优化**：在 `GetAll()` 期间，锁定 + 返回对内部快照列表的引用（而不是深层克隆），假设调用方在锁外不修改。或使用不可变集合（`ImmutableDictionary`）。
3. **正则表达式编译**：`TypeUrl` 正则表达式移到 `static readonly` 字段。
4. **LINQ 消除**：用手动 `for`/`foreach` 循环替换每帧的 LINQ。

### 8.4 服务器启动弹性

添加一个 `TryStart()` 方法，当文件损坏时回退到新文件并通知管理员，而不是直接崩溃：

```csharp
public static StartupResult TryStart()
{
    try
    {
        Config.Load(CONFIG_FILE);
    }
    catch (Exception ex)
    {
        Log.Error($"损坏的 server.conf: {ex.Message}。使用默认配置。");
        Config = new Config(); // 回退
    }
    
    try
    {
        Authenticator.Load();
    }
    catch (Exception ex)
    {
        Log.Error($"损坏的凭证数据库: {ex.Message}。创建新数据库。");
        Authenticator = new ServerAuthenticator(path, ...);
    }
    // ...
}
```

---

## 附录 A：原始审计报告提取

完整的每条审计的原始详细报告可在以下位置找到：

- 线程安全：[tools/a46be4f501a6d073b.output]
- 锁/同步：[tools/a5496ff15d3c7dc9e.output]
- 错误处理：[tools/a64628ec5cf7a3098.output]
- 性能/分配：[tools/a788fad34e546ddb5.output]

## 附录 B：原版 Phinix 卡死根因对比

| 原版 Phinix | Phinix Rework | 状态 |
|-------------|---------------|------|
| Find.LetterStack.ReceiveLetter 在网络线程 | ✅ 已修复 — 通过 dispatcher.Enqueue 调度 | 已解决 |
| Find.WindowStack.Add 在网络线程 (多处) | ❌ 仍存在 (4处) | **需要修复** |
| DropPodUtility 在网络线程 | ✅ 已修复 — 通过 dispatcher.Enqueue 调度 | 已解决 |
| thing.Destroy/GenSpawn.Spawn 在网络线程 | ❌ 仍存在 | **需要修复** |
| activeTradesLock 用阻塞 lock 竞争 UI 线程 | ✅ 已修复 — 使用 Monitor.TryEnter | 已解决 |

**结论**：Rework 版本在架构上大幅改善了原版的问题，但仍有 2 个类型的遗漏导致同样的卡死风险。修复工作量小（集中在 Client.cs 的 4 个 WindowStack 调用和 TradeWindow.cs 的 1 个事件处理器）。
