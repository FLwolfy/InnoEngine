# Inno.Core.Job

[上一页：Coroutines](Inno.Core.Coroutines.md) · [Core 索引](README.md) · [下一页：Identity](Inno.Core.Identity.md)

Job 模块提供统一 `IJobSystem` 契约、静态业务门面和两个后端：确定性的 `SingleThreadJobSystem` 与固定线程池的 `WorkStealingJobSystem`。所有本帧任务必须位于 `BeginFrame` / `EndFrame` scope 中。

## 初始化与后端选择

```csharp
JobSystemManager.Initialize();
JobSystemManager.SetJobSystem(new WorkStealingJobSystem(new JobSystemOptions
{
    workerCount = 0 // auto: max(1, CPU - 1), clamped to 64
}));

// ... frame loop ...

JobSystemManager.Shutdown();
```

`SetJobSystem` 会 Dispose 原后端。`JobSystemManager.current` 在 Manager 未初始化或未设置后端时抛异常。

## JobSystem 静态门面

| 成员 | 说明 |
| --- | --- |
| `workerCount` | 当前后端 worker 数；单线程实现返回 0。 |
| `Schedule(Action)` | 调度无依赖任务。 |
| `Schedule(Action<object?>, object?, ReadOnlySpan<JobHandle>)` | 调度带 state 与依赖的任务。 |
| `CombineDependencies(span)` | 返回所有依赖完成后结束的同步 handle。 |
| `ParallelFor(length, batchSize, Action<int,int>)` | 按 `[startInclusive, endExclusive)` 分块。 |
| `Complete(handle)` / `CompleteAll(span)` | 阻塞并协助推进，直到任务完成；fault 会重新报告。 |
| `RunOnMainThread(Action)` | 排队到主线程队列，由宿主 drain。 |

```csharp
JobHandle prepare = JobSystem.Schedule(PrepareData);
Span<JobHandle> dependencies = stackalloc[] { prepare };
JobHandle consume = JobSystem.Schedule(
    state => Consume((Buffer)state!),
    buffer,
    dependencies);

JobSystem.Complete(consume);
```

`JobHandle` 是 opaque readonly struct；只公开 `isValid`。Handle 使用 index + version 防止 slot 复用后的陈旧引用被误认为新任务。

## IJobSystem

自定义后端需要实现 `IDisposable` 以及以下全部成员：

- `workerCount`
- `BeginFrame()` / `EndFrame()`
- 两个 `Schedule` overload
- `CombineDependencies`
- `ParallelFor`
- `Complete` / `CompleteAll`
- `EnqueueMainThread` / `DrainMainThreadQueue`

`EndFrame()` 必须保证本 frame 所有任务完成，并聚合任务异常。`DrainMainThreadQueue()` 必须在主线程执行。

## 内置后端

### SingleThreadJobSystem

- 构造时记录主线程。
- `BeginFrame`、Schedule、Complete、Drain 都要求该线程。
- Complete 会在调用线程按依赖顺序执行 ready jobs，便于可重复测试。
- `workerCount == 0`。

### WorkStealingJobSystem

- `new WorkStealingJobSystem()` 使用默认 `JobSystemOptions`。
- `new WorkStealingJobSystem(options)` 创建固定 worker pool。
- 等待 Complete 时调用线程也会尝试执行可用任务，减少空等。
- worker 数 `0` 为自动，负数无效，显式值会 clamp 到 1..64。

## 帧循环

```csharp
IJobSystem jobs = JobSystemManager.current;
jobs.BeginFrame();
try
{
    ScheduleFrameWork();
}
finally
{
    jobs.EndFrame();
    jobs.DrainMainThreadQueue();
}
```

`Shell.Tick` 已执行这套顺序。`ParallelFor` 的 `length` 不得为负，`batchSize` 必须大于 0；length 为 0 时仍返回一个可完成的 handle。

## 注意事项

- Job 闭包/state 会强引用其对象，脚本热重载前必须完成旧 generation 的任务。
- 不要把跨帧失效的 JobHandle 当持久 ID。
- Main-thread callback 的异常会由 Drain 聚合为 `AggregateException`。
- `EndFrame` 即同步边界，不应在它之后仍保留本帧尚未完成的任务。
