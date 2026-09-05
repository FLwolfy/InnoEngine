# Inno.Audio.Scene

[Audio 索引](README.md) · [Runtime](Inno.Audio.Runtime.md) · [Scene](../scene/Inno.Scene.md) · [Editor Audio](../editor/Inno.Editor.Audio.md)

`Inno.Audio.Scene` 是官方但可选的 Scene 集成层。Core/Runtime 不引用 Scene；组合根通过 `SceneAudioContent.Capture` 把当前 SceneWorld 交给自动发现的 provider。

## 公开 API

| 类型 | 关键成员 | 语义 |
| --- | --- | --- |
| `AudioSource : GameBehavior` | `clip`、`playOnAwake`、`Play/Stop`、loop/volume/pitch/pan/priority/bus/loadMode 与 3D 参数 | Scene-owned emitter；变更通过 immutable snapshot 增量同步。 |
| `AudioListener : GameBehavior` | `enabled`、`priority` | 官方 listener 组件。 |
| `SceneAudioContent` | 构造函数、`Capture()` | 把一个隔离 SceneWorld 的 loaded scenes 转成 update scope。 |

`AudioSource.Play()` 推进 playback revision，因此当前 clip 仍相同时也会在下一个同步点重新播放。删除、禁用、停止请求或 Scene unload 会让对应 Voice 收敛。Clip Artifact 热重载后，新 Voice 使用新 Artifact，已经播放的 Voice 保留旧不可变 Artifact。

Runtime 默认选择 priority 最高的 active listener；同 priority 以 persistent ID 稳定排序并发布歧义诊断。底层设备仍支持多个 listener，未来 split-screen Plugin 无需改变 Core。

脚本示例：

```csharp
using InnoEngine.Audio;

public sealed class BirdSounds
{
    public AudioSource? source { get; set; }

    public void Flap() => source?.Play();
}
```

`AudioSource` 是 sealed 官方组件；玩法脚本把它作为同一 GameObject 上的组件引用并调用 `Play()`，不通过派生建立第二套 emitter 生命周期。
