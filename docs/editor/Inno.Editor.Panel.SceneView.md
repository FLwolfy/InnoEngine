# Inno.Editor.Panel.SceneView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Game View](Inno.Editor.Panel.GameView.md)

Scene View 是通用 viewport host。它使用开放 kind `inno.editor.viewport.scene` 查找 `[EditorViewportProviderExtension]`，并把尺寸、Editor context 和交互服务交给活动 Plugin Provider。

Panel 本身只负责：

- 请求离屏 GPU target 并以 opaque ImGui texture 显示；
- 调用 Provider 工具栏；
- 将点击位置归一化后转发给 Provider；
- 在 Provider 提供本帧 `EditorViewportManipulationSpace` 时，用 ImGuizmo 操作当前选择对象的 Transform；
- 将一次 Move/Rotate/Scale 连续拖拽记录成单个原子 Scene History transaction；
- 显示 provider 缺失或隔离错误；
- 关闭时释放 viewport target。

如何构建 Scene 数据、是否使用相机、如何导航、Picking 和使用哪个 Pipeline，都由 Provider 决定。Panel 不创建隐式 Scene/Camera，也不硬编码任何渲染路径。宿主只规定“当前选中 Scene 对象的 Transform 操作”这一 Editor 交互；Provider 用后端中立矩阵把它对齐到自己刚提交的画面，不需要暴露 BGFX、Camera 或渲染模型。
