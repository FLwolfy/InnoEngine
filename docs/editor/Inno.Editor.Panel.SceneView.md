# Inno.Editor.Panel.SceneView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Game View](Inno.Editor.Panel.GameView.md)

Scene View 是通用 viewport host。它使用开放 kind `inno.editor.viewport.scene` 查找 `[EditorViewportProviderExtension]`，并把尺寸、Editor context 和交互服务交给活动 Plugin Provider。

Panel 本身只负责：

- 请求离屏 GPU target 并以 opaque ImGui texture 显示；
- 保存一个不依赖 Plugin 类型的 `EditorViewportCamera`；
- 使用中键拖拽或 `Alt + 左键` 平移正交视图；
- 使用滚轮以鼠标所在世界点为锚缩放；
- 将点击位置归一化后转发给 Provider；
- 在 Provider 提供本帧 `EditorViewportManipulationSpace` 时，用 ImGuizmo 操作当前选择对象的 Transform；
- 将一次 Move/Rotate/Scale 连续拖拽记录成单个原子 Scene History transaction；
- 从 `Editor/Appearance/Viewports/Scene Background` 读取背景色并作为呈现偏好传给 Provider；
- 显示 provider 缺失或隔离错误；
- 关闭时释放 viewport target。

Host 相机只是导航协议，不是 Scene Component，也不规定 2D、3D、投影矩阵或坐标系。如何构建 Scene 数据、把 Host 相机映射为什么运行时 Camera、Picking 和使用哪个 Pipeline，仍由 Provider 决定。网格、坐标轴等具有渲染语义的辅助内容也由 Provider 生成；例如 2D Plugin 将它们放在同一 GPU 批次的内容之前，所以场景物体会自然覆盖辅助线。

顶部仅保留通用 Transform 操作模式，不再显示或编辑 Plugin Camera 的位置、尺寸等字段。导航状态通过 Panel 的 `Capture`/`Restore` 写入 `editor.ini`，但不会进入 Scene、Undo 或 Plugin 持久状态。
