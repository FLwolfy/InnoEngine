using System;

using Inno.Core.Mathematics;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>
/// Identifies one open Editor viewport purpose without prescribing rendering semantics.
/// </summary>
public readonly record struct EditorViewportKindId
{
    /// <summary>
    /// Creates a stable viewport purpose identifier.
    /// </summary>
    /// <param name="value">
    /// Globally stable viewport purpose.
    /// </param>
    public EditorViewportKindId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value.Trim();
    }

    /// <summary>
    /// Gets the stable viewport purpose.
    /// </summary>
    public string value { get; }

    /// <summary>
    /// Gets whether this identifier is usable.
    /// </summary>
    public bool isValid => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Formats this value as a human-readable representation.
    /// </summary>
    /// <returns>
    /// The human-readable representation of this value.
    /// </returns>
    public override string ToString() => value ?? string.Empty;
}

/// <summary>
/// Marks a reloadable Plugin adapter that can build one kind of Editor viewport.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorViewportProviderExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a viewport provider declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable provider identity.
    /// </param>
    /// <param name="kind">
    /// Open viewport purpose handled by the provider.
    /// </param>
    /// <param name="priority">
    /// Selection priority when several providers handle the same purpose.
    /// </param>
    public EditorViewportProviderExtensionAttribute(string id, string kind, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        this.id = id.Trim();
        this.kind = new EditorViewportKindId(kind);
        this.priority = priority;
    }

    /// <summary>
    /// Gets the globally stable provider identity.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the open viewport purpose handled by the provider.
    /// </summary>
    public EditorViewportKindId kind { get; }

    /// <summary>
    /// Gets provider selection priority.
    /// </summary>
    public int priority { get; }
}

/// <summary>
/// Supplies frame-only Editor interaction and output dimensions to a provider.
/// </summary>
public sealed class EditorViewportContext
{
    internal EditorViewportContext(
        EditorContext editor,
        EditorInteractions interactions,
        EditorViewportKindId kind,
        string viewportId,
        int pixelWidth,
        int pixelHeight,
        EditorViewportNavigationState navigation,
        RenderContentScope content,
        EditorViewportPresentation presentation)
    {
        this.editor = editor;
        this.interactions = interactions;
        this.kind = kind;
        this.viewportId = viewportId;
        this.pixelWidth = pixelWidth;
        this.pixelHeight = pixelHeight;
        this.navigation = navigation;
        this.content = content;
        this.presentation = presentation;
    }

    /// <summary>
    /// Gets the current Editor context.
    /// </summary>
    public EditorContext editor { get; }

    /// <summary>
    /// Gets the shared Editor interaction and selection service.
    /// </summary>
    public EditorInteractions interactions { get; }

    /// <summary>
    /// Gets the open viewport purpose.
    /// </summary>
    public EditorViewportKindId kind { get; }

    /// <summary>
    /// Gets the stable panel viewport identity.
    /// </summary>
    public string viewportId { get; }

    /// <summary>
    /// Gets target width in physical pixels.
    /// </summary>
    public int pixelWidth { get; }

    /// <summary>
    /// Gets target height in physical pixels.
    /// </summary>
    public int pixelHeight { get; }

    /// <summary>
    /// Gets host-owned neutral navigation state that the provider can map to its camera model.
    /// </summary>
    public EditorViewportNavigationState navigation { get; }

    /// <summary>
    /// Gets the explicit ordered host content visible to this viewport.
    /// </summary>
    public RenderContentScope content { get; }

    /// <summary>
    /// Gets host-selected presentation preferences for this viewport.
    /// </summary>
    public EditorViewportPresentation presentation { get; }
}

/// <summary>
/// Describes host-selected presentation preferences without prescribing rendering behavior.
/// </summary>
public readonly record struct EditorViewportPresentation
{
    /// <summary>
    /// Creates presentation preferences for one Editor viewport.
    /// </summary>
    /// <param name="backgroundColor">
    /// Linear clear color preferred by the host panel.
    /// </param>
    public EditorViewportPresentation(Color backgroundColor)
    {
        this.backgroundColor = backgroundColor;
    }

    /// <summary>
    /// Gets the linear clear color preferred by the host panel.
    /// </summary>
    public Color backgroundColor { get; }
}

/// <summary>
/// Describes the exact backend-neutral view and projection used to draw a viewport so host tools can manipulate
/// selected scene transforms without knowing the provider's camera model.
/// </summary>
public readonly record struct EditorViewportManipulationSpace
{
    /// <summary>
    /// Creates a manipulation space matching one rendered viewport frame.
    /// </summary>
    /// <param name="viewMatrix">
    /// World-to-view matrix used by the submitted frame.
    /// </param>
    /// <param name="projectionMatrix">
    /// View-to-clip matrix used by the submitted frame.
    /// </param>
    /// <param name="isOrthographic">
    /// Whether the projection is orthographic.
    /// </param>
    public EditorViewportManipulationSpace(
        Matrix viewMatrix,
        Matrix projectionMatrix,
        bool isOrthographic)
    {
        this.viewMatrix = viewMatrix;
        this.projectionMatrix = projectionMatrix;
        this.isOrthographic = isOrthographic;
    }

    /// <summary>
    /// Gets the world-to-view matrix used by the submitted frame.
    /// </summary>
    public Matrix viewMatrix { get; }

    /// <summary>
    /// Gets the view-to-clip matrix used by the submitted frame.
    /// </summary>
    public Matrix projectionMatrix { get; }

    /// <summary>
    /// Gets whether the submitted frame used an orthographic projection.
    /// </summary>
    public bool isOrthographic { get; }
}

/// <summary>
/// Returns provider-selected pipeline and frame data without exposing a GPU backend.
/// </summary>
public sealed class EditorViewportSubmission
{
    /// <summary>
    /// Creates a viewport submission.
    /// </summary>
    /// <param name="data">
    /// Pipeline-defined frame-only data.
    /// </param>
    /// <param name="pipeline">
    /// Provider-selected pipeline, or null for the project default.
    /// </param>
    /// <param name="targetFormat">
    /// Presentation target format expected by the pipeline.
    /// </param>
    /// <param name="priority">
    /// Ascending render scheduling priority.
    /// </param>
    /// <param name="manipulationSpace">
    /// Optional exact view/projection contract for host-owned transform manipulation tools.
    /// </param>
    public EditorViewportSubmission(
        RenderFrameData data,
        RenderPipelineAsset? pipeline = null,
        RenderTextureFormat targetFormat = RenderTextureFormat.RGBA8Srgb,
        int priority = 0,
        EditorViewportManipulationSpace? manipulationSpace = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.pipeline = pipeline;
        this.targetFormat = targetFormat;
        this.priority = priority;
        this.manipulationSpace = manipulationSpace;
    }

    /// <summary>
    /// Gets pipeline-defined frame-only data.
    /// </summary>
    public RenderFrameData data { get; }

    /// <summary>
    /// Gets the selected pipeline, or null for the project default.
    /// </summary>
    public RenderPipelineAsset? pipeline { get; }

    /// <summary>
    /// Gets the presentation target format expected by the pipeline.
    /// </summary>
    public RenderTextureFormat targetFormat { get; }

    /// <summary>
    /// Gets ascending render scheduling priority.
    /// </summary>
    public int priority { get; }

    /// <summary>
    /// Gets the optional exact view/projection contract used by host-owned transform manipulation tools.
    /// </summary>
    public EditorViewportManipulationSpace? manipulationSpace { get; }
}

/// <summary>
/// Supplies normalized pointer interaction over one rendered viewport.
/// </summary>
public sealed class EditorViewportPointerContext
{
    internal EditorViewportPointerContext(EditorViewportContext viewport, float x, float y, int button)
    {
        this.viewport = viewport;
        this.x = Math.Clamp(x, 0f, 1f);
        this.y = Math.Clamp(y, 0f, 1f);
        this.button = button;
    }

    /// <summary>
    /// Gets the owning frame-only viewport context.
    /// </summary>
    public EditorViewportContext viewport { get; }

    /// <summary>
    /// Gets normalized horizontal pointer position.
    /// </summary>
    public float x { get; }

    /// <summary>
    /// Gets normalized vertical pointer position.
    /// </summary>
    public float y { get; }

    /// <summary>
    /// Gets the platform-independent pointer button index.
    /// </summary>
    public int button { get; }
}

/// <summary>
/// Builds rendering-model-specific Editor requests while the host owns targets and presentation.
/// </summary>
public abstract class EditorViewportProvider
{
    /// <summary>
    /// Creates a parameterless reloadable viewport provider.
    /// </summary>
    protected EditorViewportProvider()
    {
    }

    /// <summary>
    /// Configures neutral host navigation before input is processed and a request is built.
    /// </summary>
    /// <param name="context">
    /// Frame-only Editor, navigation, content, and viewport context.
    /// </param>
    /// <returns>
    /// The provider's current navigation capabilities and optional selection focus bound.
    /// </returns>
    public virtual EditorViewportNavigationProfile ConfigureNavigation(EditorViewportContext context)
        => EditorViewportNavigationProfile.disabled;

    /// <summary>
    /// Builds one model-neutral render submission for the current frame.
    /// </summary>
    /// <param name="context">
    /// Frame-only Editor and viewport context.
    /// </param>
    /// <returns>
    /// Provider-selected pipeline and frame data.
    /// </returns>
    public abstract EditorViewportSubmission Build(EditorViewportContext context);

    /// <summary>
    /// Draws optional provider-specific toolbar controls.
    /// </summary>
    /// <param name="context">
    /// Frame-only Editor and viewport context.
    /// </param>
    public virtual void DrawToolbar(EditorViewportContext context)
    {
    }

    /// <summary>
    /// Handles one pointer click after the viewport image has been presented.
    /// </summary>
    /// <param name="context">
    /// Normalized frame-only pointer context.
    /// </param>
    public virtual void HandlePointer(EditorViewportPointerContext context)
    {
    }
}
