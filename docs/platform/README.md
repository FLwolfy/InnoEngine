# Platform API

[Wiki 首页](../README.md) · [Native](../native/README.md) · [Rendering](../render/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Platform](Inno.Platform.md) | 后端中立 application/window/options/native-handle contract |
| [Inno.Adapter.Platform](Inno.Adapter.Platform.md) | 平台 backend 选择与 factory contract |
| [Inno.Adapter.Platform.Sdl3](Inno.Adapter.Platform.Sdl3.md) | SDL3 application 与 window adapter |
| [Inno.Adapter.Presentation](Inno.Adapter.Presentation.md) | Host presentation 的中立 context、texture token 与 authoring catalog contract |
| [Inno.Adapter.Presentation.ImGui.Sdl3](Inno.Adapter.Presentation.ImGui.Sdl3.md) | SDL3、ImGui viewport 与 event bridge implementation |

只有 SDL3 adapter 引用 `Inno.Native.Sdl3`。SDL enum、pointer 和具体 window 类型不得进入上层 public/protected API。
