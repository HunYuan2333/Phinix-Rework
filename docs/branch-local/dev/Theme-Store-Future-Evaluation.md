# Theme and Store future evaluation / 主题与商店后续增强评估

2026-10-08. Explicit user decision: defer this enhancement; F4 may close without it. Evaluation only, no implementation or deadline. Current third-party theme behavior stays unchanged.

用户明确决定留作后续增强评估，不阻塞 F4 结束。本文件仅记录方向，未实现、未排期；现有第三方主题行为暂不改。

## Questions for later evaluation / 后续评估内容

- Explicit theme selection, provider/source display and override priority instead of applying every active mod's root Themes files in enumeration order. Determine migration for existing third-party and user themes.
- Read only selected Phinix theme resources from game-approved effective resources or explicitly managed theme packages. Define allowed colors/parameters, bounded parsing and failure isolation.
- Let Store browse and download theme packages as a distinct resource capability. Evaluate catalog/payload format, target directory, integrity/trust policy, ownership records, update/uninstall and activation semantics. Themes should not require pretending to be executable plugins or automatically loading DLLs.
- Coordinate selected-theme persistence, missing/disabled provider fallback, preview/apply/cancel and localization. Validate compatibility with current UI/theme APIs before choosing a design.
- F6-S mandatory signing admission was canceled by the user on 2026-10-08. Theme evaluation has no signing-stage prerequisite; retain independent integrity, compatibility and ownership requirements. No new implementation or release is authorized here.

后续考虑明确选用、来源展示与覆盖优先级，旧主题迁移和用户主题兼容；仅读取选中的 Phinix 主题资源，确定颜色/参数范围与故障隔离。商店增加主题浏览/下载作为独立资源能力，届时评估目录和载荷格式、安装位置、校验/信任、归属记录、更新/卸载与启用语义，不把主题伪装成可执行插件或自动加载 DLL。还需评估选择持久化、提供方缺失回退、预览/应用/取消及本地化。用户于 2026-10-08 已取消 F6-S 强制签名准入，主题评估不以该阶段为前置；完整性、兼容性和归属要求继续独立保留。本文件不授权实施或发布。

Proceed only after a later user request and concrete design review. RedPacket optimization remains a separate deferred plan.

用户后续要求并完成具体方案评审后再推进；红包优化仍为另一项暂缓计划。
