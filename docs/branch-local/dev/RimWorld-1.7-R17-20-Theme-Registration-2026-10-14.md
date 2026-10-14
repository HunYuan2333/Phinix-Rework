# R17-20：主题注册默认色的中立值边界

日期：2026-10-14。逐步实施，承接普通图像入口；这是主题注册首切片，不宣称整个主题合同中立或 1.7 renderer 完成。

## 源码检查

`Client/ClientExtensionAbstractions/UI/IClientTheme.cs:42/60/65`：IClientThemeRegistration.RegisterColor 的参数、ClientThemeColor 构造参数及 DefaultColor 属性改为已有 ClientRgba。没有新增第二套主题服务、隐式 Unity conversion、旧 Color overload 或旧客户端分支。

`Client/Source/UI/UiTheme.cs:441`：仍由同一 Registration.Stage/Metadata/Commit/Dispose 执行 namespace、owner、批次、fallback 与生命周期规则。`ThemeInputAdapter.Validate(ClientRgba)` 按原 finite 与 [0,1] 边界验证原始通道，不通过 clamping 接受非法值。`UiTheme.DrawingColor:222` 在目前仍使用 Color 的解析路径显式转换四个通道；不做舍入、gamma、premultiply 或 alpha 修改。

Host 默认色、Chat、Trade、Inventory、Store 和 Example 注册改用 ClientRgba。与各仓库当前 HEAD 的源码注册默认值逐项核对：82 个 key 与四通道数值一致。fallback、metadata、参数默认值和 theme key 没有改变。

红包与人才 `Client/Compatibility/ThemeAdapter.cs` 的当前注册路径反射查找 ClientRgba 构造函数和新的 RegisterColor 签名，按原私有 Color 通道构造公共值。内部历史 capability 分支保留，不新建兼容旧 Color 注册合同的分支；正式包范围仍按原发行基线，最终 API 3.0 联合切换前不发布这些源码。

## 功能与权威

本轮只改变注册默认色的表示。Reader/Capture、Preview、Draft、XML 输入和保存仍使用原 Color 路径，后续再迁移。普通注册 metadata 已不包含 Unity 类型，但整个 IClientTheme.cs/Abstractions 仍依赖引擎。

主题 editor、热应用、导出、本地包、第三方入口保留。导出不会自动把注册默认值写成用户 override；显式 draft override 才按既有规则写入。没有改变文件版本、主题 profiles、inherit/unknown/fallback、T5、选择意图、包兼容规则或业务保存/协议。Host 不因本切片获得任何 Thing/Pawn/资源或业务权威。

## 验证

| 项目 | 状态 | 范围 / 限制 |
|---|---|---|
| 82 个默认色 key/四通道与原源码比较 | 源码检查通过 | Host、Chat、Trade、Inventory、Store、Example；不更改默认色 |
| 1.6 / 缓存 1.7 各 22 项选定构建 | 编译通过 | Host、内置、三独立插件、Example、六测试工程；基础通信/protobuf 从保留 Output 读取，不是完整发行构建 |
| 实际合同两版各 32 断言 | 白盒通过 | 新增注册参数/DefaultColor/构造函数中立类型检查；不执行 native UI |
| 两版既有其他五套回归 | 白盒通过 | Trade 204、Inventory composition 84、game adapter 19、Example 45 断言，原 Inventory 19 组场景 |
| 主题生产源码 + stub drawing，66 断言 | 白盒通过 | 注册 staged/commit/revoke、四通道精确值、fallback provenance、非法值拒绝、真实 XML 导出/读取；原编辑/热应用/持久化回归保留 |
| 红包/人才当前 ThemeAdapter 源码链接执行 | 白盒通过，含于上述 66 | 两个真实反射实现各验证注册通道、revision 缓存、热应用、Dispose/迟到读；type marker 与 drawing 使用 fixture，不是正式 DLL 动态载入测试 |
| 生产 renderer + stub GUI，264 项 | 白盒通过 | 既有图像/输入/动作路径回归；不是新版游戏 renderer 验收 |
| 红包/人才 source-manifest、四仓库 diff checks | 检查通过 | 修改文件摘要已刷新；未改正式 pin、版本或 vendored protobuf |
| 真实游戏主题视觉、热应用、导出包重载与第三方程序集绑定 | 待测 | 游戏通过：无；完整包/API 3.0 门槛仍开放 |

1.6 引用为原 GameDlls 4633 + Harmony 2.3.6。1.7 使用保持原哈希的 4977 rev926 缓存 + Harmony 3；当前安装 Version.txt 仍为 1.6.4871 rev598，未把目录或旧日志当作新的 1.7 游戏验收。使用系统 .NET 10 运行白盒，不能等同游戏 CLR/native GUI。

最终结果在 `/tmp/phinix-r17-20-theme-registration/verified-1.6`、`verified-1.7`、`theme4`、`presentation`；默认值源码比对是 `default-parity.json`。早期尝试发现两个 Chat Color.white 调用和测试 fake 注册签名遗漏，已同步修正。另有一次 /tmp 空间耗尽导致 MSBuild 日志失败，不能算代码兼容异常；清理本轮失败及上一切片重复中间 bin/obj/run，保留 projects/logs/fingerprints、原 GameDlls、4977 引用缓存和上一切片 closed 最终输出，再以新目录重跑。

ThemeRuntimeTests/run-isolated.py 使用原 Utils/Protobuf DLL 的局部复制，不递归构建 vendored SDK，不写原 bin/obj。ThemeRuntimeTests 的 source-linked adapter 检查不代替正式程序集绑定。其他 Store/Chat 测试 fixture 的注册签名已同步，未在本轮运行它们的完整测试套件。

## 下一切片

继续把 ClientThemeDraft、Preview 与 Reader/Snapshot 的颜色值迁入同一 ClientRgba；实际绘制使用显式游戏 bridge。须维护 editor、export、包、第三方 registration/热应用的同一服务，不新增平行主题系统。之后 R17-22 受控自绘和窗口/输入，再接入新版绘制 API 与专门 1.6 adapter。

注册签名与 DefaultColor 属 ABI 破坏性变化，继续按 API 3.0 联合闭合。正式 API/CLR 身份、manifest API 范围、固定 Host pin、产品版本尚未更新；本轮没有提交、推送、发布、部署或操作正式存档。尚不需要安装中间 DLL 进行游戏测试。
