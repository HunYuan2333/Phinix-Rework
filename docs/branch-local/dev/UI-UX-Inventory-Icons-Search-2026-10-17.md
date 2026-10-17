# UX2-05：库存图标与名称/来源搜索

日期：2026-10-17。Host分支 `codex/uiux-release-main`，HEAD `27878e7fb78c11fec4f390db317dc0db5b0b10c9` 加本轮及前批未提交改动。没有安装、提交、推送、发布或更新正式Host pin；保留已有fixture、文档、UI改动及另一路协议工作。

## 玩家路径和保留规则

- 主窗→物品库存→搜索框输入名称或来源→直接选择匹配记录。非空查询直接列出匹配记录，组内记录不需要先展开；无查询恢复原分组/展开布局。
- 搜索忽略大小写和首尾空白，匹配本地化物品名、原记录名和来源摘要；先剥离显示标签，不检索payload或把中文名称反查Def。无匹配明确提示；更改语言后重建搜索文本。
- 单笔提取/定时计划的记录身份和数量草稿保持；编辑项被过滤掉时显示“当前编辑的物品不在搜索结果中”，不会把编辑器指向另一条结果。
- 更改搜索时取消已有分组批量选择并提示，保留批量数量文本；查询结果不提供隐含“整组全部”动作，避免提取未展示记录。批量提取需清空搜索后重新选择分组。实际运行中的批任务不因过滤而取消。
- 同一Game关闭重开保留查询和已有草稿/分组展开；换Game清查询和选择，继续原存档边界。旧presentation session关闭会重置其滚动；过滤导致选择消失时公共renderer也可能归位，不承诺滚动像素不变。UX2-06连续提取尚未实现。

以主窗口已开为基准，查找是1次Tab＋1次输入框聚焦＋输入，再1次记录选择；异步业务无新增等待，后续提取/数量/确认链未改。搜索省掉匹配分组的展开操作及人工逐组找记录，不把搜索返回当作业务成功。

## 图标与公共能力

`InventoryItemPresentation`新增可选string属性 `IconDefName`，由已经实现只读 `IInventoryItemPresentationCodec` 的codec提供。仅为本地基础UI图标提示，不进InventoryEntry、payload、日志、持久化schema、分组键或所有权。未新增Host插件白名单、私有Draw、renderer、第二主题或动作系统。

维护的Trade及红包codec在原有只读快照解码/有效性条件内返回真实Def标识；没有为了图标调用Materialize/CreatePreview/ThingMaker/Scribe解包，也不放松codec准入。旧codec/旧红包包不提供该属性时仍能查看和操作，图标为占位；本轮配套红包0.0.4让红包入库物品也有元数据。正式Host pin不更新。

Inventory自己的游戏适配侧按标识读取 `ThingDef.uiIcon`，以公共 `ClientTextureImage` / `WithImage` 展示；组内图标标识不一致用占位，不能从首条擅自代表混合图标。缺失、过长或解析失败的标识回退到UnknownItem图标，连该Def都不存在则BaseContent.BadTex。图标失败不影响余额、选择或提取按钮准入。

只显示基础Def图标，不声称还原材质染色、品质覆盖、造型或容器内嵌内容的完整外观；材质/品质等文本和原物品payload保留。未获可信展示元数据的内容继续占位，不猜图。

图像包装按Def缓存于当前数据/语言快照；搜索不重复解码payload，不每帧查Def。库存版本/语言/Game变化、关窗与模块释放时撤销旧图像包装，借用纹理归游戏内容系统，不复制GPU资源或销毁游戏纹理。缓存不会累积整个会话的历史Def提示。

## 源码范围与候选隔离

- Inventory合同：可选图标标识属性，仅加法。
- InventoryTab、InventoryDisplaySearch、InventoryIconCache：搜索投影、草稿/选择规则、被动图像与生命周期。
- TradeInventoryIntegration：返回现有只读快照Def。
- 独立红包 `RedPacketInventoryCodec.cs`：返回既有payload的Def；对应source-manifest摘要刷新，`check-source.py`通过。语言资源未改，不改发布配置/pin。
- Host中英文InventoryExtension.xml：搜索/无匹配/隐藏编辑项/取消分组选择四个提示键。
- InventoryDisplayRuntimeTests及InventoryRuntimeTests：行为检查。

沿用UX2-04已冻结的UI源码工作区，叠加上述文件组成 `/tmp/phinix-ux05-workspace`；不混入另一路正在修改的FrameworkAdmission连接协议代码，实时工作树这些外部改动原样保留。本地包代表这个明确的UI组合，不能仅靠HEAD重建，也不是实时工作树所有并行修改的合集。

## 实际验证

| 检查 | 结果 |
|---|---|
| 聚焦显示白盒 | 15项通过：中英文/来源/大小写/空查询、缓存复用、缺失/无效Def、占位、图像lease撤销；不执行原生GUI或物化 |
| 最终真实DLL库存回归 | 1.6/1.7各20组通过：新增实际InventoryTab搜索投影/稳定记录/单笔草稿/清除后分组恢复/余额不变；原批量、预留、组件快照和日志场景保留 |
| 最终真实消费绑定 | 两版各红包38、人才84、商店109通过；红包引用实际打包0.0.4 DLL，Host/库存为最终联合DLL |
| 完整构建 | 1.6经典完整图及3插件通过；1.7完整26项目通过；31/24 DLL、21共享源码身份/staging/Host ZIP审计通过 |
| 红包本地包 | 两版同一冻结Host与源码编译、schema2 variants、CRC/静态/引用检查通过；对最终Host再次验证两个variants的hostReferencesChecked=true |
| 静态资源 | 两语言XML可解析且键无重复；红包源摘要/语言检查通过；保留原CRLF的diff检查通过 |

日志：`/tmp/phinix-ux05-display-01`；最终库存 `/tmp/phinix-ux05-ledger-final-16`、`-17`；最终消费者 `/tmp/phinix-ux05-consumers-final-16`、`-17`；Host `/tmp/phinix-ux05-dual-02`；红包 `/tmp/phinix-ux05-redpacket-build`；最终配对验证 `/tmp/phinix-ux05-final-pair-validation.json`。本轮不声称之前415呈现/75主题等未改套件重新执行；原有通用几何断言失败没有顺手修改。

## 原生游戏待测

两版、不同主题/语言、1×/1.5×/2×缩放，查看交易/红包来源、多个材质/品质/整栈、未知codec、缺少Mod或UnknownItem的图标；确认搜索名/来源、中文IME、无匹配、清空、分组内直接匹配、编辑中隐藏结果、关闭重开及换档。用1/100/10000条记录测实际响应/滚动和缓存行为。特别验证分组批量取消提示、单笔草稿不串记录及实际提取数量/到账核对；本轮没有游戏启动/存读档验收，不把白盒当截图结论。

UX2-05源码与本地候选就绪，游戏待测；下一UX2-06为行内数量/提取与连续操作，不自动省掉业务确认或未知结果核对。

## 最终交付指纹

Host双版ZIP：`/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/inventory-icons-search-2026-10-17/Phinix-Rework-Inventory-Icons-Search.zip`，SHA256 `d495e978e6b4dbb3b8f430d9c75a0cad6739902e253e144431aecb27a60065a4`。配套红包0.0.4双版ZIP：`/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/inventory-icons-search-2026-10-17/Phinix-RedPacket-0.0.4-Dual.zip`，SHA256 `30e9a113509e55a3874135ced9877dba8ec57bcb1d78225239fd146b81ddfbee`。红包包需要与本Host一起测试；旧包仍可用但返回图标占位。人才/Example无新包变更。

最终1.7编译清单562项源码指纹与冻结源一致；上述5个本轮Host生产文件及红包codec与实时工作树的本轮修改逐字一致。候选两语言库存资源与当前XML一致。三仓/SDK/包静态验证不替代游戏操作验收。源码包含新可选属性，正式发布前仍须以真实提交闭合Host pin和消费者；本地候选不修改这些发行门槛。
