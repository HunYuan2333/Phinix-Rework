# R17-10：Trade 请求契约归属

2026-10-13。逐步实施中的首个业务边界切片；未经游戏验收，不生成或部署正式包。

## 修改与设计依据

设计哲学 §1.2/§1.3 要求 Host 业务中立，§3.3 明确 Chat 直接引用 Trade Contracts 并调用 `ITradeRequestApi.CreateTrade()`。本切片按现有设计纠正接口归属，不引入 Host 业务协调器。

- 移除 `Client/ClientExtensionAbstractions/Framework/IClientExtensionAbstractions.cs` 中的交易专用接口。
- 新增 `Extensions/Trade/Contracts/ITradeRequestApi.cs:8`，归属 `Phinix.TradeExtension` / `TradeExtension.dll`，方法仍是 `void CreateTrade(string uuid)`。注释明确派发不等于创建或物品交付确认。
- `Extensions/Chat/Client/ChatExtension.Client.csproj` 引用 Trade Contracts；`BuiltInChatClientExtension.cs:76` 与 `ChatSidebarProvider.cs:130` 继续使用普通 API registry/directory。
- Trade 原 `BuiltInTradeClientExtension.cs:84` 的注册及 `FrameworkClientTradeServiceAdapter.cs:11` 的实现绑定新类型；执行逻辑未改。Host 没有 Trade Contracts/Client 程序集引用。
- 新增 `Tests/TradeCompositionRuntimeTests/TradeRequestBoundaryTests.cs`，由 `Program.cs:28` 调用；增加真实 Chat 与 Host 二进制检查引用。

没有变更 wire、存档字段、报价/托管状态机、完成标准或返还路径。未实现 1.6/1.7 renderer adapter；本切片也不证明 IMGUI 问题已经修复。

## 本次真实引用

本地游戏已从上轮 **1.7.4976 rev1043** 更新为 `Version.txt` 所示 **1.7.4977 rev926**；现有 Player.log 仍记录上轮 4976。本次没有新的 4977 游戏运行记录。以下为本次读到的 DLL 元数据与 SHA256，不依据旧日志推断新 DLL 身份。

| 引用 | 元数据 / SHA256 |
|---|---|
| 1.6 GameDlls Assembly-CSharp | `1.6.9438.37837`（既有 1.6.4633 rev1261）；`5b8c8ea2ebc4b393b108b780916281c18e1e0c65de85d891fdf5b374abcd7398` |
| 当前 1.7 Assembly-CSharp | `1.7.9782.27799`；MVID `05376757-545e-4f78-b576-215804b3ccc6`；`be2d5a0bcc44c8e9b713ac19cb073bb128418dab3c23b1ebf8eca6eceacbc2b8` |
| 当前 UnityStubs | `1.0.0.0`；MVID `5745b426-6e35-466d-870f-41ab4b959a28`；`49badc8e2569062c693983555fc431622eab5ffc7e1e72252e8103f8d540e154` |
| 本地 1.7 Harmony | `3.0.0.0`；MVID `64580f62-9d9e-41bc-b0fc-58cd936a45fc`；`94dd929a06f720869f6a718a64bc6713a37dbe153c2f4c16c031b2587d7ee694`；哈希与上轮核对的 rc.2 相同 |

1.6 隔离编译用 net472、原 vendored Harmony 2.3.6；1.7 用 net10.0 与本地 Harmony 3。两版白盒都运行在系统 .NET 10.0.12，不是 Unity 或游戏定制 Waylith CLR。1.7 编译选择同一固定 Autofac 8.4.0 的 net8.0 asset；没有改包版本或 vendored protobuf pins。

## 验证矩阵

| 检查 | 1.6 | 1.7.4977 | 限制 |
|---|---|---|---|
| 契约归属、注册和当前调用方 | 源码检查 | 源码检查 | 红包、人才、Example 没有直接消费此接口 |
| 16 个选定源码项目 | 编译通过 | 编译通过 | Abstractions、Composition、Trade/Chat/Inventory Contracts 与 Client、Host、Store、红包/人才 Contracts 与 Client、Example、Trade 测试；不是完整正式构建图 |
| TradeCompositionRuntimeTests | 白盒通过，204 断言 | 白盒通过，204 断言 | 真实本轮编译 DLL，非 GUI；有现有生命周期、报价关联、原 Game scope 与恢复投影检查 |
| 新请求边界检查 | 白盒通过 | 白盒通过 | 检查真实 Host/Chat 的程序集引用和 provider 查找；无 Trade、未激活、失败、注销、替换、Host directory Dispose 均不误用旧 handle |
| 菜单绘制、真实联机创建、失效后的玩家交互 | 待测 | 待测 | 游戏通过：无 |
| ABI/联合包 | 待测 | 待测 | 尚无本切片候选包或发布构建检查 |

检查工具 `Tests/TradeCompositionRuntimeTests/run-isolated.py` 读取原项目的 Compile 清单，生成独立分析工程，不执行正式工程的复制/发布 targets，不覆盖 GameDlls、bin 或 Output。它为上表选定项目重编源码并按顺序换入新 DLL；Common/通信/protobuf 等基础依赖仍从既有 Output 读取，因此不能称为完整源码构建或正式发行可复现。工具保存本地引用与源码指纹、每项目日志、结果 JSON、测试日志；游戏引用和运行目录仅留本地，不提交或分发。

重现命令（输出目录必须全新，所列目录为本次本地输入）：

```sh
python3 Tests/TradeCompositionRuntimeTests/run-isolated.py --version 1.6 --game /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --harmony /home/hunyuan2333/Phinix/Phinix-Rework/.nuget/Lib.Harmony.2.3.6/lib/net472/0Harmony.dll --output /tmp/phinix-r17-10/repro-1.6
python3 Tests/TradeCompositionRuntimeTests/run-isolated.py --version 1.7 --game /mnt/data/SteamLibrary/steamapps/common/RimWorld --harmony /mnt/data/SteamLibrary/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll --autofac /home/hunyuan2333/.nuget/packages/autofac/8.4.0/lib/net8.0/Autofac.dll --output /tmp/phinix-r17-10/repro-1.7
```

## ABI 与下一切片

接口从 `ClientExtensionAbstractions.dll` / `PhinixClient.Framework` 改为 `TradeExtension.dll` / `Phinix.TradeExtension`，类型身份改变，属于破坏性 ABI 变更。旧 Chat/Trade 二进制不能混入本轮新契约。当前生产 API 2.0 身份与三个正式插件 Host pin 未更新；不能单独部署这一切片。R17-22/23 与 R17-70～73 必须统一决定新 API major、程序集身份、Host/三插件/Example 绑定和包支持范围，再允许候选交付。不为此次迁移添加旧客户端兼容桥。

R17-11 下一步仅整理 Inventory 投递实现归属。`IInventoryCodec.DeliverVerified(Thing, Map, IntVec3)` 仍是 codec owner 的游戏接入权威，不因搬目录就交给 Host；需同步 Inventory、Trade、红包消费者，保留整批校验、custody、Unknown 与 rollback 规则。R17-12 先审查实际游戏依赖，不能因为接口名称含 Host 或使用 Thing 就无依据改写业务。

真正游戏检查须在后续联合候选可运行后执行：Trade 不在/停用时 Chat 交易入口不可用；启用时正常创建；Trade 失败/注销后旧入口不可继续派发；重新激活只用当前 provider。当前不要求用户安装这个 ABI 未闭合的中间产物。
