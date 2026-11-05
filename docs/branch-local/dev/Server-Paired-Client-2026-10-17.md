# 服务端配套客户端交付（2026-10-17）

服务端确认版本校验需双方更新客户端；单更新云端时，旧客户端缺版本的新正向确认会被明确拒绝。不能由服务器猜测/补造玩家看过哪一版报价。本轮没有取消校验或加入 Legacy 核心分支。

已重新构建完整 1.6/net472 与 1.7/net10 客户端，按既有版本化 staging 工具检查 CLR 引用闭包、双运行目录、资源、ZIP CRC/字节清单和禁止携带游戏 DLL；包审计 94 文件通过。修正中英文通用更新失败提示，删除无证据的“物品已经运回”结论，保留格式参数。XML 实际解析通过。

实际 ZIP 内两套 Trade DLL 各进行独立组件探针：显示快照为 2、仓库推进到 3，实际 adapter 经真实 Framework 编解码发出观察版本 2；无观察依据的正向 API 拒绝，撤回可发送。两套通过。DispatchProxy 仅隔离 session/lifecycle/transport，不代替业务；没有网络、游戏物化或存档证明。初次探针的编号 DLL 解析和过宽游戏引用失败已修正探针配置，不修改产品布局或绕过断言。

构建命令：`DOTNET_CLI_HOME=/tmp/phinix-dotnet-113 NUGET_PACKAGES=/home/hunyuan2333/.nuget/packages MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.113/Sdks python3 tools/compatibility/build-dual-host.py --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-server-paired-client-20261017`。

构建仅编译器和包审计通过（有既有警告），三份独立插件作为维护引用的构建检查，不包含于 Host ZIP。源码未提交，base SHA 不是完整构建身份；两版最终 DLL SHA256、配套服务端源码 hash、ZIP SHA256 与探针日志见交付目录的 manifest.json/evidence。

交付位置：`/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-Server-Paired-Client-2026-10-17/`。ZIP SHA256：`29628a2ed1710cc373c7aaaa6791ac0a571a38597f2b06bdb15b2eac5ce6c4dc`。此前临时构建被系统清理，已重新构建并持久保存最终 ZIP、校验和、安装说明与证据；不包含临时探针复制的游戏引用。

用户反馈一笔取消后物品已进入库存，属于单次人工观察，尚未记录精确旧客户端候选/profile 与双边存档重载，未标 P6 或整体验收通过。当前待交付客户端内存队列的持久恢复、Unknown 查询/展示及现代 receipt 仍是后续前置。更新前先处理待交付并保存，之后双端完整替换模组并重启；保持原身份与用户数据，不发布工坊/部署云端。
