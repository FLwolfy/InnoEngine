using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Framework;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;

namespace Inno.Rendering.Runtime;

/// <summary>Owns the sole graphics frame boundary and executes model-neutral render requests.</summary>
public sealed class RenderRuntimeLayer : Layer, IRenderRequestSink
{
    private readonly object m_requestLock = new();
    private readonly object m_contributorLock = new();
    private readonly IRenderDevice m_device;
    private readonly IRenderDiagnosticSink m_diagnostics;
    private readonly RenderResourceService m_resourceService;
    private readonly RenderExtensionRegistry m_extensions = new();
    private readonly Dictionary<RenderPipelineAsset, GenerationCacheEntry> m_generations = [];
    private readonly List<RenderRequest> m_pendingRequests = [];
    private readonly List<RenderRequest> m_currentRequests = [];
    private readonly List<IRenderFrameGraphContributor> m_contributors = [];
    private ulong m_frameIndex;
    private uint m_graphGeneration;
    private bool m_acceptingCurrentFrame;
    private bool m_frameOpen;
    private RenderRuntimeReloadSession? m_reloadSession;

    /// <summary>Creates a render runtime without installing any concrete pipeline.</summary>
    /// <param name="device">Active backend-neutral device.</param>
    /// <param name="diagnostics">Structured diagnostic sink.</param>
    /// <param name="contributors">Optional frame-final contributors such as an ImGui backend.</param>
    /// <param name="shaderCompiler">Optional backend-owned compiler for source Shader assets.</param>
    /// <param name="textureCompiler">Optional backend-owned compiler for artist texture sources.</param>
    public RenderRuntimeLayer(
        IRenderDevice device,
        IRenderDiagnosticSink diagnostics,
        IEnumerable<IRenderFrameGraphContributor>? contributors = null,
        ShaderCompiler? shaderCompiler = null,
        ITextureTargetCompiler? textureCompiler = null)
        : base("RenderRuntimeLayer")
    {
        m_device = device ?? throw new ArgumentNullException(nameof(device));
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        m_resourceService = new RenderResourceService(
            m_device,
            m_diagnostics,
            shaderCompiler,
            textureCompiler);
        targets = new RenderTargetRegistry(device);
        if (contributors is not null)
            m_contributors.AddRange(contributors);
    }

    /// <summary>Gets persistent offscreen target services for viewport presentation.</summary>
    public RenderTargetRegistry targets { get; }

    /// <summary>Registers frame-final work without transferring frame ownership.</summary>
    /// <param name="contributor">Contributor invoked after all user pipeline requests.</param>
    /// <exception cref="ArgumentException">Thrown when the instance is already registered.</exception>
    public void RegisterContributor(IRenderFrameGraphContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        lock (m_contributorLock)
        {
            if (m_contributors.Contains(contributor))
                throw new ArgumentException("Frame graph contributor is already registered.", nameof(contributor));
            m_contributors.Add(contributor);
        }
    }

    /// <summary>Stops invoking a previously registered frame-final contributor.</summary>
    /// <param name="contributor">Contributor to remove.</param>
    /// <returns><see langword="true"/> when registration existed and was removed.</returns>
    public bool UnregisterContributor(IRenderFrameGraphContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        lock (m_contributorLock)
        {
            return m_contributors.Remove(contributor);
        }
    }

    /// <inheritdoc />
    public void Submit(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (m_requestLock)
        {
            (m_acceptingCurrentFrame ? m_currentRequests : m_pendingRequests).Add(request);
        }
    }

    /// <summary>Validates and selects a project default while preserving its last-good generation.</summary>
    /// <param name="pipelineAsset">Candidate project default pipeline asset.</param>
    /// <returns><see langword="true"/> when a usable generation exists for the candidate.</returns>
    /// <exception cref="InvalidOperationException">Thrown during an open graphics frame.</exception>
    public bool TryActivateDefaultPipeline(RenderPipelineAsset pipelineAsset)
    {
        ArgumentNullException.ThrowIfNull(pipelineAsset);
        if (m_frameOpen)
            throw new InvalidOperationException("A default pipeline can only change at a frame boundary.");
        if (TryGetGeneration(pipelineAsset, out _))
        {
            GraphicsSettings.defaultPipeline = pipelineAsset;
            return true;
        }
        return false;
    }

    internal RenderRuntimeReloadSession BeginExtensionReload()
    {
        if (m_frameOpen)
            throw new InvalidOperationException("Rendering extensions can only reload at a frame boundary.");
        if (m_reloadSession is not null)
            throw new InvalidOperationException("A rendering extension reload is already active.");

        m_reloadSession = RenderRuntimeReloadSession.Create(this);
        return m_reloadSession;
    }

    /// <inheritdoc />
    public override void OnAttach() => GraphicsSettings.SetDevice(m_device.capabilities);

    /// <inheritdoc />
    public override void OnBeforeRender(float deltaTime)
    {
        _ = deltaTime;
        PruneRetiredGenerations();
        m_device.BeginFrame();
        m_frameOpen = true;
        try
        {
            m_resourceService.BeginFrame(m_frameIndex);
            targets.PrepareFrame();
            lock (m_requestLock)
            {
                m_currentRequests.Clear();
                m_currentRequests.AddRange(m_pendingRequests);
                m_pendingRequests.Clear();
                m_acceptingCurrentFrame = true;
            }
        }
        catch
        {
            try
            {
                _ = m_device.EndFrame();
            }
            finally
            {
                m_frameOpen = false;
            }
            throw;
        }
    }

    /// <inheritdoc />
    public override void OnAfterRender(float deltaTime)
    {
        _ = deltaTime;
        if (!m_frameOpen)
            return;

        int executedViewCount = 0;
        int culledPassCount = 0;
        try
        {
            lock (m_requestLock)
                m_acceptingCurrentFrame = false;

            IReadOnlyList<IRenderFrameGraphContributor> preparedContributors = PrepareContributors();
            foreach (RenderRequest request in m_currentRequests
                         .OrderBy(static value => value.priority)
                         .ThenBy(static value => value.name, StringComparer.Ordinal))
            {
                bool executed = BuildAndExecuteRequest(
                    request,
                    executedViewCount,
                    out int passCount,
                    out int requestCulledPassCount);
                culledPassCount += requestCulledPassCount;
                if (executed)
                    executedViewCount += passCount;
            }

            GraphExecutionStatistics contributorStatistics = BuildAndExecuteContributors(
                preparedContributors,
                executedViewCount);
            executedViewCount += contributorStatistics.viewCount;
            culledPassCount += contributorStatistics.culledPassCount;
            m_resourceService.SweepUnused();
        }
        finally
        {
            RenderDeviceFrameCounters counters = m_device.frameCounters;
            try
            {
                _ = m_device.EndFrame();
            }
            finally
            {
                m_frameOpen = false;
                m_currentRequests.Clear();
                m_frameIndex++;
                GraphicsSettings.SetStatistics(new RenderFrameStatistics(
                    m_frameIndex,
                    executedViewCount,
                    counters.drawCount,
                    counters.dispatchCount,
                    culledPassCount));
            }
        }
    }

    /// <inheritdoc />
    public override void OnDetach()
    {
        m_reloadSession?.Rollback();
        GraphicsSettings.SetStatistics(null);
        GraphicsSettings.SetDevice(null);
        GraphicsSettings.defaultPipeline = null;
        foreach (GenerationCacheEntry entry in m_generations.Values)
            entry.lastGood?.Dispose();
        m_generations.Clear();
        targets.Dispose();
        m_resourceService.Dispose();
        m_extensions.Dispose();
    }

    private bool BuildAndExecuteRequest(
        RenderRequest request,
        int priorViews,
        out int passCount,
        out int culledPassCount)
    {
        passCount = 0;
        culledPassCount = 0;
        RenderPipelineAsset? asset = request.pipeline ?? GraphicsSettings.defaultPipeline;
        if (asset is null)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_PIPELINE_UNAVAILABLE",
                $"Render request '{request.name}' has no pipeline. The frame and UI remain active.",
                RenderDiagnosticSeverity.Warning,
                request.name));
            return false;
        }

        if (!TryGetGeneration(asset, out ActiveGeneration? generation))
            return false;

        try
        {
            RenderGraphBuilder graph = CreateGraph();
            RenderTextureHandle outputTexture = request.target.kind == RenderTargetKind.Texture
                ? targets.Import(
                    graph,
                    request.target.texture
                        ?? throw new InvalidOperationException("A texture target requires a RenderTexture."))
                : default;
            if (outputTexture.isValid)
                graph.MarkOutput(outputTexture);

            var context = new RenderPipelineContext(
                request,
                asset,
                graph,
                m_device.capabilities,
                new RenderResourceRegistry(),
                m_diagnostics,
                m_resourceService,
                m_frameIndex,
                outputTexture);
            generation!.pipeline.Build(context);
            AddFeatures(asset, generation.features, context);
            RenderGraphCompileResult result = graph.Compile();
            culledPassCount = result.culledPassCount;
            PublishGraphDiagnostics(result, request.name);
            if (result.graph is null)
                return false;

            passCount = result.graph.passes.Count;
            if (priorViews + passCount > m_device.capabilities.limits.maxViews)
            {
                m_diagnostics.Publish(new RenderDiagnostic(
                    "RENDER_GRAPH_VIEW_LIMIT_EXCEEDED",
                    $"Request '{request.name}' would exceed the device frame limit of " +
                    $"{m_device.capabilities.limits.maxViews} views.",
                    RenderDiagnosticSeverity.Error,
                    request.name));
                passCount = 0;
                return false;
            }

            if (passCount != 0)
                m_device.Execute(result.graph, m_frameIndex);
            return true;
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_REQUEST_FAILED",
                $"Render request '{request.name}' was isolated after failure: {exception.Message}",
                RenderDiagnosticSeverity.Error,
                request.name));
            passCount = 0;
            return false;
        }
    }

    private bool TryGetGeneration(RenderPipelineAsset asset, out ActiveGeneration? generation)
    {
        generation = null;
        string fingerprint = RenderExtensionRegistry.GetConfigurationFingerprint(asset);
        RenderExtensionRegistry.Snapshot snapshot;
        try
        {
            snapshot = m_extensions.extensions;
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_EXTENSION_REGISTRY_FAILED",
                $"Rendering extension discovery kept every last-good generation: {exception.Message}",
                RenderDiagnosticSeverity.Error,
                asset.pipelineTypeId));
            return m_generations.TryGetValue(asset, out GenerationCacheEntry? failedEntry)
                && (generation = failedEntry.lastGood) is not null;
        }

        if (!m_generations.TryGetValue(asset, out GenerationCacheEntry? entry))
        {
            entry = new GenerationCacheEntry();
            m_generations.Add(asset, entry);
        }

        if (entry.lastGood is not null
            && entry.lastGood.typeCacheVersion != snapshot.typeCacheVersion)
        {
            entry.lastGood.Dispose();
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
        try
        {
            ActiveGeneration next = CreateGeneration(snapshot, asset);
            ActiveGeneration? previous = entry.lastGood;
            entry.lastGood = next;
            previous?.Dispose();
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_EXTENSION_GENERATION_FAILED",
                $"Pipeline '{asset.pipelineTypeId}' kept its last-good generation: {exception.Message}",
                RenderDiagnosticSeverity.Error,
                asset.pipelineTypeId));
        }

        generation = entry.lastGood;
        return generation is not null;
    }

    private ActiveGeneration CreateGeneration(
        RenderExtensionRegistry.Snapshot snapshot,
        RenderPipelineAsset asset)
    {
        using RenderExtensionRegistry.Generation candidate = snapshot.CreateGeneration(asset);
        var generation = new ActiveGeneration(
            snapshot.typeCacheVersion,
            candidate.pipeline,
            candidate.features);
        candidate.TransferOwnership();
        return generation;
    }

    private void EndReloadSession(RenderRuntimeReloadSession session)
    {
        if (ReferenceEquals(m_reloadSession, session))
            m_reloadSession = null;
    }

    private void PruneRetiredGenerations()
    {
        RenderExtensionRegistry.Snapshot snapshot;
        try
        {
            snapshot = m_extensions.extensions;
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_EXTENSION_REGISTRY_FAILED",
                $"Rendering extension discovery kept the current frame available: {exception.Message}",
                RenderDiagnosticSeverity.Error));
            return;
        }

        foreach ((RenderPipelineAsset asset, GenerationCacheEntry entry) in m_generations.ToArray())
        {
            if (entry.lastGood is not null
                && entry.lastGood.typeCacheVersion != snapshot.typeCacheVersion)
            {
                entry.lastGood.Dispose();
                entry.lastGood = null;
            }

            if (entry.attemptedTypeCacheVersion != snapshot.typeCacheVersion)
                m_generations.Remove(asset);
        }
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
            catch (Exception exception)
            {
                PublishContributorFailure(contributor, "prepare", exception);
            }
        }
        return prepared;
    }

    private GraphExecutionStatistics BuildAndExecuteContributors(
        IReadOnlyList<IRenderFrameGraphContributor> contributors,
        int priorViews)
    {
        if (contributors.Count == 0)
            return default;

        RenderGraphBuilder graph = CreateGraph();
        foreach (IRenderFrameGraphContributor contributor in contributors)
        {
            using RenderGraphMutationScope mutation = graph.BeginMutationScope();
            try
            {
                contributor.AddRenderPasses(graph, m_frameIndex);
                mutation.Commit();
            }
            catch (Exception exception)
            {
                PublishContributorFailure(contributor, "graph build", exception);
            }
        }

        RenderGraphCompileResult result = graph.Compile();
        PublishGraphDiagnostics(result, "Frame Contributors");
        if (result.graph is null || result.graph.passes.Count == 0)
            return new GraphExecutionStatistics(0, result.culledPassCount);
        if (priorViews + result.graph.passes.Count > m_device.capabilities.limits.maxViews)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_GRAPH_VIEW_LIMIT_EXCEEDED",
                "Frame contributors exceed the device view limit.",
                RenderDiagnosticSeverity.Error,
                "Frame Contributors"));
            return new GraphExecutionStatistics(0, result.culledPassCount);
        }

        m_device.Execute(result.graph, m_frameIndex);
        return new GraphExecutionStatistics(result.graph.passes.Count, result.culledPassCount);
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
            m_diagnostics.Publish(new RenderDiagnostic(
                diagnostic.code,
                diagnostic.message,
                diagnostic.severity == RenderGraphDiagnosticSeverity.Error
                    ? RenderDiagnosticSeverity.Error
                    : diagnostic.severity == RenderGraphDiagnosticSeverity.Warning
                        ? RenderDiagnosticSeverity.Warning
                        : RenderDiagnosticSeverity.Info,
                diagnostic.passName ?? diagnostic.resourceName ?? source));
        }
    }

    private void PublishContributorFailure(
        IRenderFrameGraphContributor contributor,
        string stage,
        Exception exception)
        => m_diagnostics.Publish(new RenderDiagnostic(
            "RENDER_FRAME_CONTRIBUTOR_FAILED",
            $"Frame contributor '{contributor.GetType().Name}' failed during {stage}: {exception.Message}",
            RenderDiagnosticSeverity.Error,
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
            catch (Exception exception)
            {
                m_diagnostics.Publish(new RenderDiagnostic(
                    "RENDER_FEATURE_FAILED",
                    $"Feature '{configuration.featureTypeId}' was rolled back: {exception.Message}",
                    RenderDiagnosticSeverity.Error,
                    configuration.featureTypeId));
            }
        }
    }

    private readonly record struct GraphExecutionStatistics(int viewCount, int culledPassCount);

    private sealed class GenerationCacheEntry
    {
        internal long attemptedTypeCacheVersion { get; set; } = -1;
        internal string? attemptedFingerprint { get; set; }
        internal ActiveGeneration? lastGood { get; set; }
    }

    private readonly record struct GenerationState(
        long attemptedTypeCacheVersion,
        string? attemptedFingerprint,
        ActiveGeneration? lastGood);

    internal sealed class RenderRuntimeReloadSession
    {
        private readonly RenderRuntimeLayer m_owner;
        private readonly IReadOnlyDictionary<RenderPipelineAsset, GenerationState> m_previous;
        private readonly Dictionary<RenderPipelineAsset, GenerationState> m_candidates = [];
        private bool m_prepared;
        private bool m_activated;
        private bool m_finished;

        private RenderRuntimeReloadSession(
            RenderRuntimeLayer owner,
            IReadOnlyDictionary<RenderPipelineAsset, GenerationState> previous)
        {
            m_owner = owner;
            m_previous = previous;
        }

        internal static RenderRuntimeReloadSession Create(RenderRuntimeLayer owner)
        {
            var previous = new Dictionary<RenderPipelineAsset, GenerationState>();
            foreach ((RenderPipelineAsset asset, GenerationCacheEntry entry) in owner.m_generations)
            {
                previous.Add(asset, new GenerationState(
                    entry.attemptedTypeCacheVersion,
                    entry.attemptedFingerprint,
                    entry.lastGood));
            }
            return new RenderRuntimeReloadSession(owner, previous);
        }

        internal void PrepareCandidate()
        {
            EnsureNotFinished();
            if (m_prepared)
                return;

            RenderExtensionRegistry.Snapshot snapshot = m_owner.m_extensions.extensions;
            try
            {
                foreach ((RenderPipelineAsset asset, GenerationState previous) in m_previous)
                {
                    if (previous.lastGood is null)
                        continue;
                    string fingerprint = RenderExtensionRegistry.GetConfigurationFingerprint(asset);
                    ActiveGeneration candidate = m_owner.CreateGeneration(snapshot, asset);
                    m_candidates.Add(asset, new GenerationState(
                        snapshot.typeCacheVersion,
                        fingerprint,
                        candidate));
                }
                m_prepared = true;
            }
            catch (Exception exception)
            {
                DisposeCandidates();
                m_owner.m_diagnostics.Publish(new RenderDiagnostic(
                    "RENDER_EXTENSION_RELOAD_REJECTED",
                    "Rendering extension candidate was rejected and every last-good generation was retained: " +
                    exception.Message,
                    RenderDiagnosticSeverity.Error));
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
            m_activated = true;
        }

        internal void Complete()
        {
            EnsureNotFinished();
            if (!m_activated)
                throw new InvalidOperationException("Rendering extension candidates have not been activated.");
            foreach (GenerationState previous in m_previous.Values)
                previous.lastGood?.Dispose();
            Finish();
        }

        internal void Rollback()
        {
            if (m_finished)
                return;

            if (m_activated)
            {
                foreach ((RenderPipelineAsset asset, GenerationState previous) in m_previous)
                {
                    if (!m_owner.m_generations.TryGetValue(asset, out GenerationCacheEntry? entry))
                        continue;
                    entry.attemptedTypeCacheVersion = previous.attemptedTypeCacheVersion;
                    entry.attemptedFingerprint = previous.attemptedFingerprint;
                    entry.lastGood = previous.lastGood;
                }
            }
            DisposeCandidates();
            Finish();
        }

        private void DisposeCandidates()
        {
            foreach (GenerationState candidate in m_candidates.Values)
                candidate.lastGood?.Dispose();
            m_candidates.Clear();
        }

        private void Finish()
        {
            m_candidates.Clear();
            m_finished = true;
            m_owner.EndReloadSession(this);
        }

        private void EnsureNotFinished()
        {
            if (m_finished)
                throw new InvalidOperationException("Rendering extension reload session is already finished.");
        }
    }

    private sealed class ActiveGeneration : IDisposable
    {
        internal ActiveGeneration(
            long typeCacheVersion,
            RenderPipeline pipeline,
            IReadOnlyDictionary<string, RenderPipelineFeature> features)
        {
            this.typeCacheVersion = typeCacheVersion;
            this.pipeline = pipeline;
            this.features = features;
        }

        internal long typeCacheVersion { get; }
        internal RenderPipeline pipeline { get; }
        internal IReadOnlyDictionary<string, RenderPipelineFeature> features { get; }

        public void Dispose()
        {
            pipeline.Dispose();
            RenderExtensionRegistry.DisposeFeatures(features.Values);
        }
    }
}
