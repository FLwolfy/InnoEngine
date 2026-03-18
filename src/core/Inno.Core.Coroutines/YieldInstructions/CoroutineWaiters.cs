using System;
using System.Threading.Tasks;

namespace Inno.Core.Coroutines;

internal interface ICoroutineWaiter
{
    bool KeepWaiting(CoroutineScheduler scheduler);
}

internal sealed class NextFrameWaiter(ulong targetFrame) : ICoroutineWaiter
{
    public bool KeepWaiting(CoroutineScheduler scheduler) => scheduler.frame < targetFrame;
}

internal sealed class WaitForSecondsWaiter(double targetTime) : ICoroutineWaiter
{
    public bool KeepWaiting(CoroutineScheduler scheduler) => scheduler.now < targetTime;
}

internal sealed class WaitUntilWaiter(Func<bool> predicate) : ICoroutineWaiter
{
    public bool KeepWaiting(CoroutineScheduler scheduler) => !predicate.Invoke();
}

internal sealed class WaitWhileWaiter(Func<bool> predicate) : ICoroutineWaiter
{
    public bool KeepWaiting(CoroutineScheduler scheduler) => predicate.Invoke();
}

internal sealed class WaitForFramesWaiter(ulong targetFrame) : ICoroutineWaiter
{
    public bool KeepWaiting(CoroutineScheduler scheduler) => scheduler.frame < targetFrame;
}

internal sealed class WaitForTaskWaiter(Task task) : ICoroutineWaiter
{
    public bool KeepWaiting(CoroutineScheduler scheduler) => !task.IsCompleted;
}

internal sealed class WaitForCoroutineWaiter(CoroutineHandle handle) : ICoroutineWaiter
{
    public bool KeepWaiting(CoroutineScheduler scheduler) => scheduler.Contains(handle);
}
