# Native API

[Wiki 首页](../README.md) · [Platform](../platform/README.md) · [Rendering](../render/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Native.LibraryLoading](Inno.Native.LibraryLoading.md) | 受控动态库查找与加载 |
| [Inno.Native.Sdl3](Inno.Native.Sdl3.md) | SDL3 generated bindings |
| [Inno.Native.MiniAudio](Inno.Native.MiniAudio.md) | miniaudio generated bindings |
| [Inno.Native.Bgfx](Inno.Native.Bgfx.md) | BGFX generated/handwritten binding surface |
| [Inno.Native.Bgfx.Tools](Inno.Native.Bgfx.Tools.md) | BGFX command-line tool invocation |
| [Inno.Native.ImGui](Inno.Native.ImGui.md) | cimgui bindings |
| [Inno.Native.ImGuizmo](Inno.Native.ImGuizmo.md) | cimguizmo bindings |

Native 不引用上层项目。Generated binding API 反映外部库 contract；上层只能通过对应 Platform、Rendering 或 Audio adapter 使用。
