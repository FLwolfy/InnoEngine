# Audio API

[Wiki 首页](../README.md) · [Assets](../assets/README.md) · [Scene](../scene/README.md) · [Runtime](../runtime/README.md)

Audio 是引擎基础能力：游戏代码依赖后端中立契约，Host 在组合根选择 MiniAudio，Scene 与 Editor 集成保持可选。对话、音乐编排、录音、VOIP、遮挡和高级 DSP 属于 Plugin，不写入 Audio Core。

| 项目 | 职责 |
| --- | --- |
| [Inno.Audio](Inno.Audio.md) | Clip、Voice、Bus、Listener、稳定 ID、Mixer graph、设备/服务契约与脚本 façade。 |
| [Inno.Audio.Runtime](Inno.Audio.Runtime.md) | 调度、缓存、voice stealing、完成事件、内容 Provider 与扩展 generation。 |
| [Inno.Audio.Assets](Inno.Audio.Assets.md) | WAV、FLAC、MP3 的 metadata 与 `audio-data` Artifact 导入。 |
| [Inno.Audio.MiniAudio](Inno.Audio.MiniAudio.md) | 唯一 MiniAudio 运行时适配器；原生类型不离开程序集。 |
| [Inno.Audio.Scene](Inno.Audio.Scene.md) | 可选 `AudioSource`、`AudioListener` 与 Scene snapshot provider。 |
| [Inno.Editor.Audio](../editor/Inno.Editor.Audio.md) | Edit/Play 独立设备 generation、预览与诊断。 |

```text
Game / Plugin scripts -> Inno.Audio
                            ↑
Scene integration -> Inno.Audio.Runtime -> Inno.Audio.MiniAudio -> Native MiniAudio
                            ↑
                 immutable Asset artifacts
```

脚本统一使用逻辑 namespace `InnoEngine.Audio`。`IAudioDevice`、MiniAudio adapter、Artifact lookup 与任何 `Ma*` 类型都不导出到脚本。

