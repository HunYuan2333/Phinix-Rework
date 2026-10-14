# R17-21：Badge、媒体与 composer 图像入口

日期：2026-10-13～14（跨日收口）。承接列表 image lease 首切片；只整理公共图像合同和当前 renderer 的资源借用，不宣称 1.7 新绘制后端已完成。用户授权逐步实施，不部署、提交、推送或发布。

## 源码检查与结论

`Client/ClientExtensionAbstractions/UI/IClientPresentationService.cs:160/228/365` 的 Badge、Media、Composer 统一接收 `IClientPresentationImage`，移除普通投影的 Icon/Texture 属性与 Texture2D 构造参数。Row 已在上一切片迁入同一边界。Badge.WithColor 保留 source，测量只看 source 是否存在和 owner 提供的 aspect ratio，不 Acquire、不读 GPU dimensions。

`Client/Source/UI/ClientPresentationService.cs:829` 的 BorrowImage 是所有这些入口的共同借用路径：只在 Repaint Acquire，当前游戏 bridge 提供 base Texture。TryGetTexture 返回 false 或抛异常时清空输出，不能因第三方实现先写 out 值而画出无效资源。DrawImage 和 composer 均 finally 归还；release 异常与 resolve/draw 异常一样只记录一次图像诊断，不设置业务 FailureCode。没有原始 native pointer/object escape。

Badge cell 与 viewport 必须有交集；列表 overscan 中屏幕外的 Badge 不 Acquire。Media 也使用严格 viewport 交集。普通 label/占位及 tooltip 保留。没有图像时取消占位仍需 owner 重新投影 null source；不让测量查询实时 GPU 状态。

`Client/Source/UI/ClientPresentationFeedRenderer.cs:103` 的 Ready source 失效或抛异常时显示 owner 的 Text 和主题占位色；不把它改为 Idle，不自动发下载请求。现有 Idle 或 Ready+null 的可见加载意图、revision 去重和队列满后可重试规则保留。owner 保留 Ready/Loading/Failed/Unavailable、重试与选择动作的权威。

`Client/Source/UI/ClientPresentationService.cs:715` 的 composer 借用仅覆盖当次 IconButton。`Client/ClientExtensionAbstractions/UI/ClientWidgets.cs:176` 支持 base Texture，并在有 source 时预留原图标布局及完整按钮区域；图像失效不切换另一种按钮，不改变 submit 意图规则。该 widget 仍是过渡中的引擎相关公共工具，未冒充中立合同。其签名也属于 API 3.0 联合迁移。

## 消费者与边界

| 消费者 / 源码位置 | 当前修改 | 保留的权威 |
|---|---|---|
| ChatMainTabProvider:119 | send icon 经共享 FromTexture 包装 | draft、发送校验、动作与确认 |
| ChatMessageList:340 | 已有 media.Texture 包装成 image | 图片下载、状态、aspect ratio、资源缓存与销毁；尺寸读取仍在 owner，非 Host 测量 |
| TradeWindowPresentation:96 | 来源 Badge 包装已有 Def 图标 | Thing、报价、库存、返还与协议 |
| ManagedStorePresentation:143/145 | 商店 Badge 包装已有 icon | 图标加载缓存与商店流程；没有官方 renderer 分支 |
| 红包 PresentationAdapter:144 | 反射构造参数由 icon 改为 image，共享 FromTexture | 私有图标及业务 owner；不新增旧客户端分支 |
| 人才 PresentationAdapter:147 | 同一构造变化 | Pawn/Game/Map、PortraitsCache 与返还权威未修改 |
| Example / 主题 / Inventory | 本轮无相应新资源入口；随图编译和既有测试检查 | 同一公共服务；无额外私有能力 |

所有图像 bridge 仍在同一 Abstractions 程序集的 Game 源码目录，尚未完成物理隔离。它是两版引用下编译的过渡 bridge，不是已实现的新 Waylith renderer。主题 Color、自绘 Region.Rect、窗口/菜单/输入与其他公共工具仍有引擎依赖。没有由 Host 判断 Pawn 归属、下载图片、销毁 owner Texture 或接管任何业务。

## 验证状态

本次基于本地源代码和实际 DLL 编译/白盒，不依赖新的官方文档结论。1.6 使用原 GameDlls 4633/Harmony 2.3.6，文件未覆盖；1.7 使用先前保存的 4977 rev926 私有隔离缓存/Harmony 3。再次核对 Assembly-CSharp SHA `be2d5a0bcc44c8e9b713ac19cb073bb128418dab3c23b1ebf8eca6eceacbc2b8` 与 UnityStubs SHA `49badc8e2569062c693983555fc431622eab5ffc7e1e72252e8103f8d540e154`。当前安装 Version.txt 仍为 1.6.4871 rev598，未运行/更改游戏或正式存档。

| 检查 | 状态 | 范围与限制 |
|---|---|---|
| 当前消费者与图像调用路径 | 源码检查 | Host、Chat、Trade、Store、红包、人才；业务状态机未改 |
| 1.6 / 缓存 1.7 各 22 个选定项目 | 编译通过 | Host、内置插件、三个独立插件、Example 与测试项目；基础通信/protobuf 仍使用已有 Output，不是完整发行构建 |
| 实际合同检查，两版各 29 断言 | 白盒通过 | Image 属性/构造参数不含 Unity 类型、旧 Icon/Texture 属性移除；没有 GPU 执行 |
| 两版其他五套回归 | 白盒通过 | Trade 204、Inventory composition 84、game adapter 19、Example 45 断言，原 Inventory 19 组场景；系统 .NET 不等同游戏 CLR |
| 生产 renderer + stub GUI，264 检查 | 白盒通过 | 新增只在 Repaint/viewport 内借用、失效 out 值清空、读取/绘制/释放异常、占位、不自动加载、composer 点击区域与单次提交意图；原 IME/feed 队列回归仍通过 |
| 红包/人才 source-manifest、四仓库 diff checks | 检查通过 | 仅刷新修改文件摘要，未改语言、正式 pin 或版本 |
| GPU/native Texture/RenderTexture、肖像、切档、真实输入法与 1.7 画面 | 待测 | 游戏通过：无；不能由 stub GUI 或编译推断 |

本地隔离结果目录：`/tmp/phinix-r17-21b/closed-1.6`、`closed-1.7`、`final-presentation`。早期 final-1.x 是增加 Badge cell viewport 检查前的结果，最终以上述 closed 结果为准。分析引用与原始运行日志不进入仓库。

## 联合发布门槛与下一步

本轮构造参数/属性与 widget 签名变化继续按 API 3.0 管理；正式 API/CLR 身份、包范围、Host pin、产品版本未更新。三个插件原发布 pin 不满足当前源码合同，不部署中间 DLL，也不新增旧 API overload 来绕过门槛。

普通 page 的 Row/Badge/Media/Composer 图像入口现已统一；R17-21 的真实资源寿命和最终 backend 验收仍开放。下一切片继续收束主题 Color 与 R17-22 受控自绘，再做窗口/输入和两版后端，以及 API 3.0 程序集/包联合闭合。主题编辑、热应用、导出、本地包和第三方入口必须逐项保留。达到联合可运行候选时再通知用户进行游戏测试。
