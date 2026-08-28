# Inno.Rendering.ImGui

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [BGFX 后端](Inno.Rendering.Bgfx.md) · [Platform ImGui](../platform/Inno.Platform.ImGui.md)

## 职责与边界

`Inno.Rendering.ImGui` 实现可插拔 `IPlatformImGuiRenderer`，把 ImGui draw data 转为 RenderGraph Pass。它让 Editor 主窗口、detached viewport 和 Scene/Game `RenderTexture` 共享一个 BGFX context，不经过 CPU readback；Platform API 只看 `ImGuiTextureHandle` 与平台窗口目标。

## 公开 API

| API | 语义 |
| --- | --- |
| `BgfxImGuiRenderer` | ImGui renderer、frame contributor、多 viewport surface 与纹理 token 生命周期 |
| `BgfxImGuiRenderer.vertexLayout` | 创建兼容 ImGui Program 所需的中立 vertex layout |
| `RegisterTexture` / `UnregisterTexture` | 在 opaque ImGui token 与 device-generation texture 之间建立短期映射 |
| `ReplaceShaderArtifact` / `lastShaderError` | 成功候选原子替换与 last-good shader 状态 |
| `BgfxImGuiShaderSource` | 宿主经统一 shaderc 编译的内置 source 常量 |

每帧先 `PrepareFrame` 捕获 draw packet，再由 `AddRenderPasses` 贡献主窗口与 viewport Pass；renderer 自身不调用 `bgfx.frame`。窗口 resize、texture token 和 framebuffer 的创建/替换/释放只在安全点发生。

## 相邻页面

- [Inno.Rendering.Bgfx](Inno.Rendering.Bgfx.md)：设备、surface 与 Encoder 实现。
- [Inno.Platform.ImGui](../platform/Inno.Platform.ImGui.md)：可插拔呈现协议和默认 SDL renderer。
- [Inno.Editor.Rendering](../editor/Inno.Editor.Rendering.md)：Scene/Game viewport 桥接。
