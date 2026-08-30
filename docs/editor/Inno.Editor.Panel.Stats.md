# Inno.Editor.Panel.Stats

[Editor 索引](README.md) · [Logging](Inno.Editor.Panel.Logging.md) · [Application](Inno.Editor.Application.md)

该项目提供最小独立 Stats Panel，不拥有 Scene、Asset、Rendering 或 Interaction 业务。

`FrameStatisticsSampler` 对瞬时 delta time/FPS 进行时间窗口平滑，Panel 展示稳定的人类可读值，而不是每帧直接显示抖动的倒数。采样仍保留当前 frame time，平均值仅影响诊断显示，不改变 `Time` 或引擎更新。

Panel 通过 `[EditorPanel]` 自动发现；Application 不需要注册。除时间/FPS 外，它只读取
`EditorContext.statistics.GetSnapshot()`，按贡献的 stable group ID 和 order 绘制任意 feature
统计。Scene/Game 的 viewport 信息由通用 `EditorRenderingModule` 发布，2D Plugin、未来 3D Plugin
或其他工具无需让 Stats Panel 增加引用和类型分支。

```csharp
context.statistics.Publish(new EditorStatistic(
    new EditorStatisticId("sample.streaming.pending"),
    new EditorStatisticGroupId("sample.streaming"),
    "Streaming",
    "Pending Requests",
    pendingCount.ToString()));
```

同一帧重复发布相同 ID 时最后值替换之前值。交换保留一个 completed-frame handoff，使贡献者
位于 Stats 前后任意 Panel draw order 都能被看到；停止发布后不会形成长期 stale 数据。

宿主通过 internal lifecycle bridge 调用 Panel 的 protected `OnAttach`、`OnDraw` 与 `OnDetach`；扩展和脚本不能手工驱动生命周期。当前 generation 的 Draw 失败只 quarantine Stats Panel，不会中断其他 Editor 内容绘制。

该 Panel 项目没有 Scripting API；发布协议由 `Inno.Editor.Core` 通过逻辑命名空间
`InnoEditor.Core` 提供。未来新增 profiler timeline 时应创建新的独立 Panel feature，而不是把
采样历史、菜单和渲染塞回 Application。
