# Inno.Editor.Rendering

[返回 Editor 索引](README.md) · [Wiki 首页](../README.md) · [Rendering](../render/README.md) · [Rendering ImGui](../render/Inno.Rendering.ImGui.md)

`Inno.Editor.Rendering` 是 Editor 与运行时渲染之间的后端无关桥。`EditorViewportRequest` 以稳定 viewport ID、`RenderView`、路径覆盖、Clear、优先级、Picking 选项和 selected renderer ID 描述需求；`EditorViewportOutput` 返回 opaque `ImGuiTextureHandle` 与物理尺寸；`EditorRenderingModule` 负责 Submit、Draw 和 Release。

公开 `IEditorRenderingHost` 由 Application 组合根实现：它创建/resize `RenderTexture`、提交 `RenderRequest`、把 resident texture 注册到 ImGui renderer，并在 Panel 关闭或 Editor 退出时释放 token 与 GPU target。`EditorPipelineAssetInfo`、`GetPipelineAssets` 与 `TryActivatePipelineAsset` 提供不泄漏 AssetLoader/BGFX 的 Pipeline 选择器；活动资产 reload 时重新构建 candidate，失败不替换当前画面。Panel 不接触 BGFX handle，也不执行 CPU readback。

相邻页面：[Scene View](Inno.Editor.Panel.SceneView.md) · [Game View](Inno.Editor.Panel.GameView.md) · [Inno.Rendering](../render/Inno.Rendering.md)
