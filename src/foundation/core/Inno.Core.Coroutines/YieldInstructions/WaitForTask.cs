using System;
using System.Threading.Tasks;

namespace Inno.Core.Coroutines;

/// <summary>
/// Waits until a task is completed.
/// </summary>
/// <param name="task">
/// The task used to initialize this instance.
/// </param>
/// <returns>
/// The value produced by this implementation of the contract.
/// </returns>
public sealed class WaitForTask(Task task) : YieldInstruction
{
    /// <summary>
    /// Gets the awaited task.
    /// </summary>
    public Task task { get; } = task ?? throw new ArgumentNullException(nameof(task));

    internal override CoroutineWaitDelegate CreateWaiter(double now, ulong frame) => _ => !task.IsCompleted;
}
