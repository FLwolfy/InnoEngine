# Inno.Editor.Panel.GameView

[Editor 索引](README.md) · [Editor Rendering](Inno.Editor.Rendering.md) · [Scene View](Inno.Editor.Panel.SceneView.md)

Game View 是开放 kind `inno.editor.viewport.game` 的通用 viewport host。活动 Plugin Provider 自行选择运行时数据、Pipeline、目标格式和交互行为；Panel 不查找内建 Camera，也不解释 Scene。

内容尺寸变化会在渲染安全点 resize `RenderTexture`。输出直接作为 BGFX ImGui texture 合成，不做 CPU 回读。无 Provider 或 Provider reload 失败时 Panel 显示明确状态并释放不可用输出，不影响 Editor 主界面。
