# 第三方接入 API 规范（第一版实验）

日期：2026-10-11。用户最新要求：广泛开放可接入点，先形成一致规范，在 Example 实验；不要求每个新增接口先有内置消费者。本文取代旧评估中该强制门槛。规范尚未正式发行冻结，UI/UX 重构不属于本任务，线上更新继续延期。

## 1. 目标与分层

普通插件应能通过公开接口读取、提交操作、贡献 UI/业务能力、订阅变化，无需修改 Host、反射私有 UI 或知道旧客户端/协议。Example 是规范实验场，不是 Host 的第二套实现。内置消费方可验证和推广接口，不再逐项作为新增接口交付前置。

| 层 | 定义的接入能力 | 边界 |
| --- | --- | --- |
| Host 通用基础 | 模块身份/生命周期、任务结果、资源/配置/存储作用域、通用交互与贡献目录 | 不声明 Chat/Trade/Inventory 业务类型，不中转所有业务 |
| 插件 Contracts | typed 上下文、读取/意图/结果、业务扩展点名称与内容 | 不暴露 private view、容器、wire DTO 或可写权威列表 |
| 视图适配器 | 收集贡献、呈现菜单/工具栏/行操作/详情、执行并展示结果 | 样式和几何可调整，调用语义/所有权不藏在布局代码 |
| 协议/历史输入 adapter | 当前端口与旧接口/数据/远端的转换 | 普通业务没有旧版本、模式、schema 分支 |

## 2. 一致的接入规则

1. **模块身份**：当前作用域绑定实际正在激活的模块实例，不允许调用方伪造 owner ID。服务借用，scope/注册/订阅和短命结果集拥有；部分激活失败及 Shutdown 均撤销。
2. **命名**：定义点使用 owner.ExtensionId 前缀；第三方贡献引用已声明点名和当前 Contracts 类型。稳定 ID 与显示标签分开，显示文本可本地化。类型/排序/额度校验在输入边界。
3. **读取**：返回防御性复制或不可变模型，有明确失效版本；快照不得成为权威写入口。插件不长期持有旧 Map/Thing 等对象。
4. **操作**：拒绝、取消、回调完成与业务确认分开。已经调用但失败可能已有副作用，调用方不能自动重试；幂等/未知结果由所属业务 API 处理。
5. **贡献**：注册 factory，按打开/刷新时的 typed 只读上下文计算可见/禁用操作；收集不修改业务。闭包/上下文寿命局限于结果集，点击时重新检查 owner 与上下文。
6. **可选能力**：缺少 optional point 可以保留声明等待对应 provider；不制造兼容模式或直接 fallback 到原始传输。必需模块/API 仍应声明依赖并在缺失时明确失败。
7. **线程**：贡献绑定/收集/执行/释放在主线程；任务 Submit/Capture 按任务契约支持跨线程，实际回调主线程执行。后台只处理纯数据。
8. **异常**：factory 及 lazy enumeration 分别纳入同一隔离边界，一个贡献失败不阻断其他贡献；GUI 状态恢复，日志有 owner/阶段，结果不暴露任意异常对象/消息。
9. **额度**：注册、factory 条目数、活动结果集有界；不每帧反射/全模块扫描。窗口关闭、结果替换、scope 关闭必须释放。
10. **兼容**：普通接入只用当前契约。两个维护插件的旧 1.9 绑定留内部 adapter；API/CLR/包/数据版本分别验证，不把未知主题键当旧接口。

这些是同进程工程规范，不是 DLL 安全沙箱，也不新增强制签名或第二套插件发现机制。

## 3. 首批代码：通用强类型操作点

已实现 ClientExtensionAbstractions/Framework/IClientExtensionPoints.cs 与 Host ClientExtensionPointService：

- IClientExtensionPointService.ForModule(actualModule) → 独立 owned scope。
- scope.DefineActions<TContext>(pointId) → 当前 owner 定义的 typed point。
- scope.ContributeActions<TContext>(pointId, factory, order) → 第三方追加操作。
- point.Collect(context, isContextCurrent) → owned IClientActionSet，当前只读操作元数据与 TryExecute。
- service.CapturePoints / Revision → 当前点、owner、context type、匹配贡献数量的目录快照。

这是模块生命周期下的专用贡献目录，复用 Common 的发现/激活准入，不接管其 DI、依赖或 API 注册。当前 Scope 可在 optional point 尚未出现时声明贡献；定义/收集时只匹配同一 .NET 上下文类型。已定义的类型不匹配立即拒绝，后来定义不匹配的 pending 贡献不参与收集，不做 object payload 转型。

一组操作可由视图呈现为菜单、工具栏或行内按钮；接口不引用 FloatMenuOption，也不将所有 UI 拼成字符串挂点。首批只覆盖操作集合；详情内容块、展示装饰、通知等类型后续按相同生命周期/结果规范各自扩展，不伪装为无业务假操作。

### 限额与结果

- 每模块最多 16 个活跃 scope；每 scope 最多 128 项定义/贡献；总计 256 个定义、1024 项贡献、64 个活动结果集。
- 同一模块对同一点只注册一个 factory；factory 最多 32 项，单结果集最多 128 项。action ID 最长 64，label 256，disabled reason 512，point ID 160；非法/重复 factory 条目整批丢弃并诊断。
- 顺序为 order、owner ID、注册次序，factory 内部顺序保留。可选 point 缺失不被误标为业务可用。
- TryExecute 返回 Completed、Invoked、Code：正常返回为 Completed=true/Invoked=true；CallbackFailed 为 false/true，可能已有副作用；禁用/上下文失效/关闭/重复点击为 false/false。
- 每个收集动作最多调用一次；失败也不自动重试。再次收集不是业务幂等保证。上下文 guard 抛异常返回 ContextUnavailable；已过期为 ContextExpired。
- Dispose/owner 停止撤销动作，清理 callback 与 context guard；后续执行返回 ContributionClosed。factory 与执行均恢复 GUI/Text 状态，不处理网络确认。

## 4. Example 实验

Example 1.0.5 定义 public ExampleActionContext 和 phinix.example.basic.actions，注册两个操作：计数、打开重置确认。以零计数禁用原因、计数/代次上下文 guard、owned 菜单结果集与 scope 清理展示规则。另一个独立插件可引用该实验 context 并贡献同一点；生产业务 context 推荐独立 Contracts 工程。

页面“打开接入操作”通过收集结果构造 FloatMenu，显示执行 code；菜单只是展示 adapter。Example 不反射 Host/业务内部 UI、不声称 Chat/Trade 菜单已经开放、不模拟真实远端成功。

这次实验没有运行自动化或游戏测试。当前只在 Example 自己的点验证作者用法；跨插件贡献、不同上下文挂点和真实视图接入仍待后续核对。

## 5. 广泛开放的下一批

继续围绕同一规范增加：provider 查询/可用状态、模块资源和存储作用域、选择/确认结果、typed 菜单/工具栏/行/详情/消息展示、Chat/Trade/Inventory/Store 高层读取和意图。每项先提供可接入契约与默认行为，并在 Example 的有限实验入口演示；不为等待内置 UI 迁移而阻塞新增能力。

旧接口删除仍须核对真实现有消费者，以保护维护插件/历史数据；这个删除条件与“新 API 必须有内置消费者”不同。T5 存储事务继续原计划，UI 评估交接门槛已按最新要求调整。


## 本批 A1 扩展

IClientApiDirectory 从实际 registry/模块状态返回 typed provider，TryGet 每次核对精确注册身份，API 借用；IClientLifetimeService 在实际模块下拥有资源/主线程订阅、停止 token、迟到 callback 隔离和逆序清理。Example 1.0.6 有独立实验窗口，详见 Provider-and-Lifetime-Api-Implementation.md。模块文件资源/存储和更多业务/内容接口仍待接续；线上 API 冻结、现有发布流程改造与快捷上传一并延期。
