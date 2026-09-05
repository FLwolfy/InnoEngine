# Inno.Editor.Audio

[Editor 索引](README.md) · [Play Mode](Inno.Editor.PlayMode.md) · [Audio](../audio/README.md) · [Application](Inno.Editor.Application.md)

`Inno.Editor.Audio` 管理 Editor 的 Edit/Play 音频设备 generation、预览与诊断，不实现 Mixer 窗口或波形编辑器。普通 AudioSource/AudioListener 属性继续走通用 Inspection。

## 公开 API

| 类型 | 成员 | 语义 |
| --- | --- | --- |
| `IEditorAudioHost` | `BeginSession`、`EnterExecutionScope`、`Update`、`PlayPreview`、`StopPreview` | Play Mode 与组合根依赖的最小可替换边界。 |
| `EditorAudioHost` | 构造函数与 `IEditorAudioHost` 全部成员 | 默认创建 MiniAudio，失败时建立明确 muted generation 并写入 Editor Log。 |

Edit Session 启动时拥有常驻 generation。进入 Play 时创建独立 generation，并暂停 Edit master Bus；退出 Play 时先释放 Play audio，再释放 Play Scene/Session，最后恢复 Edit Bus。这样 preview Voice、Scene Voice、句柄和 completion event 不会跨 Session 混用。

```csharp
using IDisposable lease = audio.BeginSession(editSession);
using (audio.EnterExecutionScope(editSession))
{
    editSession.Tick(deltaTime);
    audio.Update(editSession, deltaTime);
}
```

`deviceFactory` 是平台组合与 headless 测试的 public 注入边界，不是测试后门。Audio extension reload 通过 Runtime 的 TypeCatalog generation 在 frame-safe update 中原子刷新；候选失败保留 last-good。

