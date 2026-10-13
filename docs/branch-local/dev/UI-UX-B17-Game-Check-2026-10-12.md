# B17 游戏验收（待测）

配对使用 B17 主包 / Example 1.0.24，重启游戏。B16 用户已确认时间截断和导航排版修复；本清单是新增流程，不宣称已通过。

1. 商店普通动作/更多/连接方式的圆角略大，密度、对称、时间和导航保持；不同 UI scale/宽窄窗口不截字。
2. DevMode→更多→本地插件 ZIP：粘贴路径、浏览父目录/子目录、选择 ZIP，长目录只滚列表，路径/浏览/选择固定，极短窗仍可达。无权限/不存在/非 ZIP/超限反馈可读。
3. 准备中与真实校验错误；有效 ZIP 待确认区包含 package/version/SHA，未确认不能安装。点安装后公共确认；取消确认保留待确认，页面取消丢弃 candidate；关闭/重开 Store 不自动取消业务或返还。
4. 安装后真实 controller 结果、desired/active/restart 区分；准备后改源 ZIP 仍冻结原 bytes。关闭视图/DevMode 撤销/环境变化/重复/降级/冲突按原服务限制处理。
5. 普通模式→本地主题包：选择 ZIP→真实检查信息→安装/更新→刷新列表→主题选择器可见/热应用。失败显示 Error；不把 callback 完成当安装成功。
6. 选择已安装主题包→卸载公共确认；取消不卸载，确认按原 service 整包移除，刷新后不保留无效选择。选中主题包卸载保留 profile 与原 fallback；损坏/丢失/更新移 variant 的 intent/fallback 原专项继续待测。
7. Example Host 计数窗口：公共动作“切换固定单栏 / 双栏布局”；单栏工具固定、长列表/内容滚动、短窗头部滚动、原字段/IME/composer/feed/reset 确认与双栏均正常。
8. 模块 stop、窗口关闭、贡献点或语言变化后旧 action/confirmation 不执行；第三方 Store contribution 可依据 Surface 选择适用页。

继续前批库存和 fcitx 业务专项。记录候选 SHA、分辨率/scale、步骤、结果、截图/日志。
