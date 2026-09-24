# Native API

[Wiki 首页](../README.md) · [Platform](../platform/README.md) · [Rendering](../render/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Native.LibraryLoading](Inno.Native.LibraryLoading.md) | 受控动态库查找与加载 |
| [Inno.Native.Sdl3](Inno.Native.Sdl3.md) | SDL3 generated bindings |
| [Inno.Native.MiniAudio](Inno.Native.MiniAudio.md) | miniaudio generated bindings |
| [Inno.Native.Bgfx](Inno.Native.Bgfx.md) | BGFX generated bindings, DLL loader and command-line tool invocation |
| [Inno.Native.ImGui](Inno.Native.ImGui.md) | cimgui bindings |
| [Inno.Native.ImGuizmo](Inno.Native.ImGuizmo.md) | cimguizmo bindings |
| [Inno.Native.Text](Inno.Native.Text.md) | FreeType/HarfBuzz Text bridge generated bindings |
| [Inno.Native.UI](Inno.Native.UI.md) | RmlUi bridge generated bindings |

Native 不引用上层项目。Generated binding API 反映外部库 contract；上层只能通过对应 Platform、Rendering、Audio、Text 或 UI adapter 使用。

绑定配置与扩展由各 Native 项目自己的 `Bindings/` 目录持有。独立生成、检查和跨平台验收流程见 [Native binding generation](BindingGeneration.md)。
