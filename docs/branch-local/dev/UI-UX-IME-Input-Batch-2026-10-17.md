# Windows 选词后只剩首字：输入批次丢失排查与修正

日期：2026-10-17。Host 分支 `codex/uiux-release-main`，HEAD `27878e7fb78c11fec4f390db317dc0db5b0b10c9`，叠加已有未提交 UI/UX 改动。本次没有提交、推送、安装或发布，保留现有工作区修改。

## 反馈与证据边界

用户补充：选择“词语”后只写入“词”，未知输入法，也未确认游戏版本。没有取得反馈者运行中的 DLL、日志或 Windows 实机环境。因此本文确认的是可以复现该结果的源码缺陷，不能证明它是反馈者环境中的唯一原因，也不能宣称 Windows 问题已经验收关闭。

搜索字段不是一字长度：交易搜索上限 256，红包字段也没有一字限制；owner 搜索赋值与过滤逻辑不截取首字。公共控件继续调用 Verse/Unity 原生 TextField，未添加自定义拼音处理或按字符拦截。

## 已复现的原因

Host `ClientPresentationService.Session.TryDequeueInput` 每次读取都检查当前页面上下文。如果 owner 已标记 dirty、选中项或快照改变，会清空余下输入。这个保护本身有必要，不能移除。

部分 owner 却一边读取一边修改上下文。例如交易列表/报价编辑器：

1. 原生输入一次提交在更新前累计多次文本变化，如 `词`、`词语`。
2. owner 取到第一条，写入 `词`，设置 dirty（报价筛选也会立即置 dirty）。
3. 第二次读 Host 队列，原上下文已经失效，`词语` 被丢弃。
4. owner 用首条草稿重新发布页面，表现为完整词只留下首字。连续数字、粘贴后的连续修正也可能丢失。

新增测试在修改前失败于“Trade list drains a whole native commit before invalidating its context”。旧 CapturePresentation 假实现没有执行 Host 的 current 检查，因而没覆盖真实的失效条件；本轮同时修正这个测试替身。还有直接使用生产 Host renderer/队列和生产 Store owner 的连续文本回归，避免只验证假实现。

这是不同于先前两项修复的路径：之前公共 renderer 已接受 Layout/Repaint 返回文字，聊天 composer 已调整 IMGUI 控件顺序；它们不解决 owner 读到第一条后使余下队列过期的问题。动态布局的控件 ID 稳定性仍是独立风险，不能在没有游戏证据时认定为此次根因。[Unity 原生组合输入说明](https://docs.unity3d.com/ja/460/ScriptReference/Input-compositionString.html) 也不能代替反馈者平台的实测。

## 修正范围

采用与红包现有修复相同的顺序：先在上下文仍有效时读完本次已排队的输入，再按原顺序应用本地草稿/筛选，最后按既有规则重新投影。没有人为延迟、定时防抖或强行抢焦点。

- 交易列表、报价选物/逐行数量。
- Store 公共窗口（包括本地路径编辑）、Host 主题窗口、本人改名窗口。
- 库存关页保存草稿链；正常 ViewUpdate 已先读后应用，保持。
- 人才贸易公共 view 新增插件内部 ReadInputBatch；市场/租借目录、直接交易列表及编辑窗统一调用，刷新对应 4 个生产文件摘要。
- 聊天成员侧栏用 nextFilter/nextSelected 暂存，composer 在读完后更新 message；消息列表、ManagedStore 已先收集输入，本轮保留。
- **红包最新源码和前批 0.0.4 候选已有 ReadInputBatch，本轮未再修改红包输入代码。** 后续若该候选组合仍复现，需要针对 Windows 取得复现证据；未知发行包不能当作最新源码。

批次只收集当前 session、当前发布版本本来可读取的输入。Host 的 Current 检查、旧版本拒绝、动作失效、确认、数量校验及业务权威不变；没有允许动作绕过 dirty，也没有把读输入视为报价或领取成功。文本与数量本地草稿不会发送业务请求。主题窗原有关闭检查保留，关闭后不继续应用剩余条目。

## 构建、检查和候选

候选在前批 UX2-05 冻结源码上叠加本次列出的输入修复，隔离于另一路正在变更的连接准入协议代码。冻结目录 `/tmp/phinix-ime-workspace`；主工作区源码不回退、不覆盖。两版完整 Host/内置模块及独立红包、人才贸易、Example 构建通过；31/24 DLL 分版打包、21 共享源码身份匹配，Host ZIP 静态检查通过。

已通过：418 项 presentation source/stub 检查，包含同批中文词、交易列表三次前缀、两个数量字段、生产 Host 与 Store 双次输入；原有过期输入、旧动作、输入法 Enter 不发送及 Layout/Repaint bridge 回归继续通过。修改前失败日志 `/tmp/phinix-ime-before-01/results.log`，修正后 `/tmp/phinix-ime-after-02/results.log`。人才贸易 `check-source.py` 通过。

## Windows 定向复测

使用同一候选 Host 与配套插件，记录 RimWorld 版本、实际插件版本/包摘要和输入法；不要混用旧 DLL。

- 在交易列表、报价选物、红包、人才市场/租借搜索分别整词输入“词语”“医疗包”“高级零部件”；一次选词应保留全文。
- 连续输入第二个词、退格、全选替换、粘贴、空格选词、Enter 选词；不应失焦、重复字或意外提交业务。
- 有结果/无结果、过滤后列表变短、选中物品、多行数量连续编辑各测一次。
- 搜索时接收红包/交易刷新，检查旧数据确实过期时仍阻止旧动作，而不是继续按过期选择操作。
- 名称输入保留原始标签文本；主题/商店字段连续输入及关窗保留最终草稿的路径也需复测。

若最新组合仍只剩首字，应采集原生控件返回长度、事件类型、字段身份、revision/current/焦点变化；不记录玩家搜索文本、昵称或服务器数据。此诊断尚未加入常驻日志，避免未经证据的每帧刷屏。

### 最终验证与交付

- 1.6/net472 与 1.7/net10.0 均编译成功，三个独立插件一起重编；无需变更公共 presentation API。
- 对交付 ZIP 内实际红包 0.0.4、人才 0.0.5 DLL 重新运行真实绑定检查：两版各红包 38、人才 86、Store 109 项通过。人才新增完整词批次/已过期批次拒绝两项；红包现有整词批次回归保留。此类检查使用假游戏/registry/session，不等于 Windows 输入法实机。
- 红包与人才两个 ZIP × 两版共四组均以最终 Host/目标引擎/框架引用做 offline preflight，全部 `hostReferencesChecked=true`。先前只提供 Host DLL、不补齐编译框架引用时的临时校验配置错误已补齐；最终结果保存在候选目录。
- 人才 source manifest 校验通过；修改后 diff whitespace 检查通过（Host 按既有 CRLF 约定检查）。没有业务协议、保存键、正式发布版本、Host pin 或 SDK 更新。
- Host ZIP SHA256：`f94d2b5e809a77b6da2e58c5f3947181943996870c3b589ad4791689e12e96b0`。人才本地候选 0.0.5：`bdea4d806d688ca64bafa62eb9e57a9d7fefd5543c0fbf984bde85ae5904ea0a`。红包复用前批 0.0.4 原字节：`30e9a113509e55a3874135ced9877dba8ec57bcb1d78225239fd146b81ddfbee`。这些是本地候选编号，不是正式发行更新。

候选、完整摘要及检查日志：[IME 候选目录](/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/ime-word-input-2026-10-17/)。Windows 复测完成前不关闭问题。
