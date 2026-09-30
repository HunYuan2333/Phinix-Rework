# 纯托管 WebP 渲染支持重新评估

> 评估日期：2026-09-17  
> 范围：RimWorld 1.6 客户端聊天中的外链 WebP 图片下载、解码与渲染  
> 结论性质：技术与架构评估；本文不包含代码改动

## 1. 执行结论

**纯 C#、不携带原生 DLL 的 WebP 解码在技术上可行，但在 Phinix 当前 `net472` 客户端上没有一个同时满足“受支持、可安全处理不可信输入、可直接投产”的现成版本。**

目前最接近要求的方案是 `SixLabors.ImageSharp 2.1.13`：

- 官方将其描述为 fully managed；不依赖 `libwebp.dll`、Skia 或其他原生库。
- 包含 `net472` 构建，并支持 WebP。
- 2.1.13 使用 Apache-2.0 许可证。
- 但它不是零依赖单 DLL：在 `net472` 下还依赖五个托管程序集。
- 更重要的是，ImageSharp 当前安全策略只为最新主版本提供安全修复；2.x 已属于旧主版本。当前最新版 4.1.2 仅面向 `net8.0`，无法直接加载到 RimWorld 的 `net472` 客户端。

因此本次建议为：

1. **不把 ImageSharp 2.1.13 直接作为正式 WebP 解码器发布。**它可以用于隔离的技术验证，但不应在没有维护方案的情况下处理任意互联网图片。
2. **不要自行实现或从 ImageSharp 抽取 WebP 解码源码。**VP8、VP8L、alpha、RIFF 分块和动画构成了较大的解析与安全表面，长期维护成本远超这个聊天功能的价值。
3. 如果后续接受“无原生 DLL，但允许额外托管 DLL”，可在聊天扩展内部建立解码适配器，并以 ImageSharp 2.1.13 做封闭 PoC；只有完成依赖兼容、恶意样本、性能和维护责任验证后才重新作发布决定。
4. 如果产品目标是“现在稳定支持 WebP”，应重新考虑限制条件：**使用持续维护的原生 `libwebp` 适配器**，或在可信服务端转换成 PNG/JPEG，均比在客户端固定一个停止安全维护的纯托管解码器更稳妥。

最终判定：**技术可行，当前严格约束下生产 No-Go；适配器 PoC 为 Conditional Go。**

## 2. “纯 C#、无外部 DLL”需要拆成三个不同目标

这三个概念不能混用：

| 目标 | ImageSharp 2.1.13 | 说明 |
|---|---:|---|
| 无原生 DLL / 无 P/Invoke | ✅ | 解码逻辑为托管代码，不需要 `libwebp.dll` |
| 不依赖额外托管程序集 | ❌ | `net472` 包有五个托管依赖 |
| 最终发布目录只有一个业务 DLL | ❌ 默认不满足 | 需要程序集合并或源码内嵌；两者都会增加构建、许可和运行时风险 |

本评估把用户真正关心的约束解释为：**不能携带平台相关的原生 DLL，避免 x86/x64、加载顺序和其他 Mod 的 native library 冲突。**如果要求字面意义上的“除了 `ChatExtension.Client.dll` 什么 DLL 都不能新增”，则当前没有推荐的成熟方案。

## 3. 当前实现为什么不能显示 WebP

### 3.1 当前项目约束

- `Extensions/Chat/Client/ChatExtension.Client.csproj` 目标为 `.NET Framework 4.7.2`。
- `ChatMessageList` 会识别 `.webp` 扩展名，但实际下载与纹理解码仍交给 Unity。
- 当前失败日志已经把 WebP 归为 Unity 无法解码的格式。

### 3.2 Unity 的能力边界

Unity 2022.3 官方 `ImageConversion.LoadImage` 文档只承诺加载 PNG 和 JPG 字节，不承诺 WebP。因此当前 `UnityWebRequestTexture` / `Texture2D.LoadImage` 路线失败是能力边界，不是简单的 MIME 或扩展名判断错误。

另外，Unity 文档明确说明运行时 `LoadImage(byte[])` 会强制走同步上传路径；即使 WebP 先转换成 PNG 再调用 `LoadImage`，仍可能把解码与上传造成的停顿集中到主线程。

## 4. 候选方案重新评估

### 4.1 ImageSharp 2.1.13：唯一值得做 PoC 的纯托管候选

优点：

- 官方包包含 `net472` 目标。
- 官方包描述为 fully managed，并列出 WebP 能力。
- 2.1.13 于 2025-11-25 发布，包含 WebP 对未知 RIFF chunk 的兼容修复。
- 该版本仓库许可证为 Apache-2.0，便于随 Mod 再分发，但仍需保留许可证和 NOTICE 要求。
- 可直接输出 `Rgba32` 像素，再由 Unity `Texture2D.LoadRawTextureData` 上传，不需要先转码成 PNG/JPEG。

阻断问题：

- ImageSharp 当前安全政策只维护最新主版本；旧主版本被定义为 EOL。2.1.13 即使当前 NuGet 页面没有已标注漏洞，也不代表未来发现的问题会回补。
- 最新 4.1.2 只提供 `net8.0`，不能被 RimWorld 的 `net472` 客户端直接引用。
- `net472` 的 2.1.13 依赖：
  - `System.Buffers >= 4.5.1`
  - `System.Memory >= 4.5.4`
  - `System.Numerics.Vectors >= 4.5.0`
  - `System.Runtime.CompilerServices.Unsafe >= 5.0.0`
  - `System.Text.Encoding.CodePages >= 5.0.0`
- 当前 Phinix 明确引用的是 `System.Memory 4.5.3` 和 `System.Runtime.CompilerServices.Unsafe 4.5.2`，并由 Host 打包。直接升级会影响 Protobuf、Host 和其他扩展，不再是聊天扩展的局部改动。
- RimWorld 的所有 Mod 共处同一运行时和程序集加载环境。把依赖散落到全局 `Assemblies` 目录，存在与其他 Mod 不同版本冲突的现实风险。
- NuGet 包约 4.38 MB；加上依赖后，代价明显高于“只补一个图片格式”。

判定：**PoC 可用，正式发布暂不通过。**

### 4.2 ImageSharp 4.1.2：安全维护较好，但运行时不兼容

- 这是评估时的最新版本，官方包仅包含 `net8.0`。
- 它采用 Six Labors Split License，并且 4.x 的许可执行方式与 2.x 不同。
- 即使许可可接受，也无法直接装入当前 `net472` 游戏客户端。

判定：**不适用于当前客户端。**除非 RimWorld/Phinix 客户端整体运行时升级，否则不应围绕它设计当前实现。

### 4.3 SkiaSharp、Magick.NET、libwebp wrapper

这些方案最终依赖原生二进制，通常还需要按 OS/CPU 架构分发。它们的格式成熟度和持续安全维护通常优于旧版纯托管方案，但违反本次“无原生 DLL”的前提。

判定：**技术上可靠，但不满足当前约束。**如果未来放宽约束，原生 `libwebp` 应重新进入首选比较，而不是默认使用旧版 ImageSharp。

### 4.4 Windows WIC / 系统 WebP codec

这种方法不一定需要随 Mod 携带原生 DLL，但它不是纯 C#，并依赖 Windows 版本、系统 codec 安装情况和 COM/WIC 行为。它也破坏了可预测部署，无法保证所有玩家机器表现一致。

判定：**不接受作为正式能力。**最多可做非保证的 fallback，且必须位于适配器内。

### 4.5 自研 WebP 解码器或复制第三方源码

“把源码编进现有 DLL”只能解决文件数量，不能消除复杂度、安全责任和许可证义务。WebP 不只是容器解析，还涉及 VP8/VP8L 位流、预测、熵编码、alpha、颜色转换、动画帧混合与 disposal。

对于来自互联网的不可信图片，解析器错误可能造成：

- 超大内存分配或解压缩炸弹；
- 无限循环、CPU 拒绝服务；
- 越界写入或运行时崩溃；
- 动画帧数量导致的内存放大；
- 畸形 RIFF chunk 长度和整数溢出。

判定：**明确反对。**这不符合最小侵入、边界防御和可维护性原则。

### 4.6 服务端转码

服务端可使用受支持的现代库把 WebP 转换成 PNG/JPEG，再把结果交给现有客户端路径。这样客户端不新增 codec，但会引入新的问题：服务端代请求的 SSRF 风险、带宽和缓存成本、内容审计、隐私边界，以及 URL 图片原本客户端直连语义的改变。

判定：**可作为独立后续方案评估，不能偷偷塞进当前聊天路径。**如果实施，必须有独立的代理/转码适配器、严格的出站网络策略和缓存上限。

## 5. 推荐架构：所有妥协停留在聊天图片解码适配器

即使只做 PoC，也不应让 ImageSharp 类型、版本判断或格式分支泄漏到聊天 UI、Host 或 Common。

建议边界：

```text
ChatMessageList
  -> ChatImageLoader（下载、缓存、任务去重、取消）
      -> IChatImageDecoder
          -> UnityPngJpegDecoderAdapter
          -> ManagedWebpDecoderAdapter（可选、隔离）
      -> DecodedImageBuffer（width, height, RGBA byte[]）
  -> 主线程 dispatcher
      -> Texture2D(RGBA32)
      -> LoadRawTextureData
      -> Apply
```

边界规则：

- `IChatImageDecoder` 和实现都属于 `Extensions/Chat/Client`；Host、Common、协议层无须知道 WebP。
- UI 只消费中性的 `DecodedImageBuffer`，不引用 ImageSharp 类型。
- 下载、格式嗅探、头部检查和纯托管解码可以在有界后台队列执行。
- `new Texture2D`、`LoadRawTextureData`、`Apply`、缓存替换和 `Destroy` 必须回到 Unity 主线程。
- 解码器失败只影响单张图片，显示统一占位符，不中断聊天列表绘制。
- codec 装配失败必须退化为 PNG/JPEG 正常工作；不能让可选 WebP 能力阻止 Chat 扩展启动。
- 如果未来换成 native、WIC 或服务端转码，只替换适配器，不改 UI 和聊天协议。

这符合当前设计哲学中的插件平等、Host 不依赖业务扩展、错误隔离、资源上限、生命周期对称和最小侵入原则。

## 6. 必须具备的安全和资源边界

WebP 来自外部 URL，必须按不可信输入处理。正式方案至少需要：

1. **按文件签名判断格式**：检查 `RIFF` + `WEBP`，不能相信扩展名或 HTTP `Content-Type`。
2. **下载上限**：流式读取时即限制压缩数据大小；建议初始上限 8 MiB，不能下载完成后才检查。
3. **尺寸预检**：解码前读取 VP8/VP8L/VP8X 尺寸并使用 checked 运算；建议单边不超过 4096，像素总数不超过 16,777,216。
4. **动画策略**：第一版明确拒绝 animated WebP，而不是无上限解码所有帧。检测 `VP8X` animation flag、`ANIM` 或 `ANMF` chunk 后返回可解释的占位提示。
5. **解码并发上限**：建议全局 1–2 个后台解码任务，有界等待队列；滚出缓存或窗口关闭时取消尚未开始的任务。
6. **内存预算**：RGBA 解码内存为 `width * height * 4`，还要计算解码器中间缓冲和 GPU 副本；缓存继续按条目数和估算字节数双重限制。
7. **超时与取消**：下载和后台解码都必须可超时；会话退出、返回主菜单和扩展 Shutdown 时取消任务并销毁纹理。
8. **主线程预算**：每帧限制纹理创建/上传数量，避免多个大图在同一帧 `Apply`。
9. **日志去敏**：记录格式、尺寸、阶段和错误类型；URL query 默认打码，防止 token 泄漏。
10. **失败缓存**：短时间缓存相同 URL 的确定性失败，避免每帧或每次滚动重新下载和解码恶意文件。

## 7. 如果批准 PoC，建议实施顺序

### 阶段 A：完全游戏无关的兼容实验

- 单独建立 `net472` 实验项目，固定 ImageSharp 2.1.13。
- 使用与当前客户端相同版本的 `System.Memory`、`Unsafe`、Protobuf 组合验证程序集加载；不要先改正式项目依赖。
- 测试静态 lossy、lossless、alpha WebP 到 RGBA 输出。
- 明确验证 32/64 位、Mono 运行时、线程池和 `System.Numerics.Vectors` 路径。
- 记录 DLL 数量、压缩后包体积和首次类型初始化耗时。

### 阶段 B：适配器与防御测试

- 先定义内部 `IChatImageDecoder` 和中性像素缓冲，不把 ImageSharp 暴露给 UI。
- 只支持静态 WebP，拒绝动画。
- 建立畸形 RIFF、截断 VP8/VP8L、极端尺寸、随机字节和超大 chunk 的回归语料。
- 所有测试必须证明失败可控、无无限循环、无无界内存增长。

### 阶段 C：游戏内验证

- 验证主线程只做 Texture2D 创建与上传，解码不在 `Draw()` 路径。
- 连续滚动包含 50–100 张图片的聊天记录，观察帧时间、GC.Alloc、CPU 和显存。
- 重复五轮“进服 → 打开聊天 → 加载图片 → 退出主菜单”，确认纹理、任务和回调完全释放。
- 与常见含 ImageSharp/System.Memory 的 Mod 联合加载，验证程序集冲突。
- 检查最终 `Output/phinix-rework` 内容，确认没有 native DLL，并核对许可证文件。

### 阶段 D：发布门槛

以下条件全部满足后才能重新评估 Go：

- 有明确的 2.x 安全维护责任人或可持续的 net472 fork 策略；
- 依赖版本不会破坏 Protobuf、Host 和其他扩展；
- 恶意语料与资源上限测试通过；
- 游戏内长时间滚动和会话重入测试通过；
- 包体、许可证和第三方声明经过检查；
- WebP adapter 可单独关闭，关闭后 PNG/JPEG 行为完全不变。

## 8. 设计哲学与开发者指南检查表

| 检查项 | 推荐方案状态 | 说明 |
|---|---:|---|
| Chat 仍是普通插件 | ✅ | 能力只放在 Chat Client 扩展内部 |
| Host 不引用 Chat/ImageSharp | ✅ | Host 和 Common 不新增业务依赖 |
| 兼容性妥协位于适配器 | ✅ | Unity、ImageSharp、WIC/native 差异不进入 UI/domain |
| 外部输入集中校验 | ✅ 必须 | 签名、长度、尺寸、动画和整数溢出预检 |
| 单图失败不破坏消息列表 | ✅ 必须 | 异常隔离并显示占位状态 |
| 队列和缓存有上限 | ✅ 必须 | 下载、解码、RGBA、Texture2D 均设预算 |
| Unity API 主线程约束 | ✅ 必须 | 后台只产出 RGBA，Texture2D 操作回主线程 |
| 生命周期对称 | ✅ 必须 | Shutdown/退主菜单取消任务、清缓存、Destroy 纹理 |
| Draw 热路径无解码/下载 | ✅ 必须 | 由状态变化驱动，不在每帧重试 |
| 响应式 UI | ✅ 无新增布局风险 | 沿用现有图片区域测量；失败提示需随宽度换行/截断 |
| 可维护的安全依赖 | ❌ 当前阻断 | net472 只能使用旧主版本 ImageSharp |

## 9. 决策记录

### 当前决定

- **不直接实现。**
- **不把 ImageSharp 2.1.13 引入正式客户端。**
- **允许后续建立隔离 PoC，但 PoC 不是发布批准。**
- 如果需求优先级提高，优先比较“受维护的 native libwebp 适配器”和“受控服务端转码”，不要默认接受 EOL 纯托管库。

### 重新打开决策的触发条件

- 出现仍维护 `net472`/`netstandard2.0` 的成熟纯托管 WebP 解码器；
- ImageSharp 新版本重新提供可用于 Unity/Mono 的目标框架；
- RimWorld 客户端运行时升级到可加载受支持 ImageSharp 的版本；
- 项目明确接受受维护的原生 codec；
- 产品明确接受服务端图片代理/转码的安全与运营成本。

## 10. 资料来源

以下资料均为官方文档、官方源码仓库或 NuGet 官方包页，访问日期为 2026-09-17：

- Unity `ImageConversion.LoadImage`：仅承诺 PNG/JPG 运行时解码  
  <https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ImageConversion.LoadImage.html>
- Unity 纹理加载说明：运行时 `LoadImage` 强制同步上传路径  
  <https://docs.unity3d.com/2022.3/Manual/LoadingTextureandMeshData.html>
- Unity `Texture2D.LoadRawTextureData`：数据布局和 `Apply` 要求  
  <https://docs.unity3d.com/2023.2/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html>
- Unity 线程约束：C# `new Texture2D` 必须在主线程  
  <https://docs.unity3d.com/2021.1/Documentation/ScriptReference/Texture-allowThreadedTextureCreation.html>
- ImageSharp 2.1.13 NuGet：框架、依赖、包体和 WebP 标签  
  <https://www.nuget.org/packages/SixLabors.ImageSharp/2.1.13>
- ImageSharp 2.1.13 项目文件：`net472` 目标与依赖版本  
  <https://github.com/SixLabors/ImageSharp/blob/v2.1.13/src/ImageSharp/ImageSharp.csproj>
- ImageSharp 2.1.13 许可证：Apache-2.0  
  <https://github.com/SixLabors/ImageSharp/blob/v2.1.13/LICENSE>
- ImageSharp 2.1.13 发布说明：WebP unknown chunk 修复  
  <https://github.com/SixLabors/ImageSharp/releases/tag/v2.1.13>
- ImageSharp 安全政策：只支持最新主版本  
  <https://github.com/SixLabors/ImageSharp/blob/main/SECURITY.md>
- ImageSharp 4.1.2 NuGet：当前版本仅含 `net8.0`  
  <https://www.nuget.org/packages/SixLabors.ImageSharp/4.1.2>
- ImageSharp 当前格式文档：WebP 为内建读写格式  
  <https://github.com/SixLabors/docs/blob/main/articles/imagesharp/imageformats.md>

