# Phinix Rework 已完成里程碑汇总

更新时间：2026-11-17（汇总至提交 `6c30256`）。

本文档汇总 Phinix Rework 客户端已完成并合并的各阶段里程碑与核心交付，替代原先分散在 `docs/branch-local/dev/` 中的单批过程性文档。近期仍在推进或刚更新的专项文档（如 `Client-Error-Reporting*`、`SQLite-*`、`Inventory-SQLite-*`、`Command-Capability-Relay*`、`Design-Philosophy.md`、`Compatibility-Boundaries.md`、`Inventory.md`、`Plugin-Development.md`）保持原样，不在此重复展开。

---

## 1. 基础设施重构与三仓拆分（F0 – F7）

| 阶段 | 关键提交 | 已完成核心内容 | 留存文档 |
| :--- | :--- | :--- | :--- |
| **F0 – F5** | `403cea6` 之前 | 基线清单、`ClientExtensionRuntime` 生命周期解耦、Autofac 8.4.0 DI 试点与推广、Stateless 5.20.1 商店状态流、共享源码抽取与双目标验证 | [后续实施计划](后续实施计划.md) / [English](ClientInfrastructureFollowUpPlan.md) |
| **F6 / F6-M** | `403cea6` | 物理拆分为 Client / Common / Server 三仓，固定 gitlink；引入 `IClientExtensionControlService` 实现扩展管理与商店状态双向同步；F6-S 强制签名准入已取消 | [后续实施计划](后续实施计划.md) |
| **F7** | `fa17ae4` / `c63688c` / `5366dc7` | 开发者模式本地 ZIP 侧载、可复制诊断摘要、`ManagedPackageTool` 可配置打包与离线预检、独立插件 main 自动发行 Actions，以及双语插件开发指南与 skill | [Developer-Foundation-Delivery-Plan.md](Developer-Foundation-Delivery-Plan.md) / [Local-Plugin-Quickstart.md](../../Local-Plugin-Quickstart.md) |

---

## 2. 主题系统、共享 UI 重构与 RimWorld 1.6/1.7 双运行时（F8 / UI-UX / API 3）

| 阶段 | 关键提交 | 已完成核心内容 | 留存文档 |
| :--- | :--- | :--- | :--- |
| **本地主题与 API 扩展** | `c51f8a6` | `IClientTheme` 单一契约、`ClientWidgets` 统一控件渲染、独立主题设置/编辑/导出窗口、本地主题 ZIP 安装与恢复、移除旧客户端 `Register` 入口 | [F8-Implementation-Progress.md](F8-Implementation-Progress.md) |
| **共享呈现与 1.6 UX 重构** | `783fcb0` / `3907d48` / `c6988e5` | B1–B42 公共呈现契约、Host renderer、Chat / Trade / Inventory / Store / RedPacket 界面重构，以及官方插件正式发布与目录准入 | [UI-UX-Redesign-Roadmap-2026-10-11.md](UI-UX-Redesign-Roadmap-2026-10-11.md) / [UI-UX-Official-Releases-2026-10-13.json](UI-UX-Official-Releases-2026-10-13.json) |
| **API 3 与 1.6/1.7 双运行时** | `2c228fe` / `f7ea087` | Host 升级至 API 3；`1.6/net472` 与 `1.7/net10.0` 双引擎适配与单包双 payload 布局、1.7 Native 绘制与描边修复、Workshop 身份与双版本发布验证 | [RimWorld-1.7-Migration-Roadmap-2026-10-13.md](RimWorld-1.7-Migration-Roadmap-2026-10-13.md) / [Building-Dual-RimWorld.md](../../Building-Dual-RimWorld.md) |
| **在线主题商店** | `81059c4` | 独立 `phinix.themes` 来源与 catalog schema 4、主商店类型/状态/搜索组合筛选、纯数据主题静态校验与统一主题渲染 | [Store-Themes-and-Index-Actions-Plan-2026-10-15.md](Store-Themes-and-Index-Actions-Plan-2026-10-15.md) |
| **UX2 易用性增强** | `53c92ce` / `6fe404e` | 固定本人昵称区与独立改名窗、侧栏紧凑化与可拖动分隔条/光标反馈、按钮默认对比度、库存图标/搜索/行内提取/每日计划、Windows IME 批次输入修复、主题编辑器常用配色/色轮、商店行内管理与 1.7 主窗阴影关闭 | [UI-UX-Usability-Implementation-Roadmap-2026-10-16.md](UI-UX-Usability-Implementation-Roadmap-2026-10-16.md) |

---

## 3. 服务端配对事务、回执与 Legacy 兼容（P3 – P6）

| 阶段 | 关键提交 | 已完成核心内容 | 留存文档 |
| :--- | :--- | :--- | :--- |
| **P3–P6 配对客户端** | `99bcef8` / `750a91a` / `075c929` | Trade 恢复泵与意图/结果查询边界、`TradeCompletionInbox` 与交付日志、Inventory 检查点与显式保存快照恢复选择、P4 客户端凭据、P5 玩家诊断与 P6 配对就绪 | [P3-Closeout-2026-10-29.md](P3-Closeout-2026-10-29.md) |
| **Legacy 集成修复** | `57e6d1c` / `b00862a` | 修复原版 Phinix 服务端交易确认（Consent）及派发、聊天、交易全生命周期的 Legacy 适配问题 | [P3-Closeout-2026-10-29.md](P3-Closeout-2026-10-29.md) |

---

## 4. 近期已合并与进行中的专项（2026-11-10 – 2026-11-16）

> 以下专项文档近期有更新，详见各自文档本身：
> - **命令能力 Relay（R0–R5，已合并 `9c0c5ae` / `5b2bbb3` / `311fdd6` / `6c30256`）**：见 `docs/Command-Capability-Relay.md`、`docs/Plugin-Development.md`、`docs/branch-local/dev/roadmaps/Command-Capability-Relay-*`。
> - **SQLite 基础存储与库存迁移（`e00872e`）**：基础 SQLite 存储与白盒已合入，库存迁移规划见 `docs/branch-local/dev/Inventory-SQLite-Migration-Roadmap-2026-11-10.md` 与 `docs/branch-local/dev/Client-SQLite-Storage-Design-2026-11-10.md`。
> - **客户端错误报告（E0–E4 已合入 `e00872e`，E5 进行中）**：见 `docs/branch-local/dev/Client-Error-Reporting-Roadmap-2026-11-15.md` 与 `docs/branch-local/dev/Client-Error-Reporting-2026-11-15/`。
