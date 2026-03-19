namespace Inno.Core.Coroutines;

internal delegate bool CoroutineWaitDelegate(CoroutineScheduler scheduler);

/// <summary>
/// Base type for custom coroutine wait instructions.
/// </summary>
public abstract class YieldInstruction
{
    internal abstract CoroutineWaitDelegate CreateWaiter(double now, ulong frame);
}
