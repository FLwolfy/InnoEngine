# Input API

[Wiki 首页](../README.md) · [Runtime](../runtime/README.md) · [Platform](../platform/README.md)

Input 分成后端中立契约、Session Runtime 与 SDL3 adapter；Action Map、rebinding、UI navigation 等解释策略不进入本体机制。

| 项目 | 职责 |
| --- | --- |
| [Inno.Input](Inno.Input.md) | `IInputService`、不可变 `InputSnapshot`、脚本 façade 与 backend contract |
| [Inno.Input.Runtime](Inno.Input.Runtime.md) | 每 Session 的 Begin/End frame scope 与快照生命周期 |
| [Inno.Adapter.Input](Inno.Adapter.Input.md) | Input backend 选择、event source 与 factory contract |
| [Inno.Adapter.Input.Sdl3](Inno.Adapter.Input.Sdl3.md) | 将平台事件累积为相互隔离的键鼠 backend |
