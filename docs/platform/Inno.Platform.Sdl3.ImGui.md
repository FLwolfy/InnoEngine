# Inno.Platform.Sdl3.ImGui

[Platform 索引](README.md) · [Editor ImGui](../editor/Inno.Editor.ImGui.md) · [BGFX ImGui](../render/Inno.Rendering.Bgfx.ImGui.md)

## 公开 API

- `PlatformImGuiContext`、`ImGuiContextFlags`：ImGui/viewport session ownership。
- `IPlatformImGuiRenderer`, `PlatformImGuiViewportTarget`, `ImGuiTextureHandle`：平台与 renderer bridge。
- `Sdl3PlatformApplicationImGuiExtensions`：在 SDL3 Application 上组合 ImGui。
- `ImGuiFont`, `ImGuiFontStyle`, `ImGuiIcon`：Editor presentation assets。

该项目属于 Editor deployment，不进入 Player。Context 必须在 SDL3 Application 活跃期间 Dispose；viewport 与 GPU renderer 的 retiring resources 在帧安全点释放。
