# Inline chat image recovery / 聊天内联图片恢复

Chat downloads images directly from their HTTP/HTTPS URLs. An image-host timeout does not mean the chat server rejected the message, or that Unity decoded an unsupported format.

Only visible rows request uncached images. Each chat list allows at most two concurrent downloads and 64 pending unique URLs; duplicate URLs share one request. Connection errors and HTTP 408, 429, 500, 502, 503 and 504 receive at most three attempts, with 30/60/90-second timeouts and 2/8-second retry delays. Other HTTP errors and decoding failures stop automatic retries. Once retries end, clicking the failure row requests another bounded cycle. Unsupported formats offer opening the original URL instead. Loading/failure text follows the current game language.

Results are applied on the game main thread through the host dispatcher, or through Draw for standalone chat lists. Shutdown and message-buffer resets cancel active requests and abandon queued work; late results cannot update a replacement request. The 128-texture memory cache remains bounded. Evicted textures can be downloaded again when their row becomes visible. There is no persistent image download cache, protocol change or image-host availability guarantee.

`Tests/ChatRegressionTests` includes deterministic transport fixtures for concurrency, URL deduplication, bounded backoff, successful retries, permanent HTTP/decode failures, manual new requests, cancellation, late completions, worker-thread completions, callback isolation, queue capacity and start exceptions. These tests do not exercise native Unity downloads, decoding, clicking or real network routes; those require RimWorld validation.

聊天通过图片的 HTTP/HTTPS 原链接直接下载。图床请求超时不等于聊天服务器拒绝消息，也不等于 Unity 不支持图片格式。

仅可见行请求未缓存的图片。每个聊天列表最多同时下载两张，最多保留 64 个待处理的不同链接，相同链接共享请求。连接错误和 HTTP 408、429、500、502、503、504 最多尝试三次，超时分别为 30/60/90 秒，重试前分别等待 2/8 秒。其他 HTTP 错误及解码失败停止自动重试。重试耗尽后可点击失败行，开始新一轮有限重试；格式不支持时提供点击打开原图。加载与失败提示随当前游戏语言变化。

结果通过宿主 dispatcher 在游戏主线程应用；独立创建的聊天列表则在 Draw 中处理。插件关闭或消息缓存重置会取消活动请求并放弃排队工作，过期结果不会修改新请求。内存纹理缓存仍限于 128 张，被淘汰的图片再次进入可见区域时可以重新下载。未引入持久化图片缓存、协议变更或图床可用性保证。

回归测试覆盖并发限制、相同链接合并、有限退避、重试成功、永久 HTTP/解码失败、手动新请求、取消、过期回调、后台线程完成、回调隔离、队列容量及启动异常。测试不覆盖 Unity 实际下载、解码、点击及玩家网络线路，仍需游戏内验收。
