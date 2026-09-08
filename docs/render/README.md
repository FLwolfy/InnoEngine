# Rendering API

[Wiki 首页](../README.md) · [Assets](../assets/README.md) · [Build](../build/README.md)

Rendering Core 是后端中立机制，不内建 2D/3D/PBR/Forward/Deferred/Camera/Light 世界观。

| 项目 | 职责 |
| --- | --- |
| [Inno.Rendering](Inno.Rendering.md) | capability、resource、RenderGraph、command、Pipeline、Shader IR 与 request contract |
| [Inno.Rendering.Runtime](Inno.Rendering.Runtime.md) | 帧调度、GPU resource generation、Pending 安全退休与 safe-point reload |
| [Inno.Rendering.Assets](Inno.Rendering.Assets.md) | Shader/Texture/Geometry importer 与离线编译 contract |
| [Inno.Adapter.Rendering](Inno.Adapter.Rendering.md) | Rendering backend 选择、runtime device factory 与 authoring compiler factory contract |
| [Inno.Adapter.Rendering.Authoring](Inno.Adapter.Rendering.Authoring.md) | Authoring-only shader/texture compiler factory contract |
| [Inno.Adapter.Rendering.Bgfx](Inno.Adapter.Rendering.Bgfx.md) | 唯一 BGFX device adapter |
| [Inno.Adapter.Presentation.ImGui.Bgfx](Inno.Adapter.Presentation.ImGui.Bgfx.md) | BGFX/ImGui GPU 合成 implementation |
| [Inno.Rendering.ShaderGraph](Inno.Rendering.ShaderGraph.md) | Graph 前端、节点 registry 与共享 Shader IR 输出 |

手写 Shader 与 ShaderGraph 进入同一 IR、验证、目标编译、反射和 last-good 链。只有 BGFX adapter 和对应 toolchain 可以引用 BGFX Native。
