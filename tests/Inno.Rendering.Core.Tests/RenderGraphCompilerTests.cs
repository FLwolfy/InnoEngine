using System;
using System.Collections.Generic;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Core.Tests;

public sealed class RenderGraphCompilerTests
{
    [Fact]
    public void MutationScope_Uncommitted_RollsBackPassesResourcesAndOutputs()
    {
        RenderGraphBuilder graph = CreateGraph();
        using (graph.BeginMutationScope())
        {
            RenderTextureHandle temporary = graph.CreateTexture("Temporary", ColorDescriptor());
            graph.AddRasterPass("Failed Feature", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
                .UseColorAttachment(temporary, 0, RenderLoadAction.Clear);
            graph.MarkOutput(temporary);
        }

        graph.AddRasterPass("Survivor", BuiltinRenderPhases.afterRendering, 0, static (_, _) => { })
            .HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.True(result.succeeded);
        Assert.Equal(["Survivor"], PassNames(result.graph!));
        Assert.Empty(result.graph!.textures);
    }
    [Fact]
    public void Compile_SchedulesProducerBeforeConsumer()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle intermediate = graph.CreateTexture("Intermediate", ColorDescriptor());
        RenderTextureHandle output = graph.CreateTexture("Output", ColorDescriptor());
        graph.AddRasterPass("Producer", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .UseColorAttachment(intermediate, 0, RenderLoadAction.Clear);
        graph.AddRasterPass("Consumer", BuiltinRenderPhases.postProcessing, 0, static (_, _) => { })
            .UseColorAttachment(output, 0, RenderLoadAction.Clear)
            .ReadTexture(intermediate);
        graph.MarkOutput(output);

        RenderGraphCompileResult result = graph.Compile();

        Assert.True(result.succeeded);
        Assert.Equal(["Producer", "Consumer"], PassNames(result.graph!));
    }

    [Fact]
    public void Compile_CullsUnconsumedPass()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle unused = graph.CreateTexture("Unused", ColorDescriptor());
        RenderTextureHandle output = graph.CreateTexture("Output", ColorDescriptor());
        graph.AddRasterPass("Unused Pass", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .UseColorAttachment(unused, 0, RenderLoadAction.Clear);
        graph.AddRasterPass("Output Pass", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .UseColorAttachment(output, 0, RenderLoadAction.Clear);
        graph.MarkOutput(output);

        RenderGraphCompileResult result = graph.Compile();

        Assert.True(result.succeeded);
        Assert.Equal(["Output Pass"], PassNames(result.graph!));
    }

    [Fact]
    public void Compile_WithUninitializedRead_FailsClearly()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle input = graph.CreateTexture("Input", SampledDescriptor());
        graph.AddRasterPass("Reader", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .ReadTexture(input)
            .HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_UNINITIALIZED_READ");
    }

    [Fact]
    public void Compile_WithReadWriteDeclaredAsSeparateAccess_FailsHazardValidation()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle texture = graph.CreateTexture(
            "Storage",
            new RenderTextureDescriptor(
                64,
                64,
                RenderTextureFormat.RGBA16Float,
                RenderTextureUsage.Storage | RenderTextureUsage.Sampled));
        graph.AddComputePass("Hazard", BuiltinRenderPhases.lighting, 0, static (_, _) => { })
            .ReadTexture(texture);
        ComputePassBuilder writer = graph.AddComputePass(
            "Writer",
            BuiltinRenderPhases.lighting,
            0,
            static (_, _) => { });
        writer.WriteTexture(texture);
        writer.ReadTexture(texture);
        writer.HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_PASS_HAZARD");
    }

    [Fact]
    public void Compile_ReusesCompatibleNonOverlappingTextures()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle first = graph.CreateTexture("First", ColorDescriptor());
        RenderTextureHandle middle = graph.CreateTexture("Middle", ColorDescriptor());
        RenderTextureHandle second = graph.CreateTexture("Second", ColorDescriptor());
        RenderTextureHandle output = graph.CreateTexture("Output", ColorDescriptor());

        graph.AddRasterPass("A", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .UseColorAttachment(first, 0, RenderLoadAction.Clear);
        graph.AddRasterPass("B", BuiltinRenderPhases.lighting, 0, static (_, _) => { })
            .UseColorAttachment(middle, 0, RenderLoadAction.Clear)
            .ReadTexture(first);
        graph.AddRasterPass("C", BuiltinRenderPhases.transparent, 0, static (_, _) => { })
            .UseColorAttachment(second, 0, RenderLoadAction.Clear)
            .ReadTexture(middle);
        graph.AddRasterPass("D", BuiltinRenderPhases.postProcessing, 0, static (_, _) => { })
            .UseColorAttachment(output, 0, RenderLoadAction.Clear)
            .ReadTexture(second);
        graph.MarkOutput(output);

        CompiledRenderGraph compiled = graph.Compile().graph!;

        Assert.Equal(compiled.textures[0].physicalSlot, compiled.textures[2].physicalSlot);
    }

    [Fact]
    public void Compile_WhenViewLimitExceeded_FailsClearly()
    {
        RenderGraphBuilder graph = new(1, CreateCapabilities(maxViews: 1));
        graph.AddRasterPass("A", BuiltinRenderPhases.opaque, 0, static (_, _) => { }).HasSideEffect();
        graph.AddRasterPass("B", BuiltinRenderPhases.transparent, 0, static (_, _) => { }).HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_VIEW_LIMIT");
    }

    [Fact]
    public void Compile_WithPhaseCycle_FailsClearly()
    {
        RenderPhaseId left = new("tests.left");
        RenderPhaseId right = new("tests.right");
        RenderGraphBuilder graph = CreateGraph();
        RenderPassBuilder leftPass = graph.AddRasterPass("Left", left, 0, static (_, _) => { });
        leftPass.Before(right).HasSideEffect();
        RenderPassBuilder rightPass = graph.AddRasterPass("Right", right, 0, static (_, _) => { });
        rightPass.Before(left).HasSideEffect();

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_CYCLE");
    }

    [Fact]
    public void Compile_WithInvalidAttachmentSubresource_FailsClearly()
    {
        RenderGraphBuilder graph = CreateGraph();
        RenderTextureHandle texture = graph.CreateTexture(
            "Array",
            new RenderTextureDescriptor(
                128,
                128,
                RenderTextureFormat.RGBA16Float,
                RenderTextureUsage.ColorAttachment,
                mipCount: 2,
                arrayLayers: 2));
        graph.AddRasterPass("Invalid Layer", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .UseColorAttachment(
                texture,
                0,
                RenderLoadAction.Clear,
                mipLevel: 1,
                arrayLayer: 2);
        graph.MarkOutput(texture);

        RenderGraphCompileResult result = graph.Compile();

        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "RENDER_GRAPH_ATTACHMENT_SUBRESOURCE");
    }

    [Fact]
    public void Execute_WhenPassFails_StillEndsPassAndGraph()
    {
        RenderGraphBuilder graph = CreateGraph();
        graph.AddRasterPass(
                "Failure",
                BuiltinRenderPhases.opaque,
                0,
                static (_, _) => throw new InvalidOperationException("failure"))
            .HasSideEffect();
        CompiledRenderGraph compiled = graph.Compile().graph!;
        RecordingBackend backend = new();

        Assert.Throws<InvalidOperationException>(() => compiled.Execute(backend, 12));

        Assert.Equal(["BeginGraph", "Begin:Failure", "End:Failure", "EndGraph"], backend.calls);
    }

    private static RenderGraphBuilder CreateGraph() => new(1, CreateCapabilities(64));

    private static GraphicsCapabilities CreateCapabilities(int maxViews)
        => new(
            GraphicsBackend.Noop,
            GraphicsFeature.Compute | GraphicsFeature.StorageBuffer | GraphicsFeature.TextureBlit,
            new GraphicsLimits(maxViews, 8, 16384, 16),
            Enum.GetValues<RenderTextureFormat>(),
            [RenderTextureFormat.RGBA8, RenderTextureFormat.RGBA16Float, RenderTextureFormat.R32Float],
            false,
            false);

    private static RenderTextureDescriptor ColorDescriptor()
        => new(
            128,
            128,
            RenderTextureFormat.RGBA16Float,
            RenderTextureUsage.ColorAttachment | RenderTextureUsage.Sampled);

    private static RenderTextureDescriptor SampledDescriptor()
        => new(128, 128, RenderTextureFormat.RGBA8, RenderTextureUsage.Sampled);

    private static List<string> PassNames(CompiledRenderGraph graph)
    {
        List<string> names = [];
        foreach (CompiledRenderPass pass in graph.passes)
        {
            names.Add(pass.name);
        }

        return names;
    }

    private sealed class RecordingBackend : IRenderGraphBackend
    {
        public List<string> calls { get; } = [];

        public void BeginGraph(CompiledRenderGraph graph) => calls.Add("BeginGraph");

        public RenderCommandEncoder BeginPass(CompiledRenderPass pass)
        {
            calls.Add($"Begin:{pass.name}");
            return new EmptyEncoder();
        }

        public void EndPass(CompiledRenderPass pass) => calls.Add($"End:{pass.name}");

        public void EndGraph(CompiledRenderGraph graph) => calls.Add("EndGraph");
    }

    private sealed class EmptyEncoder : RenderCommandEncoder
    {
        public override void BindGraphicsPipeline(GraphicsPipelineHandle pipeline) { }
        public override void BindComputePipeline(ComputePipelineHandle pipeline) { }
        public override void BindTexture(RenderBindingId binding, RenderTextureHandle texture) { }
        public override void BindTexture(RenderBindingId binding, PersistentTextureHandle texture) { }
        public override void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer) { }
        public override void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer) { }
        public override void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value) { }
        public override void SetTransform(ReadOnlySpan<float> columnMajorMatrix) { }
        public override void SetScissor(int x, int y, int width, int height) { }
        public override void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0) { }
        public override void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0) { }
        public override void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0) { }
        public override void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0) { }
        public override void Draw(int vertexCount, int instanceCount = 1) { }
        public override void DrawIndexed(int indexCount, int instanceCount = 1) { }
        public override void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1) { }
        public override void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination) { }
    }
}
