using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Framework;
using Inno.Rendering.Core;

namespace Inno.Rendering.Pipelines;

/// <summary>
/// Owns the sole graphics frame boundary and executes every queued camera through one active pipeline generation.
/// </summary>
public sealed class RenderingLayer : Layer, IRenderRequestSink
{
    private readonly object m_requestLock = new();
    private readonly object m_contributorLock = new();
    private readonly IRenderDevice m_device;
    private readonly IRenderPipelineExecutor m_executor;
    private readonly IRenderDiagnosticSink m_diagnostics;
    private readonly RenderExtensionRegistry? m_extensionRegistry;
    private readonly List<RenderRequest> m_pendingRequests = [];
    private readonly List<RenderRequest> m_currentRequests = [];
    private readonly List<IRenderFrameGraphContributor> m_contributors = [];
    private RenderPipelineAsset m_pipelineAsset;
    private RenderPipeline m_pipeline;
    private IReadOnlyDictionary<string, RenderPipelineFeature> m_features;
    private RenderWorldSnapshot? m_world;
    private ulong m_frameIndex;
    private uint m_graphGeneration;
    private long m_attemptedExtensionVersion = -1;
    private string? m_attemptedConfigurationFingerprint;
    private bool m_acceptingCurrentFrame;
    private bool m_frameOpen;

    /// <summary>Creates the graphics frame owner around backend-neutral pipeline services.</summary>
    /// <param name="device">Active backend-neutral device.</param>
    /// <param name="pipelineAsset">Active serialized pipeline configuration.</param>
    /// <param name="pipeline">Active generation-scoped pipeline implementation.</param>
    /// <param name="executor">Neutral operation executor.</param>
    /// <param name="diagnostics">Structured diagnostic sink.</param>
    /// <param name="features">Optional active features keyed by stable feature extension ID.</param>
    /// <param name="contributors">Optional frame-final graph contributors such as a UI backend.</param>
    /// <param name="discoverExtensions">
    /// Whether pipeline and feature implementations are refreshed from the reloadable type registry.
    /// </param>
    public RenderingLayer(
        IRenderDevice device,
        RenderPipelineAsset pipelineAsset,
        RenderPipeline pipeline,
        IRenderPipelineExecutor executor,
        IRenderDiagnosticSink diagnostics,
        IReadOnlyDictionary<string, RenderPipelineFeature>? features = null,
        IEnumerable<IRenderFrameGraphContributor>? contributors = null,
        bool discoverExtensions = false)
        : base("RenderingLayer")
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pipelineAsset);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(diagnostics);
        m_device = device;
        m_pipelineAsset = pipelineAsset;
        m_pipeline = pipeline;
        m_executor = executor;
        m_diagnostics = diagnostics;
        m_extensionRegistry = discoverExtensions ? new RenderExtensionRegistry() : null;
        m_features = features ?? new Dictionary<string, RenderPipelineFeature>(StringComparer.Ordinal);
        if (contributors is not null)
        {
            m_contributors.AddRange(contributors);
        }

        ConfigureFeatures(m_pipelineAsset, m_features);
    }

    /// <summary>Registers frame-final work without transferring frame ownership.</summary>
    /// <param name="contributor">Contributor to invoke after camera graphs.</param>
    /// <exception cref="ArgumentException">Thrown when the instance is already registered.</exception>
    public void RegisterContributor(IRenderFrameGraphContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        lock (m_contributorLock)
        {
            if (m_contributors.Contains(contributor))
            {
                throw new ArgumentException("Frame graph contributor is already registered.", nameof(contributor));
            }

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

    /// <summary>Atomically replaces pipeline and feature instances at the next frame boundary.</summary>
    /// <param name="pipelineAsset">Committed pipeline asset candidate.</param>
    /// <param name="pipeline">Committed pipeline implementation candidate.</param>
    /// <param name="features">Complete stable-ID feature candidate.</param>
    /// <exception cref="InvalidOperationException">Thrown during an open graphics frame.</exception>
    public void ReplaceGeneration(
        RenderPipelineAsset pipelineAsset,
        RenderPipeline pipeline,
        IReadOnlyDictionary<string, RenderPipelineFeature> features)
    {
        ArgumentNullException.ThrowIfNull(pipelineAsset);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(features);
        if (m_frameOpen)
        {
            throw new InvalidOperationException("A render generation can only switch at a frame boundary.");
        }

        IReadOnlyDictionary<string, RenderPipelineFeature> candidate =
            new Dictionary<string, RenderPipelineFeature>(features, StringComparer.Ordinal);
        ConfigureFeatures(pipelineAsset, candidate);
        RenderPipeline previous = m_pipeline;
        IReadOnlyDictionary<string, RenderPipelineFeature> previousFeatures = m_features;
        m_pipelineAsset = pipelineAsset;
        m_pipeline = pipeline;
        m_features = candidate;
        GraphicsSettings.SetPipelineAsset(pipelineAsset);
        if (!ReferenceEquals(previous, pipeline))
            previous.Dispose();
        RenderExtensionRegistry.DisposeFeatures(
            previousFeatures.Values.Where(
                feature => !candidate.Values.Any(candidateFeature => ReferenceEquals(feature, candidateFeature))));
    }

    /// <summary>
    /// Builds and atomically activates one imported pipeline asset while preserving the last-good generation on failure.
    /// </summary>
    /// <param name="pipelineAsset">Imported pipeline asset candidate to activate.</param>
    /// <returns><see langword="true"/> when a complete pipeline and feature generation was activated.</returns>
    /// <exception cref="InvalidOperationException">Thrown when called during an open graphics frame.</exception>
    public bool TryActivatePipelineAsset(RenderPipelineAsset pipelineAsset)
    {
        ArgumentNullException.ThrowIfNull(pipelineAsset);
        if (m_frameOpen)
        {
            throw new InvalidOperationException("A render generation can only switch at a frame boundary.");
        }

        if (m_extensionRegistry is null)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_PIPELINE_DISCOVERY_DISABLED",
                "The rendering layer was created without reloadable extension discovery; the last-good pipeline remains active.",
                RenderDiagnosticSeverity.Error,
                pipelineAsset.pipelineTypeId));
            return false;
        }

        try
        {
            RenderExtensionRegistry.Snapshot snapshot = m_extensionRegistry.extensions;
            using RenderExtensionRegistry.Generation generation = snapshot.CreateGeneration(pipelineAsset);
            ReplaceGeneration(pipelineAsset, generation.pipeline, generation.features);
            generation.TransferOwnership();
            m_attemptedExtensionVersion = snapshot.typeCacheVersion;
            m_attemptedConfigurationFingerprint =
                RenderExtensionRegistry.GetConfigurationFingerprint(pipelineAsset);
            return true;
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_PIPELINE_ASSET_ACTIVATION_FAILED",
                $"Pipeline asset '{pipelineAsset.sourcePath}' kept the last-good generation: {exception.Message}",
                RenderDiagnosticSeverity.Error,
                pipelineAsset.sourcePath));
            return false;
        }
    }

    /// <inheritdoc />
    public override void OnAttach()
    {
        GraphicsSettings.SetDevice(m_device.capabilities);
        GraphicsSettings.SetPipelineAsset(m_pipelineAsset);
    }

    /// <inheritdoc />
    public override void OnBeforeRender(float deltaTime)
    {
        _ = deltaTime;
        RefreshExtensionsAtFrameBoundary();
        m_device.BeginFrame();
        m_frameOpen = true;
        try
        {
            m_executor.PrepareFrame(m_frameIndex);
            m_world = RenderWorldSnapshot.CaptureLoadedScenes();
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
        {
            return;
        }

        int viewCount = 0;
        try
        {
            lock (m_requestLock)
            {
                m_acceptingCurrentFrame = false;
            }

            IReadOnlyList<IRenderFrameGraphContributor> preparedContributors = PrepareContributors();

            foreach (RenderRequest request in m_currentRequests
                .OrderBy(static value => value.priority)
                .ThenBy(static value => value.name, StringComparer.Ordinal))
            {
                if (BuildAndExecute(request, out int executedPasses))
                {
                    viewCount++;
                    _ = executedPasses;
                }
            }

            BuildAndExecuteContributors(preparedContributors);
        }
        finally
        {
            try
            {
                _ = m_device.EndFrame();
            }
            finally
            {
                m_frameOpen = false;
                m_currentRequests.Clear();
                m_world = null;
                m_frameIndex++;
                GraphicsSettings.SetStatistics(new RenderFrameStatistics(
                    m_frameIndex,
                    viewCount,
                    0,
                    0,
                    0));
            }
        }
    }

    /// <inheritdoc />
    public override void OnDetach()
    {
        GraphicsSettings.SetStatistics(null);
        GraphicsSettings.SetPipelineAsset(null);
        GraphicsSettings.SetDevice(null);
        m_pipeline.Dispose();
        RenderExtensionRegistry.DisposeFeatures(m_features.Values);
        m_features = new Dictionary<string, RenderPipelineFeature>(StringComparer.Ordinal);
        m_extensionRegistry?.Dispose();
    }

    private void RefreshExtensionsAtFrameBoundary()
    {
        if (m_extensionRegistry is null)
            return;

        string fingerprint = RenderExtensionRegistry.GetConfigurationFingerprint(m_pipelineAsset);
        RenderExtensionRegistry.Snapshot snapshot;
        try
        {
            snapshot = m_extensionRegistry.extensions;
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_EXTENSION_REGISTRY_FAILED",
                $"Rendering extension discovery kept the last-good generation: {exception.Message}",
                RenderDiagnosticSeverity.Error,
                m_pipelineAsset.pipelineTypeId));
            return;
        }

        if (snapshot.typeCacheVersion == m_attemptedExtensionVersion
            && string.Equals(fingerprint, m_attemptedConfigurationFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        m_attemptedExtensionVersion = snapshot.typeCacheVersion;
        m_attemptedConfigurationFingerprint = fingerprint;
        try
        {
            using RenderExtensionRegistry.Generation generation = snapshot.CreateGeneration(m_pipelineAsset);
            ReplaceGeneration(m_pipelineAsset, generation.pipeline, generation.features);
            generation.TransferOwnership();
        }
        catch (Exception exception)
        {
            m_diagnostics.Publish(new RenderDiagnostic(
                "RENDER_EXTENSION_GENERATION_FAILED",
                $"Rendering extension activation kept the last-good generation: {exception.Message}",
                RenderDiagnosticSeverity.Error,
                m_pipelineAsset.pipelineTypeId));
        }
    }

    private bool BuildAndExecute(RenderRequest request, out int passCount)
    {
        passCount = 0;
        try
        {
            m_graphGeneration++;
            if (m_graphGeneration == 0)
            {
                m_graphGeneration++;
            }

            var graph = new RenderGraphBuilder(m_graphGeneration, m_device.capabilities);
            var resources = new BuiltinRenderResources();
            var context = new RenderPipelineContext(
                request,
                m_pipelineAsset,
                m_world!,
                request.renderPath,
                graph,
                m_device.capabilities,
                resources,
                m_diagnostics,
                m_executor);
            m_pipeline.Build(context);
            AddFeatures(context);
            RenderGraphCompileResult result = graph.Compile();
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
                    diagnostic.passName ?? diagnostic.resourceName ?? request.name));
            }

            if (result.graph is null)
            {
                return false;
            }

            passCount = result.graph.passes.Count;
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
            return false;
        }
    }

    private IReadOnlyList<IRenderFrameGraphContributor> PrepareContributors()
    {
        IRenderFrameGraphContributor[] snapshot;
        lock (m_contributorLock)
        {
            snapshot = [.. m_contributors];
        }

        List<IRenderFrameGraphContributor> prepared = [];
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

    private void BuildAndExecuteContributors(IReadOnlyList<IRenderFrameGraphContributor> contributors)
    {
        if (contributors.Count == 0)
        {
            return;
        }

        m_graphGeneration++;
        if (m_graphGeneration == 0)
        {
            m_graphGeneration++;
        }

        var graph = new RenderGraphBuilder(m_graphGeneration, m_device.capabilities);
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
                diagnostic.passName ?? diagnostic.resourceName ?? "Frame Contributors"));
        }

        if (result.graph is not null && result.graph.passes.Count != 0)
        {
            m_device.Execute(result.graph, m_frameIndex);
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

    private void AddFeatures(RenderPipelineContext context)
    {
        foreach (RenderFeatureConfiguration configuration in m_pipelineAsset.features)
        {
            if (!configuration.enabled
                || !m_features.TryGetValue(configuration.featureTypeId, out RenderPipelineFeature? feature))
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

    private static void ConfigureFeatures(
        RenderPipelineAsset pipelineAsset,
        IReadOnlyDictionary<string, RenderPipelineFeature> features)
    {
        foreach (RenderFeatureConfiguration configuration in pipelineAsset.features)
        {
            if (configuration.enabled
                && features.TryGetValue(configuration.featureTypeId, out RenderPipelineFeature? feature))
            {
                feature.Configure(configuration);
            }
        }
    }
}
