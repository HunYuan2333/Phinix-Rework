# P4：按可信服务器持久化新凭据（2026-11-01）

> **当前决策（2026-11-01）：TCP＋TLS 已实现，但属于实验性功能，不建议使用。** 默认交付继续采用 UDP / ClientKey。取消本轮现代身份切换及旧登录退役，后续部署、客户端实机验收、证书生命周期和迁移切换工程列为 TODO。已有实验代码和自动化证据保留，不代表正式支持；本文后续启用/迁移步骤仅作实验与历史设计参考，不是部署要求。不得自动启用或在 TLS 认证失败后降级。详见服务端 `docs/Server-Modern-Identity-TODO.md`。


状态：SV-45 本地文件与 pending operation 组件实现，SV-41 客户端存储子集；关联 SV-43/44 的“先保存再请求”前置与 TQ-40。尚未接入新的注册/认证网络入口或生产启动路径，不代表 P4 整体完成。

## 分支和依赖

仅修改 `codex/p3-trade-business` 的客户端身份边界、独立测试和本文档；Server dev 只追加 RoadMap 记录。客户端 HEAD `53c92ce8eb314dcb8ca422ae4b6891a007931128` 加既有未提交修改，Server HEAD `5a0508acb703319e60dd5600e3e0facd0397b294`。Common/protobuf 固定 gitlink 不变；不改工坊/UI 分支，不合并交易取消 hotfix。

实现位于 `Client/Common/Authentication.Client/ServerIdentityCredentials.cs`。通过 `PHINIX_RIMWORLD_17` 只进入既定 .NET 10 / 1.7 现代客户端程序集；1.6 不增加此 API 或新登录 fallback。依赖均来自 .NET 10 内置程序集，没有新 NuGet 包。既有旧 ClientKey 文件与认证流程未改动。

## 行为与不变量

- 调用者须先验证 TLS 信任并收到该连接的服务器身份，才能构造存储绑定。`TrustedIdentityServer` 本身不是握手凭证。文件按稳定 ServerId 定位，内容同时绑定规范化服务器名及预先可信 SPKI pin；相同 ServerId 的不同名称或 pin 均拒绝读取，不能把秘密交给另一服务器。合法证书换钥仍需后续显式连续信任迁移，不自动创建新玩家。
- `CreatePending` 是显式首次登记入口，生成独立的 256-bit CSPRNG secret、credentialId、operationId。只有文件安全提交后才交还 material。客户端后续必须先调用此入口成功，再发请求；这个组件不发送网络消息。
- `Load` 遇到缺失、坏文件、版本不符、信任绑定变化或权限异常一律拒绝，不能调用 CreatePending 作为恢复。重启应回读原 operation/credential/secret，结果未知不生成第二个身份。
- `ExpectedPrincipalId` 为可选的已授权稳定身份，不接收旧 token 或 Legacy 登录规则。后续迁移 coordinator 可先保存预期原 UUID；`Confirm` 只接受匹配 operation 和主体的权威响应，保留原 UUID 字符串，不靠显示名认领。匹配的确认重放不重写文件；冲突和取消不改原文件。
- 文件为固定 magic + SHA256 + 有界 JSON，版本 1、上限 8192 bytes。校验和用于发现意外损坏，不是防止本机攻击者篡改的签名或加密。所有必要字段、未知字段、重复字段、UUID 与秘密长度均校验。秘密不进入诊断、文件名或测试结果，snapshot 的 ToString 脱敏；复制秘密返回独立数组，Dispose 清零内部数组。
- 同目录唯一临时文件、Flush(true)、rename 提交，禁止 copy/delete 覆盖权威文件。保留上次有效文件直到替换完成；失效临时文件不作为权威数据自动回读。跨实例/进程通过持久 sidecar 文件的独占打开协调，锁文件不在释放时删除，避免锁 inode 替换竞态。
- Linux 新文件 0600，目录必须已经存在且私有；Windows 检查所有者和 DACL，拒绝 null DACL 或向非当前用户/System/Administrators 放开读写和权限变更。文件、锁和所有父路径检查 ReparsePoint，覆盖 Linux symlink，Windows Junction 等需实机验证。不会为了兼容放开链接。
- 这些路径检查不能声称抵御同权限恶意进程在检查与使用之间主动替换路径；目录权限与协作锁是本切片的边界。未引入原生目录句柄/O_NOFOLLOW 全链沙箱。用户秘密仍必须保存在用户数据目录；未来宿主配置目录的创建与验证尚未接入，不在工坊安装目录保存。

`File.Move` 选择同目录提交，依据 [.NET 文件移动说明](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move) 避免跨卷复制路径；显式调用 [Flush(true)](https://source.dot.net/System.Private.CoreLib/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/FileStream.cs.html) 刷新文件。没有目录 fsync、硬件断电或所有文件系统的耐久性承诺；本轮强杀验证是进程级故障，不能替代系统断电测试。

## 测试与证据

`Tests/ClientIdentityCredentialTests` 保持控制台入口；`tools/quality/run-client-identity-tests.py` 可编译实际源码，或用 `--authentication-assembly` 对明确的产品 DLL 重跑同一测试。故障点构造器仅 internal，测试通过反射设置 barrier，不读取配置或接受网络注入。

覆盖首次保存/重启/确认重放、UUID/操作冲突、跨服务器与信任 pin/name 拒绝、可变数组隔离、缺失/截断/校验和/JSON/重复字段/缺字段/版本异常、取消不破坏原文件、Unix 权限与路径链接、真实跨进程锁，以及：

| 稳定 faultId | 独立进程重启后的预期 |
| --- | --- |
| identity.client.create.kill-after-flush | 无权威文件，未提交 tmp 不是已保存凭据，Load 明确失败 |
| identity.client.create.kill-after-commit | 原 pending operation 和秘密可回读，不假装已确认 |
| identity.client.confirm.kill-after-flush | 原 pending 文件逐字保留，未确认 |
| identity.client.confirm.kill-after-commit | 完整确认文件可读，原 operation 不变 |

每个测试进程只处理合成身份与独立临时目录；到达 barrier 后由父进程强杀，等待退出再检查文件。JSON、magic 与 checksum 另用独立读法核对；没有把模拟成功当作持久提交。固定调度由 barrier 决定，无随机 seed；随机秘密不输出或复制为证据。父进程验证持锁期间另一个实例无法读取/写入。

最终命令、构建/测试结果、源文件与包内 DLL hash 保存在工作区 `Deliverables/Phinix-Rework-P4-Client-Credentials-2026-11-01/`，以最终 manifest 为准。该目录不包含测试生成的秘密、游戏 DLL、玩家文件或运行时私钥。

## 仍需验证与下一依赖

Linux/.NET 10 的文件与强杀测试不能证明 Windows 原生 ACL/Junction、Waylith 或真实联机通过。Windows 可在源码测试副本执行：

```powershell
dotnet build Tests/ClientIdentityCredentialTests/ClientIdentityCredentialTests.csproj -c Release -p:NuGetAudit=false
dotnet Tests/ClientIdentityCredentialTests/bin/Release/net10.0/ClientIdentityCredentialTests.dll
```

测试仅使用其自动建立的临时目录。确认 Windows ACL 分支通过；另用合成专用目录建立 Junction、目录 symlink、dangling link，确认读取/创建/确认均拒绝且外部 sentinel 文件逐字不变。普通用户无创建链接权限时须记录未验证，不能标通过。不得指向玩家或现有凭据目录。

下一步联合接入 SV-42 新安全认证入口/注册预算、客户端用户数据路径与先持久再发送协调器；然后实现 SV-43/44 原 UUID 的受信任 Legacy identity mapping、重复请求与丢响应恢复。凭据轮换候选的客户端持久协议、信任换钥和可选备份策略仍待；不自动删除旧凭据或依赖显示名找回。

## 本轮最终执行结果

- 1.6/net472 和 1.7/net10 完整客户端图、三个维护中插件消费者构建通过；包审计 **95 文件**通过。既有 generated/obsolete 等构建警告保留，未改 vendored 源码消警告。
- 包内 `07-Authentication.Client.dll` 与实际测试 DLL 的 SHA256 一致：`09c726a2f0e1845ca58e81845a6652d00b1ec8d4c1e6e24885a9f915df548a89`。同一最终 DLL 的 **53 项检查**通过，含四个真实进程强杀点和持锁冲突；宿主启动 **10 项**通过。
- 最终源码 SHA256 与双版构建图的输入指纹一致；`git diff --check`（允许既有 CRLF）通过。没有重新运行 Server 全套，因此不把上一切片 Server stage 通过当成本轮新执行结果。
- 最终业务分支 ZIP：`Phinix-Rework-P4-Client-Credentials-20261101-1.6-1.7.zip`，SHA256 `8cdac64953b8df56ffc646c68e14c432a2903e77b69af95ed1615f48ce1b3ff0`。此包包含既有 P3 业务分支改动，新增现代凭据组件尚未启用；工坊/UI 分支仍独立。

实际执行的最终命令（运行环境设置 DOTNET_CLI_HOME 为隔离目录、NUGET_PACKAGES 为已有固定依赖的隔离缓存视图，未更改 SDK pin）：

```sh
python3 tools/compatibility/build-dual-host.py --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-p4-client-identity-20261101-final
python3 tools/quality/run-client-identity-tests.py --output /tmp/phinix-p4-identity-package-tests-20261101 --authentication-assembly /tmp/phinix-p4-client-identity-20261101-final/build17/infrastructure/Authentication.Client.dll
python3 tools/quality/run-client-component-probes.py --suite host-startup --assemblies /tmp/phinix-p4-client-identity-20261101-final/build17/infrastructure --game /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-p4-identity-host-startup-20261101
```

最初直接还原在受限网络环境失败（NU1301），随后使用本地固定包和隔离缓存视图完成。早期源码试验与中间候选不是最终通过依据；最终以包内实际 DLL 回归与交付 manifest 为准。未部署、未发布、未跨分支合并或修改玩家数据。


## 同日 P4 最终收口补记

后续实现已接通新的现代身份入口：版本2文件保留旧v1读取、原UUID绑定、轮换候选/撤销意图和原结果核对；增加 expected / trust authority markers，缺失/变更不可自动新建身份。已确认现代身份不依赖旧凭据文件，绝对用户路径不可用时明确失败。Frame admission 随受信任会话替换更新，现代协商超时拒绝降级；单调 deadline 阻止旧 Timer 回调误超时。前文“未接入”“格式v1”及原测试包仅是当时状态，以此补记和服务端 docs/Server-Upgrade-P4-Closeout-2026-11-01.md 为最新依据。

RoadMap §14 的 P4 产品与必要自动化门槛已满足：Server stage43/61文件，身份74/TLS宿主60，最终包内客户端SDK与独立Server.Main联合99（四种服务端提交后强杀），文件67（四个文件提交前后强杀），双版Trade各320，过渡TLS11/宿主10，双版完整构建/三个维护消费者/95文件包审计。原UUID、真实聊天回复/艾特和空报价快照有网络证据；不计作真实游戏资产、Windows ACL/Junction 或 Waylith 验收。

唯一最终配套候选与逐项SHA见 Deliverables/Phinix-Rework-P4-Closeout-2026-11-01/manifest.json；前文早期包不作为本次最终候选。P3业务分支继续与工坊/UI分离，取消hotfix未合并，无部署或发布。下一步P5/P6另按RoadMap推进。
