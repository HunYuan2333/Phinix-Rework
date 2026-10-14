# R17-21 首个切片：列表图像的借用与寿命

2026-10-13。列表图像源码/编译/白盒完成；R17-21 整体与游戏验收仍待完成。未部署、提交或发布。

## 修改和权威边界

`UI/IClientPresentationImage.cs` 定义中立 `IClientPresentationImage`、`IClientPresentationImageLease` 和像素尺寸元数据。普通来源只暴露 Size、Revision、Acquire；普通 lease 只暴露 IsValid/Dispose，没有 Texture、RenderTexture、Pawn、object 或 native pointer。未知尺寸为 0/0，不能测量时调用解析函数来猜尺寸。

`ClientPresentationRow.Image` / `WithImage` 替代原 Texture2D Icon、Func<Texture> IconSource、WithIcon/WithIconSource。不可变 row 拷贝与 Badges/ReadOnly 组合保留同一 source。Badge 自己仍有旧 Texture 属性，因此这里只关闭 row 图像入口，不声称完整对象图或 Abstractions 程序集已经与引擎脱钩。

`Game/ClientTextureImages.cs` 是所有插件均可使用的公开游戏接入桥。`ClientTextureImage` 包装现有 Texture 或被动来源；`IClientTextureImageLease` 是供当前游戏绘制后端消费的强类型 Texture lease。没有以 object/IntPtr 绕过公共边界，没有按官方 owner/程序集白名单选择 renderer。桥与中立接口目前仍编译在 Abstractions 中，仅先隔开源码边界；后续引擎适配程序集拆分与版本绑定仍须处理。

producer 保留资源所有权。动态 FromSource 必须提供 owner guard，Revision 是纯读；解析前后检查 guard 和 revision，解析期间失效或版本改变不返回旧资源。Invalidate 撤销来源并释放 delegate 引用；lease.Dispose 只释放借用引用，不 Destroy Texture、RenderTexture 或 Pawn。静态 FromTexture 不读取原生 dimensions，也不获取 GPU 像素。

lease 只跨一次绘制，不是长期 view/GPU 缓存。两个视图可以借同一 source；关闭一个视图或释放其 drawing lease 不撤销另一个视图或 producer 的资源。最终资源销毁仍由插件/游戏 owner 决定。

## renderer 和消费者

`ClientPresentationService.DrawList` 在可见 viewport 内且 Repaint 时才调用统一 DrawImage；布局、输入和 overscan 中实际位于 viewport 外的行不 Acquire。DrawImage 用当前游戏 bridge 消费 lease，并在 finally/using 释放；不把解析后的 Texture 留在 page 或跨帧缓存。

解析异常只记录一次独立图像诊断并保留行标签，不设置页面业务 FailureCode、不执行或取消动作。不以图像失败触发物品返还、协议操作或业务确认。无 source/失效时保留布局中的图像占位，避免测量查询 GPU 状态；owner 可重新投影不含图像的 row 来取消占位。

| 当前消费者 | 改动 / 保留 |
|---|---|
| TradeQuoteRows | Def/未知/内层物品图标通过共享 FromTexture 包装；预览仍不物化 Thing，报价业务未变 |
| Talent PresentationAdapter | 私有 HostRow 仍由人才 owner 保持 Texture/Func<Texture>；导出公共 row 时转为共享 image source，guard 绑定 presenter 未 Dispose；不按 Host 私有路径绘制 |
| TalentPawnIcons | 原 Pawn/Game/Map 检查、PortraitsCache.Get 和 Person fallback 未修改；公共来源尺寸使用未知值，因为 portrait 与 fallback 尺寸不同，不能硬写 64/64 冒充准确 metadata |
| Example | 静态白纹理和动态来源样例改用 WithImage，动态 guard 绑定该 session 的开放状态；普通第三方入口不变 |
| 红包、Chat、Inventory、Store、主题 | 本切片没有它们的 row 动态纹理解析调用；一起重编验证。Badge/media/composer 的旧 Texture 入口未迁移，不删除相关功能 |

测量和 metadata 读取不调用 producer 解析函数；静态图标由 producer 在投影时提供已有资源引用，Host 不下载、生成肖像或判断 Pawn 归属。

## 构建与发布预检

三个插件 `ci/config.json.requiredPresentationMembers` 从已被本迁移移除的 WithIconSource 更新为 WithImage，是更新当前合同门槛而非删除预检。用真实固定 Host 提交 `783fcb0bb6ad56b97e8ca5875457babfe6520c25` 的合同源码调用原 verify_host_api，三个仓库均拒绝旧源码，当前迁移源码均通过。只是源码 member gate，不代替 DLL/包绑定检查。

Host pin、产品版本、API/CLR 正式身份、manifest API 范围和私有参考包 pin 未更新。现有固定旧 Host 不满足新门槛，当前源码不能继续按旧包发布；须在最终联合冻结时切换实际已测试 Host 提交和 API 3.0 包范围。红包和人才的 source-manifest 已同步变更文件并通过各自 check-source，不改 vendored protobuf。

## 验证与限制

引用与 R17-20 一致：原 1.6 GameDlls 4633 + Harmony 2.3.6；1.7 使用哈希核验的 4977 隔离缓存 + Harmony 3。当前安装目录仍是 Version.txt 1.6.4871 rev598，未恢复或修改 1.7 安装。缓存 Assembly-CSharp SHA `be2d5a…`、UnityStubs SHA `49badc…` 已再次核对。

| 检查 | 1.6 | 缓存 1.7.4977 | 限制 |
|---|---|---|---|
| 22 个选定项目 | 编译通过 | 编译通过 | 包含 Host、全部内置、三插件、Example 和六套检查项目；基础通信/protobuf 仍从已有 Output 读取，不是完整发行构建图 |
| 实际公共合同检查 | 白盒通过，20 断言 | 白盒通过，20 断言 | 原数值检查 + neutral row image/lease 属性、移除旧字段、尺寸格式校验；不调用 GPU |
| Trade、Inventory、Example 五套原回归 | 白盒通过 | 白盒通过 | Trade 204、Inventory composition 84、Inventory game adapter 19、Example 45 断言，以及原 Inventory 19 组场景 |
| 生产 renderer + stub GUI | 白盒通过，244 检查 | 同一 fixture 检查 | 新增 owner/revision 失效、解析期间失效、多 lease 独立释放、布局/输入不解析、viewport 外不解析、异常不污染业务状态及日志去重 |
| 三插件发布源码预检 | 白盒通过 | 同一只读检查 | 固定旧合同被拒绝，当前合同通过；不是正式包兼容验收 |
| source-manifest / diff checks | 检查通过 | 同一只读检查 | 不提交玩家日志、游戏 DLL 或分析引用 |
| RenderTexture/native texture 实际寿命、GPU、portrait cache、切图/切档 | 待测 | 待测 | 游戏通过：无 |

最终输出：`/tmp/phinix-r17-21/final-1.6`、`final-1.7`、`final-presentation`，使用已有两套 run-isolated 工具。人才的尺寸 metadata 最后改为未知值后又单独验证两版编译；该调整不调用肖像缓存。

fake texture 的 identity 和引用释放不证明游戏原生销毁、Waylith resource 或 GPU 同步正确；Texture/RenderTexture 不经过 Texture2D 转换的类型路径可编译，真正的 1.7 肖像画面和失效行为仍须联合候选游戏测试。没有采集或改写正式存档。

## 尚未完成与下一步

本切片只处理列表图像。Badge、ClientPresentationMedia、composer 与其余 Texture 字段需继续沿同一 lease 边界整理，媒体的 Ready/Unavailable 与可见加载意图仍由 owner 决定，不能让 Host 接管下载或重复请求。

没有做 GPU 缓存或虚构 revision 更新；producer 自己提供 metadata/版本/失效证据。API 3.0 迁移不添加旧 WithIcon/WithIconSource overload 或客户端兼容分支。接下来完成其余图像入口，再整理主题 Color 与自绘、窗口/输入，最后接入新版绘制 API 和专门 1.6 adapter。当前不需要用户安装这个中间包进行游戏测试。
