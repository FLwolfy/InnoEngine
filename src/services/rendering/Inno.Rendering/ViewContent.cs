using System;
using System.Collections.Generic;
using Inno.Core.Identity;
using Inno.Core.Mathematics;
using Inno.References;

namespace Inno.Rendering;

/// <summary>
/// Describes one backend-neutral view produced by a rendering model.
/// </summary>
public readonly record struct RenderView
{
    /// <summary>
    /// Creates the exact view used to render content into a viewport.
    /// </summary>
    /// <param name="id">
    /// Stable identity within the current output session.
    /// </param>
    /// <param name="viewport">
    /// Destination pixel rectangle.
    /// </param>
    /// <param name="viewMatrix">
    /// World-to-view transform.
    /// </param>
    /// <param name="projectionMatrix">
    /// View-to-clip transform.
    /// </param>
    /// <param name="visibilityMask">
    /// Model-defined visible content bits.
    /// </param>
    public RenderView(
        string id,
        RenderViewport viewport,
        Matrix viewMatrix,
        Matrix projectionMatrix,
        ulong visibilityMask = ulong.MaxValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
        this.viewport = viewport;
        this.viewMatrix = viewMatrix;
        this.projectionMatrix = projectionMatrix;
        this.visibilityMask = visibilityMask;
    }

    /// <summary>
    /// Gets the view identity within its output session.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the destination pixel rectangle.
    /// </summary>
    public RenderViewport viewport { get; }

    /// <summary>
    /// Gets the world-to-view transform.
    /// </summary>
    public Matrix viewMatrix { get; }

    /// <summary>
    /// Gets the view-to-clip transform.
    /// </summary>
    public Matrix projectionMatrix { get; }

    /// <summary>
    /// Gets the model-defined visible content bits.
    /// </summary>
    public ulong visibilityMask { get; }
}

/// <summary>
/// Supplies one content source with the host-selected scene roots and an exact view.
/// </summary>
public sealed class ViewContentContext
{
    /// <summary>
    /// Creates a frame-scoped content collection context.
    /// </summary>
    /// <param name="content">
    /// Ordered roots selected by the host.
    /// </param>
    /// <param name="sessionId">
    /// Stable identity of the host output session.
    /// </param>
    /// <param name="view">
    /// Exact view that will consume the content.
    /// </param>
    /// <param name="frameIndex">
    /// Monotonic output frame number.
    /// </param>
    /// <param name="deltaTime">
    /// Elapsed frame time in seconds.
    /// </param>
    /// <param name="input">
    /// Pointer and keyboard input in output pixel coordinates.
    /// </param>
    /// <param name="views">
    /// All views in the same model output, used to choose shared content resolution.
    /// </param>
    /// <param name="sourceIds">
    /// Optional explicit source allowlist for a routed model layer.
    /// </param>
    public ViewContentContext(ContentReadScope content, string sessionId, RenderView view, ulong frameIndex, float deltaTime,
        RenderOutputInput? input = null, IReadOnlyList<RenderView>? views = null,
        IReadOnlyList<string>? sourceIds = null)
    {
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        this.sessionId = sessionId;
        this.view = view;
        this.frameIndex = frameIndex;
        this.deltaTime = deltaTime;
        this.input = input ?? RenderOutputInput.empty;
        this.views = views ?? [view];
        this.sourceIds = sourceIds;
    }

    /// <summary>
    /// Gets the ordered host-selected content roots.
    /// </summary>
    public ContentReadScope content { get; }

    /// <summary>
    /// Gets the stable host output session identity.
    /// </summary>
    public string sessionId { get; }

    /// <summary>
    /// Gets the exact destination view.
    /// </summary>
    public RenderView view { get; }

    /// <summary>
    /// Gets the monotonic output frame number.
    /// </summary>
    public ulong frameIndex { get; }

    /// <summary>
    /// Gets elapsed time in seconds.
    /// </summary>
    public float deltaTime { get; }
    /// <summary>
    /// Gets frame-local input for this output.
    /// </summary>
    public RenderOutputInput input { get; }
    /// <summary>
    /// Gets every view in the same model output.
    /// </summary>
    public IReadOnlyList<RenderView> views { get; }
    /// <summary>
    /// Gets the exclusive source allowlist; null collects every active source.
    /// </summary>
    public IReadOnlyList<string>? sourceIds { get; }
}

/// <summary>
/// Prepares one model-independent drawable for a specific render pass.
/// </summary>
public interface IViewDrawable
{
    /// <summary>
    /// Resolves reusable resources before render graph execution.
    /// </summary>
    /// <param name="context">
    /// Active pipeline build context.
    /// </param>
    /// <param name="view">
    /// View that will draw this item.
    /// </param>
    /// <param name="prepared">
    /// Prepared frame-local draw when resources are available.
    /// </param>
    /// <returns>
    /// Whether the drawable is ready for this pass.
    /// </returns>
    bool TryPrepare(RenderPipelineContext context, RenderView view, out IPreparedViewDrawable? prepared);
}

/// <summary>
/// Encodes a resource-ready draw into the owning model's scene pass.
/// </summary>
public interface IPreparedViewDrawable
{
    /// <summary>
    /// Encodes commands while the owning model controls attachments and view state.
    /// </summary>
    /// <param name="commands">
    /// Backend-neutral scene pass encoder.
    /// </param>
    void Encode(RenderCommandEncoder commands);
}

/// <summary>
/// Receives pointer input after the rendering model resolves visible draw order.
/// </summary>
public interface IViewPointerTarget
{
    /// <summary>
    /// Gets whether this target retains pointer ownership from an earlier press.
    /// Rendering models route captured input before ordinary hit testing.
    /// </summary>
    bool hasPointerCapture { get; }

    /// <summary>
    /// Gets whether keyboard and text input should continue reaching this target.
    /// </summary>
    bool hasKeyboardFocus { get; }

    /// <summary>
    /// Changes keyboard focus after the rendering model resolves a pointer press.
    /// </summary>
    /// <param name="focused">
    /// Whether this target owns subsequent keyboard input.
    /// </param>
    void SetKeyboardFocus(bool focused);

    /// <summary>
    /// Maps one output pointer onto this item's local surface.
    /// </summary>
    /// <param name="view">
    /// Exact rendered view.
    /// </param>
    /// <param name="input">
    /// Viewport-local input.
    /// </param>
    /// <param name="localPosition">
    /// Local target coordinates when hit.
    /// </param>
    /// <returns>
    /// Whether this target is eligible to receive pointer input.
    /// </returns>
    bool TryHit(RenderView view, RenderOutputInput input, out Vector2 localPosition);

    /// <summary>
    /// Advances this target once for the frame with either routed input or an empty snapshot.
    /// </summary>
    /// <param name="input">
    /// Routed viewport-local input, or an empty snapshot.
    /// </param>
    /// <param name="localPosition">
    /// Target-local pointer coordinates.
    /// </param>
    /// <param name="frameIndex">
    /// Shared output frame index.
    /// </param>
    void Advance(RenderOutputInput input, Vector2 localPosition, ulong frameIndex);
}

/// <summary>
/// One world item supplied to a rendering model without choosing its sort policy.
/// </summary>
public sealed class ViewContentItem
{
    /// <summary>
    /// Creates an item whose lifetime is limited to the current frame.
    /// </summary>
    /// <param name="owner">
    /// Scene object identity used for model-owned sorting and visibility.
    /// </param>
    /// <param name="localToWorld">
    /// Exact transform of its local geometry.
    /// </param>
    /// <param name="localBoundsMin">
    /// Local minimum corner for conservative culling.
    /// </param>
    /// <param name="localBoundsMax">
    /// Local maximum corner for conservative culling.
    /// </param>
    /// <param name="drawable">
    /// Drawable inserted into the model's scene pass.
    /// </param>
    /// <param name="pointerTarget">
    /// Optional input target routed by the rendering model.
    /// </param>
    public ViewContentItem(
        Identity owner,
        Matrix localToWorld,
        Vector3 localBoundsMin,
        Vector3 localBoundsMax,
        IViewDrawable drawable,
        IViewPointerTarget? pointerTarget = null)
    {
        this.owner = owner;
        this.localToWorld = localToWorld;
        this.localBoundsMin = localBoundsMin;
        this.localBoundsMax = localBoundsMax;
        this.drawable = drawable ?? throw new ArgumentNullException(nameof(drawable));
        this.pointerTarget = pointerTarget;
    }

    /// <summary>
    /// Gets the owner identity for model-owned sorting and visibility.
    /// </summary>
    public Identity owner { get; }

    /// <summary>
    /// Gets the local-to-world transform.
    /// </summary>
    public Matrix localToWorld { get; }

    /// <summary>
    /// Gets the local minimum corner.
    /// </summary>
    public Vector3 localBoundsMin { get; }

    /// <summary>
    /// Gets the local maximum corner.
    /// </summary>
    public Vector3 localBoundsMax { get; }

    /// <summary>
    /// Gets the drawable encoded by the selected rendering model.
    /// </summary>
    public IViewDrawable drawable { get; }
    /// <summary>
    /// Gets the optional pointer target associated with this draw item.
    /// </summary>
    public IViewPointerTarget? pointerTarget { get; }
}

/// <summary>
/// Receives world items without imposing a rendering-model sort key.
/// </summary>
public interface IViewContentSink
{
    /// <summary>
    /// Adds an item to the current view.
    /// </summary>
    /// <param name="item">
    /// Frame-local content item.
    /// </param>
    void Submit(ViewContentItem item);
}

/// <summary>
/// Contributes model-independent world items to selected views.
/// </summary>
public interface IViewContentSource : IDisposable
{
    /// <summary>
    /// Collects items for one exact view.
    /// </summary>
    /// <param name="context">
    /// Frame-scoped host content and view.
    /// </param>
    /// <param name="sink">
    /// Collector receiving items.
    /// </param>
    void Collect(ViewContentContext context, IViewContentSink sink);
}

/// <summary>
/// Completes frame-local input after every output has routed its views.
/// </summary>
public interface IViewContentFrameSource
{
    /// <summary>
    /// Advances retained content once after all output views have supplied input.
    /// </summary>
    /// <param name="frameIndex">
    /// The shared render frame whose input routing has completed.
    /// </param>
    void CompleteFrame(ulong frameIndex);
}

/// <summary>
/// Discovers an independently reloadable world-content source.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ViewContentSourceExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a source declaration with a stable identifier.
    /// </summary>
    /// <param name="id">
    /// Globally stable source identifier.
    /// </param>
    public ViewContentSourceExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the globally stable source identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Collects all active world-content sources for one exact view.
/// </summary>
public interface IViewContentCollector
{
    /// <summary>
    /// Collects active source items in deterministic source order.
    /// </summary>
    /// <param name="context">
    /// Frame-scoped host content and view.
    /// </param>
    /// <returns>
    /// Frame-local items; the caller owns their sort and composition.
    /// </returns>
    IReadOnlyList<ViewContentItem> Collect(ViewContentContext context);
}

/// <summary>
/// Restricts a neutral world-content collector to an output layer's source IDs.
/// </summary>
public sealed class SelectedViewContentCollector : IViewContentCollector
{
    private readonly IViewContentCollector m_inner;
    private readonly IReadOnlyList<string> m_sourceIds;

    /// <summary>
    /// Creates a collector that forwards only the selected source identities.
    /// </summary>
    /// <param name="inner">
    /// Active generation's source collector.
    /// </param>
    /// <param name="sourceIds">
    /// Source IDs exclusively assigned to this layer.
    /// </param>
    public SelectedViewContentCollector(IViewContentCollector inner, IReadOnlyList<string> sourceIds)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        m_sourceIds = sourceIds ?? throw new ArgumentNullException(nameof(sourceIds));
    }

    /// <summary>
    /// Collects only the assigned world-content sources for one exact view.
    /// </summary>
    /// <param name="context">
    /// The original model view, output input, and frame timing.
    /// </param>
    /// <returns>
    /// Frame-local content emitted by the assigned sources.
    /// </returns>
    public IReadOnlyList<ViewContentItem> Collect(ViewContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return m_inner.Collect(new ViewContentContext(context.content, context.sessionId,
            context.view, context.frameIndex, context.deltaTime, context.input,
            context.views, m_sourceIds));
    }
}
