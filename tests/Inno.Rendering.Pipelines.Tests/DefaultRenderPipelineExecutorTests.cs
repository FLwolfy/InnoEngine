using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Inno.Core.Mathematics;
using Inno.Engine.Scene.Layers;
using Inno.Rendering.Assets;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Pipelines.Tests;

[Collection(RenderingPipelineTestCollection.NAME)]
public sealed class DefaultRenderPipelineExecutorTests
{
    [Fact]
    public void FullscreenOperation_PreparesProceduralPipelineAndRecordsBindings()
    {
        var device = new RecordingDevice();
        var artifacts = new RenderPipelineArtifactRegistry();
        var diagnostics = new RecordingDiagnosticSink();
        artifacts.InstallOperation("tests.fullscreen", FullscreenArtifact(1), "Fullscreen");
        RenderPipelineOperation operation = FullscreenOperation();
        using var executor = new DefaultRenderPipelineExecutor(device, artifacts, diagnostics);

        executor.Prepare(operation);
        var commands = new RecordingEncoder();
        executor.Execute(operation, new RenderPassContext(commands, 1));

        Assert.NotNull(device.graphicsDescriptor);
        Assert.Null(device.graphicsDescriptor.vertexLayout);
        Assert.Contains(
            device.graphicsDescriptor.bindings,
            binding => binding.id.value == "inno_scene_color"
                && binding.kind == RenderShaderBindingKind.Texture);
        Assert.Equal(1UL, commands.graphicsPipeline.value);
        Assert.Equal("inno_scene_color", Assert.Single(commands.textures).value);
        Assert.Equal(new float[] { 2f, 3f, 4f, 5f }, commands.uniforms["tests_parameters"]);
        Assert.Equal(3, commands.vertexCount);
        Assert.Empty(diagnostics.values);
    }

    [Fact]
    public void TaggedOperation_InstallsByStableMetadataId()
    {
        var artifacts = new RenderPipelineArtifactRegistry();

        IReadOnlyList<string> installed = artifacts.InstallTaggedOperations(FullscreenArtifact(1));

        Assert.Equal(new[] { "tests.fullscreen" }, installed);
        Assert.True(artifacts.TryGetOperation("tests.fullscreen", out _));
    }

    [Fact]
    public void CandidatePipelineFailure_KeepsCurrentLastGoodProgram()
    {
        var device = new RecordingDevice();
        var artifacts = new RenderPipelineArtifactRegistry();
        var diagnostics = new RecordingDiagnosticSink();
        RenderPipelineOperation operation = FullscreenOperation();
        artifacts.InstallOperation("tests.fullscreen", FullscreenArtifact(1), "Fullscreen");
        using var executor = new DefaultRenderPipelineExecutor(device, artifacts, diagnostics);
        executor.Prepare(operation);

        artifacts.InstallOperation("tests.fullscreen", FullscreenArtifact(2), "Fullscreen");
        device.failPipelineCreation = true;
        executor.Prepare(operation);
        var commands = new RecordingEncoder();
        executor.Execute(operation, new RenderPassContext(commands, 2));

        Assert.Equal(1UL, commands.graphicsPipeline.value);
        Assert.Equal(0, device.destroyedGraphicsPipelines);
        Assert.Contains(diagnostics.values, value => value.code == "RENDER_PIPELINE_CREATE_FAILED");
    }

    [Fact]
    public void ImportTarget_ReusesCurrentResourceAndReplacesAfterResize()
    {
        var device = new RecordingDevice();
        using var executor = new DefaultRenderPipelineExecutor(
            device,
            new RenderPipelineArtifactRegistry(),
            new RecordingDiagnosticSink());
        var target = new RenderTexture(
            "Scene View",
            new RenderTextureDescriptor(
                320,
                180,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));

        RenderTextureHandle first = executor.ImportTarget(new RenderGraphBuilder(1, Capabilities()), target);
        RenderTextureHandle second = executor.ImportTarget(new RenderGraphBuilder(2, Capabilities()), target);
        Assert.True(first.isValid);
        Assert.True(second.isValid);
        Assert.Equal(1, device.createdTextures);
        Assert.True(executor.TryGetTargetTexture(target, out PersistentTextureHandle beforeResize));

        target.Resize(new RenderTextureDescriptor(
            640,
            360,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled));
        _ = executor.ImportTarget(new RenderGraphBuilder(3, Capabilities()), target);

        Assert.Equal(2, device.createdTextures);
        Assert.Equal(1, device.destroyedTextures);
        Assert.True(executor.TryGetTargetTexture(target, out PersistentTextureHandle afterResize));
        Assert.NotEqual(beforeResize, afterResize);

        executor.ReleaseTarget(target);
        Assert.False(executor.TryGetTargetTexture(target, out _));
        executor.PrepareFrame(1);
        Assert.Equal(2, device.destroyedTextures);
    }

    [Fact]
    public void FullscreenOperation_BindsDirectionalPointSpotAndShadowContracts()
    {
        var device = new RecordingDevice();
        var artifacts = new RenderPipelineArtifactRegistry();
        artifacts.InstallOperation("tests.lighting", LightingArtifact(), "Fullscreen");
        var view = new RenderView(
            Matrix.identity,
            Matrix.identity,
            new Vector3(1f, 2f, 3f),
            64,
            32,
            GameLayerMask.everything);
        RenderLightData[] lights =
        [
            Light(RenderLightKind.Directional, Vector3.ZERO, Vector3.FORWARD, 0f, 1f, 1f),
            Light(RenderLightKind.Point, new Vector3(2f, 3f, 4f), Vector3.FORWARD, 10f, 1f, 1f),
            Light(RenderLightKind.Spot, new Vector3(5f, 6f, 7f), Vector3.FORWARD, 20f, 0.9f, 0.8f)
        ];
        var shadows = new DirectionalShadowData(
            [Matrix.CreateTranslation(new Vector3(1f, 2f, 3f))],
            [25f],
            0.75f,
            0.001f,
            1f / 1024f);
        var operation = new RenderPipelineOperation(
            "tests.lighting",
            RenderPipelineOperationKind.Fullscreen,
            view,
            lights: lights,
            directionalShadow: shadows);
        using var executor = new DefaultRenderPipelineExecutor(
            device,
            artifacts,
            new RecordingDiagnosticSink());

        executor.Prepare(operation);
        var commands = new RecordingEncoder();
        executor.Execute(operation, new RenderPassContext(commands, 1));

        Assert.Equal(new float[] { 2f, 1f, 3f, 8f }, commands.uniforms["inno_light_count"]);
        Assert.Equal(-1f, commands.uniforms["inno_local_light_color_inner_0"][3]);
        Assert.Equal(0.9f, commands.uniforms["inno_local_light_color_inner_1"][3]);
        Assert.Equal(
            new float[] { 1f, 0.75f, 0.001f, 1f / 1024f },
            commands.uniforms["inno_shadow_parameters"]);
        Assert.Equal(16, commands.uniforms["inno_shadow_matrix_0"].Length);
    }

    private static RenderPipelineOperation FullscreenOperation()
    {
        var graph = new RenderGraphBuilder(1, Capabilities());
        RenderTextureHandle sceneColor = graph.CreateTexture(
            "Scene Color",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.Sampled));
        return new RenderPipelineOperation(
            "tests.fullscreen",
            RenderPipelineOperationKind.Fullscreen,
            new RenderView(
                Matrix.identity,
                Matrix.identity,
                Vector3.ZERO,
                8,
                8,
                GameLayerMask.everything),
            textures:
            [
                new RenderTextureBinding(new RenderBindingId("inno_scene_color"), sceneColor)
            ],
            scalarParameter: 1f,
            uniforms:
            [
                new RenderUniformBinding(
                    new RenderBindingId("tests_parameters"),
                    new Vector4(2f, 3f, 4f, 5f))
            ]);
    }

    private static CompiledShaderArtifact FullscreenArtifact(byte marker)
    {
        var definition = new ShaderPassDefinition(
            "Fullscreen",
            BuiltinShaderPassTags.Fullscreen,
            "fullscreen-vs.sc",
            "fullscreen-fs.sc",
            null,
            null,
            tags: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BuiltinShaderMetadataTags.PipelineOperation] = "tests.fullscreen"
            });
        ShaderSourceLocation vertexLocation = new(
            "fullscreen-vs.sc",
            definition.name,
            ShaderStage.Vertex);
        ShaderSourceLocation fragmentLocation = new(
            "fullscreen-fs.sc",
            definition.name,
            ShaderStage.Fragment);
        var pass = new CompiledShaderPass(
            definition,
            [
                new ShaderStageArtifact(ShaderStage.Vertex, new byte[] { marker }, vertexLocation),
                new ShaderStageArtifact(ShaderStage.Fragment, new byte[] { marker }, fragmentLocation)
            ],
            new ShaderInterface(
            [
                new ShaderInterfaceBinding(
                    new ShaderPropertyId("inno_scene_color"),
                    ShaderPropertyType.Texture2D,
                    ShaderStage.Fragment),
                new ShaderInterfaceBinding(
                    new ShaderPropertyId("inno_exposure"),
                    ShaderPropertyType.Float,
                    ShaderStage.Fragment),
                new ShaderInterfaceBinding(
                    new ShaderPropertyId("tests_parameters"),
                    ShaderPropertyType.Vector4,
                    ShaderStage.Fragment)
            ]));
        return new CompiledShaderArtifact(
            "Tests/Fullscreen",
            "tests-target",
            ShaderVariantKey.empty,
            new ShaderInterface(
            [
                new ShaderInterfaceBinding(
                    new ShaderPropertyId("inno_scene_color"),
                    ShaderPropertyType.Texture2D,
                    ShaderStage.Fragment),
                new ShaderInterfaceBinding(
                    new ShaderPropertyId("inno_exposure"),
                    ShaderPropertyType.Float,
                    ShaderStage.Fragment),
                new ShaderInterfaceBinding(
                    new ShaderPropertyId("tests_parameters"),
                    ShaderPropertyType.Vector4,
                    ShaderStage.Fragment)
            ]),
            [pass]);
    }

    private static CompiledShaderArtifact LightingArtifact()
    {
        CompiledShaderArtifact artifact = FullscreenArtifact(1);
        var bindings = new List<ShaderInterfaceBinding>
        {
            new(new ShaderPropertyId("inno_light_count"), ShaderPropertyType.Vector4, ShaderStage.Fragment),
            new(new ShaderPropertyId("inno_camera_position"), ShaderPropertyType.Vector4, ShaderStage.Fragment),
            new(new ShaderPropertyId("inno_view_parameters"), ShaderPropertyType.Vector4, ShaderStage.Fragment),
            new(new ShaderPropertyId("inno_main_light_direction"), ShaderPropertyType.Vector4, ShaderStage.Fragment),
            new(new ShaderPropertyId("inno_main_light_color"), ShaderPropertyType.Vector4, ShaderStage.Fragment),
            new(new ShaderPropertyId("inno_shadow_cascade_splits"), ShaderPropertyType.Vector4, ShaderStage.Fragment),
            new(new ShaderPropertyId("inno_shadow_parameters"), ShaderPropertyType.Vector4, ShaderStage.Fragment),
            new(new ShaderPropertyId("inno_shadow_matrix_0"), ShaderPropertyType.Matrix4x4, ShaderStage.Fragment)
        };
        for (int index = 0; index < 2; index++)
        {
            bindings.Add(new ShaderInterfaceBinding(
                new ShaderPropertyId($"inno_local_light_position_range_{index}"),
                ShaderPropertyType.Vector4,
                ShaderStage.Fragment));
            bindings.Add(new ShaderInterfaceBinding(
                new ShaderPropertyId($"inno_local_light_direction_outer_{index}"),
                ShaderPropertyType.Vector4,
                ShaderStage.Fragment));
            bindings.Add(new ShaderInterfaceBinding(
                new ShaderPropertyId($"inno_local_light_color_inner_{index}"),
                ShaderPropertyType.Vector4,
                ShaderStage.Fragment));
        }

        CompiledShaderPass sourcePass = Assert.Single(artifact.passes);
        var pass = new CompiledShaderPass(
            sourcePass.definition,
            sourcePass.stages,
            new ShaderInterface(bindings));
        return new CompiledShaderArtifact(
            "Tests/Lighting",
            artifact.targetKey,
            artifact.variant,
            new ShaderInterface(bindings),
            [pass]);
    }

    private static RenderLightData Light(
        RenderLightKind kind,
        Vector3 position,
        Vector3 direction,
        float range,
        float innerCone,
        float outerCone)
        => new(
            Guid.NewGuid(),
            kind,
            position,
            direction,
            Color.WHITE,
            2f,
            range,
            innerCone,
            outerCone,
            kind == RenderLightKind.Directional,
            0.75f,
            kind == RenderLightKind.Directional ? 1 : 0);

    private static GraphicsCapabilities Capabilities()
        => new(
            GraphicsBackend.Noop,
            GraphicsFeature.Compute | GraphicsFeature.StorageBuffer,
            new GraphicsLimits(64, 8, 1024, 8),
            Enum.GetValues<RenderTextureFormat>(),
            Enum.GetValues<RenderTextureFormat>(),
            false,
            false);

    private sealed class RecordingDiagnosticSink : IRenderDiagnosticSink
    {
        public List<RenderDiagnostic> values { get; } = [];

        public void Publish(RenderDiagnostic diagnostic) => values.Add(diagnostic);
    }

    private sealed class RecordingDevice : IRenderDevice
    {
        private ulong m_nextPipeline = 1;
        private ulong m_nextTexture = 1;

        public GraphicsCapabilities capabilities { get; } = Capabilities();
        public uint generation => 1;
        public GraphicsPipelineDescriptor? graphicsDescriptor { get; private set; }
        public bool failPipelineCreation { get; set; }
        public int destroyedGraphicsPipelines { get; private set; }
        public int createdTextures { get; private set; }
        public int destroyedTextures { get; private set; }

        public void BeginFrame() { }
        public void Execute(CompiledRenderGraph graph, ulong frameIndex) { }
        public uint EndFrame() => 0;
        public void ResizeBackbuffer(int width, int height) { }
        public PersistentTextureHandle CreateTexture(RenderTextureDescriptor descriptor, string name)
        {
            createdTextures++;
            return new PersistentTextureHandle(m_nextTexture++, generation);
        }
        public void UpdateTexture(
            PersistentTextureHandle texture,
            ReadOnlySpan<byte> data,
            int mipLevel = 0,
            int arrayLayer = 0) { }
        public void DestroyTexture(PersistentTextureHandle texture) => destroyedTextures++;
        public PersistentBufferHandle CreateBuffer(
            PersistentBufferDescriptor descriptor,
            ReadOnlySpan<byte> initialData,
            string name)
            => new(1, generation);
        public void DestroyBuffer(PersistentBufferHandle buffer) { }
        public void UpdateBuffer(PersistentBufferHandle buffer, ReadOnlySpan<byte> data, int startElement = 0) { }

        public GraphicsPipelineHandle CreateGraphicsPipeline(
            GraphicsPipelineDescriptor descriptor,
            string name)
        {
            if (failPipelineCreation)
            {
                throw new InvalidOperationException("candidate failed");
            }

            graphicsDescriptor = descriptor;
            return new GraphicsPipelineHandle(m_nextPipeline++, generation);
        }

        public void DestroyGraphicsPipeline(GraphicsPipelineHandle pipeline)
            => destroyedGraphicsPipelines++;

        public ComputePipelineHandle CreateComputePipeline(ComputePipelineDescriptor descriptor, string name)
            => new(m_nextPipeline++, generation);
        public void DestroyComputePipeline(ComputePipelineHandle pipeline) { }
        public void Dispose() { }
    }

    private sealed class RecordingEncoder : RenderCommandEncoder
    {
        public GraphicsPipelineHandle graphicsPipeline { get; private set; }
        public List<RenderBindingId> textures { get; } = [];
        public Dictionary<string, float[]> uniforms { get; } = new(StringComparer.Ordinal);
        public int vertexCount { get; private set; }

        public override void BindGraphicsPipeline(GraphicsPipelineHandle pipeline) => graphicsPipeline = pipeline;
        public override void BindComputePipeline(ComputePipelineHandle pipeline) { }
        public override void BindTexture(RenderBindingId binding, RenderTextureHandle texture) => textures.Add(binding);
        public override void BindTexture(RenderBindingId binding, PersistentTextureHandle texture) => textures.Add(binding);
        public override void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer) { }
        public override void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer) { }
        public override void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value)
            => uniforms[binding.value] = MemoryMarshal.Cast<byte, float>(value).ToArray();
        public override void SetTransform(ReadOnlySpan<float> columnMajorMatrix) { }
        public override void SetScissor(int x, int y, int width, int height) { }
        public override void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0) { }
        public override void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0) { }
        public override void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0) { }
        public override void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0) { }
        public override void Draw(int vertexCount, int instanceCount = 1) => this.vertexCount = vertexCount;
        public override void DrawIndexed(int indexCount, int instanceCount = 1) { }
        public override void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1) { }
        public override void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination) { }
    }
}
