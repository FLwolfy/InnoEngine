using System;
using System.Collections.Generic;
using Inno.Core.Serialization;
using Inno.Rendering;

namespace Inno.Rendering;

/// <summary>
/// Marks a reloadable render pipeline implementation with a stable extension identifier.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RenderPipelineExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a pipeline extension declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable pipeline extension identifier.
    /// </param>
    public RenderPipelineExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the globally stable pipeline extension identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Marks a reloadable pipeline feature implementation with a stable extension identifier.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RenderFeatureExtensionAttribute : Attribute
{
    /// <summary>
    /// Creates a feature extension declaration.
    /// </summary>
    /// <param name="id">
    /// Globally stable feature extension identifier.
    /// </param>
    public RenderFeatureExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>
    /// Gets the globally stable feature extension identifier.
    /// </summary>
    public string id { get; }
}

/// <summary>
/// Indicates the impact of a rendering diagnostic.
/// </summary>
public enum RenderDiagnosticSeverity
{
    /// <summary>
    /// Provides non-blocking information.
    /// </summary>
    Info,
    /// <summary>
    /// Reports a capability reduction or recoverable problem.
    /// </summary>
    Warning,
    /// <summary>
    /// Reports a failed request, extension, pass, or resource.
    /// </summary>
    Error
}

/// <summary>
/// Reports one structured rendering problem without backend-native data.
/// </summary>
public sealed class RenderDiagnostic
{
    /// <summary>
    /// Creates a rendering diagnostic.
    /// </summary>
    /// <param name="code">
    /// Stable machine-readable code.
    /// </param>
    /// <param name="message">
    /// Actionable user-facing message.
    /// </param>
    /// <param name="severity">
    /// Diagnostic impact.
    /// </param>
    /// <param name="sourceId">
    /// Optional stable asset or extension identity.
    /// </param>
    public RenderDiagnostic(
        string code,
        string message,
        RenderDiagnosticSeverity severity,
        string? sourceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        this.code = code;
        this.message = message;
        this.severity = severity;
        this.sourceId = sourceId;
    }

    /// <summary>
    /// Gets the stable diagnostic code.
    /// </summary>
    public string code { get; }

    /// <summary>
    /// Gets the actionable message.
    /// </summary>
    public string message { get; }

    /// <summary>
    /// Gets diagnostic impact.
    /// </summary>
    public RenderDiagnosticSeverity severity { get; }

    /// <summary>
    /// Gets an optional stable source identity.
    /// </summary>
    public string? sourceId { get; }
}

/// <summary>
/// Receives deduplicated rendering diagnostics.
/// </summary>
public interface IRenderDiagnosticSink
{
    /// <summary>
    /// Publishes current diagnostic state.
    /// </summary>
    /// <param name="diagnostic">
    /// Diagnostic to publish.
    /// </param>
    void Publish(RenderDiagnostic diagnostic);

    /// <summary>
    /// Resolves one previously published diagnostic when its underlying condition no longer exists.
    /// </summary>
    /// <param name="code">
    /// The stable machine-readable code of the diagnostic to resolve.
    /// </param>
    /// <param name="sourceId">
    /// The same optional source identity used when the diagnostic was published.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="code"/> is empty or contains only whitespace.
    /// </exception>
    void Resolve(string code, string? sourceId = null);
}

/// <summary>
/// Stores open semantic graph resources for one pipeline request.
/// </summary>
public sealed class RenderResourceRegistry
{
    private readonly Dictionary<RenderResourceId, RenderTextureHandle> m_textures = [];
    private readonly Dictionary<RenderResourceId, RenderBufferHandle> m_buffers = [];

    /// <summary>
    /// Publishes a texture under a pipeline-defined semantic identifier.
    /// </summary>
    /// <param name="id">
    /// Open semantic resource identifier.
    /// </param>
    /// <param name="texture">
    /// Valid current-graph texture.
    /// </param>
    public void PublishTexture(RenderResourceId id, RenderTextureHandle texture)
    {
        if (!id.isValid)
            throw new ArgumentException("A render resource identifier must be valid.", nameof(id));
        if (!texture.isValid)
            throw new ArgumentException("A published texture must be valid.", nameof(texture));
        m_textures[id] = texture;
    }

    /// <summary>
    /// Publishes a buffer under a pipeline-defined semantic identifier.
    /// </summary>
    /// <param name="id">
    /// Open semantic resource identifier.
    /// </param>
    /// <param name="buffer">
    /// Valid current-graph buffer.
    /// </param>
    public void PublishBuffer(RenderResourceId id, RenderBufferHandle buffer)
    {
        if (!id.isValid)
            throw new ArgumentException("A render resource identifier must be valid.", nameof(id));
        if (!buffer.isValid)
            throw new ArgumentException("A published buffer must be valid.", nameof(buffer));
        m_buffers[id] = buffer;
    }

    /// <summary>
    /// Tries to get a published texture.
    /// </summary>
    /// <param name="id">
    /// Open semantic resource identifier.
    /// </param>
    /// <param name="texture">
    /// Receives the current-graph texture.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the texture has been published.
    /// </returns>
    public bool TryGetTexture(RenderResourceId id, out RenderTextureHandle texture)
        => m_textures.TryGetValue(id, out texture);

    /// <summary>
    /// Tries to get a published buffer.
    /// </summary>
    /// <param name="id">
    /// Open semantic resource identifier.
    /// </param>
    /// <param name="buffer">
    /// Receives the current-graph buffer.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the buffer has been published.
    /// </returns>
    public bool TryGetBuffer(RenderResourceId id, out RenderBufferHandle buffer)
        => m_buffers.TryGetValue(id, out buffer);
}

/// <summary>
/// Supplies one request and frame-scoped services to a render pipeline.
/// </summary>
public sealed class RenderPipelineContext
{
    /// <summary>
    /// Creates a pipeline build context.
    /// </summary>
    /// <param name="request">
    /// Pipeline-defined render request.
    /// </param>
    /// <param name="pipelineAsset">
    /// Selected persistent pipeline configuration.
    /// </param>
    /// <param name="graph">
    /// Current frame graph builder.
    /// </param>
    /// <param name="capabilities">
    /// Current device capability snapshot.
    /// </param>
    /// <param name="resources">
    /// Open semantic resource registry.
    /// </param>
    /// <param name="diagnostics">
    /// Structured diagnostic sink.
    /// </param>
    /// <param name="resourceService">
    /// Generation-aware shader, material and persistent GPU resource service.
    /// </param>
    /// <param name="uploads">
    /// Frame-scoped streaming buffer service.
    /// </param>
    /// <param name="frameIndex">
    /// Monotonic render frame index.
    /// </param>
    /// <param name="outputTexture">
    /// Imported offscreen target, or an invalid handle for the backbuffer.
    /// </param>
    public RenderPipelineContext(
        RenderRequest request,
        RenderPipelineAsset pipelineAsset,
        RenderGraphBuilder graph,
        GraphicsCapabilities capabilities,
        RenderResourceRegistry resources,
        IRenderDiagnosticSink diagnostics,
        IRenderResourceService resourceService,
        IRenderFrameUploadService uploads,
        ulong frameIndex,
        RenderTextureHandle outputTexture = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pipelineAsset);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(resourceService);
        ArgumentNullException.ThrowIfNull(uploads);
        this.request = request;
        this.pipelineAsset = pipelineAsset;
        this.graph = graph;
        this.capabilities = capabilities;
        this.resources = resources;
        this.diagnostics = diagnostics;
        this.resourceService = resourceService;
        this.uploads = uploads;
        this.frameIndex = frameIndex;
        this.outputTexture = outputTexture;
    }

    /// <summary>
    /// Gets the current request.
    /// </summary>
    public RenderRequest request { get; }

    /// <summary>
    /// Gets selected pipeline configuration.
    /// </summary>
    public RenderPipelineAsset pipelineAsset { get; }

    /// <summary>
    /// Gets the current frame graph builder.
    /// </summary>
    public RenderGraphBuilder graph { get; }

    /// <summary>
    /// Gets current device capabilities.
    /// </summary>
    public GraphicsCapabilities capabilities { get; }

    /// <summary>
    /// Gets open semantic resources for this request.
    /// </summary>
    public RenderResourceRegistry resources { get; }

    /// <summary>
    /// Gets the structured diagnostic sink.
    /// </summary>
    public IRenderDiagnosticSink diagnostics { get; }

    /// <summary>
    /// Gets the generation-aware neutral GPU resource service.
    /// </summary>
    public IRenderResourceService resourceService { get; }

    /// <summary>
    /// Gets the frame-scoped streaming buffer service.
    /// </summary>
    public IRenderFrameUploadService uploads { get; }

    /// <summary>
    /// Gets the monotonic render frame index.
    /// </summary>
    public ulong frameIndex { get; }

    /// <summary>
    /// Gets the imported offscreen target, or an invalid handle for the backbuffer.
    /// </summary>
    public RenderTextureHandle outputTexture { get; }
}

/// <summary>
/// Supplies one configured feature with frame-scoped graph services.
/// </summary>
public sealed class RenderFeatureContext
{
    /// <summary>
    /// Creates a feature build context.
    /// </summary>
    /// <param name="pipeline">
    /// Owning pipeline context.
    /// </param>
    /// <param name="configuration">
    /// Stable feature configuration.
    /// </param>
    public RenderFeatureContext(RenderPipelineContext pipeline, RenderFeatureConfiguration configuration)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        this.configuration = configuration;
    }

    /// <summary>
    /// Gets the owning pipeline context.
    /// </summary>
    public RenderPipelineContext pipeline { get; }

    /// <summary>
    /// Gets stable feature configuration.
    /// </summary>
    public RenderFeatureConfiguration configuration { get; }

    /// <summary>
    /// Gets the current frame graph builder.
    /// </summary>
    public RenderGraphBuilder graph => pipeline.graph;

    /// <summary>
    /// Gets open semantic resources for the request.
    /// </summary>
    public RenderResourceRegistry resources => pipeline.resources;

    /// <summary>
    /// Gets current device capabilities.
    /// </summary>
    public GraphicsCapabilities capabilities => pipeline.capabilities;

    /// <summary>
    /// Gets the generation-aware neutral GPU resource service.
    /// </summary>
    public IRenderResourceService resourceService => pipeline.resourceService;

    /// <summary>
    /// Gets the frame-scoped streaming buffer service.
    /// </summary>
    public IRenderFrameUploadService uploads => pipeline.uploads;
}

/// <summary>
/// Builds frame-local passes without prescribing a rendering model.
/// </summary>
public abstract class RenderPipeline : IDisposable
{
    private bool m_disposed;

    /// <summary>
    /// Applies reload-safe pipeline settings to this generation.
    /// </summary>
    /// <param name="state">
    /// Stable type identity and neutral property bytes.
    /// </param>
    public void Configure(SerializedRenderExtensionState state)
    {
        OnConfigure(state);
    }

    /// <summary>
    /// Builds all passes for one request.
    /// </summary>
    /// <param name="context">
    /// Frame-scoped pipeline context.
    /// </param>
    public abstract void Build(RenderPipelineContext context);

    /// <summary>
    /// Releases generation-scoped pipeline state.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads pipeline-owned settings from neutral state.
    /// </summary>
    /// <param name="state">
    /// Reload-safe extension state.
    /// </param>
    protected virtual void OnConfigure(SerializedRenderExtensionState state) { }

    /// <summary>
    /// Releases managed generation-scoped state.
    /// </summary>
    /// <param name="disposing">
    /// Always true for explicit disposal.
    /// </param>
    protected virtual void Dispose(bool disposing) { }
}

/// <summary>
/// Adds capability-aware passes without owning frame graph state.
/// </summary>
public abstract class RenderPipelineFeature
{
    /// <summary>
    /// Applies reload-safe settings to this feature generation.
    /// </summary>
    /// <param name="configuration">
    /// Stable feature configuration.
    /// </param>
    public void Configure(RenderFeatureConfiguration configuration)
    {
        OnConfigure(configuration.state);
    }

    /// <summary>
    /// Adds frame-scoped passes and dependencies.
    /// </summary>
    /// <param name="context">
    /// Frame-scoped feature context.
    /// </param>
    public abstract void AddRenderPasses(RenderFeatureContext context);

    /// <summary>
    /// Reads feature-owned settings from neutral state.
    /// </summary>
    /// <param name="state">
    /// Reload-safe extension state.
    /// </param>
    protected virtual void OnConfigure(SerializedRenderExtensionState state) { }
}
