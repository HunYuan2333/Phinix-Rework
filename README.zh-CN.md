<h1 align="center">Phinix Rework</h1>
<h4 align="center"><i>RimWorld 1.6 多人联机模组客户端 — 跨殖民地聊天、异步交易与托管插件框架</i></h4>

<p align="center">
  <a href="./README.md">English</a> · 简体中文
</p>

---

## 关于项目

Phinix Rework 是一个通过独立专用服务器为 RimWorld 提供多人通信与经济互动的模组。它在保留游戏原版独立模拟机制的同时，连接不同的玩家殖民地。

本仓库为 **Phinix Rework 客户端**（Phinix Rework Client），包含 RimWorld 客户端宿主、游戏内 UI 以及官方客户端插件。

- **游戏内聊天**：支持跨殖民地文本交流、富文本格式、名字与消息自定义颜色及频道划分。
- **异步物品交易**：支持在玩家不同时在线的情况下发起、浏览和结算殖民地间的物品与白银交易。
- **统一虚拟库存**：本地暂存流水线，支撑交易结算与中转管理。
- **插件商店与扩展管理**：官方与第三方扩展享有完全相同的运行时生命周期；支持在游戏内直接浏览、安装、启停与卸载托管插件。

> [!NOTE]
> Phinix Rework 提供独立殖民地之间的通信、交易与插件交互功能，**不提供**全图帧同步建造、同屏联机走时或联机建造同步。

---

## 仓库关系与架构索引

本项目统一称为 **Phinix Rework**，与原 Phinix 项目区分。工程由三个独立仓库组成：

| 仓库 | 角色 | 职责 |
| :--- | :--- | :--- |
| [Phinix-Rework](https://github.com/HunYuan2333/Phinix-Rework/tree/dev) | 客户端（本仓库） | RimWorld 1.6 模组客户端宿主、游戏内 UI 及客户端插件 |
| [Phinix-Rework-Common](https://github.com/HunYuan2333/Phinix-Rework-Common/tree/dev) | 共享层 | 游戏无关的网络、加密认证、用户模型与中立 Chat/Trade 契约 |
| [Phinix-Rework-Server](https://github.com/HunYuan2333/Phinix-Rework-Server/tree/dev) | 服务端 | 独立的专用服务器程序、服务端插件及 Docker 容器镜像 |

客户端和服务端均将共享层作为 Git 子模块固定在 `Dependencies/Phinix.Common`，编译时通过项目引用直接参与构建。为保持生态与存档兼容性，仓库更名不改动现有程序集名称、代码命名空间、协议标识、模组 packageId、持久化键及 `Dependencies/Phinix.Common` 目录名。

---

## 获取与安装

### 客户端安装（RimWorld 1.6）

#### 方式 A：Steam 创意工坊（推荐）

1. 在 Steam 创意工坊中订阅 [Phinix Rework](https://steamcommunity.com/sharedfiles/filedetails/?id=3735269431)。Steam 将自动完成下载与版本更新管理。
2. 启动 RimWorld，在主菜单进入 **模组**（Mods），勾选启用 **Phinix Rework** 并重启游戏。

#### 方式 B：手动安装发布包

1. 从 [GitHub Releases](https://github.com/HunYuan2333/Phinix-Rework/releases) 获取发布包（或参考下方[开发者说明](#开发者说明)生成本地包）。
2. 将解压后的模组目录放置于 RimWorld 的 `Mods` 文件夹，例如：
   `<path-to-RimWorld>/Mods/Phinix-Rework/`
3. 启动 RimWorld，在 **模组**（Mods）菜单中勾选启用 **Phinix Rework** 并重启游戏。

### 服务端运行说明

Phinix Rework 客户端需要连接专用的 Phinix Rework 服务端。如果你需要自行搭建服务器，请参考 [Phinix-Rework-Server](https://github.com/HunYuan2333/Phinix-Rework-Server/tree/dev) 仓库的说明文档。服务端支持 Docker 镜像（`hunyuan2333/phinix-rework:dev`）一键部署，也支持使用 .NET 10 SDK 源码构建。

---

## 第一次连接

1. 载入已有存档或新建殖民地。
2. 点击屏幕底部导航栏的 **Phinix** 按钮。
3. 在弹出的 Phinix 窗口中打开 **设置**（Settings）。
4. 填入服务器 IP 地址、端口（默认 UDP `16200`）以及你的认证密钥或用户名。
5. 点击 **连接**（Connect）。
6. 连接成功后，状态栏将显示已连接，聊天、交易、库存和商店等 Tab 即可正常使用。

### 连接排查

- **连接超时**：确认服务器宿主机及网络防火墙已开放 UDP `16200` 端口。
- **认证失败**：检查客户端密钥是否与服务端登记的用户信息一致。
- **协议版本不匹配**：确保客户端与服务端运行相同或兼容的 Phinix Rework 版本。

---

## 插件商店与扩展管理

Phinix Rework 内置游戏内插件商店，玩家无需手动解压或修改模组目录即可安装和管理插件。

1. **浏览商店**：
   - 打开 Phinix 窗口，切换至 **商店**（Store）Tab。
   - 浏览官方索引目录中已通过审核的插件。
2. **安装插件**：
   - 选择所需插件，点击 **安装**（Install）。
   - **完全退出并重启 RimWorld**，以便游戏引擎加载新安装的程序集。
3. **扩展管理**：
   - 打开 **扩展管理**（Extension Manager）查看已安装插件清单。
   - 点击切换插件 **启用**（Enabled）或 **停用**（Disabled），或点击 **卸载**（Uninstall）。
   - 启停与卸载变更均在**下一次重启游戏后生效**。
4. **网络加速切换**：
   - 在 Phinix 设置中，可在 **GitHub 直连** 与 **CF 加速** 之间手动切换。
   - 两种访问方式读取的是官方索引中完全相同的不可变元数据与发布包字节；在直连网络受限时建议切换为 CF 加速。

---

## 内置核心功能与托管插件关系

Phinix Rework 明确区分核心内置功能与独立托管插件：

| 分类 | 包含模块 | 分发形式 | 生命周期 |
| :--- | :--- | :--- | :--- |
| **内置核心** | Chat（聊天）、Trade（交易）、Inventory（库存）、PluginStore（商店） | 随主模组包直接分发（`Common/Extensions/`） | 随主模组整体更新 |
| **托管插件** | Talent Trade（人才贸易）、Red Packet（红包）、第三方插件 | 通过插件商店安装至 `SaveData/Phinix/ManagedExtensions/packages/` | 在游戏内独立管理、启用或卸载 |
| **工坊 Mod** | 外部 RimWorld 附属 Mod | 通过 Steam 创意工坊订阅或放入 `Mods/` | 由 RimWorld 原版模组管理器管理 |

> [!NOTE]
> 旧版人才贸易（`Phinix-Legacy-TalentTrade`）与红包（`Phinix-Legacy-RedPacket`）已从主模组彻底剥离，作为独立托管插件维护。玩家可在游戏内通过插件商店按需安装。

---

## 开发者说明

### 环境准备

- **.NET 10 SDK**（用于测试与构建驱动）
- **.NET Framework 4.7.2** 目标包（工程已通过 `Microsoft.NETFramework.ReferenceAssemblies` 支持在全平台使用 dotnet MSBuild 编译）
- **RimWorld 1.6 程序集引用**：需将游戏引用 DLL 放置于 `GameDlls/1.6/` 目录（`Assembly-CSharp.dll`、所需的 `UnityEngine*.dll` 模块，包括 `UnityEngine.ImageConversionModule.dll`，以及 `com.rlabrecque.steamworks.net.dll`）。若使用外部引用目录，可通过 `-p:GameReferenceDirectory=<path>` 和 `-p:RimWorldDepDir=<path>` 指定。严禁将游戏程序集提交或分发。

### 获取源码与子模块

克隆仓库及递归子模块：

```bash
git clone --branch dev --recurse-submodules https://github.com/HunYuan2333/Phinix-Rework.git
cd Phinix-Rework
```

对于已有仓库，递归初始化并更新固定提交的子模块：

```bash
git submodule update --init --recursive
```

> [!IMPORTANT]
> 仓库使用固定提交的 gitlink 维护依赖关系，**切勿使用** `git submodule update --remote`。共享层子模块中嵌套有 protobuf 子模块，必须使用 `--recursive`。

### 自动化测试

运行无 UI 的自动化运行时测试套件（注意：自动化测试通过仅用于验证底层逻辑，不代表 Unity/RimWorld 游戏内实测通过）：

```bash
# 核心框架运行时回归
dotnet run --project Tests/Phase35ClientRuntimeTests/Phase35ClientRuntimeTests.csproj --configuration Release

# 自适应布局几何测试
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release

# 托管扩展运行时回归
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0

# 插件商店逻辑回归
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release
```

### 客户端构建

在仓库根目录执行全量客户端构建：

```bash
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

> [!NOTE]
> 解决方案文件 `Phinix.sln` 与 `PhinixClient.sln` 包含完全相同的客户端工程图，两者均可作为构建入口。

### 输出位置与结构

构建产物自动打包至仓库根目录的 `Output/phinix-rework/`：

```text
Output/phinix-rework/
├── About/               # 模组元数据与版本描述
├── Defs/                # 游戏内定义 XML
├── Languages/           # 多语言本地化文本
├── Textures/            # UI 图标与纹理资源
├── LoadFolders.xml      # 游戏版本加载规则
├── Common/
│   ├── Assemblies/      # 共享依赖库与合成运行时
│   └── Extensions/      # 内置核心扩展程序集（Chat, Trade, Inventory, PluginStore）
└── 1.6/
    └── Assemblies/      # RimWorld 1.6 客户端主程序集（13-PhinixClient.dll）
```

该目录即为完整的 RimWorld 模组目录。手动测试时直接将其复制到 `<path-to-RimWorld>/Mods/Phinix-Rework/` 即可。

### 插件开发 Skill

**Phinix Rework 插件开发 skill** 面向小白和开发者，按步骤帮助选择托管插件或 RimWorld Mod 路线、准备编译引用、使用公开接口与 DI、构建和验证插件包，以及通过 Release 和 Index 发布。详细参考按当前任务需要读取。

- [简体中文 skill](skills/phinix-rework-plugin-development/phinix-rework-plugin-development-cn/SKILL.md)
- [English skill](skills/phinix-rework-plugin-development/phinix-rework-plugin-development-en/SKILL.md)

选择一种语言，将该语言目录及其 `references/` 一起交给支持 skill 的 AI 工具，按工具支持的方式使用。外层 `phinix-rework-plugin-development/` 只是两个语言版本的分发目录，不是可单独安装的 skill。

目前 skill 为开发引导草案。开始前确认目标宿主具备文档中的接口与本地安装工具；工作区修改、正式发行和游戏验收分别记录。现有发布 Actions、手动发行及 Index 流程继续可用，具体能力状态见对应 skill 的版本说明。

### 文档与参考

- [插件开发指南](docs/插件开发指南.md) — 托管插件开发规范、生命周期与 DI 组合、打包与 Index 发布流程。
- [设计哲学](docs/设计哲学.md) — 插件平权边界、三条通信管道与生命周期规范。
- [兼容边界与故障恢复约束](docs/Compatibility-Boundaries.md) — 客户端与不同服务端版本的网络契约与恢复约束。
- [客户端虚拟库存边界](docs/Inventory.md) — 虚拟库存暂存与日志恢复规范。
- [Phinix-Example-Plugin](https://github.com/HunYuan2333/Phinix-Example-Plugin) — 官方示例插件，展示 Tab 页面、设置面板与双语本地化。
- [Phinix-Plugin-Index](https://github.com/HunYuan2333/Phinix-Plugin-Index) — 官方插件目录与收录仓库。

---

## 致谢与开源协议

Phinix Rework 延续了由 Phinix 团队打造的原始 Phinix 模组理念，并汲取了 Longwelwind 的 [Phi 模组](https://github.com/longwelwind/phi) 早期基础。
