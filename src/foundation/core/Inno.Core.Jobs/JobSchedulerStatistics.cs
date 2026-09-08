namespace Inno.Core.Jobs;

/// <summary>
/// Reports lifetime admission and bounded owner-thread queue measurements without retaining job delegates.
/// </summary>
public readonly record struct JobSchedulerStatistics
{
    /// <summary>
    /// Gets jobs admitted in the current frame, including completed jobs awaiting EndFrame.
    /// </summary>
    public int frameJobs { get; init; }
    /// <summary>
    /// Gets the largest admitted frame since scheduler creation.
    /// </summary>
    public int peakFrameJobs { get; init; }
    /// <summary>
    /// Gets rejected job admissions caused by the finite frame capacity.
    /// </summary>
    public long rejectedJobs { get; init; }
    /// <summary>
    /// Gets queued or retirement-pending owner-thread callbacks.
    /// </summary>
    public int mainThreadPending { get; init; }
    /// <summary>
    /// Gets the largest simultaneous owner-thread queue occupancy.
    /// </summary>
    public int mainThreadPeak { get; init; }
    /// <summary>
    /// Gets callbacks rejected because the queue was full or closed.
    /// </summary>
    public long mainThreadRejected { get; init; }
    /// <summary>
    /// Gets accepted callbacks canceled before execution during shutdown.
    /// </summary>
    public long mainThreadCanceled { get; init; }
}
