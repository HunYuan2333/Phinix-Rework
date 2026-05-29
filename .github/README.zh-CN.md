<h1 align="center">Phinix</h1>
<h4 align="center"><i>RimWorld 多人模组 — 聊天、交易与可扩展插件框架</i></h4>

[English](./README.md)

# 关于项目

Phinix 是一个通过独立服务器为 RimWorld 提供多人聊天和物品交易的模组。它在原有项目基础上进行了彻底重构，底层采用插件化框架——Chat 和 Trade 本身也是插件，不是内建特权模块。

- 游戏内殖民地间聊天（支持富文本，名字/消息可上色）
- 异步物品交易（无需双方同时在线）
- 独立服务器，支持认证与用户管理
- 可扩展插件系统，第三方 submod 与官方扩展地位平等

# 快速开始

## 客户端

1. 从 [Releases](https://github.com/HunYuan2333/Phinix-Rework/releases) 下载客户端发布包。
2. 解压到 RimWorld 的 `Mods` 目录。
3. 在游戏主菜单 → Mods 中启用 Phinix，重启游戏。
4. 进入存档，点底部工具栏的 Phinix 按钮，在 Settings 里填服务器地址和端口，点 Connect。

目前仅支持 RimWorld 1.6。

## 服务端（Docker，推荐）

```bash
docker pull hunyuan23333/phinix-rework:latest
docker run -d \
  --name phinix \
  --restart unless-stopped \
  -p 16200:16200/udp \
  -v ./server_data:/data \
  hunyuan23333/phinix-rework:latest

# 查看日志
docker logs -f phinix
```

也可以直接用 [docker-compose.yml](../docker-compose.yml)：

```bash
docker compose up -d --build
```

## 服务端（手动构建）

需要 .NET 10 SDK。服务端项目在 `Server/`，相关共享项目在 `Common/`。

```bash
dotnet build Server/Server.csproj -c Release -o out
dotnet out/PhinixServer.dll
```

配置文件为 `server.conf`（默认端口 16200，认证方式 ClientKey），控制台支持 `help`、`version`、`exit` 等命令。

# 已知问题

- **翻译红字**：网络回调线程上调用 RimWorld 的 `Translate()` 时偶发 `No active language` 错误。不影响功能——翻译失败时会 fallback 到 key 名本身（如 `Phinix_framework_systemDisplayName`）。这本质上是线程安全问题，将在后续版本中通过主线程封送机制修复。日志里的这类红字可以忽略。

# 架构

Phinix 采用分层、插件优先的架构：

```
插件层 (Extensions/Chat, Extensions/Trade, 第三方)
  → 共享契约层 (ClientExtensionAbstractions)
    → 宿主层 (Client / Server)
      → 基础设施层 (Common: 网络、认证、用户管理)
```

核心原则见 [设计哲学.md](../docs/设计哲学.md)，要点：

- **插件平权**——Chat 和 Trade 只是插件。第三方 submod 使用完全相同的发现 → 注册 → 激活路径。
- **三条管道**——所有通信通过 `message`（展示）、`command`（控制）、`item`（载荷）流转。
- **动态 UI**——Tab、侧栏、角标由插件通过 `IMainTabProvider` / `IServerSidebarProvider` / `IBadgeProvider` 贡献。
- **Server Pipeline**——入站消息经 IngressValidation → PreHandle → DefaultProcess → Observation → Outbound 五段流水线，扩展可在任意阶段接管。

# 路线图

当前版本已完成核心基础：服务端可在 Docker 上一键部署、客户端聊天和交易功能稳定可用。后续工作重心是让整个系统更开放、更稳定：

- **短期**：修完已知的线程安全问题和 UI 性能瓶颈
- **中期**：真正的插件化——Chat 和 Trade 从 "官方内建" 变成 "官方随发插件"，第三方作者也能用自己的扩展挂入同一个系统，不需要改宿主一行代码。
- **远期**：Mods 目录热加载（上传即启用）、插件市场、Web 管理面板。

# 开发者说明

## 环境准备

客户端项目依赖 RimWorld 程序集，需放入 `GameDlls/`：`Assembly-CSharp.dll`、`UnityEngine.dll`、`UnityEngine.CoreModule.dll`、`UnityEngine.IMGUIModule.dll`、`UnityEngine.InputLegacyModule.dll`、`UnityEngine.TextRenderingModule.dll`。支持按版本划分子目录。

## 构建

解决方案 `Phinix.sln`。提供 `TravisCI` 构建配置用于无需游戏程序集的场景。

- **Client**: .NET Framework 4.7.2（Unity/Mono 生态）
- **Common 项目**: multi-target `net472;net10.0`
- **Server**: .NET 10.0

## 扩展开发

插件实现 `IPhinixExtensionModule`，通过 `IExtensionBuilder` 注册 handler、renderer、API：

```csharp
public class MyExtension : IPhinixExtensionModule
{
    public string ExtensionId => "my.extension";

    public void Register(IExtensionBuilder builder)
    {
        builder.AddCapability("my.extension");
        builder.AddClientMessageHandler(this);
        builder.RegisterApi<IMyService>(this);
    }
}
```

完整指南见 [设计哲学.md](../docs/设计哲学.md)。

# 致谢

特别感谢原 Phinix 作者和贡献者，以及 [Longwelwind 的 Phi mod](https://github.com/longwelwind/phi) 为项目奠定的早期基础。
