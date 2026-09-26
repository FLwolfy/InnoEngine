using Inno.References;
using Inno.Runtime.Contracts;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Core.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Inno.Input;
using Inno.Core.Mathematics;

namespace Inno.Rendering.Runtime;

/// <summary>
/// Owns the sole graphics frame boundary and executes model-neutral render requests.
/// </summary>
public sealed class RenderRuntime : RuntimeSubsystem, IRenderRequestSink, IViewContentCollector
{
    private const int C_MAX_QUEUED_REQUESTS = 4096;
    private static readonly RenderPhaseId S_PRESENTATION_BACKGROUND_PHASE = new(
        "inno.runtime.presentation-background");
    private static readonly RenderClearColor S_PRESENTATION_BACKGROUND_COLOR = new(0f, 0f, 0f, 1f);

    private readonly object m_requestLock = new();
    private readonly object m_contributorLock = new();
    private readonly IRenderDevice m_device;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly Func<ContentReadScope>? m_contentScopeProvider;
    private readonly Func<InputSnapshot>? m_inputSnapshotProvider;
    private readonly Func<RenderPresentationSize>? m_primaryInputSurfaceSizeProvider;
    private readonly Func<RenderPresentationSize, RenderViewport>? m_primaryPresentationViewportProvider;
    private readonly RenderResourceService m_resourceService;
    private readonly RenderFrameUploadService m_uploads;
    private readonly RenderLayerCompositor m_compositor;
    private readonly RenderExtensionRegistry m_extensions;
    private readonly GraphicsSettingsState m_graphicsSettings;
    private readonly Dictionary<RenderPipelineAsset, GenerationCacheEntry> m_generations = [];
    private readonly List<RenderPipelineAsset> m_retiredAssets = [];
    private readonly List<RenderRequest> m_pendingRequests = [];
    private readonly List<RenderRequest> m_currentRequests = [];
    private readonly List<CompositionRequest> m_pendingCompositions = [];
    private readonly List<CompositionRequest> m_currentCompositions = [];
    private readonly List<IRenderFrameGraphContributor> m_contributors = [];
    private ulong m_frameIndex;
    private uint m_graphGeneration;
    private RenderExtensionRegistry.RequestProviderGeneration? m_requestProviders;
    private bool m_acceptingCurrentFrame;
    private bool m_disposed;
    private RenderRetirementQueue? m_retirement;
    private bool m_frameOpen;
    private RenderRuntimeReloadSession? m_reloadSession;
    private RenderPresentationSize m_primaryPresentationSize = new(1, 1);
    private RenderViewport m_primaryPresentationViewport = new(0, 0, 1, 1);
    private RenderOutputRoute? m_primaryRoute;
    private bool m_primaryModelOutputEnabled = true;
    private string? m_lastModelDiagnostic;
    private InputSnapshot m_frameInput = InputSnapshot.empty;

    /// <summary>
    /// Creates a render runtime without installing any concrete pipeline.
    /// </summary>
    /// <param name="device">
    /// Active backend-neutral device.
    /// </param>
    /// <param name="types">
    /// The type catalog that owns render extension generations.
    /// </param>
    /// <param name="diagnostics">
    /// Owner-bound structured diagnostic producer.
    /// </param>
    /// <param name="contributors">
    /// Optional frame-final contributors such as an ImGui backend.
    /// </param>
    /// <param name="targetArtifacts">
    /// Provider of immutable target artifacts. Editor hosts may provide an asynchronous authoring-backed
    /// implementation, while Player hosts provide a source-free deployment implementation.
    /// </param>
    /// <param name="contentScopeProvider">
    /// Optional host callback that supplies explicit current-frame content without coupling Rendering to Scene or documents.
    /// </param>
    /// <param name="primaryPresentationViewportProvider">
    /// Optional host callback that selects the content region within the primary presentation surface.
    /// The complete surface is used when omitted.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The contributor sequence contains a null entry or the same contributor more than once.
    /// No rendering generation is registered in this case.
    /// </exception>
    /// <param name="resourceLimits">
    /// Finite native cache and asynchronous readback limits; omitted values use the engine defaults.
    /// </param>
    /// <param name="inputSnapshotProvider">
    /// Optional host callback that reads the completed session input snapshot when output is collected.
    /// </param>
    /// <param name="primaryInputSurfaceSizeProvider">
    /// Optional logical window size used to scale input coordinates onto the presentation surface.
    /// </param>
    public RenderRuntime(
        TypeCatalog types,
        IRenderDevice device,
        IDiagnosticReporter diagnostics,
        IEnumerable<IRenderFrameGraphContributor>? contributors = null,
        IRenderTargetArtifactProvider? targetArtifacts = null,
        Func<ContentReadScope>? contentScopeProvider = null,
        Func<RenderPresentationSize, RenderViewport>? primaryPresentationViewportProvider = null,
        RenderResourceLimits? resourceLimits = null,
        Func<InputSnapshot>? inputSnapshotProvider = null,
        Func<RenderPresentationSize>? primaryInputSurfaceSizeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        resourceLimits ??= new RenderResourceLimits();
        resourceLimits.Validate();
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        if (contributors is not null)
        {
            foreach (IRenderFrameGraphContributor contributor in contributors)
            {
                if (contributor is null || m_contributors.Contains(contributor))
                    throw new ArgumentException("Initial contributors must contain unique non-null instances.", nameof(contributors));
                m_contributors.Add(contributor);
            }
        }
        m_contentScopeProvider = contentScopeProvider;
        m_inputSnapshotProvider = inputSnapshotProvider;
        m_primaryInputSurfaceSizeProvider = primaryInputSurfaceSizeProvider;
        m_primaryPresentationViewportProvider = primaryPresentationViewportProvider;
        m_extensions = new RenderExtensionRegistry(types);
        m_graphicsSettings = new GraphicsSettingsState(m_device.capabilities);
        m_resourceService = new RenderResourceService(
            m_device,
            m_diagnostics,
            targetArtifacts,
            resourceLimits);
        m_uploads = new RenderFrameUploadService(m_device, resourceLimits);
        m_compositor = new RenderLayerCompositor(m_device);
        targets = new RenderTargetStore(device, resourceLimits.targets);
    }

    /// <summary>
    /// Gets persistent offscreen target services for viewport presentation.
    /// </summary>
    public RenderTargetStore targets { get; }

    /// <summary>
    /// Gets backend-neutral persistent resource resolution for host-owned previews and rendering integrations.
    /// </summary>
    public IRenderResourceService resources => m_resourceService;

    /// <summary>
    /// Gets the generation-scoped collector used by rendering models for world content.
    /// </summary>
    public IViewContentCollector viewContent => this;

    /// <summary>
    /// Gets the monotonic index of the current or most recently completed output frame.
    /// </summary>
    public ulong currentFrameIndex => m_frameIndex;

    /// <summary>
    /// Sets the explicit model composition route for the primary output at a frame boundary.
    /// </summary>
    /// <param name="route">
    /// Ordered model identities, or null for automatic single-model selection.
    /// </param>
    public void SetPrimaryRoute(RenderOutputRoute? route)
    {
        EnsureActive();
        if (m_frameOpen)
            throw new InvalidOperationException("Output routes can only change at a frame boundary.");
        m_primaryRoute = route;
    }

    /// <summary>
    /// Enables or disables model rendering to the host's primary backbuffer.
    /// Editor hosts disable this because their Game and Scene sessions own offscreen outputs.
    /// </summary>
    /// <param name="enabled">
    /// Whether the primary backbuffer is a model output.
    /// </param>
    public void SetPrimaryModelOutputEnabled(bool enabled)
    {
        EnsureActive();
        if (m_frameOpen)
            throw new InvalidOperationException("Primary output ownership can only change at a frame boundary.");
        m_primaryModelOutputEnabled = enabled;
        m_lastModelDiagnostic = null;
    }

    /// <summary>
    /// Collects frame requests from rendering models and registered providers.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public IReadOnlyList<ViewContentItem> Collect(ViewContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureActive();
        var sink = new ViewContentSink();
        foreach (RenderExtensionRegistry.ContentSourceEntry entry in m_extensions.extensions.sources.sources)
        {
            if (context.sourceIds is { } sourceIds
                && !sourceIds.Contains(entry.id, StringComparer.Ordinal))
                continue;
            try
            {
                entry.source.Collect(context, sink);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                m_diagnostics.Publish(new Diagnostic(
                    "RENDER_VIEW_CONTENT_SOURCE_FAILED",
                    $"View content source '{entry.id}' was isolated after failure: {exception}",
                    DiagnosticSeverity.Error,
                    entry.id));
            }
        }
        return sink.items;
    }

    /// <summary>
    /// Gets the non-zero rendering-device generation that owns persistent handles.
    /// </summary>
    public uint deviceGeneration => m_device.generation;

    /// <summary>
    /// Gets a detached control-thread snapshot of resource occupancy, high-water marks and rejected admissions.
    /// </summary>
    public RenderResourceStatistics resourceStatistics => m_resourceService.statistics with
    {
        uploadPages = m_uploads.pageCount, uploadResidentBytes = m_uploads.residentBytes,
        uploadPeakBytes = m_uploads.peakResidentBytes, uploadedFrameBytes = m_uploads.frameBytes,
        rejectedUploads = m_uploads.rejectedCount, targets = targets.count, rejectedTargets = targets.rejectedCount
    };

    /// <summary>
    /// Binds this rendering runtime to script-facing graphics APIs for the current asynchronous execution flow.
    /// </summary>
    /// <returns>
    /// A strict last-in-first-out execution scope owned by the caller.
    /// </returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this rendering runtime has been disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Retirement has started or generation cleanup has faulted the owner.
    /// </exception>
    public IDisposable EnterExecutionScope()
    {
        EnsureActive();
        return GraphicsSettingsExecutionContext.Enter(m_graphicsSettings);
    }

    /// <summary>
    /// Registers frame-final work without transferring frame ownership.
    /// </summary>
    /// <param name="contributor">
    /// Contributor invoked after all user pipeline requests.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the instance is already registered.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Retirement has started or generation cleanup has faulted the owner.
    /// </exception>
    public void RegisterContributor(IRenderFrameGraphContributor contributor)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(contributor);
        lock (m_contributorLock)
        {
            if (m_contributors.Contains(contributor))
                throw new ArgumentException("Frame graph contributor is already registered.", nameof(contributor));
            m_contributors.Add(contributor);
        }
    }

    /// <summary>
    /// Stops invoking a previously registered frame-final contributor.
    /// </summary>
    /// <param name="contributor">
    /// Contributor to remove.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when registration existed and was removed.
    /// </returns>
    public bool UnregisterContributor(IRenderFrameGraphContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        lock (m_contributorLock)
        {
            return m_contributors.Remove(contributor);
        }
    }

    /// <summary>
    /// Submits validated work to the active backend for ordered processing.
    /// </summary>
    /// <param name="request">
    /// The validated immutable request that defines this operation.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The bounded frame queue has reached capacity, retirement has started, or generation cleanup has faulted
    /// the owner. The request was not accepted.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The rendering owner has retired.
    /// </exception>
    public void Submit(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (m_requestLock)
        {
            EnsureActive();
            if (m_currentRequests.Count + m_pendingRequests.Count
                + m_currentCompositions.Count + m_pendingCompositions.Count >= C_MAX_QUEUED_REQUESTS)
                throw new InvalidOperationException("The rendering request queue is at capacity.");
            (m_acceptingCurrentFrame ? m_currentRequests : m_pendingRequests).Add(request);
        }
    }

    /// <summary>
    /// Submits independently rendered model layers for premultiplied-alpha output composition.
    /// </summary>
    /// <param name="name">
    /// Stable frame-local composition identity.
    /// </param>
    /// <param name="target">
    /// Final host-owned output target.
    /// </param>
    /// <param name="viewport">
    /// Output viewport in physical pixels.
    /// </param>
    /// <param name="format">
    /// Shared sampled color format required by every layer.
    /// </param>
    /// <param name="layers">
    /// Ordered requests, one per rendering model.
    /// </param>
    /// <param name="priority">
    /// Ascending scheduling priority relative to ordinary requests.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The layer set or format is invalid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bounded frame queue is full or retirement has started.
    /// </exception>
    public void SubmitComposition(string name, RenderTarget target, RenderViewport viewport,
        RenderTextureFormat format, IReadOnlyList<RenderRequest> layers, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count < 2 || layers.Any(static layer => layer is null))
            throw new ArgumentException("A composition requires at least two non-null model layers.", nameof(layers));
        if (layers.Any(layer => layer.viewport != viewport || layer.target != target))
            throw new ArgumentException("All composition layers must name the same final target and viewport.", nameof(layers));
        if (!m_device.capabilities.SupportsRenderTarget(format)
            || !m_device.capabilities.SupportsSampled(format))
            throw new ArgumentException($"The device cannot sample and render model layers in '{format}'.", nameof(format));
        var composition = new CompositionRequest(name, target, viewport, format,
            layers.ToArray(), priority);
        lock (m_requestLock)
        {
            EnsureActive();
            if (m_currentRequests.Count + m_pendingRequests.Count
                + m_currentCompositions.Count + m_pendingCompositions.Count >= C_MAX_QUEUED_REQUESTS)
                throw new InvalidOperationException("The rendering request queue is at capacity.");
            (m_acceptingCurrentFrame ? m_currentCompositions : m_pendingCompositions).Add(composition);
        }
    }

    /// <summary>
    /// Validates and selects a project default while preserving its last-good generation.
    /// </summary>
    /// <param name="pipelineAsset">
    /// Candidate project default pipeline asset.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a usable generation exists for the candidate.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown during an open graphics frame, during retirement, or after generation cleanup faults the owner.
    /// </exception>
    public bool TryActivateDefaultPipeline(RenderPipelineAsset pipelineAsset)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(pipelineAsset);
        if (m_frameOpen)
            throw new InvalidOperationException("A default pipeline can only change at a frame boundary.");
        if (TryGetGeneration(pipelineAsset, out _))
        {
            m_graphicsSettings.defaultPipeline = pipelineAsset;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Begins an isolated rendering-extension reload transaction at a frame boundary.
    /// </summary>
    /// <returns>
    /// A transaction that preserves the active generation until its candidate is explicitly completed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown during an open frame, during retirement, after a terminal generation failure, or while another
    /// rendering reload transaction is active.
    /// </exception>
    public IRenderRuntimeReloadTransaction BeginExtensionReload()
    {
        EnsureActive();
        if (m_frameOpen)
            throw new InvalidOperationException("Rendering extensions can only reload at a frame boundary.");
        if (m_reloadSession is not null)
            throw new InvalidOperationException("A rendering extension reload is already active.");

        m_reloadSession = RenderRuntimeReloadSession.Create(this);
        return m_reloadSession;
    }

    /// <summary>
    /// Attaches this feature to its owning runtime generation.
    /// </summary>
    protected override void OnStart()
    {
        EnsureActive();
    }
    /// <summary>
    /// Captures snapshots and binds service façades.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnBeginFrame(RuntimeFrame frame)
    {
        m_frameInput = InputExecutionContext.TryGet(out IInputService? input)
            ? input!.snapshot : InputSnapshot.empty;
        OwnFrameScope(EnterExecutionScope());
    }
    /// <summary>
    /// Opens resources required for this frame's output.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnPrepareOutput(RuntimeFrame frame) => BeginRendering(frame.unscaledDeltaTime);
    /// <summary>
    /// Collects output commands without executing managed code on a native realtime callback.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnProduceOutput(RuntimeFrame frame) => CollectRendering(frame.unscaledDeltaTime);
    /// <summary>
    /// Submits output and closes output-specific temporary resources.
    /// </summary>
    /// <param name="frame">
    /// The variable frame timing.
    /// </param>
    protected override void OnCompleteOutput(RuntimeFrame frame) => CompleteRendering(frame.unscaledDeltaTime);

    /// <summary>
    /// Prepares frame-scoped state before render requests are submitted.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    private void BeginRendering(float deltaTime)
    {
        EnsureActive();
        PruneRetiredGenerations();
        EnsureRequestProviders();
        m_device.BeginFrame();
        m_frameOpen = true;
        try
        {
            m_resourceService.BeginFrame(m_frameIndex);
            m_uploads.BeginFrame(m_frameIndex);
            targets.PrepareFrame();
            m_primaryPresentationSize = m_device.primaryPresentationSize;
            m_primaryPresentationViewport = ResolvePrimaryPresentationViewport(m_primaryPresentationSize);
            lock (m_requestLock)
            {
                m_currentRequests.Clear();
                m_currentRequests.AddRange(m_pendingRequests);
                m_pendingRequests.Clear();
                m_currentCompositions.Clear();
                m_currentCompositions.AddRange(m_pendingCompositions);
                m_pendingCompositions.Clear();
                m_acceptingCurrentFrame = true;
            }
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            try
            {
                _ = m_device.EndFrame();
            }
            finally
            {
                m_resourceService.EndMutation();
                m_frameOpen = false;
            }
            throw;
        }
    }

    /// <summary>
    /// Submits render work for the current frame.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    private void CollectRendering(float deltaTime)
    {
        if (!m_frameOpen || m_requestProviders is null)
            return;

        using IDisposable executionScope = EnterExecutionScope();
        using ContentReadScope content = GetFrameContentScope();
        InputSnapshot input = m_inputSnapshotProvider?.Invoke() ?? m_frameInput;
        Vector2 pointerPosition = input.mousePosition;
        if (m_primaryInputSurfaceSizeProvider is not null)
        {
            RenderPresentationSize logicalSize = m_primaryInputSurfaceSizeProvider();
            if (logicalSize.width <= 0 || logicalSize.height <= 0)
                throw new InvalidOperationException("The primary input surface has an invalid logical size.");
            pointerPosition = new Vector2(
                pointerPosition.x * m_primaryPresentationSize.width / logicalSize.width,
                pointerPosition.y * m_primaryPresentationSize.height / logicalSize.height);
        }
        Vector2 pointer = pointerPosition - new Vector2(
            m_primaryPresentationViewport.x, m_primaryPresentationViewport.y);
        var outputInput = new RenderOutputInput(pointer,
            pointer.x >= 0f && pointer.y >= 0f
            && pointer.x < m_primaryPresentationViewport.width
            && pointer.y < m_primaryPresentationViewport.height,
            input.scrollDelta, input.modifiers, input.keysPressed, input.keysReleased,
            input.mouseButtonsPressed, input.mouseButtonsReleased, input.textInput);
        var context = new RenderRequestProviderContext(
            this,
            content,
            m_device.capabilities,
            m_primaryPresentationSize,
            m_primaryPresentationViewport,
            m_frameIndex,
            deltaTime,
            this,
            outputInput);
        if (m_primaryModelOutputEnabled)
            CollectModels(new RenderOutputSession("primary", content, m_primaryPresentationViewport,
                m_frameIndex, deltaTime, this, outputInput, m_primaryRoute));
        foreach (RenderExtensionRegistry.RequestProviderEntry entry in m_requestProviders.providers)
        {
            try
            {
                entry.provider.Submit(context);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                m_diagnostics.Publish(new Diagnostic(
                    "RENDER_REQUEST_PROVIDER_FAILED",
                    $"Render request provider '{entry.id}' was isolated after failure: {exception}",
                    DiagnosticSeverity.Error,
                    entry.id));
            }
        }
    }

    private void CollectModels(RenderOutputSession session)
    {
        var applicable = new List<RenderExtensionRegistry.RenderModelEntry>();
        foreach (RenderExtensionRegistry.RenderModelEntry entry in m_extensions.extensions.models.models)
        {
            try
            {
                if (entry.model.CanRender(session))
                    applicable.Add(entry);
            }
            catch (Exception exception)
            {
                m_diagnostics.Publish(new Diagnostic("RENDER_MODEL_ACCEPT_FAILED",
                    $"Render model '{entry.id}' failed to inspect output content: {exception}",
                    DiagnosticSeverity.Error, entry.id));
            }
        }
        if (applicable.Count == 0)
        {
            PublishModelDiagnostic("No rendering model accepts the primary output content.");
            return;
        }
        IReadOnlyList<RenderExtensionRegistry.RenderModelEntry> ordered = applicable;
        RenderOutputRoute? route = session.route;
        if (route is not null)
        {
            if (route.layers.Count != applicable.Count
                || route.layers.Any(layer => applicable.All(entry => entry.id != layer.modelId)))
            {
                PublishModelDiagnostic("The primary output route must name every applicable rendering model exactly once.");
                return;
            }
            ordered = route.layers.Select(layer => applicable.Single(entry => entry.id == layer.modelId)).ToArray();
        }
        else if (applicable.Count > 1)
        {
            PublishModelDiagnostic("Multiple rendering models accept the primary output; configure a RenderOutputRoute: "
                + string.Join(", ", applicable.Select(static entry => entry.id)));
            return;
        }
        m_lastModelDiagnostic = null;
        var requests = new List<RenderRequest>(ordered.Count);
        RenderTextureFormat? format = null;
        for (int index = 0; index < ordered.Count; index++)
        {
            RenderExtensionRegistry.RenderModelEntry entry = ordered[index];
            try
            {
                RenderOutputSession modelSession = route is null
                    ? session
                    : session.ForLayer(route.layers[index]);
                RenderModelOutput model = entry.model.Build(modelSession);
                if (format is RenderTextureFormat selected && selected != model.targetFormat)
                {
                    PublishModelDiagnostic($"Output models require different target formats: '{selected}' and '{model.targetFormat}'.");
                    return;
                }
                format ??= model.targetFormat;
                requests.Add(new RenderRequest(model.name, RenderTarget.backbuffer, session.viewport,
                    model.pipeline, model.data, 1000 + index));
            }
            catch (Exception exception)
            {
                m_diagnostics.Publish(new Diagnostic("RENDER_MODEL_BUILD_FAILED",
                    $"Render model '{entry.id}' failed to build output: {exception}",
                    DiagnosticSeverity.Error, entry.id));
                return;
            }
        }
        if (requests.Count == 1)
            Submit(requests[0]);
        else
            SubmitComposition($"Output:{session.id}", RenderTarget.backbuffer,
                session.viewport, format!.Value, requests, priority: 1000);
    }

    private void PublishModelDiagnostic(string message)
    {
        if (string.Equals(message, m_lastModelDiagnostic, StringComparison.Ordinal))
            return;
        m_lastModelDiagnostic = message;
        m_diagnostics.Publish(new Diagnostic("RENDER_OUTPUT_MODEL_UNAVAILABLE", message,
            DiagnosticSeverity.Warning));
    }

    private ContentReadScope GetFrameContentScope()
    {
        if (m_contentScopeProvider is null)
            return ContentReadScope.empty;
        try
        {
            return m_contentScopeProvider()
                ?? throw new InvalidOperationException("The host content-scope provider returned null.");
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_CONTENT_SCOPE_FAILED",
                $"The host content scope failed and an empty scope was used for this frame: {exception}",
                DiagnosticSeverity.Error,
                "RenderRuntime"));
            return ContentReadScope.empty;
        }
    }

    private RenderViewport ResolvePrimaryPresentationViewport(RenderPresentationSize size)
    {
        var fullViewport = new RenderViewport(0, 0, size.width, size.height);
        if (m_primaryPresentationViewportProvider is null)
            return fullViewport;
        try
        {
            RenderViewport viewport = m_primaryPresentationViewportProvider(size);
            if ((long)viewport.x + viewport.width > size.width
                || (long)viewport.y + viewport.height > size.height)
            {
                throw new InvalidOperationException(
                    "The configured viewport extends beyond the primary presentation surface.");
            }
            return viewport;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_PRESENTATION_VIEWPORT_FAILED",
                $"The primary presentation viewport was invalid and the complete surface was used: {exception.Message}",
                DiagnosticSeverity.Error,
                "RenderRuntime"));
            return fullViewport;
        }
    }

    private void AddPrimaryPresentationBackground(RenderGraphBuilder graph)
    {
        if (m_primaryPresentationViewport.x == 0
            && m_primaryPresentationViewport.y == 0
            && m_primaryPresentationViewport.width == m_primaryPresentationSize.width
            && m_primaryPresentationViewport.height == m_primaryPresentationSize.height)
        {
            return;
        }

        graph.AddRasterPass(
                "Primary Presentation Background",
                S_PRESENTATION_BACKGROUND_PHASE,
                0,
                static (_, _) => { })
            .ClearPresentationTarget(S_PRESENTATION_BACKGROUND_COLOR)
            .HasSideEffect();
    }

    /// <summary>
    /// Completes the current render frame and releases frame-scoped state.
    /// </summary>
    /// <param name="deltaTime">
    /// The elapsed frame time in seconds.
    /// </param>
    private void CompleteRendering(float deltaTime)
    {
        _ = deltaTime;
        if (!m_frameOpen)
            return;

        int executedViewCount = 0;
        int culledPassCount = 0;
        bool retirementPending = false;
        try
        {
            lock (m_requestLock)
                m_acceptingCurrentFrame = false;

            RenderGraphBuilder graph = CreateGraph();
            AddPrimaryPresentationBackground(graph);
            IReadOnlyList<IRenderFrameGraphContributor> preparedContributors = PrepareContributors();
            CompleteViewContentFrame();
            int requestIndex = 0;
            var presentation = new RenderPresentationComposer();
            var scheduled = m_currentRequests
                .Select(static request => new ScheduledWork(request.priority, request.name, request, null))
                .Concat(m_currentCompositions.Select(static composition =>
                    new ScheduledWork(composition.priority, composition.name, null, composition)))
                .OrderBy(static value => value.priority)
                .ThenBy(static value => value.name, StringComparer.Ordinal);
            foreach (ScheduledWork work in scheduled)
            {
                if (work.composition is CompositionRequest composition)
                {
                    BuildComposition(graph, composition, ref requestIndex);
                    continue;
                }
                RenderRequest request = work.request!;
                bool preservePresentationTarget = presentation.MustPreserve(request);
                if (TryBuildRequest(graph, request, requestIndex++, preservePresentationTarget))
                    presentation.Commit(request);
            }

            AddContributors(graph, preparedContributors);
            RenderGraphCompileResult result = graph.Compile();
            culledPassCount = result.culledPassCount;
            PublishGraphDiagnostics(result, "Frame");
            if (result.graph is not null)
            {
                executedViewCount = result.graph.passes.Count;
                if (executedViewCount != 0)
                {
                    m_resourceService.EndMutation();
                    m_device.Execute(result.graph, m_frameIndex);
                    m_resourceService.BeginMutation();
                }
            }
            m_resourceService.SweepUnused();
            m_uploads.SweepUnused();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null)
        {
            retirementPending = true;
            throw;
        }
        finally
        {
            if (!retirementPending)
                EndRenderingFrame(executedViewCount, culledPassCount);
        }
    }

    private void EndRenderingFrame(int executedViewCount, int culledPassCount)
    {
        RenderDeviceFrameCounters counters = m_device.frameCounters;
        RenderDeviceAllocationCounters? allocations = m_device.allocationCounters;
        m_resourceService.EndMutation();
        try { _ = m_device.EndFrame(); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            CompleteFrameState();
            throw;
        }
        CompleteFrameState();

        void CompleteFrameState()
        {
            m_frameOpen = false;
            m_uploads.EndFrame();
            m_currentRequests.Clear();
            m_currentCompositions.Clear();
            m_frameIndex++;
            m_graphicsSettings.frameStatistics = new RenderFrameStatistics(
                m_frameIndex, executedViewCount, counters.drawCount, counters.dispatchCount, culledPassCount,
                allocations);
        }
    }

    /// <summary>
    /// Detaches this feature and releases generation-scoped state.
    /// </summary>
    private void ReleaseRendering()
    {
        if (m_disposed)
            return;
        if (m_retirement is null)
        {
            m_retirement = new RenderRetirementQueue();
            m_retirement.Add(() => m_reloadSession?.Rollback());
            m_retirement.Add(RetireGenerations);
            m_retirement.Add(m_extensions.Dispose);
            m_retirement.Add(() =>
            {
                m_graphicsSettings.Clear();
                m_requestProviders = null;
                lock (m_requestLock)
                {
                    m_acceptingCurrentFrame = false;
                    m_pendingRequests.Clear();
                    m_currentRequests.Clear();
                    m_pendingCompositions.Clear();
                    m_currentCompositions.Clear();
                }
                lock (m_contributorLock)
                    m_contributors.Clear();
            });
            m_retirement.Add(() =>
            {
                if (!m_frameOpen)
                {
                    m_device.BeginFrame();
                    m_frameOpen = true;
                }
            });
            m_retirement.Add(targets.Dispose);
            m_retirement.Add(m_compositor.Dispose);
            m_retirement.Add(m_uploads.Dispose);
            m_retirement.Add(m_resourceService.Dispose);
            m_retirement.Add(() =>
            {
                if (m_frameOpen)
                    _ = m_device.EndFrame();
                m_frameOpen = false;
            });
            m_retirement.Add(m_uploads.EndFrame);
        }
        try { m_extensions.Retire(m_retirement); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_disposed = true;
            throw;
        }
        m_disposed = true;
    }

    private void RetireGenerations()
    {
        var resources = new RenderRetirementQueue();
        foreach (GenerationCacheEntry entry in m_generations.Values)
        {
            if (entry.lastGood is not null)
                resources.Add(entry.lastGood.Dispose);
        }
        try { m_extensions.Retire(resources); }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch
        {
            m_generations.Clear();
            throw;
        }
        m_generations.Clear();
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_retirement is not null)
            throw new InvalidOperationException("Rendering retirement has started and cannot accept new work.");
        m_extensions.EnsureHealthy();
    }

    /// <summary>
    /// Releases every runtime rendering generation, persistent target, upload, and GPU resource
    /// owned by this instance.
    /// </summary>
    protected override void OnStop()
        => ReleaseRendering();

    private void BuildComposition(RenderGraphBuilder graph, CompositionRequest composition,
        ref int requestIndex)
    {
        try
        {
            using RenderGraphMutationScope mutation = graph.BeginMutationScope();
            using RenderGraphNameScope names = graph.BeginNameScope(
                $"Composition {composition.name}");
            var layers = new List<RenderTextureHandle>(composition.layers.Length);
            for (int index = 0; index < composition.layers.Length; index++)
            {
                RenderTextureHandle color = graph.CreateTexture(
                    $"Model Layer {index + 1}",
                    new RenderTextureDescriptor(
                        composition.viewport.width, composition.viewport.height,
                        composition.format,
                        RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));
                RenderRequest layer = composition.layers[index];
                var localLayer = new RenderRequest(layer.name, layer.target,
                    new RenderViewport(0, 0, composition.viewport.width, composition.viewport.height),
                    layer.pipeline, layer.data, layer.priority);
                if (!TryBuildRequest(graph, localLayer, requestIndex++,
                        preservePresentationTarget: false, outputOverride: color))
                    return;
                layers.Add(color);
            }
            RenderTextureHandle output = composition.target.kind == RenderTargetKind.Texture
                ? targets.Import(graph, composition.target.texture
                    ?? throw new InvalidOperationException("A texture output requires a RenderTexture."))
                : default;
            m_compositor.AddPasses(graph, composition.name, layers, output,
                composition.viewport);
            if (output.isValid)
                graph.MarkOutput(output);
            RenderGraphCompileResult validation = graph.Validate();
            if (validation.graph is null)
            {
                PublishGraphDiagnostics(validation, composition.name);
                return;
            }
            mutation.Commit();
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_OUTPUT_COMPOSITION_FAILED",
                $"Output composition '{composition.name}' was isolated after failure: {exception}",
                DiagnosticSeverity.Error,
                composition.name));
        }
    }

    private void CompleteViewContentFrame()
    {
        foreach (RenderExtensionRegistry.ContentSourceEntry entry in m_extensions.extensions.sources.sources)
        {
            if (entry.source is not IViewContentFrameSource source)
                continue;
            try
            {
                source.CompleteFrame(m_frameIndex);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                m_diagnostics.Publish(new Diagnostic("RENDER_VIEW_CONTENT_FRAME_FAILED",
                    $"View content source '{entry.id}' failed to complete its frame: {exception}",
                    DiagnosticSeverity.Error, entry.id));
            }
        }
    }

    private bool TryBuildRequest(
        RenderGraphBuilder graph,
        RenderRequest request,
        int requestIndex,
        bool preservePresentationTarget,
        RenderTextureHandle outputOverride = default)
    {
        RenderPipelineAsset? asset = request.pipeline ?? m_graphicsSettings.defaultPipeline;
        if (asset is null)
        {
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_PIPELINE_UNAVAILABLE",
                $"Render request '{request.name}' has no pipeline. The frame and UI remain active.",
                DiagnosticSeverity.Warning,
                request.name));
            return false;
        }

        if (!TryGetGeneration(asset, out RenderPipelineGeneration? generation))
            return false;

        try
        {
            using RenderGraphMutationScope mutation = graph.BeginMutationScope();
            using RenderGraphNameScope names = graph.BeginNameScope(
                $"Request[{requestIndex}] {request.name}");
            RenderTextureHandle outputTexture = outputOverride.isValid
                ? outputOverride
                : request.target.kind == RenderTargetKind.Texture
                ? targets.Import(
                    graph,
                    request.target.texture
                        ?? throw new InvalidOperationException("A texture target requires a RenderTexture."))
                : default;
            if (outputTexture.isValid && !outputOverride.isValid)
                graph.MarkOutput(outputTexture);

            var context = new RenderPipelineContext(
                request,
                asset,
                graph,
                m_device.capabilities,
                new RenderResourceMap(),
                m_diagnostics,
                m_resourceService,
                m_uploads,
                m_frameIndex,
                preservePresentationTarget,
                outputTexture);
            generation!.pipeline.Build(context);
            AddFeatures(asset, generation.features, context);
            RenderGraphCompileResult validation = graph.Validate();
            if (validation.graph is null)
            {
                PublishGraphDiagnostics(validation, request.name);
                return false;
            }
            mutation.Commit();
            return true;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_REQUEST_FAILED",
                $"Render request '{request.name}' was isolated after failure: {exception}",
                DiagnosticSeverity.Error,
                request.name));
            return false;
        }
    }

    private bool TryGetGeneration(RenderPipelineAsset asset, out RenderPipelineGeneration? generation)
    {
        generation = null;
        string fingerprint = RenderExtensionRegistry.GetConfigurationFingerprint(asset);
        RenderExtensionRegistry.Snapshot snapshot;
        try
        {
            snapshot = m_extensions.extensions;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_extensions.EnsureHealthy();
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_EXTENSION_REGISTRY_FAILED",
                $"Rendering extension discovery kept every last-good generation: {exception.Message}",
                DiagnosticSeverity.Error,
                asset.pipelineTypeId));
            return m_generations.TryGetValue(asset, out GenerationCacheEntry? failedEntry)
                && (generation = failedEntry.lastGood) is not null;
        }

        if (!m_generations.TryGetValue(asset, out GenerationCacheEntry? entry))
        {
            entry = new GenerationCacheEntry(asset);
            m_generations.Add(asset, entry);
        }

        if (entry.lastGood is not null
            && entry.lastGood.typeCacheVersion != snapshot.typeCacheVersion)
        {
            m_extensions.Retire(entry.lastGood);
            entry.lastGood = null;
        }

        if (entry.attemptedTypeCacheVersion == snapshot.typeCacheVersion
            && string.Equals(entry.attemptedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            generation = entry.lastGood;
            return generation is not null;
        }

        entry.attemptedTypeCacheVersion = snapshot.typeCacheVersion;
        entry.attemptedFingerprint = fingerprint;
        RenderPipelineGeneration? previous = entry.lastGood;
        try
        {
            if (!TryCreateGeneration(snapshot, asset, out RenderPipelineGeneration? next))
            {
                return false;
            }
            entry.lastGood = next!;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_extensions.EnsureHealthy();
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_EXTENSION_GENERATION_FAILED",
                $"Pipeline '{asset.pipelineTypeId}' kept its last-good generation: {exception.Message}",
                DiagnosticSeverity.Error,
                asset.pipelineTypeId));
            generation = entry.lastGood;
            return generation is not null;
        }

        m_diagnostics.Resolve("RENDER_EXTENSION_GENERATION_FAILED", asset.pipelineTypeId);
        m_extensions.Retire(previous);

        generation = entry.lastGood;
        return generation is not null;
    }

    private static bool TryCreateGeneration(
        RenderExtensionRegistry.Snapshot snapshot,
        RenderPipelineAsset asset,
        out RenderPipelineGeneration? generation)
        => snapshot.TryCreateGeneration(asset, out generation);

    private void EndReloadSession(RenderRuntimeReloadSession session)
    {
        if (ReferenceEquals(m_reloadSession, session))
            m_reloadSession = null;
    }

    private void PruneRetiredGenerations()
    {
        PruneRetiredAssets();
        RenderExtensionRegistry.Snapshot snapshot;
        try
        {
            snapshot = m_extensions.extensions;
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_extensions.EnsureHealthy();
            m_diagnostics.Publish(new Diagnostic(
                "RENDER_EXTENSION_REGISTRY_FAILED",
                $"Rendering extension discovery kept the current frame available: {exception.Message}",
                DiagnosticSeverity.Error));
            return;
        }

        foreach ((RenderPipelineAsset asset, GenerationCacheEntry entry) in m_generations.ToArray())
        {
            if (entry.lastGood is not null
                && entry.lastGood.typeCacheVersion != snapshot.typeCacheVersion)
            {
                m_extensions.Retire(entry.lastGood);
                entry.lastGood = null;
            }

            if (entry.attemptedTypeCacheVersion != snapshot.typeCacheVersion)
                m_generations.Remove(asset);
        }

        if (m_requestProviders is not null
            && m_requestProviders.typeCacheVersion != snapshot.typeCacheVersion)
        {
            m_requestProviders = null;
        }
    }

    private void EnsureRequestProviders()
    {
        m_requestProviders = m_extensions.extensions.providers;
    }

    private void PruneRetiredAssets()
    {
        try
        {
            foreach ((RenderPipelineAsset asset, GenerationCacheEntry entry) in m_generations)
            {
                if (entry.hasAssetOwner && !ReferenceEquals(entry.assetIdentity.Resolve<RenderPipelineAsset>(), asset))
                    m_retiredAssets.Add(asset);
            }
            foreach (RenderPipelineAsset asset in m_retiredAssets)
            {
                m_extensions.Retire(m_generations[asset].lastGood);
                m_generations.Remove(asset);
            }
        }
        finally { m_retiredAssets.Clear(); }
    }

    private IReadOnlyList<IRenderFrameGraphContributor> PrepareContributors()
    {
        IRenderFrameGraphContributor[] snapshot;
        lock (m_contributorLock)
            snapshot = [.. m_contributors];

        var prepared = new List<IRenderFrameGraphContributor>(snapshot.Length);
        foreach (IRenderFrameGraphContributor contributor in snapshot)
        {
            try
            {
                contributor.PrepareFrame(m_frameIndex);
                prepared.Add(contributor);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                PublishContributorFailure(contributor, "prepare", exception);
            }
        }
        return prepared;
    }

    private void AddContributors(
        RenderGraphBuilder graph,
        IReadOnlyList<IRenderFrameGraphContributor> contributors)
    {
        for (int index = 0; index < contributors.Count; index++)
        {
            IRenderFrameGraphContributor contributor = contributors[index];
            using RenderGraphMutationScope mutation = graph.BeginMutationScope();
            using RenderGraphNameScope names = graph.BeginNameScope(
                $"Contributor[{index}] {contributor.GetType().Name}");
            try
            {
                contributor.AddRenderPasses(graph, m_frameIndex);
                RenderGraphCompileResult validation = graph.Validate();
                if (validation.graph is null)
                {
                    PublishGraphDiagnostics(validation, contributor.GetType().Name);
                    continue;
                }
                mutation.Commit();
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                PublishContributorFailure(contributor, "graph build", exception);
            }
        }
    }

    private RenderGraphBuilder CreateGraph()
    {
        m_graphGeneration++;
        if (m_graphGeneration == 0)
            m_graphGeneration++;
        return new RenderGraphBuilder(m_graphGeneration, m_device.capabilities);
    }

    private void PublishGraphDiagnostics(RenderGraphCompileResult result, string source)
    {
        foreach (RenderGraphDiagnostic diagnostic in result.diagnostics)
        {
            m_diagnostics.Publish(new Diagnostic(
                diagnostic.code,
                diagnostic.message,
                diagnostic.severity == DiagnosticSeverity.Error
                    ? DiagnosticSeverity.Error
                    : diagnostic.severity == DiagnosticSeverity.Warning
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Info,
                diagnostic.passName ?? diagnostic.resourceName ?? source));
        }
    }

    private void PublishContributorFailure(
        IRenderFrameGraphContributor contributor,
        string stage,
        Exception exception)
        => m_diagnostics.Publish(new Diagnostic(
            "RENDER_FRAME_CONTRIBUTOR_FAILED",
            $"Frame contributor '{contributor.GetType().Name}' failed during {stage}: {exception.Message}",
            DiagnosticSeverity.Error,
            contributor.GetType().FullName));

    private void AddFeatures(
        RenderPipelineAsset asset,
        IReadOnlyDictionary<string, RenderPipelineFeature> features,
        RenderPipelineContext context)
    {
        foreach (RenderFeatureConfiguration configuration in asset.features)
        {
            if (!configuration.enabled
                || !features.TryGetValue(configuration.featureTypeId, out RenderPipelineFeature? feature))
            {
                continue;
            }

            using RenderGraphMutationScope mutation = context.graph.BeginMutationScope();
            try
            {
                feature.AddRenderPasses(new RenderFeatureContext(context, configuration));
                mutation.Commit();
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                m_diagnostics.Publish(new Diagnostic(
                    "RENDER_FEATURE_FAILED",
                    $"Feature '{configuration.featureTypeId}' was rolled back: {exception.Message}",
                    DiagnosticSeverity.Error,
                    configuration.featureTypeId));
            }
        }
    }

    private sealed record CompositionRequest(
        string name,
        RenderTarget target,
        RenderViewport viewport,
        RenderTextureFormat format,
        RenderRequest[] layers,
        int priority);

    private readonly record struct ScheduledWork(
        int priority,
        string name,
        RenderRequest? request,
        CompositionRequest? composition);

    private sealed class GenerationCacheEntry(RenderPipelineAsset asset)
    {
        internal readonly Identity assetIdentity = asset.identity;
        internal readonly bool hasAssetOwner = asset.identity.runtimeIdentity.HasValue;
        internal long attemptedTypeCacheVersion { get; set; } = -1;
        internal string? attemptedFingerprint { get; set; }
        internal RenderPipelineGeneration? lastGood { get; set; }
    }

    private readonly record struct GenerationState(
        long attemptedTypeCacheVersion,
        string? attemptedFingerprint,
        RenderPipelineGeneration? lastGood);

    private sealed class ViewContentSink : IViewContentSink
    {
        private readonly List<ViewContentItem> m_items = [];

        internal IReadOnlyList<ViewContentItem> items => m_items;

        /// <summary>
        /// Submits validated work to the active backend for ordered processing.
        /// </summary>
        /// <param name="item">
        /// The stored item associated with the validated handle.
        /// </param>
public void Submit(ViewContentItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            m_items.Add(item);
        }
    }

    internal sealed class RenderRuntimeReloadSession : IRenderRuntimeReloadTransaction
    {
        private readonly RenderRuntime m_owner;
        private IReadOnlyDictionary<RenderPipelineAsset, GenerationState>? m_previous;
        private RenderExtensionRegistry.RequestProviderGeneration? m_previousRequestProviders;
        private RenderRequest[]? m_previousPendingRequests;
        private RenderRequest[]? m_previousCurrentRequests;
        private CompositionRequest[]? m_previousPendingCompositions;
        private CompositionRequest[]? m_previousCurrentCompositions;
        private readonly Dictionary<RenderPipelineAsset, GenerationState> m_candidates = [];
        private RenderExtensionRegistry.RequestProviderGeneration? m_candidateRequestProviders;
        private bool m_prepared;
        private bool m_activated;
        private bool m_finished;

        private RenderRuntimeReloadSession(
            RenderRuntime owner,
            IReadOnlyDictionary<RenderPipelineAsset, GenerationState> previous,
            RenderExtensionRegistry.RequestProviderGeneration? previousRequestProviders,
            RenderRequest[] previousPendingRequests,
            RenderRequest[] previousCurrentRequests,
            CompositionRequest[] previousPendingCompositions,
            CompositionRequest[] previousCurrentCompositions)
        {
            m_owner = owner;
            m_previous = previous;
            m_previousRequestProviders = previousRequestProviders;
            m_previousPendingRequests = previousPendingRequests;
            m_previousCurrentRequests = previousCurrentRequests;
            m_previousPendingCompositions = previousPendingCompositions;
            m_previousCurrentCompositions = previousCurrentCompositions;
        }

        internal static RenderRuntimeReloadSession Create(RenderRuntime owner)
        {
            // Disposed sessions must not contribute obsolete assets to the next extension generation.
            owner.PruneRetiredAssets();
            var previous = new Dictionary<RenderPipelineAsset, GenerationState>();
            foreach ((RenderPipelineAsset asset, GenerationCacheEntry entry) in owner.m_generations)
            {
                previous.Add(asset, new GenerationState(
                    entry.attemptedTypeCacheVersion,
                    entry.attemptedFingerprint,
                    entry.lastGood));
            }
            lock (owner.m_requestLock)
            {
                return new RenderRuntimeReloadSession(
                    owner,
                    previous,
                    owner.m_requestProviders,
                    [.. owner.m_pendingRequests],
                    [.. owner.m_currentRequests],
                    [.. owner.m_pendingCompositions],
                    [.. owner.m_currentCompositions]);
            }
        }

        internal void PrepareCandidate()
        {
            EnsureNotFinished();
            if (m_prepared)
                return;

            RenderExtensionRegistry.Snapshot snapshot = m_owner.m_extensions.extensions;
            try
            {
                m_candidateRequestProviders = snapshot.providers;
                foreach (RenderPipelineAsset asset in m_previous!.Keys)
                {
                    string fingerprint = RenderExtensionRegistry.GetConfigurationFingerprint(asset);
                    _ = TryCreateGeneration(snapshot, asset, out RenderPipelineGeneration? candidate);
                    m_candidates.Add(asset, new GenerationState(
                        snapshot.typeCacheVersion,
                        fingerprint,
                        candidate));
                }
                m_prepared = true;
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch (Exception exception)
            {
                DisposeCandidates();
                m_owner.m_diagnostics.Publish(new Diagnostic(
                    "RENDER_EXTENSION_RELOAD_REJECTED",
                    "Rendering extension candidate was rejected and every last-good generation was retained: " +
                    exception.Message,
                    DiagnosticSeverity.Error));
                throw new InvalidOperationException(
                    "Rendering extension candidate activation failed.",
                    exception);
            }
        }

        internal void Activate()
        {
            EnsureNotFinished();
            if (!m_prepared)
                throw new InvalidOperationException("Rendering extension candidates have not been prepared.");
            if (m_activated)
                return;

            foreach (RenderPipelineAsset asset in m_candidates.Keys)
            {
                if (!m_owner.m_generations.ContainsKey(asset))
                    throw new InvalidOperationException("A tracked rendering generation changed during reload.");
            }
            foreach ((RenderPipelineAsset asset, GenerationState candidate) in m_candidates)
            {
                GenerationCacheEntry entry = m_owner.m_generations[asset];
                entry.attemptedTypeCacheVersion = candidate.attemptedTypeCacheVersion;
                entry.attemptedFingerprint = candidate.attemptedFingerprint;
                entry.lastGood = candidate.lastGood;
            }
            m_owner.m_requestProviders = m_candidateRequestProviders;
            lock (m_owner.m_requestLock)
            {
                m_owner.m_acceptingCurrentFrame = false;
                m_owner.m_pendingRequests.Clear();
                m_owner.m_currentRequests.Clear();
                m_owner.m_pendingCompositions.Clear();
                m_owner.m_currentCompositions.Clear();
            }
            m_activated = true;
        }

        internal void Complete()
        {
            EnsureNotFinished();
            if (!m_activated)
                throw new InvalidOperationException("Rendering extension candidates have not been activated.");
            try
            {
                var resources = new LifetimeScope();
                foreach (GenerationState previous in m_previous!.Values)
                {
                    if (previous.lastGood is not null)
                        resources.Own(previous.lastGood);
                }
                m_owner.m_extensions.Retire(resources);
            }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch
            {
                Finish();
                throw;
            }
            Finish();
        }

        internal void Rollback()
        {
            if (m_finished)
                return;

            if (m_activated)
            {
                foreach ((RenderPipelineAsset asset, GenerationState previous) in m_previous!)
                {
                    if (!m_owner.m_generations.TryGetValue(asset, out GenerationCacheEntry? entry))
                        continue;
                    entry.attemptedTypeCacheVersion = previous.attemptedTypeCacheVersion;
                    entry.attemptedFingerprint = previous.attemptedFingerprint;
                    entry.lastGood = previous.lastGood;
                }
                m_owner.m_requestProviders = m_previousRequestProviders;
                lock (m_owner.m_requestLock)
                {
                    m_owner.m_pendingRequests.Clear();
                    m_owner.m_pendingRequests.AddRange(m_previousPendingRequests!);
                    m_owner.m_currentRequests.Clear();
                    m_owner.m_currentRequests.AddRange(m_previousCurrentRequests!);
                    m_owner.m_pendingCompositions.Clear();
                    m_owner.m_pendingCompositions.AddRange(m_previousPendingCompositions!);
                    m_owner.m_currentCompositions.Clear();
                    m_owner.m_currentCompositions.AddRange(m_previousCurrentCompositions!);
                }
            }
            try { DisposeCandidates(); }
            catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
            catch
            {
                Finish();
                throw;
            }
            Finish();
        }

        void IRenderRuntimeReloadTransaction.Prepare() => PrepareCandidate();

        void IRenderRuntimeReloadTransaction.Activate() => Activate();

        void IRenderRuntimeReloadTransaction.Complete() => Complete();

        void IRenderRuntimeReloadTransaction.Rollback() => Rollback();

        private void DisposeCandidates()
        {
            var resources = new LifetimeScope();
            foreach (GenerationState candidate in m_candidates.Values)
            {
                if (candidate.lastGood is not null)
                    resources.Own(candidate.lastGood);
            }
            m_owner.m_extensions.Retire(resources);
            m_candidates.Clear();
            m_candidateRequestProviders = null;
        }

        private void Finish()
        {
            m_candidates.Clear();
            m_candidateRequestProviders = null;
            m_previous = null;
            m_previousRequestProviders = null;
            m_previousPendingRequests = null;
            m_previousCurrentRequests = null;
            m_previousPendingCompositions = null;
            m_previousCurrentCompositions = null;
            m_finished = true;
            m_owner.EndReloadSession(this);
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Rendering extension reload session is already finished.");
        }
    }

}
