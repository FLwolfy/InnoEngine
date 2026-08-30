# Inno.Rendering.ImGui

[返回 Rendering 索引](README.md) · [Wiki 首页](../README.md) · [BGFX 后端](Inno.Rendering.Bgfx.md) · [Platform ImGui](../platform/Inno.Platform.ImGui.md)

## 职责与边界

`Inno.Rendering.ImGui` 实现可插拔 `IPlatformImGuiRenderer`，把 ImGui draw data 转为 RenderGraph Pass。它让 Editor 主窗口、detached viewport 和 Scene/Game `RenderTexture` 共享一个 BGFX context，不经过 CPU readback；Platform API 只看 `ImGuiTextureHandle` 与平台窗口目标。

Editor 保持单一 GPU 呈现所有者：SDL 负责窗口、事件、输入、光标和原生 viewport 生命周期，BGFX 负责主 backbuffer、detached viewport、Scene/Game 目标和 ImGui draw data。轻量应用仍可省略 renderer 参数并使用 `Inno.Platform.ImGui` 的默认 SDL renderer，但同一个窗口不能同时由 SDL renderer 与 BGFX 呈现。

Editor 主 backbuffer 使用 sRGB 编码。ImGui packed vertex color 按显示空间（sRGB）定义，因此内置 ImGui fragment shader 会先把 RGB 转换到线性空间，再与字体或 `RenderTexture` 采样相乘；alpha 保持线性。这样主题色不会被 backbuffer 二次提亮，Scene/Game 的线性 Tone Mapping 输出也能在最终 present 时只进行一次 sRGB 编码。Presentation clear color 同样在线性空间提交。

## 公开 API

| API | 语义 |
| --- | --- |
| `BgfxImGuiRenderer` | ImGui renderer、frame contributor、多 viewport surface 与纹理 token 生命周期 |
| `BgfxImGuiRenderer.vertexLayout` | 创建兼容 ImGui Program 所需的中立 vertex layout |
| `RegisterTexture` / `UnregisterTexture` | 在 opaque ImGui token 与 device-generation texture 之间建立短期映射 |
| `ReplaceShaderArtifact` / `lastShaderError` | 成功候选原子替换与 last-good shader 状态 |
| `BgfxImGuiShaderSource` | 宿主经统一 shaderc 编译的内置 source 常量 |

每帧先 `PrepareFrame` 捕获 draw packet，再由 `AddRenderPasses` 贡献主窗口与 viewport Pass；renderer 自身不调用 `bgfx.frame`。窗口 resize、texture token 和 framebuffer 的创建/替换/释放只在安全点发生。

关闭时，Platform 先销毁 native ImGui viewport，`BgfxImGuiRenderer.Dispose()` 再标记 renderer-owned texture、vertex/index buffer、pipeline 与 surface；宿主随后开启一个 maintenance frame 执行 `PrepareFrame`，将这些资源送入设备延迟销毁队列，最后才允许 `BgfxDevice.Dispose()` 排空队列并关闭 BGFX。该顺序不依赖用户 Pipeline，也不会把 ImGui GPU 状态留给 Plugin generation。

## 相邻页面

- [Inno.Rendering.Bgfx](Inno.Rendering.Bgfx.md)：设备、surface 与 Encoder 实现。
- [Inno.Platform.ImGui](../platform/Inno.Platform.ImGui.md)：可插拔呈现协议和默认 SDL renderer。
- [Inno.Editor.Rendering](../editor/Inno.Editor.Rendering.md)：Scene/Game viewport 桥接。
