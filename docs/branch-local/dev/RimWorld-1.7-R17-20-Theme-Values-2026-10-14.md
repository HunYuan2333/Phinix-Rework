# R17-20：主题 Draft、Preview、Reader 与显式绘制转换

日期：2026-10-14。承接注册默认色首切片；本轮完成 IClientTheme.cs 内的中立色值边界，尚未完成 Abstractions 物理拆分或新版绘制后端。

## 源码与合同

`Client/ClientExtensionAbstractions/UI/IClientTheme.cs` 的 IClientThemeReader、IClientThemeCatalog、ClientThemeSnapshot、ClientThemeDraft、ClientThemeColorPreview 全部使用 ClientRgba，包括 TryGetColor 的 out 参数、Preview 返回值及泛型字典。该文件不再引用 Unity/Verse。Snapshot 与 Draft 保持原 defensive copy、ordinal key 和缺失值规则；Raw RGBA 不做隐式 clamp 或 gamma conversion。

`Client/Source/UI/UiTheme.cs`、ThemeInputAdapter、ThemeDraftValues 的颜色解析、批次、draft、profiles、fallback、preview、XML 读写统一使用相同中立值，移除对 Unity Color/Mathf 的依赖。原 finite/[0,1] 校验继续在输入和注册边界；旧 v1 输入饱和与 float 注册范围限制用 System.Math 表达，规则不变。文件版本、字段名、持久化键、选择意图和业务协议未改。

`Client/ClientExtensionAbstractions/Game/ClientColorValues.cs` 提供显式 ToUnityColor / ToClientRgba。当前 renderer、ClientWidgets、Host shell、Chat/Trade/Inventory 私有绘制 helper 与 Example 仅在实际引擎绘制处转换；没有隐式运算符，也没有第二个 reader/主题服务。转换逐通道复制，不舍入、不预乘、不读 GPU。Game bridge 仍与合同物理同程序集，待 R17-23 联合布局决定。

ThemeSettingsWindow 的编辑缓冲直接读 RGBA，Swatch 使用同一值，无往返 Unity Color。主题编辑、热应用、导出、本地包与第三方入口仍通过同一 catalog/actions/presentation；Host 没有官方 renderer 白名单。

红包/人才 ThemeAdapter 反射读取当前 TryGetColor(string,out ClientRgba)，经共享显式 bridge 转成各插件已有私有绘制 Color；保留原 revision 缓存、借用服务与注册 lease 生命周期。没有新增旧 Color reader 的兼容分支。其历史内部能力 adapter 不是新包承诺。

## 本地包重载问题与最小修复

新增“真实 ExportPackage → ThemePackage.Read → catalog byte source 重载”测试时，旧 `XDocument.Parse(Encoding.UTF8.GetString(source.Document))` 路径报 `Data at the root level is invalid. Line 1, position 1.`。fixture 同时确认导出字节的 EF BB BF 前缀。XML stream 读取同一字节可以成功，这是源码和本地白盒确认的 BOM 处理问题，不能归因于 1.7 游戏引擎。

UiTheme.Refresh 改为 `ThemeInputAdapter.ReadXml(source.Document)`；文件与 byte input 使用同一受限 XML stream reader。按声明/BOM 解析编码，保留 1 MiB、DTD 禁止、无 resolver 与深度限制，不用 trim 或吞异常。导出/manifest 包格式与兼容范围未变。测试覆盖实际带 BOM 的导出包、无 BOM 输入、byte DTD 拒绝、unknown/inherit 保留、重载后的 fallback 和再次导出。

## 验证状态

| 检查 | 状态 | 依据与限制 |
|---|---|---|
| 公共主题签名、解析与当前消费者 | 源码检查 | IClientTheme.cs 无 Unity/Verse；引擎转换在现有绘制 helper 与 Game bridge；整个 Abstractions 仍不是引擎中立程序集 |
| 1.6 / 缓存 1.7 各 22 个选定项目 | 编译通过 | Host、全部内置、三个独立插件、Example、六测试工程；通信/protobuf 等基础设施仍读取保留 Output，不是完整发行构建图 |
| 实际合同两版各 47 断言 | 白盒通过 | 反射检查主题方法/构造参数/属性及嵌套泛型，无 engine 类型；snapshot/draft 复制、未知 key、缺失值与实际 Color 结构转换；不执行 native GUI |
| 两版其他五套回归 | 白盒通过 | Trade 204、Inventory composition 84、game adapter 19、Example 45 断言，原 Inventory 19 组场景 |
| 主题生产源码/stub drawing，73 断言 | 白盒通过 | 编辑/Apply 防御复制、fallback/inherit/unknown、保存失败不提交、XML/包导出重载与安全限制；两个真实 adapter 源码的中立注册/读取、热应用/缓存/释放 |
| 生产 renderer/stub GUI，264 项 | 白盒通过 | 原公共绘制、图像 lease、输入和动作回归；不是 Waylith/native renderer 验收 |
| 红包/人才 source-manifest、四仓库 diff checks | 检查通过 | 摘要刷新；未改正式产品/API 身份、Host pin、语言或 vendored SDK |
| 真实游戏主题视觉、热应用、第三方包动态绑定、IME/GPU | 待测 | 游戏通过：无；中间包不可部署 |

引用仍为保留的 1.6 GameDlls 4633/Harmony 2.3.6，以及 SHA 核验的 1.7.4977 rev926 缓存/Harmony 3。当前安装 Version.txt 是 1.6.4871 rev598，未操作游戏或正式存档。非 GUI suite 在系统 .NET 10 运行，不能等同游戏 CLR。

最终结果：`/tmp/phinix-r17-20-theme-reader/verified-1.6`、`verified-1.7`、`verified-theme`、`verified-presentation`。早期 final-1.x 是 BOM 修复前结果；以 verified 为准。失败的 package-diagnostic2 留下 BOM 诊断，原始玩家日志没有写入仓库。分析 DLL、临时包与引用不提交或分发。

隔离工程生成器的所有显式 Reference 设置 Private=false，避免每个临时项目重复复制游戏引用。验证运行目录仍按原规则统一复制需要的引用并运行全部 suite；不改变正式 csproj、打包规则或 SDK pin。其余 Store/Chat fixture 只同步了当前接口签名，本轮未运行它们完整套件。

## 收口与下一步

本切片关闭主题值类型的源代码迁移，不表示 API 3.0 的物理合同、两版后端、正式包或游戏验收已闭合。正式 CLR/API 身份与 manifest/pin 尚未切换，构造/返回/out 类型变化必须联合更新 Host、三个插件及 Example，不能单独部署中间 DLL。

下一入口是 R17-22：将 Region.Draw(Rect,theme) 收束为受控 draw context，限制可用绘制能力、保留裁剪和状态恢复；Example 用同一公共入口。随后处理窗口/菜单/输入与 1.7 后端、专门 1.6 adapter，再做联合游戏候选。Host 继续业务中立，不接管 Thing/Pawn、报价、账本、确认或资源 owner。
