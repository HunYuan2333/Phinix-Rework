# Chat 插件引入图片聊天可行性评估

> 评估日期：2026-05-31
>
> 评估范围：在现有 Chat Extension 架构基础上，引入图片聊天功能的可行性，涵盖网络性能影响、极端情况推演、UI 绘制性能。

---

## 一、当前架构概览

在深入分析前，先确认当前 chat 消息管道的完整链路：

```
客户端UI → TryHandleOutgoingMessage → BuiltInChatClientExtension
→ protobuf序列化(BuiltInChatMessagePayload) → FrameworkPacket.PayloadBytes(byte[])
→ DataContractJsonSerializer(JSON, PayloadBytes被base64编码) → NetDataWriter
→ LiteNetLib ReliableOrdered → 服务器接收 → 广播到所有客户端 → 客户端解析渲染
```

关键发现：

- **外封套是 JSON**（`DataContractJsonSerializer`），`byte[]` 类型的 `PayloadBytes` 会被自动 base64 编码（[FrameworkSerialization.cs:93-101](../Common/Utils/Framework/FrameworkSerialization.cs#L93-L101)）
- **内载荷是 protobuf 二进制**（`BuiltInChatMessagePayload`），当前只有文本字段（[BuiltInChat.proto](../Extensions/Chat/Contracts/Proto/Message/BuiltInChat.proto)）
- **无显式包大小限制**，LiteNetLib `ReliableOrdered` 通道自动处理分片（[NetClient.cs:315-334](../Common/Connections/NetClient.cs#L315-L334)）
- **服务端广播到所有在线客户端**（[BuiltInChatServerExtension.cs:76-95](../Extensions/Chat/Server/BuiltInChatServerExtension.cs#L76-L95)）
- **服务端内存存储历史** + 磁盘持久化（[PhinixFrameworkChatService.cs (Server)](../Extensions/Chat/Server/PhinixFrameworkChatService.cs)），默认 40 条
- **客户端内存存储** 最大 1000 条（[PhinixFrameworkClient.cs:40](../Client/Source/Framework/PhinixFrameworkClient.cs#L40)）

---

## 二、原始设想路径逐环节评估

用户设想的路径：

> 游戏内 UI 提供选择图片接口 → 自动默认压缩成 WebP → 转 base64 → 走 message handle 解析显示 → 客户端本地缓存 → 定期清理

### 2.1 游戏内 UI 提供选择图片接口

**可行性：✅ 可行**

RimWorld/Unity 环境下可以通过 `System.Windows.Forms.OpenFileDialog` 或 Unity 的 `FileBrowser` 实现。已有类似的 UI 模式（如 `FloatMenu`、扩展管理器 UI 等）可以参考。

### 2.2 自动默认压缩成 WebP

**可行性：⚠️ 需引入第三方库**

- .NET Framework 3.5/4.7.2（RimWorld 目标框架）原生不支持 WebP 编码
- 需要引入 **SkiaSharp** 或 **libwebp** 的 .NET binding
- 替代方案：使用 `System.Drawing`（仅 Windows）压缩为 JPEG，质量和体积也是可接受的（JPEG quality 70 约等于 WebP quality 75 的体积）
- Unity 的 `Texture2D.EncodeToPNG()`/`EncodeToJPG()` 是 Editor API，运行时不可直接使用

**推荐替代路径**：在客户端使用 `System.Drawing.Bitmap` → 缩放 → JPEG 压缩 → byte[]，因为 RimWorld 本身只在 Windows 上运行。

### 2.3 转 Base64

**可行性：⚠️ 不需要（架构设计建议）**

这是原设想路径中最大的架构问题。当前的 `FrameworkPacket.PayloadBytes` 已经是 `byte[]` 类型（[FrameworkPacket.cs:55](../Common/Utils/Framework/FrameworkPacket.cs#L55)），它可以直接承载二进制图片数据。**不需要先 base64 再放进去**——外封套 JSON 序列化时 `byte[]` 自动被 `DataContractJsonSerializer` 处理为 base64 字符串。如果先把图片 base64 再放进 protobuf 的 string 字段，等于做了两次 base64 编码。

**正确做法**：在 `BuiltInChatMessagePayload` proto 中新增一个 `bytes image_data` 字段，直接把 JPEG 二进制写进去。

### 2.4 走 Message Handle 解析显示

**可行性：✅ 可行，但需要扩展**

当前 `IClientMessageHandler` 链可以正确处理新的 `MessageType`（如 `"builtin.chat.image"`），利用现有的 Priority/拦截/渲染机制。`IMessageRenderer` 可以扩展支持图片渲染。

### 2.5 客户端本地缓存 + 定期清理

**可行性：✅ 可行**

客户端已有 `FileSystemExtensionStorageProvider`（[FrameworkTypes.cs:283-313](../Common/Utils/Framework/FrameworkTypes.cs#L283-L313)），路径为 `framework-extensions/client/`。可以在此目录下创建图片缓存，通过 LRU 策略 + 定期清理（如 7 天过期、总量上限 50MB）管理。

---

## 三、网络性能影响（核心关注点）

### 3.1 单张图片的完整网络开销计算

假设一张 1920×1080 游戏截图：

| 阶段 | 格式 | 大小 |
|------|------|------|
| 原始截图 | PNG (RGBA32) | ~8 MB |
| 压缩后 | JPEG quality 75 | ~150 KB |
| protobuf `bytes` 字段 | 二进制（无膨胀） | ~150 KB |
| `PayloadBytes` (byte[]) | 二进制 | ~150.3 KB（含其他字段） |
| **JSON 外封套序列化** | base64(byte[]) + JSON | **~200 KB**（33% 膨胀） |
| **最终网络传输** | LiteNetLib 分片发送 | **~200 KB** |

**对比纯文本消息**：~0.5-2 KB。图片消息是文本消息的 **100-400 倍**。

### 3.2 广播放大效应

当前服务端对每条 chat 消息执行**全量广播**（`context.BroadcastMessage()`），不区分消息类型：

| 在线玩家数 | 单张图片服务端出流量 | 带宽占用（假设每秒1张） |
|-----------|-------------------|----------------------|
| 5 | ~1 MB | ~8 Mbps |
| 20 | ~4 MB | ~32 Mbps |
| 50 | ~10 MB | ~80 Mbps |

对于小型私服（5-10 人）这是可接受的。超过 20 人后，图片频率如果不加限制会显著影响服务端带宽。

### 3.3 LiteNetLib 层的影响

- `ReliableOrdered` 交付保证可靠有序，但大包分片后的重组会阻塞同通道的后续小包
- 如果用同一个 `ReliableOrdered` 通道发送图片和文本，一个大图片的多个分片会**阻塞后续文本消息**直到图片传输完成
- **建议**：图片消息使用独立的发送通道，或使用 `Sequenced` 交付模式，或至少对图片消息做**单独队列**

### 3.4 JSON 外封套的 33% 膨胀

这是不可忽视的固定开销。`DataContractJsonSerializer` 会将 `byte[]` 自动序列化为 base64 字符串，导致 33% 的体积膨胀。如果后续需要优化，可以考虑：

- 短期：接受 33% 膨胀，200 KB 的单张图片在局域网上仍可接受
- 长期：将 FrameworkPacket 整体迁移到纯 protobuf 序列化，消除 JSON 外封套。这会是一次较大的重构，但收益显著

---

## 四、极端情况推演

### 4.1 大图攻击/滥用

**场景**：用户选取 4K（3840×2160）未压缩截图

- 压缩前：~32 MB
- JPEG 压缩后（quality 75）：~500 KB
- 外封套后（33% 膨胀）：~667 KB
- 服务端广播到 20 人：~13.3 MB

**影响**：
- 发送端 Frame drop（压缩 4K 图片耗时 ~50-200ms，取决于 CPU）
- 所有接收端同时收到 667 KB 数据，解析 JSON + protobuf 耗时
- 无**客户端侧图片大小限制**和**服务端侧大小限制**会导致此问题

**缓解策略**：
- 客户端强制缩放：最大分辨率 1280×720（或 1024×768）
- 客户端 JPEG quality 设为 60-70（肉眼几乎无法区分）
- 服务端拒绝超过 512 KB 的 PayloadBytes
- 限制发送频率：每用户每分钟最多 3 张图片

### 4.2 服务端内存/存储爆炸

当前 `messageHistory`（默认 40 条）存储的是完整 protobuf。如果 40 条中有 20 张图片：

- 20 × 150 KB = 3 MB 纯聊天历史（对比纯文本 ~20 KB）
- 磁盘持久化 `chat-history.bin`：3 MB（对比纯文本 ~5 KB）
- 1000 条客户端缓存：最多 ~150 MB 内存占用

**缓解策略**：
- 服务端历史只存储图片的**元数据 + hash**，图片实体分离存储
- 客户端 `MaxDisplayMessages = 1000` 在图片场景下需要调整（或只在内存中保留缩略图引用）

### 4.3 慢速连接用户

- 上行 1 Mbps：发送 200 KB 需要 ~1.6 秒
- 下行 1 Mbps：接收 200 KB 需要 ~1.6 秒
- 期间其他文本消息可能被阻塞

**影响**：慢速用户可能造成消息延迟感知。但考虑到 RimWorld 多人通常是局域网或国内服务器场景，影响有限。不过如果未来扩展到公网服务器，需要提供"自动跳过图片，只显示文字"的客户端选项。

### 4.4 并发图片风暴

**场景**：5 个用户同时发送图片

- 服务端瞬间处理 5 × 200 KB = 1 MB 入流量
- 广播到 20 人 = 20 × 1 MB = 20 MB 出流量
- 服务端 protobuf 解析 5 次（每次 ~5ms），可接受
- JSON 序列化 20 × 5 = 100 次（每次 ~10-50ms），在高并发下可能成为 CPU 瓶颈

**缓解**：服务端实现基于令牌桶的速率限制（token bucket），平滑突发流量。

---

## 五、UI 绘制性能

### 5.1 当前渲染机制

[ChatMessageList.cs](../Extensions/Chat/Client/ChatMessageList.cs) 的渲染流程：

1. `recalculateMessageRects()` (line 157-189)：用 `Text.CalcHeight()` 计算每条消息高度（纯文本度量）
2. `Draw()` (line 50-106)：每帧遍历可见消息，调用 `drawChatMessage()`
3. `drawChatMessage()` (line 191-237)：用 `Widgets.Label()` 渲染富文本

**问题**：`Text.CalcHeight()` 无法计算带图片的消息高度。需要自定义高度计算逻辑。

### 5.2 关键性能问题

#### Texture2D.LoadImage() 阻塞主线程

- 从 byte[] 加载 150 KB JPEG → `Texture2D.LoadImage()` 约 5-20ms（取决于图片大小和硬件）
- 如果在 `Draw()` 中调用，会导致**每帧卡顿**
- **必须异步加载**：通过 `IClientMainThreadDispatcher.Enqueue()` 在接收消息时分帧加载纹理

#### 纹理内存占用

| 纹理尺寸 | RGBA32 内存 | DXT5 压缩 |
|---------|------------|----------|
| 256×256 | 256 KB | 64 KB |
| 512×512 | 1 MB | 256 KB |
| 1024×512 | 2 MB | 512 KB |

如果聊天记录中同时有 20 张图片纹理驻留内存，按 512px 宽缩略图计算：20 × 1 MB = 20 MB GPU 内存。

**缓解策略**：
- 显示缩略图（最大宽度 400-512px），不加载全分辨率
- 点击查看大图时再加载全分辨率到独立窗口
- 滚动出屏幕的消息**卸载纹理**（`UnityEngine.Object.Destroy()`）
- 使用 `Texture2D.Compress()` 减少 GPU 内存占用

### 5.3 高度计算优化

带图片的消息高度 = 文本高度 + 图片高度 + padding。由于图片宽度固定（如 400px），高度 = 400 / 原始比例。

`messageRectCache` 字典缓存（line 27）已经存在，但需要新增图片尺寸字段来计算正确高度。

### 5.4 滚动性能

- 如果每条图片消息都持有一个独立 Texture2D，滚动时需要 GPU 频繁切换纹理绑定
- **优化**：使用 Texture Atlas 将多个缩略图合并为一张大纹理，减少 draw call
- 或者接受 Unity/RimWorld 的渲染限制，通过限制同时可见图片数量（如最多 3 张）来控制性能

---

## 六、综合可行性与建议

### 6.1 可行性总评：🟡 可行但需要较多工程改动

| 模块 | 可行性 | 主要风险 | 工程复杂度 |
|------|-------|---------|-----------|
| UI 选择图片 | ✅ 可行 | 低 | 低 |
| 图片压缩 | ✅ 可行 | 需 System.Drawing 或第三方库 | 中 |
| Proto 扩展 | ✅ 可行 | 低 | 低 |
| Message Handler | ✅ 可行 | 低 | 低 |
| 网络传输 | ⚠️ 需关注 | 广播放大、大包阻塞、JSON 外封套膨胀 | 中 |
| 服务端处理 | ⚠️ 需关注 | 内存/存储膨胀、无频率限制 | 中 |
| UI 渲染 | ⚠️ 需关注 | LoadImage 阻塞主线程、纹理内存 | 中 |
| 本地缓存 | ✅ 可行 | 低 | 低 |

### 6.2 推荐的实现优先级

#### 第一阶段（核心通路）

1. **扩展 proto**：[BuiltInChat.proto](../Extensions/Chat/Contracts/Proto/Message/BuiltInChat.proto) 增加字段：
   ```protobuf
   bytes image_data = 5;        // JPEG/WebP 二进制
   string image_mime_type = 6;  // "image/jpeg" 或 "image/webp"
   int32 image_width = 7;       // 图片原始宽度（用于客户端缩略图比例计算）
   int32 image_height = 8;      // 图片原始高度
   ```
2. **新增 MessageType**：`"builtin.chat.image"`，新建 `IClientMessageHandler` + `IMessageRenderer` 处理图片消息
3. **客户端图片处理**：`System.Drawing.Bitmap` 缩放 + JPEG 压缩，最大分辨率 1280×720，quality 70
4. **ChatMessageList 扩展**：用 `GUI.DrawTexture` / `Widgets.DrawTextureFitted` 渲染缩略图，异步加载纹理

#### 第二阶段（安全与性能）

5. **服务端 PayloadBytes 大小上限**：256-512 KB
6. **发送频率限制**：客户端和服务端双层限制，每用户每分钟 3-5 张
7. **图片使用 protobuf `bytes` 字段**：不做额外 base64，直接利用现有 `PayloadBytes` 二进制通道
8. **纹理 LRU 缓存 + 滚动卸载**：可见范围外的纹理释放，重新进入时异步重载

#### 第三阶段（体验优化）

9. **服务端历史存储分离**：文本 + 图片引用，图片存到独立文件或 blob store
10. **缩略图预生成**：服务端生成缩略图，客户端先收到缩略图，点击加载原图
11. **大图独立窗口查看**：类似 RimWorld 的物品预览窗口，支持缩放和保存
12. **慢速连接兼容选项**：客户端设置项 "自动跳过图片，仅显示 [图片] 占位符"

### 6.3 关键架构决策

原设想的 **"WebP → base64 → message string"** 路径建议调整为：

> **JPEG/WebP → protobuf `bytes image_data` → `PayloadBytes`(byte[]) → 现有 JSON 外封套自动处理 → LiteNetLib**

理由：

1. 去掉多余的 base64 编码步骤，直接将二进制图片放入 protobuf bytes 字段
2. 外封套 JSON 序列化 `byte[]` 时的 base64 是**不可避免的**（除非修改序列化方案），但至少不会双重编码
3. JPEG 压缩在 .NET Framework 上原生支持（`System.Drawing`），无需额外依赖
4. 如果后续需要优化网络开销（去掉外封套 JSON 的 33% 膨胀），可以评估将 FrameworkPacket 整体迁移到纯 protobuf 序列化——这会是一次较大的重构，但收益显著

### 6.4 需要进一步确认的前置条件

| 前置条件 | 状态 | 备注 |
|---------|------|------|
| RimWorld 运行时的 .NET 版本 | 待确认 | 影响可用 API（System.Drawing 等） |
| LiteNetLib `NetDataWriter.Put(byte[])` 的最大长度 | 待测试 | 理论上无限制，但需实测大 byte[] 的表现 |
| Unity `Texture2D.LoadImage` 在主线程调用的性能上限 | 待测试 | 需实测 150-200 KB JPEG 的加载耗时 |
| 服务端磁盘 I/O 对大文件的处理能力 | 待确认 | history 持久化路径是否在有 IOPS 限制的磁盘上 |

---

## 七、替代方案

### 7.1 图片链接（最小改动）

不传输图片数据，只允许用户在消息中粘贴图片链接（如 `https://i.imgur.com/xxx.png`）。在 ChatMessageList 的 URL 解析（[line 19](../Extensions/Chat/Client/ChatMessageList.cs#L19)）基础上，识别图片链接并渲染为缩略图。

| 优点 | 缺点 |
|------|------|
| 零网络开销 | 需要用户自行上传到图床 |
| 几乎不需要服务端改动 | 依赖外部服务 |
| 现有 URL 解析机制可直接复用 | 链接失效后图片不可见 |

### 7.2 富媒体消息框架（长期方案）

不针对图片单独设计，而是在 FrameworkPacket 层面引入 `Attachment` 概念——每条消息可以附加 0-N 个附件（图片、文件等）。附件通过独立的传输通道发送，消息本身只携带附件引用。

| 优点 | 缺点 |
|------|------|
| 通用性强，可扩展到文件、音频等 | 工程量大 |
| 附件和消息解耦，互不阻塞 | 需要新增附件存储和传输子系统 |
| 更好的架构设计 | 当前阶段可能 over-engineering |

---

## 八、关联文档

- [设计哲学](设计哲学.md) — Section 3.7 规定所有外发通信必须经过 handler pipeline
- [架构耦合度与内聚度评估](架构耦合度与内聚度评估.md) — 扩展系统边界分析
- [framework-protobuf-design](framework-protobuf-design.md) — 当前 protobuf 设计
- [现存问题](现存问题.md) — 已知的 UI/架构相关问题
