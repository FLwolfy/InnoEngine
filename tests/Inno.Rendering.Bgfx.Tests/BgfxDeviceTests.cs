using System;
using Inno.Rendering.Core;
using Inno.Platform;
using Xunit;

namespace Inno.Rendering.Bgfx.Tests;

[Collection(BgfxDeviceCollection.name)]
public sealed class BgfxDeviceTests
{
    private readonly BgfxDevice m_device;

    public BgfxDeviceTests(BgfxDeviceFixture fixture)
    {
        m_device = fixture.device;
    }

    [Fact]
    public void NoopDevice_ExecutesOneGraphAndSubmitsOneFrame()
    {
        RenderGraphBuilder builder = new(1, m_device.capabilities);
        builder.AddRasterPass("Noop Clear", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .HasSideEffect();
        CompiledRenderGraph graph = builder.Compile().graph!;

        m_device.BeginFrame();
        m_device.Execute(graph, 1);
        uint submitted = m_device.EndFrame();

        Assert.Equal(GraphicsBackend.Noop, m_device.capabilities.backend);
        Assert.Equal(submitted, m_device.backendFrame);
        Assert.Single(graph.passes);
    }

    [Fact]
    public void NoopDevice_AllocatesViewIdsAcrossAllGraphsInTheFrame()
    {
        RenderGraphBuilder firstBuilder = new(21, m_device.capabilities);
        firstBuilder.AddRasterPass("First", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .HasSideEffect();
        RenderGraphBuilder secondBuilder = new(22, m_device.capabilities);
        secondBuilder.AddRasterPass("Second A", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .HasSideEffect();
        secondBuilder.AddRasterPass("Second B", BuiltinRenderPhases.postProcessing, 0, static (_, _) => { })
            .After(BuiltinRenderPhases.opaque)
            .HasSideEffect();

        m_device.BeginFrame();
        m_device.Execute(firstBuilder.Compile().graph!, 21);
        m_device.Execute(secondBuilder.Compile().graph!, 22);

        Assert.Equal(3, m_device.allocatedViewCount);
        m_device.EndFrame();
    }

    [Fact]
    public void NoopDevice_ResetsFrameViewAllocationAtTheNextFrame()
    {
        RenderGraphBuilder builder = new(23, m_device.capabilities);
        builder.AddRasterPass("Only", BuiltinRenderPhases.opaque, 0, static (_, _) => { })
            .HasSideEffect();
        CompiledRenderGraph graph = builder.Compile().graph!;

        m_device.BeginFrame();
        m_device.Execute(graph, 23);
        m_device.EndFrame();
        m_device.BeginFrame();

        Assert.Equal(0, m_device.allocatedViewCount);
        m_device.EndFrame();
    }

    [Fact]
    public void CreateWindowSurface_RejectsUnsupportedPlatformHandlesBeforeNativeCreation()
    {
        m_device.BeginFrame();

        Assert.Throws<PlatformNotSupportedException>(() => m_device.CreateWindowSurface(
            new PlatformNativeHandles(new IntPtr(1), handleKind: PlatformNativeHandleKind.Unknown),
            32,
            32,
            "Unsupported"));

        m_device.EndFrame();
    }

    [Fact]
    public void NoopDevice_CreatesAndDefersPersistentTextureDestruction()
    {
        RenderTextureDescriptor descriptor = new(
            16,
            16,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.Sampled);

        m_device.BeginFrame();
        PersistentTextureHandle texture = m_device.CreateTexture(descriptor, "Persistent Test");
        m_device.DestroyTexture(texture);
        m_device.EndFrame();

        for (int index = 0; index < 4; index++)
        {
            m_device.BeginFrame();
            m_device.EndFrame();
        }

        Assert.True(texture.isValid);
    }

    [Fact]
    public void NoopDevice_UploadsCompletePersistentTextureSubresource()
    {
        RenderTextureDescriptor descriptor = new(
            4,
            4,
            RenderTextureFormat.RGBA8,
            RenderTextureUsage.Sampled);

        m_device.BeginFrame();
        PersistentTextureHandle texture = m_device.CreateTexture(descriptor, "Upload Test");
        m_device.UpdateTexture(texture, new byte[4 * 4 * 4]);
        m_device.DestroyTexture(texture);
        m_device.EndFrame();
    }

    [Fact]
    public void NoopDevice_UpdatesDynamicVertexBuffer()
    {
        RenderVertexLayout layout = new(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3)
        ]);
        PersistentBufferDescriptor descriptor = new(
            new RenderBufferDescriptor(3, layout.stride, RenderBufferUsage.Vertex | RenderBufferUsage.Dynamic),
            layout);

        m_device.BeginFrame();
        PersistentBufferHandle buffer = m_device.CreateBuffer(descriptor, ReadOnlySpan<byte>.Empty, "Dynamic");
        m_device.UpdateBuffer(buffer, new byte[layout.stride * 3]);
        m_device.DestroyBuffer(buffer);
        m_device.EndFrame();
    }

    [Fact]
    public void NoopDevice_CreatesVertexAndIndexBuffersAtFrameSafetyPoint()
    {
        RenderVertexLayout layout = new(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3)
        ]);
        PersistentBufferDescriptor vertices = new(
            new RenderBufferDescriptor(3, layout.stride, RenderBufferUsage.Vertex),
            layout);
        PersistentBufferDescriptor indices = new(
            new RenderBufferDescriptor(3, sizeof(ushort), RenderBufferUsage.Index),
            indexFormat: RenderIndexFormat.UInt16);

        m_device.BeginFrame();
        PersistentBufferHandle vertexBuffer = m_device.CreateBuffer(vertices, new byte[36], "Test Vertices");
        PersistentBufferHandle indexBuffer = m_device.CreateBuffer(indices, new byte[6], "Test Indices");
        m_device.DestroyBuffer(vertexBuffer);
        m_device.DestroyBuffer(indexBuffer);
        m_device.EndFrame();

        Assert.True(vertexBuffer.isValid);
        Assert.True(indexBuffer.isValid);
    }

    [Fact]
    public void NoopDevice_AllocatesTransientStorageBufferForCompiledGraph()
    {
        RenderGraphBuilder builder = new(8, m_device.capabilities);
        RenderBufferHandle buffer = builder.CreateBuffer(
            "Cluster Light List",
            new RenderBufferDescriptor(64, 16, RenderBufferUsage.Storage));
        builder.AddComputePass("Build Light List", BuiltinRenderPhases.lighting, 0, static (_, _) => { })
            .WriteBuffer(buffer)
            .HasSideEffect();
        CompiledRenderGraph graph = builder.Compile().graph!;

        m_device.BeginFrame();
        m_device.Execute(graph, 8);
        m_device.EndFrame();

        Assert.Single(graph.buffers);
    }

    [Fact]
    public void CreateBuffer_WithPartialInitialData_FailsWithoutClosingFrame()
    {
        RenderVertexLayout layout = new(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3)
        ]);
        PersistentBufferDescriptor descriptor = new(
            new RenderBufferDescriptor(3, layout.stride, RenderBufferUsage.Vertex),
            layout);

        m_device.BeginFrame();
        Assert.Throws<ArgumentException>(() => m_device.CreateBuffer(descriptor, new byte[12], "Partial"));
        m_device.EndFrame();
    }

    [Fact]
    public void NoopDevice_BindsRequestedTextureArrayLayer()
    {
        RenderGraphBuilder builder = new(4, m_device.capabilities);
        RenderTextureHandle texture = builder.CreateTexture(
            "Layered Target",
            new RenderTextureDescriptor(
                8,
                8,
                RenderTextureFormat.RGBA8,
                RenderTextureUsage.ColorAttachment,
                arrayLayers: 2));
        builder.AddRasterPass("Layer One", BuiltinRenderPhases.shadows, 0, static (_, _) => { })
            .UseColorAttachment(
                texture,
                0,
                RenderLoadAction.Clear,
                arrayLayer: 1);
        builder.MarkOutput(texture);
        CompiledRenderGraph graph = builder.Compile().graph!;

        m_device.BeginFrame();
        m_device.Execute(graph, 4);
        m_device.EndFrame();

        Assert.Equal(1, Assert.Single(Assert.Single(graph.passes).attachments).arrayLayer);
    }

    [Fact]
    public void EndFrame_WithoutBeginFrame_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => m_device.EndFrame());
    }

    [Fact]
    public void ResizeBackbuffer_AppliesAtNextFrameSafetyPoint()
    {
        m_device.ResizeBackbuffer(32, 24);
        m_device.BeginFrame();
        m_device.EndFrame();
    }
}

[CollectionDefinition(name, DisableParallelization = true)]
public sealed class BgfxDeviceCollection : ICollectionFixture<BgfxDeviceFixture>
{
    public const string name = "BGFX device";
}

public sealed class BgfxDeviceFixture : IDisposable
{
    public BgfxDeviceFixture()
    {
        device = CreateDevice();
    }

    public BgfxDevice device { get; }

    public void Dispose()
        => device.Dispose();

    private static BgfxDevice CreateDevice()
        => new(new BgfxDeviceOptions
        {
            preferredBackend = GraphicsBackend.Noop,
            backbufferWidth = 1,
            backbufferHeight = 1,
            verticalSync = false,
            sRgbBackbuffer = false,
            forceSingleThreaded = true,
            deferredDestroyFrames = 2
        });
}
