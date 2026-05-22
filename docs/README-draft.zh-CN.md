<h1 align="center">phinix-rework</h1>
<h4 align="center"><i>基于 Phinix 重构的 RimWorld 聊天与交易模组</i></h4>
<p align="center"><img src="../Client/About/Preview.png" alt="phinix-rework 预览图"></p>
<br><br>

> 这是用于评审的 README 中文草稿。  
> 正式使用的 `.github/README.md` 目前仍保持不动。

English version: [README-draft.md](./README-draft.md)

# 关于项目
`phinix-rework` 是基于 **Phinix** 延续开发的一次重构版本。Phinix 原本由 Phinix 团队制作，是一个为 RimWorld 提供聊天和交易功能的模组。

和原版一样，`phinix-rework` 的核心目标仍然是让玩家可以：

- 在游戏内进行聊天
- 在不同殖民地之间交易物品
- 使用独立外置服务器进行联机交互
- 以异步方式处理交易，而不要求双方同时立刻响应

这次 rework 会保留这些核心能力，但更强调后续维护性、兼容边界，以及未来扩展能力。

和原版相比，这次 rework 并不只是改名。它正在逐步把原本较为固定的实现，整理成更清晰、可扩展的 framework 架构，好让未来新增功能和 submod 更容易接入。

当前这次重构重点包括：

- 在现有客户端/服务端流程之上引入 framework 化的消息管线
- 增加客户端与服务端之间的 capability negotiation
- 使用基于反射的 extension 自动发现机制
- 更清楚地区分默认内建行为与扩展行为
- 在 framework 不可用时保留 legacy fallback
- 为自定义消息类型、渲染器、物品 codec、交易完成处理器打基础

简单来说，项目正在从“把聊天和交易硬编码在核心里”逐步转向“把聊天和交易作为默认实现，运行在更通用的扩展框架之上”。

# 这次 Rework 改了什么
原版 Phinix 已经具备不少很实用的基础功能，例如：

- 聊天消息时间戳
- 未读消息提醒
- 双向交易界面
- 异步交易
- 可配置的聊天显示方式

`phinix-rework` 则是在这些已有能力之上，进一步推动架构层面的调整，例如：

- 把聊天和交易逐步迁移到 framework 管线管理
- 引入客户端和服务端之间的特性协商
- 为 submod 和自定义协议扩展提前预留接口
- 更明确地区分 legacy 兼容路径与新 framework 路径
- 减少只能靠核心硬编码维护的行为

这样做的意义在于，项目后续演进时不需要反复重写核心逻辑，新增玩法也更容易以扩展方式接入。

# 安装
## 客户端
1. 构建或获取 `phinix-rework` 的客户端发布包。
2. 将其解压到 `RimWorld/Mods` 目录中。
3. 安装所需的 Harmony 依赖。
4. 启动 RimWorld 并启用该模组。
5. 如果游戏要求重启，则重启后生效。

当前仓库在 `About.xml` 中声明支持的 RimWorld 版本为：

- `1.3`
- `1.4`
- `1.5`
- `1.6`

## 服务端
服务端配置文件为 `server.conf`。

当前默认配置大致包括：

- 端口 `16200`
- 最大连接数 `1000`
- 认证方式 `ClientKey`

运行服务端的大致步骤：

1. 打开 `server.conf` 并按需修改配置。
2. 构建服务端项目。
3. 运行 `PhinixServer.exe`。
4. 如有需要，可在控制台中使用 `help`、`version` 等命令。

## Docker
仓库目前仍保留了服务端容器部署相关文件：

- `Dockerfile`
- `docker-compose.yml`

这说明 Docker 形式的服务端部署仍然属于项目预期支持的工作流之一。

# 使用方式
## 客户端
1. 进入一个存档或创建新殖民地。
2. 打开游戏内的 `Chat` 标签页。
3. 点击 `Settings`。
4. 输入服务器地址和端口。
5. 连接服务器。

连接成功后，聊天面板应当变为可用状态，右侧用户列表也会同步显示当前在线玩家。

## 服务端
服务端的职责主要包括：

- 接收客户端连接
- 执行认证
- 同步聊天消息
- 同步交易状态
- 在支持时向新客户端暴露 framework capabilities

这次 rework 也在努力保证：当双方都不具备新 framework 能力时，基础功能仍可以尽量优雅地降级运行。

# 开发者说明
## 环境准备
### 游戏 DLL
客户端项目依赖 RimWorld 安装目录中的程序集，通常位于：

`<RimWorldDir>/RimWorldXXX_Data/Managed/`

这些 DLL 需要放入 `GameDlls/` 目录中。

常见包括：

- `Assembly-CSharp.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `UnityEngine.IMGUIModule.dll`
- `UnityEngine.InputLegacyModule.dll`
- `UnityEngine.TextRenderingModule.dll`

如果你同时维护多个 RimWorld 版本，也可以在 `GameDlls/` 下使用按版本划分的目录。

### 构建
仓库当前包含：

- `Phinix.sln`
- 客户端、公共库、服务端等 C# 项目
- 一个不依赖 RimWorld 客户端 DLL 的 `TravisCI` 构建配置

如果你只需要构建共享库和服务端，那么非客户端构建流程会更轻量。

### 协议相关
项目使用 Protobuf 定义网络数据包和生成相关代码。

如果你修改了 packet 结构，那么也需要配套的 `protoc` 和 C# 生成支持。

## 本次 Rework 对开发者最重要的变化
如果你是从开发者角度阅读这个仓库，那么 `phinix-rework` 最重要的变化，是它正在逐步把核心逻辑转向 framework extension points。

当前架构工作的重点包括：

- framework envelope
- capability negotiation
- extension discovery
- client/server message handler
- renderer 和 interceptor
- item pipeline 与 trade completion contract

长期目标是让未来新玩法优先通过这些接口接入，而不是一遍遍去改核心宿主代码。

# 致谢
特别感谢原 **Phinix** 的作者和贡献者，构建了这次 rework 所依赖的基础代码。

同时，这个项目也承认 Phinix 更早之前的来源，包括 [Longwelwind 的 Phi mod](https://github.com/longwelwind/phi)。

没有这些前作的积累，就不会有现在这次 rework。

# 草稿备注
这份文档的目标，是在替换 `.github/README.md` 之前先把正式 README 的方向整理清楚。

后续可以继续补充的内容包括：

- 在新的发布地址确定后补上 release/download 链接
- 增加聊天界面和交易界面的截图，让 README 更图文并茂
- 持续同步中英文两个版本的内容
