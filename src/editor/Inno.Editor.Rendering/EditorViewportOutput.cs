using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Inno.Platform.Sdl3.ImGui;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>
/// Identifies a renderer-owned editor viewport image through an opaque presentation token.
/// </summary>
public readonly record struct EditorViewportOutput
{
    /// <summary>
    /// Creates an editor viewport output snapshot.
    /// </summary>
    /// <param name="viewportId">
    /// Stable viewport identity.
    /// </param>
    /// <param name="texture">
    /// Opaque presentation token, or an invalid token while warming up.
    /// </param>
    /// <param name="pixelWidth">
    /// Current target width.
    /// </param>
    /// <param name="pixelHeight">
    /// Current target height.
    /// </param>
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

    /// <summary>
    /// Gets the stable viewport identity.
    /// </summary>
    public string viewportId { get; }

    /// <summary>
    /// Gets the opaque presentation token.
    /// </summary>
    public ImGuiTextureHandle texture { get; }

    /// <summary>
    /// Gets the current target width.
    /// </summary>
    public int pixelWidth { get; }

    /// <summary>
    /// Gets the current target height.
    /// </summary>
    public int pixelHeight { get; }

    /// <summary>
    /// Gets whether a completed target can be drawn.
    /// </summary>
    public bool isReady => texture.isValid;
}

/// <summary>
/// Bridges editor extensions to a host-owned render request sink and opaque texture presenter.
/// </summary>
public interface IEditorRenderingHost
{
    /// <summary>
    /// Submits or updates one composed offscreen editor viewport.
    /// </summary>
    /// <param name="composition">
    /// Complete ordered model composition for the current frame.
    /// </param>
    /// <returns>
    /// The current presentation output, which can be warming up for one frame.
    /// </returns>
    EditorViewportOutput Submit(EditorViewportComposition composition);

    /// <summary>
    /// Draws a ready viewport output inside the current ImGui window.
    /// </summary>
    /// <param name="output">
    /// Output returned by <see cref="Submit"/>.
    /// </param>
    /// <param name="logicalSize">
    /// Destination size in logical UI pixels.
    /// </param>
    void Draw(EditorViewportOutput output, Vector2 logicalSize);

    /// <summary>
    /// Releases one viewport and queues its GPU target for frame-safe destruction.
    /// </summary>
    /// <param name="viewportId">
    /// Stable viewport identity.
    /// </param>
    void Release(string viewportId);

    /// <summary>
    /// Releases every viewport owned by this editor host service.
    /// </summary>
    void ReleaseAll();
}

/// <summary>
/// Describes one model-neutral layer in an Editor viewport composition.
/// </summary>
public sealed class EditorViewportLayer
{
    /// <summary>
    /// Creates one immutable model-neutral viewport layer.
    /// </summary>
    /// <param name="contributorId">
    /// Stable identity of the rendering-model contributor that produced the layer.
    /// </param>
    /// <param name="pipeline">
    /// Contributor-selected pipeline, or null for the project default.
    /// </param>
    /// <param name="data">
    /// Contributor-defined frame-only data.
    /// </param>
    /// <param name="order">
    /// Ascending model-composition order.
    /// </param>
    public EditorViewportLayer(
        string contributorId,
        RenderPipelineAsset? pipeline,
        RenderFrameData data,
        int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorId);
        ArgumentNullException.ThrowIfNull(data);
        this.contributorId = contributorId;
        this.pipeline = pipeline;
        this.data = data;
        this.order = order;
    }

    /// <summary>
    /// Gets the stable identity of the rendering-model contributor.
    /// </summary>
    public string contributorId { get; }

    /// <summary>
    /// Gets the contributor-selected pipeline, or null for the project default.
    /// </summary>
    public RenderPipelineAsset? pipeline { get; }

    /// <summary>
    /// Gets contributor-defined frame-only data.
    /// </summary>
    public RenderFrameData data { get; }

    /// <summary>
    /// Gets ascending model-composition order.
    /// </summary>
    public int order { get; }
}

/// <summary>
/// Describes a complete ordered set of rendering-model layers targeting one Editor viewport.
/// </summary>
public sealed class EditorViewportComposition
{
    private readonly IReadOnlyList<EditorViewportLayer> m_layers;

    /// <summary>
    /// Creates an immutable viewport composition.
    /// </summary>
    /// <param name="viewportId">
    /// Stable panel viewport identity.
    /// </param>
    /// <param name="pixelWidth">
    /// Positive target width.
    /// </param>
    /// <param name="pixelHeight">
    /// Positive target height.
    /// </param>
    /// <param name="targetFormat">
    /// Shared presentation target format required by every layer.
    /// </param>
    /// <param name="layers">
    /// Ordered non-empty rendering-model layer collection.
    /// </param>
    public EditorViewportComposition(
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        RenderTextureFormat targetFormat,
        IEnumerable<EditorViewportLayer> layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewportId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        ArgumentNullException.ThrowIfNull(layers);
        EditorViewportLayer[] materializedLayers = layers.ToArray();
        if (materializedLayers.Length == 0)
            throw new ArgumentException("A viewport composition requires at least one model layer.", nameof(layers));
        if (materializedLayers.Any(static layer => layer is null))
            throw new ArgumentException("A viewport composition cannot contain null model layers.", nameof(layers));
        if (materializedLayers.Select(static layer => layer.contributorId).Distinct(StringComparer.Ordinal).Count()
            != materializedLayers.Length)
        {
            throw new ArgumentException("Viewport contributor identities must be unique.", nameof(layers));
        }
        m_layers = Array.AsReadOnly(materializedLayers
            .OrderBy(static layer => layer.order)
            .ThenBy(static layer => layer.contributorId, StringComparer.Ordinal)
            .ToArray());
        this.viewportId = viewportId;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
        this.targetFormat = targetFormat;
    }

    /// <summary>
    /// Gets the stable viewport identity.
    /// </summary>
    public string viewportId { get; }

    /// <summary>
    /// Gets the target width.
    /// </summary>
    public int pixelWidth { get; }

    /// <summary>
    /// Gets the target height.
    /// </summary>
    public int pixelHeight { get; }

    /// <summary>
    /// Gets the shared presentation target format.
    /// </summary>
    public RenderTextureFormat targetFormat { get; }

    /// <summary>
    /// Gets the ordered rendering-model layers.
    /// </summary>
    public IReadOnlyList<EditorViewportLayer> layers => m_layers;
}
