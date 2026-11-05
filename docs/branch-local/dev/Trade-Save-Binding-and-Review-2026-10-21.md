# P3 存档绑定与只读待核对入口（2026-10-21）

关联 SV-31/32、TQ-30/31 子集；承接交付尝试日志。继续实施已获授权。本切片不新增自动恢复、重投、退款、人工确认到账或现代保存 ACK。

## 产品边界与行为

- Inventory 的普通 `InventoryStatus` 增加只读投影值 `SaveIdentity`，算法复用既有 journal 分区键 `SHA256(saveId + "|" + attachedIdentity)`。实际未激活、未保存、存档缺失或路径漂移时不提供该值；只读故障仍允许读取有效身份。摘要不包含原始路径字段，不是秘密、可信服务器标识或游戏保存证明。
- Trade 仍依赖 Inventory 的公开合同；不引用其 Client 实现、不在宿主添加业务分支，不修改 Common/vendor/gitlink。这里是同一客户端内 Inventory.Contracts/Client 与 Trade.Contracts/Client 联合改动；新完整候选须联合更新，不能把新 Trade DLL 单独混入旧 Inventory 包。`TradeInventoryRecoveryRecord` 保留原七参数构造函数，新增重载和只读投影字段，不删除旧 ABI。
- 非空交付在写 Attempted 前，以 immutable `.binding` 关联原始 receipt fingerprint 和存档摘要。原绑定不能覆盖；其他存档拒绝。已有历史 attempt 无绑定时不得追认为当前存档。未提供摘要时维持 Received，不调用交付，因此同一 Game 中保存完成后的可用性通知仍可尝试首次绑定；空批次保留无需存档的既有行为。
- `.receipt`/`.attempt`/`.result` 格式与 hash 保持原样，binding 使用独立 schema1；私有临时文件 flush 后同目录原子发布，损坏/孤立绑定失败关闭。旧接收记录仅显示未绑定，不自动迁移或重投；不接管此前丢失的纯内存数据。
- 现有交易列表加入“待核对交付”，展示 Received/Attempted/本地接口返回成功或失败，以及当前存档/其他或不可用存档/未绑定。只读取相同接收 UUID 和 endpoint 的 receipt；主体、地址、当前 Game/保存身份和 inbox revision 改变会失效投影。摘要和物品载荷不作为详情输出。收到或成功返回均明确不代表物品已保存。
- 沿用普通 presentation/action point；新的只读页上下文为 `trade-completion-recovery`，不冒充库存预留操作。产品没有为此上下文提供重投、取消、退款按钮；宿主保持中立。翻页、搜索及选择沿用原列表，完成记录与库存预留分别标注来源。

## 已执行检查

1. 真实文件入口 **61 项**检查通过（包含上轮 48 项）；覆盖重开、只绑定一次、冲突不覆盖、历史 attempt 不追认、非法摘要、坏 hash、孤立绑定、原有强杀/并发与未知结果保持。固定合成输入，无随机 seed。
2. 独立 `binding_oracle.py` 通过：字面核对 `a`×64 的存档摘要、接收主体、opaque byte=23、receipt/binding 哈希关联、文件身份与无原始路径/凭据。它不调用产品 reader。
3. 实际最终 **1.7 产品 DLL** 的组件入口 **241 项断言**通过。真实 Inventory.GetStatus 使用隔离临时文件、合成存档谱系和未初始化 Game/组件 fixture 核对 journal 分区摘要、谱系变化、路径漂移/文件缺失拒绝；没有初始化 Unity 或把这些文本文件当作游戏存档。真实恢复 reader 核对跨账号/endpoint 隔离、绑定变化与缓存失效；实际交易列表 action factory 对核对页不提供产品资产动作。实际交付 owner 对坏 codec 仍记录失败且不 ACK/重投。
4. 最终完整 **1.6/net472 与 1.7/net10** 构建、维护插件引用与 **94 文件**包检查通过；有既有编译器/框架警告。编译用 1.7.4977-rev1248 引用，未执行 1.7.4981 原生游戏。中英文 XML 解析、重复键及新增键检查通过，diff whitespace 检查通过。

命令环境：`DOTNET_CLI_HOME=/tmp/phinix-dotnet-113 NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks`。

```sh
dotnet build Tests/TradeCompletionInboxTests -m:1 -p:BuildInParallel=false -p:NuGetAudit=false --no-restore -v:q
PHINIX_BINDING_ORACLE=/tmp/phinix-binding-oracle-20261021 dotnet Tests/TradeCompletionInboxTests/bin/Debug/net10.0/TradeCompletionInboxTests.dll
python3 Tests/TradeCompletionInboxTests/binding_oracle.py /tmp/phinix-binding-oracle-20261021
python3 tools/compatibility/build-dual-host.py --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-binding-final-dual-20261021
dotnet build /tmp/phinix-binding-components-20261021/Tests.csproj -c Release -v:q
dotnet /tmp/phinix-binding-components-20261021/bin/Release/net10.0/TradeCompositionRuntimeTests.dll
```

组件临时工程编译现有控制台源码，仅引用最终 1.7 包及 compile-only 游戏依赖；沿用上轮解决的版本隔离布局。第一次全构建在核对页来源标签、总数和 page context 调整前完成，保留但不作为最终候选；最终冻结产品后在 `binding-final-dual` 重建，之后只新增 action 断言并重跑组件，无产品变更。没有通过无限重试/跳过掩盖失败。

证据及内部候选：`/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-Save-Binding-2026-10-21/`，manifest 包含 dirty/base SHA、固定 gitlink、产品/测试 hash、命令、包和 fixture 身份。内部整包含工作区已有 UI 工作，不安装、不发布或部署，不要求用户此时联测。

## 剩余门槛

绑定只在首次主线程交付准备时产生；网络事件入队到首次主线程执行之间切存档的归属问题仍未解决。已有 lineage+path 不检测同路径旧存档回滚/覆盖，Save As/拷贝路径会进入不同分区，不能自动迁移。绑定存在不证明资产到账或保存；不据此自动恢复。

当前只读界面需要相同身份重新连接相同 endpoint，未提供离线选择主体/服务器的浏览权限。这里没有新增服务端权威结果查询、稳定客户端 operationId 与请求指纹日志、现代 durable game-save receipt、历史未知自动修复或旧端 Adapter 扩展；P3/P6 不标完成。下一项先做关联主体的权威结果查询与恢复核对，再补接收时归属及回滚证据，最后具备可审查联合候选后通知用户执行真实游戏 UI/库存/存读档测试。无新网络、Windows ACL、掉电、实际玩家数据或游戏资产证据。
