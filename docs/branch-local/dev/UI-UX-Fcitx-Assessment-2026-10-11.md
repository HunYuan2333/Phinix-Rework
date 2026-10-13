# B3 fcitx 输入回归评估（2026-10-11）

用户反馈：旧 Chat 可配 Fcitx CJK Input，更新 Chat 后中文输入仍不可用。本次只做评估，未修改 renderer 或第三方模组。

## 已验证证据

- 本机 Workshop 3776312582 / scarf.cjkinput / 0.0.3，About 指向 https://github.com/scarf005/Rimworld-CJK-input 。实际 DLL SHA256：a32f4a62a3554aed10b170f990301b2c73f7166b94fe235426bdb0df9ecbd303
- 检查已安装 DLL 的 IL：Harmony patch Unity GUI desktop TextField。BeforeRootOnGui 每 frame DrainNativeMessages；BeforeDesktopTextField 无 Layout/Repaint 排除条件，TakeActions 后直接更新 TextEditor.text 与 GUIContent.text、设置 GUI.changed。AfterDesktopTextField 在 Repaint 绘制自有 preedit。不是针对 Phinix 固定控件名称的 patch。
- 新旧 Chat 均调用 ClientWidgets.TextField → Verse.Widgets.TextField，保留 0x50484349 control boundary。因此没有证据表明是换为自绘输入控件或改名使 patch 找不到输入框。
- 旧 Chat 直接接受 TextField 返回值；新 Host composer 在 ClientPresentationService.cs:391 用 InputEvent 限制，而 InputEvent(:412) 排除 Layout/Repaint。section fields(:288) 有相同限制。
- 本机 /tmp/fcitxcjkinput.log 显示 Unity 2022.3.35f1、XMODIFIERS=@im=fcitx、SDL_IM_MODULE=fcitx、native=True，且可观察 focus/engine/native events。不能根据该日志声称初始化或环境变量缺失。当前该片段主要为 QuickSearch，唯一 commit 是切换时 DROP 的 [f]，没有 Chat commit→Repaint→覆盖的完整 trace。

## 结论与置信边界

1. **已确认源码缺陷，高优先级根因候选**：将所有文本变更限制为键鼠事件，不能接受异步 bridge 在 GUI Layout/Repaint 注入的已提交文本。桥接已更新返回值，Host 忽略，下一 pass 又输入旧 draft，形成覆盖。旧 Chat 无此限制。由两侧代码可推导失效路径，但尚未取得本次 Chat 的现场完整 trace，不能称唯一根因已实机复现。
2. **独立输入法提交风险**：Enter 在 TextField 调用前预判且之后不重验是否被消耗；只查 Input.compositionString。第三方 bridge 维护自有 preedit，Unity compositionString 不能证明没有组合/选词。因此有提前派发或错误提交的风险；不将它直接等同当前文字丢失根因。
3. 控件 ID 在实际复杂页面变化、窗口输入权和重聚焦仍需 trace 排除。单纯改回固定控件名没有证据支持；不应放宽 ownership、私有反射第三方状态或新增官方 renderer。

## 修复范围与验收建议

同一公共 renderer 的 field/composer 应接受可用控件返回的真实文本差异，包括异步提交；只更新 view draft/输入意图队列，业务调用仍禁止在 Layout/Repaint 中执行。动作 click/row selection 与业务提交继续保留显式事件约束，不全局删除 InputEvent。Enter 在控件处理后重验事件消耗，并单独验证桥接组合/选词行为；不声称重验 Event 就能完整识别所有外部 IME 状态。

补非游戏场景：Layout/Repaint TextField 返回已提交中文、owner 消费回写保留、重复 pass 不重复排队、普通字段同样保留、Draw 不执行 action、被控件消费的 Return 不发送。随后由用户实机：fcitx 中文候选确认、切换中英文、Enter/空格/数字选词、接收新消息时继续输入、光标编辑、失焦重开、失败草稿。游戏全部待测；不要求重新安装已经初始化的 fcitx 模组来掩盖 Host 缺陷。


## B4 源码修复状态

用户授权修复并暂缓游戏复测。renderer 已接受普通 field/composer 的 Layout/Repaint 文本返回差异；Return 改为控件处理后重验，消费/同次文字提交不发送。新增相关非游戏检查通过，Presentation 总计 34 项。实际桥接 IME 全行为与游戏焦点仍待合批验收；上述原始评估中的“只做评估”是历史状态。
