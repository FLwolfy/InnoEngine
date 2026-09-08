using System;
using System.Collections.Generic;

namespace Inno.Core.Jobs.Internal;

internal sealed class JobRecord
{
    internal int version = 1;
    internal bool inUse;
    internal JobExecutionState executionState;
    internal int remainingDependencies;
    internal Action<object?>? callback;
    internal object? state;
    internal Exception? exception;
    internal List<int>? dependents;

    internal void ResetForAllocation(Action<object?> callback, object? state)
    {
        inUse = true;
        executionState = JobExecutionState.Created;
        remainingDependencies = 0;
        this.callback = callback;
        this.state = state;
        exception = null;
        dependents ??= [];
        dependents.Clear();
    }

    internal void ResetForReuse()
    {
        inUse = false;
        executionState = JobExecutionState.None;
        remainingDependencies = 0;
        callback = null;
        state = null;
        exception = null;
        dependents?.Clear();
        checked
        {
            version++;
            if (version <= 0)
            {
                version = 1;
            }
        }
    }
}
