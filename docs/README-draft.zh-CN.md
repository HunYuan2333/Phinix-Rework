<h1 align="center">Phinix</h1>
<h4 align="center"><i>RimWorld 多人模组 — 聊天、交易与可扩展插件框架</i></h4>
<p align="center"><img src="../Client/About/Preview.png" alt="Phinix 预览图"></p>
<br><br>

> 这是用于评审的 README 中文草稿。
> 正式使用的 `.github/README.md` 目前仍保持不动。

English version: [README-draft.md](./README-draft.md)

# 关于项目

Phinix 是一个通过独立外置服务器为 RimWorld 提供多人聊天和物品交易的模组。它在原版 Phinix 基础上进行了重构，底层采用插件化框架架构。

核心能力：

- 游戏内殖民地间聊天
- 异步物品交易（无需双方同时在线）
- 独立服务器，支持认证与用户管理
- 可扩展插件系统，支持第三方 submod

# 架构

Phinix 已重构为分层、插件优先的架构：

```
插件层 (Extensions/Chat, Extensions/Trade, 第三方)
  → 共享契约层 (ClientExtensionAbstractions)
    → 宿主层 (Client / Server)
      → 基础设施层 (Common: 网络、认证、用户管理)
```

核心原则：

- **插件平权**——Chat 和 Trade 只是插件，不是内建特权模块。第三方 submod 使用完全相同的发现 → 注册 → 激活路径。
- **Host 不依赖插件**——宿主只引用 `ClientExtensionAbstractions`（通用契约层），不编译期引用任何具体插件工程。
- **三条管道**——所有网络通信通过 `message`（展示）、`command`（控制）、`item`（物品载荷）三条管道流转。
- **动态 UI**——Tab、侧栏、角标由插件通过 `IMainTabProvider` / `IServerSidebarProvider` / `IBadgeProvider` 贡献，宿主只提供容器壳。
- **API registry**——插件通过 `RegisterApi<T>()` / `TryResolve<T>()` 暴露和发现能力，不经过宿主中转。

# 安装

## 客户端

1. 构建或获取客户端发布包。
2. 解压到 `RimWorld/Mods` 目录。
3. 安装所需的 Harmony 依赖。
4. 启用模组并重启游戏。

支持的 RimWorld 版本：1.3、1.4、1.5、1.6。

## 服务端

服务端配置文件为 `server.conf`（默认：端口 `16200`，最大连接 `1000`，认证方式 `ClientKey`）。

1. 编辑 `server.conf` 按需修改。
2. 构建服务端项目（需要 .NET 10 SDK）。
3. 运行服务端。
4. 使用控制台命令，如 `help`、`version`。

## Docker

支持通过 `Dockerfile` 和 `docker-compose.yml` 进行容器化部署。

# 使用方式

## 客户端

1. 进入存档或创建新殖民地。
2. 打开底部工具栏的 Phinix 按钮。
3. 进入 Settings 输入服务器地址和端口。
4. 连接服务器。

## 服务端

服务端负责：连接管理、认证、聊天消息中继、交易状态同步、framework capability 协商。

# 开发者说明

## 环境准备

客户端项目依赖 RimWorld 程序集，需放入 `GameDlls/`：

- `Assembly-CSharp.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `UnityEngine.IMGUIModule.dll`
- `UnityEngine.InputLegacyModule.dll`
- `UnityEngine.TextRenderingModule.dll`

支持在 `GameDlls/` 下按版本划分子目录。

## 构建

解决方案 `Phinix.sln` 包含客户端、公共库、服务端和扩展项目。提供 `TravisCI` 构建配置用于无需 RimWorld 程序集的场景。

- **Client**: .NET Framework 4.7.2（Unity/Mono 生态）
- **Common 项目**: multi-target `net472;net10.0`
- **Server**: .NET 10.0

## 协议

数据包使用 Protobuf 定义。修改 packet 结构需配套 `protoc` 和 C# 生成工具链。

## 扩展开发

插件实现 `IPhinixExtensionModule`，通过 `IExtensionBuilder` 注册 handler、renderer、codec 和 API：

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

完整架构指南和设计规则见 [设计哲学.md](./设计哲学.md)。

# 致谢

特别感谢原 Phinix 的作者和贡献者，以及 [Longwelwind 的 Phi mod](https://github.com/longwelwind/phi) 为项目奠定的早期基础。
