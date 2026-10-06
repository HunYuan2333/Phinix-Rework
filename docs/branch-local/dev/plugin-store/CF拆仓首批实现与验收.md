# CF 拆仓首批实现与验收

[English](CfGatewayExtractionAcceptance.md) · 2026-10-06 · S1 / 网关 M0—M1。

## 本批交付

- 原路径 Worker 143 项回归通过后，固定 46 个原有源文件及两个外部 JSON 的输入白名单/摘要，来源明确为当前未跟踪工作区，不伪造提交历史。
- `tests/helpers.mjs` 与 `tests/localized-display.test.mjs` 改读 Worker 自有 `tests/fixtures/`；两个样例与客户端原件字节一致，`provenance.json` 记录来源/长度/摘要，新增一致性回归。`.gitignore` 补 Python 字节码/缓存，避免运行测试后把生成文件发布出去。
- 独立候选仓库 `/tmp/phinix-plugin-gateway-20261006`，固定提交 `e31bfcce71bdce4317175b0ccff2275432734d7e`，62 个已核对白名单文件。迁入 JS 生产实现、全部 Wrangler 配置逐字节保持；只有测试路径、独立文档/历史归档、CI 和忽略规则变化。
- 候选加入只读 GitHub Actions：固定 action 提交，Node 26.10.0、Python 3.13，干净安装、Node/Python 及原生 workerd 检查；没有部署任务或云凭据。此工作流尚未推送/运行，不能称 GitHub CI 已通过。
- 新仓库 [HunYuan2333/Phinix-Plugin-Gateway](https://github.com/HunYuan2333/Phinix-Plugin-Gateway) 已建立，**当前为空**。API 验证 size=0；Actions 默认 contents read、不能批准 PR。62 文件还未上传。

候选只新增独立运行/部署的必要技术入口，不代做用户留给其他模型的多仓文案重写或 AI 作者 skill。原源码没有根许可证文件，本批未擅自新增许可授权。原历史说明作为文本归档保留，不把旧 PoC 描述当作正式服务现状。

## 已执行验证

原路径 `npm test`：143/143。候选 `npm ci --ignore-scripts --cache /tmp/phinix-gateway-npm-cache --no-audit --no-fund` 从锁文件独立安装成功，没有复用主仓库 node_modules。

候选验证命令：

```sh
cd /tmp/phinix-plugin-gateway-20261006
npm test
python3 tests/ops-cli.test.py
python3 tests/network-check.test.py
python3 tests/live-probe.test.py
WRANGLER_SEND_METRICS=false WRANGLER_LOG_PATH=/tmp/phinix-gateway-candidate-build.log npm run test:native
git diff --check HEAD
```

结果：Worker 144/144；Python 5+5+4；原生 workerd 的匿名入口、限流、SQLite/R2、内部 RPC 和确认丢失恢复通过。native 命令打包 default/ops/production 三份**dry-run** bundle，不部署云资源；故意损坏流的测试会输出运行时错误栈，最终断言及退出码为成功。

运维 Python 初次在沙箱因 localhost socket `EPERM` 失败；在沙箱外临时本地服务器重跑 5/5 通过。原生 harness 同样在允许临时监听的环境运行，不把沙箱权限失败当成业务失败，也不隐去首次失败。

另外已核对：Git 已跟踪文件集合严格等于 61 项内容清单加 manifest，自有源文件逐项 SHA-256 匹配，工作区干净；实际生产代码/配置与导入字节一致；凭据模式/私钥及不应分发的 DLL/ZIP/日志均未进入提交；新入口及运维文档本地链接可解析。这些检查不是通用安全审计或游戏验收。

## 审阅和保留

- 文件/来源/摘要：`/tmp/phinix-plugin-gateway-20261006/migration-manifest.json`。
- 部署边界：候选 `DEPLOYMENT.md` / `DEPLOYMENT.zh-CN.md`；复用现有登录及线上 secret，不导出凭据。
- 持久本地副本：主仓库已忽略的 `Output/gateway-migration-20261006/gateway-source-e31bfcc.tar` 与 `.bundle`。它们不在 `Output/phinix-rework`，不是游戏发行物；tar 对应固定 HEAD，bundle 保留可恢复提交。
- 原输入记录：`/tmp/phinix-gateway-input-before-migration.json`，SHA-256 `54e7badfda94cce1b97c3abad35b6b80d426ba0b13af63490c1695c50fdb1969`。

## 公开范围、后续门槛

首次远端创建命令被自动审批错误判断为同时推送源码而拒绝。核对 `gh repo create --help` 后，证明未指定 `--push` 不上传提交；同一创建命令带该证据复审通过，空仓库建立。**审查同时明确：候选未公开源码、云定位配置和运维历史的公开发布仍需具体授权。** 本批没有尝试源码推送，也没有绕过该要求。

下一步取得这批 62 文件的公开推送/只读 CI 授权后，推送固定提交并核对远端 CI；随后按 M2/M3 配置唯一发布归属、从新仓库复用现有 Worker 发布并进行协议/下载/审计与必要游戏冒烟。新入口和回滚验收前，不删除主仓库 Worker，不关闭现有可用部署路径。

当前没有新增/修改线上 Worker 版本、路由、绑定、token 或玩家记录；不需要因本批离线迁出重装 Mod 或立即游戏测试。正式新域名的人测缺口仍按 S0 单独记录。旧 PoC Worker/R2/DO/secret 永久删除仍待另行具体批准，不能由源码公开授权代替。

职责及兼容风险：源身份和格式未变，存档、物品、ACK 不受本批改动影响；公开配置包含账号/域名定位信息，公开前须明确审阅；正式接管后的版本、路由及真实传输尚未验收。不把本批 M0/M1 验证说成整个 S1 或商店拆包已经完成。
