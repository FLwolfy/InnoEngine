using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Mathematics;
using Inno.Engine.Scene;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Pipelines.Tests;

[Collection(RenderingPipelineTestCollection.NAME)]
public sealed class UniversalRenderPipelineTests
{
    public UniversalRenderPipelineTests(RenderingPipelineTestFixture _)
    {
    }

    [Fact]
    public void Build_Deferred_ProducesSharedProductionGraph()
    {
        TestBuild build = Build(RenderPath.Deferred, FullCapabilities(), EmptyWorld());

        Assert.True(build.result.succeeded, FormatDiagnostics(build.result));
        string[] names = build.result.graph!.passes.Select(static pass => pass.name).ToArray();
        Assert.Contains("Test/Deferred GBuffer", names);
        Assert.Contains("Test/Deferred Lighting", names);
        Assert.Contains("Test/Transparent", names);
        Assert.Contains("Test/Bloom Downsample", names);
        Assert.Contains("Test/Bloom Upsample", names);
        Assert.Equal("Test/Tone Mapping", names[^1]);
        Assert.DoesNotContain("Test/Forward Opaque", names);
        Assert.True(build.resources.gBuffer0.isValid);
        Assert.True(build.resources.gBuffer1.isValid);
        Assert.True(build.resources.gBuffer2.isValid);
    }

    [Fact]
    public void Build_DeferredUnsupported_FallsBackWithoutBlackFrame()
    {
        GraphicsCapabilities capabilities = FullCapabilities(maxColorAttachments: 2);
        TestBuild build = Build(RenderPath.Deferred, capabilities, EmptyWorld());

        Assert.True(build.result.succeeded, FormatDiagnostics(build.result));
        Assert.Contains(build.diagnostics.values, value => value.code == "RENDER_PIPELINE_DEFERRED_FALLBACK");
        Assert.Contains(build.result.graph!.passes, pass => pass.name == "Test/Forward Opaque");
        Assert.DoesNotContain(build.result.graph.passes, pass => pass.name == "Test/Deferred GBuffer");
    }

    [Fact]
    public void Build_ComputeUnsupported_UsesCpuLightList()
    {
        GraphicsCapabilities capabilities = FullCapabilities(
            features: GraphicsFeature.TextureBlit);
        TestBuild build = Build(RenderPath.ForwardPlus, capabilities, EmptyWorld());

        Assert.True(build.result.succeeded, FormatDiagnostics(build.result));
        Assert.Contains(build.diagnostics.values, value => value.code == "RENDER_PIPELINE_FORWARD_CPU_LIGHTS");
        Assert.DoesNotContain(build.result.graph!.passes, pass => pass.name == "Test/Cluster Lights");
        Assert.Contains(build.result.graph.passes, pass => pass.name == "Test/Forward Opaque");
    }

    [Fact]
    public void Build_ForwardPlusWithLocalLight_ProducesAndConsumesClusterContract()
    {
        var scene = new GameScene("Clustered");
        GameObject lightObject = scene.CreateObject("Point");
        lightObject.transform.worldPosition = new Vector3(0f, 0f, 4f);
        PointLight light = lightObject.AddComponent<PointLight>();
        light.range = 6f;

        TestBuild build = Build(
            RenderPath.ForwardPlus,
            FullCapabilities(),
            RenderWorldSnapshot.Capture([scene]));

        Assert.True(build.result.succeeded, FormatDiagnostics(build.result));
        CompiledRenderPass clusterPass = Assert.Single(build.result.graph!.passes.Where(
            static pass => pass.name == "Test/Cluster Lights"));
        Assert.NotNull(clusterPass.viewTransform);
        RenderPipelineOperation cluster = Assert.Single(build.operations.Where(
            static operation => operation.id == BuiltinPipelineOperations.ClusterLights));
        Assert.Equal((16, 16, 24), (cluster.dispatchX, cluster.dispatchY, cluster.dispatchZ));
        Assert.Contains(cluster.uniforms, static value => value.binding.value == "inno_cluster_parameters");
        RenderPipelineOperation forward = Assert.Single(build.operations.Where(
            static operation => operation.id == BuiltinPipelineOperations.ForwardOpaque));
        Assert.Equal(BuiltinShaderPassTags.ForwardLitClustered, forward.shaderPassTag);
        Assert.Equal(2, forward.buffers.Count);
        Assert.Contains(
            forward.uniforms,
            static value => value.binding.value == "inno_cluster_depth_parameters");
    }

    [Fact]
    public void Build_PickingRequested_PublishesGpuObjectIdTarget()
    {
        TestBuild build = Build(
            RenderPath.ForwardPlus,
            FullCapabilities(),
            EmptyWorld(),
            enablePicking: true);

        Assert.True(build.result.succeeded, FormatDiagnostics(build.result));
        Assert.True(build.resources.picking.isValid);
        Assert.Contains(build.result.graph!.passes, static pass => pass.name == "Test/Picking");
    }

    [Fact]
    public void Build_DirectionalShadow_UsesOneArrayLayerPerCascade()
    {
        var scene = new GameScene("Shadows");
        GameObject meshObject = scene.CreateObject("Caster");
        MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
        renderer.mesh = CreateMesh();
        renderer.SetMaterial(0, new MaterialAsset());
        GameObject lightObject = scene.CreateObject("Sun");
        DirectionalLight light = lightObject.AddComponent<DirectionalLight>();
        light.shadowCascadeCount = 3;
        RenderWorldSnapshot world = RenderWorldSnapshot.Capture([scene]);

        TestBuild build = Build(RenderPath.ForwardPlus, FullCapabilities(), world);

        Assert.True(build.result.succeeded, FormatDiagnostics(build.result));
        CompiledRenderPass[] shadowPasses = build.result.graph!.passes
            .Where(static pass => pass.phase == BuiltinRenderPhases.shadows)
            .ToArray();
        Assert.Equal(3, shadowPasses.Length);
        Assert.Equal([0, 1, 2], shadowPasses.Select(static pass => Assert.Single(pass.attachments).arrayLayer));
        RenderPipelineOperation forward = Assert.Single(build.operations.Where(static operation =>
            operation.id == BuiltinPipelineOperations.ForwardOpaque));
        DirectionalShadowData shadowData = Assert.IsType<DirectionalShadowData>(forward.directionalShadow);
        Assert.Equal(3, shadowData.cascadeCount);
        Assert.True(shadowData.cascadeSplits.SequenceEqual(
            shadowData.cascadeSplits.OrderBy(static value => value)));
        Assert.All(shadowData.worldToShadowMatrices, static matrix => Assert.NotEqual(Matrix.identity, matrix));
    }

    [Fact]
    public void RenderWorld_CullsAndSortsOpaqueAndTransparentDeterministically()
    {
        var scene = new GameScene("World");
        AddRenderer(scene, "Near", 2f, transparent: false);
        AddRenderer(scene, "Far", 8f, transparent: false);
        AddRenderer(scene, "Transparent Near", 3f, transparent: true);
        AddRenderer(scene, "Transparent Far", 7f, transparent: true);
        AddRenderer(scene, "Behind", -5f, transparent: false);
        RenderView view = View();

        RenderCullingResults result = RenderWorldSnapshot.Capture([scene]).Cull(view);

        Assert.Equal([2f, 8f], result.opaqueObjects.Select(static value => value.bounds.center.z));
        Assert.Equal([7f, 3f], result.transparentObjects.Select(static value => value.bounds.center.z));
    }

    private static TestBuild Build(
        RenderPath path,
        GraphicsCapabilities capabilities,
        RenderWorldSnapshot world,
        bool enablePicking = false)
    {
        var graph = new RenderGraphBuilder(1, capabilities);
        var resources = new BuiltinRenderResources();
        var diagnostics = new RecordingDiagnostics();
        var executor = new EmptyExecutor();
        var asset = new RenderPipelineAsset
        {
            defaultRenderPath = path
        };
        var request = new RenderRequest(
            "Test",
            View(),
            RenderTarget.backbuffer,
            path,
            enablePicking: enablePicking);
        var context = new RenderPipelineContext(
            request,
            asset,
            world,
            path,
            graph,
            capabilities,
            resources,
            diagnostics,
            executor);
        new UniversalRenderPipeline().Build(context);
        return new TestBuild(graph.Compile(), resources, diagnostics, executor.operations);
    }

    private static RenderWorldSnapshot EmptyWorld()
        => RenderWorldSnapshot.Capture(Array.Empty<GameScene>());

    private static RenderView View()
        => new(
            Matrix.CreateLookAt(Vector3.ZERO, Vector3.FORWARD, Vector3.UP),
            Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60f), 1f, 0.1f, 100f),
            Vector3.ZERO,
            256,
            256,
            Inno.Engine.Scene.Layers.GameLayerMask.everything);

    private static MeshAsset CreateMesh()
        => new()
        {
            vertexCount = 3,
            indexCount = 3,
            subMeshCount = 1,
            boundsCenter = Vector3.ZERO,
            boundsExtents = Vector3.ONE
        };

    private static void AddRenderer(GameScene scene, string name, float z, bool transparent)
    {
        GameObject gameObject = scene.CreateObject(name);
        gameObject.transform.worldPosition = new Vector3(0f, 0f, z);
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.mesh = CreateMesh();
        var material = new MaterialAsset
        {
            renderQueue = transparent ? 3000 : 2000
        };
        renderer.SetMaterial(0, material);
    }

    private static GraphicsCapabilities FullCapabilities(
        int maxColorAttachments = 8,
        GraphicsFeature features = GraphicsFeature.Compute
            | GraphicsFeature.StorageBuffer
            | GraphicsFeature.TextureBlit)
        => new(
            GraphicsBackend.Noop,
            features,
            new GraphicsLimits(256, maxColorAttachments, 16384, 16),
            Enum.GetValues<RenderTextureFormat>(),
            [RenderTextureFormat.RGBA8, RenderTextureFormat.RGBA16Float, RenderTextureFormat.R32Float],
            originBottomLeft: false,
            homogeneousDepth: false);

    private static string FormatDiagnostics(RenderGraphCompileResult result)
        => string.Join(Environment.NewLine, result.diagnostics.Select(
            static diagnostic => $"{diagnostic.code}: {diagnostic.message}"));

    private sealed record TestBuild(
        RenderGraphCompileResult result,
        BuiltinRenderResources resources,
        RecordingDiagnostics diagnostics,
        IReadOnlyList<RenderPipelineOperation> operations);

    private sealed class RecordingDiagnostics : IRenderDiagnosticSink
    {
        public List<RenderDiagnostic> values { get; } = [];

        public void Publish(RenderDiagnostic diagnostic) => values.Add(diagnostic);
    }

    private sealed class EmptyExecutor : IRenderPipelineExecutor
    {
        public List<RenderPipelineOperation> operations { get; } = [];

        public void PrepareFrame(ulong frameIndex) { }

        public RenderTextureHandle ImportTarget(RenderGraphBuilder graph, RenderTexture target)
            => throw new NotSupportedException();

        public bool TryGetTargetTexture(RenderTexture target, out PersistentTextureHandle texture)
        {
            texture = default;
            return false;
        }

        public void ReleaseTarget(RenderTexture target) { }

        public void Prepare(RenderPipelineOperation operation)
        {
            operations.Add(operation);
        }

        public void Execute(RenderPipelineOperation operation, RenderPassContext context)
        {
        }
    }
}
