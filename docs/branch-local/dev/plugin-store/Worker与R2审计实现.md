# Worker 与 R2 审计实现

日期：2026-10-03；分支：`dev`。本批交付 **P1 本地工程、固定 GitHub 回源、私有 R2 协调与审计**，以及 P2 客户端错误编号/日志增量。没有创建 CF 账号、域名、桶、部署、真实测试 Release 或正式索引发布；P1 公网/边缘缓存/费用与游戏网络验收仍未完成。

工程入口：[Worker 中文说明](../../../../Extensions/PluginStore/RepositoryWorker/README.zh-CN.md)、[English](../../../../Extensions/PluginStore/RepositoryWorker/README.md)、[审计手册](../../../../Extensions/PluginStore/RepositoryWorker/AUDIT.zh-CN.md)、[English runbook](../../../../Extensions/PluginStore/RepositoryWorker/AUDIT.md)。实施依据仍为[主计划](插件商店分步实施计划.md)与[CF/R2 架构](插件商店GitHub权威源与Cloudflare分发架构.md)。宿主不依赖商店业务，后端独立于游戏专用 Server；不触及交易协议、物品所有权或旧存档。

## 本批行为

- allowlist 来源与规范 GET/HEAD 路由；固定 repo/owner 身份和同一次请求的 publication commit。stable/published 原始 hash/size 交叉核对，catalog 与包采用受审 Release/asset/tag/source commit 身份；CDN 跳转只在 Worker 内处理，token 不转交 CDN，客户端没有外部 Location。
- metadata 强 ETag/304；ZIP 分发有界增量 SHA-256、精确长度与 EOF 校验，不整包加载内存。最后一个有界块在 EOF/hash 确认前暂不交付，避免 Content-Length 客户端把错摘要文件判作下载完成。已有静态 ZIP/PE 校验仍由客户端/发布工具负责。
- 一个 SQLite Durable Object 对一个专用私有 R2 桶；最多 256 条跟踪记录。原型容量 64 MiB、单次填充上限 8 MiB，较大合法包可只流式服务。操作计数、容量预留、lease、状态与最近 256 条审计记录持久化。R2 stream 使用已知长度 FixedLengthStream；可选缓存分支落后超过 200 ms 就断开，交付继续。
- R2 与 SQL 分开确认。写入/删除确认丢失、SQL/audit 提交失败、重启、租约/周期过期均不自动释放不确定字节或清零计数；重复填充/未确认命中关闭。只在删除确认后释放旧版本容量。对象缺失/不符暂停缓存，预算/周期不符停止缓存读写；合法回源仍可继续。空桶初始化须明确确认，并留下 sentinel，防账本丢失后重置历史。
- 客户端刷新编号贯穿 metadata 请求；服务端 requestId 进入错误提示和过滤后的日志。区分响应头、完整 body、链路验证、暂存与实际原子提交；失败/取消/超时和清理记录不依赖窗口打开。错误 JSON 最大 4 KiB，拒绝重复/未知字段、无效 ID/code、非 bool retryable 及 header/body 编号冲突；坏 envelope 保留 HTTP 诊断。
- 自定义结构化日志不写 token、签名 URL、响应正文或未过滤异常堆栈。Worker requestId/spanId/component/sequence/build 与源/快照/包/hash/预算/lease 串联排查；事件截断保留终态。日志 sink 失败不改变校验/提交结果；SQL 审计失败则随状态事务一起回滚。平台/宿主自身日志不在该白名单承诺内。

默认配置不开放公网，R2/空桶确认关闭。没有自动核对 uncertain 条目、周期重置或公共 SQL/admin/purge API；需要未来受控恢复工具，当前不能通过删账本/过期 lease 推定容量已释放。9 GB 部署硬上限和账号零费用未获证明；不确定记录保留容量可能使缓存长期停填充，分发仍可回源。metadata 缓存可能重放原生产者 requestId，排查新请求同时使用 clientRequestId。

## 审计发现与修复

| 风险 | 本轮修复与证据 |
| --- | --- |
| 普通 Node 替身不能验证原生全局 API | 原生 workerd 发现把 fetch 当对象方法调用导致 Illegal invocation；改为正确的全局调用，并加入原生 stable/填充/命中用例。 |
| 发完最后字节再核摘要太晚 | 最后块保持到 EOF/hash 成功；Node 错摘要拒绝与原生 workerd 损坏文件无法完整交付通过。原生运行时可能返回 500，而非给测试端抛同一种异常；不绑定某个异常表现。 |
| put 成功但确认/账本失败误判空闲 | reserve 先持久化，不确定、SQL/audit 失败及晚于 lease/period 的提交保留字节；日志只在事务返回后声明 committed。 |
| 同 key 并发填充及桶/账本漂移 | 单协调器事务预留，重启仍拒绝重复；容量压力、旧版本删除确认丢失、sentinel 发现账本丢失与对象偏离暂停均有故障用例。 |
| 回源/慢缓存/取消无限拖延 | 回源调用/总预算、body idle、包总/空闲、慢分支与取消限制；没有无限 tee 缓冲。原型时间/CPU 仍待云端验证。 |
| 过滤日志丢失终态或泄露响应 | 截断标记和保留终态；URL/token/body/无效 ID 不进结构化字段，sink 故障与恶意 error envelope 测试通过。 |

## 已运行验证

以下命令均在仓库根运行（本地依赖安装已完成，不向游戏包附带 Node/Wrangler）：

```sh
npm --prefix Extensions/PluginStore/RepositoryWorker test
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false npm --prefix Extensions/PluginStore/RepositoryWorker run test:native
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release -p:BuildInParallel=false -m:1 --no-restore
dotnet build Phinix.sln --configuration 'Release 1.6' -p:BuildInParallel=false -m:1 --no-restore
mcs -langversion:7.2 -out:/tmp/PhinixRepositoryMonoSmoke.exe -r:System.Xml.Linq.dll -r:System.Runtime.Serialization.dll -r:System.Net.Http.dll Tests/PluginStoreRuntimeTests/Fixtures/RepositoryMonoSmoke/Program.cs Extensions/PluginStore/Client/PackageVersion.cs Extensions/PluginStore/Client/PackageModels.cs Extensions/PluginStore/Client/CatalogReader.cs Extensions/PluginStore/Client/RepositoryMetadata.cs Extensions/PluginStore/Client/RepositoryDiagnostics.cs Extensions/PluginStore/Client/RepositoryTransport.cs Extensions/PluginStore/Client/RepositoryCache.cs Client/ClientExtensionAbstractions/Framework/ClientEnvironmentSnapshot.cs
mono /tmp/PhinixRepositoryMonoSmoke.exe docs/branch-local/dev/plugin-store/metadata-protocol-v1
python3 /tmp/check-phinix-store-tab-artifacts.py
git diff --check
```

结果：Worker **63 个实际用例**通过；客户端 **429 项断言**通过；原生 workerd/SQLite/R2 替身验证 stable、已知长度 checksum 填充、命中且无作者回源、对象缺失暂停和错摘要无法完整交付。原生故障用例预期输出 `StreamDigestMismatch` 与 workerd 错误诊断，进程最终为成功；不将故障日志当作成功下载。Wrangler 仅 dry-run；Node 26.10.0、Wrangler 4.147.0、Miniflare 5.20261001.0-alpha 均锁定/核对，测试工具版本不等于生产套餐验收。

Mono 编译同一生产 metadata/transport/cache/diagnostics 源码并实际验证日志 JSON 序列化、模拟 HTTP 与缓存替换/损坏拒绝。solution 为 0 errors、2 项既有 protobuf SDK warnings；harness 沿用缓存 NU1900 网络 warning。产物镜像检查确认 26 必需文件、host/store 二进制与双语资源一致，预览未附带游戏/框架 DLL。未安装 PowerShell，原 `.github/scripts/check-artifacts.ps1` 未直接运行。框架/布局无新变化，本轮未重复跑其上一批通过的独立 harness；未触发远端 CI。

测试用 GitHub ID/包数据为合成值，原生 R2 仍为本地模拟，不是实际云服务；客户端测试 handler 没有真实 DNS/TLS/socket。没有游戏内新增网络验证、大陆网络、HTTP/SOCKS5、Steam、128 MiB 峰值、安装事务/恢复或线上 CPU/账单证据。未修改交易/持久业务状态，也未声称游戏安装已交付。

## 下一批

先准备一个受控真实 ZIP Release/catalog、published/stable 与测试 CF account/route/新私有桶/SQLite 资源，跑公开小容量冷启动、R2 命中、失败与账本核对、真实日志留存和限额。明确处置 uncertain 的受控修复路径，再考虑边缘缓存及 9 GB 容量。随后将 endpoint 接到游戏 metadata 验证，并进入 P3 有界临时文件下载/静态载荷校验；P4 归属与安装恢复依旧独立验收。暂不重复本地 JSON 游戏基础测试，也不把 HTTP 200 或 R2 保存当安装成功。
