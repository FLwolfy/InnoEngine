# Inno.Animation

[Animation 索引](README.md) · [Runtime](Inno.Animation.Runtime.md) · [Wiki 首页](../README.md)

`Inno.Animation` 不引用 Scene、Rendering、Audio、Editor 或具体 backend。它定义可由 2D、3D、UI、Camera 或音频参数共同消费的值采样协议。

## 公开 API

| API | 稳定语义 |
| --- | --- |
| `AnimationClipAsset` | 带 Stable Type ID 的 duration、tracks 与 marker 集合。 |
| `AnimationBindingId` / `IAnimationBindingSink` | 以开放稳定 ID 接收最终 blended sample，不反射写属性。 |
| `AnimationTarget` | 使用 Identity 的原 runtime slot 定位目标；不同目标独立混合；纯采样显式选择 samplingOnly。 |
| `AnimationBindingProviderAttribute` / `AnimationBindingProvider` | 按稳定 binding ID/value kind 发现控制线程适配，不内建 Transform 或 Sprite。 |
| `AnimationValue` / `AnimationValueKind` | Scalar、Vector2/3/4 与 normalized Quaternion。 |
| `AnimationTrack` / `AnimationKeyframe` | Step/Linear 时间采样。 |
| `AnimationPlayOptions` | scaled/unscaled clock、speed、loop、layer、weight。 |
| `AnimationPlaybackHandle` | Runtime generation 校验的 opaque handle。 |
| `IAnimationService` / `Animation` | 显式基础设施边界与脚本 façade。 |
| `AnimationMarkerEvent` | 主线程 EventDispatcher 上的 stable event ID 与中立 payload。 |

```csharp
using InnoEngine.Animation;

AnimationPlaybackHandle playback = Animation.Play(
    clip,
    new AnimationTarget(targetObject.identity),
    new AnimationPlayOptions
    {
        speed = 1f,
        weight = 1f,
        layer = 0,
        loop = true,
        clock = AnimationClock.Scaled
    });
```

Clip 在 import/play 前严格验证。自然完成或显式 Stop 后句柄立即 stale，不能控制后来复用同一 slot 的播放。

[下一页：Inno.Animation.Runtime](Inno.Animation.Runtime.md)
