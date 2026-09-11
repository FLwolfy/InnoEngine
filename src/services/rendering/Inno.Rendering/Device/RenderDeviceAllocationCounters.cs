using System;

namespace Inno.Rendering;

/// <summary>
/// Reports cumulative successful native allocations made by a device's transient graph resource pools.
/// Pool reuse does not increment these counters; retirement does not decrement them. They are not live
/// resource counts, managed allocation counts, byte measurements or per-frame command counts.
/// </summary>
public readonly record struct RenderDeviceAllocationCounters
{
    /// <summary>
    /// Creates an immutable allocation snapshot scoped to one device generation.
    /// </summary>
    /// <param name="deviceGeneration">
    /// Non-zero generation that owns the measured pools; counters restart for a new generation.
    /// </param>
    /// <param name="textureAllocations">
    /// Successful native transient texture allocations since device creation.
    /// </param>
    /// <param name="bufferAllocations">
    /// Successful native transient buffer allocations since device creation.
    /// </param>
    /// <param name="frameBufferAllocations">
    /// Successful native transient attachment framebuffer allocations since device creation.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The device generation is zero.
    /// </exception>
    public RenderDeviceAllocationCounters(
        uint deviceGeneration,
        ulong textureAllocations,
        ulong bufferAllocations,
        ulong frameBufferAllocations)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceGeneration);
        this.deviceGeneration = deviceGeneration;
        this.textureAllocations = textureAllocations;
        this.bufferAllocations = bufferAllocations;
        this.frameBufferAllocations = frameBufferAllocations;
    }

    /// <summary>
    /// Gets the device generation whose cumulative counters are represented by this snapshot.
    /// </summary>
    public uint deviceGeneration { get; }

    /// <summary>
    /// Gets successful native transient texture allocations since device creation.
    /// </summary>
    public ulong textureAllocations { get; }

    /// <summary>
    /// Gets successful native transient buffer allocations since device creation.
    /// </summary>
    public ulong bufferAllocations { get; }

    /// <summary>
    /// Gets successful native transient attachment framebuffer allocations since device creation.
    /// </summary>
    public ulong frameBufferAllocations { get; }
}
