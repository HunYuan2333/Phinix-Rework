# P3 客户端发送前持久意图与原结果查询

2026-10-22；SV-31/33、TQ-30/31。本轮在已冻结工坊业务基线后的 `codex/p3-trade-business` 工作区实施，原 UI 工作区保持冻结字节。客户端/Common/protobuf gitlink 不改变，无新增外部包。

## 行为与边界

- 正式 Trade 激活在用户数据目录下建立 `operation-intents`，与工坊 Mod 目录分离。create/offer/status 在进入任何物品/命令传输前保存原 operationId、规范 typed request、SHA-256 指纹、correlation、观察版本以及整批 staged 引用/opaque payload。持久 attempt 先于第一条物品/命令；文件发布失败、整批引用缺口或冲突不进入发送回调。一个已尝试的原意图不能被重新发送。
- offer 先完整编码并形成确切 item packet ID 与命令，再保存完整意图，最后按原管线发物品、命令。没有绕过 handler 管线，未修补第三方 claimed item handler 的未声明关联能力。
- 已关联原响应先原子保存再进入原业务处理。精确重复不再次触发回调；同 ID/correlation 换请求、冲突响应、坏记录和写入失败拒绝。原响应与“持久查询确认”的证据种类分开：`ResponseSuccess/Failure` 不自行证明 SQLite COMMIT 或游戏保存；只有现代查询返回原已提交摘要才记 `CommittedSuccess/Failure`。
- 通过 Trade 正常插件 API 注册只读核对能力。按当前 endpoint/稳定主体筛选记录，现代查询必须有实际协商的 request/response capabilities。请求仅携原 ID/指纹；响应关联查询 ID、当前 session、system sender、endpoint 和主体，不接受别人的结果。
- 查询不会重新发送原资产或触发库存结算。Committed 摘要持久保留，NotFound/Conflict/InvalidRequest 是当前会话的观察；未找到不允许退款，之后的 NotFound 也不能覆盖已知原结果。旧端没有被补造能力，没有 fallback 重发。
- 交易列表新增操作记录与“核对服务端原结果”入口。区分已保存未尝试、发送结果未知、普通回执、已提交摘要和本次未找到。已知结果不证明游戏交付保存，未提供自动退款/补投按钮。查询本身无权限解决旧库存预留；恢复结算仍需后续主体/存档证据。
- 当前固定解析/证据保护上限是单条 2 MiB、总证据 64 MiB、1024 个原意图，查询摘要单条 8 KiB、在途查询最多 64；相同操作的新查询替换旧只读关联，不创建资产重试。预算命中停止新发送，不淘汰旧证据。Linux 私有目录/文件，身份与内容校验，fsync 文件后不可覆盖发布。未承诺目录落盘/断电零丢失。SessionId、ClientKey 等认证秘密不进入意图文件。

## 实际检查

SDK 10.0.113 / runtime 10.0.13，`NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages`、`DOTNET_CLI_HOME=/tmp/phinix-dotnet-113`、`MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks`。未改 vendored SDK pin。1.6 使用仓库外原有 GameDlls，1.7 使用 `references/1.7.4977-rev1248`；编译引用不进入交付证据。

- `dotnet build Tests/TradeIntentJournalTests/TradeIntentJournalTests.csproj -c Release -m:1 -p:NuGetAudit=false`；运行 DLL **32 项**通过。真实文件、独立字面请求 SHA、完整 staged 批次/ID、主体/endpoint、秘密排除、冲突/坏记录、结果持久/重复、会话与主体错配、NotFound 不覆盖原结果，以及 `intent.attempt-before-transport` 子进程 barrier/SIGKILL 后仍未知。
- 按现有 `build-net10-graph.py` 构建 1.7 **26 项目**通过，按 `build-graph-isolated.py --plugins --test-locks` 构建 1.6 及三个维护插件通过。两版都有既有警告；没有当作原生游戏运行通过。
- 最终实际 1.6、1.7 Trade DLL 各 **28 项** wire/持久边界检查通过：真实 builder/serializer/adapter，原始物品字节、准确 packet ID 与 correlation、首个外部回调前已有 intent+attempt、日志不含 session、超预算整批不发。1.6 DLL 探针运行于 .NET 10，未声称真实 Mono/Unity 游戏验收。
- 最终 1.7 实际 Trade DLL 组合 **241 断言**通过；原持久收件箱 61 项检查沿用本轮拆分执行；原 UI 工作区 536 项 stub renderer 检查通过且文件 hash 保持。
- `tools/quality/p3_intent_network.py`：独立 published server、独立真实 UDP peer 进程，反射使用最终产品 DLL 中的实际 journal/query 组件，不引用服务端 Adapter 生成预期。三阶段通过：发送前保存 create/offer、刻意不应用两个真实原响应；服务端 SIGKILL 后客户端和服务端分别以新进程恢复，按原 ID/指纹得到 create 版本 1、offer 版本 2；再次重开客户端直接保留两个持久结果。never-sent 查询只得到 NotFound 观察。每阶段独立 SQLite integrity/操作唯一/资产字节 `{17,0,255}` 检查，业务操作始终 2 条，查询写操作 0，无原请求或资产重发。临时凭据私有且不出日志/证据。故障是受控丢应用响应与 SIGKILL，不是断电。

命令入口如下；联合脚本需允许本机临时 UDP，只访问合成身份和临时目录：

```
python3 tools/quality/p3_intent_network.py --server <published-server>/PhinixServer.dll --config-writer <ServerLifecycleTests.dll> --probe <FrameworkAdmissionRuntimeTests.dll> --client <actual-TradeExtension.Client.dll> --output <new-evidence-directory>
```

服务端同时完成 SQL Pending 索引/live revision 和 34 项 stage，见 Server 对应文档；它与客户端固定 Common 不同，联合流程已真实执行，未用单仓库编译替代协议检查。

## 未完成的 P3 范围

本轮没有实现现代 durable delivery receipt、基于持久去重的安全交付重试或自动解决未知库存预留。接收排队期间的存档切换、同路径旧存档回滚、真实游戏物化/库存/双边存读档仍未通过。服务端整体 XML 热账本、冷归档、结果表磁盘预算与原版 protobuf 导入仍有缺口。原第三方 claimed item handler 关联缺口继续保留；未更改其契约或假造 ID。

因此完整 P3/P6 不标完成，未安装到工坊目录、发布、推送、部署或提交；本轮不是可直接发给玩家的联合最终版本。源码、失败 runner 记录、最终日志与实际 DLL hash 保存在独立 Deliverables 目录。
