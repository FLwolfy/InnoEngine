using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Inno.Native.Bgfx;
using Inno.Platform;
using Inno.Rendering.Core;

namespace Inno.Rendering.Bgfx;

/// <summary>
/// Implements the sole BGFX device generation, API-thread frame boundary and graph backend.
/// </summary>
public sealed unsafe partial class BgfxDevice : IRenderDevice, IRenderGraphBackend
{
    private static readonly object S_DEVICE_LOCK = new();
    private static int s_nextGeneration;
    private static bool s_deviceActive;
    private static bool s_singleThreadedDeviceInitialized;

    private readonly int m_apiThreadId;
    private readonly int m_deferredDestroyFrames;
    private readonly Dictionary<ulong, bgfx.TextureHandle> m_persistentTextures = [];
    private readonly Dictionary<ulong, RenderTextureDescriptor> m_persistentTextureDescriptors = [];
    private readonly List<DeferredResource> m_deferredResources = [];
    private readonly Dictionary<int, bgfx.TextureHandle> m_graphTextures = [];
    private readonly Dictionary<int, bgfx.TextureHandle> m_transientTextureSlots = [];
    private readonly List<bgfx.FrameBufferHandle> m_graphFrameBuffers = [];
    private readonly uint m_resetFlags;

    private CompiledRenderGraph? m_activeGraph;
    private bgfx.Encoder* m_activeEncoder;
    private ulong m_nextPersistentId = 1;
    private uint m_backendFrame;
    private int m_activeGraphViewBase;
    private int m_backbufferWidth;
    private int m_backbufferHeight;
    private int m_nextViewId;
    private int m_pendingWidth;
    private int m_pendingHeight;
    private int m_drawCount;
    private int m_dispatchCount;
    private bool m_frameOpen;
    private bool m_disposed;

    /// <summary>
    /// Initializes BGFX and captures immutable device capabilities.
    /// </summary>
    /// <param name="options">Backend-neutral initialization options.</param>
    /// <exception cref="InvalidOperationException">Thrown when another BGFX device is active or initialization fails.</exception>
    public BgfxDevice(BgfxDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (S_DEVICE_LOCK)
        {
            if (s_deviceActive)
            {
                throw new InvalidOperationException("Only one BGFX device may be active in a process.");
            }

            if (options.forceSingleThreaded && s_singleThreadedDeviceInitialized)
            {
                throw new InvalidOperationException(
                    "BGFX single-threaded mode cannot be initialized again after device shutdown in the same process.");
            }

            s_deviceActive = true;
        }

        m_apiThreadId = Environment.CurrentManagedThreadId;
        m_deferredDestroyFrames = options.deferredDestroyFrames;
        m_backbufferWidth = options.backbufferWidth;
        m_backbufferHeight = options.backbufferHeight;
        m_resetFlags = (uint)(
            (options.verticalSync ? bgfx.ResetFlags.Vsync : bgfx.ResetFlags.None)
            | (options.sRgbBackbuffer ? bgfx.ResetFlags.SrgbBackbuffer : bgfx.ResetFlags.None));

        try
        {
            if (options.forceSingleThreaded)
            {
                bgfx.render_frame(0);
            }

            bgfx.Init init;
            bgfx.init_ctor(&init);
            if (options.preferredBackend.HasValue)
            {
                init.type = BgfxCapabilityMapper.ToNativeRenderer(options.preferredBackend.Value);
            }

            ApplyPlatformData(ref init, options.window);
            init.resolution.width = checked((uint)m_backbufferWidth);
            init.resolution.height = checked((uint)m_backbufferHeight);
            init.resolution.reset = m_resetFlags;
            if (!bgfx.init(&init))
            {
                throw new InvalidOperationException("BGFX device initialization failed.");
            }

            generation = unchecked((uint)Interlocked.Increment(ref s_nextGeneration));
            if (generation == 0)
            {
                generation = unchecked((uint)Interlocked.Increment(ref s_nextGeneration));
            }

            capabilities = BgfxCapabilityMapper.FromNative(bgfx.get_caps());
            if (options.forceSingleThreaded)
            {
                s_singleThreadedDeviceInitialized = true;
            }
        }
        catch
        {
            lock (S_DEVICE_LOCK)
            {
                s_deviceActive = false;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public GraphicsCapabilities capabilities { get; }

    /// <inheritdoc />
    public uint generation { get; private set; }

    /// <inheritdoc />
    public RenderDeviceFrameCounters frameCounters
        => new(Volatile.Read(ref m_drawCount), Volatile.Read(ref m_dispatchCount));

    /// <summary>Gets the last frame number returned by BGFX submission.</summary>
    public uint backendFrame => m_backendFrame;

    /// <inheritdoc />
    public void BeginFrame()
    {
        EnsureApiThread();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_frameOpen)
        {
            throw new InvalidOperationException("A BGFX frame is already open.");
        }

        ResetPreviousViews();
        Volatile.Write(ref m_drawCount, 0);
        Volatile.Write(ref m_dispatchCount, 0);
        ProcessDeferredResources(force: false);
        if (m_pendingWidth > 0 && m_pendingHeight > 0)
        {
            m_backbufferWidth = m_pendingWidth;
            m_backbufferHeight = m_pendingHeight;
            m_pendingWidth = 0;
            m_pendingHeight = 0;
            if (capabilities.backend != GraphicsBackend.Noop)
            {
                bgfx.reset(
                    checked((uint)m_backbufferWidth),
                    checked((uint)m_backbufferHeight),
                    m_resetFlags,
                    bgfx.TextureFormat.Count);
            }
        }

        m_frameOpen = true;
    }

    /// <inheritdoc />
    public void Execute(CompiledRenderGraph graph, ulong frameIndex)
    {
        EnsureFrameSafetyPoint();
        ArgumentNullException.ThrowIfNull(graph);
        graph.Execute(this, frameIndex);
    }

    /// <inheritdoc />
    public uint EndFrame()
    {
        EnsureApiThread();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_frameOpen)
        {
            throw new InvalidOperationException("No BGFX frame is open.");
        }

        if (m_activeGraph is not null || m_activeEncoder is not null)
        {
            throw new InvalidOperationException("All render graphs and encoders must end before BGFX frame submission.");
        }

        if (m_nextViewId == 0)
        {
            bgfx.touch(0);
        }

        m_backendFrame = bgfx.frame((byte)bgfx.FrameFlags.None);
        m_frameOpen = false;
        return m_backendFrame;
    }

    /// <inheritdoc />
    public void ResizeBackbuffer(int width, int height)
    {
        EnsureApiThread();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        m_pendingWidth = width;
        m_pendingHeight = height;
    }

    /// <inheritdoc />
    public PersistentTextureHandle CreateTexture(RenderTextureDescriptor descriptor, string name)
    {
        EnsureFrameSafetyPoint();
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        bgfx.TextureHandle nativeTexture = CreateNativeTexture(descriptor);
        if (!nativeTexture.Valid)
        {
            throw new InvalidOperationException($"BGFX could not create texture '{name}'.");
        }

        bgfx.set_texture_name(nativeTexture, name, Utf8Length(name));
        ulong id = m_nextPersistentId++;
        m_persistentTextures.Add(id, nativeTexture);
        m_persistentTextureDescriptors.Add(id, descriptor);
        return new PersistentTextureHandle(id, generation);
    }

    /// <inheritdoc />
    public PersistentTextureHandle CreateTexture(
        RenderTextureContainer container,
        ReadOnlySpan<byte> data,
        bool sRgb,
        string name)
    {
        EnsureFrameSafetyPoint();
        if (container != RenderTextureContainer.Ktx)
        {
            throw new NotSupportedException($"BGFX does not accept texture container '{container}'.");
        }

        if (data.IsEmpty)
        {
            throw new ArgumentException("An encoded texture container cannot be empty.", nameof(data));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        bgfx.Memory* memory;
        fixed (byte* pointer = data)
        {
            memory = bgfx.copy(pointer, checked((uint)data.Length));
        }

        bgfx.TextureInfo info = default;
        bgfx.TextureHandle nativeTexture = bgfx.create_texture(
            memory,
            sRgb ? (ulong)bgfx.TextureFlags.Srgb : 0,
            0,
            &info);
        if (!nativeTexture.Valid)
        {
            throw new InvalidOperationException($"BGFX could not create encoded texture '{name}'.");
        }

        bgfx.set_texture_name(nativeTexture, name, Utf8Length(name));
        ulong id = m_nextPersistentId++;
        m_persistentTextures.Add(id, nativeTexture);
        return new PersistentTextureHandle(id, generation);
    }

    /// <inheritdoc />
    public void UpdateTexture(
        PersistentTextureHandle texture,
        ReadOnlySpan<byte> data,
        int mipLevel = 0,
        int arrayLayer = 0)
    {
        EnsureFrameSafetyPoint();
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayLayer);
        ValidatePersistentHandle(texture);
        if (!m_persistentTextures.TryGetValue(texture.value, out bgfx.TextureHandle nativeTexture)
            || !m_persistentTextureDescriptors.TryGetValue(texture.value, out RenderTextureDescriptor? descriptor))
        {
            throw new ArgumentException("Persistent texture is not active on this device.", nameof(texture));
        }

        if (mipLevel >= descriptor.mipCount
            || arrayLayer >= descriptor.GetSubresourceLayerCount(mipLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), "Texture subresource is outside the descriptor.");
        }

        int width = Math.Max(1, descriptor.width >> mipLevel);
        int height = Math.Max(1, descriptor.height >> mipLevel);
        int expectedSize = checked(width * height * BytesPerPixel(descriptor.format));
        if (data.Length != expectedSize)
        {
            throw new ArgumentException(
                $"Texture update requires exactly {expectedSize} tightly packed bytes.",
                nameof(data));
        }

        bgfx.Memory* memory;
        fixed (byte* pointer = data)
        {
            memory = bgfx.copy(pointer, checked((uint)data.Length));
        }

        switch (descriptor.dimension)
        {
            case RenderTextureDimension.Texture2D:
                bgfx.update_texture_2d(
                    nativeTexture,
                    checked((ushort)arrayLayer),
                    checked((byte)mipLevel),
                    0,
                    0,
                    checked((ushort)width),
                    checked((ushort)height),
                    memory,
                    ushort.MaxValue);
                break;
            case RenderTextureDimension.Texture3D:
                bgfx.update_texture_3d(
                    nativeTexture,
                    checked((byte)mipLevel),
                    0,
                    0,
                    checked((ushort)arrayLayer),
                    checked((ushort)width),
                    checked((ushort)height),
                    1,
                    memory);
                break;
            case RenderTextureDimension.Cube:
                bgfx.update_texture_cube(
                    nativeTexture,
                    checked((ushort)(arrayLayer / 6)),
                    checked((byte)(arrayLayer % 6)),
                    checked((byte)mipLevel),
                    0,
                    0,
                    checked((ushort)width),
                    checked((ushort)height),
                    memory,
                    ushort.MaxValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(descriptor));
        }
    }

    /// <inheritdoc />
    public void DestroyTexture(PersistentTextureHandle texture)
    {
        EnsureFrameSafetyPoint();
        ValidatePersistentHandle(texture);
        if (!m_persistentTextures.Remove(texture.value, out bgfx.TextureHandle nativeTexture))
        {
            throw new ArgumentException("Persistent texture is not active on this device.", nameof(texture));
        }

        m_persistentTextureDescriptors.Remove(texture.value);

        EnqueueDestroy(DeferredResource.ForTexture(nativeTexture));
    }

    /// <inheritdoc />
    public void BeginGraph(CompiledRenderGraph graph)
    {
        EnsureFrameSafetyPoint();
        ArgumentNullException.ThrowIfNull(graph);
        if (m_activeGraph is not null)
        {
            throw new InvalidOperationException("A render graph is already executing.");
        }

        int requestedViews = graph.passes.Count;
        if (requestedViews > capabilities.limits.maxViews - m_nextViewId)
        {
            throw new InvalidOperationException(
                $"Frame requires at least {m_nextViewId + requestedViews} views, "
                + $"but the device supports {capabilities.limits.maxViews}. "
                + "Reduce active cameras, viewports, or render passes.");
        }

        m_activeGraph = graph;
        m_activeGraphViewBase = m_nextViewId;
        m_graphTextures.Clear();
        m_transientTextureSlots.Clear();
        m_graphFrameBuffers.Clear();
        try
        {
            PrepareGraphBuffers(graph);

            foreach (CompiledRenderTexture texture in graph.textures)
            {
                bgfx.TextureHandle nativeTexture;
                if (texture.imported)
                {
                    ValidatePersistentHandle(texture.persistentHandle);
                    if (!m_persistentTextures.TryGetValue(texture.persistentHandle.value, out nativeTexture))
                    {
                        throw new InvalidOperationException($"Imported texture '{texture.name}' is no longer active.");
                    }
                }
                else
                {
                    if (texture.physicalSlot < 0)
                    {
                        continue;
                    }

                    if (!m_transientTextureSlots.TryGetValue(texture.physicalSlot, out nativeTexture))
                    {
                        nativeTexture = CreateNativeTexture(texture.descriptor);
                        if (!nativeTexture.Valid)
                        {
                            throw new InvalidOperationException($"BGFX could not allocate transient texture '{texture.name}'.");
                        }

                        bgfx.set_texture_name(nativeTexture, texture.name, Utf8Length(texture.name));
                        m_transientTextureSlots.Add(texture.physicalSlot, nativeTexture);
                    }
                }

                m_graphTextures.Add(texture.handle.index, nativeTexture);
            }

            if (graph.passes.Count != 0)
            {
                ushort* order = stackalloc ushort[graph.passes.Count];
                for (int index = 0; index < graph.passes.Count; index++)
                {
                    order[index] = checked((ushort)(m_activeGraphViewBase + graph.passes[index].viewIndex));
                }

                bgfx.set_view_order(
                    checked((ushort)m_activeGraphViewBase),
                    checked((ushort)graph.passes.Count),
                    order);
            }

            m_nextViewId += requestedViews;
        }
        catch
        {
            ReleasePreparedGraphResources();
            m_activeGraphViewBase = 0;
            throw;
        }
    }

    /// <inheritdoc />
    public RenderCommandEncoder BeginPass(CompiledRenderPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (m_activeGraph is null || m_activeEncoder is not null)
        {
            throw new InvalidOperationException("Pass execution is outside a valid BGFX graph scope.");
        }

        ushort viewId = checked((ushort)(m_activeGraphViewBase + pass.viewIndex));
        bgfx.set_view_name(viewId, pass.name, Utf8Length(pass.name));
        bgfx.set_view_mode(viewId, bgfx.ViewMode.Sequential);
        ApplyViewTransform(viewId, pass.viewTransform);
        ConfigureViewTarget(viewId, pass);
        m_activeEncoder = bgfx.encoder_begin(false);
        if (m_activeEncoder is null)
        {
            throw new InvalidOperationException($"BGFX could not acquire an encoder for pass '{pass.name}'.");
        }

        bgfx.encoder_touch(m_activeEncoder, viewId);
        return new BgfxCommandEncoder(this, m_activeEncoder, viewId);
    }

    /// <inheritdoc />
    public void EndPass(CompiledRenderPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (m_activeEncoder is null)
        {
            throw new InvalidOperationException("No BGFX encoder is active.");
        }

        bgfx.encoder_end(m_activeEncoder);
        m_activeEncoder = null;
    }

    /// <inheritdoc />
    public void EndGraph(CompiledRenderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!ReferenceEquals(m_activeGraph, graph) || m_activeEncoder is not null)
        {
            throw new InvalidOperationException("BGFX graph cleanup does not match the active graph state.");
        }

        foreach (bgfx.FrameBufferHandle frameBuffer in m_graphFrameBuffers)
        {
            EnqueueDestroy(DeferredResource.ForFrameBuffer(frameBuffer));
        }

        foreach (bgfx.TextureHandle texture in m_transientTextureSlots.Values)
        {
            EnqueueDestroy(DeferredResource.ForTexture(texture));
        }

        foreach (BgfxBufferResource buffer in m_transientBufferSlots.Values)
        {
            EnqueueDestroy(DeferredResource.ForBuffer(buffer));
        }

        m_graphFrameBuffers.Clear();
        m_transientTextureSlots.Clear();
        m_graphTextures.Clear();
        m_transientBufferSlots.Clear();
        m_graphBuffers.Clear();
        m_activeGraph = null;
        m_activeGraphViewBase = 0;
    }

    internal int allocatedViewCount => m_nextViewId;

    internal void RecordDraw(int count = 1) => Interlocked.Add(ref m_drawCount, count);

    internal void RecordDispatch(int count = 1) => Interlocked.Add(ref m_dispatchCount, count);

    /// <summary>
    /// Shuts down BGFX after releasing all active and queued backend resources.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }

        EnsureApiThread();
        if (m_activeEncoder is not null || m_activeGraph is not null)
        {
            throw new InvalidOperationException("Cannot dispose BGFX while a render graph or encoder is active.");
        }

        if (m_frameOpen)
        {
            EndFrame();
        }

        ResetPreviousViews();

        foreach (bgfx.TextureHandle texture in m_persistentTextures.Values)
        {
            EnqueueDestroy(DeferredResource.ForTexture(texture));
        }

        foreach (BgfxBufferResource buffer in m_persistentBuffers.Values)
        {
            EnqueueDestroy(DeferredResource.ForBuffer(buffer));
        }

        foreach (BgfxPipelineResource pipeline in m_graphicsPipelines.Values)
        {
            EnqueuePipelineDestroy(pipeline);
            if (pipeline.vertexLayoutHandle.Valid)
            {
                EnqueueDestroy(DeferredResource.ForVertexLayout(pipeline.vertexLayoutHandle));
            }
        }

        foreach (BgfxPipelineResource pipeline in m_computePipelines.Values)
        {
            EnqueuePipelineDestroy(pipeline);
        }

        foreach (BgfxWindowSurfaceResource surface in m_windowSurfaces.Values)
        {
            EnqueueDestroy(DeferredResource.ForFrameBuffer(surface.frameBuffer));
        }

        m_persistentTextures.Clear();
        m_persistentTextureDescriptors.Clear();
        m_persistentBuffers.Clear();
        m_graphicsPipelines.Clear();
        m_computePipelines.Clear();
        m_windowSurfaces.Clear();
        DrainDeferredResourcesForShutdown();
        bgfx.shutdown();
        m_disposed = true;
        generation = 0;
        lock (S_DEVICE_LOCK)
        {
            s_deviceActive = false;
        }
    }

    internal bgfx.TextureHandle ResolveTexture(RenderTextureHandle texture)
    {
        if (m_activeGraph is null
            || texture.generation != m_activeGraph.generation
            || !m_graphTextures.TryGetValue(texture.index, out bgfx.TextureHandle nativeTexture))
        {
            throw new ArgumentException("Texture is not active in the current BGFX graph.", nameof(texture));
        }

        return nativeTexture;
    }

    internal RenderTextureDescriptor ResolveTextureDescriptor(RenderTextureHandle texture)
    {
        if (m_activeGraph is null || texture.generation != m_activeGraph.generation)
        {
            throw new ArgumentException("Texture is not active in the current BGFX graph.", nameof(texture));
        }

        return m_activeGraph.textures[texture.index].descriptor;
    }

    private void ConfigureViewTarget(ushort viewId, CompiledRenderPass pass)
    {
        int width = m_backbufferWidth;
        int height = m_backbufferHeight;
        bgfx.ClearFlags clearFlags = 0;
        uint clearColor = 0;
        float clearDepth = 1f;
        byte clearStencil = 0;

        if (pass.surface.isValid)
        {
            BgfxWindowSurfaceResource surface = ResolveSurface(pass.surface);
            width = surface.width;
            height = surface.height;
            bgfx.set_view_frame_buffer(viewId, surface.frameBuffer);
        }
        else if (pass.attachments.Count != 0)
        {
            bgfx.Attachment* attachments = stackalloc bgfx.Attachment[pass.attachments.Count];
            for (int index = 0; index < pass.attachments.Count; index++)
            {
                CompiledRenderAttachment attachment = pass.attachments[index];
                bgfx.TextureHandle nativeTexture = ResolveTexture(attachment.texture);
                bgfx.attachment_init(
                    &attachments[index],
                    nativeTexture,
                    bgfx.Access.Write,
                    checked((ushort)attachment.arrayLayer),
                    1,
                    checked((ushort)attachment.mipLevel),
                    (byte)bgfx.ResolveFlags.None);
                RenderTextureDescriptor descriptor = ResolveTextureDescriptor(attachment.texture);
                width = Math.Max(1, descriptor.width >> attachment.mipLevel);
                height = Math.Max(1, descriptor.height >> attachment.mipLevel);

                if (attachment.loadAction == RenderLoadAction.Clear)
                {
                    if (attachment.isDepth)
                    {
                        clearFlags |= bgfx.ClearFlags.Depth;
                        if (descriptor.format == RenderTextureFormat.Depth24Stencil8)
                        {
                            clearFlags |= bgfx.ClearFlags.Stencil;
                        }

                        clearDepth = attachment.clearDepth;
                        clearStencil = attachment.clearStencil;
                    }
                    else
                    {
                        clearFlags |= bgfx.ClearFlags.Color;
                        clearColor = PackColor(attachment.clearColor);
                    }
                }

                if (attachment.storeAction == RenderStoreAction.Discard)
                {
                    clearFlags |= attachment.isDepth
                        ? bgfx.ClearFlags.DiscardDepth
                        : ColorDiscardFlag(attachment.slot);
                }
            }

            bgfx.FrameBufferHandle frameBuffer = bgfx.create_frame_buffer_from_attachment(
                checked((byte)pass.attachments.Count),
                attachments,
                false);
            if (!frameBuffer.Valid)
            {
                throw new InvalidOperationException($"BGFX could not create framebuffer for pass '{pass.name}'.");
            }

            m_graphFrameBuffers.Add(frameBuffer);
            bgfx.set_view_frame_buffer(viewId, frameBuffer);
        }
        else
        {
            bgfx.set_view_frame_buffer(viewId, new bgfx.FrameBufferHandle { idx = ushort.MaxValue });
        }


        if (pass.clearsPresentationTarget)
        {
            clearFlags |= bgfx.ClearFlags.Color;
            clearColor = PackColor(pass.presentationClearColor);
        }

        bgfx.set_view_rect(
            viewId,
            0,
            0,
            checked((ushort)width),
            checked((ushort)height));
        bgfx.set_view_clear(viewId, (ushort)clearFlags, clearColor, clearDepth, clearStencil);
    }

    private static void ApplyViewTransform(ushort viewId, RenderViewTransform? transform)
    {
        if (transform is null)
        {
            bgfx.set_view_transform(viewId, null, null);
            return;
        }

        ReadOnlySpan<float> view = transform.viewMatrix.Span;
        ReadOnlySpan<float> projection = transform.projectionMatrix.Span;
        fixed (float* viewPointer = view)
        fixed (float* projectionPointer = projection)
        {
            bgfx.set_view_transform(viewId, viewPointer, projectionPointer);
        }
    }

    private bgfx.TextureHandle CreateNativeTexture(RenderTextureDescriptor descriptor)
    {
        if (descriptor.width > capabilities.limits.maxTextureSize
            || descriptor.height > capabilities.limits.maxTextureSize
            || descriptor.depth > capabilities.limits.maxTextureSize)
        {
            throw new NotSupportedException(
                "The texture descriptor exceeds the active backend extent limit.");
        }

        if (!capabilities.SupportsSampled(descriptor.format, descriptor.dimension)
            && (descriptor.usage & RenderTextureUsage.Sampled) != 0)
        {
            throw new NotSupportedException(
                $"The active graphics backend cannot sample {descriptor.dimension} textures in format '{descriptor.format}'.");
        }

        if ((descriptor.usage
                & (RenderTextureUsage.ColorAttachment | RenderTextureUsage.DepthStencilAttachment)) != 0
            && !capabilities.SupportsRenderTarget(descriptor.format))
        {
            throw new NotSupportedException(
                $"The active graphics backend cannot attach texture format '{descriptor.format}'.");
        }

        if ((descriptor.usage
                & (RenderTextureUsage.ColorAttachment | RenderTextureUsage.DepthStencilAttachment)) != 0
            && descriptor.sampleCount > 1
            && !capabilities.SupportsMultisampleRenderTarget(descriptor.format))
        {
            throw new NotSupportedException(
                $"The active graphics backend cannot multisample texture format '{descriptor.format}'.");
        }

        if ((descriptor.usage & RenderTextureUsage.Storage) != 0
            && (!capabilities.Supports(GraphicsFeature.Compute)
                || !capabilities.Supports(GraphicsFeature.StorageTexture)
                || (!capabilities.SupportsStorage(descriptor.format, RenderStorageAccess.Read)
                    && !capabilities.SupportsStorage(descriptor.format, RenderStorageAccess.Write))))
        {
            throw new NotSupportedException(
                $"The active graphics backend cannot use texture format '{descriptor.format}' for storage access.");
        }

        if (descriptor.dimension == RenderTextureDimension.Texture2D
            && descriptor.arrayLayers > 1
            && !capabilities.Supports(GraphicsFeature.Texture2DArray))
        {
            throw new NotSupportedException(
                "The active graphics backend does not support two-dimensional texture arrays.");
        }

        if (descriptor.dimension == RenderTextureDimension.Texture3D
            && !capabilities.Supports(GraphicsFeature.Texture3D))
        {
            throw new NotSupportedException(
                "The active graphics backend does not support three-dimensional textures.");
        }

        if (descriptor.dimension == RenderTextureDimension.Cube
            && descriptor.arrayLayers > 1
            && !capabilities.Supports(GraphicsFeature.TextureCubeArray))
        {
            throw new NotSupportedException(
                "The active graphics backend does not support cubemap texture arrays.");
        }

        bgfx.TextureFlags flags = bgfx.TextureFlags.None;
        if ((descriptor.usage
            & (RenderTextureUsage.ColorAttachment | RenderTextureUsage.DepthStencilAttachment)) != 0)
        {
            flags |= descriptor.sampleCount switch
            {
                1 => bgfx.TextureFlags.Rt,
                2 => bgfx.TextureFlags.RtMsaaX2,
                4 => bgfx.TextureFlags.RtMsaaX4,
                8 => bgfx.TextureFlags.RtMsaaX8,
                16 => bgfx.TextureFlags.RtMsaaX16,
                _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
            };
        }

        if ((descriptor.usage & RenderTextureUsage.Storage) != 0)
        {
            flags |= bgfx.TextureFlags.ComputeWrite;
        }

        if ((descriptor.usage & RenderTextureUsage.CopyDestination) != 0)
        {
            flags |= bgfx.TextureFlags.BlitDst;
        }

        if (descriptor.format == RenderTextureFormat.RGBA8Srgb)
        {
            flags |= bgfx.TextureFlags.Srgb;
        }

        return descriptor.dimension switch
        {
            RenderTextureDimension.Texture2D => bgfx.create_texture_2d(
                checked((ushort)descriptor.width),
                checked((ushort)descriptor.height),
                descriptor.mipCount > 1,
                checked((ushort)descriptor.arrayLayers),
                BgfxCapabilityMapper.ToNativeFormat(descriptor.format),
                (ulong)flags,
                null,
                0),
            RenderTextureDimension.Texture3D => bgfx.create_texture_3d(
                checked((ushort)descriptor.width),
                checked((ushort)descriptor.height),
                checked((ushort)descriptor.depth),
                descriptor.mipCount > 1,
                BgfxCapabilityMapper.ToNativeFormat(descriptor.format),
                (ulong)flags,
                null,
                0),
            RenderTextureDimension.Cube => bgfx.create_texture_cube(
                checked((ushort)descriptor.width),
                descriptor.mipCount > 1,
                checked((ushort)descriptor.arrayLayers),
                BgfxCapabilityMapper.ToNativeFormat(descriptor.format),
                (ulong)flags,
                null,
                0),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
        };
    }

    private void EnqueueDestroy(DeferredResource resource)
        => m_deferredResources.Add(resource with
        {
            eligibleFrame = m_backendFrame
                + checked((uint)m_deferredDestroyFrames)
        });

    private void EnqueuePipelineDestroy(BgfxPipelineResource pipeline)
        => EnqueueDestroy(DeferredResource.ForProgram(pipeline.program));

    private void ReleasePreparedGraphResources()
    {
        foreach (bgfx.FrameBufferHandle frameBuffer in m_graphFrameBuffers)
        {
            EnqueueDestroy(DeferredResource.ForFrameBuffer(frameBuffer));
        }

        foreach (bgfx.TextureHandle texture in m_transientTextureSlots.Values)
        {
            EnqueueDestroy(DeferredResource.ForTexture(texture));
        }

        foreach (BgfxBufferResource buffer in m_transientBufferSlots.Values)
        {
            EnqueueDestroy(DeferredResource.ForBuffer(buffer));
        }

        m_graphFrameBuffers.Clear();
        m_transientTextureSlots.Clear();
        m_transientBufferSlots.Clear();
        m_graphTextures.Clear();
        m_graphBuffers.Clear();
        m_activeGraph = null;
        m_activeGraphViewBase = 0;
    }

    private void ProcessDeferredResources(bool force)
    {
        if (m_deferredResources.Count == 0)
        {
            return;
        }

        List<DeferredResource> pending = [];
        foreach (DeferredResource resource in m_deferredResources)
        {
            if (!force && resource.eligibleFrame > m_backendFrame)
            {
                pending.Add(resource);
                continue;
            }

            switch (resource.kind)
            {
                case DeferredResourceKind.Texture:
                    bgfx.destroy_texture(new bgfx.TextureHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.FrameBuffer:
                    bgfx.destroy_frame_buffer(new bgfx.FrameBufferHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.VertexBuffer:
                    bgfx.destroy_vertex_buffer(new bgfx.VertexBufferHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.IndexBuffer:
                    bgfx.destroy_index_buffer(new bgfx.IndexBufferHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.DynamicVertexBuffer:
                    bgfx.destroy_dynamic_vertex_buffer(new bgfx.DynamicVertexBufferHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.DynamicIndexBuffer:
                    bgfx.destroy_dynamic_index_buffer(new bgfx.DynamicIndexBufferHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.IndirectBuffer:
                    bgfx.destroy_indirect_buffer(new bgfx.IndirectBufferHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.Program:
                    bgfx.destroy_program(new bgfx.ProgramHandle { idx = resource.index });
                    break;
                case DeferredResourceKind.VertexLayout:
                    bgfx.destroy_vertex_layout(new bgfx.VertexLayoutHandle { idx = resource.index });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resource));
            }
        }

        m_deferredResources.Clear();
        m_deferredResources.AddRange(pending);
    }

    private void DrainDeferredResourcesForShutdown()
    {
        while (m_deferredResources.Count != 0)
        {
            bgfx.touch(0);
            m_backendFrame = bgfx.frame((byte)bgfx.FrameFlags.None);
            ProcessDeferredResources(force: false);
        }

        for (int frame = 0; frame < m_deferredDestroyFrames; frame++)
        {
            bgfx.touch(0);
            m_backendFrame = bgfx.frame((byte)bgfx.FrameFlags.None);
        }
    }

    private void ResetPreviousViews()
    {
        for (int viewIndex = 0; viewIndex < m_nextViewId; viewIndex++)
        {
            bgfx.reset_view(checked((ushort)viewIndex));
        }

        m_nextViewId = 0;
    }

    private void ValidatePersistentHandle(PersistentTextureHandle texture)
    {
        if (!texture.isValid || texture.deviceGeneration != generation)
        {
            throw new ArgumentException("Texture handle belongs to another device generation.", nameof(texture));
        }
    }

    private void ValidatePersistentHandle(RenderSurfaceHandle surface)
    {
        if (!surface.isValid || surface.deviceGeneration != generation)
        {
            throw new ArgumentException("Presentation surface handle belongs to another device generation.", nameof(surface));
        }
    }

    private void EnsureFrameSafetyPoint()
    {
        EnsureApiThread();
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (!m_frameOpen || m_activeGraph is not null || m_activeEncoder is not null)
        {
            throw new InvalidOperationException("Operation requires an open frame before graph execution.");
        }
    }

    private void EnsureApiThread()
    {
        if (Environment.CurrentManagedThreadId != m_apiThreadId)
        {
            throw new InvalidOperationException("BGFX API operations must run on the device API thread.");
        }
    }

    private static void ApplyPlatformData(ref bgfx.Init init, PlatformWindow? window)
    {
        if (window is null)
        {
            return;
        }

        PlatformNativeHandles handles = window.nativeHandles;
        if (handles.handleKind is not (PlatformNativeHandleKind.Win32 or PlatformNativeHandleKind.Cocoa))
        {
            throw new PlatformNotSupportedException(
                $"BGFX window surfaces do not support native handle kind '{handles.handleKind}'.");
        }

        init.platformData.nwh = handles.windowHandle.ToPointer();
        init.platformData.ndt = handles.displayHandle.ToPointer();
    }

    private static uint PackColor(RenderClearColor color)
    {
        byte r = (byte)(Math.Clamp(color.r, 0f, 1f) * 255f);
        byte g = (byte)(Math.Clamp(color.g, 0f, 1f) * 255f);
        byte b = (byte)(Math.Clamp(color.b, 0f, 1f) * 255f);
        byte a = (byte)(Math.Clamp(color.a, 0f, 1f) * 255f);
        return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
    }

    private static int Utf8Length(string value)
        => Encoding.UTF8.GetByteCount(value);

    private static int BytesPerPixel(RenderTextureFormat format)
        => format switch
        {
            RenderTextureFormat.R8 => 1,
            RenderTextureFormat.RG8 => 2,
            RenderTextureFormat.RGBA8 or RenderTextureFormat.RGBA8Srgb
                or RenderTextureFormat.RGB10A2 or RenderTextureFormat.RG11B10Float
                or RenderTextureFormat.R32Float or RenderTextureFormat.Depth24Stencil8
                or RenderTextureFormat.Depth32Float => 4,
            RenderTextureFormat.RGBA16Float => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private static bgfx.ClearFlags ColorDiscardFlag(int slot)
        => slot switch
        {
            0 => bgfx.ClearFlags.DiscardColor0,
            1 => bgfx.ClearFlags.DiscardColor1,
            2 => bgfx.ClearFlags.DiscardColor2,
            3 => bgfx.ClearFlags.DiscardColor3,
            4 => bgfx.ClearFlags.DiscardColor4,
            5 => bgfx.ClearFlags.DiscardColor5,
            6 => bgfx.ClearFlags.DiscardColor6,
            7 => bgfx.ClearFlags.DiscardColor7,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };

    private enum DeferredResourceKind
    {
        Texture,
        FrameBuffer,
        VertexBuffer,
        IndexBuffer,
        DynamicVertexBuffer,
        DynamicIndexBuffer,
        IndirectBuffer,
        Program,
        VertexLayout
    }

    private readonly record struct DeferredResource(
        DeferredResourceKind kind,
        ushort index,
        uint eligibleFrame)
    {
        public static DeferredResource ForTexture(bgfx.TextureHandle texture)
            => new(DeferredResourceKind.Texture, texture.idx, 0);

        public static DeferredResource ForFrameBuffer(bgfx.FrameBufferHandle frameBuffer)
            => new(DeferredResourceKind.FrameBuffer, frameBuffer.idx, 0);

        public static DeferredResource ForBuffer(BgfxBufferResource buffer)
            => new(buffer.kind switch
            {
                BgfxBufferKind.Vertex => DeferredResourceKind.VertexBuffer,
                BgfxBufferKind.Index => DeferredResourceKind.IndexBuffer,
                BgfxBufferKind.DynamicVertex => DeferredResourceKind.DynamicVertexBuffer,
                BgfxBufferKind.DynamicIndex => DeferredResourceKind.DynamicIndexBuffer,
                BgfxBufferKind.Indirect => DeferredResourceKind.IndirectBuffer,
                _ => throw new ArgumentOutOfRangeException(nameof(buffer))
            }, buffer.nativeIndex, 0);

        public static DeferredResource ForProgram(bgfx.ProgramHandle program)
            => new(DeferredResourceKind.Program, program.idx, 0);

        public static DeferredResource ForVertexLayout(bgfx.VertexLayoutHandle layout)
            => new(DeferredResourceKind.VertexLayout, layout.idx, 0);
    }
}
