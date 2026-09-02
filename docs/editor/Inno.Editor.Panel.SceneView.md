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
- 每帧从 `IEditorGameScenePresentation` 原子捕获有序 Scene 与 active Scene，并作为显式 `RenderContentScope` 交给 Provider；
- 将点击位置归一化后转发给 Provider；
- 在 Provider 提供本帧 `EditorViewportManipulationSpace` 时，用 ImGuizmo 操作当前选择对象的 Transform；
- 在左侧 overlay toolbar 中选择 Move、Rotate、Scale 和 Local/World coordinate space；
- 将一次 Move/Rotate/Scale 连续拖拽记录成单个原子 Scene History transaction；
- 从 `Editor/Appearance/Viewports/Scene Background` 读取背景色并作为呈现偏好传给 Provider；
- 显示 provider 缺失或隔离错误；
- 关闭时释放 viewport target。

Editing、Compiling 与尚未提交完成的 Preparing 阶段，presentation 指向 Edit SceneWorld；Play Scene 完整物化后，Scene View、Game View、Hierarchy、Inspector 与 Selection 在同一个安全点切换到隔离 Runtime Session，退出时再共同切回 Edit SceneWorld。Scene View 因而会显示脚本驱动的 Transform、Component 与层级变化，也允许 Gizmo 临时修改当前 runtime Transform。修改通过 `SceneEdits` 进入 Play 专用 History 分支，既不会进入 Edit History，也不会改变 Edit 对象；Play workspace 的 persistence gate 同时禁止 Save 并让 `IsDirty` 恒为 false。停止后 runtime 变化和 Play History 一起释放，原 Edit Scene 与选择状态重新显示。

Host 导航状态不是 Scene Component，也不规定 2D、3D、投影矩阵或坐标系。2D Provider 可以只声明 Planar，3D Provider 可以声明 Orbit/Fly/Perspective；两者复用同一 Host 交互而无需 Editor 为具体渲染模型增加分支。Provider 必须从显式 content scope 构建 Scene 数据，不能在收集器内部遍历进程全局 Loaded Scene。网格、坐标轴等具有渲染语义的辅助内容仍由 Provider 生成。

Local/World 只改变操作轴基准，不改变 Transform 数据模型：World 轴保持世界方向，Local 轴跟随
选择对象的最终 world rotation，所以未旋转的 2D 对象看起来相同；父节点或对象有旋转时 Move/Rotate
会明显不同。Scale 按 ImGuizmo 与 Transform 的约定始终在 Local 空间执行，toolbar 会禁用该切换并
显示说明，避免制造无效状态。导航状态通过 Panel 的 `Capture`/`Restore` 写入 `editor.ini`，但不会进入
Scene、Undo 或 Plugin 持久状态。
