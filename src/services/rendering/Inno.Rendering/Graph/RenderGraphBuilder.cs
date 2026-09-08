using System;
using System.Collections.Generic;

using Inno.Scripting.Api;

namespace Inno.Rendering;

/// <summary>
/// Builds one generation-scoped render graph from explicit passes and resources.
/// </summary>
public sealed class RenderGraphBuilder
{
    private readonly GraphicsCapabilities m_capabilities;
    private readonly uint m_generation;
    private readonly List<RenderTextureRecord> m_textures = [];
    private readonly List<RenderBufferRecord> m_buffers = [];
    private readonly List<RenderPassRecord> m_passes = [];
    private readonly HashSet<RenderResourceKey> m_outputs = [];
    private readonly List<string> m_nameScopes = [];
    private bool m_compiled;

    /// <summary>
    /// Creates a render graph builder for one frame generation.
    /// </summary>
    /// <param name="generation">
    /// Non-zero frame-scoped generation.
    /// </param>
    /// <param name="capabilities">
    /// Device capabilities used during validation.
    /// </param>
    public RenderGraphBuilder(uint generation, GraphicsCapabilities capabilities)
    {
        if (generation == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Generation must be non-zero.");
        }

        ArgumentNullException.ThrowIfNull(capabilities);
        m_generation = generation;
        m_capabilities = capabilities;
    }

    /// <summary>
    /// Creates a transient texture eligible for lifetime aliasing.
    /// </summary>
    /// <param name="name">
    /// Debug and diagnostic name.
    /// </param>
    /// <param name="descriptor">
    /// Texture requirements.
    /// </param>
    /// <returns>
    /// A handle valid only for this graph generation.
    /// </returns>
    public RenderTextureHandle CreateTexture(string name, RenderTextureDescriptor descriptor)
    {
        EnsureBuilding();
        ValidateNameAndValue(name, descriptor);
        RenderTextureHandle handle = new(m_textures.Count, m_generation);
        m_textures.Add(new RenderTextureRecord(QualifyName(name), descriptor, false, default));
        return handle;
    }

    /// <summary>
    /// Imports a persistent device texture into the current graph.
    /// </summary>
    /// <param name="name">
    /// Debug and diagnostic name.
    /// </param>
    /// <param name="texture">
    /// Persistent texture owned by the active device generation.
    /// </param>
    /// <param name="descriptor">
    /// Texture requirements and usage.
    /// </param>
    /// <returns>
    /// A frame-scoped handle for graph declarations.
    /// </returns>
    public RenderTextureHandle ImportTexture(
        string name,
        PersistentTextureHandle texture,
        RenderTextureDescriptor descriptor)
    {
        EnsureBuilding();
        ValidateNameAndValue(name, descriptor);
        if (!texture.isValid)
        {
            throw new ArgumentException("Persistent texture handle is invalid.", nameof(texture));
        }

        RenderTextureHandle handle = new(m_textures.Count, m_generation);
        m_textures.Add(new RenderTextureRecord(QualifyName(name), descriptor, true, texture));
        return handle;
    }

    /// <summary>
    /// Creates a transient buffer eligible for lifetime aliasing.
    /// </summary>
    /// <param name="name">
    /// Debug and diagnostic name.
    /// </param>
    /// <param name="descriptor">
    /// Buffer requirements.
    /// </param>
    /// <returns>
    /// A handle valid only for this graph generation.
    /// </returns>
    public RenderBufferHandle CreateBuffer(string name, RenderBufferDescriptor descriptor)
    {
        EnsureBuilding();
        ValidateNameAndValue(name, descriptor);
        RenderBufferHandle handle = new(m_buffers.Count, m_generation);
        m_buffers.Add(new RenderBufferRecord(QualifyName(name), descriptor, false, default));
        return handle;
    }

    /// <summary>
    /// Imports a persistent device buffer into the current graph.
    /// </summary>
    /// <param name="name">
    /// Debug and diagnostic name.
    /// </param>
    /// <param name="buffer">
    /// Persistent buffer owned by the active device generation.
    /// </param>
    /// <param name="descriptor">
    /// Buffer requirements and usage.
    /// </param>
    /// <returns>
    /// A frame-scoped handle for graph declarations.
    /// </returns>
    public RenderBufferHandle ImportBuffer(
        string name,
        PersistentBufferHandle buffer,
        RenderBufferDescriptor descriptor)
    {
        EnsureBuilding();
        ValidateNameAndValue(name, descriptor);
        if (!buffer.isValid)
        {
            throw new ArgumentException("Persistent buffer handle is invalid.", nameof(buffer));
        }

        RenderBufferHandle handle = new(m_buffers.Count, m_generation);
        m_buffers.Add(new RenderBufferRecord(QualifyName(name), descriptor, true, buffer));
        return handle;
    }

    /// <summary>
    /// Marks a texture as a graph output that keeps its producers alive.
    /// </summary>
    /// <param name="texture">
    /// Output texture.
    /// </param>
    public void MarkOutput(RenderTextureHandle texture)
    {
        EnsureTexture(texture);
        m_outputs.Add(new RenderResourceKey(true, texture.index));
    }

    /// <summary>
    /// Marks a buffer as a graph output that keeps its producers alive.
    /// </summary>
    /// <param name="buffer">
    /// Output buffer.
    /// </param>
    public void MarkOutput(RenderBufferHandle buffer)
    {
        EnsureBuffer(buffer);
        m_outputs.Add(new RenderResourceKey(false, buffer.index));
    }

    /// <summary>
    /// Begins an isolated graph mutation that rolls back every added pass and resource unless committed.
    /// </summary>
    /// <returns>
    /// A mutation scope used by reloadable features and other failure-isolated producers.
    /// </returns>
    public RenderGraphMutationScope BeginMutationScope()
    {
        EnsureBuilding();
        return new RenderGraphMutationScope(
            this,
            m_textures.Count,
            m_buffers.Count,
            m_passes.Count,
            new HashSet<RenderResourceKey>(m_outputs));
    }

    /// <summary>
    /// Prefixes pass and resource diagnostic names until the returned scope is disposed.
    /// </summary>
    /// <param name="name">
    /// Non-empty scope segment.
    /// </param>
    /// <returns>
    /// A disposable name scope that must be closed in nesting order.
    /// </returns>
    public RenderGraphNameScope BeginNameScope(string name)
    {
        EnsureBuilding();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        m_nameScopes.Add(name.Trim());
        return new RenderGraphNameScope(this, m_nameScopes.Count);
    }

    /// <summary>
    /// Adds a frame-scoped raster pass.
    /// </summary>
    /// <typeparam name="TPassData">
    /// Pass payload type.
    /// </typeparam>
    /// <param name="name">
    /// Unique diagnostic name.
    /// </param>
    /// <param name="phase">
    /// Open render phase identifier.
    /// </param>
    /// <param name="passData">
    /// Payload that lives only until this graph executes.
    /// </param>
    /// <param name="execute">
    /// Command recording callback.
    /// </param>
    /// <returns>
    /// A builder for raster resources, attachments and ordering.
    /// </returns>
    public RasterPassBuilder AddRasterPass<TPassData>(
        string name,
        RenderPhaseId phase,
        TPassData passData,
        RenderPassExecute<TPassData> execute)
        where TPassData : notnull
        => new(this, AddPass(name, phase, RenderPassKind.Raster, passData, execute));

    /// <summary>
    /// Adds a frame-scoped compute pass.
    /// </summary>
    /// <typeparam name="TPassData">
    /// Pass payload type.
    /// </typeparam>
    /// <param name="name">
    /// Unique diagnostic name.
    /// </param>
    /// <param name="phase">
    /// Open render phase identifier.
    /// </param>
    /// <param name="passData">
    /// Payload that lives only until this graph executes.
    /// </param>
    /// <param name="execute">
    /// Command recording callback.
    /// </param>
    /// <returns>
    /// A builder for compute resources and ordering.
    /// </returns>
    public ComputePassBuilder AddComputePass<TPassData>(
        string name,
        RenderPhaseId phase,
        TPassData passData,
        RenderPassExecute<TPassData> execute)
        where TPassData : notnull
        => new(this, AddPass(name, phase, RenderPassKind.Compute, passData, execute));

    /// <summary>
    /// Adds a frame-scoped resource copy pass.
    /// </summary>
    /// <typeparam name="TPassData">
    /// Pass payload type.
    /// </typeparam>
    /// <param name="name">
    /// Unique diagnostic name.
    /// </param>
    /// <param name="phase">
    /// Open render phase identifier.
    /// </param>
    /// <param name="passData">
    /// Payload that lives only until this graph executes.
    /// </param>
    /// <param name="execute">
    /// Command recording callback.
    /// </param>
    /// <returns>
    /// A builder for copy resources and ordering.
    /// </returns>
    public CopyPassBuilder AddCopyPass<TPassData>(
        string name,
        RenderPhaseId phase,
        TPassData passData,
        RenderPassExecute<TPassData> execute)
        where TPassData : notnull
        => new(this, AddPass(name, phase, RenderPassKind.Copy, passData, execute));

    /// <summary>
    /// Validates, culls and schedules this graph exactly once.
    /// </summary>
    /// <returns>
    /// A compilation result containing either an executable graph or diagnostics.
    /// </returns>
    [ScriptingApiIgnore]
    public RenderGraphCompileResult Compile()
    {
        EnsureBuilding();
        m_compiled = true;
        return RenderGraphCompiler.Compile(
            m_generation,
            m_capabilities,
            m_textures,
            m_buffers,
            m_passes,
            m_outputs);
    }

    /// <summary>
    /// Validates and previews graph compilation without consuming the builder.
    /// </summary>
    /// <returns>
    /// The compiled graph candidate and deterministic diagnostics.
    /// </returns>
    [ScriptingApiIgnore]
    public RenderGraphCompileResult Validate()
    {
        EnsureBuilding();
        return RenderGraphCompiler.Compile(
            m_generation,
            m_capabilities,
            m_textures,
            m_buffers,
            m_passes,
            m_outputs);
    }

    internal void EndNameScope(int depth)
    {
        EnsureBuilding();
        if (m_nameScopes.Count != depth)
            throw new InvalidOperationException("Render graph name scopes must be disposed in nesting order.");
        m_nameScopes.RemoveAt(m_nameScopes.Count - 1);
    }

    internal void AddUse(
        RenderPassRecord pass,
        RenderTextureHandle texture,
        RenderResourceAccess access,
        RenderResourceUseKind kind)
    {
        EnsureTexture(texture);
        pass.resources.Add(new RenderResourceUse(new RenderResourceKey(true, texture.index), access, kind));
    }

    internal void AddUse(
        RenderPassRecord pass,
        RenderBufferHandle buffer,
        RenderResourceAccess access,
        RenderResourceUseKind kind)
    {
        EnsureBuffer(buffer);
        pass.resources.Add(new RenderResourceUse(new RenderResourceKey(false, buffer.index), access, kind));
    }

    internal void AddAttachment(RenderPassRecord pass, RenderAttachment attachment)
    {
        EnsureTexture(attachment.texture);
        pass.attachments.Add(attachment);
        AddUse(
            pass,
            attachment.texture,
            attachment.loadAction == RenderLoadAction.Load
                ? RenderResourceAccess.ReadWrite
                : RenderResourceAccess.Write,
            attachment.isDepth
                ? RenderResourceUseKind.DepthStencilAttachment
                : RenderResourceUseKind.ColorAttachment);
    }

    internal void Rollback(
        int textureCount,
        int bufferCount,
        int passCount,
        IReadOnlySet<RenderResourceKey> outputs)
    {
        EnsureBuilding();
        m_textures.RemoveRange(textureCount, m_textures.Count - textureCount);
        m_buffers.RemoveRange(bufferCount, m_buffers.Count - bufferCount);
        m_passes.RemoveRange(passCount, m_passes.Count - passCount);
        m_outputs.Clear();
        m_outputs.UnionWith(outputs);
    }

    private RenderPassRecord AddPass<TPassData>(
        string name,
        RenderPhaseId phase,
        RenderPassKind kind,
        TPassData passData,
        RenderPassExecute<TPassData> execute)
        where TPassData : notnull
    {
        EnsureBuilding();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(execute);
        string qualifiedName = QualifyName(name);
        if (m_passes.Exists(pass => StringComparer.Ordinal.Equals(pass.name, qualifiedName)))
        {
            throw new ArgumentException($"Render pass '{qualifiedName}' already exists.", nameof(name));
        }

        RenderPassRecord pass = new()
        {
            name = qualifiedName,
            phase = phase,
            kind = kind,
            execute = context => execute(passData, context)
        };
        m_passes.Add(pass);
        return pass;
    }

    private void EnsureTexture(RenderTextureHandle handle)
    {
        EnsureBuilding();
        if (handle.generation != m_generation || handle.index < 0 || handle.index >= m_textures.Count)
        {
            throw new ArgumentException("Texture handle does not belong to this render graph generation.", nameof(handle));
        }
    }

    private void EnsureBuffer(RenderBufferHandle handle)
    {
        EnsureBuilding();
        if (handle.generation != m_generation || handle.index < 0 || handle.index >= m_buffers.Count)
        {
            throw new ArgumentException("Buffer handle does not belong to this render graph generation.", nameof(handle));
        }
    }

    private void EnsureBuilding()
    {
        if (m_compiled)
        {
            throw new InvalidOperationException("The render graph has already been compiled.");
        }
    }

    private string QualifyName(string name)
        => m_nameScopes.Count == 0
            ? name
            : $"{string.Join('/', m_nameScopes)}/{name}";

    private static void ValidateNameAndValue<T>(string name, T value)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
    }
}

/// <summary>
/// Owns one nested diagnostic-name prefix in a render graph builder.
/// </summary>
public sealed class RenderGraphNameScope : IDisposable
{
    private RenderGraphBuilder? m_graph;
    private readonly int m_depth;

    internal RenderGraphNameScope(RenderGraphBuilder graph, int depth)
    {
        m_graph = graph;
        m_depth = depth;
    }

    /// <summary>
    /// Ends the name prefix scope.
    /// </summary>
    public void Dispose()
    {
        RenderGraphBuilder? graph = m_graph;
        if (graph is null)
            return;
        graph.EndNameScope(m_depth);
        m_graph = null;
    }
}

/// <summary>
/// Provides transactional failure isolation while a feature contributes temporary graph state.
/// </summary>
public sealed class RenderGraphMutationScope : IDisposable
{
    private RenderGraphBuilder? m_graph;
    private readonly int m_textureCount;
    private readonly int m_bufferCount;
    private readonly int m_passCount;
    private readonly IReadOnlySet<RenderResourceKey> m_outputs;
    private bool m_committed;

    internal RenderGraphMutationScope(
        RenderGraphBuilder graph,
        int textureCount,
        int bufferCount,
        int passCount,
        IReadOnlySet<RenderResourceKey> outputs)
    {
        m_graph = graph;
        m_textureCount = textureCount;
        m_bufferCount = bufferCount;
        m_passCount = passCount;
        m_outputs = outputs;
    }

    /// <summary>
    /// Keeps every pass, resource and output added since this scope began.
    /// </summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(m_graph is null, this);
        m_committed = true;
    }

    /// <summary>
    /// Rolls back uncommitted graph mutations and ends this scope.
    /// </summary>
    public void Dispose()
    {
        RenderGraphBuilder? graph = m_graph;
        if (graph is null)
        {
            return;
        }

        m_graph = null;
        if (!m_committed)
        {
            graph.Rollback(m_textureCount, m_bufferCount, m_passCount, m_outputs);
        }
    }
}

/// <summary>
/// Provides ordering and common resource declarations for one pass.
/// </summary>
public abstract class RenderPassBuilder
{
    private readonly RenderGraphBuilder m_graph;
    private readonly RenderPassRecord m_pass;

    internal RenderPassBuilder(RenderGraphBuilder graph, RenderPassRecord pass)
    {
        m_graph = graph;
        m_pass = pass;
    }

    internal RenderGraphBuilder graph => m_graph;
    internal RenderPassRecord pass => m_pass;

    /// <summary>
    /// Orders this pass before every pass in a target phase.
    /// </summary>
    /// <param name="phase">
    /// Target phase.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RenderPassBuilder Before(RenderPhaseId phase)
    {
        m_pass.before.Add(phase);
        return this;
    }

    /// <summary>
    /// Orders this pass after every pass in a target phase.
    /// </summary>
    /// <param name="phase">
    /// Target phase.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RenderPassBuilder After(RenderPhaseId phase)
    {
        m_pass.after.Add(phase);
        return this;
    }

    /// <summary>
    /// Prevents pass culling because execution has an externally observable effect.
    /// </summary>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RenderPassBuilder HasSideEffect()
    {
        m_pass.hasSideEffect = true;
        return this;
    }

    /// <summary>
    /// Opts this pass into worker-thread recording through an isolated backend-neutral command list.
    /// </summary>
    /// <remarks>
    /// Pass data and code reached by the callback must be safe for worker-thread access. GPU execution
    /// and backend view order remain identical to the compiled graph order.
    /// </remarks>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RenderPassBuilder AllowParallelRecording()
    {
        m_pass.recordingMode = RenderPassRecordingMode.Parallel;
        return this;
    }

    /// <summary>
    /// Declares a shader read from a texture.
    /// </summary>
    /// <param name="texture">
    /// Texture read by this pass.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RenderPassBuilder ReadTexture(RenderTextureHandle texture)
    {
        m_graph.AddUse(
            m_pass,
            texture,
            RenderResourceAccess.Read,
            RenderResourceUseKind.GenericRead);
        return this;
    }

    /// <summary>
    /// Declares a shader read from a buffer.
    /// </summary>
    /// <param name="buffer">
    /// Buffer read by this pass.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RenderPassBuilder ReadBuffer(RenderBufferHandle buffer)
    {
        m_graph.AddUse(
            m_pass,
            buffer,
            RenderResourceAccess.Read,
            RenderResourceUseKind.GenericRead);
        return this;
    }
}

/// <summary>
/// Declares raster attachments and resource access.
/// </summary>
public sealed class RasterPassBuilder : RenderPassBuilder
{
    internal RasterPassBuilder(RenderGraphBuilder graph, RenderPassRecord pass)
        : base(graph, pass) { }

    /// <summary>
    /// Sets backend-ready column-major transform matrices for this raster view.
    /// </summary>
    /// <param name="viewMatrix">
    /// Exactly sixteen column-major world-to-view values.
    /// </param>
    /// <param name="projectionMatrix">
    /// Exactly sixteen column-major projection values.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RasterPassBuilder SetViewTransform(
        ReadOnlySpan<float> viewMatrix,
        ReadOnlySpan<float> projectionMatrix)
    {
        pass.viewTransform = new RenderViewTransform(viewMatrix, projectionMatrix);
        return this;
    }

    /// <summary>
    /// Directs this pass to a persistent presentation surface instead of the primary backbuffer.
    /// </summary>
    /// <param name="surface">
    /// Surface owned by the active graphics-device generation.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="surface"/> is invalid.
    /// </exception>
    public RasterPassBuilder UseSurface(RenderSurfaceHandle surface)
    {
        if (!surface.isValid)
        {
            throw new ArgumentException("Presentation surface handle is invalid.", nameof(surface));
        }

        pass.surface = surface;
        return this;
    }

    /// <summary>
    /// Clears the primary backbuffer or detached presentation surface before this pass.
    /// </summary>
    /// <param name="clearColor">
    /// Linear color written before draw commands.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RasterPassBuilder ClearPresentationTarget(RenderClearColor clearColor)
    {
        pass.clearsPresentationTarget = true;
        pass.presentationClearColor = clearColor;
        return this;
    }

    /// <summary>
    /// Attaches a color texture.
    /// </summary>
    /// <param name="texture">
    /// Color attachment texture.
    /// </param>
    /// <param name="slot">
    /// Zero-based color attachment slot.
    /// </param>
    /// <param name="loadAction">
    /// Initial content behavior.
    /// </param>
    /// <param name="storeAction">
    /// Final content behavior.
    /// </param>
    /// <param name="clearColor">
    /// Linear clear value used for <see cref="RenderLoadAction.Clear"/>.
    /// </param>
    /// <param name="mipLevel">
    /// Attached mip level.
    /// </param>
    /// <param name="arrayLayer">
    /// Attached texture-array layer.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RasterPassBuilder UseColorAttachment(
        RenderTextureHandle texture,
        int slot,
        RenderLoadAction loadAction,
        RenderStoreAction storeAction = RenderStoreAction.Store,
        RenderClearColor clearColor = default,
        int mipLevel = 0,
        int arrayLayer = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayLayer);
        graph.AddAttachment(pass, new RenderAttachment(
            texture,
            slot,
            false,
            mipLevel,
            arrayLayer,
            loadAction,
            storeAction,
            clearColor,
            1f,
            0));
        return this;
    }

    /// <summary>
    /// Attaches a depth or depth-stencil texture.
    /// </summary>
    /// <param name="texture">
    /// Depth attachment texture.
    /// </param>
    /// <param name="loadAction">
    /// Initial content behavior.
    /// </param>
    /// <param name="storeAction">
    /// Final content behavior.
    /// </param>
    /// <param name="clearDepth">
    /// Depth clear value.
    /// </param>
    /// <param name="clearStencil">
    /// Stencil clear value.
    /// </param>
    /// <param name="mipLevel">
    /// Attached mip level.
    /// </param>
    /// <param name="arrayLayer">
    /// Attached texture-array layer.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public RasterPassBuilder UseDepthAttachment(
        RenderTextureHandle texture,
        RenderLoadAction loadAction,
        RenderStoreAction storeAction = RenderStoreAction.Store,
        float clearDepth = 1f,
        byte clearStencil = 0,
        int mipLevel = 0,
        int arrayLayer = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayLayer);
        graph.AddAttachment(pass, new RenderAttachment(
            texture,
            0,
            true,
            mipLevel,
            arrayLayer,
            loadAction,
            storeAction,
            default,
            clearDepth,
            clearStencil));
        return this;
    }
}

/// <summary>
/// Declares compute resource reads and unordered writes.
/// </summary>
public sealed class ComputePassBuilder : RenderPassBuilder
{
    internal ComputePassBuilder(RenderGraphBuilder graph, RenderPassRecord pass)
        : base(graph, pass) { }

    /// <summary>
    /// Sets backend-ready column-major transform matrices exposed to this compute view.
    /// </summary>
    /// <param name="viewMatrix">
    /// Exactly sixteen column-major world-to-view values.
    /// </param>
    /// <param name="projectionMatrix">
    /// Exactly sixteen column-major projection values.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public ComputePassBuilder SetViewTransform(
        ReadOnlySpan<float> viewMatrix,
        ReadOnlySpan<float> projectionMatrix)
    {
        pass.viewTransform = new RenderViewTransform(viewMatrix, projectionMatrix);
        return this;
    }

    /// <summary>
    /// Declares an unordered texture read.
    /// </summary>
    /// <param name="texture">
    /// Storage texture read by compute work.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public ComputePassBuilder ReadStorageTexture(RenderTextureHandle texture)
    {
        graph.AddUse(
            pass,
            texture,
            RenderResourceAccess.Read,
            RenderResourceUseKind.StorageRead);
        return this;
    }

    /// <summary>
    /// Declares an unordered texture write.
    /// </summary>
    /// <param name="texture">
    /// Storage texture written by compute work.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public ComputePassBuilder WriteStorageTexture(RenderTextureHandle texture)
    {
        graph.AddUse(
            pass,
            texture,
            RenderResourceAccess.Write,
            RenderResourceUseKind.StorageWrite);
        return this;
    }

    /// <summary>
    /// Declares an unordered texture read and write.
    /// </summary>
    /// <param name="texture">
    /// Storage texture read and written by compute work.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public ComputePassBuilder ReadWriteStorageTexture(RenderTextureHandle texture)
    {
        graph.AddUse(
            pass,
            texture,
            RenderResourceAccess.ReadWrite,
            RenderResourceUseKind.StorageReadWrite);
        return this;
    }

    /// <summary>
    /// Declares an unordered buffer read.
    /// </summary>
    /// <param name="buffer">
    /// Storage buffer read by compute work.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public ComputePassBuilder ReadStorageBuffer(RenderBufferHandle buffer)
    {
        graph.AddUse(
            pass,
            buffer,
            RenderResourceAccess.Read,
            RenderResourceUseKind.StorageRead);
        return this;
    }

    /// <summary>
    /// Declares an unordered buffer write.
    /// </summary>
    /// <param name="buffer">
    /// Storage buffer written by compute work.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public ComputePassBuilder WriteStorageBuffer(RenderBufferHandle buffer)
    {
        graph.AddUse(
            pass,
            buffer,
            RenderResourceAccess.Write,
            RenderResourceUseKind.StorageWrite);
        return this;
    }

    /// <summary>
    /// Declares an unordered buffer read and write.
    /// </summary>
    /// <param name="buffer">
    /// Storage buffer read and written by compute work.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public ComputePassBuilder ReadWriteStorageBuffer(RenderBufferHandle buffer)
    {
        graph.AddUse(
            pass,
            buffer,
            RenderResourceAccess.ReadWrite,
            RenderResourceUseKind.StorageReadWrite);
        return this;
    }
}

/// <summary>
/// Declares explicit resource copy access.
/// </summary>
public sealed class CopyPassBuilder : RenderPassBuilder
{
    internal CopyPassBuilder(RenderGraphBuilder graph, RenderPassRecord pass)
        : base(graph, pass) { }

    /// <summary>
    /// Declares one texture copy operation.
    /// </summary>
    /// <param name="source">
    /// Copy source.
    /// </param>
    /// <param name="destination">
    /// Copy destination.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public CopyPassBuilder CopyTexture(RenderTextureHandle source, RenderTextureHandle destination)
    {
        graph.AddUse(
            pass,
            source,
            RenderResourceAccess.Read,
            RenderResourceUseKind.CopySource);
        graph.AddUse(
            pass,
            destination,
            RenderResourceAccess.Write,
            RenderResourceUseKind.CopyDestination);
        return this;
    }

    /// <summary>
    /// Declares one buffer copy operation.
    /// </summary>
    /// <param name="source">
    /// Copy source.
    /// </param>
    /// <param name="destination">
    /// Copy destination.
    /// </param>
    /// <returns>
    /// This builder for fluent declarations.
    /// </returns>
    public CopyPassBuilder CopyBuffer(RenderBufferHandle source, RenderBufferHandle destination)
    {
        graph.AddUse(
            pass,
            source,
            RenderResourceAccess.Read,
            RenderResourceUseKind.CopySource);
        graph.AddUse(
            pass,
            destination,
            RenderResourceAccess.Write,
            RenderResourceUseKind.CopyDestination);
        return this;
    }
}
