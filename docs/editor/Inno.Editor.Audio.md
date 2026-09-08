# Inno.Editor.Audio

[Editor 索引](README.md) · [Play Mode](Inno.Editor.PlayMode.md) · [Audio](../audio/README.md) · [Application](Inno.Editor.Application.md)

`Inno.Editor.Audio` 管理 Editor 的 Edit/Play 音频设备 generation、预览与诊断，不实现 Mixer 窗口、波形编辑器或 Scene 音频组件。

## 公开 API

| 类型 | 成员 | 语义 |
| --- | --- | --- |
| `IEditorAudioHost` | `CreateRuntimeSubsystemFactory`、`EnterExecutionScope`、`PlayPreview`、`StopPreview` | Play Mode 与组合根依赖的最小可替换边界。 |
| `EditorAudioHost` | 构造函数与 `IEditorAudioHost` 全部成员 | 默认创建 MiniAudio，失败时建立明确 muted generation 并写入 Editor Log。 |

Edit Session 启动时拥有常驻 generation。进入 Play 时创建独立 generation，并暂停 Edit master Bus；退出 Play 时先释放 Play audio，再释放 Play Scene/Session，最后恢复 Edit Bus。这样 preview Voice、游戏 Voice、句柄和 completion event 不会跨 Session 混用。

```csharp
var options = new RuntimeSessionOptions
{
    subsystemFactories = [audio.CreateRuntimeSubsystemFactory()]
};

using RuntimeSession session = engine.CreateSession(options);
session.Tick(deltaTime);
```

Audio 的 scope、update 与逆序释放全部由 Session Subsystem Pipeline 管理；`EnterExecutionScope` 只用于 Editor 帧中不属于 Session tick 的预览表现代码。`deviceFactory` 是平台组合与 headless 测试的 public 注入边界，不是测试后门。Audio extension reload 通过 Runtime 的 TypeCatalog generation 在 frame-safe update 中原子刷新；候选失败保留 last-good。
