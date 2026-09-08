# Inno.Adapter.Audio

[Audio 索引](README.md) · [中立 Audio](Inno.Audio.md) · [MiniAudio implementation](Inno.Adapter.Audio.MiniAudio.md)

该项目定义 Audio Adapter family，不包含 MiniAudio binding 或 `Ma*` 类型。

## 公开 API

- `AudioBackend`：Composition 启动时使用的设备 implementation 选择。
- `AudioBackendOptions`：只描述后端中立的设备启动策略；`noDevice` 用于无硬件运行和测试。
- `IAudioBackendFactory.CreateDevice`：返回 caller-owned `IAudioDevice`。

```csharp
IAudioDevice device = catalog.audio.CreateDevice(
    selection.audio,
    new AudioBackendOptions { noDevice = true });
```

设备丢失、muted state、voice/mixer 语义仍由 `Inno.Audio` / `Inno.Audio.Runtime` 处理。Factory 不包装 gameplay AudioSource、BGM 或 Dialogue 模型。
