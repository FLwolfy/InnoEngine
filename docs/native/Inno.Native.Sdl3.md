# Inno.Native.Sdl3

[Native 索引](README.md) · [Platform SDL3](../platform/Inno.Adapter.Platform.Sdl3.md)

该项目是 SDL3 generated binding assembly。`Generated/Bindings.cs` 由 BGCS 从 SDL3 header 和配置生成函数、结构、Flags enum、delegate 与 pointer wrapper；`SDL.cs` 仅加载原生库并初始化函数表。不另附手写 `Point32`、`Msg` 或 Flags 类型。精确成员以生成 XML 为准。

只有 `Inno.Adapter.Platform.Sdl3`、SDL3 toolchain 与 Native tests 可以引用。上层通过中立 `IPlatformApplication`/`IPlatformWindow` 工作，不直接调用 SDL。
