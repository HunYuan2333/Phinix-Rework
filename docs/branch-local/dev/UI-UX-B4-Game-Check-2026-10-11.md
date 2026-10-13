# B4 合批游戏验收（2026-10-11）

用户当前不能复测，本表供之后合批测试。以下全部**待测**；仅源码/编译与非游戏回归通过，不要求现在测试。

## 候选

Host：`/home/hunyuan2333/Phinix/local-plugin-packages/2026-10-11-b4/phinix-rework-b4-rimworld1.6.zip`

Example：`/home/hunyuan2333/Phinix/local-plugin-packages/2026-10-11-b4/example-1.0.9.zip`

准确 SHA256 / runtime DLL 与资源清单在 `UI-UX-B4-Build-2026-10-11.json`。旧 B3/fix 包不覆盖。本批包括 Chat 三个 point 的 builtin.chat ownership、fcitx 文本接收修复和消息动作流程。消息卡片完整视觉迁移、Inventory 与后续业务不属于已完成范围。

备份原 Mod/插件与设置，在测试环境安装同批 Host/Example 并重启 RimWorld 1.6，避免同时加载旧 Host 或只换 Example。保留已安装 fcitx 模组及正确启动环境。不要覆盖原存档测试；本任务未部署或发布。

## 操作与结果记录

| 流程 | 预期 | 结果 |
| --- | --- | --- |
| 打开 Chat、成员侧栏、Example 新 Host 窗口 | 三个 Chat contribution points 激活无 OwnershipConflict；Example 实际为 1.0.9 | 待测 |
| fcitx 输入中文，Enter/空格/数字选择候选，中英文切换 | 已提交文字不消失；选词不误发；正常 Enter/KeypadEnter 与按钮各只发送一次 | 待测 |
| 中文输入时收到新消息，光标中间插入/删除，焦点移走返回 | 草稿与光标保持；异步重绘提交保留 | 待测 |
| 离线/发送返回失败、关闭 Chat 再打开、回复与 @ | 禁用/失败保留草稿；无自动重发；原 reply/mentions 语义保持；关闭不撤销业务 | 待测 |
| 点击消息正文→复制、URL、回复→发送 | 复制 raw text；URL 沿原解析规则；回复目标正确，后续派发仍走原 sender；callback 完成不等于回执 | 待测 |
| 消息含 21+ URL，previous/next；窄窗、中文/英文、多 UI scale | 所有链接可通过分页访问；动作可滚动，无静默截断 | 待测 |
| 动作窗打开时消息删除/替换/状态变化、语言变化、停插件、切换 Chat | 旧 action 无法执行；父 view/stop 清理其窗口与 owned set | 待测 |
| 第二插件向 ChatMessageActionContext 贡献动作后移除 | 与复制/回复同一 Host 列表；移除后旧 action 拒绝；超限显式失败 | 待测 |
| 显示图片、加载/失败、断线/关闭/重新打开 Chat | completion/pump 在 update 推进；原 fallback/纹理清理保持；无 Layout/Repaint 业务命令 | 待测 |
| Example filter/composer 中文输入→add→reset 取消/确认；主题热切 | 同 public renderer 修复；字段变化不执行计数，显式 add 执行一次；确认规则保持 | 待测 |
| B2 设置四入口、成员屏蔽/取消、多个侧栏/公告和旧 Draw provider | 所有入口继续可达 | 待测 |

反馈记录游戏版本、语言/UI scale、实际 DLL/包版本、步骤与结果；输入异常保留 fcitx debug 的选词/提交和控件 pass，布局异常保留宽/窄窗截图。已有 B3 日志只证明旧 Chat 激活失败，不算本批测试结果。


## B5 视觉基础追加候选

之后合批测试使用 Host `2026-10-11-b5/phinix-rework-b5-rimworld1.6.zip` 与 Example `2026-10-11-b5/example-1.0.10.zip`（根目录仍为 /home/hunyuan2333/Phinix/local-plugin-packages/）。准确 digest 见 UI-UX-B5-Build-2026-10-11.json。上表所有 B4 行为继续验证，额外记录：100%/125%/150% UI scale 的圆角边缘/边框；composer 不再套内部方框、focus outline 正常；primary normal/hover/press/disabled；原发送图标 11.875×10 与文字成组居中；长中文/英文和窄窗；12px cards/列表换行/滚动无 clipping；自定义主题热应用。字体、阴影与旧消息卡片完整视觉仍未完成，游戏结果全部待测。
