# 两版 API3 本地联合候选与 G1 测试

最终目标是 Mod 本体及每个插件分别一个产品版本/一个包，同时支持1.6/1.7；共享业务源码和API3，只在游戏adapter及构建/加载处区分。移除1.6只处理该adapter、构建/profile、目录与对应测试，不修改业务/UI和中立合同。正式插件双payload单ZIP尚未实现；商店推送暂缓。

## 本轮源码与 DLL

- `Client/ClientExtensionAbstractions/Properties/AssemblyInfo.cs`：旧 CLR 1.9身份结束，改3.0.0.0，FileVersion同步3.0.0.0。`Framework/ClientEnvironmentSnapshot.cs` 的 API兼容标记3.0.0。
- `Client/PresentationContracts/ClientPresentation.Contracts.csproj`：新中立合同程序集固定3.0.0.0。两游戏构建保持相同公共源码/身份，各自具有不同TFM；不是把一个net10 DLL交给1.6。
- Common子模块的 `ManagedExtensionManifest{,Reader}.cs` 保留并验证net472/net10.0声明；`ManagedExtensionPayloadInspector.cs` 校验实际DLL属性与声明一致；`ManagedExtensionCandidatePlanner.cs` 按游戏profile拒绝跨框架组合。游戏profile规则不是实际CLR运行结果，后者仍游戏待测。
- 商店安装规划与更新提示使用同一profile检查；包工具新增显式 `--target-framework`，检查游戏声明及所提供SDK的实际TFM，`--validate --game-version`可验证目标游戏。保留原API/CLR引用、ownership、hash、业务确认保护；没有发布到商店。
- `tools/compatibility/stage-local-joint.py`：在隔离目录生成Mod双版本布局，并通过现有普通bundle加载器加入红包、人才贸易、Example。资源与语言伴随声明由实际包工具生成，不增设官方插件私有加载路径。六个单TFM ZIP只留在validation目录用于校验，**不在Mod测试ZIP中**。

## 验证状态

| 项目 | 结果 | 状态 |
|---|---|---|
| 1.6真实工程及三个独立插件 | 严格locked构建通过；保留protobuf source/pins及旧GameDlls；末次仅源码换行整理后Host图增量通过 | 编译通过 |
| 1.7完整图 | 26个源码项目net10构建通过，随后增量闭合；不借旧Output基础设施 | 编译通过 |
| 实际双框架payload | 真实net472/net10 DLL，Host预检与商店规划交叉组合18检查；包CLI11命令、CRC/声明与拒绝前不写制品检查通过 | 白盒通过 |
| API3当前消费者 | 红包32、人才84、商店109，实际新DLL+fake session/registry | 编译通过、白盒通过 |
| API3 managed回归 | 完整3396；其中商店操作2225，含真实启动子进程、替换/恢复及本地导入 | 白盒通过 |
| 联合实物 | 1.6有36个DLL，1.7有29个；普通加载目录、无重复身份、API3引用及两语言companions共61检查 | 白盒通过 |
| 游戏/CLR/native/GPU/IME/业务/存档 | 尚未启动新候选；旧游戏记录不可代替 | 待测，无游戏通过 |

回归曾因测试重新生成ZIP时使用当前时间，跨ZIP两秒时间边界导致摘要变化。仅将 `Tests/ManagedExtensionRuntimeTests/ManagedStoreProtocolTests.cs` 的测试ZIP时间固定为与包工具相同常量，完整回归随后通过；没有改实际安装/提交逻辑来迎合测试。

## 冻结本地候选

- ID：`local-api3-20261014-03`，源码基于Host13412fe及未提交迁移修改，**不是正式发布**。
- 路径：`/home/hunyuan2333/Phinix/local-compatibility-analysis/local-joint-api3/candidate-03/phinix-rework-local-api3-20261014-03.zip`。
- SHA256：`2bb381bbfcd02a381832e575fb99e441c66d44e2112f40e630d96a7c7cdc9888`。
- 一个Mod测试ZIP，120文件，1.6/net472与1.7/net10.0分别在版本目录；共同API3。包含Host、Chat/Inventory/Trade/LegacyAdapter/Store、红包、人才、Example及资源。
- 没有Game/UnityStubs/Waylith/Harmony DLL、PDB、私有引用、源代码或玩家数据；PublishedFileId不在包内。Harmony由当前对应游戏版本的独立Harmony Mod供应。
- 插件在本地validation manifest中标记0.0.0，与正式版本分开；其DLL保留真实源码程序集版本。正式三个插件Host pin仍783fcb，不填写未存在的最终commit；原正式pack流程仍面向API2，不宣称已支持API3发行。
- 逐文件摘要、引用报告、六项payload验证和构建/白盒日志位于同目录或上级本地verification目录，不纳入Git。

## G1：可以开始1.7游戏测试

1. 解压测试ZIP到本地Mods目录；仅启用这个Phinix Rework测试副本，停用原同packageId副本及另行加载的旧红包/人才/Example副本。保持对应1.7 Harmony启用。无需从商店下载或安装validation目录中的ZIP。
2. 使用专用新测试存档；本轮先看加载、模组设置、主题界面和普通窗口。确认扩展列表能看到红包、人才、Example，记录实际游戏/Harmony/Host加载版本。原已装API2 managed包应被兼容检查拒绝，不应被新SDK悄悄接管；不清除其数据。
3. 检查普通背景/圆角/边框/透明主题有无此前整块橙色覆盖；调整游戏UI缩放、窗口尺寸，查看文字、裁剪、滚动及肖像有无异常。先测当前常用缩放，再补1×/1.5×/2×。
4. 检查主题编辑、热应用、导出、关窗重开；打开Example页面/自绘示例与动作菜单，尝试确认/取消/Esc，确认不会误触其他窗口。Chat页面可使用测试服务器连接观察，暂不发送人物、物品或执行正式交易。
5. 测试中文输入法选词、回车、粘贴、失焦再聚焦。候选选择回车与发送意图分别观察。结束后提供本次Player.log及对应操作顺序；若异常，保留最早异常上下文，先处理阻断再扩展场景。

G1只验证执行过的UI与输入路径。人物往返/地图交付/Scribe加载/保存重载/换档/断线为后续G3；1.6仍须自己的游戏回归。没有异常日志也不能宣布全面兼容。

## 后续收口

先根据G1日志修Host renderer/adapter中实际证实的问题，保持1.6独立实现。随后闭合最终双payload插件ZIP的声明、全包hash/CRC、当前变体选择/依赖预检、安装回执/重启恢复以及Local import；不以跳过未选中payload完整性检查换取兼容。商店UI与玩家流程保持稳定，推送另行授权；R44业务增强不纳入迁移。
