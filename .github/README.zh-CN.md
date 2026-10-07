<h1 align="center">Phinix Rework</h1>
<h4 align="center"><i>RimWorld 1.6 多人联机模组 — 跨殖民地聊天、异步交易与托管插件框架</i></h4>

<p align="center">
  <a href="./README.md">English</a> · 简体中文
</p>

---

## 关于项目

Phinix Rework 是一个通过独立专用服务器为 RimWorld 提供多人通信与经济互动的模组。它在保留游戏原版独立模拟机制的同时，连接不同的玩家殖民地。

- **游戏内聊天**：支持跨殖民地文本交流、富文本格式、名字与消息自定义颜色及频道划分。
- **异步物品交易**：支持在玩家不同时在线的情况下发起、浏览和结算殖民地间的物品与白银交易。
- **统一虚拟库存**：共享物品暂存流水线，支撑交易结算与中转管理。
- **专用服务端**：轻量级独立服务器程序，支持用户身份认证与权限管理。
- **插件商店与可扩展运行时**：官方与第三方扩展享有完全相同的运行时生命周期；支持在游戏内直接浏览与安装托管插件。

> [!NOTE]
> Phinix Rework 提供独立殖民地之间的通信、交易与插件交互功能，**不提供**全图帧同步建造、同屏联机走时或联机建造同步。

---

## 获取与安装

### 客户端（RimWorld 1.6）

#### 方式 A：Steam 创意工坊（推荐，主流方式）

1. 在 Steam 创意工坊中订阅 [Phinix Rework](https://steamcommunity.com/sharedfiles/filedetails/?id=3735269431)。Steam 将自动完成下载与版本更新管理。
2. 启动 RimWorld，在主菜单进入 **模组**（Mods），勾选启用 **Phinix Rework** 并重启游戏。

#### 方式 B：手动安装

1. 从 [GitHub Releases](https://github.com/HunYuan2333/Phinix-Rework/releases) 获取发布包（或参考下方[开发者说明](#开发者说明)进行本地构建）。
2. 将模组文件夹解压至 RimWorld 的 `Mods` 目录，例如：
   `<path-to-RimWorld>/Mods/Phinix-Rework/`
3. 启动 RimWorld，在 **模组**（Mods）菜单中勾选启用 **Phinix Rework** 并重启游戏。

### 服务端（专用服务器）

服务端支持使用 Docker（推荐）一键部署，或使用 .NET 10 SDK 手动构建。

#### 方式 A：Docker 部署（推荐）

```bash
docker pull hunyuan23333/phinix-rework:latest

docker run -d \
  --name phinix-server \
  --restart unless-stopped \
  -p 16200:16200/udp \
  -v ./server_data:/data \
  hunyuan23333/phinix-rework:latest

# 查看运行日志
docker logs -f phinix-server
```

或使用 `docker-compose.yml` 启动：

```bash
docker compose up -d
```

#### 方式 B：手动构建（.NET 10 SDK）

```bash
dotnet build Server/Server.csproj -c Release -o out
dotnet out/PhinixServer.dll
```

配置文件为 `server.conf`（默认 UDP 端口 `16200`，认证方式为 `ClientKey`）。交互控制台支持 `help`、`version`、`exit` 等命令。

---

## 第一次连接

1. 载入已有存档或新建殖民地。
2. 点击屏幕底部导航栏的 **Phinix** 按钮。
3. 在弹出的 Phinix 窗口中打开 **设置**（Settings）。
4. 填入服务器 IP 地址、端口（默认 `16200`）以及您的认证密钥或用户名。
5. 点击 **连接**（Connect）。
6. 连接成功后，状态栏将显示已连接，聊天、交易、库存和商店等 Tab 即可正常使用。

### 连接排查

- **连接超时**：确认服务器宿主机的防火墙已开放 UDP `16200` 端口。
- **认证失败**：检查客户端密钥是否与服务端登记的用户信息一致。
- **协议版本不匹配**：确保客户端与服务器运行相同或兼容的 Phinix Rework 版本。

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

## 主包功能与独立插件关系

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

- **.NET 10 SDK**（服务端与自动化测试工具）
- **.NET Framework 4.7.2** 目标包（客户端编译支持）
- **RimWorld 1.6 程序集引用**：将引用 DLL 放置于 `GameDlls/1.6/`（`Assembly-CSharp.dll`、`UnityEngine*.dll`），或在构建时通过 `-p:GameReferenceDirectory=<path>` 指定。严禁将游戏程序集提交或分发。

### 开发者构建

```bash
# 生成服务端
dotnet build Server/Server.csproj --configuration Release

# 运行核心框架运行时回归
dotnet run --project Tests/Phase35RuntimeTests/Phase35RuntimeTests.csproj --configuration Release

# 运行自适应布局几何测试
dotnet run --project Tests/ResponsiveUiGeometryTests/ResponsiveUiGeometryTests.csproj --configuration Release

# 运行托管扩展运行时回归
dotnet run --project Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj --configuration Release --framework net10.0

# 运行插件商店逻辑回归
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release

# 全量客户端/服务端构建（需要本机 RimWorld 1.6 参考 DLL）
dotnet build Phinix.sln --configuration "Release 1.6" --no-incremental
```

### 文档与参考

- [设计哲学](../docs/设计哲学.md) — 插件平权边界、三条通信管道与生命周期规范。
- [Phinix 附属 Mod 开发者指南](../docs/Phinix附属Mod开发者指南.md) — 编写自定义扩展的进阶指南。
- [Phinix-Example-Plugin](https://github.com/HunYuan2333/Phinix-Example-Plugin) — 官方示例插件，展示 Tab 页面、设置面板与双语本地化。
- [Phinix-Plugin-Index](https://github.com/HunYuan2333/Phinix-Plugin-Index) — 官方插件目录与申请仓库。

---

## 致谢与开源协议

Phinix Rework 延续了由 Phinix 团队打造的原始 Phinix 模组理念，并汲取了 Longwelwind 的 [Phi 模组](https://github.com/longwelwind/phi) 早期基础。
