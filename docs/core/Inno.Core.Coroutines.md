# Inno.Core.Coroutines

[上一页：Events](Inno.Core.Events.md) · [Core 索引](README.md) · [下一页：Job](Inno.Core.Job.md)

`CoroutineScheduler` 执行 `IEnumerator` 协程，支持嵌套 enumerator、帧/时间/Task/条件等待以及 owner 级批量停止。普通 Start/Stop 请求通过线程安全命令队列进入 Scheduler，实际状态变化在 `Tick` 安全点应用；owner 级停止会取得 Tick gate，并在返回前立即移除匹配状态。

## CoroutineScheduler

| API | 说明 |
| --- | --- |
| `StartCoroutine(IEnumerator)` | 启动无 owner 协程，返回 handle。 |
| `StartCoroutine(object? owner, IEnumerator)` | 用引用身份保存 owner，便于批量停止。 |
| `StopCoroutine(CoroutineHandle)` | 请求停止；handle 不属于当前 scheduler 或已失效时返回 `false`。 |
| `StopAllCoroutines(object owner)` | 同步停止与该 owner `ReferenceEquals` 的全部协程；返回时 handle 已失效，Scheduler 不再持有 owner/enumerator。 |
| `StopAllCoroutines()` | 请求停止全部协程。 |
| `Tick(float deltaTime)` | 推进一帧；负 delta 当作 0。 |
| `Dispose()` | 清空全部 active/pending 状态并永久关闭。 |

`CoroutineHandle.isValid` 表示 handle 仍指向一个存活 scheduler 中的 live coroutine。Handle 是 opaque readonly struct，不公开内部 ID。

```csharp
IEnumerator Fade()
{
    yield return new WaitForFrames(1);
    yield return new WaitForSeconds(0.25f);
    yield return new WaitUntil(() => resourceReady);
    yield return LoadAsync();       // Nested IEnumerator is supported.
    yield return new WaitForTask(task);
}

CoroutineHandle handle = scheduler.StartCoroutine(owner: this, Fade());
scheduler.Tick(deltaTime);
```

## YieldInstruction

`YieldInstruction` 是公开抽象基类，但 waiter 创建由 Scheduler 内部控制。内置指令：

| 类型 | 公开属性 | 恢复条件 |
| --- | --- | --- |
| `WaitForFrames(int)` | `frames` | 经过指定帧数；`<= 0` 仍至少等到下一帧。 |
| `WaitForSeconds(float)` | `seconds` | Scheduler 累计时间达到目标；`<= 0` 等下一帧。 |
| `WaitForTask(Task)` | `task` | `Task.IsCompleted`；构造时不接受 null。 |
| `WaitUntil(Func<bool>)` | `predicate` | predicate 返回 `true`。 |
| `WaitWhile(Func<bool>)` | `predicate` | predicate 返回 `false`。 |

协程可 `yield return null` 表示等待下一次 Tick，也可 yield 另一个 `IEnumerator` 形成嵌套栈。

## Owner 与热重载

Owner 和 IEnumerator 都会强引用脚本实例及其程序集。脚本代际退出时，应在迁移前调用 `StopAllCoroutines(oldInstance)`；该调用返回时 Scheduler 已释放匹配的 owner/enumerator，因此不依赖下一帧 Tick 才允许旧 collectible ALC 回收。不要用值相等但引用不同的对象去停止 owner 协程。

## 线程与异常

- Start/Stop API 可从其他线程提交；`Tick` 自身由 gate 串行化。
- Scheduler 设计为由主循环每帧推进，不会自行创建更新线程。
- Enumerator/predicate 抛出的异常会从 `Tick` 传播；调用层应决定日志与故障策略。
- Dispose 后 `StartCoroutine`、owner stop 和 `Tick` 会抛 `ObjectDisposedException`；无参 stop-all 安全返回。
