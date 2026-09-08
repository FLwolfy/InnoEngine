namespace Inno.Rendering.Runtime;

/// <summary>
/// Reports control-thread resource occupancy and admission pressure without exposing backend objects.
/// </summary>
public readonly record struct RenderResourceStatistics
{
    /// <summary>
    /// Gets active persistent buffers, textures, pipelines, material programs and geometry pairs.
    /// </summary>
    public int activeResources { get; init; }
    /// <summary>
    /// Gets resource entries whose native retirement is still pending.
    /// </summary>
    public int retiringResources { get; init; }
    /// <summary>
    /// Gets cache admissions rejected by per-kind capacity.
    /// </summary>
    public long rejectedResources { get; init; }
    /// <summary>
    /// Gets readbacks that still own native operations.
    /// </summary>
    public int pendingReadbacks { get; init; }
    /// <summary>
    /// Gets the largest simultaneous readback occupancy.
    /// </summary>
    public int peakReadbacks { get; init; }
    /// <summary>
    /// Gets readbacks rejected before native allocation.
    /// </summary>
    public long rejectedReadbacks { get; init; }
    /// <summary>
    /// Gets retained upload pages, including pages awaiting retirement.
    /// </summary>
    public int uploadPages { get; init; }
    /// <summary>
    /// Gets resident upload bytes, including pages awaiting retirement.
    /// </summary>
    public long uploadResidentBytes { get; init; }
    /// <summary>
    /// Gets the high-water mark of resident upload bytes.
    /// </summary>
    public long uploadPeakBytes { get; init; }
    /// <summary>
    /// Gets successfully uploaded bytes during the current frame.
    /// </summary>
    public long uploadedFrameBytes { get; init; }
    /// <summary>
    /// Gets upload requests rejected by resident or per-frame limits.
    /// </summary>
    public long rejectedUploads { get; init; }
    /// <summary>
    /// Gets active offscreen targets.
    /// </summary>
    public int targets { get; init; }
    /// <summary>
    /// Gets offscreen target requests rejected by finite capacity.
    /// </summary>
    public long rejectedTargets { get; init; }
}
