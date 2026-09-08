namespace Inno.Runtime;

/// <summary>
/// Selects how one runtime session executes its frame jobs.
/// </summary>
public enum RuntimeJobExecutionMode
{
    /// <summary>
    /// Executes jobs deterministically on the session owner thread.
    /// </summary>
    SingleThread,

    /// <summary>
    /// Executes ready jobs on a bounded worker pool owned by the session.
    /// </summary>
    WorkerPool
}
