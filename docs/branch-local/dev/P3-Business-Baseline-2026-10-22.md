# P3 独立业务分支与工坊基线

本分支 `codex/p3-trade-business`，工作区 `/home/hunyuan2333/Phinix/Phinix-Rework-P3-Business`，从 `53c92ce8eb314dcb8ca422ae4b6891a007931128` 建立，Common `c165f61d749189f700ca896d63d9366d5c71dbf7`、protobuf `4b0c3aacf0657fbf38253b38918d3358dd4319ec` 不变。

按用户授权，先冻结本机工坊 `3735269431` 和原客户端未提交状态，再把 51 个已有业务文件迁入本工作区。原 `codex/uiux-release-main` 保留 28 个 UI 变化与未分类文件，未 reset/stash/切换原分支。TradeWindow 观察版本、TradeList 交付记录展示属于业务，不能因文件是 UI 而混入主题/商店候选。基线制品 92 文件精确匹配 ThemePalette-20261018-02 包；HEAD 重建 1.7 的 18 个业务程序集在保守 IL/签名检查中匹配工坊，宿主/展示契约的 UI 差异单列。

完整冻结源码、分类、patch、工坊包和失败/通过证据：`/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-Client-Baseline-2026-10-22/README.md`。后续实现继续写本分支，不混入原 UI 工作区；业务代码尚未提交、推送或发布。
