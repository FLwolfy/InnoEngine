# Inno.Audio

## Provider 退休约束

`AudioContentProvider.Dispose()` 的派生 hook 如果返回 `RetirementPendingException`，实例保持未退休，
所属 generation 必须保留它并在 owner thread 重试。普通异常表示失败而不是 Pending；由 Runtime 的 TypeRegistry
汇总并关闭共享 generation gate。禁止在 hook 未完成时提前标记 disposed，也不允许运行托管实时 DSP callback。

[Audio 索引](README.md) · [Runtime](Inno.Audio.Runtime.md) · [Assets](Inno.Audio.Assets.md) · [Wiki 首页](../README.md)

`Inno.Audio` 是后端中立且脚本稳定的音频契约。它引用 Assets、Events、Mathematics、Serialization、Type metadata 与 Scripting API，但不引用 Runtime、Scene、Editor、Platform、MiniAudio 或任何 Native 项目。

## 公开契约

| 分组 | 类型 | 稳定语义 |
| --- | --- | --- |
| 游戏入口 | `Audio`、`AudioExecutionContext`、`IAudioService` | façade 只解析当前严格 LIFO scope；真实状态由显式服务拥有。 |
| 设备边界 | `IAudioDevice`、`AudioDevice`、`AudioClipDescriptor`、`AudioDeviceCompletion`、`AudioClipState`、`AudioDeviceLimits` | Host/backend 使用的低层契约，GetClipState 明确准备进度；不进入脚本 API。 |
| 句柄 | `AudioClipHandle`、`AudioDeviceVoiceHandle`、`AudioBusHandle`、`AudioListenerHandle` | 编码设备 generation；旧设备和已退休对象的句柄安全失败。 |
| 游戏 Voice | `AudioVoiceHandle` / `AudioVoiceAllocator` | 服务级请求身份与设备 Voice 分离；准备中即可取得 handle，Runtime 不再继承 AudioDevice。 |
| 状态 | `AudioCapabilities`、`AudioDeviceState`、`AudioPlaybackState`、`AudioCompletionReason`、`AudioStatistics` | 查询能力、输出状态、播放状态和资源计数。 |
| 播放 | `AudioPlayOptions`、`AudioVoiceParameters`、`AudioSpatialOptions`、`AudioListenerState`、`AudioClipLoadMode`、`AudioDistanceModel` | 2D/基础 3D、scheduled start、loop、priority、route 与 load mode。 |
| ID | `AudioBusId`、`AudioProcessorId`、`AudioParameterId`、`AudioCodecId` | 开放字符串协议；`AudioBusId.master` 永远存在。 |
| Clip | `AudioClipAsset`、`AudioClipMetadata`、`AudioClipMetadataCodec` | Asset 对象与严格 metadata payload 编解码。 |
| Mixer | `AudioMixerAsset`、`AudioMixer`、`AudioMixerBuilder`、`AudioBusDefinition`、`AudioProcessorConfiguration`、`AudioProcessorParameter` | 构建并验证有向无环 Bus graph。 |
| 扩展 | `AudioMixerExtensionAttribute`、`AudioMixerFeatureExtensionAttribute`、`AudioMixerExtension`、`AudioMixerFeature`、`SerializedAudioExtensionState`、`AudioMixerFeatureConfiguration` | Stable ID + 中立 bytes；不持久化 Plugin `Type`、实例或 delegate。 |
| 内容输入 | `AudioContentProviderExtensionAttribute`、`AudioContentProvider`、`AudioContentProviderContext`、`AudioEmitterSnapshot`、`AudioListenerSnapshot` | Provider 单次控制线程贡献；总容量限制、整批接纳、返回后撤销，不依赖 Scene 模型。 |
| 事件/诊断 | `AudioVoiceCompletedEvent`；Core `DiagnosticSeverity` / `IDiagnosticReporter` | 完成事件进入主线程 dispatcher；诊断只有 Core 一套 producer/hub/sink。 |

`Audio` 提供 `Play`、`PlayScheduled`、`Stop`、`Pause`、`Resume`、`Seek`、`SetVoiceParameters`、`TryGetVoiceState`、Bus 控制、`PreloadAsync`、`ReleasePreload`，以及 `dspTime`、`capabilities`、`deviceState`、`statistics`。

## 参数与接纳边界

播放配置、Voice 参数与空间配置的构造函数拒绝 NaN/Infinity；空间向量的每个轴都检查有限值，
`AudioClipLoadMode` 与 `AudioDistanceModel` 必须是已定义值。原有范围规则仍生效，有限零向量没有新增方向归一化要求。
显式播放应使用 `AudioPlayOptions.defaultValue` 或 `new AudioPlayOptions(...)`，不能使用绕过构造函数的
`default(AudioPlayOptions)`；后者没有有效 Bus，服务在抢占 Voice 或取得 Artifact 前拒绝。
`default(AudioVoiceParameters)` 的 pitch 为零，`SetVoiceParameters` 返回 false，不覆盖当前 Voice 参数。
这些验证属于中立契约，不依赖 MiniAudio；直接设备调用同样拒绝无效默认配置、非有限/负 schedule 与非有限 Bus volume。

## Content Provider 工作流

Host 持有 `ContentReadScope`，Runtime 为每个 Provider 创建独立 `AudioContentProviderContext`。
Provider 只在 `Submit(context)` 内解析 Identity-backed 内容并调用 `context.Submit(snapshot)`；
不得保存 context、解析到的对象或在后台任务中继续提交，也不得释放借用的 content scope。

- `content`、`deltaTime` 提供本次内容与时间；`capacity` 是本次剩余的 emitter + listener 共同预算。
- `Submit` 拒绝 default snapshot、未初始化的 emitter options、重复 identity 与超限。一次提交失败会使整批无效；吞掉异常也不能让此前部分提交生效。
- `emitters` / `listeners` 返回冻结副本；它们是当前 invocation 的快照，不是跨 generation 的持久状态。
- `Dispose` 清掉暂存引用并撤销提交/读取；Runtime 在成功或失败返回后都执行它，不释放上层拥有的 content scope。
- 所有收集操作限定于创建 context 的线程。直接构造 context 的工具/测试必须自行 Dispose。
- 跨 Provider 重复由 Runtime 在整批接纳前验证。较早成功的 Provider 保留；失败 Provider 不占用后续 Provider 的 identity 或预算。

这些扩展仍通过本项目单一 `Properties/ScriptingApi.cs` 导出，不增加 Runtime 中央类型名单或托管音频 callback。

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

`AudioDeviceLimits(clips = 16384, voices = 65536, buses = 4096)` 是 Host 的中立正容量配置，不导出到游戏脚本。voices 包含活动 Voice 和尚未交付的 completion；这层容量不是 AudioRuntime 的 priority/stealing 策略。设备接纳失败返回 invalid handle，调用者必须检查；不分配部分 native 对象，也不丢掉完成通知来接纳新播放。
