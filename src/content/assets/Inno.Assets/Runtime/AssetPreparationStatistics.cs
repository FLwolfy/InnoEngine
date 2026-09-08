namespace Inno.Assets;

/// <summary>
/// Reports owner-thread cold-load admission and unique encoded IO reservations without retaining assets or tasks.
/// </summary>
public readonly record struct AssetPreparationStatistics
{
    internal AssetPreparationStatistics(int pendingRequests, int peakPendingRequests, long rejectedRequests,
        int pendingPayloads, long payloadReadsStarted, long sharedPayloadReads, long reservedBytes, long peakReservedBytes)
    {
        this.pendingRequests = pendingRequests;
        this.peakPendingRequests = peakPendingRequests;
        this.rejectedRequests = rejectedRequests;
        this.pendingPayloads = pendingPayloads;
        this.payloadReadsStarted = payloadReadsStarted;
        this.sharedPayloadReads = sharedPayloadReads;
        this.reservedBytes = reservedBytes;
        this.peakReservedBytes = peakReservedBytes;
    }

    /// <summary>
    /// Gets requests still awaiting owner-thread completion, including canceled callers not yet drained.
    /// </summary>
    public int pendingRequests { get; }

    /// <summary>
    /// Gets the largest simultaneous admitted request count during this database lifetime.
    /// </summary>
    public int peakPendingRequests { get; }

    /// <summary>
    /// Gets requests rejected by the pending-count or encoded-byte budget.
    /// </summary>
    public long rejectedRequests { get; }

    /// <summary>
    /// Gets unique payload reservations still retained by one or more cold-load roots.
    /// </summary>
    public int pendingPayloads { get; }

    /// <summary>
    /// Gets the cumulative number of unique physical payload reads started.
    /// </summary>
    public long payloadReadsStarted { get; }

    /// <summary>
    /// Gets payload reads reused by distinct roots with overlapping dependency closures.
    /// </summary>
    public long sharedPayloadReads { get; }

    /// <summary>
    /// Gets unique encoded bytes retained until all requesting roots complete, fail or cancel.
    /// </summary>
    public long reservedBytes { get; }

    /// <summary>
    /// Gets the largest simultaneous unique encoded-byte reservation.
    /// </summary>
    public long peakReservedBytes { get; }
}
