using System;

namespace Inno.Core.Coroutines;

/// <summary>
/// Waits until the predicate returns <see langword="true"/>.
/// </summary>
public sealed class WaitUntil(Func<bool> predicate) : YieldInstruction
{
    /// <summary>
    /// Gets the predicate evaluated each tick.
    /// </summary>
    public Func<bool> predicate { get; } = predicate ?? throw new ArgumentNullException(nameof(predicate));

    internal override CoroutineWaitDelegate CreateWaiter(double now, ulong frame) => _ => !predicate.Invoke();
}
