# T5：纯数据主题包，本地实现记录

日期：2026-10-11。状态：T5 本地闭环代码已实现：编辑器 ZIP 导出、商店本地安装/更新/卸载、目录刷新与恢复后处理；同批编译/打包，游戏及故障恢复未验收。Index/远端主题准入与现有线上流程的后续实施、快捷投稿一并延期。下文首批记录保留历史背景，以末节当前状态为准。

## 本批实现

Extensions/PluginStore/Shared/ThemePackage.cs 为工具和商店客户端共用的无游戏依赖源码，定义不可变清单/变体及冻结载荷、严格 JSON/XML/ZIP 校验。没有改 Common gitlink、历史 DLL 清单、收据或 journal；当前中立源码先随客户端工具共同演进，Index 接入时再固定可信源码输入。

- management 为 phinix-theme，载荷类别计划为 theme-zip，清单 schemaVersion=1。
- packageId、name、author、version、phinixRange 和 themes 必填。变体含 id、name、path、schemaVersion、length、sha256；稳定选择 ID 为 package/{packageId}/{variantId}，版本和文件名不参与身份。
- 不要求 SDK/CLR 范围、targetFramework、模块、程序集或可执行依赖。包主题只允许规范 v2/v3 XML；历史 v1/旧键先通过普通主题导入/导出归一化，再打包。旧格式归一化不进入新业务。
- 一个包 1–16 个变体。ZIP 最大 8 MiB、展开最大 8 MiB、单主题最大 1 MiB、清单最大 64 KiB，每个主题最多 512 个显式键。文件仅 manifest.json 与 Themes 下声明的直接 XML 文件，不含目录条目/别名/链接/脚本/DLL/额外资源。
- 路径区分大小写但碰撞检测忽略大小写，拒绝 Windows 保留名称、反斜线、路径穿越和重复项；限制同样适用于三端。所有声明文件校验长度与摘要，拒绝未声明文件。ZIP 验证不解压到磁盘，不调用作者代码。
- XML 禁止 DTD、外部实体、处理指令，拒绝嵌套业务元素、重复/冲突键、非有限值、范围外 RGBA、不匹配 schema；未知规范键作为部分主题保留，不要求当前客户端/已安装插件提供完整键集合。参数合法范围及回退在实际主题 runtime 使用时再按注册定义验证。
- name/author 为基础可读文本；本批没有实现额外描述/本地化资源。后续以有界明确声明扩展，不能先允许任意附加文件。

## 作者工具

使用现有 ManagedPackageTool，不新增 NuGet SDK 或另一套可执行插件包工具：

```sh
dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll --pack-theme /absolute/package-config.json /absolute/theme-1.0.0.zip
dotnet Extensions/PluginStore/Tools/ManagedPackageTool/bin/Release/net10.0/ManagedPackageTool.dll --validate-theme /absolute/theme-1.0.0.zip
```

配置包含 packageId/name/author/version/phinixRange 与 themes；每变体为 id/name/file。file 相对配置目录，仅用于本地作者输入；包内路径由工具生成，不能映射任意目标路径。打包先静态验证再写候选，拒绝覆盖已有输出。结果注明 static-data-only 和 installed=false，不能误当安装成功。

本地配置和演示包在 Phinix/local-theme-packages/2026-10-11-t5。尚未生成正式 Release/Index 记录；使用同一 XML 的普通导入仍可用，勿把 ZIP 放进用户 Themes 目录或手工复制受管文件。

## 后续事务要求

共用来源、包身份、冻结载荷、工作区与提交/恢复标准，数据包放独立受管主题根；不能假造模块、写入用户 Themes 或利用空 DLL 避开现有 gate。安装/更新全包提交后刷新目录；选择器读取可信收据，不扫描任意文件当安装。卸载选中包先说明影响并回退默认，保留用户覆盖；丢失包保留选择意图属于不同情形。既有 DLL 收据/journal 保持可恢复，类型分支仅在包输入/存储边界，主题业务不识别历史 schema。

需继续完成：theme-zip 来源绑定和候选规划、数据包 journal/receipt 与恢复、侧载/管理 UI、Host 主题来源服务、目录 v4 及工具/可信 Validator 固定输入。在线发行继续延期，不修改现有在线功能开关。


## 存储接续核对

当前 DLL 安装主体在 Common 的 ManagedExtension Runtime/InstallationRecovery，不能调用历史 Mod 安装器或假造模块接入主题。下一批先抽取中立包事实与类型 adapter，保留当前来源/所有权、lease 和 preparing/committing 恢复规则；旧 receipt/journal 在输入边界继续读取。显式卸载后的主题选择持久化需要可恢复的后处理，不能宣称跨目录原子。详细方案见 Theme-Package-Storage-Design.md；本批为方案，尚未实现事务。


## 最新交互计划：保留导出、本地导入走商店

2026-10-11 用户补充：编辑器导出保留，目标改为标准主题 ZIP，成功时显示绝对路径、复制/打开目录，并可选提示是否提交商店。商店负责纯主题包本地安装与卸载；此闭环完成后移除选择器本地导入，已有用户 XML/覆盖继续读取。当前编辑器仍导出 XML，商店主题事务尚未实现，不能提前删除入口并宣称替代已完成。

游戏内导出应调用共享数据打包核心，不要求玩家安装 gh/dotnet；旧格式归一化继续由输入 adapter 承担。快捷提交及无 GitHub 投稿路径见 Theme-Export-and-Quick-Publishing-Assessment.md，仅为评估，不作为当前本地 T5 完成的前置，也没有恢复线上发布。


## 本地闭环实现（当前）

- 校验/打包核心移至 Common/Utils/Framework/Themes，Host、Store、作者工具消费同一实现，不再从 Store 源码反向链接到 Host；Common gitlink 未更新、protobuf 未修改。
- 安装 Recovery 的 Item 使用中立存储事实，DLL adapter 保留旧 manifest/receipt/state 校验；共用 InstallItems 的 staging、精确工作树、preparing/committing 决定、原文件备份与提交重放。主题使用明确 schema 3 数据收据/状态/日志，不假造模块。
- 卸载抽取共用 FinishOwned 文件删除引擎，仍核对精确完整/剩余文件和元数据；主题 journal 完成后处理才删除。设置意图使用预期选择及有界设置摘要，失败可重试且后续用户选择优先，不宣称跨目录原子。
- 独立 SaveData/Phinix/ManagedThemes lease/受管目录。进程内包变更共用 gate；主线程主题操作遇忙立即拒绝，避免等待正在请求主线程确认的后台 DLL 操作。新包与已安装其他类型按包 ID 互相核对冲突。
- Host 提供 IClientThemePackageService，Store 借用；已验证收据与防御复制数据进入当前主题来源，业务不读历史 schema。刷新后目录热发布；显式卸载选中包转默认并保留覆盖，意外缺失/更新移除变体保留选择意图。
- 新增商店“本地主题包”窗口与非 DevMode ZIP 选择；原代码侧载 DevMode 要求不变。安装前展示冻结候选与来源，预期摘要变化拒绝；本地修订允许同版本不同摘要，重复摘要/降级拒绝。
- 编辑器保留导出，弹窗填写名称/作者/稳定新包 ID/版本/路径，输出规范 ZIP、复制/打开目录；同一编辑来源的作者元数据保存在本地设置。显式值/unknown/inherit 保留，不铺平所有回退。目标存在拒绝覆盖；不登录、不上传。选择器导入按钮移除，已有用户 XML/配置及公共输入边界保留。
- 包宿主范围按产品版本 0.9.7 核对，与 API 2.0/CLR 1.9 分开；新样例在 Phinix/local-theme-packages/2026-10-11-t5-local。旧首批 2.0 范围样例不作为本轮游戏安装包。
- 包最多 256，目录内冻结主题文件总计最多 64 MiB；单包/文件/变体/键的原限额保持。渲染不扫描磁盘，刷新/操作时验证；本地小数据包操作同步进行，慢磁盘耗时尚未实机测量。

游戏操作见 Theme-T5-Local-Playtest.md；当前/设想线上流程及整体延期见 Theme-and-Plugin-Publishing-Flows.md。恢复实现未做故障注入，不以编译代替验证；本轮未新增或运行测试、同步游戏目录、提交或线上发布。
