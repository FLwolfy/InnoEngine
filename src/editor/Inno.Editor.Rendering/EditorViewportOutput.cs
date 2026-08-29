using System;
using System.Numerics;
using Inno.Platform.ImGui;
using Inno.Rendering;
using Inno.Rendering.Core;

namespace Inno.Editor.Rendering;

/// <summary>
/// Identifies a renderer-owned editor viewport image through an opaque presentation token.
/// </summary>
public readonly record struct EditorViewportOutput
{
    /// <summary>Creates an editor viewport output snapshot.</summary>
    /// <param name="viewportId">Stable viewport identity.</param>
    /// <param name="texture">Opaque presentation token, or an invalid token while warming up.</param>
    /// <param name="pixelWidth">Current target width.</param>
    /// <param name="pixelHeight">Current target height.</param>
    public EditorViewportOutput(
        string viewportId,
        ImGuiTextureHandle texture,
        int pixelWidth,
        int pixelHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        this.viewportId = viewportId;
        this.texture = texture;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
    }

    /// <summary>Gets the stable viewport identity.</summary>
    public string viewportId { get; }

    /// <summary>Gets the opaque presentation token.</summary>
    public ImGuiTextureHandle texture { get; }

    /// <summary>Gets the current target width.</summary>
    public int pixelWidth { get; }

    /// <summary>Gets the current target height.</summary>
    public int pixelHeight { get; }

    /// <summary>Gets whether a completed target can be drawn.</summary>
    public bool isReady => texture.isValid;
}

/// <summary>
/// Bridges editor extensions to a host-owned render request sink and opaque texture presenter.
/// </summary>
public interface IEditorRenderingHost
{
    /// <summary>Submits or updates one offscreen editor viewport.</summary>
    /// <param name="request">Complete frame request.</param>
    /// <returns>The current presentation output, which can be warming up for one frame.</returns>
    EditorViewportOutput Submit(EditorViewportRequest request);

    /// <summary>Draws a ready viewport output inside the current ImGui window.</summary>
    /// <param name="output">Output returned by <see cref="Submit"/>.</param>
    /// <param name="logicalSize">Destination size in logical UI pixels.</param>
    void Draw(EditorViewportOutput output, Vector2 logicalSize);

    /// <summary>Releases one viewport and queues its GPU target for frame-safe destruction.</summary>
    /// <param name="viewportId">Stable viewport identity.</param>
    void Release(string viewportId);

    /// <summary>Releases every viewport owned by this editor host service.</summary>
    void ReleaseAll();
}

/// <summary>Describes one model-neutral offscreen request prepared by an Editor viewport provider.</summary>
public sealed class EditorViewportRequest
{
    /// <summary>Creates one immutable model-neutral viewport request.</summary>
    /// <param name="viewportId">Stable panel viewport identity.</param>
    /// <param name="pixelWidth">Positive target width.</param>
    /// <param name="pixelHeight">Positive target height.</param>
    /// <param name="pipeline">Provider-selected pipeline, or null for the project default.</param>
    /// <param name="data">Provider-defined frame-only data.</param>
    /// <param name="targetFormat">Provider-selected presentation target format.</param>
    /// <param name="priority">Ascending render scheduling priority.</param>
    public EditorViewportRequest(
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        RenderPipelineAsset? pipeline,
        RenderFrameData data,
        RenderTextureFormat targetFormat = RenderTextureFormat.RGBA8Srgb,
        int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        ArgumentNullException.ThrowIfNull(data);
        this.viewportId = viewportId;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
        this.pipeline = pipeline;
        this.data = data;
        this.targetFormat = targetFormat;
        this.priority = priority;
    }

    /// <summary>Gets the stable viewport identity.</summary>
    public string viewportId { get; }

    /// <summary>Gets the target width.</summary>
    public int pixelWidth { get; }

    /// <summary>Gets the target height.</summary>
    public int pixelHeight { get; }

    /// <summary>Gets the provider-selected pipeline, or null for the project default.</summary>
    public RenderPipelineAsset? pipeline { get; }

    /// <summary>Gets provider-defined frame-only data.</summary>
    public RenderFrameData data { get; }

    /// <summary>Gets the provider-selected presentation target format.</summary>
    public RenderTextureFormat targetFormat { get; }

    /// <summary>Gets ascending render scheduling priority.</summary>
    public int priority { get; }
}
