namespace Inno.Core.Coroutines;

/// <summary>
/// Base type for custom coroutine wait instructions.
/// </summary>
public abstract class YieldInstruction
{
    internal abstract ICoroutineWaiter CreateWaiter(double now, ulong frame);
}
