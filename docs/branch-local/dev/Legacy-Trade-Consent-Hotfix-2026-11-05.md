# 老 Phinix 服务器交易确认回归

工坊候选 99bcef8 的确认路径强制要求服务端报价版本，老 Phinix 快照未提供版本，导致确认在本地抛出 Refresh the trade quote before confirming，未发送请求。

本次仅在已经识别为 Legacy 的连接中调用命名 LegacyTradeConsentAdapter。原始快照与当前仓库快照指纹必须一致，才可提交不带报价版本的原版确认。新版连接继续要求有效报价版本，未绑定的占位报价继续拒绝。未改窗口布局或服务器端账本。

实际验证：1.6/1.7 完整构建与包审计通过，候选包 SHA-256 为 02d2f5fac58f74ef33070e62b93195867c4a0312778a5efe99ccc2ccdc6a0f96。1.7 实际包内 Trade 组件通过 323 项断言，包含 Legacy 确认、现代无版本拒绝和过期 Legacy 快照拒绝。实际 Legacy Adapter/序列化器的确认 wire 专项通过。

证据保存在 /tmp/phinix-legacy-confirm-final.ctsGCg。首次构建期间源文件调整触发打包校验拒绝，最终使用新目录重建通过。完整旧 LegacyTradeRuntimeTests 在既有聊天发送 fixture 上失败，未计入通过；新增 --consent-only 可单独运行确认 wire 回归。真实老服务器的双方确认和到账等待用户复测。
