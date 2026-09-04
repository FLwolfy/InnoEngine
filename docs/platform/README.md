# Platform API

[Wiki 首页](../README.md) · [Native](../native/README.md) · [Rendering](../render/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Platform](Inno.Platform.md) | 后端中立 application/window/options/native-handle contract |
| [Inno.Platform.Sdl3](Inno.Platform.Sdl3.md) | SDL3 application 与 window adapter |
| [Inno.Platform.Sdl3.ImGui](Inno.Platform.Sdl3.ImGui.md) | SDL3、ImGui viewport 与 renderer bridge |

只有 SDL3 adapter 引用 `Inno.Native.Sdl3`。SDL enum、pointer 和具体 window 类型不得进入上层 public/protected API。
