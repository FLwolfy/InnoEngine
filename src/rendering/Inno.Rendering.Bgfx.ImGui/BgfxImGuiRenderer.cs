using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Inno.Native.ImGui;
using Inno.Platform.Sdl3.ImGui;
using Inno.Rendering.Bgfx;
using Inno.Rendering;

namespace Inno.Rendering.Bgfx.ImGui;

/// <summary>
/// Copies Dear ImGui command lists into frame-owned data and composites them through BGFX RenderGraph passes.
/// </summary>
public sealed unsafe class BgfxImGuiRenderer : IPlatformImGuiRenderer, IRenderFrameGraphContributor
{
    private const int C_VERTEX_STRIDE = 20;
    private const int C_INDEX_STRIDE = 2;
    private const int C_INITIAL_VERTEX_CAPACITY = 4096;
    private const int C_INITIAL_INDEX_CAPACITY = 8192;

    private static readonly RenderBindingId S_TEXTURE_BINDING = new("s_tex");
    private static readonly RenderPhaseId S_USER_INTERFACE_PHASE = new("inno.imgui.compose");
    private static readonly RenderClearColor S_PRESENTATION_CLEAR_COLOR = new(
        SrgbToLinear(0.08f),
        SrgbToLinear(0.08f),
        SrgbToLinear(0.09f),
        1f);
    private static readonly RenderVertexLayout S_VERTEX_LAYOUT = new(
    [
        new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float2),
        new RenderVertexAttribute(RenderVertexSemantic.TextureCoordinate0, RenderVertexFormat.Float2),
        new RenderVertexAttribute(RenderVertexSemantic.Color0, RenderVertexFormat.UInt8Normalized4)
    ]);

    private readonly object m_sync = new();
    private readonly IRenderDevice m_device;
    private readonly BgfxDevice m_bgfxDevice;
    private readonly Dictionary<uint, ViewportState> m_viewports = [];
    private readonly Dictionary<ulong, PersistentTextureHandle> m_textures = [];
    private readonly Dictionary<ulong, OwnedTexture> m_ownedTextures = [];
    private readonly Dictionary<ulong, TextureUpload> m_pendingTextureUploads = [];
    private readonly HashSet<ulong> m_pendingTextureDestruction = [];
    private IReadOnlyList<PreparedPacket> m_framePackets = [];
    private GraphicsPipelineDescriptor? m_pendingPipelineDescriptor;
    private GraphicsPipelineHandle m_pipeline;
    private PersistentBufferHandle m_vertexBuffer;
    private PersistentBufferHandle m_indexBuffer;
    private DrawPacket? m_mainPacket;
    private ulong m_nextTextureToken = 1;
    private int m_vertexCapacity;
    private int m_indexCapacity;
    private int m_mainWidth;
    private int m_mainHeight;
    private bool m_mainResizePending;
    private bool m_disposeRequested;
    private bool m_released;

    /// <summary>
    /// Creates a frame-contributing ImGui renderer around a backend-neutral shader artifact.
    /// </summary>
    /// <param name="device">
    /// Active BGFX-backed neutral device.
    /// </param>
    /// <param name="pipelineDescriptor">
    /// Compiled ImGui shader stages and reflected interface.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the shader artifact does not use the required ImGui layout.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when <paramref name="device"/> is not BGFX-backed.
    /// </exception>
    public BgfxImGuiRenderer(IRenderDevice device, GraphicsPipelineDescriptor pipelineDescriptor)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pipelineDescriptor);
        ValidatePipelineDescriptor(pipelineDescriptor);
        m_device = device;
        m_bgfxDevice = device as BgfxDevice
            ?? throw new NotSupportedException("The BGFX ImGui renderer requires an Inno.Rendering.Bgfx device.");
        m_pendingPipelineDescriptor = pipelineDescriptor;
    }

    /// <summary>
    /// Gets the exact interleaved vertex layout required by the built-in ImGui shaders.
    /// </summary>
    public static RenderVertexLayout vertexLayout => S_VERTEX_LAYOUT;

    /// <summary>
    /// Gets the last recoverable shader replacement error while the last-good pipeline remains active.
    /// </summary>
    public string? lastShaderError { get; private set; }

    /// <summary>
    /// Gets whether supports viewports is enabled for this implementation.
    /// </summary>
    public bool supportsViewports => true;

    /// <summary>
    /// Queues a compiled shader replacement that commits atomically at a frame safety point.
    /// </summary>
    /// <param name="pipelineDescriptor">
    /// Complete candidate artifact.
    /// </param>
    public void ReplaceShaderArtifact(GraphicsPipelineDescriptor pipelineDescriptor)
    {
        ArgumentNullException.ThrowIfNull(pipelineDescriptor);
        ValidatePipelineDescriptor(pipelineDescriptor);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposeRequested, this);
            m_pendingPipelineDescriptor = pipelineDescriptor;
        }
    }

    /// <summary>
    /// Registers a persistent render texture as an opaque ImGui texture token.
    /// </summary>
    /// <param name="texture">
    /// Texture owned by the active device generation.
    /// </param>
    /// <returns>
    /// An opaque token accepted by <see cref="PlatformImGuiContext.DrawImage"/>.
    /// </returns>
    public ImGuiTextureHandle RegisterTexture(PersistentTextureHandle texture)
    {
        if (!texture.isValid)
        {
            throw new ArgumentException("Persistent texture handle is invalid.", nameof(texture));
        }

        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposeRequested, this);
            ulong token = AllocateTextureToken();
            m_textures.Add(token, texture);
            return new ImGuiTextureHandle(token);
        }
    }

    /// <summary>
    /// Unregisters an opaque texture token without taking ownership of the source texture.
    /// </summary>
    /// <param name="texture">
    /// Previously allocated token.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the token was an external registration.
    /// </returns>
    public bool UnregisterTexture(ImGuiTextureHandle texture)
    {
        if (!texture.isValid)
        {
            return false;
        }

        lock (m_sync)
        {
            return !m_ownedTextures.ContainsKey(texture.value) && m_textures.Remove(texture.value);
        }
    }

    /// <summary>
    /// Records ImGui draw data for the main application viewport.
    /// </summary>
    /// <param name="drawData">
    /// The draw data consumed by render main; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void RenderMain(IntPtr drawData)
    {
        lock (m_sync)
        {
            if (m_disposeRequested)
            {
                return;
            }

            m_mainPacket = CaptureDrawData(drawData, 0, default);
            if (m_mainPacket is not null
                && (m_mainPacket.pixelWidth != m_mainWidth || m_mainPacket.pixelHeight != m_mainHeight))
            {
                m_mainWidth = m_mainPacket.pixelWidth;
                m_mainHeight = m_mainPacket.pixelHeight;
                m_mainResizePending = true;
            }
        }
    }

    /// <summary>
    /// Synchronizes the main render output with the current drawable dimensions.
    /// </summary>
    /// <param name="pixelWidth">
    /// The pixel width consumed by synchronize main output; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="pixelHeight">
    /// The pixel height consumed by synchronize main output; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void SynchronizeMainOutput(int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        lock (m_sync)
        {
            m_mainWidth = pixelWidth;
            m_mainHeight = pixelHeight;
            m_mainResizePending = true;
        }
    }

    /// <summary>
    /// Creates a viewport using this implementation's validated inputs.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    public void CreateViewport(PlatformImGuiViewportTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (m_sync)
        {
            ObjectDisposedException.ThrowIf(m_disposeRequested, this);
            if (!m_viewports.TryAdd(target.viewportId, new ViewportState(target)))
            {
                throw new ArgumentException($"Viewport {target.viewportId} is already registered.", nameof(target));
            }
        }
    }

    /// <summary>
    /// Resizes an auxiliary viewport surface to its current platform dimensions.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    public void ResizeViewport(PlatformImGuiViewportTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (m_sync)
        {
            if (m_viewports.TryGetValue(target.viewportId, out ViewportState? state))
            {
                state.target = target;
                state.resizePending = true;
            }
        }
    }

    /// <summary>
    /// Records ImGui draw data for the supplied auxiliary viewport.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    /// <param name="drawData">
    /// The draw data consumed by render viewport; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public void RenderViewport(PlatformImGuiViewportTarget target, IntPtr drawData)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (m_sync)
        {
            if (m_disposeRequested || !m_viewports.TryGetValue(target.viewportId, out ViewportState? state))
            {
                return;
            }

            state.target = target;
            state.packet = CaptureDrawData(drawData, target.viewportId, state.surface);
        }
    }

    /// <summary>
    /// Presents the completed frame for the supplied auxiliary viewport.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    public void PresentViewport(PlatformImGuiViewportTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
    }

    /// <summary>
    /// Destroys the auxiliary viewport and releases its rendering resources.
    /// </summary>
    /// <param name="target">
    /// The existing target that receives the validated result.
    /// </param>
    public void DestroyViewport(PlatformImGuiViewportTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        RenderSurfaceHandle surface = default;
        lock (m_sync)
        {
            if (m_viewports.Remove(target.viewportId, out ViewportState? state))
            {
                surface = state.surface;
            }
        }

        if (surface.isValid)
        {
            m_bgfxDevice.DestroyWindowSurface(surface);
        }
    }

    /// <summary>
    /// Prepares frame-owned resources before render graph recording begins.
    /// </summary>
    /// <param name="frameIndex">
    /// The monotonic frame identity associated with this operation.
    /// </param>
    public void PrepareFrame(ulong frameIndex)
    {
        _ = frameIndex;
        lock (m_sync)
        {
            if (m_released)
            {
                return;
            }

            if (m_disposeRequested)
            {
                ReleaseDeviceResources();
                m_released = true;
                return;
            }

            PreparePipeline();
            PrepareSurfaces();
            PrepareTextures();
            if (m_mainResizePending && m_mainWidth > 0 && m_mainHeight > 0)
            {
                m_device.ResizeBackbuffer(m_mainWidth, m_mainHeight);
                m_mainResizePending = false;
            }

            PrepareDrawPackets();
        }
    }

    /// <summary>
    /// Adds the renderer's frame passes and resource declarations to the render graph.
    /// </summary>
    /// <param name="graph">
    /// The graph consumed by add render passes; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="frameIndex">
    /// The monotonic frame identity associated with this operation.
    /// </param>
    public void AddRenderPasses(RenderGraphBuilder graph, ulong frameIndex)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _ = frameIndex;
        lock (m_sync)
        {
            if (m_released || !m_pipeline.isValid || !m_vertexBuffer.isValid || !m_indexBuffer.isValid)
            {
                return;
            }

            foreach (PreparedPacket packet in m_framePackets)
            {
                RasterPassBuilder pass = graph.AddRasterPass(
                    packet.viewportId == 0 ? "ImGui/Main" : $"ImGui/Viewport/{packet.viewportId}",
                    S_USER_INTERFACE_PHASE,
                    packet,
                    ExecutePacket);
                pass.SetViewTransform(Identity(), Orthographic(packet.displayPosition, packet.displaySize))
                    .ClearPresentationTarget(S_PRESENTATION_CLEAR_COLOR)
                    .HasSideEffect();
                if (packet.surface.isValid)
                {
                    pass.UseSurface(packet.surface);
                }
            }
        }
    }

    /// <summary>
    /// Marks renderer-owned GPU resources for release at the next frame safety point or device teardown.
    /// </summary>
    public void Dispose()
    {
        RenderSurfaceHandle[] surfaces;
        lock (m_sync)
        {
            if (m_disposeRequested)
            {
                return;
            }

            m_disposeRequested = true;
            surfaces = m_viewports.Values
                .Select(static value => value.surface)
                .Where(static value => value.isValid)
                .ToArray();
            m_viewports.Clear();
            m_mainPacket = null;
            m_framePackets = [];
        }

        foreach (RenderSurfaceHandle surface in surfaces)
        {
            m_bgfxDevice.DestroyWindowSurface(surface);
        }
    }

    private void PreparePipeline()
    {
        if (m_pendingPipelineDescriptor is null)
        {
            return;
        }

        try
        {
            GraphicsPipelineHandle candidate = m_device.CreateGraphicsPipeline(
                m_pendingPipelineDescriptor,
                "ImGui Pipeline");
            GraphicsPipelineHandle previous = m_pipeline;
            m_pipeline = candidate;
            m_pendingPipelineDescriptor = null;
            lastShaderError = null;
            if (previous.isValid)
            {
                m_device.DestroyGraphicsPipeline(previous);
            }
        }
        catch (Exception exception) when (m_pipeline.isValid)
        {
            lastShaderError = exception.Message;
            m_pendingPipelineDescriptor = null;
        }
    }

    private void PrepareSurfaces()
    {
        foreach (ViewportState state in m_viewports.Values)
        {
            if (!state.surface.isValid)
            {
                state.surface = m_bgfxDevice.CreateWindowSurface(
                    state.target.nativeHandles,
                    state.target.width,
                    state.target.height,
                    $"ImGui Viewport {state.target.viewportId}");
                state.resizePending = false;
            }
            else if (state.resizePending)
            {
                m_bgfxDevice.ResizeWindowSurface(state.surface, state.target.width, state.target.height);
                state.resizePending = false;
            }

            if (state.packet is not null)
            {
                state.packet.surface = state.surface;
            }
        }
    }

    private void PrepareTextures()
    {
        foreach (ulong token in m_pendingTextureDestruction)
        {
            if (m_ownedTextures.Remove(token, out OwnedTexture? owned))
            {
                m_textures.Remove(token);
                m_device.DestroyTexture(owned.handle);
            }
        }

        m_pendingTextureDestruction.Clear();
        foreach ((ulong token, TextureUpload upload) in m_pendingTextureUploads)
        {
            PersistentTextureHandle texture;
            if (m_ownedTextures.TryGetValue(token, out OwnedTexture? current)
                && current.width == upload.width
                && current.height == upload.height)
            {
                texture = current.handle;
            }
            else
            {
                texture = m_device.CreateTexture(
                    new RenderTextureDescriptor(
                        upload.width,
                        upload.height,
                        RenderTextureFormat.RGBA8,
                        RenderTextureUsage.Sampled),
                    $"ImGui Texture {token}");
                if (current is not null)
                {
                    m_device.DestroyTexture(current.handle);
                }

                m_ownedTextures[token] = new OwnedTexture(texture, upload.width, upload.height);
                m_textures[token] = texture;
            }

            m_device.UpdateTexture(texture, upload.pixels);
        }

        m_pendingTextureUploads.Clear();
    }

    private void PrepareDrawPackets()
    {
        List<DrawPacket> packets = [];
        if (m_mainPacket is not null)
        {
            packets.Add(m_mainPacket);
            m_mainPacket = null;
        }

        foreach (ViewportState state in m_viewports.Values.OrderBy(static value => value.target.viewportId))
        {
            if (state.packet is not null)
            {
                packets.Add(state.packet);
                state.packet = null;
            }
        }

        int vertexCount = packets.Sum(static value => value.vertices.Length / C_VERTEX_STRIDE);
        int indexCount = packets.Sum(static value => value.indices.Length / C_INDEX_STRIDE);
        if (vertexCount == 0 || indexCount == 0)
        {
            m_framePackets = [];
            return;
        }

        EnsureDynamicBuffers(vertexCount, indexCount);
        byte[] vertices = new byte[checked(vertexCount * C_VERTEX_STRIDE)];
        byte[] indices = new byte[checked(indexCount * C_INDEX_STRIDE)];
        List<PreparedPacket> prepared = [];
        int vertexBase = 0;
        int indexBase = 0;
        foreach (DrawPacket packet in packets)
        {
            packet.vertices.CopyTo(vertices, vertexBase * C_VERTEX_STRIDE);
            packet.indices.CopyTo(indices, indexBase * C_INDEX_STRIDE);
            List<PreparedDrawCommand> commands = [];
            foreach (CapturedDrawCommand command in packet.commands)
            {
                if (m_textures.TryGetValue(command.textureToken, out PersistentTextureHandle texture))
                {
                    commands.Add(new PreparedDrawCommand(
                        command.clipX,
                        command.clipY,
                        command.clipWidth,
                        command.clipHeight,
                        vertexBase + command.firstVertex,
                        indexBase + command.firstIndex,
                        command.indexCount,
                        texture));
                }
            }

            prepared.Add(new PreparedPacket(
                packet.viewportId,
                packet.surface,
                packet.displayPosition,
                packet.displaySize,
                commands,
                m_pipeline,
                m_vertexBuffer,
                m_indexBuffer));
            vertexBase += packet.vertices.Length / C_VERTEX_STRIDE;
            indexBase += packet.indices.Length / C_INDEX_STRIDE;
        }

        m_device.UpdateBuffer(m_vertexBuffer, vertices);
        m_device.UpdateBuffer(m_indexBuffer, indices);
        m_framePackets = prepared;
    }

    private void EnsureDynamicBuffers(int vertexCount, int indexCount)
    {
        if (vertexCount > m_vertexCapacity)
        {
            int capacity = GrowCapacity(vertexCount, C_INITIAL_VERTEX_CAPACITY);
            PersistentBufferHandle replacement = m_device.CreateBuffer(
                new PersistentBufferDescriptor(
                    new RenderBufferDescriptor(
                        capacity,
                        C_VERTEX_STRIDE,
                        RenderBufferUsage.Vertex | RenderBufferUsage.Dynamic),
                    S_VERTEX_LAYOUT),
                ReadOnlySpan<byte>.Empty,
                "ImGui Vertices");
            if (m_vertexBuffer.isValid)
            {
                m_device.DestroyBuffer(m_vertexBuffer);
            }

            m_vertexBuffer = replacement;
            m_vertexCapacity = capacity;
        }

        if (indexCount > m_indexCapacity)
        {
            int capacity = GrowCapacity(indexCount, C_INITIAL_INDEX_CAPACITY);
            PersistentBufferHandle replacement = m_device.CreateBuffer(
                new PersistentBufferDescriptor(
                    new RenderBufferDescriptor(
                        capacity,
                        C_INDEX_STRIDE,
                        RenderBufferUsage.Index | RenderBufferUsage.Dynamic),
                    indexFormat: RenderIndexFormat.UInt16),
                ReadOnlySpan<byte>.Empty,
                "ImGui Indices");
            if (m_indexBuffer.isValid)
            {
                m_device.DestroyBuffer(m_indexBuffer);
            }

            m_indexBuffer = replacement;
            m_indexCapacity = capacity;
        }
    }

    private DrawPacket? CaptureDrawData(IntPtr address, uint viewportId, RenderSurfaceHandle surface)
    {
        if (address == IntPtr.Zero)
        {
            return null;
        }

        var drawData = new ImDrawDataPtr((ImDrawData*)address);
        if (drawData.IsNull || !drawData.Valid || drawData.TotalVtxCount <= 0 || drawData.TotalIdxCount <= 0)
        {
            return null;
        }

        ProcessTextureRequests(drawData);
        byte[] vertices = new byte[checked(drawData.TotalVtxCount * C_VERTEX_STRIDE)];
        byte[] indices = new byte[checked(drawData.TotalIdxCount * C_INDEX_STRIDE)];
        List<CapturedDrawCommand> commands = [];
        int vertexBase = 0;
        int indexBase = 0;
        Vector2 clipOffset = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;
        int pixelWidth = Math.Max(1, (int)MathF.Round(drawData.DisplaySize.X * clipScale.X));
        int pixelHeight = Math.Max(1, (int)MathF.Round(drawData.DisplaySize.Y * clipScale.Y));
        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            ImDrawListPtr drawList = drawData.CmdLists[listIndex];
            int listVertexCount = drawList.VtxBuffer.Size;
            int listIndexCount = drawList.IdxBuffer.Size;
            new ReadOnlySpan<byte>(drawList.VtxBuffer.Data, checked(listVertexCount * C_VERTEX_STRIDE))
                .CopyTo(vertices.AsSpan(vertexBase * C_VERTEX_STRIDE));
            new ReadOnlySpan<byte>(drawList.IdxBuffer.Data, checked(listIndexCount * C_INDEX_STRIDE))
                .CopyTo(indices.AsSpan(indexBase * C_INDEX_STRIDE));

            for (int commandIndex = 0; commandIndex < drawList.CmdBuffer.Size; commandIndex++)
            {
                ImDrawCmd command = drawList.CmdBuffer[commandIndex];
                if (command.UserCallback != null || command.ElemCount == 0)
                {
                    continue;
                }

                Vector4 clip = command.ClipRect;
                int clipX = Math.Clamp((int)MathF.Floor((clip.X - clipOffset.X) * clipScale.X), 0, pixelWidth);
                int clipY = Math.Clamp((int)MathF.Floor((clip.Y - clipOffset.Y) * clipScale.Y), 0, pixelHeight);
                int clipRight = Math.Clamp((int)MathF.Ceiling((clip.Z - clipOffset.X) * clipScale.X), 0, pixelWidth);
                int clipBottom = Math.Clamp((int)MathF.Ceiling((clip.W - clipOffset.Y) * clipScale.Y), 0, pixelHeight);
                if (clipRight <= clipX || clipBottom <= clipY)
                {
                    continue;
                }

                commands.Add(new CapturedDrawCommand(
                    clipX,
                    clipY,
                    clipRight - clipX,
                    clipBottom - clipY,
                    checked(vertexBase + (int)command.VtxOffset),
                    checked(indexBase + (int)command.IdxOffset),
                    checked((int)command.ElemCount),
                    command.GetTexID().Handle));
            }

            vertexBase += listVertexCount;
            indexBase += listIndexCount;
        }

        return new DrawPacket(
            viewportId,
            surface,
            drawData.DisplayPos,
            drawData.DisplaySize,
            pixelWidth,
            pixelHeight,
            vertices,
            indices,
            commands);
    }

    private void ProcessTextureRequests(ImDrawDataPtr drawData)
    {
        ImVector<ImTextureDataPtr>* textures = drawData.Handle->Textures;
        if (textures is null)
        {
            return;
        }

        for (int index = 0; index < textures->Size; index++)
        {
            ImTextureDataPtr textureData = textures->Data[index];
            if (textureData.IsNull)
            {
                continue;
            }

            if (textureData.Status == ImTextureStatus.WantDestroy)
            {
                ulong destroyedToken = textureData.GetTexID().Handle;
                if (destroyedToken != 0)
                {
                    m_pendingTextureUploads.Remove(destroyedToken);
                    m_pendingTextureDestruction.Add(destroyedToken);
                }

                textureData.SetTexID(ImTextureID.Null);
                textureData.SetStatus(ImTextureStatus.Destroyed);
                continue;
            }

            if (textureData.Status is not (ImTextureStatus.WantCreate or ImTextureStatus.WantUpdates)
                || textureData.Pixels is null
                || textureData.Width <= 0
                || textureData.Height <= 0)
            {
                continue;
            }

            ulong token = textureData.GetTexID().Handle;
            if (token == 0)
            {
                token = AllocateTextureToken();
                textureData.SetTexID(new ImTextureID(token));
            }

            m_pendingTextureUploads[token] = new TextureUpload(
                textureData.Width,
                textureData.Height,
                CopyTexturePixels(textureData));
            textureData.SetStatus(ImTextureStatus.Ok);
        }
    }

    private static byte[] CopyTexturePixels(ImTextureDataPtr textureData)
    {
        int sourcePitch = textureData.GetPitch();
        if (textureData.Format == ImTextureFormat.Rgba32)
        {
            int rowBytes = checked(textureData.Width * 4);
            byte[] result = new byte[checked(rowBytes * textureData.Height)];
            for (int y = 0; y < textureData.Height; y++)
            {
                new ReadOnlySpan<byte>(textureData.Pixels + (y * sourcePitch), rowBytes)
                    .CopyTo(result.AsSpan(y * rowBytes, rowBytes));
            }

            return result;
        }

        byte[] expanded = new byte[checked(textureData.Width * textureData.Height * 4)];
        for (int y = 0; y < textureData.Height; y++)
        {
            byte* source = textureData.Pixels + (y * sourcePitch);
            for (int x = 0; x < textureData.Width; x++)
            {
                int destination = ((y * textureData.Width) + x) * 4;
                expanded[destination] = 255;
                expanded[destination + 1] = 255;
                expanded[destination + 2] = 255;
                expanded[destination + 3] = source[x];
            }
        }

        return expanded;
    }

    private static void ExecutePacket(PreparedPacket packet, RenderPassContext context)
    {
        context.commands.BindGraphicsPipeline(packet.pipeline);
        foreach (PreparedDrawCommand command in packet.commands)
        {
            context.commands.SetScissor(command.clipX, command.clipY, command.clipWidth, command.clipHeight);
            context.commands.BindVertexBuffer(packet.vertexBuffer, command.firstVertex);
            context.commands.BindIndexBuffer(packet.indexBuffer, command.firstIndex);
            context.commands.BindTexture(S_TEXTURE_BINDING, command.texture);
            context.commands.DrawIndexed(command.indexCount);
        }
    }

    private void ReleaseDeviceResources()
    {
        foreach (OwnedTexture texture in m_ownedTextures.Values)
        {
            m_device.DestroyTexture(texture.handle);
        }

        if (m_vertexBuffer.isValid)
        {
            m_device.DestroyBuffer(m_vertexBuffer);
        }

        if (m_indexBuffer.isValid)
        {
            m_device.DestroyBuffer(m_indexBuffer);
        }

        if (m_pipeline.isValid)
        {
            m_device.DestroyGraphicsPipeline(m_pipeline);
        }

        m_ownedTextures.Clear();
        m_textures.Clear();
        m_vertexBuffer = default;
        m_indexBuffer = default;
        m_pipeline = default;
    }

    private ulong AllocateTextureToken()
    {
        if (m_nextTextureToken == 0)
        {
            throw new InvalidOperationException("ImGui texture token space was exhausted.");
        }

        return m_nextTextureToken++;
    }

    private static void ValidatePipelineDescriptor(GraphicsPipelineDescriptor descriptor)
    {
        if (descriptor.vertexLayout is null || !descriptor.vertexLayout.Equals(S_VERTEX_LAYOUT))
        {
            throw new ArgumentException("ImGui pipeline vertex layout must be Position2, UV2, ColorRGBA8.", nameof(descriptor));
        }

        RenderShaderBindingDescriptor? texture = descriptor.bindings.SingleOrDefault(
            static value => value.id == S_TEXTURE_BINDING);
        if (texture is null || texture.kind != RenderShaderBindingKind.Texture || texture.slot != 0)
        {
            throw new ArgumentException("ImGui pipeline must declare texture binding 's_tex' at slot zero.", nameof(descriptor));
        }
    }

    private static int GrowCapacity(int required, int minimum)
    {
        int capacity = minimum;
        while (capacity < required)
        {
            capacity = checked(capacity * 2);
        }

        return capacity;
    }

    private static float SrgbToLinear(float value)
        => value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float[] Identity()
        =>
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f
        ];

    private static float[] Orthographic(Vector2 position, Vector2 size)
    {
        float left = position.X;
        float right = position.X + size.X;
        float top = position.Y;
        float bottom = position.Y + size.Y;
        return
        [
            2f / (right - left), 0f, 0f, 0f,
            0f, 2f / (top - bottom), 0f, 0f,
            0f, 0f, 1f, 0f,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0f, 1f
        ];
    }

    private sealed class ViewportState
    {
        /// <summary>
        /// Creates a validated viewport state instance.
        /// </summary>
        /// <param name="target">
        /// The existing target that receives the validated result.
        /// </param>
        public ViewportState(PlatformImGuiViewportTarget target) => this.target = target;
        /// <summary>
        /// Gets the platform viewport whose render resources are tracked by this state.
        /// </summary>
        public PlatformImGuiViewportTarget target { get; set; }
        /// <summary>
        /// Gets the presentation surface targeted by this render pass.
        /// </summary>
        public RenderSurfaceHandle surface { get; set; }
        /// <summary>
        /// Gets the most recently prepared immutable draw packet, or null before preparation.
        /// </summary>
        public DrawPacket? packet { get; set; }
        /// <summary>
        /// Gets whether the caller-visible condition represented by this property is satisfied.
        /// </summary>
        public bool resizePending { get; set; }
    }

    private sealed record OwnedTexture(PersistentTextureHandle handle, int width, int height);
    private sealed record TextureUpload(int width, int height, byte[] pixels);
    private sealed record CapturedDrawCommand(
        int clipX,
        int clipY,
        int clipWidth,
        int clipHeight,
        int firstVertex,
        int firstIndex,
        int indexCount,
        ulong textureToken);

    private sealed record PreparedDrawCommand(
        int clipX,
        int clipY,
        int clipWidth,
        int clipHeight,
        int firstVertex,
        int firstIndex,
        int indexCount,
        PersistentTextureHandle texture);

    private sealed record DrawPacket(
        uint viewportId,
        RenderSurfaceHandle initialSurface,
        Vector2 displayPosition,
        Vector2 displaySize,
        int pixelWidth,
        int pixelHeight,
        byte[] vertices,
        byte[] indices,
        IReadOnlyList<CapturedDrawCommand> commands)
    {
        /// <summary>
        /// Gets the presentation surface targeted by this render pass.
        /// </summary>
        public RenderSurfaceHandle surface { get; set; } = initialSurface;
    }

    private sealed record PreparedPacket(
        uint viewportId,
        RenderSurfaceHandle surface,
        Vector2 displayPosition,
        Vector2 displaySize,
        IReadOnlyList<PreparedDrawCommand> commands,
        GraphicsPipelineHandle pipeline,
        PersistentBufferHandle vertexBuffer,
        PersistentBufferHandle indexBuffer);
}
