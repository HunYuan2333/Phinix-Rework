# CF 正式入口迁移与旧 PoC 退役记录

2026-10-06 最新进展：[CF 已由独立仓库接管并完成用户游戏增量验收](CF独立发布接管与验收.md)。当前版本/源码入口以该记录为准；本文件保留此前域名迁移和仍待批准的旧资源退役清单。

[English](ProductionCfMigration.md)。2026-10-06，dev。用户已反馈 1.0.1 → 1.0.2 游戏升级通过，随后授权继续正式部署收尾。

## 已完成

正式地址为 https://plugins.hunyuan2333.com，phinix.official 固定来源。使用 wrangler.production.jsonc / src/read-only.mjs，启用 REPOSITORY_ENABLED。复用现有服务 phinix-plugin-repository-staging 及其 GITHUB_TOKEN；内部服务名不代表独立测试环境。旧 plugins-staging 地址只是同服务过渡别名。两配置当前相同，旧配置部署不会摘掉正式域名；后续通过正式地址的人工检查后再单独移除别名。

部署版本 7da31ea5-902c-4105-a460-ed459ef4d183，BUILD_ID production-20261006-read-only。只读官方协议，R2_ENABLED=false，无桶、DO 或公开运维绑定；每 Cloudflare location 聚合限流 30/60s，非全局计费硬上限。日志完整采样，仍不按数据块或每帧写日志。

客户端默认 CF origin 已改为正式地址。来源身份仍为 409040fdc5aeb97524d08c2e10f573b58453fc37c7134be666c143de04c9db93，不变更已安装凭据、路径或数据。HTTP AccessKey 随 origin 改变，旧地址 ETag 不复用；新增两条身份/HTTP 验证器断言。

正式与过渡域名的账号 API 路由复核通过，均指向当前服务。GitHub/.NET 和正式 CF/Mono 无代理真实下载一致：目录快照 2514837d84f27475c17cbb5daa5464ebd0f58339，目录 SHA-256 65b18df38737a142353719f10f13f45e56eaa651070f45bbc4f1695f70f961eb；示例 1.0.2 ZIP SHA-256 b0c8d5f9dc7674f355f2c30fd05967dbfdbd5aca3b7be48949293bdfc2139a80。

补充协议检查 13 次请求通过：stable/条件 304、published/catalog、1.0.0/1.0.1/1.0.2 原件及可信 ZIP/CLR 校验、别名一致；公开 operations、未知来源、Range、错误方法及 query 均拒绝。所有测试只读，不安装/执行插件。Cloudflare tail 捕获服务器日志，与响应 requestId 匹配 241 条结构化记录，包含 read_admitted、origin、stream.verified、request.complete/rejected，均绑定正式 BUILD_ID。使用默认 Python User-Agent 的探测在网关前返回 403；正常产品标识及实际客户端通过，不据此猜测来源协议失败。

## 旧 PoC 状态及待批准范围

旧 phinix-plugin-repository-poc 已取消全部公开路由，workers.dev 与预览关闭，API 复核 enabled=false / previews_enabled=false。旧配置同步取消公开路由，防止重新部署抢占正式域名。未删除 Worker、namespace、桶或 secret。

清点三个已部署服务的绑定，旧资源只被旧 PoC 本身使用，正式服务无引用。目标如下：

- Worker phinix-plugin-repository-poc，原版本 fb9c678d-307c-481b-9290-702368429ad2。
- DO namespace a48ff4dbf6684a008e9f663de491e910，phinix-plugin-repository-poc_CacheCoordinator，SQLite；按已部署源码用途是旧缓存预算、租约和审计，不是玩家设置、插件安装数据或存档。完整账本导出尝试出现 OperationsRpcTimeout，因此没有全量 SQL 副本。
- 桶 phinix-plugin-poc-20261004：仅两个对象。epoch 标记 __phinix/cache-ledger/poc-20261004-v1，45 字节；旧 phinix.poc.marker 1.0.0 包，4179 字节，SHA-256 0299999c8d6b14f7759e5d5dd06ce922aa520ef08e74bfc2540898cea68cfb11。已下载并验证两者，本地私有备份为 Output/operations-backup-20261006，位于正式 mod 输出之外且被 git 忽略。配置、对象清单、原部署及域名/引用清点元数据同时保留；这不等于完整 DO 备份。
- 旧 Worker 中 GITHUB_TOKEN / POC_ACCESS_TOKEN 的 secret 副本；不撤销正式 Worker 的 GITHUB_TOKEN，也不撤销 GitHub 账号 token。

自动审批审查拒绝了 wrangler delete：没有可信用户内容明确批准这次具体永久删除，并且账本副本不完整。未重试或换接口绕过；仅采用可恢复的关闭公开访问。等待用户明确批准上列旧资源删除或选择保留。

如获准：重新核对域名仍属于正式服务、资源清单及对象摘要未变，删除旧 Worker（会连带旧 namespace 数据与 secret），逐个删除已核验并备份的两个旧 R2 对象，再删除空桶，不使用 force 清空未知对象；复核旧服务/namespace/桶不在，正式访问仍正常。旧 DO 数据删除后不可恢复。GitHub 历史目录、审批锁、发布资产及客户端数据不在清理范围。

Cloudflare 官方参考：[列举对象](https://developers.cloudflare.com/api/resources/r2/subresources/buckets/subresources/objects/methods/list/)、[列举域名](https://developers.cloudflare.com/api/resources/workers/subresources/domains/methods/list/)、[删除 Worker 与 DO 影响](https://developers.cloudflare.com/api/python/resources/workers/subresources/scripts/methods/delete/)。

## 构建及回滚

已通过 Worker 143 项、原生 workerd/SQLite/R2/RPC/匿名限流回归、.NET/Mono 各 858 条主进程及真实子进程 18+18+16+16+20、商店 902 条。完整 solution 16 条既有警告/0 错误；NuGet 漏洞服务不可达、旧 protobuf 目标为现有环境限制。标准 PowerShell 制品工具不可用，等价必需文件/LoadFolders/主包与最新 host-store 字节/无游戏参考 DLL 检查通过，私有备份不在 distributable mod。

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework/Extensions/PluginStore/RepositoryWorker
npm test
WRANGLER_LOG_PATH=/tmp/phinix-production-local.log npm run check:production
# The native bundle prerequisites were built by npm run test:native.
# Sandbox EPERM prevented its local listener; this native command passed outside that sandbox.
node tests/native-runtime.mjs
WRANGLER_LOG_PATH=/tmp/phinix-production-deploy.log ./node_modules/.bin/wrangler deploy -c wrangler.production.jsonc
cd /home/hunyuan2333/Phinix/Phinix-Rework
dotnet build Phinix.sln --configuration "Release 1.6" --no-restore -p:RimWorldDepDir=/mnt/data/SteamLibrary/steamapps/common/RimWorld/RimWorldLinux_Data/Managed -p:BuildInParallel=false -m:1
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/PluginStoreRuntimeTests/bin/Release/net10.0/PluginStoreRuntimeTests.dll
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins.hunyuan2333.com phinix.official /tmp/phinix-production-cf-live-20261006 --official-cf
dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-production-github-live-20261006 --official-github
```

重复实时验证需新绝对状态目录；本机游戏引用路径可换成正确 GameDlls/1.6。生产部署已经执行，回滚只作为已准备命令，未实际回滚验收：

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework/Extensions/PluginStore/RepositoryWorker
WRANGLER_LOG_PATH=/tmp/phinix-production-rollback.log ./node_modules/.bin/wrangler rollback feb3bcce-e77d-48ea-90f0-952f7a813eba -c wrangler.production.jsonc -m "Restore previously checked official gateway"
```

此回滚恢复上一已验证的官方只读代码/绑定，不把域名指回旧 PoC，不回滚 catalog 或玩家数据。执行后需复核两个域名、secret 名称和真实下载；恢复正式版本可重新部署 production 配置。退役旧 PoC 无法恢复已删 DO，因此必须有单独明确删除批准。

## 下一次人工检查

覆盖最新 Output/phinix-rework，重启游戏，选择 CF 加速刷新商店；确认示例 1.0.2 仍被识别为已安装、管理器状态和计数正常。切回 GitHub 仍是相同条目，访问方式能保存。不要为了域名切换卸载现有插件。真实 Unity 游戏还未验收这次地址更换；旧别名暂时保留。永久退役审批与这次客户端检查分别处理。

正式入口与 A3 状态说明已通过索引文档 [PR #21](https://github.com/HunYuan2333/Phinix-Plugin-Index/pull/21) 合入；只修正文档事实，不做用户留给其他模型的风格重写，不改审批或发布资产。
