---
name: phinix-rework-plugin-development-cn
description: 开发或排查 Phinix Rework 客户端托管插件与纯数据主题包，涵盖公共 API、双版本打包、本地安装和 Index 交付。用于新增页签、动作、主题及插件构建排障；不替代完整 RimWorld Mod 或服务端开发流程。
---

# Phinix Rework 插件与主题开发

根据用户要完成的功能选择参考。以实际源码、编译引用和交付包为准；历史记录、工作区版本、远端提交和玩家已安装版本分别核对。

## 选择参考

| 任务 | 参考 |
| --- | --- |
| 选择 DLL 插件、主题包或 RimWorld Mod | [需求与路线](references/routes.md) |
| 查接口签名和程序集归属 | [API 速查](references/api-cheat-sheet.md) |
| 编写主题 XML、打包及安装 | [主题包](references/theme-package.md) |
| 构建 1.6/net472 与 1.7/net10.0 | [双版本构建](references/dual-runtime.md) |
| 确认版本、引用和工具链 | [环境与版本](references/environment.md) |
| 组织功能、状态和外部边界 | [工程组织](references/engineering.md) |
| Compose、依赖注入及资源清理 | [组合与所有权](references/composition.md) |
| 呈现、动作、消息与库存 | [公共能力](references/capabilities.md) |
| 新建项目、离线预检和本地安装 | [本地闭环](references/local-loop.md) |
| 故障定位与验收范围 | [验证与恢复](references/verification.md) |
| 本地分发、Release 和 Index | [正式交付](references/publication.md) |
| 采用或排查自动发布流程 | [Actions](references/actions.md) |
| 区分源码状态和历史验证 | [依据](references/basis.md)、[验证记录](references/validation.md) |

## 最小工作流

- **托管插件**：读取 API 速查和组合说明；把 `templates/minimal-plugin/` 的项目、C# 和配置复制到独立插件的 `example/`；修改包/模块/程序集身份。入口是 `Compose(IExtensionBuilder builder)`。按双版本参考构建和打包，再本地安装、重启游戏验证。模板是最小原生页签示例；完整公共呈现会话参考当前 Example。
- **主题包**：复制 `templates/theme-package/`。修改 `theme-config.json` 的 `packageId`、名称、作者、版本与兼容范围；编辑 `<PhinixTheme version="2">` 的浮点 RGBA，或按参考使用 version 3。执行 `--pack-theme CONFIG OUTPUT.zip` 与 `--validate-theme ZIP`。安装、选择和应用是独立操作，主题包无需双运行时变体。
- **交付**：沿用用户选择的本地分发、手动 Release 或自动化路线，不反复要求重新选型。未指定时先提供可本地验证的产物。DLL 插件的策略扫描和主题维护者发布是两条流程，不能承诺任意 Release 都会自动上架。

命令参数与输出格式见相应参考；不要猜测工具的成功字符串、CLI 参数或 API 重载。

## 保持的边界

- Host 提供同一套公共呈现、主题、动作与确认能力；插件保留业务、物品和协议权威。不要引用 Host 内部实现或另造官方专用 renderer。
- 借用服务不释放；插件拥有的 scope、会话、注册句柄和订阅按生命周期清理。窗口关闭不等于模块停用或业务取消。
- 回调、Task 或队列完成不等于业务确认。已发送但结果未知的物品操作保留记录，不自动返还或重发。
- 游戏对象只在有效的游戏主线程/存档上下文访问。整批转换失败时保留输入，不静默丢掉无法解码的物品。
- 保留已有修改、固定子模块 gitlink；不靠修改 vendored protobuf pins 绕过构建问题。业务代码不散落游戏版本分支；确有差异时放在插件自己的窄 adapter 中。
- 不分发游戏、Harmony、Host 核心 DLL、私有引用或凭据。开发、验证或本地打包不自动授权推送、发版、索引准入、部署或修改正式存档；沿用用户已有授权。

缺少必要引用或权限时说明受阻步骤，继续不依赖它的工作。报告区分源码检查、编译通过、白盒通过、游戏通过和待测；不把日志暂时无异常当作全面兼容。
