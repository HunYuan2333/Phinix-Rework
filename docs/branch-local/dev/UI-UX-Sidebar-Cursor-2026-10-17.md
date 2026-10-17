# 分隔条光标与悬停反馈

日期：2026-10-17。用户实测反馈拖动区域没有指针变化、不易发现。承接右侧分区候选未提交工作树；Host HEAD `27878e7fb78c11fec4f390db317dc0db5b0b10c9`、分支 `codex/uiux-release-main`。保留已有代码/文档/fixture，不安装、提交、推送或发布。

## 改动

- 悬停8逻辑像素分隔间隔时切换为黑边白色左右双箭头指针，32×32，中心hotspot (16,16)。拖动时即使鼠标移出分隔条也继续显示；松开后若仍悬停仍显示，否则恢复。
- 悬停/拖动时整条轨道使用当前主题Hover背景，握柄由2×48增为4×64并使用Accent（沿用focus回退）。静止不悬停时保持原细分隔；提示文字和点击区域不变，不与旁边滚动条争输入。
- 新内部 `SidebarResizeCursor` 只在实际取得光标后负责恢复。移出、关闭、失去窗口输入/应用焦点、侧栏隐藏或转入抽屉时释放；按玩家当前Prefs.CustomCursorEnabled恢复游戏自定义指针或系统默认指针，不修改该设置。Unity没有公开“读取其他Mod当前纹理”的接口，本轮不通过反射读取其私有状态，不声称能保存任意其他Mod自定义指针。
- 新 `Client/Textures/UI/Phinix/ResizeHorizontal.png` 是代码绘制的简单对称像素资源，无外部图片依赖；通过现有ContentFinder借用，纹理生命周期归游戏内容系统，不逐帧创建/销毁。资源缺失时保留普通指针并只记录一次提示。

比例、最小宽度、导航、名字编辑与所有业务动作不变。仍使用原主题与Host壳，无新SDK合同或renderer。

## API证据和验证

用Mono.Cecil只读检查本地两版引擎：Verse.CustomCursor.Activate/Deactivate均调用UnityEngine.Cursor.SetCursor；1.7 rev1248的SetCursor实际调用Waylith HardwareCursor.SetImage/Reset，非空实现。使用两版共同公开API，无UnityEditor依赖。

9项独立光标所有权检查通过：未取得时不恢复、悬停只切换一次、hotspot、借用一次、重复释放幂等、按当前玩家偏好恢复、重入/失焦释放、资源缺失不碰其他光标且仅加载/提示一次。测试调用为stub，不执行原生OS指针；位于 `/tmp/phinix-sidebar-cursor-tests-01`。本轮不无理由重跑未改的403呈现或16几何检查，不把它们写成本候选已重新通过。

## 游戏待测

在1.6/1.7分别检查：从聊天正文与侧栏滚动条移入分隔间隔马上出现左右箭头；按住拖出间隔/窗口后松手；移出、Alt-Tab、弹窗、关闭重开、缩到抽屉时恢复；游戏“自定义鼠标指针”开启和关闭两种设置；1×/1.5×/2×缩放与不同主题；确认旁边滚动条/成员点击不误触、指针不会留在调整状态。

以上原生指针、平台显示大小和操作手感仍待游戏验收。

## 最终构建及候选

1.6完整经典客户端与红包/人才/Example隔离编译、1.7完整26项目编译通过；staging审计31/24 DLL及21共享源码身份一致，ZIP审计通过。最终1.7编译清单559项生产源码指纹与当前工作树一致；最终包中32×32光标PNG与源码资源逐字一致。

本地双版Host测试包：`/home/hunyuan2333/Phinix/local-compatibility-analysis/candidates/sidebar-cursor-2026-10-17/Phinix-Rework-Sidebar-Cursor.zip`。SHA256：`82c27f2e12f3c4541e73b9fa32898ef79615e8c5972cee5cd8557b1fc25e7ff8`。日志、指纹及包审计位于 `/tmp/phinix-sidebar-cursor-dual-01`。包含前轮分区与拖动，不捆绑独立插件；配套既有API3双运行时插件包。未安装/提交/推送/发布，原生指针仍待游戏验证。
