# Build API

[Wiki 首页](../README.md) · [Runtime](../runtime/README.md) · [Native](../native/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Build](Inno.Build.md) | Profile、request/result、Plugin/Game pipeline 与内部 staging stages |
| [Inno.Build.Platform.MacOS](Inno.Build.Platform.MacOS.md) | macOS ARM64 target artifact 与 app bundle |
| [Inno.Build.Platform.Windows](Inno.Build.Platform.Windows.md) | Windows x64 target artifact 与 portable application directory |
| [Inno.Build.Cli](Inno.Build.Cli.md) | Headless Composition Root |
| [Inno.Build.SupportPacks](Inno.Build.SupportPacks.md) | 生产 source-independent Player Support Pack |
| [Inno.Build.Toolchains](Inno.Build.Toolchains.md) | toolchain layout、process environment 与 artifact copy |
| [Inno.Build.Toolchains.Bgfx](Inno.Build.Toolchains.Bgfx.md) | BGFX native build CLI |
| [Inno.Build.Toolchains.Bgfx.Tools](Inno.Build.Toolchains.Bgfx.Tools.md) | shaderc/texturec 与目标内容编译 |
| [Inno.Build.Toolchains.Sdl3](Inno.Build.Toolchains.Sdl3.md) | SDL3 native build CLI |
| [Inno.Build.Toolchains.MiniAudio](Inno.Build.Toolchains.MiniAudio.md) | miniaudio native build CLI |
| [Inno.Build.Toolchains.ImGui](Inno.Build.Toolchains.ImGui.md) | cimgui native build CLI |
| [Inno.Build.Toolchains.ImGuizmo](Inno.Build.Toolchains.ImGuizmo.md) | cimguizmo native build CLI |

Game Build 固定执行 Validate → Combined Snapshot → Scripts/Target Artifacts → Content Pack → Support Pack composition → Platform Package → Atomic Commit。Editor Exporting 只调用该 API，不拥有构建机制。
