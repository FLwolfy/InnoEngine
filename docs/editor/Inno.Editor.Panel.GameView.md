# Inno.Editor.Panel.GameView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Scene View](Inno.Editor.Panel.SceneView.md)

Game View 是开放 kind `inno.editor.viewport.game` 的通用 viewport host。活动 Plugin Provider 自行选择运行时数据、Pipeline、目标格式和交互行为；Panel 不查找内建 Camera，也不解释 Scene。Panel 只依赖 `IEditorGameScenePresentation`，每帧把其中原子捕获的有序 Scene 与 active Scene 转为 `RenderContentScope`；具体单 Camera 或 Base/Overlay 合成由 Plugin 决定。

`IEditorGameScenePresentation` 的 owner 是 `Inno.Editor.Scene`。Editing、Compiling 和尚未提交完成的 Preparing 阶段返回 Edit Session；Play Scene 全部物化成功后一次切换到隔离 Runtime Session；停止时先切回 Edit Session，再释放 Play 世界。Game View、Scene View、Hierarchy、Inspector、Selection 与 Gizmo 因此观察并操作同一个脚本驱动 runtime graph。Play workspace 禁止持久化且使用独立 History 分支，所以这些临时修改不会污染 Edit 文档；Panel 不访问 `RuntimeSession`，PlayMode 也不依赖 Rendering。

内容尺寸变化会在渲染安全点 resize `RenderTexture`。输出直接作为 BGFX ImGui texture 合成，不做 CPU 回读。`Editor/Appearance/Viewports/Game Background` 是 Game View 的默认背景色，并通过中立 `EditorViewportPresentation` 交给 Provider；具体 Pipeline 是否直接清屏或参与更复杂合成仍由 Provider 决定。

`Editor/Appearance/Viewports/Game Framing` 属于 Editor 预览设置，而不是 Camera、Scene 或 Build Profile。默认开启并使用 `16:9`：Panel 先在可用区域内计算完整可见的最大内接矩形，以该内矩形的真实像素尺寸向 Provider 申请 target，再把输出居中合成，剩余区域绘制纯黑 letterbox/pillarbox。关闭后 target 直接跟随 Panel 完整尺寸。该流程不会先把图像拉伸再遮罩，因此 Camera 投影和 picking 使用的 aspect 与最终显示严格一致，也不会进入游戏数据或 Player 输出。

无 Provider、Provider reload 失败或 GPU target 尚未准备好时，Panel 使用背景色填充完整区域，并以淡色、可换行、有左右/上下 padding 的文本块在区域中央显示状态；不可用输出会被释放，不影响 Editor 主界面。

Editor 的通用 `RenderRuntimeLayer` 不注入任何隐式 Scene content。Scene View 与 Game View 都必须显式提交自己的 `RenderContentScope`，因此不会在 viewport 请求之外额外把 Edit 世界渲染到 Editor backbuffer，也不存在同一帧两个互相矛盾的游戏世界来源。
