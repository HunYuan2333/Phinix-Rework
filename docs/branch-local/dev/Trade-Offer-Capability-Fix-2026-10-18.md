# 交易物品包缺能力标识修复（2026-10-18）

用户更新双方配套客户端后仍失败。最新游戏日志显示现代协商成功、未注册 outgoing item handler，随后三次报价以 incomplete item batch 拒绝。日志中的 NO handler 本身只表示进入直接发送分支，不等于物品编码失败。

根因证据：实际 adapter 使用通用 BuildItemPacket，后者不填 MessageType；客户端传输只补 Kind/Flow/session，不补业务能力。服务端在进入正式 Trade item handler 和暂存缓存前按 MessageType 检查协商能力，因此空标识被拒绝。此前网络 Harness 手动填写正确 builtin.trade，遗漏真实 adapter 缺陷。服务端继续维持能力和整批引用校验，不以跳过保护解决问题。

关联 SV-11/23/31 与 TQ-20/30 子集；不变量为认证作用域内的实际传输 ID 与报价引用一致、opaque 字节不变、整批接收先于提交请求、提交后结果可恢复。不扩展 P3 接收日志功能。修复只在 Trade 客户端为物品包声明自身协商能力，无新依赖、Common/gitlink/vendor 或服务端产品修改。

## 执行证据

- 新 Tests/TradeOfferWireTests 使用实际包内 adapter、service 和序列化器；仅游戏 encoder/session/transport 外部依赖为合成。对原配套 1.7 DLL 的回归明确失败于独立字面预期 builtin.trade。修复后的最终 ZIP 两版 DLL 各通过 13 项检查：两物品引用、顺序、身份、opaque 字节、correlation、空批次。1.6 DLL 在 .NET 10 组件 runner 中执行，未声称 RimWorld 1.6/Mono 运行通过。
- 1.6/net472 与 1.7/net10 Trade 分别编译通过，各一个既有 obsolete 警告；编译引用基线仍为 1.7.4977-rev1248，用户本次游戏为 1.7.4981 rev136，实际引擎加载仍待复测。
- 真实 UDP：实际包内 adapter 导出单物品 [17,0,255] 和对应报价包；既有独立进程 Harness 不再为该路径手工补 MessageType，只有认证 envelope、动态交易目标与命令 Kind 归一化由测试提供。两个合成客户端向真实正式 Trade/SQLite 提交，强杀宿主后新进程读取报价和原结果、重放原 MessageId，不生成新 cache 代替旧结果；独立 Python 查询持久账本。身份文件保存失败明确拒绝。network-evidence/manifest.json 记录包 fixture、宿主和进程 SHA/命令/退出码。
- 最终 94 文件包审计、CRC、路径及逐字节比较通过；相对于 2026-10-17 配套完整包仅 1.6/1.7 Trade.Client DLL 改变。之前暂停的 Inbox 类型不在新 DLL 内，service/window/observation 源码 hash 与原配套 manifest 一致；没有将当前 UI 修改混入包。

实际命令（带 `DOTNET_CLI_HOME=/tmp/phinix-dotnet-113 NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks`）：

```sh
dotnet build /tmp/phinix-offer-fix-20261018/build1.7/Trade.csproj -c Release -p:RestoreIgnoreFailedSources=true -v:q
dotnet build /tmp/phinix-offer-fix-20261018/build1.6/Trade.csproj -c Release -p:RestoreIgnoreFailedSources=true -v:q
dotnet run --project Tests/TradeOfferWireTests -c Release -p:RestoreIgnoreFailedSources=true -p:ProbeReferences=/tmp/phinix-offer-fix-20261018/probe-refs -p:GameReferences=/home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 -- /tmp/phinix-offer-fix-20261018/wire-fixture
dotnet build Tests/FrameworkAdmissionRuntimeTests -c Release --no-restore -p:NuGetAudit=false -m:1 -v:q
```

服务端目录执行：

```sh
PHINIX_TEST_ADAPTER_WIRE=/tmp/phinix-offer-fix-20261018/wire-fixture python3 tools/quality/trade_network_probe.py --server Server/bin/Release/net10.0/PhinixServer.dll --test-tool Tests/ServerLifecycleTests/bin/Release/net10.0/ServerLifecycleTests.dll --client-probe /home/hunyuan2333/Phinix/Phinix-Rework/Tests/FrameworkAdmissionRuntimeTests/bin/Release/net10.0/FrameworkAdmissionRuntimeTests.dll --output /tmp/phinix-offer-fix-20261018/network-evidence
```

初始探针还原因缺 NUGET_PACKAGES 尝试受限网络失败；补充已有缓存后执行。第一次网络测试实际报价成功，但独立 oracle 错把 response correlation 当作 operation MessageId，拒绝验收；修正测试保存/重放实际 ID 后通过，不改产品账本键。最终包初次校验误用正在修改的 UI 资源源码而报 stale Shell.xml；改用明确冻结的旧候选基线，保留“仅两个 DLL 改变”的独立逐字节检查，而非把新 UI 混入修复。

## 交付与限制

完整安装包位于 `/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-Trade-Offer-Fix-2026-10-18/`，ZIP SHA256 `a8fe8bf69516db2ac29ec21f9851117d65f36843a28c95d22c30ddf96f76250a`。1.7 Trade DLL SHA256 `894041c1cb359a3f3e5e67a4238fe428a0678964b4bbd020b11dbcd678f5d324`。服务端无须为本修复换包；双方完整退出游戏后换客户端再启动，保留身份/用户数据，不以删除服务端交易记录解决 AlreadyTrading。

真实游戏物化、库存和双方存读档未验收；本轮不证明资产守恒或 durable ACK。第三方 outgoing item handler 已认领时当前接口不返回实际 packet ID，其关联问题不属于已验证的无 handler 分支。客户端 correlation token 不是本轮修复的新持久 operation ID；网络重放明确使用原 MessageId。P3 仍实施中，新功能开发按用户要求暂停，先验收该缺陷候选。未安装到游戏、未部署云端、未推送或发布工坊。
