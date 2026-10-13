# B41 公共借用纹理来源

`ClientPresentationRow.WithIconSource(Func<UnityEngine.Texture>)` 与静态 `WithIcon(Texture2D)` 二选一；`WithBadges` 保留当前来源。可消费Texture2D或RenderTexture。Host在可见行GUI Repaint调用一次，测量/Layout/input不调用；无需ReadPixels/GPU回读，不由Host创建/销毁纹理。关闭session不再调用source。source必须主线程、无输入/业务副作用/订阅/IO，owner保留资产寿命与失效检查；返回null表示该帧无图，仍保留图标布局槽。普通第三方亦可消费，无Pawn参数或插件Contracts依赖。

Talent借用PortraitsCache，远端只展示中性图标，动物沿用Def图。Example1.0.35 task-fact演示普通延迟texture source；Host stub228断言含不在Publish/measure/Layout解析、可见Repaint一次、不可变copy、替换静态icon及关闭不接管owner纹理。native GPU行为仍待测。
