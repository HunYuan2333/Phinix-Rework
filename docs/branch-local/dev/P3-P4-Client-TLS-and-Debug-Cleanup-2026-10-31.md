# P3 / P4：客户端TLS接入与临时日志清理（2026-10-31）

> **当前决策（2026-11-01）：TCP＋TLS 已实现，但属于实验性功能，不建议使用。** 默认交付继续采用 UDP / ClientKey。取消本轮现代身份切换及旧登录退役，后续部署、客户端实机验收、证书生命周期和迁移切换工程列为 TODO。已有实验代码和自动化证据保留，不代表正式支持；本文后续启用/迁移步骤仅作实验与历史设计参考，不是部署要求。不得自动启用或在 TLS 认证失败后降级。详见服务端 `docs/Server-Modern-Identity-TODO.md`。


状态：完成本轮垂直切片；P4整体未完成，P3真实资产验收及明确延期项不冒充完成。

## 范围、依赖与不变量

Client仅`codex/p3-trade-business`，HEAD53c92ce加既有未提交工作；Server dev HEAD5a0508ac加既有工作。普通UI/workshop分支未合入业务代码。Common/protobuf gitlink、SDK pin不变。关联P4 SV-40/TQ-40/FR-10传输/信任子集；P3 SV-33/38及用户发布前临时debug日志清理待办。SV-41～45尚未实施。

客户端1.7构建新增对Server仓库中纯.NET中立`Server/ServerTransport`源码项目的条件依赖，不引用Server业务/认证/插件程序集。生成net10图保留该项目ImplicitUsings，打包加入唯一明确批准的1.7专用ServerTransport依赖闭包；1.6目标不引用该项目。联合版本以本次源码hash/DLL清单为准，不将一仓编译当组合兼容。源码构建1.7需服务端仓库位于现有同级Phinix-Rework-Server；实际玩家包已含所需DLL，无需源码仓库或手工私钥。

- IClientTransport与ConfiguredClientTransport：认证、用户、Framework、原始模块adapter以及Host共用同一所选transport；每次显式Connect只创建一个后端，配置/信任/网络失败不尝试另一协议。处理器和事件在重连时重新关联；关闭超时保留旧后端所有权，不将尚未停止的资源直接替换。
- 1.7 TlsNetClient在连接成功、链/名称/用途/有效期/SPKI与ALPN验证完成之后才启动模块读取和认证处理。生产Factory默认在线吊销检查；合成CA没有CRL，联合探针只在测试代码中显式NoCheck，不暴露玩家设置来关闭检查。
- 信任来自预先配置的serverName/SPKI pin及可选根证书文件；不从未可信网络学习pin/root。CA只读，读取有64KiB上限；不保存私钥，不写入用户包、日志或制品。
- 所有认证、会话、用户、Framework与第三方模块使用同一TLS packet body与真实既有序列化器。每连接出站16条/4MiB，完整帧1MiB；本地队列接管不是远端/业务/游戏成功ACK。断开或异常不重发资产、不退款、不修改P3权威证据。
- 客户端5秒中立ping/服务端pong维持30秒帧读取deadline内的空闲连接。控制只在transport处理，保留$transport.命名空间，不进入Chat/Trade或其他领域。异常控制/非法载荷不伪造业务成功。
- 应用模块回调异常记录类型摘要、继续其他模块；损坏帧/认证信任/传输异常关闭连接。初版错误地把单个插件异常传播为整连接关闭，新增独立回归在修复前实际DLL上失败，修复后通过。
- 1.6缺少已验证TLS适配，选择TLS明确PlatformNotSupported，禁止回退UDP。默认udp保持现有连接体验。本轮未更改UI布局或窗口逻辑。

## 配置边界

沿用现有ModSettings扩展配置存储，仅增加中立读取键，无新UI：

| key | 作用 |
|---|---|
| transport.mode | 缺省udp；tls为明确可选模式；其他值拒绝 |
| transport.tls.serverName | TLS验证目标名称，缺省本次连接地址；证书需匹配 |
| transport.tls.spkiPin | 预先可信的32字节SPKI SHA256的Base64，不可从失败连接自动获取 |
| transport.tls.rootCertificateFile | 可选预先可信PEM根CA绝对/管理员配置路径；空表示系统根 |

客户端在Connect时读取配置，错误由既有连接失败提示处理，不在Mod构造时把ServerTab炸掉。只有配套TLS服务端候选/端口及可信配置已准备时才能切tls；不能直接把已部署P3 UDP端口当成TLS端点。本轮未部署云端，现有开发服保持UDP。旧凭据文件保留；新版本仍以过渡旧身份包登录，不宣称已实现按可信服务器分离新随机凭据或保UUID自动迁移。

## P3临时日志

用SDK Roslyn语法树定位并移除交易窗口、FrameworkClientTradeServiceAdapter、PhinixFrameworkTradeClientService共16处逐操作DEBUG表达式（包含报价/确认/token/列表过滤/普通响应trace）。保留失败、异常、冲突、未知/待核对与拒绝摘要；未修改布局/业务决策、intent/result/receipt/outbox/checkpoint文件，不删除玩家journal。持久操作证据不是本项“临时操作日志”。

## 实际验证

- 双版完整构建、三个维护插件消费者、95文件包审计通过；1.7新增一个中立ServerTransport.dll，1.6不带它。
- 最终包1.6/1.7 Trade组件各320项（原315+5项transport选择/无回退/未连接发送拒绝/Dispose检查），1.7 wire34、库存保存回调33、恢复队列35通过。
- 最终包真实ClientAuthenticator/ClientUserManager/Configured+TlsNetClient与真实TLS ServerAuthenticator/UserManager/Framework组件联合：11项通过。错误pin/主机名/CA各只尝试一次且Server credential数仍0；正常自动ClientKey登录、opaque模块往返、Framework协商、插件异常不影响下一模块、真实32秒空闲、断线重连同UUID/同凭据，以及未知模式拒绝。
- 新插件异常用旧实际DLL负回归失败，错误为“One plugin handler error disconnected unrelated modules”；未改成接受错误行为。首轮默认沙箱socket13失败，后在获准回环网络执行相同合成测试。首次开发构建的路径错误及后续空白行检查失败已纠正；最终构建/测试输入hash另存，不用过期候选充当最终证据。
- Server keepalive改动后的隔离stage40项/61发布文件审计通过，包括原UDP、真实SQLite及P3独立进程恢复；此轮TLS联合为单进程真实网络L2，不能称独立游戏L3或到账证明。

主要命令及全部参数保存在交付manifest：build-dual-host.py（最终输出/tmp/phinix-p3-p4-dual-release-20261031）；run-client-component-probes.py；run-client-tls-probe.py（显式客户端实际包/服务端DLL）；Server tools/quality/check.py --suite stage --isolated（/tmp/phinix-p4-keepalive-stage-20261031）。SDK10.0.113/runtime10.0.13；TLS profile phinix-stream/1，旧UDP wire回归为FrameworkV2合成profile；TLS payload固定[17,0,255]/[42]，合成CA/身份CSPRNG不输出秘密。无新增可由客户端触发的故障开关；固定FsCheck seed及既有faultIds随Server stage manifest记录。

## 尚未验收/下一依赖

真实Windows/Waylith与1.6 Mono未运行；引用1.7.4977-rev1248和1.6 GameDlls的构建/组件执行不等于用户1.7.4981实际游戏通过。用户存档/库存和云端数据未动，未发布工坊或独立插件、未合并/push分支、未部署服务。

P4下一项：SV-41 principal/凭据分离、按可信服务器存储、服务端事务绑定和可撤销/轮换；继而SV-42～44自动注册/旧身份保UUID幂等迁移。信任配置分发/轮换、实际目标平台联合验收仍需证据；不先对玩家迁移秘密。

P3本轮仅关闭明确的临时日志待办；终态证据安全冷归档仍为已延期项，Unknown/原结果不能TTL删除。未绑定历史交付仍待核对，不自动换存档重新物化。真实双方库存/存读档/完成取消断线验收继续按原清单保留，不因上述自动化标通过。


## 2026-11-01 交付路径补记

10/31临时目录被环境清理，原文的10/31交付目录未完成落盘；不将丢失文件视为仍可下载。11/01重新完整构建并执行同一范围验证，已立即保存到`Deliverables/Phinix-Rework-P3-P4-Client-TLS-2026-11-01/`。ZIP SHA256 `4e8e2976d7f6e7e383ba82284bdbb093b42b6deecf0cc5e36d2a16d0655e1500`，双版95文件、Trade各320、wire34/保存33/恢复35、TLS联合11项通过。服务端另推进SV-41身份持久组件69项，未开放新网络身份入口/迁移玩家；详见Server 11/01记录。
