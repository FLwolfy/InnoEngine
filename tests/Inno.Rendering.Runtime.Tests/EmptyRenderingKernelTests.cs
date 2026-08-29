using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Inno.Core.Assemblies;
using Inno.Core.Reflection;
using Inno.Rendering;
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
        AssemblyManager.Initialize(new AssemblyManagerOptions { cacheDirectory = m_cacheDirectory });
        TypeCacheManager.Initialize();
        DisposablePipeline.Reset();
    }

    public void Dispose()
    {
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
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

    private sealed class TestDiagnosticSink : IRenderDiagnosticSink
    {
        public void Publish(RenderDiagnostic diagnostic) => _ = diagnostic;
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

        internal static IRenderDevice Create(out TestDeviceProxy proxy)
        {
            IRenderDevice device = Create<IRenderDevice, TestDeviceProxy>();
            proxy = (TestDeviceProxy)(object)device;
            return device;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _ = args;
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
                default:
                    return targetMethod.ReturnType == typeof(void)
                        ? null
                        : targetMethod.ReturnType.IsValueType
                            ? Activator.CreateInstance(targetMethod.ReturnType)
                            : null;
            }
        }
    }
}
