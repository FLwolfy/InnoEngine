namespace Inno.Core.Jobs;

/// <summary>
/// Selects the execution strategy owned by a job scheduler.
/// </summary>
public enum JobExecutionMode
{
    /// <summary>
    /// Executes jobs deterministically on the scheduler owner thread.
    /// </summary>
    SingleThread,

    /// <summary>
    /// Executes ready jobs on a bounded work-stealing worker pool.
    /// </summary>
    WorkerPool
}
