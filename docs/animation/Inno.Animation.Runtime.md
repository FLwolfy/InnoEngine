# Inno.Animation.Runtime

[Animation 索引](README.md) · [Contract](Inno.Animation.md) · [Assets](Inno.Animation.Assets.md)

`AnimationRuntime : RuntimeSubsystem, IAnimationService` 为一个 Session 拥有全部 playback。每帧按选定 scaled/unscaled clock 前进，按 `(AnimationTarget, AnimationBindingId)` 分组，在最高有效 layer 内按 weight 混合。不同目标不会因为 binding 相同而串值；Quaternion 输出会归一化。

`AnimationRuntimeFactory` 声明对 Scene subsystem 的依赖，并在 Session update 阶段推进 runtime；Begin/End frame 管理 `AnimationExecutionContext`。Marker 通过 Session `EventDispatcher` 发布，原生实时音频路径不执行这些 Provider；Animation binding Provider 本身在控制线程执行。

构造必须注入 `IAnimationBindingSink`。默认发行使用 `AnimationBindingRuntime(types, diagnostics)`，按 `AnimationBindingProviderAttribute(id, bindingId, kind)` 发现 Provider，构建 TypeRegistry 候选，拒绝重复 ID/协议，退休时释放旧 Provider。`Apply(samples)` 在 owner thread 解析目标并应用值；缺失/不兼容/异常通过 Core reporter 发布，下一批成功时清除。Dispose 注销 registry，不拥有传入 reporter。

`Animation.Play(clip, target)` / `Play(clip, target, options)` 要求明确目标。`new AnimationTarget(object.identity)` 不持有对象；对象退休后不会自动控制新 runtime slot。纯采样/marker 使用 `AnimationTarget.samplingOnly`，不再以缺少 sink 隐式丢弃输出。播放捕获独立 track/keyframe/marker 副本；后续修改创作资产不影响已播放快照。

Provider 只实现中立绑定机制。Transform、Sprite、骨骼、Timeline 等具体绑定不内建；可通过 Provider 扩展，而不修改 Runtime 中央 switch。

[下一页：Inno.Animation.Assets](Inno.Animation.Assets.md)

## 播放预算

`AnimationRuntime(events, bindings, maxPlaybacks = 16384)` 要求正容量；Play 超限在捕获/分配前明确拒绝。创作数据冻结失败不消耗 slot。Stop/自然完成释放 slot，重用时增加 generation，旧句柄不能控制新播放。`playbackCount` 与 `rejectedPlaybacks` 是只读 Host 统计。没有增加具体 Animation binding 世界观或 gameplay Plugin。
