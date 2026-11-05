# P3 交付尝试证据与当前游戏实例边界（2026-10-21）

用户报告 2026-10-18 报价能力标识修复验证通过并授权继续。按该回复记录为人工缺陷复测通过；未提供双边存读档/断线/重启明细，不扩大为 P3/P6 或资产守恒验收。

关联 SV-31/32、TQ-30/31 子集。复用已有 immutable completion receipt，不重写接收数据：调用 Inventory/Direct API 前先发布 `.attempt`，返回后追加 `.result`。每个标记关联原 receipt fingerprint，私有文件 flush 后原子发布、不覆盖；只接受 Received → Attempted → ReturnedSuccess/ReturnedFailure。这些是本地调用证据，后两者都不是游戏保存回执。异常/强杀停在 Attempted，不能据此退款、补投或切换方式。

正式交付 owner 共享 service 的同一 inbox。每个 completion 在首次主线程处理时捕获当前 Game 实例、接收 UUID 和 endpoint，后续检查永久失效；无游戏时的非空批次不能等到另一个存档再交付。空批次不要求游戏实例，仍校验身份与记录。此边界不是稳定存档 ID，也不证明事件入队到首次主线程处理之间没有切换游戏；跨重启绑定仍待实现。endpoint 是路由事实，不是可信服务器身份。

已经写入尝试后，AvailabilityChanged、设置变化、重复处理或重开日志都不自动再次调用交付。即便 API 返回失败，本轮仍保守保留待核对；尚未将各类明确 NotInvoked 结果分类以恢复安全重试。记录写入失败发生在交付前则保留 Received。状态或 hash 损坏、孤立结果记录、方式/终态冲突明确拒绝。旧 schema1 receipt 无标记时只显示 Received，不推断历史是否物化，也不自动恢复。未来启用恢复前必须完成历史来源与存档核对。

未新增依赖、公共协议、公开 SDK、Common/gitlink/vendor 或宿主业务分支；仅客户端 Trade 产品/测试与必要工程 compile 列表改变。既有 ACK 链只在当前交付 API 成功返回且证据写入后继续，本轮没有升级为现代 durable game-save ACK。服务端产品不需部署。

## 实际检查

- 真实文件测试 48 项通过（既有 21 项加本轮尝试/冲突/并发/损坏/方式切换/当前实例与身份/强杀测试）。faultId `delivery-attempt-held`：子进程写入 Attempted 后 barrier，父进程强杀，重开不得变为成功或获得第二次交付许可。另保留原 `committed-receipt-held` 与 final-path-is-directory 受控异常。无随机 seed；全部固定调度/合成数据。
- 两个独立 Python oracle 通过：按字面检查 exact byte=23、主体、receipt/attempt hash 关联和不存在 `.result`；不借助产品 Reader 生成预期。无真实玩家数据。
- 实际 1.7 产品 DLL 的组件入口 227 断言通过：真实 owner 的坏 codec 返回失败持久可读、再次触发不 ACK；空批次、activation/cleanup 和旧控制台入口保持可执行。只有外部 transport/game 端口隔离，无游戏物化声明。
- 最终冻结源码的完整 1.6/net472、1.7/net10 构建及维护消费者引用、94 文件包审计通过（既有编译器警告）。编译游戏引用仍为 1.7.4977-rev1248；真实 1.7.4981 执行待验收。
- 初始完整构建在空批次与测试调整前结束，只保留过程日志，不作为最终候选。组件探针第一次以 1.6 DLL 在 net10 缺少依赖，随后目录混存 1.6/1.7 引用导致 Texture2D 同名不同归属；这些失败不计通过。最终专用 1.7 引用目录删除旧版本候选引用并干净重编后 227 断言通过，没有跳过断言或改错误预期。

实际命令使用 `DOTNET_CLI_HOME=/tmp/phinix-dotnet-113 NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks`：

```sh
dotnet build Tests/TradeCompletionInboxTests -m:1 -p:BuildInParallel=false -p:NuGetAudit=false --no-restore -v:q
PHINIX_ATTEMPT_ORACLE=/tmp/phinix-attempt-final-oracle-20261021 dotnet Tests/TradeCompletionInboxTests/bin/Debug/net10.0/TradeCompletionInboxTests.dll --oracle /tmp/phinix-receipt-final-oracle-20261021.receipt
python3 Tests/TradeCompletionInboxTests/attempt_oracle.py /tmp/phinix-attempt-final-oracle-20261021
python3 Tests/TradeCompletionInboxTests/oracle.py /tmp/phinix-receipt-final-oracle-20261021.receipt
python3 tools/compatibility/build-dual-host.py --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-attempt-final-dual-20261021
dotnet build /tmp/phinix-attempt-components-20261021/Tests.csproj -c Release -v:q
dotnet /tmp/phinix-attempt-components-20261021/bin/Release/net10.0/TradeCompositionRuntimeTests.dll
```

临时组件工程只编译原控制台测试源码，引用最终 1.7 包与 compile-only 游戏 DLL；未改产品依赖或把游戏 DLL 带入分发 ZIP。工程、测试/构建日志、oracle、source/input hash、base SHA 与固定 gitlink 见 `/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-Delivery-Attempt-2026-10-21/manifest.json`。内部候选包含工作区现有 UI 源码，用于构建证据，尚未建议安装或发布。

下一前置仍是跨重启存档/主体绑定、只读待核对界面与权威结果查询，再实现安全恢复和 durable receipt。Received/Attempted/ReturnedSuccess/ReturnedFailure 均不授予自动物化；未执行新网络、实际游戏、Windows ACL、掉电/磁盘损坏或完整 P6。不部署云端、不安装游戏、不发布工坊、不推送。
