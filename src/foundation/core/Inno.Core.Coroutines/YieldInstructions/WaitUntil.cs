using System;

namespace Inno.Core.Coroutines;

/// <summary>
/// Waits until the predicate returns <see langword="true"/>.
/// </summary>
/// <param name="predicate">
/// The predicate used to initialize this instance.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
public sealed class WaitUntil(Func<bool> predicate) : YieldInstruction
{
    /// <summary>
    /// Gets the predicate evaluated each tick.
    /// </summary>
    public Func<bool> predicate { get; } = predicate ?? throw new ArgumentNullException(nameof(predicate));

    internal override CoroutineWaitDelegate CreateWaiter(double now, ulong frame) => _ => !predicate.Invoke();
}
