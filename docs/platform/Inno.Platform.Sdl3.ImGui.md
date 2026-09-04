# Inno.Platform.Sdl3.ImGui

[Platform 索引](README.md) · [Editor ImGui](../editor/Inno.Editor.ImGui.md) · [BGFX ImGui](../render/Inno.Rendering.Bgfx.ImGui.md)

## 公开 API

- `PlatformImGuiContext`、`ImGuiContextFlags`：ImGui/viewport session ownership。
- `IPlatformImGuiRenderer`, `PlatformImGuiViewportTarget`, `ImGuiTextureHandle`：平台与 renderer bridge。
- `Sdl3PlatformApplicationImGuiExtensions`：在 SDL3 Application 上组合 ImGui。
- `ImGuiFont`, `ImGuiFontStyle`, `ImGuiIcon`：Editor presentation assets。

该项目属于 Editor deployment，不进入 Player。Context 必须在 SDL3 Application 活跃期间 Dispose；viewport 与 GPU renderer 的 retiring resources 在帧安全点释放。

每个 `PlatformImGuiContext` 独立拥有自己的 viewport、SDL window 与 renderer target 映射。Native
ImGui callback 只使用当前 `ImGuiContext` 查找 owner backend；创建第二个 context 不会覆盖第一个
context 的路由，Dispose 只注销精确 owner。该 router 保存 Host-owned backend，不保存 Plugin 类型、
实例或 delegate，因此不会延长 collectible extension generation。

这项 context 隔离不改变 BGFX 的进程约束：平台层可以正确管理多个 ImGui context，但当前 BGFX
Adapter 同一进程只允许一个活动设备。Editor Application 是唯一图形 composition root；若未来要
同时运行多个图形 Host，应选择多进程隔离或实现真正支持多 device 的另一个 Rendering backend。
