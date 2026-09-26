# Inno.Editor.Panel.GameView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Scene View](Inno.Editor.Panel.SceneView.md)

Game View 是开放 kind `inno.editor.viewport.game` 的通用 viewport host。Panel 不查找内建 Camera，也不解释 Scene。Panel 从 `IEditorGameScenePresentation` 捕获内容范围，并提供输出尺寸、viewport 和指针输入。Rendering2D 等插件的适配器把这些数据交给同一个 `IRenderModel`，由模型建立 Camera stack 和帧数据。

Canvas 只发布世界空间 `IViewContentSource`，不提供 Game View contributor。单独安装 Canvas 时 Game View 不出图；安装 Rendering2D 并在场景中添加 Camera2D 与 Rendering2DSceneSystem 后，2D 模型将 Canvas 和精灵统一排序。多个模型接受同一个 Game View 输出时必须指定 `RenderOutputRoute`，否则显示诊断。

`IEditorGameScenePresentation` 的 owner 是 `Inno.Editor.Scene`。Editing、Compiling 和尚未提交完成的 Preparing 阶段返回 Edit Session；Play Scene 全部物化成功后一次切换到隔离 Runtime Session；停止时先切回 Edit Session，再释放 Play 世界。Game View、Scene View、Hierarchy、Inspector、Selection 与 Gizmo 因此观察并操作同一个脚本驱动 runtime graph。Play workspace 禁止持久化且使用独立 History 分支，所以这些临时修改不会污染 Edit 文档；Panel 不访问 `RuntimeSession`，PlayMode 也不依赖 Rendering。

内容尺寸变化会在渲染安全点 resize `RenderTexture`。输出直接作为 BGFX ImGui texture 合成，不做 CPU 回读。`Editor/Appearance/Viewports/Game Background` 是 Game View 的默认背景色，并通过中立 `EditorViewportPresentation` 交给渲染模型。

`Project/Player/Presentation` 编辑项目级 `GamePresentationSettings`，由 Game View 与导出 Player 共同消费。默认参考帧为 `1280×720` 且开启 `Preserve Aspect Ratio`：Panel 先在可用区域内计算完整可见的最大内接矩形，以该内矩形对应的物理像素尺寸申请 composition target，再把输出居中合成，剩余区域绘制纯黑 letterbox/pillarbox。关闭后 target 直接跟随 Panel 完整尺寸。该流程不会先把图像拉伸再遮罩，因此各模型投影使用的 aspect 与最终显示严格一致；同一项目设置进入部署内容，Player resize 后也计算完全相同的 content viewport。

在高像素密度显示器上，Panel 仍以 ImGui 逻辑单位计算可见区域，却按 framebuffer scale 为 composition target 分配物理像素。`EditorViewportPresentation.pixelDensity` 把这一比例交给 Contributor；布局类内容可保持原有逻辑大小，同时按物理分辨率栅格化。Panel 绘制输出时使用逻辑区域大小。

无适用 Contributor、模型争用、贡献失败或 GPU target 尚未准备好时，Panel 使用背景色填充完整区域，并在中央显示状态；不可用输出会被释放，不影响 Editor 主界面。

Editor 的通用 `RenderRuntime` 不注入任何隐式 Scene content。Scene View 与 Game View 都必须显式提交自己的 `RenderContentScope`，因此不会在 viewport 请求之外额外把 Edit 世界渲染到 Editor backbuffer，也不存在同一帧两个互相矛盾的游戏世界来源。
