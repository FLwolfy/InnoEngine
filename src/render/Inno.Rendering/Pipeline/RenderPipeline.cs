using System;
using System.Collections.Generic;
using System.Text.Json;
using Inno.Core.Mathematics;
using Inno.Rendering.Core;

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
    /// <param name="id">Globally stable pipeline extension identifier.</param>
    public RenderPipelineExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>Gets the globally stable pipeline extension identifier.</summary>
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
    /// <param name="id">Globally stable feature extension identifier.</param>
    public RenderFeatureExtensionAttribute(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.id = id;
    }

    /// <summary>Gets the globally stable feature extension identifier.</summary>
    public string id { get; }
}

/// <summary>
/// Indicates the impact of a rendering diagnostic.
/// </summary>
public enum RenderDiagnosticSeverity
{
    /// <summary>Provides non-blocking information.</summary>
    Info,
    /// <summary>Reports a capability fallback or recoverable issue.</summary>
    Warning,
    /// <summary>Reports a failed request, feature, pass or artifact.</summary>
    Error
}

/// <summary>
/// Reports one structured rendering problem without a backend-native payload.
/// </summary>
public sealed class RenderDiagnostic
{
    /// <summary>
    /// Creates a rendering diagnostic.
    /// </summary>
    /// <param name="code">Stable machine-readable code.</param>
    /// <param name="message">Actionable artist-facing message.</param>
    /// <param name="severity">Diagnostic impact.</param>
    /// <param name="sourceId">Optional asset, pass, feature or node Stable ID.</param>
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

    /// <summary>Gets the stable machine-readable code.</summary>
    public string code { get; }

    /// <summary>Gets the actionable artist-facing message.</summary>
    public string message { get; }

    /// <summary>Gets diagnostic impact.</summary>
    public RenderDiagnosticSeverity severity { get; }

    /// <summary>Gets an optional asset, pass, feature or node Stable ID.</summary>
    public string? sourceId { get; }
}

/// <summary>
/// Receives deduplicated rendering diagnostics from pipelines and features.
/// </summary>
public interface IRenderDiagnosticSink
{
    /// <summary>
    /// Publishes current rendering state for one stable diagnostic code and source.
    /// </summary>
    /// <param name="diagnostic">Diagnostic to publish.</param>
    void Publish(RenderDiagnostic diagnostic);
}

/// <summary>
/// Distinguishes built-in scene, fullscreen and compute work without exposing backend commands.
/// </summary>
public enum RenderPipelineOperationKind
{
    /// <summary>Draws a material pass for a sorted render-object list.</summary>
    Scene,
    /// <summary>Draws one backend-neutral fullscreen operation.</summary>
    Fullscreen,
    /// <summary>Dispatches one backend-neutral compute operation.</summary>
    Compute
}

/// <summary>
/// Binds one frame-graph texture to a stable shader interface name.
/// </summary>
public readonly record struct RenderTextureBinding
{
    /// <summary>
    /// Creates a texture binding.
    /// </summary>
    /// <param name="binding">Stable shader interface binding.</param>
    /// <param name="texture">Frame-scoped graph texture.</param>
    public RenderTextureBinding(RenderBindingId binding, RenderTextureHandle texture)
    {
        if (!texture.isValid)
        {
            throw new ArgumentException("A pipeline texture binding requires a valid graph handle.", nameof(texture));
        }

        this.binding = binding;
        this.texture = texture;
    }

    /// <summary>Gets the stable shader interface binding.</summary>
    public RenderBindingId binding { get; }

    /// <summary>Gets the frame-scoped graph texture.</summary>
    public RenderTextureHandle texture { get; }
}

/// <summary>
/// Binds one frame-graph buffer to a stable shader interface name.
/// </summary>
public readonly record struct RenderBufferBinding
{
    /// <summary>
    /// Creates a buffer binding.
    /// </summary>
    /// <param name="binding">Stable shader interface binding.</param>
    /// <param name="buffer">Frame-scoped graph buffer.</param>
    public RenderBufferBinding(RenderBindingId binding, RenderBufferHandle buffer)
    {
        if (!buffer.isValid)
        {
            throw new ArgumentException("A pipeline buffer binding requires a valid graph handle.", nameof(buffer));
        }

        this.binding = binding;
        this.buffer = buffer;
    }

    /// <summary>Gets the stable shader interface binding.</summary>
    public RenderBindingId binding { get; }

    /// <summary>Gets the frame-scoped graph buffer.</summary>
    public RenderBufferHandle buffer { get; }
}

/// <summary>
/// Binds one typed uniform value to a stable shader interface name.
/// </summary>
public sealed class RenderUniformBinding
{
    private readonly float[] m_values;

    /// <summary>
    /// Creates a four-component uniform binding.
    /// </summary>
    /// <param name="binding">Stable shader interface binding.</param>
    /// <param name="value">Four-component value copied into the frame operation.</param>
    public RenderUniformBinding(RenderBindingId binding, Vector4 value)
    {
        if (!binding.isValid)
        {
            throw new ArgumentException("A uniform binding requires a stable shader interface name.", nameof(binding));
        }

        this.binding = binding;
        m_values = [value.x, value.y, value.z, value.w];
    }

    /// <summary>
    /// Creates a column-major four-by-four matrix uniform binding.
    /// </summary>
    /// <param name="binding">Stable shader interface binding.</param>
    /// <param name="value">Matrix value copied into the frame operation.</param>
    public RenderUniformBinding(RenderBindingId binding, Matrix value)
    {
        if (!binding.isValid)
        {
            throw new ArgumentException("A uniform binding requires a stable shader interface name.", nameof(binding));
        }

        this.binding = binding;
        m_values = value.ToColumnMajorArray();
    }

    /// <summary>Gets the stable shader interface binding.</summary>
    public RenderBindingId binding { get; }

    /// <summary>Gets four vector values or sixteen column-major matrix values.</summary>
    public ReadOnlyMemory<float> values => m_values;
}

/// <summary>
/// Carries backend-neutral cascaded directional-shadow transforms for one camera operation.
/// </summary>
public sealed class DirectionalShadowData
{
    private readonly IReadOnlyList<Matrix> m_worldToShadowMatrices;
    private readonly IReadOnlyList<float> m_cascadeSplits;

    /// <summary>
    /// Creates immutable cascaded directional-shadow data.
    /// </summary>
    /// <param name="worldToShadowMatrices">One light clip-space transform per cascade.</param>
    /// <param name="cascadeSplits">Ascending camera-view distances ending each cascade.</param>
    /// <param name="strength">Normalized shadow contribution.</param>
    /// <param name="depthBias">Positive receiver depth bias in normalized shadow depth.</param>
    /// <param name="texelSize">Reciprocal shadow-map layer resolution used by filtering.</param>
    public DirectionalShadowData(
        IReadOnlyList<Matrix> worldToShadowMatrices,
        IReadOnlyList<float> cascadeSplits,
        float strength,
        float depthBias,
        float texelSize)
    {
        ArgumentNullException.ThrowIfNull(worldToShadowMatrices);
        ArgumentNullException.ThrowIfNull(cascadeSplits);
        if (worldToShadowMatrices.Count is < 1 or > 4
            || worldToShadowMatrices.Count != cascadeSplits.Count)
        {
            throw new ArgumentException(
                "Directional shadows require one through four matching matrices and split distances.",
                nameof(worldToShadowMatrices));
        }

        float previous = 0f;
        foreach (float split in cascadeSplits)
        {
            if (!float.IsFinite(split) || split <= previous)
            {
                throw new ArgumentException(
                    "Directional shadow split distances must be finite, positive and strictly ascending.",
                    nameof(cascadeSplits));
            }

            previous = split;
        }

        if (!float.IsFinite(depthBias) || depthBias < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(depthBias));
        }

        if (!float.IsFinite(texelSize) || texelSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(texelSize));
        }

        m_worldToShadowMatrices = [.. worldToShadowMatrices];
        m_cascadeSplits = [.. cascadeSplits];
        this.strength = Math.Clamp(strength, 0f, 1f);
        this.depthBias = depthBias;
        this.texelSize = texelSize;
    }

    /// <summary>Gets one light clip-space transform per cascade.</summary>
    public IReadOnlyList<Matrix> worldToShadowMatrices => m_worldToShadowMatrices;

    /// <summary>Gets ascending camera-view distances ending each cascade.</summary>
    public IReadOnlyList<float> cascadeSplits => m_cascadeSplits;

    /// <summary>Gets the number of populated cascades.</summary>
    public int cascadeCount => m_worldToShadowMatrices.Count;

    /// <summary>Gets normalized shadow contribution.</summary>
    public float strength { get; }

    /// <summary>Gets receiver depth bias in normalized shadow depth.</summary>
    public float depthBias { get; }

    /// <summary>Gets reciprocal shadow-map layer resolution used by filtering.</summary>
    public float texelSize { get; }
}

/// <summary>
/// Describes one built-in pipeline operation using only frame-scoped and neutral data.
/// </summary>
public sealed class RenderPipelineOperation
{
    private readonly IReadOnlyList<RenderObjectData> m_objects;
    private readonly IReadOnlyList<RenderLightData> m_lights;
    private readonly IReadOnlyList<RenderTextureBinding> m_textures;
    private readonly IReadOnlyList<RenderBufferBinding> m_buffers;
    private readonly IReadOnlyList<RenderUniformBinding> m_uniforms;

    /// <summary>
    /// Creates a pipeline operation payload.
    /// </summary>
    /// <param name="id">Stable built-in or extension operation identifier.</param>
    /// <param name="kind">Operation command domain.</param>
    /// <param name="view">Immutable camera view.</param>
    /// <param name="shaderPassTag">Open material pass tag for scene draws.</param>
    /// <param name="objects">Sorted scene objects consumed by the operation.</param>
    /// <param name="lights">Frame lights consumed by the operation.</param>
    /// <param name="textures">Explicit shader texture bindings.</param>
    /// <param name="buffers">Explicit shader buffer bindings.</param>
    /// <param name="dispatchX">Compute workgroup count on X.</param>
    /// <param name="dispatchY">Compute workgroup count on Y.</param>
    /// <param name="dispatchZ">Compute workgroup count on Z.</param>
    /// <param name="subpassIndex">Optional cascade, mip or face index.</param>
    /// <param name="scalarParameter">Optional operation-specific scalar such as exposure.</param>
    /// <param name="directionalShadow">Optional camera-relative directional-shadow transforms.</param>
    /// <param name="uniforms">Explicit typed uniform bindings for extension operations.</param>
    public RenderPipelineOperation(
        string id,
        RenderPipelineOperationKind kind,
        RenderView view,
        string? shaderPassTag = null,
        IReadOnlyList<RenderObjectData>? objects = null,
        IReadOnlyList<RenderLightData>? lights = null,
        IReadOnlyList<RenderTextureBinding>? textures = null,
        IReadOnlyList<RenderBufferBinding>? buffers = null,
        int dispatchX = 0,
        int dispatchY = 0,
        int dispatchZ = 0,
        int subpassIndex = -1,
        float scalarParameter = 0f,
        DirectionalShadowData? directionalShadow = null,
        IReadOnlyList<RenderUniformBinding>? uniforms = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(view);
        if (kind == RenderPipelineOperationKind.Scene)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(shaderPassTag);
        }

        if (kind == RenderPipelineOperationKind.Compute
            && (dispatchX <= 0 || dispatchY <= 0 || dispatchZ <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(dispatchX), "Compute workgroup counts must be positive.");
        }

        this.id = id;
        this.kind = kind;
        this.view = view;
        this.shaderPassTag = shaderPassTag;
        m_objects = objects ?? Array.Empty<RenderObjectData>();
        m_lights = lights ?? Array.Empty<RenderLightData>();
        m_textures = textures ?? Array.Empty<RenderTextureBinding>();
        m_buffers = buffers ?? Array.Empty<RenderBufferBinding>();
        this.dispatchX = dispatchX;
        this.dispatchY = dispatchY;
        this.dispatchZ = dispatchZ;
        this.subpassIndex = subpassIndex;
        this.scalarParameter = scalarParameter;
        this.directionalShadow = directionalShadow;
        m_uniforms = uniforms ?? Array.Empty<RenderUniformBinding>();
    }

    /// <summary>Gets the stable operation identifier.</summary>
    public string id { get; }

    /// <summary>Gets the operation command domain.</summary>
    public RenderPipelineOperationKind kind { get; }

    /// <summary>Gets the immutable camera view.</summary>
    public RenderView view { get; }

    /// <summary>Gets the open material pass tag for scene draws.</summary>
    public string? shaderPassTag { get; }

    /// <summary>Gets sorted scene objects consumed by this operation.</summary>
    public IReadOnlyList<RenderObjectData> objects => m_objects;

    /// <summary>Gets frame lights consumed by this operation.</summary>
    public IReadOnlyList<RenderLightData> lights => m_lights;

    /// <summary>Gets explicit shader texture bindings.</summary>
    public IReadOnlyList<RenderTextureBinding> textures => m_textures;

    /// <summary>Gets explicit shader buffer bindings.</summary>
    public IReadOnlyList<RenderBufferBinding> buffers => m_buffers;

    /// <summary>Gets compute workgroup count on X.</summary>
    public int dispatchX { get; }

    /// <summary>Gets compute workgroup count on Y.</summary>
    public int dispatchY { get; }

    /// <summary>Gets compute workgroup count on Z.</summary>
    public int dispatchZ { get; }

    /// <summary>Gets an optional cascade, mip or face index.</summary>
    public int subpassIndex { get; }

    /// <summary>Gets an optional operation-specific scalar such as exposure.</summary>
    public float scalarParameter { get; }

    /// <summary>Gets optional camera-relative directional-shadow transforms.</summary>
    public DirectionalShadowData? directionalShadow { get; }

    /// <summary>Gets explicit typed uniform bindings for extension operations.</summary>
    public IReadOnlyList<RenderUniformBinding> uniforms => m_uniforms;
}

/// <summary>
/// Translates built-in pipeline operations into cached shaders and neutral command-encoder calls.
/// </summary>
public interface IRenderPipelineExecutor
{
    /// <summary>Applies queued resource changes at the beginning of one graphics frame.</summary>
    /// <param name="frameIndex">Monotonic engine render-frame index.</param>
    void PrepareFrame(ulong frameIndex);

    /// <summary>
    /// Makes a persistent offscreen target resident and imports it into the current frame graph.
    /// </summary>
    /// <param name="graph">Current frame graph that will write the target.</param>
    /// <param name="target">Backend-neutral persistent target description.</param>
    /// <returns>A frame-scoped handle for the target's current device resource.</returns>
    RenderTextureHandle ImportTarget(RenderGraphBuilder graph, RenderTexture target);

    /// <summary>
    /// Tries to resolve the current neutral device texture for an already-resident target.
    /// </summary>
    /// <param name="target">Backend-neutral persistent target description.</param>
    /// <param name="texture">Current device-generation handle when resident.</param>
    /// <returns><see langword="true"/> when the target has a current resident resource.</returns>
    bool TryGetTargetTexture(RenderTexture target, out PersistentTextureHandle texture);

    /// <summary>Queues an offscreen target for release at the next graphics frame safety point.</summary>
    /// <param name="target">Target whose current device resource is no longer needed.</param>
    void ReleaseTarget(RenderTexture target);

    /// <summary>
    /// Resolves and creates operation resources at the current frame safety point.
    /// </summary>
    /// <param name="operation">Neutral operation that will execute after graph compilation.</param>
    void Prepare(RenderPipelineOperation operation);

    /// <summary>
    /// Records one frame-scoped operation without advancing or presenting the graphics frame.
    /// </summary>
    /// <param name="operation">Neutral operation payload.</param>
    /// <param name="context">Pass-scoped command encoder.</param>
    void Execute(RenderPipelineOperation operation, RenderPassContext context);
}

/// <summary>
/// Exposes standard frame resources through typed properties instead of string blackboards.
/// </summary>
public sealed class BuiltinRenderResources
{
    private RenderTextureHandle m_sceneColor;
    private RenderTextureHandle m_sceneDepth;
    private RenderTextureHandle m_shadowAtlas;
    private RenderTextureHandle m_gBuffer0;
    private RenderTextureHandle m_gBuffer1;
    private RenderTextureHandle m_gBuffer2;
    private RenderTextureHandle m_picking;
    private RenderTextureHandle m_cameraTarget;

    /// <summary>Gets the current scene color, or an invalid handle before publication.</summary>
    public RenderTextureHandle sceneColor => m_sceneColor;

    /// <summary>Gets the current scene depth, or an invalid handle before publication.</summary>
    public RenderTextureHandle sceneDepth => m_sceneDepth;

    /// <summary>Gets the directional shadow atlas, or an invalid handle when unavailable.</summary>
    public RenderTextureHandle shadowAtlas => m_shadowAtlas;

    /// <summary>Gets BaseColor/Metallic GBuffer data, or an invalid handle outside Deferred.</summary>
    public RenderTextureHandle gBuffer0 => m_gBuffer0;

    /// <summary>Gets Normal/Roughness GBuffer data, or an invalid handle outside Deferred.</summary>
    public RenderTextureHandle gBuffer1 => m_gBuffer1;

    /// <summary>Gets Emissive/AO GBuffer data, or an invalid handle outside Deferred.</summary>
    public RenderTextureHandle gBuffer2 => m_gBuffer2;

    /// <summary>Gets editor picking data, or an invalid handle when picking is not requested.</summary>
    public RenderTextureHandle picking => m_picking;

    /// <summary>Gets an imported offscreen camera target, or an invalid handle for the backbuffer.</summary>
    public RenderTextureHandle cameraTarget => m_cameraTarget;

    /// <summary>Publishes current scene color for downstream features.</summary>
    /// <param name="texture">Scene color texture.</param>
    public void PublishSceneColor(RenderTextureHandle texture) => m_sceneColor = RequireValid(texture);

    /// <summary>Publishes current scene depth for downstream features.</summary>
    /// <param name="texture">Scene depth texture.</param>
    public void PublishSceneDepth(RenderTextureHandle texture) => m_sceneDepth = RequireValid(texture);

    /// <summary>Publishes the directional shadow atlas.</summary>
    /// <param name="texture">Shadow atlas texture.</param>
    public void PublishShadowAtlas(RenderTextureHandle texture) => m_shadowAtlas = RequireValid(texture);

    /// <summary>Publishes semantic Deferred GBuffer textures.</summary>
    /// <param name="baseColorMetallic">BaseColor/Metallic texture.</param>
    /// <param name="normalRoughness">Normal/Roughness texture.</param>
    /// <param name="emissiveOcclusion">Emissive/AO texture.</param>
    public void PublishGBuffer(
        RenderTextureHandle baseColorMetallic,
        RenderTextureHandle normalRoughness,
        RenderTextureHandle emissiveOcclusion)
    {
        m_gBuffer0 = RequireValid(baseColorMetallic);
        m_gBuffer1 = RequireValid(normalRoughness);
        m_gBuffer2 = RequireValid(emissiveOcclusion);
    }

    /// <summary>Publishes editor picking data.</summary>
    /// <param name="texture">Picking texture.</param>
    public void PublishPicking(RenderTextureHandle texture) => m_picking = RequireValid(texture);

    /// <summary>Publishes an imported offscreen camera target.</summary>
    /// <param name="texture">Imported destination texture.</param>
    public void PublishCameraTarget(RenderTextureHandle texture) => m_cameraTarget = RequireValid(texture);

    private static RenderTextureHandle RequireValid(RenderTextureHandle texture)
        => texture.isValid
            ? texture
            : throw new ArgumentException("Published render resource handle must be valid.", nameof(texture));
}

/// <summary>
/// Supplies one view request and frame-scoped services to a render pipeline.
/// </summary>
public sealed class RenderPipelineContext
{
    /// <summary>
    /// Creates a pipeline build context.
    /// </summary>
    /// <param name="request">View request being built.</param>
    /// <param name="pipelineAsset">Active pipeline configuration.</param>
    /// <param name="world">Immutable frame render-world snapshot.</param>
    /// <param name="resolvedPath">Capability-resolved render path.</param>
    /// <param name="graph">Current frame graph builder.</param>
    /// <param name="capabilities">Current device capability snapshot.</param>
    /// <param name="resources">Typed standard resource registry for this view.</param>
    /// <param name="diagnostics">Structured diagnostic sink.</param>
    /// <param name="executor">Backend-neutral built-in operation executor.</param>
    public RenderPipelineContext(
        RenderRequest request,
        RenderPipelineAsset pipelineAsset,
        RenderWorldSnapshot world,
        RenderPath resolvedPath,
        RenderGraphBuilder graph,
        GraphicsCapabilities capabilities,
        BuiltinRenderResources resources,
        IRenderDiagnosticSink diagnostics,
        IRenderPipelineExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pipelineAsset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(executor);
        this.request = request;
        this.pipelineAsset = pipelineAsset;
        this.world = world;
        this.resolvedPath = resolvedPath;
        this.graph = graph;
        this.capabilities = capabilities;
        this.resources = resources;
        this.diagnostics = diagnostics;
        this.executor = executor;
    }

    /// <summary>Gets the view request being built.</summary>
    public RenderRequest request { get; }

    /// <summary>Gets the active pipeline configuration.</summary>
    public RenderPipelineAsset pipelineAsset { get; }

    /// <summary>Gets the immutable frame render-world snapshot.</summary>
    public RenderWorldSnapshot world { get; }

    /// <summary>Gets the capability-resolved render path.</summary>
    public RenderPath resolvedPath { get; }

    /// <summary>Gets the current frame graph builder.</summary>
    public RenderGraphBuilder graph { get; }

    /// <summary>Gets the current device capability snapshot.</summary>
    public GraphicsCapabilities capabilities { get; }

    /// <summary>Gets typed standard resources for this view.</summary>
    public BuiltinRenderResources resources { get; }

    /// <summary>Gets the structured diagnostic sink.</summary>
    public IRenderDiagnosticSink diagnostics { get; }

    /// <summary>Gets the backend-neutral built-in operation executor.</summary>
    public IRenderPipelineExecutor executor { get; }
}

/// <summary>
/// Supplies one configured feature with frame-scoped render graph services.
/// </summary>
public sealed class RenderFeatureContext
{
    /// <summary>
    /// Creates a feature build context.
    /// </summary>
    /// <param name="pipeline">Owning pipeline context.</param>
    /// <param name="configuration">Stable feature configuration.</param>
    public RenderFeatureContext(RenderPipelineContext pipeline, RenderFeatureConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(configuration);
        this.pipeline = pipeline;
        this.configuration = configuration;
    }

    /// <summary>Gets the owning pipeline context.</summary>
    public RenderPipelineContext pipeline { get; }

    /// <summary>Gets the stable feature configuration.</summary>
    public RenderFeatureConfiguration configuration { get; }

    /// <summary>Gets the current frame graph builder.</summary>
    public RenderGraphBuilder graph => pipeline.graph;

    /// <summary>Gets typed standard resources for this view.</summary>
    public BuiltinRenderResources resources => pipeline.resources;

    /// <summary>Gets the current device capability snapshot.</summary>
    public GraphicsCapabilities capabilities => pipeline.capabilities;
}

/// <summary>
/// Builds frame-local passes for each camera or editor view.
/// </summary>
public abstract class RenderPipeline : IDisposable
{
    private bool m_disposed;

    /// <summary>
    /// Builds all built-in and configured feature passes for one view request.
    /// </summary>
    /// <param name="context">Frame-scoped pipeline context.</param>
    public abstract void Build(RenderPipelineContext context);

    /// <summary>
    /// Releases generation-scoped pipeline state without directly destroying GPU resources.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        m_disposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed generation-scoped pipeline state.
    /// </summary>
    /// <param name="disposing">Always <see langword="true"/> for explicit disposal.</param>
    protected virtual void Dispose(bool disposing) { }
}

/// <summary>
/// Adds capability-aware custom passes without retaining frame graph delegates.
/// </summary>
public abstract class RenderPipelineFeature
{
    /// <summary>
    /// Applies neutral configuration to this generation-scoped feature instance.
    /// </summary>
    /// <param name="configuration">Stable feature configuration.</param>
    public void Configure(RenderFeatureConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        using JsonDocument settings = JsonDocument.Parse(configuration.settingsJson);
        OnConfigure(settings.RootElement);
    }

    /// <summary>
    /// Adds frame-scoped passes and explicit resource dependencies.
    /// </summary>
    /// <param name="context">Frame-scoped feature context.</param>
    public abstract void AddRenderPasses(RenderFeatureContext context);

    /// <summary>
    /// Reads neutral feature settings when a candidate feature generation is built.
    /// </summary>
    /// <param name="settings">Validated settings root for the active configuration.</param>
    protected virtual void OnConfigure(JsonElement settings) { }
}
