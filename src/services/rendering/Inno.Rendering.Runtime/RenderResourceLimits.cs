using System;

namespace Inno.Rendering.Runtime;

/// <summary>
/// Sets finite native resource and asynchronous readback admission limits for one rendering owner.
/// </summary>
public sealed class RenderResourceLimits
{
    /// <summary>
    /// Gets the maximum active entries in each buffer, texture or pipeline cache.
    /// </summary>
    public int resourcesPerKind { get; init; } = 16384;

    /// <summary>
    /// Gets the maximum readbacks that can retain GPU resources before owner-thread completion.
    /// </summary>
    public int pendingReadbacks { get; init; } = 64;

    /// <summary>
    /// Gets the maximum resident frame-upload pages.
    /// </summary>
    public int uploadPages { get; init; } = 1024;
    /// <summary>
    /// Gets the maximum resident frame-upload bytes, including retired pages.
    /// </summary>
    public long uploadResidentBytes { get; init; } = 256L * 1024 * 1024;
    /// <summary>
    /// Gets the maximum bytes uploaded during one frame.
    /// </summary>
    public long uploadBytesPerFrame { get; init; } = 64L * 1024 * 1024;
    /// <summary>
    /// Gets the offscreen target count and per-frame target allocation capacity.
    /// </summary>
    public int targets { get; init; } = 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resourcesPerKind);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uploadPages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uploadResidentBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uploadBytesPerFrame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pendingReadbacks);
    }
}
