# P3：接收时库存存档范围（2026-10-22）

关联 SV-32/33、TQ-30/31、FR-12 的交付归属子集。业务分支 `codex/p3-trade-business`，HEAD `53c92ce8eb314dcb8ca422ae4b6891a007931128` 加未提交变更；原 UI 工作区未改。客户端 Common 固定 `c165f61`，Server 固定 `67f243d`，protobuf 固定 `4b0c3aa`。本轮只有客户端产品/测试/验证工具和 RoadMap 文档变化，没有服务端产品、共享 gitlink、协议或外部依赖升级。

## 问题与实现

上一切片的 Game fence 不等于库存日志的存档范围。同一 Game 的通知先入队、随后另存为时，旧代码在执行时直接读取新的 `InventoryStatus.SaveIdentity` 并绑定，可能把交易交给后来的存档。先新增独立预期的实际交付组件回归；修改前实际失败为 `A same-Game save change between receipt and dispatch must not enter delivery`。

Inventory 通过普通插件 API 注册 `IInventoryReceiveScopeApi`，返回不可变的 `IInventoryReceiveScope`。Capture 只读取发布的引用，可用于接收线程；保存路径、谱系、Game 和组件检查仅在绑定的游戏主线程发布/验证。令牌只暴露 opaque SaveIdentity，失效不能复活。原 Inventory API/ContractVersion 6 保留；这是可单独发现的附加 API，不是协议版本分支。缺少新 API 时非空交付保留 inbox，不降级到执行时选择存档；空批次维持原规则。

Inventory 主线程 Attach / 游戏生命周期观察发布范围。稳定帧只比较内存中的 Game/组件/路径/谱系/路径代数，不额外读文件或做 SHA；实际使用时再核验现有文件/状态。路径改变使代数前进，回到原路径也不复活旧令牌。同路径保存不改变代数，同路径 Load 的 prefix 始终使代数前进并暂停接收范围；只有属于当前 Game 的 Inventory 组件 FinalizeInit 才释放暂停，再经主线程发布。初始化别的游戏组件不能释放当前暂停。加载失败而未完成初始化时保持暂停，不以超时自动解除。

Trade 统一完成入口在枚举物品、落盘和解码之前同时捕获 Game 与 Inventory 范围，经内部事件传给交付 owner。绑定/attempt 前校验原范围与当前 Inventory 状态，只把原令牌的 SaveIdentity 写入不可覆盖的 `.binding`。通知后另存为、接收时无可用存档而稍后保存、范围丢失均保留 Received/Unbound，不调用交付或发 ACK；availability/设置回调不为旧通知补造新的接收范围。稳定范围仍走真实 codec 整批校验，拒绝后保持原交付证据。

为了让同一组件回归在 1.6/1.7 的无游戏进程中运行，将原生 `Messages.Message`/翻译边界封装为可注入的可选通知委托；正式默认行为不变。测试仅隔离这个明确外部 UI 依赖，实际组合容器、业务、文件日志、编解码拒绝仍运行，且核对每个拒绝恰好请求一次通知。最终测试不捕获/跳过原生异常来获得通过。

## 构建与执行证据

SDK 10.0.113 / runtime 10.0.13，环境：

```sh
export NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages
export DOTNET_CLI_HOME=/tmp/phinix-dotnet-113
export MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks
```

1.7 使用 `tools/compatibility/build-net10-graph.py --game /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-p3-receive-save-final17` 构建完整 26 项目源码图。随后针对本切片实际修改执行有记录的增量 `dotnet build .../Joint.csproj --no-restore -c Release -m:1 -nr:false -p:BuildInParallel=false`，再按源码图节点同步测试闭包。最终源码与 DLL hash 见 evidence manifest，不沿用最初生成时的旧 source fingerprint。

1.6 使用 `tools/compatibility/build-graph-isolated.py --game /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --output /tmp/phinix-p3-receive-save-final16 --plugins --test-locks`，真实 solution、三个维护插件及既有测试锁 restore 均通过。后续私有范围/通知适配调整同步至隔离源码，再执行 `dotnet build PhinixClient.sln --no-restore '-p:Configuration=Release 1.6' -p:BuildInParallel=false -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1 -nr:false`。两版保留既有警告；本轮没有执行在线漏洞审计。

新增 `tools/quality/run-client-component-probes.py` 只为已有控制台测试生成隔离 runner，不引入测试框架、不重写既有场景。它消费显式构建闭包并记录输入 hash，支持 `trade-composition`、`trade-wire`、`inventory-runtime`、`inventory-composition`；原控制台项目仍保留。已有 `run-isolated.py` 还会重建消费者并依赖 Output seed，本入口用于实际产品图编译后的针对性验证。

```sh
python3 tools/quality/run-client-component-probes.py \
  --assemblies /tmp/phinix-p3-receive-save-final17/infrastructure \
  --game /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 \
  --output /tmp/NEW_ISOLATED_PROBE --suite trade-composition
```

每个 suite 必须用新目录；完整实际 argv/exit/输入 hash 在对应 `manifest.json`。trade-wire 同时执行既有可选导出/逐字检查，产生合成 `wire-fixtures`，因此最终每版 34 项；只运行无导出入口会报告 28 项。

最终计数及通过日志见 `/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-Client-P3-Receive-Save-Scope-2026-10-22/manifest.json`。场景包括同 Game 路径切换、回原路径、同路径读档暂停、无关组件初始化、缺存档、后台 Capture/主线程验证、停止、原始事件在枚举/IO 前携带原范围、空批次和稳定范围的 codec 整批拒绝。固定人工调度、无随机 seed、无新增可由普通客户端触发的生产故障点。本轮未重跑 L3/强杀/系统故障；先前证据不冒充本轮通过。

## 失败记录与限制

临时旧 runner 已被环境清理，首次调用找不到路径；从源码重建后，旧产品确实被新回归捕捉。库存组合测试原来硬编码工作区 `Output` 路径，现优先取已验证闭包中的宿主 DLL，并保留原控制台布局。1.6 初次合并多个 bin 的依赖因不同 Unsafe DLL 被拒绝；最终使用实际 Host 依赖闭包和所需维护插件本体，逐字记录来源，未替换 NuGet 包。首次 1.6 组合触发无原生 Unity 的 ECall 异常，最终以明确 UI 边界注入解决，没有跳过业务断言或修改预期。

这些令牌仍只属于当前进程，不能证明游戏存盘或真实资产守恒。先落原始接收日志、主线程验证并绑定之间仍有崩溃窗口：此类 Received/Unbound 不自动追认当前存档。尚无现代 recipient/deliveryId 持久 receipt、安全重投、未知库存预留结算、磁盘冷归档；也没有验证人为替换为同路径旧存档后的完整资产语义或重叠真实加载时序。新约束要求接收时已有可用存档范围；接收时没有范围的通知暂时保留核对，不因后来保存而自动补投。

没有真实 RimWorld 1.6/Mono 或 1.7.4981 的联机、保存/载入和物化验收，1.6 DLL 的控制台检查运行于 .NET 10。源码/编译/组件通过均不等于 P3 全部完成或 P6 通过。没有安装工坊候选、发布、部署 ECS、提交或推送；后续需要真实黑盒验收时另行通知。
