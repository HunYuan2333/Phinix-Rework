# 游戏联网只读 staging

2026-10-04 更新：用户已反馈上一轮游戏联网/下载/取消/管理检查正常。当前 staging 有 marker 1.0.0 **和 Playtest 1.1.0**；最新步骤见[安装卸载游戏交接](安装卸载与游戏测试.md)。下文数字证据对应此前仅 marker 的快照。

2026-10-04，分支 `dev`。[English](GameStaging.md)。

独立 Worker `phinix-plugin-repository-staging` 已部署到 `https://plugins-staging.hunyuan2333.com`，索引源 ID 为 `phinix.poc`。它提供既有受控惰性测试包，尚未接正式索引。玩家请求不携带 bearer token；操作者通过 Wrangler 单独上传只读 `GITHUB_TOKEN`，未读取该值，也未从旧 Worker 提取凭据。

| 部署 | 用途 | 状态 |
| --- | --- | --- |
| `plugins.hunyuan2333.com` / PoC Worker | 私有认证的 R2/SQLite 实验 | 保留原期限、桶与账本；本批未重新部署 |
| `plugins-staging.hunyuan2333.com` / staging Worker | 匿名游戏传输测试 | 独立回源读取；无桶、DO、运维入口或 PoC 到期限制 |
| 正式生产 | 已审批源与持久分发 | 未部署；正式索引未改变 |

staging 只有在 `STAGING_ENABLED=true`、回源凭据和限流绑定有效时才访问 GitHub；误绑缓存资源/配置会关闭读取。无效 path/method/Range/source 在回源前拒绝。上传前实测缺凭据返回 503 `OriginCredentialsMissing`，Python/Mono 均有对应诊断与关联记录。Cloudflare 限流使用一个汇总 key，每个 location 30 次/60 秒；这是最终一致的本地测试节流，不是全局计费或 GitHub 额度硬上限。全部响应 `no-store`。要关闭入口，在专用配置中将 `STAGING_ENABLED` 改为 `false` 后重新部署。

限流边界见 [Cloudflare 官方文档](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/)。

## 实测发现并修复流式响应问题

初轮 Python 字节检查通过，但真实 net472/Mono 客户端在 ZIP 响应头阶段报 `PayloadSizeMismatch`。直连头检查显示实际 4179 字节、`Transfer-Encoding: chunked`、没有 `Content-Length`。Workers 对普通 `ReadableStream` 忽略手写长度，见 [Response 文档](https://developers.cloudflare.com/workers/runtime-apis/response/)。

gateway 现将已验证的流传入原生 `FixedLengthStream` 输出；最后一块数据仍在摘要验证后放行，损坏/短读继续失败，客户端严格长度/hash 规则未放宽。已增加正确/损坏 framing 回归，原生 workerd 核对真实响应长度。共享源修复目前仅部署 staging；旧 PoC 部署仍保留先前代码，后续有意重新部署时才更新。

最终 staging build 为 `staging-20261004-v2`，操作者上传 secret 后的修复部署返回 version `a8bdaf64-ce8c-48a4-bd33-d970e1b2b8d8`。

## 验证证据

- Worker 117 项测试通过，覆盖公开入口守卫/限流失败与成功、framing/流失败及既有缓存恢复。
- 原生 workerd 通过匿名 staging、实际限流绑定、真实 `Content-Length`，并回归 SQLite/R2/内部 RPC/恢复。
- 公开 Python 九项无代理断言通过：stable/published/catalog、两次回源 ZIP、304、Range 416、query 400。ZIP 均为 `Content-Length: 4179`，字节完全一致，SHA-256 `0299999c8d6b14f7759e5d5dd06ce922aa520ef08e74bfc2540898cea68cfb11`。
- Worker 过滤审计共 280 条，九个 Python 请求关联通过；Mono 五个响应也逐一核对 request/client 编号及终态。Mono 包请求编号 `a9b44ddd-2e8d-4dce-819d-3b96a1c0721e`。
- 链接实际客户端源码的 net472/Mono 工具全链通过：元数据链、浏览缓存/304、发 HTTP 前取消、ZIP 长度/摘要/三个文件静态校验、只读持有文件与释放清理。无残留 partial、无 Mods 目录写入、未加载下载程序集。退出 0。
- 客户端 500 项断言通过；net472 商店编译零错误/警告并刷新预览包。传输工具 net472/net10.0 均编译通过，保留 NU1900 漏洞源警告；net10.0 本轮仅编译，未用于真实网络验收。
- Python probe 四项回归通过，包括公开模式不读/发送 secret、拒绝缺失包响应长度。

Mono 或编译成功不能替代游戏实测。本轮仅一个网络和 4179 字节包；游戏中途取消、Unity 网络、大 ZIP 内存/CPU、不同运营商/地区和生产预算仍待验证。日志/凭据仅存忽略的私有文件，不提交 Git。有界 tail 捕获完后 timeout 退出 124，与验收命令退出 0 分别记录。

Worker 目录的准确命令：

```sh
npm test
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false timeout 45s npm run test:native
python3 tests/live-probe.test.py
WRANGLER_LOG_PATH=/tmp/phinix-worker-wrangler-logs WRANGLER_SEND_METRICS=false ./node_modules/.bin/wrangler deploy -c wrangler.staging.jsonc
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy python3 tests/live-poc.py --endpoint https://plugins-staging.hunyuan2333.com --directory .wrangler/private-staging-20261004/framed-response --direct --public --origin-only --require-package-length
python3 tests/live-audit.py --directory .wrangler/private-staging-20261004/framed-response
```

Python 公网命令需另行准备私有预期样例，audit 需同时捕获 Wrangler tail；公开模式不读 secret，复测应保留之前失败记录。仓库根目录命令：

```sh
dotnet restore Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --ignore-failed-sources
dotnet build Tests/PluginStoreDownloadCheck/PluginStoreDownloadCheck.csproj --configuration Release --no-restore -m:1
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins-staging.hunyuan2333.com phinix.poc /tmp/phinix-staging-mono-20261004-framed
dotnet run --project Tests/PluginStoreRuntimeTests/PluginStoreRuntimeTests.csproj --configuration Release --no-restore
dotnet build Extensions/PluginStore/Client/PluginStore.Client.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
git diff --check
```

每次运行 live checker 需新的绝对状态目录，复测更换 `/tmp/...` 参数。工具显式禁用代理并保留正常 TLS 校验，无需游戏 DLL 或客户端 bearer token。这是手动执行的公网工具，不纳入自动 CI 或 solution 构建。

## 游戏验证及下一批

使用已重建的 `Output/phinix-plugin-store-preview`，搭配现有宿主；替换 DLL 后重启游戏。在商店来源设置填写 endpoint `https://plugins-staging.hunyuan2333.com`，source `phinix.poc`。

1. 关闭代理/TUN 后在线刷新，应看到 `phinix.poc.marker` 1.0.0 和 `phinix.poc.playtest` 1.1.0 两个条目，无认证/过期错误。
2. 选择条目，查看依赖计划，应无依赖且可下载。
3. 点击“下载并校验 ZIP 包（预览）”，应通过三个文件校验并显示关联编号，不增加已安装模组。
4. 再刷新，取消后重试，任务中离开/返回商店；迟到回调不得覆盖新任务或永久忙碌。小包可能来不及取消，需记录尚未覆盖中途取消。
5. 在线刷新成功后断网，用离线浏览缓存；在线失败应保留旧有效浏览状态且可重试，离线数据不授权下载。

报错时记录操作、页面错误码、request ID 和时间。客户端结构化事件为 `repository.*`、`package.*`，Worker 为 `staging.read_admitted`、`origin.*`、`stream.*`、`request.complete`；完整平台日志保持私有。

游戏反馈后推进 P4 新包 staging/归属/事务日志/恢复及确认/提交前 freshness 复核。生产 R2 需要独立桶/账本和经过审计的操作周期切换，保留占用、预留与 uncertain 状态；不能延长或重置固定 PoC 周期来模拟永久服务。正式发布、签名决策、大文件/CPU/费用和多网络证据仍是独立生产工作。本批交付游戏 staging，尚未交付可放量的生产配置。
