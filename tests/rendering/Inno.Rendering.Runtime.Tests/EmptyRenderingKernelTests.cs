using Inno.References;
using Inno.Runtime.Contracts;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Extensibility.Modules;
using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Runtime.Tests;

public sealed class EmptyRenderingKernelTests
{
    [Theory]
    [InlineData("Pbr")]
    [InlineData("Forward")]
    [InlineData("Deferred")]
    [InlineData("DirectionalLight")]
    [InlineData("MeshRenderer")]
    public void ProductionRenderingAssemblyDoesNotDeclareConcreteRenderingWorldviews(string forbiddenName)
    {
        Type[] types = typeof(RenderPipeline).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => type.Name.Contains(forbiddenName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenShaderProtocolsRemainBackendNeutral()
    {
        Type[] publicTypes = typeof(ShaderContractId).Assembly.GetExportedTypes();
        Assert.DoesNotContain(publicTypes, static type =>
            type.FullName?.Contains("Bgfx", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(publicTypes, static type => type == typeof(ShaderContractId));
        Assert.Contains(publicTypes, static type => type == typeof(ShaderPassRoleId));
    }
}

public sealed class RenderRuntimeGenerationTests : IDisposable
{
    private readonly string m_cacheDirectory = Path.Combine(
        Path.GetTempPath(),
        "InnoRenderRuntimeTests",
        Guid.NewGuid().ToString("N"));
    private readonly IdentityAllocator m_identities;
    private readonly IDisposable m_identityScope;
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;

    public RenderRuntimeGenerationTests()
    {
        _ = typeof(TextureAsset);
        m_identities = new IdentityAllocator();
        m_identityScope = m_identities.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = m_cacheDirectory });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        ResourceProbePipeline.action = null;
        DisposablePipeline.Reset();
        PendingFeature.Reset();
        TestRequestProvider.Reset();
        UploadPipeline.Reset();
        TexturePrewarmPipeline.texture = null;
        PendingShaderPipeline.Reset();
        ReadbackPipeline.Reset();
        PresentationPipeline.Reset();
    }

    public void Dispose()
    {
        ResourceProbePipeline.action = null;
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void GeometryCandidatePublishesACompletePairAndPreservesOldPairAfterPartialAllocationFailure()
    {
        IRenderDevice backend = TestDeviceProxy.Create(out TestDeviceProxy device);
        using var runtime = new RenderRuntime(m_types, backend, new TestDiagnosticSink());
        var owner = new AssetRuntimeOwner();
        var geometry = new GeometryAsset(3, 3, 1, default, default);
        m_identities.Register(geometry);
        byte[] bytes = GeometryArtifact.Encode(new GeometryData(
            [new(default, default, default, default), new(default, default, default, default), new(default, default, default, default)],
            [0, 1, 2], [new GeometrySection(0, 3)]));
        owner.Initialize(geometry, AssetPath.Project("triangle.obj"), "first", bytes, false, 1);
        RenderGeometry? first = null;
        ResourceProbePipeline.action = resources => resources.TryResolveGeometry(geometry, out first);
        RunResourceFrame(runtime);
        Assert.NotNull(first);
        Assert.Equal(2, device.createdBuffers.Count);
        device.failBufferCreationAt = 4;
        owner.Initialize(geometry, AssetPath.Project("triangle.obj"), "second", bytes, false, 2);
        RenderGeometry? failed = null;
        ResourceProbePipeline.action = resources => resources.TryResolveGeometry(geometry, out failed);
        RunResourceFrame(runtime);
        Assert.NotNull(failed);
        Assert.Equal(first!.vertexBuffer, failed!.vertexBuffer);
        Assert.Equal(first.indexBuffer, failed.indexBuffer);
        Assert.Equal(device.createdBuffers[2], Assert.Single(device.destroyedBuffers));
        device.failBufferCreationAt = 0;
        RunResourceFrame(runtime);
        Assert.NotEqual(first.vertexBuffer, failed.vertexBuffer);
        Assert.NotEqual(first.indexBuffer, failed.indexBuffer);
        Assert.Equal(3, device.destroyedBuffers.Count);
        runtime.Dispose();
        Assert.Equal(device.createdBuffers.Count, device.destroyedBuffers.Count);
        owner.Release(geometry);
        m_identities.Unregister(geometry);
        ResourceProbePipeline.action = null;
    }

    [Fact]
    public void PersistentResourceAdmissionAndReplacementStayBoundedAcrossFrames()
    {
        IRenderDevice backend = TestDeviceProxy.Create(out TestDeviceProxy device);
        using var runtime = new RenderRuntime(m_types, backend, new TestDiagnosticSink(),
            resourceLimits: new RenderResourceLimits { resourcesPerKind = 1 });
        var descriptor = new PersistentBufferDescriptor(new RenderBufferDescriptor(4, 4, RenderBufferUsage.Storage));
        int rejected = 0;
        for (int frame = 0; frame < 256; frame++)
        {
            long revision = frame;
            ResourceProbePipeline.action = resources =>
            {
                _ = resources.AcquireBuffer(new RenderPersistentResourceId("owned"), revision, descriptor, new byte[16], "owned");
                try { resources.AcquireBuffer(new RenderPersistentResourceId("overflow"), revision, descriptor, new byte[16], "overflow"); }
                catch (InvalidOperationException) { rejected++; }
            };
            RunResourceFrame(runtime);
            Assert.Equal(1, runtime.resourceStatistics.activeResources);
        }
        Assert.Equal(256, rejected);
        Assert.Equal(256, runtime.resourceStatistics.rejectedResources);
        runtime.Dispose();
        Assert.Equal(256, device.createdBuffers.Count);
        Assert.Equal(256, device.destroyedBuffers.Count);
        ResourceProbePipeline.action = null;
    }

    [Fact]
    public async Task ReadbackAdmissionIsRejectedBeforeAllocatingASecondNativeOperation()
    {
        var device = new RecordingRenderDevice(supportsReadback: true);
        using var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink(),
            resourceLimits: new RenderResourceLimits { pendingReadbacks = 1 });
        Task<RenderTextureReadbackResult>? readback = null;
        bool rejected = false;
        ResourceProbePipeline.action = resources =>
        {
            readback = resources.ReadTextureAsync(device.textureHandle).AsTask();
            try { _ = resources.ReadTextureAsync(device.textureHandle); }
            catch (InvalidOperationException) { rejected = true; }
        };
        RunResourceFrame(runtime);
        ResourceProbePipeline.action = null;
        Assert.True(rejected);
        Assert.Equal(1, runtime.resourceStatistics.pendingReadbacks);
        Assert.Equal(1, runtime.resourceStatistics.rejectedReadbacks);
        runtime.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => readback!);
        Assert.Equal(1, device.cancelReadbackCount);
    }

    private static void RunResourceFrame(RenderRuntime runtime)
    {
        runtime.Submit(CreateRequest("Resource ownership", new RenderPipelineAsset { pipelineTypeId = ResourceProbePipeline.extensionId }));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class ResourceProbePipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.resource-owner";
        internal static Action<IRenderResourceService>? action;
        public override void Build(RenderPipelineContext context)
        {
            action?.Invoke(context.resourceService);
            context.graph.AddRasterPass("Resources", new RenderPhaseId("tests.resources"), 0, static (_, _) => { }).HasSideEffect();
        }
    }

    [Fact]
    public void SuccessfulTypeCacheChangeRetiresUnrequestedPipelineAtFrameBoundary()
    {
        IRenderDevice device = TestDeviceProxy.Create(out _);
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        var asset = new RenderPipelineAsset { pipelineTypeId = DisposablePipeline.extensionId };

        Assert.True(runtime.TryActivateDefaultPipeline(asset));
        Assert.Equal(1, DisposablePipeline.createdCount);
        Assert.Equal(0, DisposablePipeline.disposedCount);

        m_types.Rebuild();
        BeginRenderFrame(runtime, 0f);

        Assert.Equal(1, DisposablePipeline.disposedCount);

        runtime.AfterRender(default);
        runtime.EndFrame(default);
        runtime.Detach();
    }

    [Fact]
    public void PendingProviderRetirementIsRetriedBeforeTheSnapshotIsReleased()
    {
        using var runtime = new RenderRuntime(m_types, TestDeviceProxy.Create(out _), new TestDiagnosticSink());
        Assert.True(runtime.TryActivateDefaultPipeline(new RenderPipelineAsset { pipelineTypeId = DisposablePipeline.extensionId }));
        TestRequestProvider.pendingRetirements = 2;

        m_types.Rebuild();

        Assert.Equal(3, TestRequestProvider.retirementAttempts);
        Assert.True(runtime.TryActivateDefaultPipeline(new RenderPipelineAsset { pipelineTypeId = DisposablePipeline.extensionId }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidInitialContributorsFailBeforeRegisteringAnExtensionOwner(bool duplicate)
    {
        var contributor = new SideEffectContributor();
        IRenderFrameGraphContributor[] contributors = duplicate ? [contributor, contributor] : [null!];
        Assert.Throws<ArgumentException>(() => new RenderRuntime(
            m_types, TestDeviceProxy.Create(out _), new TestDiagnosticSink(), contributors));
        m_types.Rebuild();
        Assert.Equal(0, TestRequestProvider.retirementAttempts);
    }

    [Fact]
    public void PendingFeatureRetirementKeepsItsPipelineAliveUntilQuiescent()
    {
        var runtime = new RenderRuntime(m_types, TestDeviceProxy.Create(out TestDeviceProxy device), new TestDiagnosticSink());
        Assert.True(runtime.TryActivateDefaultPipeline(new RenderPipelineAsset
        {
            pipelineTypeId = DisposablePipeline.extensionId,
            features = [new RenderFeatureConfiguration(PendingFeature.extensionId)]
        }));
        PendingFeature.pendingRetirements = 2;
        DisposablePipeline.pendingRetirements = 1;

        runtime.Dispose();
        runtime.Dispose();

        Assert.Equal(3, PendingFeature.retirementAttempts);
        Assert.Equal(2, DisposablePipeline.retirementAttempts);
        Assert.Equal(1, DisposablePipeline.disposedCount);
        Assert.Equal(1, device.endFrameCount);
        Assert.Throws<ObjectDisposedException>(() => runtime.Submit(CreateRequest("Late", new RenderPipelineAsset())));
    }

    [Fact]
    public void RejectedPipelineWaitsForItsCandidateToRetireBeforeKeepingLastGood()
    {
        using var runtime = new RenderRuntime(m_types, TestDeviceProxy.Create(out _), new TestDiagnosticSink());
        var asset = new RenderPipelineAsset { pipelineTypeId = DisposablePipeline.extensionId };
        Assert.True(runtime.TryActivateDefaultPipeline(asset));
        DisposablePipeline.rejectConfiguration = true;
        DisposablePipeline.pendingRetirements = 2;
        asset.pipelineState = new SerializedRenderExtensionState(Guid.NewGuid(), [1]);

        Assert.True(runtime.TryActivateDefaultPipeline(asset));

        Assert.Equal(3, DisposablePipeline.retirementAttempts);
        Assert.Equal(1, DisposablePipeline.disposedCount);
        DisposablePipeline.rejectConfiguration = false;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadRetirementDrainsPendingBeforeFinishingTheTransaction(bool commit)
    {
        using var runtime = new RenderRuntime(m_types, TestDeviceProxy.Create(out _), new TestDiagnosticSink());
        Assert.True(runtime.TryActivateDefaultPipeline(new RenderPipelineAsset { pipelineTypeId = DisposablePipeline.extensionId }));
        IRenderRuntimeReloadTransaction transaction = runtime.BeginExtensionReload();
        transaction.Prepare();
        transaction.Activate();
        DisposablePipeline.pendingRetirements = 2;

        if (commit)
            transaction.Complete();
        else
            transaction.Rollback();

        Assert.Equal(3, DisposablePipeline.retirementAttempts);
        Assert.Equal(1, DisposablePipeline.disposedCount);
        runtime.BeginExtensionReload().Rollback();
    }

    [Fact]
    public void TargetCapacityRejectsBeforeNativeAllocationAndReopensAfterRelease()
    {
        IRenderDevice backend = TestDeviceProxy.Create(out TestDeviceProxy device);
        using var targets = new RenderTargetStore(backend, capacity: 1);
        backend.BeginFrame();
        var descriptor = new RenderTextureDescriptor(16, 16, RenderTextureFormat.RGBA8, RenderTextureUsage.ColorAttachment);
        var first = new RenderTexture("First", descriptor);
        var second = new RenderTexture("Second", descriptor);
        var graph = new RenderGraphBuilder(1, backend.capabilities);
        targets.Import(graph, first);
        Assert.Throws<InvalidOperationException>(() => targets.Import(graph, second));
        Assert.Single(device.createdTextures);
        Assert.Equal(1, targets.rejectedCount);
        targets.Release(first);
        targets.PrepareFrame();
        targets.Import(new RenderGraphBuilder(2, backend.capabilities), second);
        Assert.Equal(1, targets.count);
        targets.Dispose();
        Assert.Equal(device.createdTextures.Count, device.destroyedTextures.Count);
        backend.EndFrame();
    }

    [Fact]
    public void UploadAdmissionResetsEachFrameWithoutGrowingResidentPages()
    {
        IRenderDevice backend = TestDeviceProxy.Create(out TestDeviceProxy device);
        using var runtime = new RenderRuntime(m_types, backend, new TestDiagnosticSink(), resourceLimits:
            new RenderResourceLimits { uploadPages = 1, uploadResidentBytes = 4096, uploadBytesPerFrame = 36 });
        for (int frame = 0; frame < 256; frame++)
        {
            runtime.Submit(CreateRequest("Upload budget", new RenderPipelineAsset { pipelineTypeId = UploadPipeline.extensionId }));
            BeginRenderFrame(runtime, 0f);
            runtime.AfterRender(default);
            runtime.EndFrame(default);
            Assert.Equal(1, runtime.resourceStatistics.uploadPages);
            Assert.Equal(36, runtime.resourceStatistics.uploadedFrameBytes);
        }
        Assert.Equal(256, runtime.resourceStatistics.rejectedUploads);
        Assert.Single(device.createdBuffers);
        runtime.Dispose();
        Assert.Single(device.destroyedBuffers);
    }

    [Fact]
    public void TargetRetirementRetainsProgressAndOrdinaryErrorsAcrossPendingRetries()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var targets = new RenderTargetStore(device);
        device.BeginFrame();
        var descriptor = new RenderTextureDescriptor(16, 16, RenderTextureFormat.RGBA8, RenderTextureUsage.ColorAttachment);
        var graph = new RenderGraphBuilder(1, device.capabilities);
        targets.Import(graph, new RenderTexture("First", descriptor));
        targets.Import(graph, new RenderTexture("Second", descriptor));
        targets.Import(graph, new RenderTexture("Third", descriptor));
        proxy.failFirstTextureRetirement = true;
        proxy.pendingTextureRetirement = proxy.createdTextures[1];

        Assert.Throws<RetirementPendingException>(targets.Dispose);
        Assert.Equal(2, proxy.textureRetirementAttempts);
        Assert.True(proxy.frameOpen);
        Assert.Throws<ObjectDisposedException>(() => targets.Import(graph, new RenderTexture("Late", descriptor)));
        Assert.Throws<AggregateException>(targets.Dispose);
        Assert.Equal(4, proxy.textureRetirementAttempts);
        Assert.Equal(proxy.createdTextures.Skip(1), proxy.destroyedTextures);
        targets.Dispose();
        Assert.Equal(4, proxy.textureRetirementAttempts);
        device.EndFrame();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalPipelineRetirementBlocksAdmissionAndPreservesLowerOwners(bool duringFrame)
    {
        var modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_cacheDirectory, "FaultedRendering")
        });
        var types = new TypeCatalog(modules);
        var runtime = new RenderRuntime(types, TestDeviceProxy.Create(out TestDeviceProxy device), new TestDiagnosticSink());
        var asset = new RenderPipelineAsset { pipelineTypeId = DisposablePipeline.extensionId };
        Assert.True(runtime.TryActivateDefaultPipeline(asset));
        DisposablePipeline.retirementExpired = true;
        try
        {
            if (duringFrame)
            {
                asset.pipelineState = new SerializedRenderExtensionState(Guid.NewGuid(), [1]);
                DisposablePipeline.rejectConfiguration = true;
                runtime.Submit(CreateRequest("Faulting candidate", asset));
                BeginRenderFrame(runtime, 0f);
                Assert.Throws<RetirementTimeoutException>(() => runtime.AfterRender(default));
                Assert.True(device.frameOpen);
            }
            else
                Assert.Throws<RetirementTimeoutException>(runtime.Dispose);
            Assert.Throws<InvalidOperationException>(() => runtime.Submit(CreateRequest("Late", new RenderPipelineAsset())));
            Assert.Throws<InvalidOperationException>(() => runtime.RegisterContributor(new SideEffectContributor()));
            Assert.Throws<InvalidOperationException>(() => runtime.BeginExtensionReload());
            Assert.ThrowsAny<Exception>(() => types.Rebuild());
            Assert.Equal(1, DisposablePipeline.retirementAttempts);
            Assert.Equal(0, TestRequestProvider.retirementAttempts);
            Assert.Equal(0, device.endFrameCount);

            DisposablePipeline.retirementExpired = false;
            Assert.ThrowsAny<RetirementPendingException>(runtime.Dispose);
            Assert.ThrowsAny<RetirementPendingException>(types.Dispose);
            Assert.ThrowsAny<RetirementPendingException>(modules.Dispose);
            Assert.Equal(1, DisposablePipeline.retirementAttempts);
            Assert.Equal(0, device.endFrameCount);
        }
        finally
        {
            DisposablePipeline.retirementExpired = false;
            DisposablePipeline.rejectConfiguration = false;
        }
    }

    [Fact]
    public void RuntimeKeepsItsMaintenanceFrameOpenUntilTargetsFinishRetiring()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        var target = new RenderTexture("Pending Target", new RenderTextureDescriptor(
            16, 16, RenderTextureFormat.RGBA8, RenderTextureUsage.ColorAttachment));
        runtime.Submit(new RenderRequest("Target", RenderTarget.FromTexture(target),
            new RenderViewport(0, 0, 16, 16), new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId }));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
        proxy.pendingTextureRetirement = Assert.Single(proxy.createdTextures);

        runtime.Dispose();

        Assert.Equal(2, proxy.textureRetirementAttempts);
        Assert.Single(proxy.destroyedTextures);
        Assert.False(proxy.frameOpen);
        Assert.Equal(2, proxy.endFrameCount);
    }

    [Fact]
    public void RemovingAndRestoringPluginRenderingCommitsTheSameUnavailableStateAsColdStart()
    {
        const string extensionId = "tests.runtime.reloadable-plugin";
        AssemblyLoadRequest plugin = CreateRenderingPluginRequest();
        AssemblyModuleHandle activeModule = m_modules.Load(plugin);
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var diagnostics = new TestDiagnosticSink();
        using var runtime = new RenderRuntime(m_types, device, diagnostics);
        var asset = new RenderPipelineAsset { pipelineTypeId = extensionId };
        var featureAsset = new RenderPipelineAsset
        {
            pipelineTypeId = SideEffectPipeline.extensionId,
            features = [new RenderFeatureConfiguration("tests.runtime.reloadable-plugin-feature")]
        };

        runtime.Submit(CreateRequest("Before Removal", asset));
        runtime.Submit(CreateRequest("Feature Before Removal", featureAsset));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
        Assert.Equal(1, proxy.executeCount);
        proxy.ReleaseRecordedGraph();

        QueueCollectiblePayload(runtime, m_types, asset);
        (AssemblyUnloadMonitor removalMonitor, IRenderRuntimeReloadTransaction renderingRemoval) =
            RemoveRenderingPlugin(runtime, m_modules, plugin);

        ForceCollection();
        Assert.True(removalMonitor.isCompleted);
        GC.KeepAlive(renderingRemoval);

        runtime.Submit(CreateRequest("While Unavailable", asset));
        runtime.Submit(CreateRequest("Feature While Unavailable", featureAsset));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
        Assert.Equal(1, proxy.executeCount);
        Assert.DoesNotContain(
            diagnostics.items,
            static diagnostic => diagnostic.code == "RENDER_EXTENSION_RELOAD_REJECTED");

        IRenderRuntimeReloadTransaction renderingRecovery = runtime.BeginExtensionReload();
        using (AssemblyReloadSession recovery = m_modules.BeginReload([plugin]))
        {
            activeModule = recovery.context.module;
            recovery.Activate();
            renderingRecovery.Prepare();
            renderingRecovery.Activate();
            _ = recovery.Complete();
            renderingRecovery.Complete();
        }

        runtime.Submit(CreateRequest("After Recovery", asset));
        runtime.Submit(CreateRequest("Feature After Recovery", featureAsset));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
        Assert.Equal(2, proxy.executeCount);

        runtime.Detach();
        _ = m_modules.Unload(activeModule);
    }

    [Fact]
    public void RemovingUnusedRenderingPluginReleasesItsAssemblyContext()
    {
        AssemblyLoadRequest plugin = CreateRenderingPluginRequest();
        _ = m_modules.Load(plugin);
        AssemblyUnloadMonitor monitor = RemoveUnusedPlugin(m_modules, plugin);

        ForceCollection();

        Assert.True(monitor.isCompleted);
    }

    [Fact]
    public void FrameStatisticsReportBackendCommandsAndGraphCulling()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        proxy.frameCounters = new RenderDeviceFrameCounters(3, 2);
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        using IDisposable executionScope = runtime.EnterExecutionScope();
        var asset = new RenderPipelineAsset { pipelineTypeId = StatisticsPipeline.extensionId };
        runtime.Attach();
        Assert.True(runtime.TryActivateDefaultPipeline(asset));
        runtime.Submit(new RenderRequest(
            "Statistics",
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, 64, 64),
            asset));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        RenderFrameStatistics statistics = Assert.IsType<RenderFrameStatistics>(GraphicsSettings.frameStatistics);
        Assert.Equal(1, statistics.viewCount);
        Assert.Equal(3, statistics.drawCount);
        Assert.Equal(2, statistics.dispatchCount);
        Assert.Equal(1, statistics.culledPassCount);
        runtime.Detach();
    }

    [Fact]
    public void GraphicsFacadeResolvesTheCurrentlyBoundRenderingRuntime()
    {
        IRenderDevice firstDevice = TestDeviceProxy.Create(out TestDeviceProxy firstProxy);
        IRenderDevice secondDevice = TestDeviceProxy.Create(out TestDeviceProxy secondProxy);
        firstProxy.frameCounters = new RenderDeviceFrameCounters(2, 0);
        secondProxy.frameCounters = new RenderDeviceFrameCounters(7, 1);
        using var first = new RenderRuntime(m_types, firstDevice, new TestDiagnosticSink());
        using var second = new RenderRuntime(m_types, secondDevice, new TestDiagnosticSink());
        var asset = new RenderPipelineAsset { pipelineTypeId = StatisticsPipeline.extensionId };

        using (first.EnterExecutionScope())
        {
            first.Attach();
            Assert.True(first.TryActivateDefaultPipeline(asset));
            first.Submit(CreateRequest("First Runtime", asset));
            BeginRenderFrame(first, 0f);
            first.AfterRender(default);
        first.EndFrame(default);
            Assert.Equal(2, Assert.IsType<RenderFrameStatistics>(GraphicsSettings.frameStatistics).drawCount);

            using (second.EnterExecutionScope())
            {
                second.Attach();
                Assert.True(second.TryActivateDefaultPipeline(asset));
                second.Submit(CreateRequest("Second Runtime", asset));
                BeginRenderFrame(second, 0f);
                second.AfterRender(default);
        second.EndFrame(default);
                Assert.Equal(7, Assert.IsType<RenderFrameStatistics>(GraphicsSettings.frameStatistics).drawCount);
            }

            Assert.Equal(2, Assert.IsType<RenderFrameStatistics>(GraphicsSettings.frameStatistics).drawCount);
        }

        Assert.Null(GraphicsSettings.frameStatistics);
        Assert.Throws<InvalidOperationException>(() => GraphicsSettings.defaultPipeline = asset);
        second.Detach();
        first.Detach();
    }

    [Fact]
    public void RequestProviderProducesCurrentFrameWorkThroughPublicPluginApi()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var asset = new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId };
        TestRequestProvider.pipeline = asset;
        TestRequestProvider.enabled = true;
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        runtime.Attach();

        BeginRenderFrame(runtime, 1f / 60f);
        runtime.Render(default);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(1, TestRequestProvider.submitCount);
        Assert.Equal(1, proxy.executeCount);
        CompiledRenderPass pass = Assert.Single(Assert.IsType<CompiledRenderGraph>(proxy.lastGraph).passes);
        Assert.Equal("Request[0] Provider Request/Visible", pass.name);
        runtime.Detach();
    }

    [Fact]
    public void RequestProviderReceivesExplicitHostContentScope()
    {
        IRenderDevice device = TestDeviceProxy.Create(out _);
        Guid contentId = Guid.Parse("3aee0ced-b598-4366-ab45-a6ef8e0feb30");
        var content = new TestContent();
        var identities = new IdentityAllocator();
        identities.Register(content, contentId);
        var scope = new ContentReadScope([content.identity], contentId);
        Assert.True(scope.TryGetValue(contentId, out TestContent? resolved));
        Assert.Same(content, resolved);
        TestRequestProvider.enabled = true;
        var runtime = new RenderRuntime(
            m_types,
            device,
            new TestDiagnosticSink(),
            contentScopeProvider: () => scope);
        runtime.Attach();

        BeginRenderFrame(runtime, 0f);
        runtime.Render(default);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Same(scope, TestRequestProvider.lastContent);
        Assert.Throws<ObjectDisposedException>(() => scope.TryGetValue(contentId, out TestContent? _));
        runtime.Detach();
    }

    [Fact]
    public void PrimaryPresentationViewportIsSharedAndLetterboxSurfaceIsCleared()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        proxy.presentationSize = new RenderPresentationSize(1000, 1000);
        TestRequestProvider.enabled = true;
        var runtime = new RenderRuntime(
            m_types,
            device,
            new TestDiagnosticSink(),
            primaryPresentationViewportProvider: static size => new RenderViewport(
                0,
                (size.height - 562) / 2,
                size.width,
                562));
        runtime.Attach();

        BeginRenderFrame(runtime, 0f);
        runtime.Render(default);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(new RenderViewport(0, 219, 1000, 562), TestRequestProvider.lastPresentationViewport);
        CompiledRenderPass pass = Assert.Single(Assert.IsType<CompiledRenderGraph>(proxy.lastGraph).passes);
        Assert.Equal("Primary Presentation Background", pass.name);
        Assert.True(pass.clearsPresentationTarget);
        Assert.Equal(new RenderClearColor(0f, 0f, 0f, 1f), pass.presentationClearColor);
        runtime.Detach();
    }

    [Fact]
    public void MultipleRequestsAndContributorsCompileAndExecuteAsOneFrameGraph()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var asset = new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId };
        var contributor = new SideEffectContributor();
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink(), [contributor]);
        runtime.Submit(CreateRequest("Second", asset, priority: 10));
        runtime.Submit(CreateRequest("First", asset, priority: -10));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(1, proxy.executeCount);
        string[] names = Assert.IsType<CompiledRenderGraph>(proxy.lastGraph).passes
            .Select(static pass => pass.name)
            .ToArray();
        string[] expectedNames =
        [
            "Request[0] First/Visible",
            "Request[1] Second/Visible",
            "Contributor[0] SideEffectContributor/Overlay"
        ];
        Assert.Equal(expectedNames, names);
        runtime.Detach();
    }

    [Fact]
    public void OverlappingRequestsPreserveEarlierPresentationLayersInSchedulingOrder()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var asset = new RenderPipelineAsset { pipelineTypeId = PresentationPipeline.extensionId };
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        runtime.Submit(CreateRequest("Overlay", asset, priority: 100));
        runtime.Submit(CreateRequest("Base", asset, priority: 0));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(
            new[]
            {
                new PresentationObservation("Base", false),
                new PresentationObservation("Overlay", true)
            },
            PresentationPipeline.observations);
        Assert.Equal(1, proxy.executeCount);
        runtime.Detach();
    }

    [Fact]
    public void DisjointViewportsCanInitializeTheSamePresentationTargetIndependently()
    {
        IRenderDevice device = TestDeviceProxy.Create(out _);
        var asset = new RenderPipelineAsset { pipelineTypeId = PresentationPipeline.extensionId };
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        runtime.Submit(new RenderRequest(
            "Left",
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, 32, 64),
            asset));
        runtime.Submit(new RenderRequest(
            "Right",
            RenderTarget.backbuffer,
            new RenderViewport(32, 0, 32, 64),
            asset));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(
            new[]
            {
                new PresentationObservation("Left", false),
                new PresentationObservation("Right", false)
            },
            PresentationPipeline.observations);
        runtime.Detach();
    }

    [Fact]
    public void FailedRequestDoesNotClaimThePresentationTarget()
    {
        IRenderDevice device = TestDeviceProxy.Create(out _);
        var failed = new RenderPipelineAsset { pipelineTypeId = ThrowingPipeline.extensionId };
        var valid = new RenderPipelineAsset { pipelineTypeId = PresentationPipeline.extensionId };
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        runtime.Submit(CreateRequest("Failed", failed, priority: 0));
        runtime.Submit(CreateRequest("Recovery", valid, priority: 100));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(
            new[] { new PresentationObservation("Recovery", false) },
            PresentationPipeline.observations);
        runtime.Detach();
    }

    [Fact]
    public void ReplacedAndReleasedTargetsRemainValidAcrossPreparedPresentationFrames()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var registry = new RenderTargetStore(device);
        var target = new RenderTexture(
            "Deferred Target",
            new RenderTextureDescriptor(
                32,
                32,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));

        device.BeginFrame();
        registry.PrepareFrame();
        _ = registry.Import(new RenderGraphBuilder(1, device.capabilities), target);
        _ = device.EndFrame();
        PersistentTextureHandle first = Assert.Single(proxy.createdTextures);

        target.Resize(new RenderTextureDescriptor(
            64,
            64,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));
        device.BeginFrame();
        registry.PrepareFrame();
        _ = registry.Import(new RenderGraphBuilder(2, device.capabilities), target);
        _ = device.EndFrame();
        PersistentTextureHandle second = proxy.createdTextures[1];
        Assert.Empty(proxy.destroyedTextures);

        device.BeginFrame();
        registry.PrepareFrame();
        _ = device.EndFrame();
        Assert.Empty(proxy.destroyedTextures);

        device.BeginFrame();
        registry.PrepareFrame();
        _ = device.EndFrame();
        Assert.Equal(new[] { first }, proxy.destroyedTextures);

        registry.Release(target);
        for (int frame = 0; frame < 2; frame++)
        {
            device.BeginFrame();
            registry.PrepareFrame();
            _ = device.EndFrame();
        }
        Assert.Equal(new[] { first }, proxy.destroyedTextures);

        device.BeginFrame();
        registry.PrepareFrame();
        registry.Dispose();
        _ = device.EndFrame();
        Assert.Equal(new[] { first, second }, proxy.destroyedTextures);
    }

    [Fact]
    public void DetachUsesADeviceSafetyFrameToReleaseResidentTargets()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        var asset = new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId };
        var target = new RenderTexture(
            "Detach Target",
            new RenderTextureDescriptor(
                16,
                16,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.ColorAttachment));
        runtime.Submit(new RenderRequest(
            "Detach",
            RenderTarget.FromTexture(target),
            new RenderViewport(0, 0, 16, 16),
            asset));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
        PersistentTextureHandle resident = Assert.Single(proxy.createdTextures);

        runtime.Detach();

        Assert.Equal(new[] { resident }, proxy.destroyedTextures);
        Assert.False(proxy.frameOpen);
        Assert.Equal(2, proxy.endFrameCount);
    }

    [Fact]
    public void FailedRequestRollsBackWithoutDiscardingOtherRequests()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var diagnostics = new TestDiagnosticSink();
        var failed = new RenderPipelineAsset { pipelineTypeId = ThrowingPipeline.extensionId };
        var valid = new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId };
        var runtime = new RenderRuntime(m_types, device, diagnostics);
        runtime.Submit(CreateRequest("A Failed", failed));
        runtime.Submit(CreateRequest("B Valid", valid));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(1, proxy.executeCount);
        CompiledRenderPass pass = Assert.Single(Assert.IsType<CompiledRenderGraph>(proxy.lastGraph).passes);
        Assert.Equal("Request[1] B Valid/Visible", pass.name);
        Assert.Contains(diagnostics.items, diagnostic => diagnostic.code == "RENDER_REQUEST_FAILED");
        runtime.Detach();
    }

    [Fact]
    public void FrameUploadsReusePagesAndResetSliceOffsetsBetweenFrames()
    {
        var device = new RecordingRenderDevice();
        var asset = new RenderPipelineAsset { pipelineTypeId = UploadPipeline.extensionId };
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());

        runtime.Submit(CreateRequest("Upload A", asset));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(1, device.createBufferCount);
        Assert.Equal(new[] { 0, 3 }, device.bufferUpdateOffsets.ToArray());
        Assert.Equal(0, UploadPipeline.firstSlice.firstElement);
        Assert.Equal(3, UploadPipeline.secondSlice.firstElement);
        Assert.Equal(3, UploadPipeline.secondSlice.elementCount);

        UploadPipeline.singleUpload = true;
        runtime.Submit(CreateRequest("Upload B", asset));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(1, device.createBufferCount);
        Assert.Equal(new[] { 0, 3, 0 }, device.bufferUpdateOffsets.ToArray());
        Assert.Equal(0, UploadPipeline.firstSlice.firstElement);
        runtime.Detach();
    }

    [Fact]
    public void TexturePrewarmRunsOutsideTheRenderFrameAndDoesNotDelaySubmission()
    {
        var texture = new TextureAsset(1, 1, TextureColorSpace.Srgb, "png");
        Assert.True(m_identities.Register(texture));
        TexturePrewarmPipeline.texture = texture;

        var compiler = new DelayedTextureArtifactSource();
        var device = new RecordingRenderDevice();
        var asset = new RenderPipelineAsset { pipelineTypeId = TexturePrewarmPipeline.extensionId };
        var artifacts = new DelayedArtifactProvider(compiler);
        var runtime = new RenderRuntime(
            m_types,
            device,
            new TestDiagnosticSink(),
            targetArtifacts: artifacts);
        runtime.Submit(CreateRequest("Prewarm", asset));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(1, device.endFrameCount);
        Assert.True(compiler.started.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(compiler.isCompleted);
        compiler.Complete([1, 2, 3, 4]);
        runtime.Detach();
    }

    [Fact]
    public void PendingShaderCompilationDoesNotPublishAnUnavailableArtifactError()
    {
        var contract = new ShaderContractId("tests.pending.contract");
        var role = new ShaderPassRoleId("draw");
        var pass = new ShaderPassDefinition("draw", ShaderProgramKind.Raster);
        var shader = new ShaderAsset();
        Assert.True(m_identities.Register(shader));
        shader.SetDefinition(
            new ShaderDefinition(
                "Pending Shader",
                [],
                [],
                [pass],
                [new ShaderTechniqueDefinition(
                    new ShaderTechniqueId("default"),
                    contract,
                    [new ShaderTechniquePass(role, pass.name)])]),
            m_serialization);
        PendingShaderPipeline.material = new MaterialAsset { shader = shader };
        PendingShaderPipeline.contract = contract;
        PendingShaderPipeline.role = role;

        var diagnostics = new TestDiagnosticSink();
        var compiler = new DelayedTextureArtifactSource();
        var runtime = new RenderRuntime(
            m_types,
            new RecordingRenderDevice(),
            diagnostics,
            targetArtifacts: new DelayedArtifactProvider(compiler));
        var asset = new RenderPipelineAsset { pipelineTypeId = PendingShaderPipeline.extensionId };
        runtime.Submit(CreateRequest("Pending Shader", asset));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.False(PendingShaderPipeline.resolved);
        Assert.DoesNotContain(
            diagnostics.items,
            static diagnostic => diagnostic.code == "RENDER_SHADER_TARGET_UNAVAILABLE");
        runtime.Detach();
    }

    [Fact]
    public async Task PersistentTextureRegionUpdateAndReadbackCompleteAcrossFrameBoundaries()
    {
        var device = new RecordingRenderDevice(supportsReadback: true);
        ReadbackPipeline.texture = device.textureHandle;
        var asset = new RenderPipelineAsset { pipelineTypeId = ReadbackPipeline.extensionId };
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        runtime.Submit(CreateRequest("Readback", asset));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(new RenderTextureRegion(0, 1, 1, 0, 2, 2), device.updatedRegion);
        Assert.Equal(Enumerable.Range(0, 16).Select(static value => (byte)value), device.updatedBytes);
        Task<RenderTextureReadbackResult> readback = Assert.IsType<Task<RenderTextureReadbackResult>>(
            ReadbackPipeline.readback);
        Assert.False(readback.IsCompleted);

        device.readbackReady = true;
        BeginRenderFrame(runtime, 0f);
        RenderTextureReadbackResult result = await readback;
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(16, result.rowPitch);
        Assert.Equal(64, result.data.Length);
        Assert.All(result.data.ToArray(), static value => Assert.Equal((byte)37, value));
        runtime.Detach();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingUploadOrReadbackRetirementPreservesDependentWork(bool pendingReadback)
    {
        var device = new RecordingRenderDevice(supportsReadback: true);
        ReadbackPipeline.texture = device.textureHandle;
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        runtime.Submit(CreateRequest("Upload", new RenderPipelineAsset { pipelineTypeId = UploadPipeline.extensionId }));
        runtime.Submit(CreateRequest("Readback", new RenderPipelineAsset { pipelineTypeId = ReadbackPipeline.extensionId }));
        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
        Task<RenderTextureReadbackResult> readback = Assert.IsType<Task<RenderTextureReadbackResult>>(ReadbackPipeline.readback);
        device.pendingBufferRetirements = pendingReadback ? 0 : 1;
        device.pendingReadbackRetirements = pendingReadback ? 1 : 0;

        runtime.Dispose();

        Assert.Equal(pendingReadback ? 1 : 2, device.bufferRetirementAttempts);
        Assert.Equal(pendingReadback ? 2 : 1, device.cancelReadbackCount);
        Assert.Equal(2, device.endFrameCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await readback);
        runtime.Dispose();
        Assert.Equal(2, device.endFrameCount);
    }

    [Fact]
    public async Task CancelingReadbackReleasesTheDeviceOperationAtTheNextFrameBoundary()
    {
        var device = new RecordingRenderDevice(supportsReadback: true);
        using var cancellation = new CancellationTokenSource();
        ReadbackPipeline.texture = device.textureHandle;
        ReadbackPipeline.cancellationToken = cancellation.Token;
        var asset = new RenderPipelineAsset { pipelineTypeId = ReadbackPipeline.extensionId };
        var runtime = new RenderRuntime(m_types, device, new TestDiagnosticSink());
        runtime.Submit(CreateRequest("Canceled Readback", asset));

        BeginRenderFrame(runtime, 0f);
        runtime.AfterRender(default);
        runtime.EndFrame(default);
        cancellation.Cancel();
        Task<RenderTextureReadbackResult> readback = Assert.IsType<Task<RenderTextureReadbackResult>>(
            ReadbackPipeline.readback);
        Assert.False(readback.IsCompleted);
        BeginRenderFrame(runtime, 0f);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await readback);
        runtime.AfterRender(default);
        runtime.EndFrame(default);

        Assert.Equal(1, device.cancelReadbackCount);
        runtime.Detach();
    }

    private static RenderRequest CreateRequest(
        string name,
        RenderPipelineAsset pipeline,
        int priority = 0)
        => new(
            name,
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, 64, 64),
            pipeline,
            priority: priority);

    private static AssemblyLoadRequest CreateRenderingPluginRequest()
        => new()
        {
            moduleName = "RenderingRuntimeReloadTests",
            mainAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                "Modules",
                "RenderingReload",
                "Inno.Rendering.Runtime.Reload.TestModule.dll"),
            domain = AssemblyDomain.InnoPlugin,
            scope = AssemblyScope.Runtime
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (
        AssemblyUnloadMonitor monitor,
        IRenderRuntimeReloadTransaction transaction) RemoveRenderingPlugin(
        RenderRuntime runtime,
        ModuleHost modules,
        AssemblyLoadRequest plugin)
    {
        IRenderRuntimeReloadTransaction rendering = runtime.BeginExtensionReload();
        using AssemblyReloadSession removal = modules.BeginReload(
            Array.Empty<AssemblyLoadRequest>(),
            [plugin.moduleName]);
        removal.Activate();
        rendering.Prepare();
        rendering.Activate();
        AssemblyUnloadMonitor monitor = removal.Complete();
        rendering.Complete();
        return (monitor, rendering);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AssemblyUnloadMonitor RemoveUnusedPlugin(
        ModuleHost modules,
        AssemblyLoadRequest plugin)
    {
        using AssemblyReloadSession removal = modules.BeginReload(
            Array.Empty<AssemblyLoadRequest>(),
            [plugin.moduleName]);
        removal.Activate();
        return removal.Complete();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void QueueCollectiblePayload(
        RenderRuntime runtime,
        TypeCatalog types,
        RenderPipelineAsset pipeline)
    {
        Type payloadType = types.current.types
            .Select(type => type.Resolve(types))
            .Single(type => string.Equals(
                type.FullName,
                "Inno.Rendering.Runtime.Reload.TestModule.ReloadableFramePayload",
                StringComparison.Ordinal));
        object payload = Activator.CreateInstance(payloadType)
            ?? throw new InvalidOperationException("The collectible frame payload could not be created.");
        var data = new RenderFrameData();
        data.Set(new RenderDataChannelId("tests.runtime.collectible-payload"), payload);
        runtime.Submit(new RenderRequest(
            "Collectible Pending Request",
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, 64, 64),
            pipeline,
            data));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private static void BeginRenderFrame(RenderRuntime runtime, float deltaTime)
    {
        if (!runtime.isStarted)
            runtime.Attach();
        var frame = new RuntimeFrame(0, deltaTime, deltaTime, 0, 0, 1, false);
        runtime.BeginFrame(frame);
        runtime.BeforeRender(frame);
    }

    private sealed class TestContent : IdentityObject;

    [RenderPipelineExtension(extensionId)]
    private sealed class DisposablePipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.disposable";

        internal static int createdCount { get; private set; }
        internal static int disposedCount { get; private set; }
        internal static bool rejectConfiguration { get; set; }
        internal static int pendingRetirements { get; set; }
        internal static int retirementAttempts { get; private set; }
        internal static bool retirementExpired { get; set; }

        public DisposablePipeline() => createdCount++;

        public override void Build(RenderPipelineContext context) => _ = context;

        protected override void OnConfigure(SerializedRenderExtensionState state)
        {
            _ = state;
            if (rejectConfiguration)
                throw new InvalidOperationException("Rejected pipeline configuration candidate.");
        }

        internal static void Reset()
        {
            createdCount = 0;
            disposedCount = 0;
            rejectConfiguration = false;
            pendingRetirements = 0;
            retirementAttempts = 0;
            retirementExpired = false;
        }

        protected override void Dispose(bool disposing)
        {
            _ = disposing;
            retirementAttempts++;
            if (retirementExpired)
                throw new RetirementTimeoutException("The pipeline-owned device work exceeded its deadline.");
            if (pendingRetirements > 0)
            {
                pendingRetirements--;
                throw new RetirementPendingException("Pipeline work remains active.");
            }
            disposedCount++;
        }
    }

    [RenderFeatureExtension(extensionId)]
    private sealed class PendingFeature : RenderPipelineFeature, IDisposable
    {
        internal const string extensionId = "tests.runtime.pending-feature";
        internal static int pendingRetirements { get; set; }
        internal static int retirementAttempts { get; private set; }

        public override void AddRenderPasses(RenderFeatureContext context) { }

        public void Dispose()
        {
            retirementAttempts++;
            Assert.Equal(0, DisposablePipeline.disposedCount);
            if (pendingRetirements > 0)
            {
                pendingRetirements--;
                throw new RetirementPendingException("Feature work remains active.");
            }
        }

        internal static void Reset()
        {
            retirementAttempts = 0;
            pendingRetirements = 0;
        }
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class StatisticsPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.statistics";

        public override void Build(RenderPipelineContext context)
        {
            context.graph
                .AddRasterPass(
                    "Visible",
                    new RenderPhaseId("tests.statistics.visible"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
            context.graph.AddRasterPass(
                "Culled",
                new RenderPhaseId("tests.statistics.culled"),
                0,
                static (_, _) => { });
        }
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class SideEffectPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.side-effect";

        public override void Build(RenderPipelineContext context)
        {
            context.graph
                .AddRasterPass(
                    "Visible",
                    new RenderPhaseId("tests.runtime.visible"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
        }
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class PresentationPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.presentation";

        internal static List<PresentationObservation> observations { get; } = [];

        public override void Build(RenderPipelineContext context)
        {
            observations.Add(new PresentationObservation(
                context.request.name,
                context.preservePresentationTarget));
            context.graph
                .AddRasterPass(
                    "Presentation",
                    new RenderPhaseId("tests.runtime.presentation"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
        }

        internal static void Reset() => observations.Clear();
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class ThrowingPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.throwing";

        public override void Build(RenderPipelineContext context)
        {
            context.graph
                .AddRasterPass(
                    "Rolled Back",
                    new RenderPhaseId("tests.runtime.rolled-back"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
            throw new InvalidOperationException("Expected request failure.");
        }
    }

    private readonly record struct PresentationObservation(
        string requestName,
        bool preserveTarget);

    [RenderPipelineExtension(extensionId)]
    private sealed class UploadPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.upload";

        internal static RenderBufferSlice firstSlice { get; private set; }
        internal static RenderBufferSlice secondSlice { get; private set; }
        internal static bool singleUpload { get; set; }

        public override void Build(RenderPipelineContext context)
        {
            var layout = new RenderVertexLayout(
            [
                new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3)
            ]);
            var descriptor = new RenderBufferUploadDescriptor(
                layout.stride,
                RenderBufferUsage.Vertex,
                layout);
            firstSlice = context.uploads.UploadBuffer(descriptor, new byte[layout.stride * 3], "Vertices");
            if (!singleUpload)
            {
                secondSlice = context.uploads.UploadBuffer(
                    descriptor,
                    new byte[layout.stride * 3],
                    "Vertices");
            }
        }

        internal static void Reset()
        {
            firstSlice = default;
            secondSlice = default;
            singleUpload = false;
        }
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class TexturePrewarmPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.texture-prewarm";

        internal static TextureAsset? texture { get; set; }

        public override void Build(RenderPipelineContext context)
        {
            context.resourceService.PrewarmTexture(
                texture ?? throw new InvalidOperationException("A test texture is required."));
            context.graph
                .AddRasterPass(
                    "Submit While Prewarming",
                    new RenderPhaseId("tests.runtime.prewarm"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
        }
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class PendingShaderPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.pending-shader";

        internal static MaterialAsset? material { get; set; }
        internal static ShaderContractId contract { get; set; }
        internal static ShaderPassRoleId role { get; set; }
        internal static bool resolved { get; private set; }

        public override void Build(RenderPipelineContext context)
        {
            resolved = context.resourceService.TryResolveGraphicsMaterial(
                material ?? throw new InvalidOperationException("A test material is required."),
                contract,
                role,
                null,
                null,
                out _);
            context.graph
                .AddRasterPass(
                    "Submit While Compiling",
                    new RenderPhaseId("tests.runtime.pending-shader"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
        }

        internal static void Reset()
        {
            material = null;
            contract = default;
            role = default;
            resolved = false;
        }
    }

    [RenderPipelineExtension(extensionId)]
    private sealed class ReadbackPipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.readback";

        internal static PersistentTextureHandle texture { get; set; }
        internal static CancellationToken cancellationToken { get; set; }
        internal static Task<RenderTextureReadbackResult>? readback { get; private set; }

        public override void Build(RenderPipelineContext context)
        {
            byte[] update = Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray();
            context.resourceService.UpdateTexture(
                texture,
                new RenderTextureRegion(0, 1, 1, 0, 2, 2),
                update);
            readback ??= context.resourceService
                .ReadTextureAsync(texture, cancellationToken: cancellationToken)
                .AsTask();
            context.graph
                .AddRasterPass(
                    "Readback Submission",
                    new RenderPhaseId("tests.runtime.readback"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
        }

        internal static void Reset()
        {
            texture = default;
            cancellationToken = default;
            readback = null;
        }
    }

    [RenderRequestProviderExtension(extensionId)]
    private sealed class TestRequestProvider : RenderRequestProvider
    {
        internal const string extensionId = "tests.runtime.request-provider";

        internal static bool enabled { get; set; }
        internal static RenderPipelineAsset? pipeline { get; set; }
        internal static int submitCount { get; private set; }
        internal static int pendingRetirements { get; set; }
        internal static int retirementAttempts { get; private set; }
        internal static ContentReadScope? lastContent { get; private set; }
        internal static RenderViewport lastPresentationViewport { get; private set; }

        public override void Submit(RenderRequestProviderContext context)
        {
            if (!enabled)
                return;
            submitCount++;
            lastContent = context.content;
            lastPresentationViewport = context.primaryPresentationViewport;
            if (pipeline is null)
                return;
            context.requests.Submit(CreateRequest(
                "Provider Request",
                pipeline));
        }

        internal static void Reset()
        {
            enabled = false;
            pipeline = null;
            submitCount = 0;
            pendingRetirements = 0;
            retirementAttempts = 0;
            lastContent = null;
            lastPresentationViewport = default;
        }

        protected override void Dispose(bool disposing)
        {
            retirementAttempts++;
            if (pendingRetirements > 0)
            {
                pendingRetirements--;
                throw new RetirementPendingException("Provider work remains active.");
            }
        }
    }

    private sealed class SideEffectContributor : IRenderFrameGraphContributor
    {
        public void PrepareFrame(ulong frameIndex) => _ = frameIndex;

        public void AddRenderPasses(RenderGraphBuilder graph, ulong frameIndex)
        {
            _ = frameIndex;
            graph.AddRasterPass(
                    "Overlay",
                    new RenderPhaseId("tests.runtime.overlay"),
                    0,
                    static (_, _) => { })
                .HasSideEffect();
        }
    }

    private sealed class TestDiagnosticSink : IDiagnosticReporter
    {
        internal List<Diagnostic> items { get; } = [];

        public void Publish(Diagnostic diagnostic) => items.Add(diagnostic);

        public void Resolve(string code, string? semanticId = null, Guid? objectId = null)
            => items.RemoveAll(diagnostic =>
                string.Equals(diagnostic.code, code, StringComparison.Ordinal) &&
                string.Equals(diagnostic.semanticId, semanticId, StringComparison.Ordinal) && diagnostic.objectId == objectId);

        public void Replace(IEnumerable<Diagnostic> diagnostics)
        {
            items.Clear();
            items.AddRange(diagnostics);
        }
    }

    private sealed class TestDeviceProxy : RenderDevice, IRenderDevice
    {
        private static readonly GraphicsCapabilities S_CAPABILITIES = new(
            GraphicsApi.Noop,
            GraphicsCapability.None,
            new GraphicsLimits(64, 4, 4096, 8),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            [],
            [],
            originBottomLeft: false,
            homogeneousDepth: false);

        public RenderDeviceFrameCounters frameCounters { get; internal set; }
        internal List<PersistentBufferHandle> createdBuffers { get; } = [];
        internal List<PersistentBufferHandle> destroyedBuffers { get; } = [];
        internal int failBufferCreationAt { get; set; }
        private int m_bufferCreationCount;
        internal List<PersistentTextureHandle> createdTextures { get; } = [];
        internal List<PersistentTextureHandle> destroyedTextures { get; } = [];
        internal bool frameOpen { get; private set; }
        internal int endFrameCount { get; private set; }
        internal int executeCount { get; private set; }
        internal int textureRetirementAttempts { get; private set; }
        internal bool failFirstTextureRetirement { get; set; }
        internal PersistentTextureHandle pendingTextureRetirement { get; set; }
        internal CompiledRenderGraph? lastGraph { get; private set; }

        internal static IRenderDevice Create(out TestDeviceProxy proxy)
        {
            proxy = new TestDeviceProxy();
            return proxy;
        }

        internal void ReleaseRecordedGraph() => lastGraph = null;

        public GraphicsCapabilities capabilities => S_CAPABILITIES;
        public uint generation => 1;
        public RenderPresentationSize presentationSize { get; set; } = new(1, 1);
        public RenderPresentationSize primaryPresentationSize => presentationSize;

        public void BeginFrame()
        {
            Assert.False(frameOpen);
            frameOpen = true;
        }

        public void Execute(CompiledRenderGraph graph, ulong frameIndex)
        {
            Assert.True(frameOpen);
            _ = frameIndex;
            executeCount++;
            lastGraph = graph;
        }

        public uint EndFrame()
        {
            Assert.True(frameOpen);
            frameOpen = false;
            endFrameCount++;
            return checked((uint)endFrameCount);
        }

        public void ResizeBackbuffer(int width, int height)
        {
            _ = width;
            _ = height;
        }

        public PersistentTextureHandle CreateTexture(RenderTextureDescriptor descriptor, string name)
        {
            Assert.True(frameOpen);
            _ = descriptor;
            _ = name;
            PersistentTextureHandle texture = CreatePersistentTextureHandle(
                checked((ulong)createdTextures.Count + 1),
                generation);
            createdTextures.Add(texture);
            return texture;
        }

        public void UpdateTexture(
            PersistentTextureHandle texture,
            ReadOnlySpan<byte> data,
            int mipLevel = 0,
            int arrayLayer = 0)
        {
            _ = texture;
            _ = data;
            _ = mipLevel;
            _ = arrayLayer;
        }

        public void UpdateTextureRegion(
            PersistentTextureHandle texture,
            RenderTextureRegion region,
            ReadOnlySpan<byte> data)
        {
            _ = texture;
            _ = region;
            _ = data;
        }

        public RenderTextureReadbackHandle BeginTextureReadback(
            PersistentTextureHandle texture,
            int mipLevel = 0)
        {
            _ = texture;
            _ = mipLevel;
            return default;
        }

        public bool TryGetTextureReadback(
            RenderTextureReadbackHandle readback,
            out RenderTextureReadbackResult? result)
        {
            _ = readback;
            result = null;
            return false;
        }

        public void CancelTextureReadback(RenderTextureReadbackHandle readback) => _ = readback;

        public void DestroyTexture(PersistentTextureHandle texture)
        {
            Assert.True(frameOpen);
            textureRetirementAttempts++;
            if (failFirstTextureRetirement)
            {
                failFirstTextureRetirement = false;
                throw new InvalidOperationException("Texture retirement failed.");
            }
            if (texture == pendingTextureRetirement)
            {
                pendingTextureRetirement = default;
                throw new RetirementPendingException("Texture still has pending work.");
            }
            destroyedTextures.Add(texture);
        }

        public PersistentBufferHandle CreateBuffer(PersistentBufferDescriptor descriptor, ReadOnlySpan<byte> initialData, string name)
        {
            m_bufferCreationCount++;
            if (m_bufferCreationCount == failBufferCreationAt) throw new InvalidOperationException("Native allocation rejected.");
            PersistentBufferHandle handle = CreatePersistentBufferHandle((ulong)m_bufferCreationCount, generation);
            createdBuffers.Add(handle);
            return handle;
        }

        public void UpdateBuffer(
            PersistentBufferHandle buffer,
            ReadOnlySpan<byte> data,
            int startElement = 0)
        {
            _ = buffer;
            _ = data;
            _ = startElement;
        }

        public void DestroyBuffer(PersistentBufferHandle buffer)
        {
            Assert.DoesNotContain(buffer, destroyedBuffers);
            destroyedBuffers.Add(buffer);
        }

        public GraphicsPipelineHandle CreateGraphicsPipeline(
            GraphicsPipelineDescriptor descriptor,
            string name)
        {
            _ = descriptor;
            _ = name;
            return default;
        }

        public void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline) => _ = pipeline;

        public ComputePipelineHandle CreateComputePipeline(
            ComputePipelineDescriptor descriptor,
            string name)
        {
            _ = descriptor;
            _ = name;
            return default;
        }

        public void DestroyComputePipeline(ComputePipelineHandle pipeline) => _ = pipeline;

        public void Dispose() { }
    }

    private sealed class RecordingRenderDevice : RenderDevice, IRenderDevice
    {
        private readonly RenderTextureDescriptor m_readbackDescriptor = new(
            4,
            4,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.Readback);
        private readonly RenderTextureReadbackHandle m_readbackHandle;

        internal List<int> bufferUpdateOffsets { get; } = [];
        internal PersistentTextureHandle textureHandle { get; }
        internal RenderTextureRegion? updatedRegion { get; private set; }
        internal byte[] updatedBytes { get; private set; } = [];
        internal bool readbackReady { get; set; }
        internal int cancelReadbackCount { get; private set; }
        internal int pendingReadbackRetirements { get; set; }
        internal int pendingBufferRetirements { get; set; }
        internal int bufferRetirementAttempts { get; private set; }
        internal int createBufferCount { get; private set; }
        internal int endFrameCount { get; private set; }
        internal int executeCount { get; private set; }

        internal RecordingRenderDevice(bool supportsReadback = false)
        {
            capabilities = new GraphicsCapabilities(
                GraphicsApi.Noop,
                supportsReadback ? GraphicsCapability.TextureReadback : GraphicsCapability.None,
                new GraphicsLimits(64, 4, 4096, 8),
                Enum.GetValues<RenderTextureFormat>(),
                Enum.GetValues<RenderTextureFormat>(),
                [],
                [],
                originBottomLeft: false,
                homogeneousDepth: false);
            textureHandle = CreatePersistentTextureHandle(1UL, generation);
            m_readbackHandle = CreateRenderTextureReadbackHandle(1UL, generation);
        }

        public GraphicsCapabilities capabilities { get; }

        public uint generation => 1;

        public void BeginFrame() { }

        public void Execute(CompiledRenderGraph graph, ulong frameIndex)
        {
            _ = graph;
            _ = frameIndex;
            executeCount++;
        }

        public uint EndFrame()
        {
            endFrameCount++;
            return checked((uint)endFrameCount);
        }

        public void ResizeBackbuffer(int width, int height)
        {
            _ = width;
            _ = height;
        }

        public PersistentTextureHandle CreateTexture(RenderTextureDescriptor descriptor, string name)
        {
            _ = descriptor;
            _ = name;
            return default;
        }

        public void UpdateTexture(
            PersistentTextureHandle texture,
            ReadOnlySpan<byte> data,
            int mipLevel = 0,
            int arrayLayer = 0)
        {
            _ = texture;
            _ = data;
            _ = mipLevel;
            _ = arrayLayer;
        }

        public void UpdateTextureRegion(
            PersistentTextureHandle texture,
            RenderTextureRegion region,
            ReadOnlySpan<byte> data)
        {
            Assert.Equal(textureHandle, texture);
            updatedRegion = region;
            updatedBytes = data.ToArray();
        }

        public RenderTextureReadbackHandle BeginTextureReadback(
            PersistentTextureHandle texture,
            int mipLevel = 0)
        {
            Assert.Equal(textureHandle, texture);
            _ = mipLevel;
            return m_readbackHandle;
        }

        public bool TryGetTextureReadback(
            RenderTextureReadbackHandle readback,
            out RenderTextureReadbackResult? result)
        {
            Assert.Equal(m_readbackHandle, readback);
            result = readbackReady
                ? new RenderTextureReadbackResult(
                    m_readbackDescriptor,
                    0,
                    16,
                    Enumerable.Repeat((byte)37, 64).ToArray())
                : null;
            return result is not null;
        }

        public void CancelTextureReadback(RenderTextureReadbackHandle readback)
        {
            Assert.Equal(m_readbackHandle, readback);
            cancelReadbackCount++;
            if (pendingReadbackRetirements > 0)
            {
                pendingReadbackRetirements--;
                Assert.False(ReadbackPipeline.readback!.IsCompleted);
                Assert.Equal(1, endFrameCount);
                throw new RetirementPendingException("Readback cancellation remains active.");
            }
        }

        public void DestroyTexture(PersistentTextureHandle texture) => _ = texture;

        public PersistentBufferHandle CreateBuffer(
            PersistentBufferDescriptor descriptor,
            ReadOnlySpan<byte> initialData,
            string name)
        {
            _ = descriptor;
            _ = initialData;
            _ = name;
            createBufferCount++;
            return default;
        }

        public void UpdateBuffer(
            PersistentBufferHandle buffer,
            ReadOnlySpan<byte> data,
            int startElement = 0)
        {
            _ = buffer;
            _ = data;
            bufferUpdateOffsets.Add(startElement);
        }

        public void DestroyBuffer(PersistentBufferHandle buffer)
        {
            bufferRetirementAttempts++;
            if (pendingBufferRetirements > 0)
            {
                pendingBufferRetirements--;
                Assert.Equal(0, cancelReadbackCount);
                Assert.False(ReadbackPipeline.readback!.IsCompleted);
                Assert.Equal(1, endFrameCount);
                throw new RetirementPendingException("Upload work remains active.");
            }
        }

        public GraphicsPipelineHandle CreateGraphicsPipeline(
            GraphicsPipelineDescriptor descriptor,
            string name)
        {
            _ = descriptor;
            _ = name;
            return default;
        }

        public void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline) => _ = pipeline;

        public ComputePipelineHandle CreateComputePipeline(
            ComputePipelineDescriptor descriptor,
            string name)
        {
            _ = descriptor;
            _ = name;
            return default;
        }

        public void DestroyComputePipeline(ComputePipelineHandle pipeline) => _ = pipeline;

        public void Dispose() { }

    }

    private sealed class DelayedTextureArtifactSource
    {
        private readonly TaskCompletionSource<byte[]> m_completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal ManualResetEventSlim started { get; } = new(initialState: false);
        internal bool isCompleted => m_completion.Task.IsCompleted;

        internal Task<byte[]> LoadAsync(CancellationToken cancellationToken = default)
        {
            started.Set();
            return m_completion.Task.WaitAsync(cancellationToken);
        }

        internal void Complete(byte[] data) => m_completion.TrySetResult(data);
    }

    private sealed class DelayedArtifactProvider(DelayedTextureArtifactSource compiler)
        : IRenderTargetArtifactProvider
    {
        private Task<byte[]>? m_texture;

        public RenderTargetArtifactStatus GetShaderArtifact(
            ShaderAsset shader,
            RenderShaderVariant variant,
            GraphicsCapabilities capabilities,
            out RenderShaderArtifact? artifact)
        {
            _ = shader;
            _ = variant;
            _ = capabilities;
            artifact = null;
            return RenderTargetArtifactStatus.Pending;
        }

        public RenderTargetArtifactStatus GetTextureArtifact(
            TextureAsset texture,
            out ReadOnlyMemory<byte> artifact)
        {
            _ = texture;
            m_texture ??= compiler.LoadAsync();
            if (m_texture.IsCompletedSuccessfully)
            {
                artifact = m_texture.Result;
                return RenderTargetArtifactStatus.Ready;
            }
            artifact = ReadOnlyMemory<byte>.Empty;
            return RenderTargetArtifactStatus.Pending;
        }
    }
}
