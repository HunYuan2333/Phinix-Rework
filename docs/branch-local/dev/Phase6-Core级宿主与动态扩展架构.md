# Phase 6: Core 级宿主与动态扩展架构

> 定义 Core / Host / Extension 四层边界和三条管道规则。

---

## 1. 完成状态总览

| Step | 内容 | 状态 |
|------|------|------|
| 1 | 文档边界定死 | ✅ |
| 2 | `IExtensionBuilder` + `IExtensionApiRegistry` | ✅ |
| 3 | Registry 主发现→module-first | ✅（非 module 发现降级为兼容路径） |
| 4 | Chat 协议常量迁出 `FrameworkProtocol` | ✅（→ `Extensions/Chat/Contracts`） |
| 4 | `BuiltInChat*HostServices` / `BuiltInTrade*HostServices` 删除 | ✅ |
| 5 | Host 侧业务装配消除 | 🔧 进行中 |
| 6 | Extension 代码从宿主项目边界拆开 | ⬜ |

---

## 2. 已知耦合（已解决项）

| 问题 | 状态 |
|------|------|
| `FrameworkProtocol` 中的 `BuiltInChat*` 常量 | ✅ 已迁至 `Chat/Contracts` |
| `IExtensionApiRegistry` 缺失 | ✅ 已实现 `RegisterApi/TryResolve/ResolveAll` |
| 管线绕过（`SendFrameworkPacket` 直连） | ✅ 出站走 `TryHandleOutgoingMessage/Command` |
| Client.csproj ProjectReference 官方扩展实现 | ✅ 已消除（构建期复制+运行时预加载） |

## 已知耦合（仍存在）

| 问题 | 状态 |
|------|------|
| `FrameworkPacket.MessageType` 命名残留 chat 语义 | ⬜ Phase 6 改名 |
| `message pipeline` 未改名为 `content pipeline` | ⬜ |
| `IClientMessageHandler` 同时处理文本输入和入站 message | ⬜ |
| 服务端 `ProcessIncomingItem` 无三阶段链 | ✅ 已补全（P0 完成） |
| Common 程序集中仍有端侧实现 | ⬜ 见"当前遗留问题与稳定性汇总" |
| Docker 发布链对官方扩展做显式构建/复制 | ⚠️ 过渡性妥协，见下文 2.1 |

### 2.1 Docker 发布链的过渡性妥协

当前 Docker 服务端镜像为了保证 **官方 Chat / Trade server extensions** 能稳定进入运行时扫描目录，采用了比理想状态更“显式”的发布链：

- `Dockerfile` 会显式 restore 官方 server extension 项目
- `Server.csproj` 会显式 build / 收集官方 Chat / Trade 的 server 与 contract 产物
- 镜像构建阶段会把这些 DLL 明确放入 `/app/Extensions`

这 **不是** Phase 6 的最终目标，而是当前发布链在 Docker/CI 环境下的 **工程性妥协**。它解决的是“镜像可运行、capability negotiation 不丢官方扩展”的现实问题，不代表宿主重新获得了 Chat/Trade 的业务编译期耦合。

边界说明：

- 允许：**构建期/打包期** 对官方扩展做显式 restore、build、copy
- 不允许：在宿主运行时代码里重新引入 Chat/Trade 业务 API、协议分支或专用 host service
- 目标仍然是：宿主在**运行时边界**只认识通用扩展目录与通用框架接口，不认识具体业务实现

后续如果继续推进彻底解耦，优先方向应是：

1. 让官方 server extensions 的产物发现与复制在发布链中更通用化，而不是继续扩写 Chat/Trade 专属步骤
2. 保持 `/app/Extensions` 作为唯一稳定运行时装载点
3. 在确认 Docker/CI 可稳定复现后，再考虑把当前显式复制逻辑收敛成更抽象的发布机制

---

## 3. 四层目标架构

### Core（仅保留通用能力）
- packet/envelope/payload 基础协议、capability negotiation
- extension discovery/registration/activation/shutdown
- content / command / item 三条 pipeline + 通用 metadata
- 基础 host context + API registry

### Host
- 启动网络与认证、创建 `ExtensionHostContext`
- 加载 extension assembly、启动 `PhinixFrameworkClient/Server`
- 提供：logging、clock、id generation、storage root、network send/broadcast adapter、user/session lookup
- **不提供**：业务 host service wrapper

### Extension
- protocol constants、payload contracts、handlers/renderers/codecs
- repository/store、service/facade、对外 API
- Chat 和 Trade 在架构地位上完全一致

### Projection
- 客户端 UI 不直接消费 transport 契约，消费 extension 投影

---

## 4. 三条 Pipeline 规则

| Pipeline | 职责 | 不负责 |
|----------|------|--------|
| Message | 用户可见内容、渲染器输出 | 状态同步、控制流 |
| Command | 请求/响应、状态同步、控制 | UI 渲染 |
| Item | 物品编解码、codec 调度 | Trade 状态机、聊天显示 |

**硬约束：** extension 不允许新增第四条 pipeline。所有网络接入必须复用这三条之一。

---

## 5. 明确推迟到 2.0

dependency graph、extension versioning、hot reload、sandboxing、remote extension download、complex plugin manifests、dependency solver、graph-based startup ordering、multi-version API coexistence

---

## 6. 相关文档

- [设计哲学.md](设计哲学.md)
- [框架化重构路线图.md](框架化重构路线图.md)
- [框架Protobuf协议设计.md](框架Protobuf协议设计.md)
