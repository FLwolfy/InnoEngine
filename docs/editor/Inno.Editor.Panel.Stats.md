# Inno.Editor.Panel.Stats

[Editor 索引](README.md) · [Logging](Inno.Editor.Panel.Logging.md) · [Application](Inno.Editor.Application.md)

该项目提供最小独立 Stats Panel，不拥有 Scene、Asset 或 Interaction 业务。

`FrameStatisticsSampler` 对瞬时 delta time/FPS 进行时间窗口平滑，Panel 展示稳定的人类可读值，而不是每帧直接显示抖动的倒数。采样仍保留当前 frame time，平均值仅影响诊断显示，不改变 `Time` 或引擎更新。

Panel 通过 `[EditorPanel]` 自动发现；Application 不需要注册。布局、分割线与禁用文本色统一读取 `EditorStyleMetrics` 和 `EditorPalette`。

宿主通过 internal lifecycle bridge 调用 Panel 的 protected `OnAttach`、`OnDraw` 与 `OnDetach`；扩展和脚本不能手工驱动生命周期。当前 generation 的 Draw 失败只 quarantine Stats Panel，不会中断其他 Editor 内容绘制。

该项目没有 Scripting API：脚本不应依赖 Editor 诊断表现。未来新增 profiler timeline 时应创建新的独立 Panel feature，而不是把采样、菜单和渲染塞回 Application。
