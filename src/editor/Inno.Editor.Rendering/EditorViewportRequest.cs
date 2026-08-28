using System;
using Inno.Core.Mathematics;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>
/// Describes one editor viewport without exposing a graphics-backend resource.
/// </summary>
public sealed class EditorViewportRequest
{
    /// <summary>Creates an immutable editor viewport request.</summary>
    /// <param name="viewportId">Stable project-independent viewport identity.</param>
    /// <param name="view">Camera matrices and pixel dimensions.</param>
    /// <param name="renderPath">Per-viewport render-path override.</param>
    /// <param name="clearMode">Target initialization mode.</param>
    /// <param name="backgroundColor">Linear fallback clear color.</param>
    /// <param name="priority">Ascending render priority.</param>
    /// <param name="enablePicking">Whether to render an object-ID attachment.</param>
    /// <param name="selectedObjectId">Optional selected renderer identity for overlay features.</param>
    public EditorViewportRequest(
        string viewportId,
        RenderView view,
        RenderPath renderPath = RenderPath.Automatic,
        CameraClearMode clearMode = CameraClearMode.Sky,
        Color backgroundColor = default,
        int priority = 0,
        bool enablePicking = false,
        Guid selectedObjectId = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        ArgumentNullException.ThrowIfNull(view);
        this.viewportId = viewportId;
        this.view = view;
        this.renderPath = renderPath;
        this.clearMode = clearMode;
        this.backgroundColor = backgroundColor;
        this.priority = priority;
        this.enablePicking = enablePicking;
        this.selectedObjectId = selectedObjectId;
    }

    /// <summary>Gets the stable project-independent viewport identity.</summary>
    public string viewportId { get; }

    /// <summary>Gets camera matrices and pixel dimensions.</summary>
    public RenderView view { get; }

    /// <summary>Gets the per-viewport render-path override.</summary>
    public RenderPath renderPath { get; }

    /// <summary>Gets target initialization behavior.</summary>
    public CameraClearMode clearMode { get; }

    /// <summary>Gets the linear fallback clear color.</summary>
    public Color backgroundColor { get; }

    /// <summary>Gets ascending render priority.</summary>
    public int priority { get; }

    /// <summary>Gets whether an object-ID attachment is requested.</summary>
    public bool enablePicking { get; }

    /// <summary>Gets the optional selected renderer identity for overlay features.</summary>
    public Guid selectedObjectId { get; }
}
