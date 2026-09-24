# Inno.Native.ImGui

[Native 索引](README.md) · [Editor ImGui](../editor/Inno.Editor.ImGui.md)

该项目提供 cimgui generated bindings。运行时源码只有 `ImGui.cs` 原生库/函数表加载器和 `Generated/Bindings.cs`。配置与 BGCS emitter plugin 均属于本项目的 `Bindings/` 目录；`ImVector<T>`、`ImTextureID` 的显式 ABI 实现由 `Bindings/Extension` 合入生成文件，而非 BGCS 核心中的库名特判。生成方式见 [Native binding generation](BindingGeneration.md)。完整清单由生成 XML 提供。

它只服务 Editor/SDL3-ImGui/BGFX-ImGui adapter。业务脚本与 Runtime 不应直接依赖 Native ImGui。
