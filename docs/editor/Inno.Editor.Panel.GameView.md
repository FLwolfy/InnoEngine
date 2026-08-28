# Inno.Editor.Panel.GameView

[返回 Editor 索引](README.md) · [Wiki 首页](../README.md) · [Editor Rendering](Inno.Editor.Rendering.md)

该 Panel 查找第一个 active runtime `Camera`，使用其 `CreateRenderRequest` 契约提交 GPU Game View。没有 Camera 时不创建隐式 Scene 或 Camera，并立即释放旧 viewport target。内容尺寸变化通过 Host 安全替换 RenderTexture；画面以 opaque ImGui texture 直接合成，无 CPU readback。

Panel 没有额外公开 API；稳定行为由 `Camera`、`EditorViewportRequest` 与 `EditorRenderingModule` 提供。关闭 Panel 时释放 `game-view` target。

相邻页面：[Scene View](Inno.Editor.Panel.SceneView.md) · [Inno.Rendering](../render/Inno.Rendering.md)
