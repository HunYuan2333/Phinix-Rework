# R5 白盒测试报告

执行日期为 2026-11-09。客户端与服务端分支均为 codex/command-capability-relay。本次按当前工作树重建并运行服务端与客户端核心测试，重跑完整服务端阶段检查及客户端组件测试。

## 本次结果

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 服务端核心白盒 | 13 组、109 条断言通过，构建 0 错误 | /tmp/phinix-relay-whitebox-report-build.log，Phase35ServerRuntimeTests 实际执行输出 |
| 服务端完整阶段回归 | 50 项检查全部通过 | /tmp/phinix-relay-r5-report-stage/manifest.json |
| 客户端核心白盒 | 7 组、97 条断言通过，构建 0 错误 | /tmp/phinix-relay-r5-report-client-build.log，Phase35ClientRuntimeTests 实际执行输出 |
| 客户端收包预算与线程生命周期 | 真实网络预算场景通过，线程生命周期 18 条断言通过 | /tmp/phinix-relay-r5-report-transport-lifetime/manifest.json 及 test.log |
| 聊天回归 | 20 组通过 | /tmp/phinix-relay-r5-report-chat-regression/manifest.json 及 test.log |
| 旧交易 adapter 回归 | 12 组、74 条断言通过 | /tmp/phinix-relay-r5-report-legacy-trade/manifest.json 及 test.log |
| 服务端 UDP 接收预算专项 | 通过 | ServerLifecycleTests.dll --udp-receive-budget 实际执行输出 |

服务端阶段检查包含真实 UDP Relay、身份、TLS、Chat、Trade、LegacyAdapter、持久化、参考模型、架构、发布构建及独立进程恢复。50 项包含构建与验证步骤，具体分类以 manifest 为准。

核心测试覆盖声明所有权、并发声明、模块失败回滚、同步乱序和恢复、迟到 ACK、重连 epoch、版本匹配、单播和广播、权限、Replace 后验证、错误及逐连接投递结果、报文兼容和预算。十万次同时间片洪峰验证确定性限流。两端实际固定 DLL 的接收预算测试覆盖 1 MiB 有效载荷、异常分片和合包、重组预算、事件数与载荷字节水位、排空恢复及故障处理。

本次未新增发现失败项。服务端核心构建有既有 Protobuf 裁剪配置警告。客户端核心构建有既有 Protobuf 和过时成员警告，均为 0 编译错误。客户端组件回归使用已完成完整 net472 构建的实际程序集闭包，本次重跑组件测试。完整客户端此前构建证据见 R0–R4 完成记录。

## 结果范围

白盒与本机网络结果通过。真实发行版交叉部署、游戏内操作、长时压力、慢接收者及进程峰值内存仍属于用户执行的 R5 黑盒和验收范围。Submitted 表示网络层接受提交，插件业务执行成功需要独立接收记录或业务 ACK。

## 黑盒配套要求

基础 Relay 测试需要候选 Framework Server，以及安装测试客户端插件的发送方和接收方。当前 Samples/CommandRelay 提供能力声明、Handler、RequestPlayer、Announce 和 Received API，已编译验证。

当前示例的人工操作入口尚未接入游戏界面。手工执行 B01–B18 时，需要测试驱动或测试面板提供发送、声明/释放、查看同步状态、查看路由结果和查看接收记录的操作。安装现有示例 DLL 后还需接入这些 API，不能直接在现有游戏界面点击发送。

普通转发由 Framework Server 提供，服务端业务插件无需参与。B13、B14 的服务端 Interceptor 特殊行为，需要临时服务端测试插件或测试宿主注入对应拦截器。这些测试钩子只用于指定验收环境。

优先执行两端声明后的单播、按能力广播、撤销后的 NoCapability/NoRecipients、断线后的 TargetOffline、重连恢复及旧 Chat/Trade 回归，再执行版本、多连接、异常和负载项目。详细步骤见 Command-Capability-Relay-R5-Tests.md。

## 2026-11-10 最终补测

用户授权以白盒完成交叉与压力验收。最终 Pipeline 为 21 组、628 条断言，服务端阶段 50 项，客户端 7 组、97 条断言；精确交付源码的两版构建、聊天和实际 net35 传输专项通过。详细替代矩阵、边界和最终证据见 [最终核对报告](Command-Capability-Relay-R5-Final-Verification.md)。原文数字为先前执行记录。
