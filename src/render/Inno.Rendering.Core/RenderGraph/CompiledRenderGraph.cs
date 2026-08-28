using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Inno.Rendering.Core;

/// <summary>
/// Stores backend-ready column-major camera matrices for one raster view.
/// </summary>
public sealed class RenderViewTransform
{
    private readonly float[] m_viewMatrix;
    private readonly float[] m_projectionMatrix;

    /// <summary>
    /// Creates a raster view transform.
    /// </summary>
    /// <param name="viewMatrix">Exactly sixteen column-major world-to-view values.</param>
    /// <param name="projectionMatrix">Exactly sixteen column-major projection values.</param>
    public RenderViewTransform(
        ReadOnlySpan<float> viewMatrix,
        ReadOnlySpan<float> projectionMatrix)
    {
        if (viewMatrix.Length != 16)
        {
            throw new ArgumentException("A view matrix requires exactly sixteen values.", nameof(viewMatrix));
        }

        if (projectionMatrix.Length != 16)
        {
            throw new ArgumentException(
                "A projection matrix requires exactly sixteen values.",
                nameof(projectionMatrix));
        }

        m_viewMatrix = viewMatrix.ToArray();
        m_projectionMatrix = projectionMatrix.ToArray();
    }

    /// <summary>Gets the immutable column-major view matrix.</summary>
    public ReadOnlyMemory<float> viewMatrix => m_viewMatrix;

    /// <summary>Gets the immutable column-major projection matrix.</summary>
    public ReadOnlyMemory<float> projectionMatrix => m_projectionMatrix;
}

/// <summary>
/// Describes one texture allocation selected by render-graph compilation.
/// </summary>
public sealed class CompiledRenderTexture
{
    internal CompiledRenderTexture(
        RenderTextureHandle handle,
        string name,
        RenderTextureDescriptor descriptor,
        bool imported,
        PersistentTextureHandle persistentHandle,
        int physicalSlot)
    {
        this.handle = handle;
        this.name = name;
        this.descriptor = descriptor;
        this.imported = imported;
        this.persistentHandle = persistentHandle;
        this.physicalSlot = physicalSlot;
    }

    /// <summary>Gets the frame-scoped logical handle.</summary>
    public RenderTextureHandle handle { get; }

    /// <summary>Gets the debug and diagnostic name.</summary>
    public string name { get; }

    /// <summary>Gets texture requirements.</summary>
    public RenderTextureDescriptor descriptor { get; }

    /// <summary>Gets whether the resource was imported from persistent device state.</summary>
    public bool imported { get; }

    /// <summary>Gets the persistent device texture for imported resources.</summary>
    public PersistentTextureHandle persistentHandle { get; }

    /// <summary>Gets the aliasing allocation slot, or negative one for imported resources.</summary>
    public int physicalSlot { get; }
}

/// <summary>
/// Describes one buffer allocation selected by render-graph compilation.
/// </summary>
public sealed class CompiledRenderBuffer
{
    internal CompiledRenderBuffer(
        RenderBufferHandle handle,
        string name,
        RenderBufferDescriptor descriptor,
        bool imported,
        PersistentBufferHandle persistentHandle,
        int physicalSlot)
    {
        this.handle = handle;
        this.name = name;
        this.descriptor = descriptor;
        this.imported = imported;
        this.persistentHandle = persistentHandle;
        this.physicalSlot = physicalSlot;
    }

    /// <summary>Gets the frame-scoped logical handle.</summary>
    public RenderBufferHandle handle { get; }

    /// <summary>Gets the debug and diagnostic name.</summary>
    public string name { get; }

    /// <summary>Gets buffer requirements.</summary>
    public RenderBufferDescriptor descriptor { get; }

    /// <summary>Gets whether the resource was imported from persistent device state.</summary>
    public bool imported { get; }

    /// <summary>Gets the persistent device buffer for imported resources.</summary>
    public PersistentBufferHandle persistentHandle { get; }

    /// <summary>Gets the aliasing allocation slot, or negative one for imported resources.</summary>
    public int physicalSlot { get; }
}

/// <summary>
/// Describes one compiled raster attachment.
/// </summary>
public sealed class CompiledRenderAttachment
{
    internal CompiledRenderAttachment(RenderAttachment attachment)
    {
        texture = attachment.texture;
        slot = attachment.slot;
        isDepth = attachment.isDepth;
        mipLevel = attachment.mipLevel;
        arrayLayer = attachment.arrayLayer;
        loadAction = attachment.loadAction;
        storeAction = attachment.storeAction;
        clearColor = attachment.clearColor;
        clearDepth = attachment.clearDepth;
        clearStencil = attachment.clearStencil;
    }

    /// <summary>Gets the attached graph texture.</summary>
    public RenderTextureHandle texture { get; }

    /// <summary>Gets the zero-based color slot, or zero for a depth attachment.</summary>
    public int slot { get; }

    /// <summary>Gets whether this is a depth or depth-stencil attachment.</summary>
    public bool isDepth { get; }

    /// <summary>Gets the attached mip level.</summary>
    public int mipLevel { get; }

    /// <summary>Gets the attached texture-array layer.</summary>
    public int arrayLayer { get; }

    /// <summary>Gets initial content behavior.</summary>
    public RenderLoadAction loadAction { get; }

    /// <summary>Gets final content behavior.</summary>
    public RenderStoreAction storeAction { get; }

    /// <summary>Gets the linear color clear value.</summary>
    public RenderClearColor clearColor { get; }

    /// <summary>Gets the depth clear value.</summary>
    public float clearDepth { get; }

    /// <summary>Gets the stencil clear value.</summary>
    public byte clearStencil { get; }
}

/// <summary>
/// Provides immutable backend-facing metadata for one scheduled pass.
/// </summary>
public sealed class CompiledRenderPass
{
    private readonly IReadOnlyList<CompiledRenderAttachment> m_attachments;
    private readonly Action<RenderPassContext> m_execute;

    internal CompiledRenderPass(
        string name,
        RenderPhaseId phase,
        RenderPassKind kind,
        int viewIndex,
        IReadOnlyList<CompiledRenderAttachment> attachments,
        RenderSurfaceHandle surface,
        bool clearsPresentationTarget,
        RenderClearColor presentationClearColor,
        RenderViewTransform? viewTransform,
        Action<RenderPassContext> execute)
    {
        this.name = name;
        this.phase = phase;
        this.kind = kind;
        this.viewIndex = viewIndex;
        m_attachments = attachments;
        this.surface = surface;
        this.clearsPresentationTarget = clearsPresentationTarget;
        this.presentationClearColor = presentationClearColor;
        this.viewTransform = viewTransform;
        m_execute = execute;
    }

    /// <summary>Gets the unique diagnostic name.</summary>
    public string name { get; }

    /// <summary>Gets the open render phase identifier.</summary>
    public RenderPhaseId phase { get; }

    /// <summary>Gets the pass command domain.</summary>
    public RenderPassKind kind { get; }

    /// <summary>Gets the backend-neutral logical view order.</summary>
    public int viewIndex { get; }

    /// <summary>Gets raster attachments in declaration order.</summary>
    public IReadOnlyList<CompiledRenderAttachment> attachments => m_attachments;

    /// <summary>Gets the detached presentation surface, or an invalid handle for the primary backbuffer.</summary>
    public RenderSurfaceHandle surface { get; }

    /// <summary>Gets whether this pass clears its presentation target before drawing.</summary>
    public bool clearsPresentationTarget { get; }

    /// <summary>Gets the linear clear color for a presentation target.</summary>
    public RenderClearColor presentationClearColor { get; }

    /// <summary>Gets backend-ready camera matrices, or <see langword="null"/> for a matrix-free pass.</summary>
    public RenderViewTransform? viewTransform { get; }

    internal void Execute(RenderPassContext context) => m_execute(context);
}

/// <summary>
/// Bridges compiled graph execution to a concrete graphics backend.
/// </summary>
public interface IRenderGraphBackend
{
    /// <summary>Begins execution and prepares transient allocations.</summary>
    /// <param name="graph">Compiled graph to execute.</param>
    void BeginGraph(CompiledRenderGraph graph);

    /// <summary>Begins one scheduled pass and acquires its command encoder.</summary>
    /// <param name="pass">Scheduled pass metadata.</param>
    /// <returns>A command encoder scoped to this pass.</returns>
    RenderCommandEncoder BeginPass(CompiledRenderPass pass);

    /// <summary>Ends one scheduled pass and releases its encoder.</summary>
    /// <param name="pass">Scheduled pass metadata.</param>
    void EndPass(CompiledRenderPass pass);

    /// <summary>Ends graph execution without presenting an additional frame.</summary>
    /// <param name="graph">Compiled graph that finished or failed.</param>
    void EndGraph(CompiledRenderGraph graph);
}

/// <summary>
/// Contains validated, culled and topologically scheduled frame work.
/// </summary>
public sealed class CompiledRenderGraph
{
    private readonly IReadOnlyList<CompiledRenderPass> m_passes;
    private readonly IReadOnlyList<CompiledRenderTexture> m_textures;
    private readonly IReadOnlyList<CompiledRenderBuffer> m_buffers;

    internal CompiledRenderGraph(
        uint generation,
        IReadOnlyList<CompiledRenderPass> passes,
        IReadOnlyList<CompiledRenderTexture> textures,
        IReadOnlyList<CompiledRenderBuffer> buffers)
    {
        this.generation = generation;
        m_passes = passes;
        m_textures = textures;
        m_buffers = buffers;
    }

    /// <summary>Gets the source frame generation.</summary>
    public uint generation { get; }

    /// <summary>Gets scheduled passes in backend view order.</summary>
    public IReadOnlyList<CompiledRenderPass> passes => m_passes;

    /// <summary>Gets compiled logical texture allocations.</summary>
    public IReadOnlyList<CompiledRenderTexture> textures => m_textures;

    /// <summary>Gets compiled logical buffer allocations.</summary>
    public IReadOnlyList<CompiledRenderBuffer> buffers => m_buffers;

    /// <summary>
    /// Executes pass callbacks through a concrete backend with complete unwind.
    /// </summary>
    /// <param name="backend">Backend bridge for the current device generation.</param>
    /// <param name="frameIndex">Monotonic frame index exposed to pass callbacks.</param>
    /// <exception cref="AggregateException">Thrown when execution and cleanup both fail.</exception>
    public void Execute(IRenderGraphBackend backend, ulong frameIndex)
    {
        ArgumentNullException.ThrowIfNull(backend);
        List<Exception>? errors = null;
        bool graphStarted = false;

        try
        {
            backend.BeginGraph(this);
            graphStarted = true;
            foreach (CompiledRenderPass pass in m_passes)
            {
                bool passStarted = false;
                try
                {
                    RenderCommandEncoder commands = backend.BeginPass(pass);
                    ArgumentNullException.ThrowIfNull(commands);
                    passStarted = true;
                    pass.Execute(new RenderPassContext(commands, frameIndex));
                }
                finally
                {
                    if (passStarted)
                    {
                        backend.EndPass(pass);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            errors = [exception];
        }
        finally
        {
            if (graphStarted)
            {
                try
                {
                    backend.EndGraph(this);
                }
                catch (Exception exception)
                {
                    errors ??= [];
                    errors.Add(exception);
                }
            }
        }

        if (errors is null)
        {
            return;
        }

        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }

        throw new AggregateException("Render graph execution failed during recording or cleanup.", errors);
    }
}

/// <summary>
/// Contains render-graph compilation diagnostics and an optional executable graph.
/// </summary>
public sealed class RenderGraphCompileResult
{
    private readonly IReadOnlyList<RenderGraphDiagnostic> m_diagnostics;

    internal RenderGraphCompileResult(
        CompiledRenderGraph? graph,
        IReadOnlyList<RenderGraphDiagnostic> diagnostics)
    {
        this.graph = graph;
        m_diagnostics = diagnostics;
    }

    /// <summary>Gets the executable graph, or <see langword="null"/> when compilation failed.</summary>
    public CompiledRenderGraph? graph { get; }

    /// <summary>Gets deterministic compilation diagnostics.</summary>
    public IReadOnlyList<RenderGraphDiagnostic> diagnostics => m_diagnostics;

    /// <summary>Gets whether an executable graph was produced.</summary>
    public bool succeeded => graph is not null;
}
