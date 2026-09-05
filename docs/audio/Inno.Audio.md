# Inno.Audio

[Audio 索引](README.md) · [Runtime](Inno.Audio.Runtime.md) · [Assets](Inno.Audio.Assets.md) · [Wiki 首页](../README.md)

`Inno.Audio` 是后端中立且脚本稳定的音频契约。它引用 Assets、Events、Mathematics、Serialization、Type metadata 与 Scripting API，但不引用 Runtime、Scene、Editor、Platform、MiniAudio 或任何 Native 项目。

## 公开契约

| 分组 | 类型 | 稳定语义 |
| --- | --- | --- |
| 游戏入口 | `Audio`、`AudioExecutionContext`、`IAudioService` | façade 只解析当前严格 LIFO scope；真实状态由显式服务拥有。 |
| 设备边界 | `IAudioDevice`、`AudioDevice`、`AudioClipDescriptor`、`AudioDeviceCompletion` | Host/backend 使用的低层契约；不进入脚本 API。 |
| 句柄 | `AudioClipHandle`、`AudioVoiceHandle`、`AudioBusHandle`、`AudioListenerHandle` | 编码设备 generation；旧设备和已退休对象的句柄安全失败。 |
| 状态 | `AudioCapabilities`、`AudioDeviceState`、`AudioPlaybackState`、`AudioCompletionReason`、`AudioStatistics` | 查询能力、输出状态、播放状态和资源计数。 |
| 播放 | `AudioPlayOptions`、`AudioVoiceParameters`、`AudioSpatialOptions`、`AudioListenerState`、`AudioClipLoadMode`、`AudioDistanceModel` | 2D/基础 3D、scheduled start、loop、priority、route 与 load mode。 |
| ID | `AudioBusId`、`AudioProcessorId`、`AudioParameterId`、`AudioCodecId` | 开放字符串协议；`AudioBusId.master` 永远存在。 |
| Clip | `AudioClipAsset`、`AudioClipMetadata`、`AudioClipMetadataCodec` | Asset 对象与严格 metadata payload 编解码。 |
| Mixer | `AudioMixerAsset`、`AudioMixer`、`AudioMixerBuilder`、`AudioBusDefinition`、`AudioProcessorConfiguration`、`AudioProcessorParameter` | 构建并验证有向无环 Bus graph。 |
| 扩展 | `AudioMixerExtensionAttribute`、`AudioMixerFeatureExtensionAttribute`、`AudioMixerExtension`、`AudioMixerFeature`、`SerializedAudioExtensionState`、`AudioMixerFeatureConfiguration` | Stable ID + 中立 bytes；不持久化 Plugin `Type`、实例或 delegate。 |
| 事件/诊断 | `AudioVoiceCompletedEvent`、`AudioDiagnostic`、`AudioDiagnosticSeverity`、`IAudioDiagnosticSink` | 完成事件进入主线程 dispatcher；诊断由 Host 显式接收。 |

`Audio` 提供 `Play`、`PlayScheduled`、`Stop`、`Pause`、`Resume`、`Seek`、`SetVoiceParameters`、`TryGetVoiceState`、Bus 控制、`PreloadAsync`、`ReleasePreload`，以及 `dspTime`、`capabilities`、`deviceState`、`statistics`。

## Mixer 工作流

```csharp
using InnoEngine.Audio;

var builder = new AudioMixerBuilder();
var music = new AudioBusId("game.audio.bus.music");
builder.AddBus(music, AudioBusId.master, volume: 0.8f);
builder.AddProcessor(
    music,
    new AudioProcessorConfiguration(
        AudioProcessorId.lowPass,
        [new AudioProcessorParameter(AudioParameterId.frequency, 12000f)]));
AudioMixer mixer = builder.Build();
```

Builder 拒绝重复 Bus、缺失 parent、环和无效 processor 参数。标准 processor 包含 LPF、HPF、BPF、notch、peak、low/high shelf 与 delay；协议仍使用开放 ID，Plugin 可组合而不修改中央 enum。

## 生命周期与错误

- `Play` 可立即返回 `Preparing` Voice；准备、解码和 Artifact 定位由 Runtime 完成。
- 自然结束、显式停止、抢占、解码失败和设备丢失都通过 `AudioVoiceCompletedEvent` 区分。
- 无活动 `AudioExecutionContext` 时 façade 抛出 `InvalidOperationException`。
- `AudioDevice` 的 opaque handle 编解码只向后端派生类型开放；Native 类型不得进入 public/protected 签名。

