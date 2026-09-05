# Inno.Audio.Runtime

[Audio 索引](README.md) · [Core contract](Inno.Audio.md) · [Scene](Inno.Audio.Scene.md) · [MiniAudio](Inno.Audio.MiniAudio.md)

`Inno.Audio.Runtime` 实现控制线程上的播放编排，不认识 Scene、Editor 或具体后端。`AudioRuntimeLayer` 同时实现 `IAudioService`，并拥有一个 `IAudioDevice` generation。

## 公开 API

| 类型 | 作用 |
| --- | --- |
| `AudioRuntimeLayer` | 播放/预加载、Mixer 安装、设备替换/恢复、Provider 同步、每帧安全点更新与释放。 |
| `AudioRuntimeOptions` | `maxVoices`、decoded cache budget、automatic stream threshold 与 device recovery interval。 |
| `AudioProjectSettings` | 可部署的 default mixer、master volume 与 Runtime budgets。 |
| `MutedAudioDevice` | 无声卡或测试环境下明确的静音设备；时钟、状态和完成仍推进。 |
| `AudioContentId`、`AudioContentReference`、`AudioContentScope` | Host 传入的一帧不可变内容边界。 |
| `AudioEmitterSnapshot`、`AudioListenerSnapshot` | 不含 Scene 类型的 emitter/listener 状态。 |
| `AudioContentProviderExtensionAttribute`、`AudioContentProvider`、`AudioContentProviderContext` | TypeCatalog 自动发现的控制线程扩展协议。 |

## 更新与 generation

`Update(deltaTime)` 是唯一安全点：刷新 TypeCatalog provider generation、构建候选、同步 emitter/listener、推进 backend、收集完成并派发事件。候选失败保留 last-good provider generation；旧 provider 在退休时统一 `Dispose`。

Voice stealing 在 `maxVoices` 达到上限时按 priority、创建序列和稳定 handle 确定性选择。Clip cache 按 Asset persistent ID、content generation 和 load mode 分键；预加载引用与 live voice 引用独立，旧不可变 Artifact 会保留到使用它的 Voice 结束。Automatic 会在 encoded threshold 或估算 decoded footprint 超过预算时选择 Stream；显式 Decode 超过预算则明确失败。

```csharp
using var audio = new AudioRuntimeLayer(
    host.types,
    device,
    session.assets,
    session.events,
    diagnostics,
    new AudioRuntimeOptions { maxVoices = 64 },
    contentScopeProvider);

using (audio.EnterExecutionScope())
{
    session.Tick(deltaTime);
    audio.Update(deltaTime);
}
```

MiniAudio 原生线程不执行 provider、反射、Asset IO、托管日志、锁或托管 DSP callback。扩展只在控制线程提交中立 snapshot/graph，设备只消费已构建状态。

## 失败语义

- 设备初始化失败由组合根显式建立 `MutedAudioDevice` 并发布诊断，不伪装为正常输出。
- `ApplyMixer` 的候选无效时返回 `false` 并保留当前 graph。
- `ReplaceDevice` 使旧 generation 句柄失效，并在候选设备上先重建 last-good Mixer graph；设备丢失的 Voice 以明确 completion reason 收敛。
- Host 提供 recovery factory 时，Runtime 按 `deviceRecoveryIntervalSeconds` 在 `Update` 安全点重试；候选初始化或 graph 构建失败不会替换当前 generation，成功后保留 Bus volume/mute/pause 控制状态。
- Dispose 停止 Voice、释放 Clip/Bus/Listener、provider generation 和设备，重复释放安全。
