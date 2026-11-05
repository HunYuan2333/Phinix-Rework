# P3 客户端持久接收日志（2026-10-18）

关联 SV-31/32 客户端子集、TQ-30/31。只完成“接收原始事件→先落盘→发布到既有交付 owner→重开可核对”，不等于待交付自动恢复、库存/存档到账证明或现代 durable receipt。

## 实现与边界

- `TradeCompletionInbox` 在用户 SaveData/Phinix/ExtensionData/builtin.trade/completion-inbox 中保存版本化记录，包含接收者、实际远端 endpoint、交易 ID、成交/取消、对方 UUID 与完整 opaque 物品批次。不写 session、凭据或 ClientKey；不尝试物化/过滤未知 codec。
- 按 endpoint/recipient/trade/terminal 分类，UUID/交易 ID 路径键大小写归一；精确重放不再次发布业务事件，内容冲突拒绝。同一接收者和交易换 endpoint 或出现矛盾终态也拒绝，不授予第二次交付。endpoint 只是当前路由边界，不是 P4 的可信服务器身份；地址变更须核对，不能自动迁移。
- 私有临时文件 Flush(true) 后同目录 rename，已提交文件不覆盖；内容 SHA256 和文件名身份校验。单项 4 MiB、已提交日志 32 MiB/4096 项，不淘汰旧记录。Linux 测得目录 0700/文件 0600；未作 Windows ACL 承诺。孤立临时文件不当作已提交记录，不自动导入。
- 核心 `CompleteTrade` 在解码、移除本地报价和通知交付前完成写入；写失败/冲突/损坏不会继续发布交付。重启后精确重复也不再发布。正式插件必须取得持久路径与连接身份服务，缺失时激活失败；直接创建的无宿主组件 API 保留以运行现有隔离测试，不授予持久交付承诺。
- Host 只增加对所有插件相同的只读 endpoint 接口与已有路径服务，SDK 固定 Common/gitlink/vendor 未改，无 Trade 专属 Host 分支，无新增包。插件内部没有 Legacy/V2 判断。

## 实际验证

- 源链接实际 inbox、真实文件：21 检查通过。包括重开 opaque bytes、重复、冲突不覆盖、大小写/跨 endpoint/接收者/矛盾终态、坏批次整批拒绝、大小预算、文件权限、内容 hash 损坏拒绝、最终文件路径被目录占用的受控失败/临时文件清理，以及 `committed-receipt-held` barrier 后子进程 SIGKILL。
- 独立 Python `json/hashlib` 对强杀后的实际文件逐字面核对 endpoint/recipient/terminal、原始 byte=23 和内容 hash；未使用产品 ReadAll 作为预期生成器。
- 原组件入口 223 断言通过，包含实际 service 写文件先于交付回调、重复/新 service 实例不再次触发、损坏不进入业务、持久服务缺失拒绝激活与正常生命周期清理。net10 runner 执行 net472 组件是隔离组件证据，存在原有跨目标警告，不是原生游戏运行。
- 最终完整 1.6/net472 与 1.7/net10 构建、维护消费者引用和既有双版包审计通过，包 94 文件。无游戏 DLL、凭据或玩家数据进入候选。未执行新真实网络、原生游戏、Windows 或双边存档检查。
- 两个较早的构建在源码修改期间被 source-fingerprint gate 拒绝，未算通过；冻结产品源码后使用 `verified-dual` 新目录完成最终验证，不跳过 fingerprint gate。

命令使用 `DOTNET_CLI_HOME=/tmp/phinix-dotnet-113 NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks`：

1. `dotnet build Tests/TradeCompletionInboxTests/TradeCompletionInboxTests.csproj -m:1 -p:BuildInParallel=false -p:NuGetAudit=false`
2. `dotnet Tests/TradeCompletionInboxTests/bin/Debug/net10.0/TradeCompletionInboxTests.dll --oracle /tmp/phinix-inbox-oracle-20261018.receipt`
3. `python3 Tests/TradeCompletionInboxTests/oracle.py /tmp/phinix-inbox-oracle-20261018.receipt`
4. `dotnet build Tests/TradeCompositionRuntimeTests/TradeCompositionRuntimeTests.csproj -p:RuntimeTestFramework=net10.0 -p:Configuration=Release -m:1 -p:BuildInParallel=false -p:NuGetAudit=false`
5. `dotnet Tests/TradeCompositionRuntimeTests/bin/Release/net10.0/TradeCompositionRuntimeTests.dll`
6. `python3 tools/compatibility/build-dual-host.py --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-completion-inbox-verified-dual-20261018`

持久证据和内部候选：`/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-Completion-Inbox-2026-10-18/`。manifest 记录 dirty/base SHA、产品/测试源 hash、归档 hash、故障点和未执行范围；源码未提交，不能单用 HEAD SHA 复现。

## 下一步与限制

日志只证明事件被本机持久记录。它没有写入 Applied/游戏保存确认，不可作为成功、退款或自动重投依据；当前没有恢复查询/核对 UI，也没有跨存档授权绑定。原有 live owner 的失败重试逻辑本轮未改造，不能将本轮重复网络事件抑制宣传为物化 exactly-once。

下一切片需要把 received/pending/attempted/已提交库存或未决结果分开，绑定存档与交付主体，并接只读核对界面/恢复查询。存储出错或可能已物化时不自动换目标、退款或重发。旧版本丢失的纯内存队列无法由新日志凭空恢复，需后续查询服务端原结果/权威 outbox 核对。没有承诺断电、硬件损坏零丢失或目录 fsync 的系统级恢复。

本候选仅留作开发验证，未安装到游戏、未部署云端、未推送或发布工坊。P3/P6 保持未完成，不要求用户现在组织人工联测。
