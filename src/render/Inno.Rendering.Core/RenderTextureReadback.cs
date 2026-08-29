using System;

namespace Inno.Rendering.Core;

/// <summary>Identifies one backend-neutral asynchronous texture readback operation.</summary>
public readonly record struct RenderTextureReadbackHandle
{
    internal RenderTextureReadbackHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }
    internal uint deviceGeneration { get; }

    /// <summary>Gets whether the handle identifies a device readback operation.</summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>Contains immutable bytes copied from one complete texture mip.</summary>
public sealed class RenderTextureReadbackResult
{
    private readonly byte[] m_data;

    /// <summary>Creates one immutable texture readback result.</summary>
    /// <param name="descriptor">Source texture descriptor.</param>
    /// <param name="mipLevel">Source mip level.</param>
    /// <param name="rowPitch">Number of bytes between adjacent texel rows.</param>
    /// <param name="data">Complete tightly packed mip bytes for every addressable layer.</param>
    public RenderTextureReadbackResult(
        RenderTextureDescriptor descriptor,
        int mipLevel,
        int rowPitch,
        ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowPitch);
        if (mipLevel >= descriptor.mipCount)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if (data.IsEmpty)
            throw new ArgumentException("Texture readback data cannot be empty.", nameof(data));
        this.descriptor = descriptor;
        this.mipLevel = mipLevel;
        this.rowPitch = rowPitch;
        m_data = data.ToArray();
    }

    /// <summary>Gets the source texture descriptor.</summary>
    public RenderTextureDescriptor descriptor { get; }

    /// <summary>Gets the source mip level.</summary>
    public int mipLevel { get; }

    /// <summary>Gets the byte distance between adjacent rows.</summary>
    public int rowPitch { get; }

    /// <summary>Gets complete tightly packed mip bytes for every addressable layer.</summary>
    public ReadOnlyMemory<byte> data => m_data;
}
