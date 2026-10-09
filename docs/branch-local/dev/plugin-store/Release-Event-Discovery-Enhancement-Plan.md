# Release 事件发现：可选增强计划

状态：**后续评估，暂不实施**。2026-10-09 用户确认当前发布与发现方案保持运行；未来事件发现作为新增可选入口，不替换已有流程。

## 当前流程继续保留

- 插件 main push → 编译/校验/打包 → 正式 GitHub Release；dev 与 PR 不触发发行构建。
- 三个维护插件已有发布后 Actions 通知，使用 `workflow_dispatch` 唤醒 Index 扫描；需要维护者配置 `INDEX_UPDATE_TOKEN`。
- Index 已获批来源的定时扫描、手动扫描、candidate/准入审核与受控目录发布继续保留。
- 现有发布 Actions、本地打包后经授权手动发布，以及尚未接入通知的第三方插件仍可使用。不要求作者为了新发现方式更换构建或发布工具。
- 通知不是审批证据；来源、版本、摘要、依赖范围、不可变资产及提交证据校验继续决定是否发布。

## 后续候选：统一 GitHub App 接收 Release 事件

作者可选择为插件仓库安装 GitHub App。App 接收正式 Release 事件，经独立接收服务验证后唤醒 Index 的现有校验/发布流程。接收端可评估独立 Cloudflare Worker，不预先修改当前缓存网关或部署基础设施。

此方案减少作者修改 Actions 或持有 Index 维护者令牌的需要，但增加接收服务、事件保存与故障恢复的维护成本。具体权限、托管费用和复杂度需另行评估。

## 兼容要求

1. **增加发现入口，保留发布流程**：GitHub App 只负责通知。原来的 main 发行 Actions、发布后通知 Actions、手动入口和定时扫描仍可工作。
2. **接入可选**：不安装 App、不配置发布通知的作者仍可走正常准入和定时发现；不得把 App 接入变成插件准入的新门槛。
3. **多入口幂等**：同一 Release 被 App、现有通知和定时扫描同时发现时，按来源身份、版本及不可变资产事实去重，沿用现有排队/发布事务，不重复批准或覆盖资产。
4. **准入独立**：仅处理获批来源范围内的更新；新插件及策略范围变化继续审核。不能把事件中的仓库、URL 或 payload 当作可信发布依据。
5. **身份边界先设计**：当前扫描证据要求既有维护者用户身份。App 安装令牌不能直接视为同等审批者；后续必须明确“唤醒者”和“审批依据”的职责并补充验证，不移除现有身份检查。
6. **失败可恢复**：验签、投递去重、持久化队列、有限重试及失败投递核对须形成完整方案。定时扫描继续补漏，不能承诺事件必达或所有中间版本逐一上架。
7. **发布方式无关**：作者通过现有 Actions、本地工具或其他 CI 创建符合要求的正式 Release，均可被发现；不强制使用本项目的某套 workflow。
8. **单独验收**：验证草稿/预发布过滤、重复/乱序投递、资产尚未齐备、同来源并发更新、卸载 App、接收端故障、队列上限和旧入口继续可用后，才考虑上线。

本轮只记录计划，不创建 App、接收服务、外部队列或新凭据，不修改现有 workflow，不移除旧入口。

## English

Deferred optional enhancement, confirmed on 2026-10-09. A GitHub App could receive stable Release events and wake the existing Index validation/publication pipeline through a separately evaluated receiver. It adds a discovery channel; it does not replace main release Actions, existing post-release notifications, manual packaging/publication, manual Index entry points or scheduled scanning.

Authors may keep their current build and release tools and need not install the App to qualify for admission. Multiple discovery channels must deduplicate the same immutable release and retain the existing writer queue, source policy, approval proof and publication checks. The current maintainer-user identity requirement needs an explicit design before accepting App-triggered work. Signature verification, durable intake, bounded retries and failed-delivery reconciliation require separate implementation and acceptance; scheduled scanning remains available for recovery. No infrastructure or workflow changes are authorized by this planning entry alone.
