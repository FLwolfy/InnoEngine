using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Inno.Assets;
using Inno.Assets.Core;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;
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

    public RenderRuntimeGenerationTests()
    {
        _ = typeof(TextureAsset);
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions { cacheDirectory = m_cacheDirectory });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        Directory.CreateDirectory(Path.Combine(m_cacheDirectory, "Assets"));
        AssetManager.Initialize(AssetManagerOptions.Create(
            Path.Combine(m_cacheDirectory, "Assets"),
            Path.Combine(m_cacheDirectory, "Library")) with
        {
            enableFileSystemWatcher = false
        });
        DisposablePipeline.Reset();
        TestRequestProvider.Reset();
        UploadPipeline.Reset();
        TexturePrewarmPipeline.texture = null;
        ReadbackPipeline.Reset();
    }

    public void Dispose()
    {
        AssetManager.Shutdown();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
        if (Directory.Exists(m_cacheDirectory))
            Directory.Delete(m_cacheDirectory, recursive: true);
    }

    [Fact]
    public void SuccessfulTypeCacheChangeRetiresUnrequestedPipelineAtFrameBoundary()
    {
        IRenderDevice device = TestDeviceProxy.Create(out _);
        var runtime = new RenderRuntimeLayer(device, new TestDiagnosticSink());
        var asset = new RenderPipelineAsset { pipelineTypeId = DisposablePipeline.extensionId };

        Assert.True(runtime.TryActivateDefaultPipeline(asset));
        Assert.Equal(1, DisposablePipeline.createdCount);
        Assert.Equal(0, DisposablePipeline.disposedCount);

        TypeCacheManager.Rebuild();
        runtime.OnBeforeRender(0f);

        Assert.Equal(1, DisposablePipeline.disposedCount);

        runtime.OnAfterRender(0f);
        runtime.OnDetach();
    }

    [Fact]
    public void FrameStatisticsReportBackendCommandsAndGraphCulling()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        proxy.frameCounters = new RenderDeviceFrameCounters(3, 2);
        var runtime = new RenderRuntimeLayer(device, new TestDiagnosticSink());
        var asset = new RenderPipelineAsset { pipelineTypeId = StatisticsPipeline.extensionId };
        runtime.OnAttach();
        Assert.True(runtime.TryActivateDefaultPipeline(asset));
        runtime.Submit(new RenderRequest(
            "Statistics",
            RenderTarget.backbuffer,
            new RenderViewport(0, 0, 64, 64),
            asset));

        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

        RenderFrameStatistics statistics = Assert.IsType<RenderFrameStatistics>(GraphicsSettings.frameStatistics);
        Assert.Equal(1, statistics.viewCount);
        Assert.Equal(3, statistics.drawCount);
        Assert.Equal(2, statistics.dispatchCount);
        Assert.Equal(1, statistics.culledPassCount);
        runtime.OnDetach();
    }

    [Fact]
    public void RequestProviderProducesCurrentFrameWorkThroughPublicPluginApi()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var asset = new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId };
        TestRequestProvider.pipeline = asset;
        TestRequestProvider.enabled = true;
        var runtime = new RenderRuntimeLayer(device, new TestDiagnosticSink());
        runtime.OnAttach();

        runtime.OnBeforeRender(1f / 60f);
        runtime.OnRender(1f / 60f);
        runtime.OnAfterRender(1f / 60f);

        Assert.Equal(1, TestRequestProvider.submitCount);
        Assert.Equal(1, proxy.executeCount);
        CompiledRenderPass pass = Assert.Single(Assert.IsType<CompiledRenderGraph>(proxy.lastGraph).passes);
        Assert.Equal("Request[0] Provider Request/Visible", pass.name);
        runtime.OnDetach();
    }

    [Fact]
    public void MultipleRequestsAndContributorsCompileAndExecuteAsOneFrameGraph()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var asset = new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId };
        var contributor = new SideEffectContributor();
        var runtime = new RenderRuntimeLayer(device, new TestDiagnosticSink(), [contributor]);
        runtime.Submit(CreateRequest("Second", asset, priority: 10));
        runtime.Submit(CreateRequest("First", asset, priority: -10));

        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

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
        runtime.OnDetach();
    }

    [Fact]
    public void FailedRequestRollsBackWithoutDiscardingOtherRequests()
    {
        IRenderDevice device = TestDeviceProxy.Create(out TestDeviceProxy proxy);
        var diagnostics = new TestDiagnosticSink();
        var failed = new RenderPipelineAsset { pipelineTypeId = ThrowingPipeline.extensionId };
        var valid = new RenderPipelineAsset { pipelineTypeId = SideEffectPipeline.extensionId };
        var runtime = new RenderRuntimeLayer(device, diagnostics);
        runtime.Submit(CreateRequest("A Failed", failed));
        runtime.Submit(CreateRequest("B Valid", valid));

        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

        Assert.Equal(1, proxy.executeCount);
        CompiledRenderPass pass = Assert.Single(Assert.IsType<CompiledRenderGraph>(proxy.lastGraph).passes);
        Assert.Equal("Request[1] B Valid/Visible", pass.name);
        Assert.Contains(diagnostics.items, diagnostic => diagnostic.code == "RENDER_REQUEST_FAILED");
        runtime.OnDetach();
    }

    [Fact]
    public void FrameUploadsReusePagesAndResetSliceOffsetsBetweenFrames()
    {
        var device = new RecordingRenderDevice();
        var asset = new RenderPipelineAsset { pipelineTypeId = UploadPipeline.extensionId };
        var runtime = new RenderRuntimeLayer(device, new TestDiagnosticSink());

        runtime.Submit(CreateRequest("Upload A", asset));
        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

        Assert.Equal(1, device.createBufferCount);
        Assert.Equal(new[] { 0, 3 }, device.bufferUpdateOffsets.ToArray());
        Assert.Equal(0, UploadPipeline.firstSlice.firstElement);
        Assert.Equal(3, UploadPipeline.secondSlice.firstElement);
        Assert.Equal(3, UploadPipeline.secondSlice.elementCount);

        UploadPipeline.singleUpload = true;
        runtime.Submit(CreateRequest("Upload B", asset));
        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

        Assert.Equal(1, device.createBufferCount);
        Assert.Equal(new[] { 0, 3, 0 }, device.bufferUpdateOffsets.ToArray());
        Assert.Equal(0, UploadPipeline.firstSlice.firstElement);
        runtime.OnDetach();
    }

    [Fact]
    public void TexturePrewarmRunsOutsideTheRenderFrameAndDoesNotDelaySubmission()
    {
        AssetPath path = AssetPath.Project("Textures/Prewarm.png");
        string absolutePath = Path.Combine(m_cacheDirectory, "Assets", path.localPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        byte[] source = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(source, 0);
        source[19] = 1;
        source[23] = 1;
        File.WriteAllBytes(absolutePath, source);
        Assert.True(AssetManager.Import(path));
        TexturePrewarmPipeline.texture = AssetManager.Load<TextureAsset>(path);

        var compiler = new DelayedTextureCompiler();
        var device = new RecordingRenderDevice();
        var asset = new RenderPipelineAsset { pipelineTypeId = TexturePrewarmPipeline.extensionId };
        var runtime = new RenderRuntimeLayer(
            device,
            new TestDiagnosticSink(),
            textureCompiler: compiler);
        runtime.Submit(CreateRequest("Prewarm", asset));

        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

        Assert.Equal(1, device.endFrameCount);
        Assert.True(compiler.started.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(compiler.isCompleted);
        compiler.Complete([1, 2, 3, 4]);
        runtime.OnDetach();
    }

    [Fact]
    public async Task PersistentTextureRegionUpdateAndReadbackCompleteAcrossFrameBoundaries()
    {
        var device = new RecordingRenderDevice(supportsReadback: true);
        ReadbackPipeline.texture = device.textureHandle;
        var asset = new RenderPipelineAsset { pipelineTypeId = ReadbackPipeline.extensionId };
        var runtime = new RenderRuntimeLayer(device, new TestDiagnosticSink());
        runtime.Submit(CreateRequest("Readback", asset));

        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

        Assert.Equal(new RenderTextureRegion(0, 1, 1, 0, 2, 2), device.updatedRegion);
        Assert.Equal(Enumerable.Range(0, 16).Select(static value => (byte)value), device.updatedBytes);
        Task<RenderTextureReadbackResult> readback = Assert.IsType<Task<RenderTextureReadbackResult>>(
            ReadbackPipeline.readback);
        Assert.False(readback.IsCompleted);

        device.readbackReady = true;
        runtime.OnBeforeRender(0f);
        RenderTextureReadbackResult result = await readback;
        runtime.OnAfterRender(0f);

        Assert.Equal(16, result.rowPitch);
        Assert.Equal(64, result.data.Length);
        Assert.All(result.data.ToArray(), static value => Assert.Equal((byte)37, value));
        runtime.OnDetach();
    }

    [Fact]
    public async Task CancelingReadbackReleasesTheDeviceOperationAtTheNextFrameBoundary()
    {
        var device = new RecordingRenderDevice(supportsReadback: true);
        using var cancellation = new CancellationTokenSource();
        ReadbackPipeline.texture = device.textureHandle;
        ReadbackPipeline.cancellationToken = cancellation.Token;
        var asset = new RenderPipelineAsset { pipelineTypeId = ReadbackPipeline.extensionId };
        var runtime = new RenderRuntimeLayer(device, new TestDiagnosticSink());
        runtime.Submit(CreateRequest("Canceled Readback", asset));

        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);
        cancellation.Cancel();
        Task<RenderTextureReadbackResult> readback = Assert.IsType<Task<RenderTextureReadbackResult>>(
            ReadbackPipeline.readback);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await readback);

        runtime.OnBeforeRender(0f);
        runtime.OnAfterRender(0f);

        Assert.Equal(1, device.cancelReadbackCount);
        runtime.OnDetach();
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

    [RenderPipelineExtension(extensionId)]
    private sealed class DisposablePipeline : RenderPipeline
    {
        internal const string extensionId = "tests.runtime.disposable";

        internal static int createdCount { get; private set; }
        internal static int disposedCount { get; private set; }
        internal static bool rejectConfiguration { get; set; }

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
        }

        protected override void Dispose(bool disposing)
        {
            _ = disposing;
            disposedCount++;
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

        public override void Submit(RenderRequestProviderContext context)
        {
            if (!enabled)
                return;
            submitCount++;
            context.requests.Submit(CreateRequest(
                "Provider Request",
                pipeline ?? throw new InvalidOperationException("A provider pipeline is required.")));
        }

        internal static void Reset()
        {
            enabled = false;
            pipeline = null;
            submitCount = 0;
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

    private sealed class TestDiagnosticSink : IRenderDiagnosticSink
    {
        internal List<RenderDiagnostic> items { get; } = [];

        public void Publish(RenderDiagnostic diagnostic) => items.Add(diagnostic);
    }

    private class TestDeviceProxy : DispatchProxy
    {
        private static readonly GraphicsCapabilities S_CAPABILITIES = new(
            GraphicsBackend.Noop,
            GraphicsFeature.None,
            new GraphicsLimits(64, 4, 4096, 8),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            [],
            [],
            originBottomLeft: false,
            homogeneousDepth: false);

        internal RenderDeviceFrameCounters frameCounters { get; set; }
        internal int executeCount { get; private set; }
        internal CompiledRenderGraph? lastGraph { get; private set; }

        internal static IRenderDevice Create(out TestDeviceProxy proxy)
        {
            IRenderDevice device = Create<IRenderDevice, TestDeviceProxy>();
            proxy = (TestDeviceProxy)(object)device;
            return device;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                throw new InvalidOperationException("A dispatch proxy invocation requires method metadata.");
            switch (targetMethod.Name)
            {
                case "get_capabilities":
                    return S_CAPABILITIES;
                case "get_generation":
                case nameof(IRenderDevice.EndFrame):
                    return 1u;
                case "get_frameCounters":
                    return frameCounters;
                case nameof(IRenderDevice.Execute):
                    executeCount++;
                    lastGraph = (CompiledRenderGraph?)args![0];
                    return null;
                default:
                    return targetMethod.ReturnType == typeof(void)
                        ? null
                        : targetMethod.ReturnType.IsValueType
                            ? Activator.CreateInstance(targetMethod.ReturnType)
                            : null;
            }
        }
    }

    private sealed class RecordingRenderDevice : IRenderDevice
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
        internal int createBufferCount { get; private set; }
        internal int endFrameCount { get; private set; }
        internal int executeCount { get; private set; }

        internal RecordingRenderDevice(bool supportsReadback = false)
        {
            capabilities = new GraphicsCapabilities(
                GraphicsBackend.Noop,
                supportsReadback ? GraphicsFeature.TextureReadback : GraphicsFeature.None,
                new GraphicsLimits(64, 4, 4096, 8),
                Enum.GetValues<RenderTextureFormat>(),
                Enum.GetValues<RenderTextureFormat>(),
                [],
                [],
                originBottomLeft: false,
                homogeneousDepth: false);
            textureHandle = CreateOpaqueHandle<PersistentTextureHandle>(1UL, generation);
            m_readbackHandle = CreateOpaqueHandle<RenderTextureReadbackHandle>(1UL, generation);
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

        public void DestroyBuffer(PersistentBufferHandle buffer) => _ = buffer;

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

        private static THandle CreateOpaqueHandle<THandle>(ulong value, uint deviceGeneration)
            where THandle : struct
        {
            ConstructorInfo constructor = typeof(THandle).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(ulong), typeof(uint)],
                modifiers: null) ?? throw new InvalidOperationException(
                    $"Opaque render handle '{typeof(THandle).Name}' has no device constructor.");
            return (THandle)constructor.Invoke([value, deviceGeneration]);
        }
    }

    private sealed class DelayedTextureCompiler : ITextureTargetCompiler
    {
        private readonly TaskCompletionSource<byte[]> m_completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal ManualResetEventSlim started { get; } = new(initialState: false);
        internal bool isCompleted => m_completion.Task.IsCompleted;

        public ValueTask<byte[]> CompileKtxAsync(
            string sourcePath,
            TextureColorSpace colorSpace,
            CancellationToken cancellationToken = default)
        {
            _ = sourcePath;
            _ = colorSpace;
            started.Set();
            return new ValueTask<byte[]>(m_completion.Task.WaitAsync(cancellationToken));
        }

        internal void Complete(byte[] data) => m_completion.TrySetResult(data);
    }
}
