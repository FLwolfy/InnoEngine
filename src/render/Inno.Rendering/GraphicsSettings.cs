using System;
using Inno.Rendering.Core;

namespace Inno.Rendering;

/// <summary>
/// Reports read-only statistics for the most recently completed render frame.
/// </summary>
public sealed class RenderFrameStatistics
{
    /// <summary>
    /// Creates an immutable frame statistics snapshot.
    /// </summary>
    /// <param name="frameIndex">Monotonic render frame index.</param>
    /// <param name="viewCount">Executed logical view count.</param>
    /// <param name="drawCount">Recorded draw count.</param>
    /// <param name="dispatchCount">Recorded compute dispatch count.</param>
    /// <param name="culledPassCount">Passes removed by graph compilation.</param>
    public RenderFrameStatistics(
        ulong frameIndex,
        int viewCount,
        int drawCount,
        int dispatchCount,
        int culledPassCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(viewCount);
        ArgumentOutOfRangeException.ThrowIfNegative(drawCount);
        ArgumentOutOfRangeException.ThrowIfNegative(dispatchCount);
        ArgumentOutOfRangeException.ThrowIfNegative(culledPassCount);
        this.frameIndex = frameIndex;
        this.viewCount = viewCount;
        this.drawCount = drawCount;
        this.dispatchCount = dispatchCount;
        this.culledPassCount = culledPassCount;
    }

    /// <summary>Gets the monotonic render frame index.</summary>
    public ulong frameIndex { get; }

    /// <summary>Gets the executed logical view count.</summary>
    public int viewCount { get; }

    /// <summary>Gets the recorded draw count.</summary>
    public int drawCount { get; }

    /// <summary>Gets the recorded compute dispatch count.</summary>
    public int dispatchCount { get; }

    /// <summary>Gets passes removed by graph compilation.</summary>
    public int culledPassCount { get; }
}

/// <summary>
/// Exposes current rendering configuration and immutable device state.
/// </summary>
public static class GraphicsSettings
{
    private static readonly object S_LOCK = new();
    private static GraphicsCapabilities? s_capabilities;
    private static RenderPipelineAsset? s_pipelineAsset;
    private static RenderFrameStatistics? s_statistics;

    /// <summary>Gets current device capabilities, or <see langword="null"/> before device initialization.</summary>
    public static GraphicsCapabilities? capabilities
    {
        get
        {
            lock (S_LOCK)
            {
                return s_capabilities;
            }
        }
    }

    /// <summary>Gets the active pipeline asset, or <see langword="null"/> before configuration.</summary>
    public static RenderPipelineAsset? pipelineAsset
    {
        get
        {
            lock (S_LOCK)
            {
                return s_pipelineAsset;
            }
        }
    }

    /// <summary>Gets statistics for the last completed frame, or <see langword="null"/> before the first frame.</summary>
    public static RenderFrameStatistics? frameStatistics
    {
        get
        {
            lock (S_LOCK)
            {
                return s_statistics;
            }
        }
    }

    internal static void SetDevice(GraphicsCapabilities? capabilities)
    {
        lock (S_LOCK)
        {
            s_capabilities = capabilities;
        }
    }

    internal static void SetPipelineAsset(RenderPipelineAsset? pipelineAsset)
    {
        lock (S_LOCK)
        {
            s_pipelineAsset = pipelineAsset;
        }
    }

    internal static void SetStatistics(RenderFrameStatistics? statistics)
    {
        lock (S_LOCK)
        {
            s_statistics = statistics;
        }
    }
}
