using System;
using System.Collections.Generic;

namespace Inno.Rendering;

/// <summary>
/// Defines a destination pixel rectangle without assuming producer or rendering semantics.
/// </summary>
public readonly record struct RenderViewport
{
    /// <summary>
    /// Creates a render viewport.
    /// </summary>
    /// <param name="x">
    /// Left pixel offset in the destination.
    /// </param>
    /// <param name="y">
    /// Top pixel offset in the destination.
    /// </param>
    /// <param name="width">
    /// Positive viewport width.
    /// </param>
    /// <param name="height">
    /// Positive viewport height.
    /// </param>
    public RenderViewport(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    /// <summary>
    /// Gets the left pixel offset.
    /// </summary>
    public int x { get; }

    /// <summary>
    /// Gets the top pixel offset.
    /// </summary>
    public int y { get; }

    /// <summary>
    /// Gets the viewport width.
    /// </summary>
    public int width { get; }

    /// <summary>
    /// Gets the viewport height.
    /// </summary>
    public int height { get; }
}

/// <summary>
/// Carries generation-scoped, frame-only payloads between a request producer and its pipeline.
/// </summary>
/// <remarks>
/// Values may use reloadable plugin types because a snapshot is retained only until the owning
/// render frame completes. Persistent configuration must use stable identifiers and serialized bytes.
/// </remarks>
public sealed class RenderFrameData
{
    private readonly Dictionary<FrameDataKey, object> m_values = [];
    private bool m_isReadOnly;

    /// <summary>
    /// Gets the number of populated channel and value-type pairs.
    /// </summary>
    public int count => m_values.Count;

    /// <summary>
    /// Adds or replaces one typed value in an open data channel.
    /// </summary>
    /// <typeparam name="TValue">
    /// Frame-local payload type.
    /// </typeparam>
    /// <param name="channel">
    /// Pipeline-defined stable data channel.
    /// </param>
    /// <param name="value">
    /// Value retained until the current frame completes.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown after the data has entered a request.
    /// </exception>
    public void Set<TValue>(Inno.Rendering.RenderDataChannelId channel, TValue value)
        where TValue : notnull
    {
        if (!channel.isValid)
            throw new ArgumentException("A render data channel must be valid.", nameof(channel));
        if (m_isReadOnly)
            throw new InvalidOperationException("Submitted render frame data is immutable.");
        ArgumentNullException.ThrowIfNull(value);
        m_values[new FrameDataKey(channel, typeof(TValue))] = value;
    }

    /// <summary>
    /// Tries to read one typed value from an open data channel.
    /// </summary>
    /// <typeparam name="TValue">
    /// Expected frame-local payload type.
    /// </typeparam>
    /// <param name="channel">
    /// Pipeline-defined stable data channel.
    /// </param>
    /// <param name="value">
    /// Receives the stored value when present.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the channel contains the exact requested type.
    /// </returns>
    public bool TryGet<TValue>(
        Inno.Rendering.RenderDataChannelId channel,
        out TValue? value)
    {
        if (channel.isValid
            && m_values.TryGetValue(new FrameDataKey(channel, typeof(TValue)), out object? stored)
            && stored is TValue typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Removes all values before this object enters a submitted request.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown after the data has entered a request.
    /// </exception>
    public void Clear()
    {
        if (m_isReadOnly)
            throw new InvalidOperationException("Submitted render frame data is immutable.");
        m_values.Clear();
    }

    internal RenderFrameData Snapshot()
    {
        var snapshot = new RenderFrameData();
        foreach ((FrameDataKey key, object value) in m_values)
            snapshot.m_values.Add(key, value);
        snapshot.m_isReadOnly = true;
        return snapshot;
    }

    private readonly record struct FrameDataKey(
        Inno.Rendering.RenderDataChannelId channel,
        Type type);
}

/// <summary>
/// Requests one pipeline-defined rendering operation without prescribing world semantics.
/// </summary>
public sealed class RenderRequest
{
    /// <summary>
    /// Creates an immutable render request.
    /// </summary>
    /// <param name="name">
    /// Frame-local diagnostic name.
    /// </param>
    /// <param name="target">
    /// Render destination.
    /// </param>
    /// <param name="viewport">
    /// Destination pixel viewport.
    /// </param>
    /// <param name="pipeline">
    /// Optional per-request pipeline asset; the project default is used when null.
    /// </param>
    /// <param name="data">
    /// Optional pipeline-defined frame data copied into an immutable snapshot.
    /// </param>
    /// <param name="priority">
    /// Ascending frame scheduling priority.
    /// </param>
    public RenderRequest(
        string name,
        RenderTarget target,
        RenderViewport viewport,
        RenderPipelineAsset? pipeline = null,
        RenderFrameData? data = null,
        int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.name = name;
        this.target = target;
        this.viewport = viewport;
        this.pipeline = pipeline;
        this.data = data?.Snapshot() ?? new RenderFrameData().Snapshot();
        this.priority = priority;
    }

    /// <summary>
    /// Gets the frame-local diagnostic name.
    /// </summary>
    public string name { get; }

    /// <summary>
    /// Gets the render destination.
    /// </summary>
    public RenderTarget target { get; }

    /// <summary>
    /// Gets the destination pixel viewport.
    /// </summary>
    public RenderViewport viewport { get; }

    /// <summary>
    /// Gets the per-request pipeline asset, or null to use the project default.
    /// </summary>
    public RenderPipelineAsset? pipeline { get; }

    /// <summary>
    /// Gets immutable pipeline-defined frame data.
    /// </summary>
    public RenderFrameData data { get; }

    /// <summary>
    /// Gets the ascending frame scheduling priority.
    /// </summary>
    public int priority { get; }
}
