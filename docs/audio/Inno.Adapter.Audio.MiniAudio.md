# Inno.Adapter.Audio.MiniAudio

Clip 不再只是文件描述。`CreateClip` 创建并保留 native resource-manager data source，使用异步 Decode/Stream；`GetClipState` 无阻塞查询 Preparing/Ready/Failed。每个 Voice 仍有独立播放游标，decoded buffer 由 native resource manager 共享。播放准备错误以 DecodeFailed 完成。DestroyClip/Dispose 在 EngineUninit 前释放所有 data source 和原生分配；Artifact 路径的寿命由上层 cache 租约保证。

[Audio 索引](README.md) · [Runtime](Inno.Audio.Runtime.md) · [Native binding](../native/Inno.Native.MiniAudio.md) · [Build toolchain](../build/Inno.Build.Toolchains.MiniAudio.md)

`Inno.Adapter.Audio.MiniAudio` 是 `IAudioDevice` 的官方后端适配器，也是运行时中唯一允许引用 `Inno.Native.MiniAudio` 的项目。程序集 public/protected API 只暴露后端中立类型。

## 公开 API

| 类型 | 成员 | 语义 |
| --- | --- | --- |
| `MiniAudioDeviceOptions` | `noDevice`、`channels`、`sampleRate`、`listenerCount`、`limits` | 创建一个不可变 backend generation；listener 数为 1–4。 |
| `MiniAudioDevice` | 默认构造、options 构造、`IAudioDevice`、`Dispose` | 包装 `ma_engine`、resource manager、sound/group 与 node graph。 |

设备支持 WAV/FLAC/MP3、Decode/Stream、异步准备、scheduled playback、2D pan、基础 3D distance/cone/doppler、多个 listener、Bus graph 与八种标准 processor。滤波器映射到 native biquad coefficient/node，delay 映射到 native delay node。

`Play` 在分配原生 Voice 前拒绝未初始化的 options 与非有限/负 scheduled time，返回 invalid handle。
`SetVoiceParameters` 对默认零 pitch 返回 false；`SetBusVolume` 对 NaN/Infinity 返回 false，不污染已播放的声音。
其余数值与空间向量由中立配置构造函数验证；静音后端遵循相同接纳规则。no-device 测试实际创建 native Clip，
验证拒绝不会增加 active voice 数且后续合法播放仍能推进。

```csharp
using IAudioDevice device = new MiniAudioDevice(new MiniAudioDeviceOptions
{
    noDevice = true,
    sampleRate = 48000,
    channels = 2
});
```

`noDevice` 不是静默跳过：adapter 主动拉取 PCM frame，使 native graph、DSP clock、scheduled voice 与自然结束在 CI/headless 环境真实推进。普通模式使用 macOS CoreAudio 或 Windows WASAPI 默认输出。

初始化任一步失败都会逆序回滚 native allocation；运行中会在控制线程检查 native output device state，并把无法重新启动的 generation 标记为 Lost，交由 Runtime 候选恢复。Dispose 先停止 Voice，再释放 Clip、processor、Bus、Listener 和 engine。native library 与 binding 必须来自同一固定 miniaudio commit，不混用动态 ABI。

`MiniAudioDeviceOptions.limits` 接收 `AudioDeviceLimits`，创建时验证非 null。Clip、Bus 与 Voice 分配在 native 入口前检查预算；未排空 completion 仍占 Voice 容量，TryDequeueCompletion 后才可重新接纳。容量不足返回 invalid handle，不发起 native operation。
