# B3 游戏验收（2026-10-11）

状态：源码/编译及非游戏回归通过；以下全部游戏项目**待测**。没有部署或发布。本批依据 Figma 主窗口/发送栏实施共享基础与 composer，消息流/R32 尚未整体迁移。

## 同批候选

- Host：`/home/hunyuan2333/Phinix/local-plugin-packages/2026-10-11-b3/phinix-rework-b3-rimworld1.6.zip`，SHA256 `9ce6d56c05f1f51147c6b0ad577ace63531f171d0e87eb850de4dfe19a001847`。
- Example：`/home/hunyuan2333/Phinix/local-plugin-packages/2026-10-11-b3/example-1.0.8.zip`，SHA256 `21c4717e20bfae8d3f894f7481fc0323e23ea6478d2f10be2d2eba1cf198667c`。
- DLL/资源清单：`UI-UX-B3-Build-2026-10-11.json`。Host 包包含 1.6/Common 运行 DLL 与原游戏资源，不包含 SDK 源码/Abstractions 或 Any 构建目录。Example 包走已有本地包入口，不开启投稿/在线更新。

先备份原 Mod 目录与现有用户设置，在 RimWorld 1.6 测试环境安装同批 Host 和 Example 后重启；避免同时加载旧 Host 或只换 Example。先记录实际游戏版本、UI scale、语言、加载 DLL 与日志，失败不要通过重复发送真实交易来探测。不要覆盖原存档进行测试。

## 最小可操作流程

| 流程 | 预期 | 结果 |
| --- | --- | --- |
| 打开 Phinix → Chat，鼠标聚焦输入框，用中文输入法选词按 Enter | 选词不发送；结束组合后再按 Enter 才发送一次；点击发送也仅一次 | 待测 |
| 输入英文/中文、@ 选择已有成员、回复已有消息，然后发送 | 保留原 mentions/reply 语义；成功派发后清空；没有新增回执或 presence | 待测 |
| 输入草稿后断开本地连接，再尝试发送/恢复连接 | 离线发送 disabled；草稿不丢失；恢复不自动重发；实际发送返回失败也保留草稿 | 待测（返回失败需真实可控证据，离线 disabled 不代替） |
| 未发送草稿关闭 Chat 再重开；输入后打开 @/overflow 菜单 | 同 provider 草稿保留；菜单操作不误提交；关窗不撤销已派发业务 | 待测 |
| 缩窄窗口，切中文/英文和 UI scale，热切默认/自定义主题 | 输入/发送/overflow 可达、图标无重叠；暖灰/琥珀默认应用；自定义/fallback 仍有效 | 待测 |
| Example → Host 窗口 → 便笺输入 → Enter 或 add → reset 取消/确认 | 每次显式 add 一次；选择列表不 add；重置按原确认语义；任务完成仅为 callback 状态 | 待测 |
| 设置四入口、成员搜索/屏蔽/取消、多个 sidebar/banner、旧 Draw providers | B2 能力保持可达；贡献失效/模块停止后旧动作拒绝 | 待测 |

请反馈：哪个步骤、操作前后结果、语言/UI scale；若异常提供 Player.log 对应片段；若布局问题提供正常宽度与窄窗截图。第二插件 composer/member 贡献、维护插件 1.9、T5 本地包和后续业务流程仍独立待验收，不因此表通过而全部通过。

## Chat 激活修复批次

首轮用户日志证实 Chat 激活失败，修复 PointId ownership 后请改用 `phinix-rework-b3-chatfix-rimworld1.6.zip`，旧 B3 Host 包保留为历史失败批次。新 digest 见 `UI-UX-B3-Fix-Build-2026-10-11.json`。先重启确认 Chat 与成员侧栏可用、日志无 ContributionPointOwnershipConflict，再执行上述输入验收。游戏修复状态仍待复测。Example 继续用 1.0.8。
