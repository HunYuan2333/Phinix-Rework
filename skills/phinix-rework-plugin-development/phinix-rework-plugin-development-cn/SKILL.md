---
name: phinix-rework-plugin-development-cn
description: 引导或协助开发 Phinix Rework 托管插件与接入 Phinix 的 RimWorld Mod，涵盖需求选路、公开接口与 DI、独立构建、本地 ZIP 侧载迭代、兼容恢复、Release 和 Index 交付；适用于新建插件、扩展页签、消息或库存功能及构建发布排障。
---

# Phinix Rework 插件开发

先理解用户，再按当前阶段读取参考。面向小白和技术人员使用同一技术与验收标准；小白解释必要术语，每次给一组可执行步骤，技术人员直接讨论接口、约束与取舍。

## 开始与分流

信息不足时用少量简短问题确认：逐步引导还是直接实现；功能、场景与交付对象；RimWorld/宿主版本及本地试用还是正式发布。已有答案、授权或验收记录不重复询问。简单 Tab 只需简短范围和验收条件；涉及状态、物品或联机时先明确依赖、数据所有者、失败恢复与验收，再拆分实施。

简单托管 DLL 的默认路径是：确认需求与宿主能力 → 用一个项目组织最小功能 → 独立构建/预检 → 开发者侧载与重启验证。小插件所需工程组织由本 skill 自带，无需加载外部 Mod skill；扩大到 Mod 路线时再按授权联合使用。

按需阅读，不先加载所有仓库：

| 什么时候读 | 参考 |
| --- | --- |
| 需求未定或涉及游戏原生功能 | [需求与路线](references/routes.md) |
| 开始构建、版本不明、缺工具或准备发布 | [环境与版本](references/environment.md) |
| 选择托管路线后组织新代码、增加功能或整理职责 | [轻量工程组织](references/engineering.md) |
| 编写模块、服务或生命周期 | [DI 与所有权](references/composition.md) |
| 实现 UI、消息或库存中的对应能力 | [能力入口](references/capabilities.md)（仅对应小节） |
| 本地编译、打包、预检、侧载或修订 | [本地闭环](references/local-loop.md)（主要路线） |
| 验收、升级、恢复或排障 | [验证与恢复](references/verification.md) |
| 正式 DLL Release/Index、通知或商店未更新排障，以及工坊交付 | [正式发布](references/publication.md) |
| 使用或配置 main CI、发行后通知与失败重试 | [Actions 实践](references/actions.md) |
| 需要判断上述知识依据与当前交付限制 | [依据与状态](references/basis.md) |
| 维护本 skill 或检查引导场景 | [验证记录](references/validation.md) |

正式 Release 公开、通知/扫描成功、准入和目录发布、商店可见是不同层次；正式发布或 Index 排障时读取上述发布与 Actions 参考，不把绿色 job 当作全部更新已完成。

## 必要边界

- 本 skill 是开发引导草案。新 DI、配置/预检及侧载须确认目标宿主实际具备；工作区实现、提交、CI 固定引用、发行包和游戏验收分别记录，不能互相替代。
- 先读目标仓 AGENTS.md、分支及完整差异（含未跟踪文件与子模块内修改）。保留并行工作；固定宿主与 Common/protobuf gitlink，不使用 `submodule update --remote`，不凭过期 origin 判断远端。
- 项目名是 **Phinix Rework**。规范仓库为 `Phinix-Rework`、`Phinix-Rework-Common`、`Phinix-Rework-Server`；命名整理不改变协议、程序集、命名空间、ID、持久化键及 `Dependencies/Phinix.Common`。
- 新插件优先 `ClientExtensionModule.Compose` 与中立 DI。构造被动，部分失败可清理；借用服务不释放，自有资源/订阅成对清理；游戏对象与 GUI 在主线程，延迟动作核对当前上下文。
- 只依赖公开契约，不依赖宿主内部类或具体容器，不改宿主迁就单个插件，不扫描或管理第三方 Mod。API 可发现不代表依赖已启用/激活。
- 保持权威确认、整批交付和幂等。已经发送但结果未知时保留托管记录并核对，不自动返还或重发奖励。
- 本地开发无需 GitHub、token、Release 或 Index。优先独立编译与开发者侧载，不为试用伪装传统 Mod，不添加热加载、生成器、签名、专用宿主 API 或额外 Phinix NuGet SDK。
- 本地准备先完成。推送、Issue/PR、发布、安装软件或外部 skill 按用户授权执行；本 skill 的加载本身不授权这些动作。缺少权限时停在可审阅产物，并说明具体原因。授权仍有效时不重复询问。

交付实现/引导时给出完成内容、验证证据及其边界、目标版本与待办。缺游戏或远端验证就明确未验证，不自行宣布正式验收；发现需要宿主增强的能力单列待办。
