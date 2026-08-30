# Inno.Editor.Panel.SceneView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Game View](Inno.Editor.Panel.GameView.md)

Scene View 是通用 viewport host。它使用开放 kind `inno.editor.viewport.scene` 查找 `[EditorViewportProviderExtension]`，并把尺寸、Editor context 和交互服务交给活动 Plugin Provider。

Panel 本身只负责：

- 请求离屏 GPU target 并以 opaque ImGui texture 显示；
- 保存一个不依赖 Plugin 类型的完整 `EditorViewportNavigationState`；
- 在提交请求前根据 Provider Profile 独立处理 Pan、Zoom、Orbit、Fly 与 Frame Selection；
- Planar 使用中键或 `Alt + 左键` 平移，滚轮以鼠标世界点为锚缩放；
- 允许 Orbit 的 Provider 使用 `Alt + 左键` 环绕 pivot，允许 Fly 的 Provider 使用右键视角与 `W/A/S/D/Q/E` 移动（Shift 加速）；
- `F` 使用 Provider 的精确 bounds（缺失时使用选择对象 Transform）执行 Frame Selection；
- 把当前 Editor workspace 的有序 Scene 和 active Scene 作为显式 `RenderContentScope` 交给 Provider；
- 将点击位置归一化后转发给 Provider；
- 在 Provider 提供本帧 `EditorViewportManipulationSpace` 时，用 ImGuizmo 操作当前选择对象的 Transform；
- 将一次 Move/Rotate/Scale 连续拖拽记录成单个原子 Scene History transaction；
- 从 `Editor/Appearance/Viewports/Scene Background` 读取背景色并作为呈现偏好传给 Provider；
- 显示 provider 缺失或隔离错误；
- 关闭时释放 viewport target。

Host 导航状态不是 Scene Component，也不规定 2D、3D、投影矩阵或坐标系。2D Provider 可以只声明 Planar，3D Provider 可以声明 Orbit/Fly/Perspective；两者复用同一 Host 交互而无需 Editor 为具体渲染模型增加分支。Provider 必须从显式 content scope 构建 Scene 数据，不能在收集器内部遍历进程全局 Loaded Scene。网格、坐标轴等具有渲染语义的辅助内容仍由 Provider 生成。

顶部仅保留通用 Transform 操作模式，不再显示或编辑 Plugin Camera 的位置、尺寸等字段。导航状态通过 Panel 的 `Capture`/`Restore` 写入 `editor.ini`，但不会进入 Scene、Undo 或 Plugin 持久状态。
