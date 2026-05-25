Trade 迁移完成后，请先不要直接大规模改代码。先做一次架构 review 和设计方案。

我发现目前 `MessagePipeline` 里可能混入了一些 Chat 业务逻辑，这会影响解耦和长期可维护性。现在重新明确 framework 的业务边界：

1. Framework Core 只负责抽象数据传输、pipeline、extension 生命周期、注册与发现机制。
2. Framework Core 不应该包含任何具体业务 extension 的 API，例如 Chat、Trade、RedPacket 等。
3. Chat 应该作为一个 built-in extension / official extension 存在，而不是 framework core 的一部分。
4. 属于 Chat domain 的逻辑，例如 ChatMessage、消息展示、聊天 UI、文本/图片/系统消息语义，都应该逐步迁移到 ChatExtension 内部。
5. Framework 层如果需要处理传输载体，命名和语义应避免继续使用容易和聊天混淆的 `Message`，请评估是否应改为 Packet / Envelope / Payload 等更基础设施化的概念。

接下来请你先输出一份设计文档，不要急着实现。文档需要包括：

- 当前 framework / chat / trade 的边界问题分析。
- 哪些逻辑应该留在 framework，哪些应该迁移到 ChatExtension。
- MessagePipeline 的语义整理建议，包括是否需要重命名，以及推荐命名。
- 一个轻量级 extension 注册机制设计。
- 一个轻量级 API discovery / capability registry 设计，例如 `RegisterApi<T>()` / `TryResolve<T>()`。
- TradeExtension 如何在不硬编码依赖 ChatExtension 实现类的情况下调用 Chat 提供的 API。
- 哪些功能本轮必须做，哪些明确推迟到 2.0，例如 dependency graph、versioning、hot reload、复杂插件依赖解析等。
- 给出最小实现方案和改动范围，优先保证简单、可维护、可测试，不要过度设计。

设计原则：

- 高内聚，低耦合。
- Framework 不理解具体业务。
- Extension 可以暴露自己的 API，其他 extension 通过 framework 查询 API。
- 插件开发者自己负责声明和处理前置依赖，framework 只提供最小注册和发现能力。
- 不要引入复杂 DI / IoC / dependency solver。
- 第一版目标是稳定、简单、可演化，而不是完整插件平台。
