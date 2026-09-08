using System;
using System.Collections.Generic;

namespace Inno.Rendering;

internal sealed class RecordedRenderCommandEncoder : RenderCommandEncoder
{
    private readonly List<Action<RenderCommandEncoder>> m_commands = [];
    private bool m_sealed;

    internal void Seal() => m_sealed = true;

    internal void Replay(RenderCommandEncoder destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!m_sealed)
            throw new InvalidOperationException("A render command list must be sealed before replay.");
        foreach (Action<RenderCommandEncoder> command in m_commands)
            command(destination);
    }

    /// <summary>
    /// Binds the graphics pipeline used by subsequent draw commands.
    /// </summary>
    /// <param name="pipeline">
    /// The pipeline consumed by bind graphics pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindGraphicsPipeline(GraphicsPipelineHandle pipeline)
        => Add(commands => commands.BindGraphicsPipeline(pipeline));

    /// <summary>
    /// Binds the compute pipeline used by subsequent dispatch commands.
    /// </summary>
    /// <param name="pipeline">
    /// The pipeline consumed by bind compute pipeline; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindComputePipeline(ComputePipelineHandle pipeline)
        => Add(commands => commands.BindComputePipeline(pipeline));

    /// <summary>
    /// Binds a texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="sampler">
    /// The sampler consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        RenderSamplerState sampler)
        => Add(commands => commands.BindTexture(binding, texture, sampler));

    /// <summary>
    /// Binds a texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="sampler">
    /// The sampler consumed by bind texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        RenderSamplerState sampler)
        => Add(commands => commands.BindTexture(binding, texture, sampler));

    /// <summary>
    /// Binds a writable texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="mipLevel">
    /// The mip level consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindStorageTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        int mipLevel = 0)
        => Add(commands => commands.BindStorageTexture(binding, texture, mipLevel));

    /// <summary>
    /// Binds a writable texture resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="texture">
    /// The texture consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="mipLevel">
    /// The mip level consumed by bind storage texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindStorageTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        int mipLevel = 0)
        => Add(commands => commands.BindStorageTexture(binding, texture, mipLevel));

    /// <summary>
    /// Binds a buffer resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="buffer">
    /// The buffer consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer)
        => Add(commands => commands.BindBuffer(binding, buffer));

    /// <summary>
    /// Binds a buffer resource to the requested shader binding.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="buffer">
    /// The buffer consumed by bind buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer)
        => Add(commands => commands.BindBuffer(binding, buffer));

    /// <summary>
    /// Updates the uniform state and applies the resulting invariants.
    /// </summary>
    /// <param name="binding">
    /// The binding consumed by set uniform; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="value">
    /// The concrete value read or transformed by this operation.
    /// </param>
    public override void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value)
    {
        byte[] snapshot = value.ToArray();
        Add(commands => commands.SetUniform(binding, snapshot));
    }

    /// <summary>
    /// Updates the transform state and applies the resulting invariants.
    /// </summary>
    /// <param name="columnMajorMatrix">
    /// The column major matrix consumed by set transform; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void SetTransform(ReadOnlySpan<float> columnMajorMatrix)
    {
        float[] snapshot = columnMajorMatrix.ToArray();
        Add(commands => commands.SetTransform(snapshot));
    }

    /// <summary>
    /// Updates the raster state state and applies the resulting invariants.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    public override void SetRasterState(RenderRasterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Add(commands => commands.SetRasterState(state));
    }

    /// <summary>
    /// Updates the stencil state and applies the resulting invariants.
    /// </summary>
    /// <param name="state">
    /// The lifecycle or domain state applied by this operation.
    /// </param>
    public override void SetStencil(RenderStencilState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Add(commands => commands.SetStencil(state));
    }

    /// <summary>
    /// Updates the viewport state and applies the resulting invariants.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    /// <param name="width">
    /// The width in logical units or pixels required by this operation.
    /// </param>
    /// <param name="height">
    /// The height in logical units or pixels required by this operation.
    /// </param>
    public override void SetViewport(int x, int y, int width, int height)
        => Add(commands => commands.SetViewport(x, y, width, height));

    /// <summary>
    /// Updates the scissor state and applies the resulting invariants.
    /// </summary>
    /// <param name="x">
    /// The horizontal or first component.
    /// </param>
    /// <param name="y">
    /// The vertical or second component.
    /// </param>
    /// <param name="width">
    /// The width in logical units or pixels required by this operation.
    /// </param>
    /// <param name="height">
    /// The height in logical units or pixels required by this operation.
    /// </param>
    public override void SetScissor(int x, int y, int width, int height)
        => Add(commands => commands.SetScissor(x, y, width, height));

    /// <summary>
    /// Binds a vertex buffer and its first vertex for subsequent draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstVertex">
    /// The first vertex consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0)
        => Add(commands => commands.BindVertexBuffer(buffer, firstVertex));

    /// <summary>
    /// Binds a vertex buffer and its first vertex for subsequent draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstVertex">
    /// The first vertex consumed by bind vertex buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0)
        => Add(commands => commands.BindVertexBuffer(buffer, firstVertex));

    /// <summary>
    /// Binds an index buffer and its first index for subsequent indexed draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstIndex">
    /// The first index consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0)
        => Add(commands => commands.BindIndexBuffer(buffer, firstIndex));

    /// <summary>
    /// Binds an index buffer and its first index for subsequent indexed draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstIndex">
    /// The first index consumed by bind index buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0)
        => Add(commands => commands.BindIndexBuffer(buffer, firstIndex));

    /// <summary>
    /// Binds per-instance data for subsequent instanced draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstInstance">
    /// The first instance consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindInstanceBuffer(
        RenderBufferHandle buffer,
        int firstInstance,
        int instanceCount)
        => Add(commands => commands.BindInstanceBuffer(buffer, firstInstance, instanceCount));

    /// <summary>
    /// Binds per-instance data for subsequent instanced draws.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstInstance">
    /// The first instance consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by bind instance buffer; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BindInstanceBuffer(
        PersistentBufferHandle buffer,
        int firstInstance,
        int instanceCount)
        => Add(commands => commands.BindInstanceBuffer(buffer, firstInstance, instanceCount));

    /// <summary>
    /// Renders the value presentation for the current editor frame.
    /// </summary>
    /// <param name="vertexCount">
    /// The vertex count consumed by draw; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by draw; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void Draw(int vertexCount, int instanceCount = 1)
        => Add(commands => commands.Draw(vertexCount, instanceCount));

    /// <summary>
    /// Renders the procedural presentation for the current editor frame.
    /// </summary>
    /// <param name="vertexCount">
    /// The vertex count consumed by draw procedural; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by draw procedural; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawProcedural(int vertexCount, int instanceCount = 1)
        => Add(commands => commands.DrawProcedural(vertexCount, instanceCount));

    /// <summary>
    /// Renders the indexed presentation for the current editor frame.
    /// </summary>
    /// <param name="indexCount">
    /// The index count consumed by draw indexed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="instanceCount">
    /// The instance count consumed by draw indexed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawIndexed(int indexCount, int instanceCount = 1)
        => Add(commands => commands.DrawIndexed(indexCount, instanceCount));

    /// <summary>
    /// Renders the indirect presentation for the current editor frame.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DrawIndirect(buffer, firstCommand, commandCount));

    /// <summary>
    /// Renders the indirect presentation for the current editor frame.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by draw indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DrawIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DrawIndirect(buffer, firstCommand, commandCount));

    /// <summary>
    /// Dispatches compute work using the supplied thread-group dimensions.
    /// </summary>
    /// <param name="groupCountX">
    /// The group count x consumed by dispatch; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="groupCountY">
    /// The group count y consumed by dispatch; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="groupCountZ">
    /// The group count z consumed by dispatch; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1)
        => Add(commands => commands.Dispatch(groupCountX, groupCountY, groupCountZ));

    /// <summary>
    /// Dispatches compute work using dimensions read from the supplied indirect buffer.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DispatchIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DispatchIndirect(buffer, firstCommand, commandCount));

    /// <summary>
    /// Dispatches compute work using dimensions read from the supplied indirect buffer.
    /// </summary>
    /// <param name="buffer">
    /// The buffer consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="firstCommand">
    /// The first command consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="commandCount">
    /// The command count consumed by dispatch indirect; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void DispatchIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DispatchIndirect(buffer, firstCommand, commandCount));

    /// <summary>
    /// Copies texture without transferring ownership of the source state.
    /// </summary>
    /// <param name="source">
    /// The source value or location read by this operation.
    /// </param>
    /// <param name="destination">
    /// The destination that receives the completed result.
    /// </param>
    public override void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination)
        => Add(commands => commands.CopyTexture(source, destination));

    /// <summary>
    /// Copies the requested texture region into the destination texture resource.
    /// </summary>
    /// <param name="source">
    /// The source value or location read by this operation.
    /// </param>
    /// <param name="sourceRegion">
    /// The source region consumed by blit texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="destination">
    /// The destination that receives the completed result.
    /// </param>
    /// <param name="destinationRegion">
    /// The destination region consumed by blit texture; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    public override void BlitTexture(
        RenderTextureHandle source,
        RenderTextureRegion sourceRegion,
        RenderTextureHandle destination,
        RenderTextureRegion destinationRegion)
        => Add(commands => commands.BlitTexture(source, sourceRegion, destination, destinationRegion));

    /// <summary>
    /// Copies buffer without transferring ownership of the source state.
    /// </summary>
    /// <param name="source">
    /// The source value or location read by this operation.
    /// </param>
    /// <param name="destination">
    /// The destination that receives the completed result.
    /// </param>
    public override void CopyBuffer(RenderBufferHandle source, RenderBufferHandle destination)
        => Add(commands => commands.CopyBuffer(source, destination));

    private void Add(Action<RenderCommandEncoder> command)
    {
        if (m_sealed)
            throw new InvalidOperationException("A recorded render command list is already sealed.");
        m_commands.Add(command);
    }
}
