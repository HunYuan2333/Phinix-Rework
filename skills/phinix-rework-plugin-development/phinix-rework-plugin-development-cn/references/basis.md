# 依据与更新方法

本轮核对日期：2026-10-15（Asia/Singapore）。依据本地实际源码及交付制品，不把工作区改动等同于已推送 main，不将旧阶段记录当作当前实现。

## 主要依据

| 主题 | 仓库内位置 |
| --- | --- |
| 架构与业务边界 | Phinix-Rework/docs/Design-Philosophy.md、docs/Compatibility-Boundaries.md |
| 组合与公共服务 | Client/ClientExtensionAbstractions/Framework/、Dependencies/Phinix.Common/Common/Utils/Framework/FrameworkTypes.cs |
| 中立呈现 | Client/PresentationContracts/、Client/Source/UI/ |
| 原生 UI 边界 | Client/ClientExtensionAbstractions/UI/、UI/ClientWidgets.cs 与 Game/ |
| 库存 | Extensions/Inventory/Contracts/InventoryContracts.cs |
| 双版本构建 | tools/compatibility/build-managed-dual.py；实际插件 pack.py、example/*.csproj、ci/config.json |
| 包与主题校验 | Extensions/PluginStore/Tools/ManagedPackageTool/；Common/Utils/Framework/Managed*.cs |
| Index 发布与主题接入 | Phinix-Plugin-Index 的 .github/workflows/、sources/ 与发布脚本；以实际仓库布局为准 |

工作区当前产品/CompatibilityVersion 为 0.10.4，API 为 3.0.0。这是本轮快照，不是未来最低版本或远端最新声明。独立核对产品、兼容版本、API、CLR 标识及 CI pin；CI 可覆盖源码版本，最终发布 ZIP/manifest 优先。

本轮未重新查询远端发布状态。发布、令牌、Action 运行和线上缓存状态需要当时的 SHA、run、HTTP 或制品证据，不能从 skill 文字推断。历史技能验证保留在 [validation.md](validation.md)，只适用于各自日期与范围。

## 后续维护

改 API 时同步核对声明、速查表、可复制模板和两个语言版本；改打包时核对真实 CLI 参数与输出 JSON；改发布策略时分别检查 DLL 与主题流程。编译引用与反编译结果仅本地使用，不放入 skill 或公开包。

工程组织借鉴的外部固定快照见 [engineering.md](engineering.md)，属于设计参考，不是必须安装或执行的技能。历史借鉴不覆盖当前用户授权与实际源码。
