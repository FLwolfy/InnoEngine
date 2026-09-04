# Inno.Rendering.Bgfx.ImGui

[Rendering 索引](README.md) · [BGFX](Inno.Rendering.Bgfx.md) · [Platform ImGui](../platform/Inno.Platform.Sdl3.ImGui.md)

该 adapter 把 ImGui draw data 合成到 BGFX，不属于用户 Render Pipeline，也不进入普通游戏脚本 API。

公开 `BgfxImGuiShaderSource` 提供 adapter 自身的稳定 shader source contract；renderer、buffer、texture 与 submission 实现保持 internal，由 Editor Application 组合。GPU 资源在 BGFX device 仍活跃时释放，窗口/viewport 关闭不直接销毁正在提交的资源。
