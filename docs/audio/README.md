# Audio API

[Wiki 首页](../README.md) · [Assets](../assets/README.md) · [Scene](../scene/README.md) · [Runtime](../runtime/README.md)

Audio 是引擎基础能力：游戏代码依赖后端中立契约，Host 在组合根选择 MiniAudio，Editor 集成保持可选。对话、音乐编排、录音、VOIP、遮挡、高级 DSP 以及 Scene Component 模型属于 Plugin，不写入 Audio Mechanism。

| 项目 | 职责 |
| --- | --- |
| [Inno.Audio](Inno.Audio.md) | Clip、Voice、Bus、Listener、稳定 ID、Mixer graph、设备/服务契约与脚本 façade。 |
| [Inno.Audio.Runtime](Inno.Audio.Runtime.md) | 调度、缓存、voice stealing、完成事件、原子有界的内容 Provider 接纳、预算统计与扩展 generation。 |
| [Inno.Audio.Assets](Inno.Audio.Assets.md) | WAV、FLAC、MP3 的 metadata 与 `audio-data` Artifact 导入。 |
| [Inno.Adapter.Audio](Inno.Adapter.Audio.md) | Audio backend 选择、创建参数与 factory contract。 |
| [Inno.Adapter.Audio.MiniAudio](Inno.Adapter.Audio.MiniAudio.md) | 唯一 MiniAudio 运行时适配器；原生类型不离开程序集。 |
| [Inno.Editor.Audio](../editor/Inno.Editor.Audio.md) | Edit/Play 独立设备 generation、预览与诊断。 |

```text
Game / Plugin scripts -> Inno.Audio <- Inno.Audio.Runtime
                              ↑                ↑
Inno.Adapter.Audio.MiniAudio   │       Composition supplies
           └─ implements IAudioDevice   content + artifacts + device
           └─ uses Native MiniAudio
```

脚本统一使用逻辑 namespace `InnoEngine.Audio`。`IAudioDevice`、MiniAudio adapter、Artifact lookup 与任何 `Ma*` 类型都不导出到脚本。

中立配置拒绝非有限参数，未初始化播放 options 在接纳前失败；Runtime 缓存复用 Core lifetime 保留
Clip/Artifact 退休阶段，取消 preload 和背压重试不提前丢失资源所有权，详见各项目页。
