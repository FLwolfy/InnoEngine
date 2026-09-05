# Inno.Audio.MiniAudio

[Audio 索引](README.md) · [Runtime](Inno.Audio.Runtime.md) · [Native binding](../native/Inno.Native.MiniAudio.md) · [Build toolchain](../build/Inno.Build.Toolchains.MiniAudio.md)

`Inno.Audio.MiniAudio` 是 `IAudioDevice` 的官方后端适配器，也是运行时中唯一允许引用 `Inno.Native.MiniAudio` 的项目。程序集 public/protected API 只暴露后端中立类型。

## 公开 API

| 类型 | 成员 | 语义 |
| --- | --- | --- |
| `MiniAudioDeviceOptions` | `noDevice`、`channels`、`sampleRate`、`listenerCount` | 创建一个不可变 backend generation；listener 数为 1–4。 |
| `MiniAudioDevice` | 默认构造、options 构造、`IAudioDevice`、`Dispose` | 包装 `ma_engine`、resource manager、sound/group 与 node graph。 |

设备支持 WAV/FLAC/MP3、Decode/Stream、异步准备、scheduled playback、2D pan、基础 3D distance/cone/doppler、多个 listener、Bus graph 与八种标准 processor。滤波器映射到 native biquad coefficient/node，delay 映射到 native delay node。

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
