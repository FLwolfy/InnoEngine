# Inno.Native.ImGui

[Native 索引](README.md) · [Editor ImGui](../editor/Inno.Editor.ImGui.md)

该项目提供 cimgui generated bindings。手写稳定入口为 `ImGuiConfig` 和 `ImGui` partial static API；其余公开 enum、struct、pointer wrapper 与 function table 是外部 C API 映射，完整清单由生成 XML 提供。

它只服务 Editor/SDL3-ImGui/BGFX-ImGui adapter。业务脚本与 Runtime 不应直接依赖 Native ImGui。
