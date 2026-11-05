# P3 保存切换路径后的库存 authority（2026-10-30）

用户Windows/Waylith日志：RefreshAfterSave → Attach → InventoryJournal.Load，在sequence=0时抛出“Expected inventory journal marker is missing”。用户确认操作是手动存档；尚未提供保存前后文件名，因此不能仅凭此日志认定现场一定发生另存为，也不能排除迁移、删除文件等真正缺失本地证据的情况。

代码及旧包回归确认：journal路径由SaveId及绝对存档路径共同hash；GameComponent携带JournalIdentity。手动另存为或轮换自动存档时，新路径没有authority文件，但旧的非空JournalIdentity继续传入普通构造函数，必然被当成缺失证据拒绝。sequence=0也会发生，不证明玩家物品已丢失或磁盘损坏。

关联SV-33/38、TQ-30客户端存档归属子集、FR-12。只修改P3业务分支Inventory，保留未提交工作；不改UI布局、Server、Common、协议或依赖。普通UI分支没有此JournalIdentity/authority实现，不能把P3整体合入UI分支来同步修复。

## 最小修复

- Attach在同一GameComponent切换存档路径时，保留旧journal直到新journal打开完成，finally释放旧锁。其他加载仍走原构造路径。
- OpenSavedBranch只允许存活、健康、无未批准恢复的源journal；源sequence与完整快照必须匹配，authority文件及原journal仍在，源authority与组件身份一致。
- 回读实际新.rws，验证唯一Inventory组件、SaveId、完整快照和authority一致，持有保存证明文件lease期间创建目标journal。
- 仅全新目标（journal及authority均不存在）可用已验证的原authority初始化。保留原身份，避免破坏已保存receipt绑定。既有目标仍执行原校验、未来恢复及冲突拒绝，不覆盖孤立journal、不补造缺失marker。
- 冷加载时marker缺失仍只读，sequence=0不提供例外；不清空数据、不重发物品、不伪造ACK。旧故障存档不会仅靠升级无证据恢复，需要核对原保存路径及journal来源。

## 验证与限制

独立临时目录、合成.rws XML、真实打包Inventory模块/journal/保存回调，dispatcher及Game空壳隔离游戏运行时。覆盖sequence=0/2，手动另存为及随后轮换自动存档，保持authority/源历史，冷加载缺失marker、孤立journal、过期保存证明、源authority丢失、未批准未来记录均拒绝。旧版DLL负对照必须在首次Save As断言失败，不能以原错误行为作为预期。

完整命令、最终计数、源码及程序集SHA见本轮交付manifest/evidence。没有读取或修改用户Windows存档/日志目录，没有Windows Junction、NTFS强杀或真实Waylith存读档验收。Linux .NET10执行1.6程序集不替代Mono实机。

人工：完整退出后替换本轮P3客户端，保留库存/交易证据；在原本健康的测试存档上保存到新名称、再保存、等待一次自动存档，检查库存及交易按钮，退出重载核对数量。故障已固化到冷加载存档时保留原.rws及整个builtin.inventory目录，不删除authority/锁/journal进行“修复”。


最终验证：双版完整构建及94文件包审计通过；保存回调1.6/1.7各33项通过（包含42件合成条目及不透明payload完整保留），1.7库存既有21组场景、Trade composition315项通过。相同最终测试在上一Save-Guard包实际1.7 DLL上编译成功、首次Save As断言失败；失败日志保留。初始构建后增加源authority存在性校验，因此完整重建02并仅交付02；测试阶段另有一次错误相对路径修改命令失败，随后在正确目录完成，不涉及产品成功伪报。

命令：`python3 tools/compatibility/build-dual-host.py --game16 /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls --game17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll --output /tmp/phinix-inventory-saveas-dual-20261030-02`。

`tools/quality/run-client-component-probes.py --suite inventory-checkpoint`消费最终1.6/1.7归一化包闭包；同脚本`--suite inventory-runtime`及`--suite trade-composition`消费最终1.7。完整参数和输入SHA保存在交付evidence的各manifest。无新增协议profile/fixture版本；合成固定payload，无随机seed、无新生产fault注入点。

交付：`Deliverables/Phinix-Rework-P3-Inventory-Save-Branch-2026-10-30`，ZIP SHA256 `c5f963ef168d177e579f9521439392b6003e856e01a31c4df505b0ee2bde0ee9`。基线HEAD53c92ce加未提交修改，Common c165f61；未部署、未安装实机、未跨分支合并。
