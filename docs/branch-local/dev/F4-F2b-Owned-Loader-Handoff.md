# F4-F2b：客户端加载与依赖解析边界

2026-10-07，dev；实施完成、游戏核查待反馈，未提交/推送。此前商店门禁与读档历史最小修复用户已反馈“没问题”，仅记录实际接受，不扩大为全部第三方模组兼容认证。F4-B 示例及其他并行修改保留。

## 实施 / Changes

- Client 从自身 ModContentPack.RootDir 得到 Common/Assemblies 和 Common/Extensions 的显式插件目录；移除游戏程序根、第三方根 Assemblies 的主动加载，以及根据可能被 Prepatcher 改写的 Assembly.Location 推导目录。版本目录的 host 和普通模组由 RimWorld 加载，模块仍从 AppDomain 进入同一注册表。
- Utils 新增 LoadOwnedAssemblies，返回 IDisposable 范围。预先读取完整 CLR 身份，准备文件与依赖索引后才开始 LoadFrom；数字前缀文件名不是身份判断依据。
- 解析器只处理目录候选中的请求者，以及其真实 AssemblyRef 声明的完整身份。第三方、无请求者、错版本、未声明请求返回 null；没有全局目录兜底和 Assembly.Load 简单名称回落。准备后加入的文件不被搜索；重复 CLR 简名明确报告并排除。
- 重读候选身份及实际 LoadFrom 返回身份，防止悄悄替换版本；已有同名不同身份给出诊断。正常停机/部分启动失败清理完模块及 managed runtime 后释放加载范围，撤销事件；重复 Dispose 安全。不会卸载 CLR 程序集。
- 服务端仍使用原 LoadAssemblies 兼容入口，本轮未改变服务端的加载行为。新的客户端入口和 managed payload 的绑定是不同职责；managed 请求不落回普通目录通配解析。

English: the client proactively loads only its explicit host runtime/bundle roots. Ordinary RimWorld mods are discovered after the game loads them. A disposable loader prepares complete identities, handles only owned requesters and declared references, rejects ambiguity/version mismatches, and detaches its resolver on shutdown. The old server loader remains compatible. This narrows our own resolver; it does not sandbox the shared CLR or control other mods' resolvers.

## 验证 / Validation

- 新真实程序集加载回归：.NET 10 / Mono net472 各 13 条，覆盖前缀文件、完整依赖图、外部目录不加载、外部请求不解析、无来源、错版本、未声明、晚加入文件、释放与重名；游戏已加载普通 submod 仍经同一注册表发现。
- Managed runtime net10 / Mono 各 3108 主断言通过，启动子场景和 2137 Store operation 回归通过。已有严格依赖/冲突/事务保护不变。
- Release 1.6 完整构建 0 错误/14 警告（既有代码/框架/网络 NuGet audit 警告）；新测试初始构建 0 错误/3 警告。
- git diff --check 通过。分发检查和最终包哈希见下方追加记录；未运行真实 Unity/RimWorld。

实际命令（根目录，日志 /tmp/phinix-owned-loader-*.log）：

```sh
dotnet build Tests/ExtensionLoaderRuntimeTests/ExtensionLoaderRuntimeTests.csproj -c Release -p:NuGetAudit=false -p:BuildInParallel=false -m:1
dotnet build Tests/ExtensionLoaderRuntimeTests/ExtensionLoaderRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ExtensionLoaderRuntimeTests/bin/Release/net10.0/ExtensionLoaderRuntimeTests.dll Tests/ManagedExtensionRuntimeTests/bin/Release/net472/Fixtures
mono Tests/ExtensionLoaderRuntimeTests/bin/Release/net472/ExtensionLoaderRuntimeTests.exe Tests/ManagedExtensionRuntimeTests/bin/Release/net472/Fixtures
dotnet build Tests/ManagedExtensionRuntimeTests/ManagedExtensionRuntimeTests.csproj -c Release --no-restore -p:BuildInParallel=false -m:1
dotnet Tests/ManagedExtensionRuntimeTests/bin/Release/net10.0/ManagedExtensionRuntimeTests.dll
mono Tests/ManagedExtensionRuntimeTests/bin/Release/net472/ManagedExtensionRuntimeTests.exe
dotnet build Phinix.sln --configuration 'Release 1.6' --no-restore -p:SolutionDir=/home/hunyuan2333/Phinix/Phinix-Rework/ -p:RimWorldDepDir=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:GameReferenceDirectory=/home/hunyuan2333/Phinix/Phinix-Rework/GameDlls/ -p:BuildInParallel=false -m:1
git diff --check
```

## 游戏步骤与兼容性 / Game checks and compatibility

1. 退出游戏、备份主包，再整体更新。保留托管插件目录和玩家存档；包不含独立 F4-B 示例候选。
2. 保留原第三方模组及 Prepatcher 组合，启动确认 Chat/Trade/Inventory/Store 正常，无缺少程序集/激活失败。旧加载行为未文档化但有人可能依赖它；放在游戏未选中目录的 submod DLL 不再由 Phinix 补加载，作者须通过游戏有效目录加载或显式 Phinix bundle 部署。
3. 在商店下载/安装红包，重启确认插件正常且仅激活一次，验证实际缺失依赖仍拒绝。
4. 互发聊天并同进程读档；若重连，检查历史恢复和新消息收发，确认前批修复未退化。
5. 如有第三方 Phinix submod，确认经游戏正常加载后仍出现且可禁用/恢复。退出游戏检查清理无异常。

不变更协议、物品所有权、安装日志及存档格式。该批对本地 DLL 加载来源更严格，可能暴露先前被通配探测掩盖的部署问题；须按明确来源修复部署，不能恢复扫描普通模组根目录。全局旧整模组安装器、主题选择、安装前最新环境采集另分批，F4-F2 整体仍未完成。

最终整包构建：0 错误/9 警告。`python3 /tmp/phinix-owned-loader-package.py` 等价分发检查通过：31 必需资产、唯一 runtime 与构建一致、无游戏 DLL、ZIP 完整性；进一步验证 ZIP 中 Utils 和 Client 为本批最新字节。PowerShell 未安装，未调用 .ps1。

交付副本 `/tmp/phinix-rework-owned-loader-fix-20261007.zip`，源包 Output/phinix-rework-owned-loader-fix-20261007.zip；SHA256 bac6c2b248a0bed268045563a474dc47fc5a41ea6d77140a6d638037540b449b。包含此前已接受的商店/聊天历史修复。已审查 git diff/status，未提交或发布。稳定设计哲学中英同步补充加载边界。
