# Inno.Editor.Panel.SceneView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Game View](Inno.Editor.Panel.GameView.md)

Scene View 是通用 viewport host。它使用开放 kind `inno.editor.viewport.scene` 查找 `[EditorViewportProviderExtension]`，并把尺寸、Editor context 和交互服务交给活动 Plugin Provider。

Panel 本身只负责：

- 请求离屏 GPU target 并以 opaque ImGui texture 显示；
- 调用 Provider 工具栏；
- 将点击位置归一化后转发给 Provider；
- 显示 provider 缺失或隔离错误；
- 关闭时释放 viewport target。

如何构建 Scene 数据、是否使用相机、如何导航、Picking、Gizmo 对应何种对象、使用哪个 Pipeline，都由 Provider 决定。Panel 不创建隐式 Scene/Camera，也不硬编码任何渲染路径。
