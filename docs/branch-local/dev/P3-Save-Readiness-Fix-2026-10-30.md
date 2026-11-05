# P3 未保存游戏前置提示与交付保留（2026-10-30）

## 现场与范围

用户要求检查本地Player.log，随后确认“保存后通过了，库存正常”，希望未保存时有合理兜底提示。读取当前本地RimWorld/Waylith日志，10:52:29出现 `Trade receipt belongs to an unavailable or changed inventory save scope; reconciliation required.`，10:52:34保存/availability通知后相同记录重复输出。实际安装Trade DLL SHA与10/29 P3候选一致，排除该DLL混用旧版。当前实现要求接收时已具有稳定存档范围，未保存时允许发起资产操作，直到收件阶段才拒绝，缺少前置UX。

日志只确认范围拒绝；用户补充确认未保存场景。旁边的Compatibility保存证明不可用是另一条保守维护警告，不据此推断磁盘损坏或删除Journal；`NO handler claimed item`单凭该日志也不能证明发包失败，既有adapter有同协议item packet发送路径。原始Player.log不复制入制品，仅存hash/大小与脱敏诊断摘要。

关联SV-33/38、TQ-30客户端接收/归属子集、FR-12。只改 `codex/p3-trade-business` 的Trade插件/测试/中英文字串，不改UI布局、Server、协议、schema、Common/protobuf gitlink、依赖版本，不读写玩家库存/账本或自动保存游戏。

## 行为

- 新 `TradeSaveReadiness` 统一判断已保存且库存Ready。没有状态provider、没有SaveIdentity、Inactive/UnsavedGame均明确拒绝；已保存但只读/待恢复也明确阻止新增资产操作。
- 交易窗口沿用现有状态条/按钮禁用说明，显示“请先保存当前游戏，再提交物品或确认交易。本次操作尚未转移物品。”。提交、确认、重置报价在escrow/reservation/地图物品移除之前再次检查；打开/浏览、撤回同意和取消仍可用。
- 公开业务port也在编码、枚举items、写intent/attempt或发包前检查，不依赖仅UI禁用。真实Activate配置必需Inventory API；未配置不会默认放行。
- 交易窗口监听InventoryChanged及AvailabilityChanged，保存后状态刷新并重新启用动作；关闭窗口成对解绑。没有新增布局、自动存档、协议重发或退款规则。
- 已收到但缺少/失效原范围的completion进入当前owner的ReconciliationOnly：保留Received/Unbound原证据、给出一次清楚提示，后续保存/availability不重复检查不可能复活的旧token，不刷完整异常堆栈。不会把旧记录绑定给后来存档，也不伪造Attempted/成功ACK。

保存后新操作正常，不代表历史Unbound收件已经恢复交付；它们继续待核对，不能凭新保存重新造物品。本轮没有删除玩家日志或任何持久证据。

## 验证

SDK10.0.113/runtime10.0.13，Linux。最终双版完整构建、三个维护插件消费者与94文件包审计通过。1.6 DLL运行于.NET10，不等于1.6 Mono实机。

- 旧P3包实际1.7 DLL上，同一“未保存时确认动作必须禁用且有原因”的回归失败；最终负对照编译0、测试非零，未把旧行为设成期望。
- 最终包1.6/1.7各315项Trade组件断言通过。新增检查覆盖未保存时提示一致、禁用确认/报价、编程调用UI也不进入escrow、保存后可用、只读阻止、撤回/取消可用；业务port未配置fail closed、未保存不枚举items、不写intent/attempt、不发送；Ready保持原持久发送路径；旧收件保持Unbound、无ACK且availability通知不反复提示。
- 最终包1.7实际adapter wire34项通过，正常保存状态以明确外部Inventory读状态fixture隔离；未Mock事务/资产到账成功。
- 中英文XML解析及重复key检查通过；包中已核验新增四组key存在。
- 初始测试引用internal UI类导致编译失败，改用已有反射探针方式；1.6初始probe未初始化新增语言key，触发无Unity进程中的native logging异常，沿用既有InventoryRuntimeTests的LoadedLanguage fixture隔离语言加载。保留失败日志，未捕获/跳过业务断言，没有为测试加入产品放行分支。

最终命令/每文件与DLL hash见交付evidence。主要命令：

`python3 tools/compatibility/build-dual-host.py --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-p3-save-dual-20261030-02`

`run-client-component-probes.py --suite trade-composition` 两次，分别消费最终包的1.6/1.7闭包；`--suite trade-wire`消费最终1.7闭包。各manifest含完整--assemblies/--game/--harmony/--output命令。产品Source HEAD为53c92ce加工作区修改，固定Common c165f61不变。合成状态、真实组件/真实临时意图文件，无随机seed或新faultId。

没有重跑Server stage/L3：本轮Server、协议和接收保存规则没有改变；这些保持10/29收口与10/30部署证据，不冒充本轮重验。新的原生UI/游戏存读档和本轮未保存→保存的实机交互仍待用户验证。

## 日志清理待办（用户明确要求）

发布前清理或默认关闭P3临时逐操作调试输出（例如TradeWindow/TradeAdapter过程trace），保留必要异常/状态摘要；本轮先消除同一不可恢复范围的重复堆栈。日志轮转、统一格式在后续日志工程处理。

`.intent/.attempt/.result/.query/.receipt/.binding/.plan/.checkpoint` 是幂等与恢复证据，不是可无条件删除的调试日志。只有具备权威终态、原存档checkpoint和回退/去重契约后才安全归档；Unknown/待交付/未到账证据不因“清日志”丢弃。该待办已写入session优先级与RoadMap，不创建后台清理任务。

## 安装与复测

更新双方客户端为 `Phinix-Rework-P3-Save-Guard-20261030-1.6-1.7.zip`，完整退出后替换程序文件，保留凭据、配置、插件及交易/库存持久化数据，仅启用一份Phinix。这是P3业务测试包，不是工坊UI发布；独立人才插件未包含。云端已部署的P3 Server兼容，无需再传/重启服务器。

用测试存档验证：未保存时打开交易，确认按钮/提交报价有先保存提示且物品不动；保存就绪后提示解除，少量正常成交/库存到账；旧待核对记录不因反复保存而刷错或重复发放。此前真实正常库存到账由用户反馈，不能扩展成新候选全部实机通过。

未提交、推送、工坊发布、安装本地Mod或新部署服务器。交付路径和SHA见manifest。
