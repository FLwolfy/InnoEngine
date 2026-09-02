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
/// Marks a reloadable rendering-model contributor for one Editor viewport purpose.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorViewportContributorExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a viewport contributor declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable contributor identity.
    /// </param>
    /// <param name="kind">
    /// Open viewport purpose handled by the contributor.
    /// </param>
    /// <param name="order">
    /// Ascending model-composition order within the viewport.
    /// </param>
    /// <param name="controllerPriority">
    /// Priority used to select the contributor that owns navigation, tools, and pointer interaction.
    /// </param>
    public EditorViewportContributorExtensionAttribute(
        string id,
        string kind,
        int order = 0,
        int controllerPriority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        this.id = id.Trim();
        this.kind = new EditorViewportKindId(kind);
        this.order = order;
        this.controllerPriority = controllerPriority;
    }

    /// <summary>
    /// Gets the globally stable contributor identity.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the open viewport purpose handled by the contributor.
    /// </summary>
    public EditorViewportKindId kind { get; }

    /// <summary>
    /// Gets ascending model-composition order.
    /// </summary>
    public int order { get; }

    /// <summary>
    /// Gets the priority used to select the viewport interaction controller.
    /// </summary>
    public int controllerPriority { get; }
}

/// <summary>
/// Supplies frame-only Editor interaction and output dimensions to a contributor.
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
    /// Gets host-owned neutral navigation state that the selected controller can map to its camera model.
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
/// selected scene transforms without knowing the controller's camera model.
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
/// Returns one rendering-model contribution without exposing a GPU backend.
/// </summary>
public sealed class EditorViewportContribution
{
    /// <summary>
    /// Creates a viewport model contribution.
    /// </summary>
    /// <param name="data">
    /// Pipeline-defined frame-only data.
    /// </param>
    /// <param name="pipeline">
    /// Contributor-selected pipeline, or null for the project default.
    /// </param>
    /// <param name="targetFormat">
    /// Presentation target format expected by the pipeline.
    /// </param>
    /// <param name="manipulationSpace">
    /// Optional exact view/projection contract for host-owned transform manipulation tools.
    /// </param>
    public EditorViewportContribution(
        RenderFrameData data,
        RenderPipelineAsset? pipeline = null,
        RenderTextureFormat targetFormat = RenderTextureFormat.RGBA8Srgb,
        EditorViewportManipulationSpace? manipulationSpace = null)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        this.pipeline = pipeline;
        this.targetFormat = targetFormat;
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
/// Contributes one rendering model while the host owns viewport composition, targets, and presentation.
/// </summary>
public abstract class EditorViewportContributor
{
    /// <summary>
    /// Creates a parameterless reloadable viewport contributor.
    /// </summary>
    protected EditorViewportContributor()
    {
    }

    /// <summary>
    /// Determines whether this rendering model participates in the supplied viewport content.
    /// </summary>
    /// <param name="context">
    /// Frame-only Editor, navigation, content, and viewport context.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when this contributor can build a valid model contribution.
    /// </returns>
    public abstract bool CanContribute(EditorViewportContext context);

    /// <summary>
    /// Configures neutral host navigation before input is processed and a request is built.
    /// </summary>
    /// <param name="context">
    /// Frame-only Editor, navigation, content, and viewport context.
    /// </param>
    /// <returns>
    /// The controller's current navigation capabilities and optional selection focus bound.
    /// </returns>
    public virtual EditorViewportNavigationProfile ConfigureNavigation(EditorViewportContext context)
        => EditorViewportNavigationProfile.disabled;

    /// <summary>
    /// Builds one model-neutral render contribution for the current frame.
    /// </summary>
    /// <param name="context">
    /// Frame-only Editor and viewport context.
    /// </param>
    /// <returns>
    /// Contributor-selected pipeline and frame data.
    /// </returns>
    public abstract EditorViewportContribution Build(EditorViewportContext context);

    /// <summary>
    /// Draws optional controller-specific toolbar controls when this contributor owns viewport interaction.
    /// </summary>
    /// <param name="context">
    /// Frame-only Editor and viewport context.
    /// </param>
    public virtual void DrawToolbar(EditorViewportContext context)
    {
    }

    /// <summary>
    /// Handles one pointer click when this contributor owns viewport interaction.
    /// </summary>
    /// <param name="context">
    /// Normalized frame-only pointer context.
    /// </param>
    public virtual void HandlePointer(EditorViewportPointerContext context)
    {
    }
}
