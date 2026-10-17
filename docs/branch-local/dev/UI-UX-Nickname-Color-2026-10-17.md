# 本人昵称颜色显示修正

日期：2026-10-17。用户在确认拖动/右侧基本可用后指出本人卡片昵称仍是普通正文色。本次优先修正该显示问题；UX2-04默认主题只做了读取检查，未把主题状态改动混入本候选。

基线仍是Host `codex/uiux-release-main` / `27878e7fb78c11fec4f390db317dc0db5b0b10c9` 加已有未提交修改。源码、前轮文档及4个fixture DLL保留；没有提交、推送、安装或发布。

## 原因和显示行为

Host本人卡片直接用原始DisplayName和PrimaryText绘制；Chat的FormatDisplayName会为无显式标签的名字补色，而Host没有这个显示步骤。不能让Host引用Chat业务formatter或读取其私有settings/UUID配色。

现在使用既有主题 `ui.accent` 给本人昵称提供显示默认色；“我：”仍用 `ui.text.primary`。已有原始颜色标签完整嵌在默认颜色内，显式逐字颜色由原生富文本标签覆盖外层默认色。外层GUI使用中性白tint，避免将显式颜色再次乘上主题正文色。本地原文、在线提交和身份事实均不被修改；显示标签绝不写回设置或请求。

`NicknameDisplayMarkup` 是Host内部无引擎纯显示转换，不是名字编辑器/第二主题或公共SDK。颜色采用RGB/Alpha十六进制标签。ServerTab缓存最终显示字符串，仅在原名、语言前缀或主题版本变化时更新；悬停仍显示原来的去标签全名和在线状态。

这是Host通用本人身份区的主题配色，不承诺与Chat每个成员按UUID生成的自动颜色完全相同。离线及无Chat时仍使用当前Host主题。手动HTML和未来调色器的原文保存边界不变；此次不实现逐字编辑器。

## 验证

406项PresentationRuntimeTests通过，新增检查覆盖前缀/昵称不同颜色、显式逐字标签逐字保留在显示外层、主题Alpha；原有草稿/保存/取消/发送与成员密度检查仍通过。日志 `/tmp/phinix-nickname-color-presentation-01/results.log`，stub GUI不能代替原生富文本像素验收。

本次用户“没啥问题”按其右侧/拖动体验反馈记录，没有游戏版本、语言/缩放矩阵证据，不能把两版全部原生验收直接标完成。新的昵称颜色修正仍待游戏确认。

游戏核验：纯文本昵称应使用当前主题强调色；含红绿逐字标签的名字保持原颜色；切主题立即更新，前缀不被昵称配色带走；长名裁切和悬停不变；打开编辑器原文不多出显示外层标签，取消无保存/网络动作。保留既有输入原文和编辑预览；远期调色器仍未实现。

## 最终构建与本地候选

1.6完整经典客户端与红包/人才/Example隔离构建、1.7完整26项目构建及双版包审计通过，staging为31/24 DLL、21共享源码身份一致。最终生产源码与1.7编译清单560项指纹相符。

本地Host双版测试包：`/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/nickname-color-2026-10-17/Phinix-Rework-Nickname-Color.zip`，SHA256 `8bd835757905973cb8f6fd580ddd20728f4877c63319f6e02567594e3df5bc05`。完整日志/指纹/包审计 `/tmp/phinix-nickname-color-dual-01`。包含前轮分隔条光标及右侧分区，不捆绑独立插件，仍配套API3双运行时插件包。原生颜色显示待游戏确认；未安装、提交、推送或发布。
