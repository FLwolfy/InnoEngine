using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Inno.References;

namespace Inno.Rendering;

/// <summary>
/// Declares a host output without naming a camera or scene model.
/// </summary>
public sealed class RenderOutputSession
{
    /// <summary>
    /// Creates a frame-scoped output session.
    /// </summary>
    /// <param name="id">
    /// Stable host output identity.
    /// </param>
    /// <param name="content">
    /// Host-selected content roots.
    /// </param>
    /// <param name="viewport">
    /// Pixel rectangle within the target.
    /// </param>
    /// <param name="frameIndex">
    /// Shared output frame index.
    /// </param>
    /// <param name="deltaTime">
    /// Elapsed frame time in seconds.
    /// </param>
    /// <param name="viewContent">
    /// Model-independent world content collector.
    /// </param>
    /// <param name="input">
    /// Viewport-local input.
    /// </param>
    /// <param name="route">
    /// Explicit composition route when several models are applicable.
    /// </param>
    public RenderOutputSession(string id, ContentReadScope content, RenderViewport viewport,
        ulong frameIndex, float deltaTime, IViewContentCollector viewContent,
        RenderOutputInput? input = null, RenderOutputRoute? route = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        this.viewport = viewport;
        this.frameIndex = frameIndex;
        this.deltaTime = deltaTime;
        this.viewContent = viewContent ?? throw new ArgumentNullException(nameof(viewContent));
        this.input = input ?? RenderOutputInput.empty;
        this.route = route;
    }

    /// <summary>
    /// Gets the stable host output identity.
    /// </summary>
    public string id { get; }
    /// <summary>
    /// Gets selected content roots.
    /// </summary>
    public ContentReadScope content { get; }
    /// <summary>
    /// Gets the output pixel viewport.
    /// </summary>
    public RenderViewport viewport { get; }
    /// <summary>
    /// Gets the shared output frame index.
    /// </summary>
    public ulong frameIndex { get; }
    /// <summary>
    /// Gets elapsed frame time.
    /// </summary>
    public float deltaTime { get; }
    /// <summary>
    /// Gets the world content collector.
    /// </summary>
    public IViewContentCollector viewContent { get; }
    /// <summary>
    /// Gets viewport-local input.
    /// </summary>
    public RenderOutputInput input { get; }
    /// <summary>
    /// Gets the explicit model route, if configured.
    /// </summary>
    public RenderOutputRoute? route { get; }

    /// <summary>
    /// Creates the session seen by one explicitly assigned model layer.
    /// </summary>
    /// <param name="layer">
    /// The model and content-source assignment.
    /// </param>
    /// <returns>
    /// A session with only the assigned world-content sources.
    /// </returns>
    public RenderOutputSession ForLayer(RenderOutputLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new RenderOutputSession(id, content, viewport, frameIndex, deltaTime,
            new SelectedViewContentCollector(viewContent, layer.sourceIds), input);
    }
}

/// <summary>
/// Assigns exactly one rendering model and its world-content sources to an output layer.
/// </summary>
public sealed class RenderOutputLayer
{
    /// <summary>
    /// Creates a model layer with explicit, distinct content-source IDs.
    /// </summary>
    /// <param name="modelId">
    /// Stable render-model or Editor contributor ID.
    /// </param>
    /// <param name="sourceIds">
    /// World-content sources exclusively owned by this layer.
    /// </param>
    public RenderOutputLayer(string modelId, IEnumerable<string> sourceIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(sourceIds);
        string[] ids = sourceIds.ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("A layer requires distinct non-empty source IDs.", nameof(sourceIds));
        this.modelId = modelId;
        this.sourceIds = new ReadOnlyCollection<string>(ids);
    }

    /// <summary>
    /// Gets the exact rendering model ID.
    /// </summary>
    public string modelId { get; }
    /// <summary>
    /// Gets the assigned world-content source IDs.
    /// </summary>
    public IReadOnlyList<string> sourceIds { get; }
}

/// <summary>
/// States the exact model order and exclusive world-content assignment for one output.
/// </summary>
public sealed class RenderOutputRoute
{
    /// <summary>
    /// Creates a route from explicitly assigned model layers in draw order.
    /// </summary>
    /// <param name="layers">
    /// Exact model identities and exclusive source assignments in draw order.
    /// </param>
    public RenderOutputRoute(IEnumerable<RenderOutputLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        RenderOutputLayer[] values = layers.ToArray();
        if (values.Length == 0 || values.Any(static layer => layer is null)
            || values.Select(static layer => layer.modelId).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("A route requires distinct model layers.", nameof(layers));
        string[] sourceIds = values.SelectMany(static layer => layer.sourceIds).ToArray();
        if (sourceIds.Distinct(StringComparer.Ordinal).Count() != sourceIds.Length)
            throw new ArgumentException("Each world-content source may be assigned to only one model layer.", nameof(layers));
        this.layers = new ReadOnlyCollection<RenderOutputLayer>(values);
    }

    /// <summary>
    /// Gets exact model and source assignments in draw order.
    /// </summary>
    public IReadOnlyList<RenderOutputLayer> layers { get; }
}

/// <summary>
/// Gets one model's prepared pipeline data for a host-owned target.
/// </summary>
public sealed class RenderModelOutput
{
    /// <summary>
    /// Creates prepared model output.
    /// </summary>
    /// <param name="name">
    /// Frame-local diagnostic name.
    /// </param>
    /// <param name="pipeline">
    /// Exact model pipeline.
    /// </param>
    /// <param name="data">
    /// Immutable model frame data.
    /// </param>
    /// <param name="targetFormat">
    /// Required color target format.
    /// </param>
    public RenderModelOutput(string name, RenderPipelineAsset pipeline,
        RenderFrameData data, RenderTextureFormat targetFormat = RenderTextureFormat.RGBA8Srgb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.name = name;
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        this.data = data?.Snapshot() ?? throw new ArgumentNullException(nameof(data));
        this.targetFormat = targetFormat;
    }

    /// <summary>
    /// Gets the diagnostic name.
    /// </summary>
    public string name { get; }
    /// <summary>
    /// Gets the model's pipeline.
    /// </summary>
    public RenderPipelineAsset pipeline { get; }
    /// <summary>
    /// Gets model frame data.
    /// </summary>
    public RenderFrameData data { get; }
    /// <summary>
    /// Gets required target format.
    /// </summary>
    public RenderTextureFormat targetFormat { get; }
}

/// <summary>
/// Builds view requests from host output sessions without owning a host window.
/// </summary>
public interface IRenderModel : IDisposable
{
    /// <summary>
    /// Determines whether this model accepts the session's content.
    /// </summary>
    /// <param name="session">
    /// Host output session.
    /// </param>
    /// <returns>
    /// Whether the model can render the selected content.
    /// </returns>
    bool CanRender(RenderOutputSession session);

    /// <summary>
    /// Builds one model output after acceptance.
    /// </summary>
    /// <param name="session">
    /// Host output session.
    /// </param>
    /// <returns>
    /// Prepared frame data and pipeline.
    /// </returns>
    RenderModelOutput Build(RenderOutputSession session);
}

/// <summary>
/// Marks a reloadable rendering model with a stable identity.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RenderModelExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a model declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable model identity.
    /// </param>
    public RenderModelExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the globally stable model identity.
    /// </summary>
    public string id { get; }
}
