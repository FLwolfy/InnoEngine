# Inno.Native.Sdl3

[Native 索引](README.md) · [Platform SDL3](../platform/Inno.Platform.Sdl3.md)

该项目是 SDL3 generated binding assembly。公开面包含 SDL 函数、结构、枚举、delegate、pointer wrapper，以及生成器附带的 `Point32`、`Msg` 等平台结构。精确成员以生成 XML 为准。

只有 `Inno.Platform.Sdl3`、SDL3 toolchain 与 Native tests 可以引用。上层通过中立 `IPlatformApplication`/`IPlatformWindow` 工作，不直接调用 SDL。
