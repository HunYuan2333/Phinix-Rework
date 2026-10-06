# CF 独立发布接管与验收

[English](CfGatewayTakeoverAcceptance.md) · 2026-10-06 · S1 / M1—M4 完成记录。

## 交付与实际线上状态

- 用户明确授权的 62 文件、固定提交 `e31bfcce71bdce4317175b0ccff2275432734d7e` 已公开推送到 [Phinix-Plugin-Gateway](https://github.com/HunYuan2333/Phinix-Plugin-Gateway)。
- 首轮 CI 在工作流解析时失败，未运行测试。原因是 job env 不能使用 runner context，按 [GitHub 上下文表](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts)改为固定临时日志路径；修复提交 `92aea4808c794b4a52aaa21d14830feac18d7076`，生产代码/配置不变。
- [远端 CI 37416583678](https://github.com/HunYuan2333/Phinix-Plugin-Gateway/actions/runs/37416583678)全部通过：独立安装、144 Worker 测试、Python 5+5+4、原生 workerd/SQLite/R2/RPC/匿名限流。本次初始失败没有隐藏或冒充成功。
- Actions 默认只读、不批准 PR；main 要求 GitHub Actions app 15368 的 `check`，禁止强推/删除。维护者管理员仍有管理例外；没有 CI 云部署任务或 CF token 放入 GitHub。
- 已从新仓库的干净固定提交接管**同一个** `phinix-plugin-repository-staging`。版本 `90d4921e-7704-4296-a07a-198046e96a76`，BUILD_ID `gateway-92aea4808c79`，上线前版本 `7da31ea5-902c-4105-a460-ed459ef4d183` 留作已核实回滚点。
- 正式及临时别名域名、phinix.official 身份、GITHUB_TOKEN secret、限流 namespace/参数保持；无 R2/DO/服务管理绑定。生产与接管 dry-run bundle 完全一致：47745 字节，SHA-256 `4f59c5e11547d45e3223883df2761a6e047b89b5b8c4039261af4ea747a5c1b0`。本次 CLI 只额外覆盖日志 BUILD_ID，不改业务代码。

## 真实验收

已核对 Cloudflare 两条域名、绑定类型、secret **名称**、公开 vars 和旧 PoC 的公开入口关闭状态，没有读取 secret 明文。前后证据保存在忽略的 `Output/gateway-migration-20261006/cloud-preflight.json` / `cloud-postflight.json`，私有文件权限 0600。

实际 net472/Mono→CF 和 .NET→GitHub 均在禁用代理的隔离状态目录下载通过：metadata/cache、预取消、下载前后 fresh 核对、ZIP/PE、临时文件清理；没有安装或执行下载的 DLL。

两端同一目录快照 `2514837d84f27475c17cbb5daa5464ebd0f58339`，Example 1.0.2 ZIP SHA-256 `b0c8d5f9dc7674f355f2c30fd05967dbfdbd5aca3b7be48949293bdfc2139a80`，目录 SHA-256 `65b18df38737a142353719f10f13f45e56eaa651070f45bbc4f1695f70f961eb`。

13 项附加请求覆盖：formal/alias stable、304、published/catalog 摘要链、三个保留版本的 ZIP/CLR 静态验证、公开管理/未知源/Range/错误方法/query 拒绝。255 条服务器结构化审计关联到全部 13 个 response requestId，全部来自新 BUILD_ID，每个请求均有 `request.complete`，三个包都有 `stream.verified`。原始 tail 留在私有临时文件，未公开上传；摘要存于忽略的 `audit-postflight.json`，tail 已停止。

用户随后明确反馈：CF 刷新、切回 GitHub 目录一致，Example Tab/计数/设置**全部正常**。仅覆盖本轮增量检查，不扩展到全部网络、平台、大包或官方两插件业务验收。

## 主仓库撤出与可恢复性

公开、远端 CI、真实传输及人工反馈通过后，逐文件核对主仓库旧 Worker：52 文件全部匹配导入/已知迁移摘要，先做并读取校验完整的 `main-worker-before-removal.tar`。仅删除 49 个已核实迁出文件，保留双语入口与 `.gitignore`；未知文件没有删除，忽略的依赖/本地运行时状态也未清理。

`Extensions/PluginStore/RepositoryWorker` 现在只是指向新仓库的入口，不再有维护中的源码/部署配置。`.dockerignore` 显式排除该目录，旧本地依赖不再进入服务端构建上下文。未提交主工作区的其他既有改动，未改客户端项目、模块或 DLL 复制路径。

原源码备份 SHA-256 `f5ef7f6b13d3b9c65336e2cbd22470f855c235d99bfe8f921e495386bb35ee89`，具体文件清单 `main-removal-inventory.json`。首次源 tar/bundle 仍保留。备份都在 `Output/gateway-migration-20261006`，不在游戏包目录中；不是完整云 DO 账本备份。

## 实际命令与回滚边界

```sh
cd /tmp/phinix-plugin-gateway-20261006
git push -u origin main
# 修复 workflow 后再推送普通提交；远端 CI 全部通过后接管。
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-takeover-dry-run.log ./node_modules/.bin/wrangler deploy -c wrangler.production.jsonc --var BUILD_ID:gateway-92aea4808c79 --dry-run --outdir .wrangler/takeover-dry-run
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-takeover-deploy.log ./node_modules/.bin/wrangler deploy -c wrangler.production.jsonc --var BUILD_ID:gateway-92aea4808c79 --strict
cd /home/hunyuan2333/Phinix/Phinix-Rework
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy mono Tests/PluginStoreDownloadCheck/bin/Release/net472/PluginStoreDownloadCheck.exe https://plugins.hunyuan2333.com phinix.official /tmp/phinix-gateway-live-cf-20261006 --official-cf
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY -u http_proxy -u https_proxy -u all_proxy dotnet Tests/PluginStoreDownloadCheck/bin/Release/net10.0/PluginStoreDownloadCheck.dll https://api.github.com phinix.official /tmp/phinix-gateway-live-github-20261006 --official-github
python3 /tmp/phinix-gateway-protocol-check.py
dotnet build Server/Server.csproj --configuration Release --no-restore -p:BuildInParallel=false -m:1
```

Server 构建通过：6 个已有 protobuf trim/NuGet 漏洞源不可达警告，0 错误。没有声称执行 Docker 镜像构建、完整客户端重编译或重跑全部游戏用例；本轮没有改游戏打包行为。

本机未安装 PowerShell，未执行 `.github/scripts/check-artifacts.ps1 -IncludeClient`；按该脚本做等价 Python 检查，26 个路径匹配、加载目录及服务端/现有客户端产物的游戏 DLL 排除通过。另核对旧目录没有源码/package/Wrangler 配置，Docker 排除规则和双语证据链接通过。

已确认可用的旧正式版本在平台版本列表中，下面是**未执行**的故障回滚命令：

```sh
cd /tmp/phinix-plugin-gateway-20261006
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-rollback.log ./node_modules/.bin/wrangler rollback 7da31ea5-902c-4105-a460-ed459ef4d183 -c wrangler.production.jsonc -m "Restore verified production version"
```

回滚后重新核对路由/绑定/真实下载，不能把准备好命令说成云回滚演练成功。源撤出可由本地 tar/bundle/公开仓库恢复；不回退目录或玩家数据。

旧 PoC Worker/R2/DO/secret **没有删除**，具体退役授权仍独立待办，不属于这次源码公开授权。临时域名别名继续保留。S1 可以收口，下一批进入 S2 资源/存档前置；红包和人才仍随主体，本轮没有完成它们的独立发行。
