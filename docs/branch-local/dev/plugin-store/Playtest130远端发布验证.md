# Playtest 1.3.0 远端发布验证

[English](Playtest130Publication.md)。2026-10-05。用户已明确授权本版本推送远端测试；本记录覆盖此前“仅本地候选”的状态。目录 v3 和客户端 GitHub 直连仍待实施。

已公开到 HunYuan2333/Phinix-PluginStore-PoC 的 `codex/managed-publication` 分支，未改 main 或推送其他宿主源码。发布八份样例、项目、打包工具、说明、语言文件；不含凭据、游戏/框架 DLL、日志。

- [插件 Release 1.3.0](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/v1.3.0)：release 403580117，asset 612183754。
- [目录 Release](https://github.com/HunYuan2333/Phinix-PluginStore-PoC/releases/tag/managed-catalog-0027158)：release 403581372，asset 612187685。
- 源码/快照：`002715878af86191b361d6ff642a686bb81ad550`；测试指针提交：`313f1eb56d4ce830965b4865e18a436973f08b4c`。
- ZIP：6822 字节，SHA-256 `5a8e14105639e0180b10cbf82923d91fe64e2e6a5a14ae6728a6851ba7575f88`。
- 清单：SHA-256 `1249fe3864ef05f3c28e69bf6698e4ec5053c794051e7c513647f945d4b07d3f`。
- catalog v2：5266 字节，SHA-256 `d22dcd114f7d901c1019189fb580043f23a0512e86209e4158d931242ec772f8`。
- published：407 字节，SHA-256 `f83eaca0ee1f2e5f72a36fea5551c007f49ea673aa61a609205af38ee71b7a0f`。

ZIP 和目录上传后回读，逐字节核对后才快进 stable 指针。采用固定 REST 数字 ID，不使用 GraphQL node ID。保留旧版本不可变资产与声明身份；当前目录把 1.2.x 撤回，避免新宿主选到引用 ClientExtensionAbstractions 1.6.0 的旧包，新样例要求 1.7.0。安装器不支持原位替换，旧托管版需正常卸载并重启，保留设置/存档。

staging 已配置这个分支的 phinix.managed，因此只更新测试内容/指针，没有部署 Worker 代码、修改配置或生产域名。真实入口返回新快照和一致 ZIP 摘要。语言清单通过当前 v2 测试链可读取；目录 v3 商店多语言简介/changelog 尚未交付。

## 验证

准确命令见[英文记录](Playtest130Publication.md#exact-validation)。可信发布校验器静态核验公开 ZIP/PE/语言通过；.NET 10 和 net472/Mono 通过实际客户端免代理联网验证：stable/published/catalog、缓存、预取消、下载字节/长度/摘要、DLL 和语言文件、前后实时目录复核、临时文件清理。均报告四个文件、预期快照和相同摘要。net472 构建成功，只有一个 NU1900 漏洞源不可达警告。git diff --check 通过；本轮未修改生产代码，主体/商店已在前一本地化批次编译。

命令只写全新隔离目录，不安装或执行下载 DLL。不能据此宣称游戏界面/白银/重启验收、所有地区网络或未实现的 GitHub 客户端直连通过。重复验证时换一个新的隔离状态路径。

## 游戏测试交接

远端安装前取走整个手动 Playtest 文件夹并重启，避免重复加载；若仍有旧托管版，通过扩展管理卸载并重启，不删设置/存档。使用 [staging 入口](https://plugins-staging.hunyuan2333.com)、sourceId phinix.managed，刷新并选择 1.3.0，沿当前计划/下载/安装流程完成后重启。检查每插件目录中的 DLL/两份 JSON、中文/英文/缺译回退、计数保留、商店停用后翻译、启停/卸载。详见[样例](../../../../Extensions/PluginStore/Samples/Playtest/README.zh-CN.md)和[下一批访问架构](GitHub直连与CF适配器计划.md)。
