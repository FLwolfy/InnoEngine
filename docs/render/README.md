# Rendering API

[Wiki 首页](../README.md) · [Assets](../assets/README.md) · [Build](../build/README.md)

Rendering Core 是后端中立机制，不内建 2D/3D/PBR/Forward/Deferred/Camera/Light 世界观。

| 项目 | 职责 |
| --- | --- |
| [Inno.Rendering](Inno.Rendering.md) | capability、resource、RenderGraph、command、Pipeline、Shader IR 与 request contract |
| [Inno.Rendering.Runtime](Inno.Rendering.Runtime.md) | 帧调度、GPU resource generation、Pending 安全退休与 safe-point reload |
| [Inno.Rendering.Assets](Inno.Rendering.Assets.md) | Shader/Texture/Geometry importer 与离线编译 contract |
| [Inno.Rendering.Shaders](Inno.Rendering.Shaders.md) | 源码函数、节点降低、typed stage/资源/结构化区域；完整资产闭环实施中 |
| [Inno.Rendering.Shaders.Tests](Inno.Rendering.Shaders.Tests.md) | 源码/图/资源/控制流与代际测试、Metal 原生编译，不替代 GPU 验收 |
| [Inno.Adapter.Rendering](Inno.Adapter.Rendering.md) | Rendering backend 选择、runtime device factory 与 authoring compiler factory contract |
| [Inno.Adapter.Rendering.Authoring](Inno.Adapter.Rendering.Authoring.md) | Authoring-only shader/texture compiler factory contract |
| [Inno.Adapter.Rendering.Bgfx](Inno.Adapter.Rendering.Bgfx.md) | 唯一 BGFX device adapter |
| [Inno.Adapter.Presentation.ImGui.Bgfx](Inno.Adapter.Presentation.ImGui.Bgfx.md) | BGFX/ImGui GPU 合成 implementation |
| [Inno.Editor.Panel.ShaderEditor](../editor/Inno.Editor.Panel.ShaderEditor.md) | `.ishader` 选择跟随、图画布、节点设置与自动保存；创作层，不属于运行时 |

目标是所有 `.ishader` 由图定义，源码仅为函数节点，Material 只保存 Shader 引用与参数。
Shader 图已成为唯一创作格式；旧 MaterialGraph 和全文源码 Shader API 已移除。验收状态与未完成项目详见
[统一 Shader 实施记录](../issues/2026-09-11-unified-shader-implementation.md)。只有 BGFX adapter 和对应 toolchain 可以引用 BGFX Native。
