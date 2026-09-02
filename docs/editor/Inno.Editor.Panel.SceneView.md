# Inno.Editor.Panel.SceneView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Game View](Inno.Editor.Panel.GameView.md)

Scene View 是通用 viewport host。它使用开放 kind `inno.editor.viewport.scene` 收集所有活动 `[EditorViewportContributorExtension]`，并把尺寸、Editor context 和交互服务交给 Editor Rendering 的模型合成流程。

Panel 本身只负责：

- 请求离屏 GPU target 并以 opaque ImGui texture 显示；
- 保存一个不依赖 Plugin 类型的完整 `EditorViewportNavigationState`；
- 在提交请求前根据控制 Contributor 的 Profile 独立处理 Pan、Zoom、Orbit、Fly 与 Frame Selection；
- Planar 使用中键或 `Alt + 左键` 平移，滚轮以鼠标世界点为锚缩放；
- 允许 Orbit 控制者使用 `Alt + 左键` 环绕 pivot，允许 Fly 控制者使用右键视角与 `W/A/S/D/Q/E` 移动（Shift 加速）；
- `F` 使用控制 Contributor 的精确 bounds（缺失时使用选择对象 Transform）执行 Frame Selection；
- 每帧从 `IEditorGameScenePresentation` 原子捕获有序 Scene 与 active Scene，并作为显式 `RenderContentScope` 交给全部 Contributor；
- 将点击位置归一化后转发给唯一控制 Contributor；
- 在控制 Contributor 提供本帧 `EditorViewportManipulationSpace` 时，用 ImGuizmo 操作当前选择对象的 Transform；
- 在左侧 overlay toolbar 中选择 Move、Rotate、Scale 和 Local/World coordinate space；
- 将一次 Move/Rotate/Scale 连续拖拽记录成单个原子 Scene History transaction；
- 从 `Editor/Appearance/Viewports/Scene Background` 读取背景色并作为呈现偏好传给全部 Contributor；
- 在完整 viewport 中以居中、可换行且带 padding 的淡色文本显示 Contributor 缺失、隔离错误或 target 准备状态；
- 关闭时释放 viewport target。

Editing、Compiling 与尚未提交完成的 Preparing 阶段，presentation 指向 Edit SceneWorld；Play Scene 完整物化后，Scene View、Game View、Hierarchy、Inspector 与 Selection 在同一个安全点切换到隔离 Runtime Session，退出时再共同切回 Edit SceneWorld。Scene View 因而会显示脚本驱动的 Transform、Component 与层级变化，也允许 Gizmo 临时修改当前 runtime Transform。修改通过 `SceneEdits` 进入 Play 专用 History 分支，既不会进入 Edit History，也不会改变 Edit 对象；Play workspace 的 persistence gate 同时禁止 Save 并让 `IsDirty` 恒为 false。停止后 runtime 变化和 Play History 一起释放，原 Edit Scene 与选择状态重新显示。

Host 导航状态不是 Scene Component，也不规定 2D、3D、投影矩阵或坐标系。2D Contributor 可以只声明 Planar，3D Contributor 可以声明 Orbit/Fly/Perspective；两者能同时贡献同一 viewport，但只有 `controllerPriority` 选中的一个控制交互。每个 Contributor 必须从显式 content scope 构建自己选择参与的 Scene 数据，不能遍历进程全局 Loaded Scene。网格、坐标轴等具有渲染语义的辅助内容仍由对应模型生成。

Local/World 只改变操作轴基准，不改变 Transform 数据模型：World 轴保持世界方向，Local 轴跟随
选择对象的最终 world rotation，所以未旋转的 2D 对象看起来相同；父节点或对象有旋转时 Move/Rotate
会明显不同。Scale 按 ImGuizmo 与 Transform 的约定始终在 Local 空间执行，toolbar 会禁用该切换并
显示说明，避免制造无效状态。导航状态通过 Panel 的 `Capture`/`Restore` 写入 `editor.ini`，但不会进入
Scene、Undo 或 Plugin 持久状态。
