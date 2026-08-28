# Inno.Editor.Panel.SceneView

[返回 Editor 索引](README.md) · [Wiki 首页](../README.md) · [Editor Rendering](Inno.Editor.Rendering.md)

该 Panel 使用独立 Editor Camera 提交 GPU Scene View，不修改任何运行时 `Camera`。工具栏可选择项目 `.irenderpipeline`、按 View 覆盖 `Automatic`/`ForwardPlus`/`Deferred`，内容尺寸直接驱动离屏 RenderTexture resize，并启用 Picking 输出。点击视口以 camera ray 与当前 RenderWorld bounds 做无 GPU 回读选择；GPU object-ID target 仍由 Pipeline 生成，供后续异步精确 picking 使用。选中 renderer ID 会随 `RenderRequest` 进入 Outline Feature。

Move/Rotate/Scale 与 World/Local ImGuizmo 直接覆盖 GPU viewport；一次连续拖动在结束时通过 `SceneEdits.ChangeProperty` 和单个 `EditorHistoryTransaction` 记录为可逆修改。Panel 是 internal feature；稳定扩展入口是 `[EditorPanel("rendering.scene-view", ...)]` 及 `EditorRenderingModule`。`Capture`/`Restore` 只持久化 Pipeline path、Render Path override、gizmo operation/mode 与 camera 导航状态；selection、Picking GPU resource 和临时 camera 不写入 `editor.ini`。关闭 Panel 会释放 viewport ID 对应 target。

相邻页面：[Game View](Inno.Editor.Panel.GameView.md) · [Rendering Pipelines](../render/Inno.Rendering.Pipelines.md)
