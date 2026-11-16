# Relay 与错误处理合并安排

评估日期：2026-11-16。按用户授权继续错误处理，并在完成后整合 relay；本轮读取本地分支和其已保存的 R5 记录，没有切换分支、合并、暂存或提交当前工作树。机器可读范围见 `relay-integration-assessment.json`。

## 合并起点

**E3 完成 Host 路径 → E4 完成内置插件错误迁移 → M1 整合 relay 并覆盖新增错误路径 → E5 联合回归与发行闭合。**

现在可以准备冲突清单与检查项；实际代码合并从 M1 开始。把 M1 放在 E5 前，使回归针对最终合并代码，避免把两条分支各自的历史通过记录当作联合验收。

relay 本地分支为 `codex/command-capability-relay`，提交 `311fdd6`；其 R5 记录注明 2026-11-10 的回归与用户反馈，并固定 Common 到 `911047cf8a6dd34e661e107e182e9c921b8e9586`。这些是 relay 旧版本的证据，不代表当前错误处理/SQLite 工作树已经通过同样测试。未联网检查远端是否有更新。

## 已核对的冲突面

- `PhinixFrameworkClient`：双方都改消息管线、predicate 隔离、生命周期和观察者。Relay 新增 ACK/Result、能力声明、timer、sendRelayPacket；其中仅记类型的 catch 必须接入错误服务。保留 E3 的普通发送 NotSent/Unknown 行为，不能恢复原先吞失败的 sendPacket。
- `NetClient`：Relay 新增固定 UDP 接收 guard、分片/收包预算、积压拒绝、轮询失败终止。当前分支新增原异常诊断和 worker 停止；两边要结合，不能用一种覆盖另一种。原分支使用具体 NetClient，当前 Host 使用 `IClientTransport`/ConfiguredClientTransport/TLS；`TrySendRelay` 需要适配当前传输边界，不能直接替换回旧构造函数，也不能绕过 relay 预算直接调 Send。
- Common：Relay 有 `CommandRelay`、`ClientCommandRelay`、FrameworkPacket/FrameworkTypes/Registry 更改；错误处理也改 Types/Registry 并新增安全诊断。当前 Common HEAD `c165f61` 是 relay Common HEAD `911047c` 的祖先，但错误处理修改尚未提交，不能把更新 gitlink 视为已整合这些修改。
- Phase35 夹具双方都补过当前客户端入口与编译清单；合并按实际依赖补齐，避免重复类型、漏掉新诊断源码或削弱原断言。
- 当前工作树还含独立的 Inventory/Trade/SQLite 修改。先明确保存这些来源与提交边界；不得用 reset/checkout/stash 清空，或把全部未提交内容混作 relay 提交。

## M1 执行顺序

1. 冻结已完成错误处理的源码指纹和验证结果，复核 relay 与关联服务端/Example 的实际提交。保存工作树来源，不直接在大量未提交更改上执行盲合并。
2. 先在 Common 整合安全诊断与 relay 契约/注册，验证 Registry/序列化/Relay 生命周期，提交共同结果得到新的精确 SHA。
3. 在客户端整合 Host、transport、测试和示例；让客户端 pin 新的 Common SHA，而不是把当前 gitlink简单改成旧 relay SHA。Common 的 SDK 合同与 Host 新错误服务必须成套。
4. 补审 relay 新增的 catch/throw 与后台入口：ACK/Result 坏包、匹配谓词、timer、观察者、关闭和发送异常。预期拒绝仍是正常结果；原故障保留原因链/堆栈，未知交付不触发重发/退款。
5. 与对应服务端组合校验 Common，补上 Example 的 relay API 缺口，再进入 E5。代码提交/合并与发布分开，不能把合并授权理解成发布、更新 main、替换已交付包或部署服务器。

## 联合验收门槛

- 1.6/net472 与 1.7/net10 Host、内置模块、RedPacket/TalentTrade，以及 CommandRelay Sample/Example 的实际构建。
- 错误窗口/复制与原栈传播、诊断观察者失败隔离；普通消息发送 NotSent/Unknown。
- Relay 能力声明/撤销、epoch/revision、ACK/Result、零匹配/部分提交/超时 Unknown、旧 epoch 回调和关闭清理。
- UDP 收包/分片预算、Relay 出站积压与正常排空；TLS/Configured transport 的准入、取消和生命周期。不为了通过编译退回具体 UDP 依赖。
- Chat、Trade、LegacyAdapter、SQLite 事务与不确定操作回归；合并后没有新增发送、退款或物化。
- 当前代码的游戏内窗口/剪贴板与联机验收仍需按 E5 单独记录，不能引用 relay 历史黑盒反馈代替。
