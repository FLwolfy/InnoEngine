using System;
using System.Threading;

namespace Inno.Rendering;

/// <summary>
/// Reports read-only statistics for the most recently completed render frame.
/// </summary>
public sealed class RenderFrameStatistics
{
    /// <summary>
    /// Creates an immutable frame statistics snapshot.
    /// </summary>
    /// <param name="frameIndex">
    /// Monotonic render frame index.
    /// </param>
    /// <param name="viewCount">
    /// Executed logical view count.
    /// </param>
    /// <param name="drawCount">
    /// Recorded draw count.
    /// </param>
    /// <param name="dispatchCount">
    /// Recorded compute dispatch count.
    /// </param>
    /// <param name="culledPassCount">
    /// Passes removed by graph compilation.
    /// </param>
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

    /// <summary>
    /// Gets the monotonic render frame index.
    /// </summary>
    public ulong frameIndex { get; }

    /// <summary>
    /// Gets the executed logical view count.
    /// </summary>
    public int viewCount { get; }

    /// <summary>
    /// Gets the recorded draw count.
    /// </summary>
    public int drawCount { get; }

    /// <summary>
    /// Gets the recorded compute dispatch count.
    /// </summary>
    public int dispatchCount { get; }

    /// <summary>
    /// Gets passes removed by graph compilation.
    /// </summary>
    public int culledPassCount { get; }
}

/// <summary>
/// Exposes current rendering configuration and immutable device state.
/// </summary>
public static class GraphicsSettings
{
    /// <summary>
    /// Gets current device capabilities, or <see langword="null"/> before device initialization.
    /// </summary>
    public static GraphicsCapabilities? capabilities
        => GraphicsSettingsExecutionContext.currentOrNull?.capabilities;

    /// <summary>
    /// Gets or sets the project default pipeline used by requests without an override.
    /// </summary>
    public static RenderPipelineAsset? defaultPipeline
    {
        get => GraphicsSettingsExecutionContext.currentOrNull?.defaultPipeline;
        set => GraphicsSettingsExecutionContext.current.defaultPipeline = value;
    }

    /// <summary>
    /// Gets statistics for the last completed frame, or <see langword="null"/> before the first frame.
    /// </summary>
    public static RenderFrameStatistics? frameStatistics
        => GraphicsSettingsExecutionContext.currentOrNull?.frameStatistics;
}

internal sealed class GraphicsSettingsState
{
    private readonly object m_sync = new();
    private RenderPipelineAsset? m_defaultPipeline;
    private RenderFrameStatistics? m_frameStatistics;

    internal GraphicsSettingsState(GraphicsCapabilities capabilities)
    {
        this.capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    internal GraphicsCapabilities capabilities { get; }

    internal RenderPipelineAsset? defaultPipeline
    {
        get
        {
            lock (m_sync)
                return m_defaultPipeline;
        }
        set
        {
            lock (m_sync)
                m_defaultPipeline = value;
        }
    }

    internal RenderFrameStatistics? frameStatistics
    {
        get
        {
            lock (m_sync)
                return m_frameStatistics;
        }
        set
        {
            lock (m_sync)
                m_frameStatistics = value;
        }
    }

    internal void Clear()
    {
        lock (m_sync)
        {
            m_defaultPipeline = null;
            m_frameStatistics = null;
        }
    }
}

internal static class GraphicsSettingsExecutionContext
{
    private static readonly AsyncLocal<Scope?> S_CURRENT = new();

    internal static GraphicsSettingsState current
        => currentOrNull
            ?? throw new InvalidOperationException(
                "No rendering runtime is bound to the current execution context.");

    internal static GraphicsSettingsState? currentOrNull => S_CURRENT.Value?.state;

    internal static IDisposable Enter(GraphicsSettingsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var scope = new Scope(state, S_CURRENT.Value);
        S_CURRENT.Value = scope;
        return scope;
    }

    private sealed class Scope(GraphicsSettingsState state, Scope? parent) : IDisposable
    {
        private bool m_disposed;

        internal GraphicsSettingsState state { get; } = state;

        /// <summary>
        /// Restores the parent rendering execution scope in last-in-first-out order.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
                return;
            if (!ReferenceEquals(S_CURRENT.Value, this))
            {
                throw new InvalidOperationException(
                    "Rendering execution scopes must be disposed in last-in-first-out order.");
            }
            m_disposed = true;
            S_CURRENT.Value = parent;
        }
    }
}
