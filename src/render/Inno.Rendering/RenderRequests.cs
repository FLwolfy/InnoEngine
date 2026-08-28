using System;
using Inno.Core.Mathematics;
using Inno.Engine.Scene.Layers;

namespace Inno.Rendering;

/// <summary>
/// Contains immutable camera matrices and viewport data for one render request.
/// </summary>
public sealed class RenderView
{
    /// <summary>
    /// Creates an immutable render view.
    /// </summary>
    /// <param name="viewMatrix">Left-handed world-to-view matrix.</param>
    /// <param name="projectionMatrix">Left-handed zero-to-one projection before backend correction.</param>
    /// <param name="worldPosition">Camera world position.</param>
    /// <param name="pixelWidth">Viewport width in pixels.</param>
    /// <param name="pixelHeight">Viewport height in pixels.</param>
    /// <param name="cullingMask">Scene layers visible to this view.</param>
    public RenderView(
        Matrix viewMatrix,
        Matrix projectionMatrix,
        Vector3 worldPosition,
        int pixelWidth,
        int pixelHeight,
        GameLayerMask cullingMask)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        this.viewMatrix = viewMatrix;
        this.projectionMatrix = projectionMatrix;
        this.worldPosition = worldPosition;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
        this.cullingMask = cullingMask;
    }

    /// <summary>Gets the left-handed world-to-view matrix.</summary>
    public Matrix viewMatrix { get; }

    /// <summary>Gets the left-handed zero-to-one projection before backend correction.</summary>
    public Matrix projectionMatrix { get; }

    /// <summary>Gets the camera world position.</summary>
    public Vector3 worldPosition { get; }

    /// <summary>Gets the viewport width in pixels.</summary>
    public int pixelWidth { get; }

    /// <summary>Gets the viewport height in pixels.</summary>
    public int pixelHeight { get; }

    /// <summary>Gets scene layers visible to this view.</summary>
    public GameLayerMask cullingMask { get; }
}

/// <summary>
/// Requests one camera or editor view from the active render pipeline.
/// </summary>
public sealed class RenderRequest
{
    /// <summary>
    /// Creates a render request.
    /// </summary>
    /// <param name="name">Unique frame-local diagnostic name.</param>
    /// <param name="view">Immutable camera view.</param>
    /// <param name="target">Render destination.</param>
    /// <param name="renderPath">Per-view path override.</param>
    /// <param name="clearMode">Target initialization mode.</param>
    /// <param name="backgroundColor">Linear background clear color.</param>
    /// <param name="priority">Ascending camera scheduling priority.</param>
    /// <param name="enablePicking">Whether the pipeline should generate a GPU object-ID target.</param>
    /// <param name="selectedObjectId">Optional renderer identity consumed by editor overlay features.</param>
    public RenderRequest(
        string name,
        RenderView view,
        RenderTarget target,
        RenderPath renderPath = RenderPath.Automatic,
        CameraClearMode clearMode = CameraClearMode.Sky,
        Color backgroundColor = default,
        int priority = 0,
        bool enablePicking = false,
        Guid selectedObjectId = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(view);
        this.name = name;
        this.view = view;
        this.target = target;
        this.renderPath = renderPath;
        this.clearMode = clearMode;
        this.backgroundColor = backgroundColor;
        this.priority = priority;
        this.enablePicking = enablePicking;
        this.selectedObjectId = selectedObjectId;
    }

    /// <summary>Gets the unique frame-local diagnostic name.</summary>
    public string name { get; }

    /// <summary>Gets the immutable camera view.</summary>
    public RenderView view { get; }

    /// <summary>Gets the render destination.</summary>
    public RenderTarget target { get; }

    /// <summary>Gets the per-view path override.</summary>
    public RenderPath renderPath { get; }

    /// <summary>Gets target initialization mode.</summary>
    public CameraClearMode clearMode { get; }

    /// <summary>Gets the linear background clear color.</summary>
    public Color backgroundColor { get; }

    /// <summary>Gets ascending camera scheduling priority.</summary>
    public int priority { get; }

    /// <summary>Gets whether a GPU object-ID target is requested for this view.</summary>
    public bool enablePicking { get; }

    /// <summary>Gets the optional selected renderer identity for editor overlay features.</summary>
    public Guid selectedObjectId { get; }
}
