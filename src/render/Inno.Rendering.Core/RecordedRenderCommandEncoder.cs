using System;
using System.Collections.Generic;

namespace Inno.Rendering.Core;

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

    public override void BindGraphicsPipeline(GraphicsPipelineHandle pipeline)
        => Add(commands => commands.BindGraphicsPipeline(pipeline));

    public override void BindComputePipeline(ComputePipelineHandle pipeline)
        => Add(commands => commands.BindComputePipeline(pipeline));

    public override void BindTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        RenderSamplerState sampler)
        => Add(commands => commands.BindTexture(binding, texture, sampler));

    public override void BindTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        RenderSamplerState sampler)
        => Add(commands => commands.BindTexture(binding, texture, sampler));

    public override void BindStorageTexture(
        RenderBindingId binding,
        RenderTextureHandle texture,
        int mipLevel = 0)
        => Add(commands => commands.BindStorageTexture(binding, texture, mipLevel));

    public override void BindStorageTexture(
        RenderBindingId binding,
        PersistentTextureHandle texture,
        int mipLevel = 0)
        => Add(commands => commands.BindStorageTexture(binding, texture, mipLevel));

    public override void BindBuffer(RenderBindingId binding, RenderBufferHandle buffer)
        => Add(commands => commands.BindBuffer(binding, buffer));

    public override void BindBuffer(RenderBindingId binding, PersistentBufferHandle buffer)
        => Add(commands => commands.BindBuffer(binding, buffer));

    public override void SetUniform(RenderBindingId binding, ReadOnlySpan<byte> value)
    {
        byte[] snapshot = value.ToArray();
        Add(commands => commands.SetUniform(binding, snapshot));
    }

    public override void SetTransform(ReadOnlySpan<float> columnMajorMatrix)
    {
        float[] snapshot = columnMajorMatrix.ToArray();
        Add(commands => commands.SetTransform(snapshot));
    }

    public override void SetRasterState(RenderRasterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Add(commands => commands.SetRasterState(state));
    }

    public override void SetStencil(RenderStencilState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Add(commands => commands.SetStencil(state));
    }

    public override void SetViewport(int x, int y, int width, int height)
        => Add(commands => commands.SetViewport(x, y, width, height));

    public override void SetScissor(int x, int y, int width, int height)
        => Add(commands => commands.SetScissor(x, y, width, height));

    public override void BindVertexBuffer(RenderBufferHandle buffer, int firstVertex = 0)
        => Add(commands => commands.BindVertexBuffer(buffer, firstVertex));

    public override void BindVertexBuffer(PersistentBufferHandle buffer, int firstVertex = 0)
        => Add(commands => commands.BindVertexBuffer(buffer, firstVertex));

    public override void BindIndexBuffer(RenderBufferHandle buffer, int firstIndex = 0)
        => Add(commands => commands.BindIndexBuffer(buffer, firstIndex));

    public override void BindIndexBuffer(PersistentBufferHandle buffer, int firstIndex = 0)
        => Add(commands => commands.BindIndexBuffer(buffer, firstIndex));

    public override void BindInstanceBuffer(
        RenderBufferHandle buffer,
        int firstInstance,
        int instanceCount)
        => Add(commands => commands.BindInstanceBuffer(buffer, firstInstance, instanceCount));

    public override void BindInstanceBuffer(
        PersistentBufferHandle buffer,
        int firstInstance,
        int instanceCount)
        => Add(commands => commands.BindInstanceBuffer(buffer, firstInstance, instanceCount));

    public override void Draw(int vertexCount, int instanceCount = 1)
        => Add(commands => commands.Draw(vertexCount, instanceCount));

    public override void DrawProcedural(int vertexCount, int instanceCount = 1)
        => Add(commands => commands.DrawProcedural(vertexCount, instanceCount));

    public override void DrawIndexed(int indexCount, int instanceCount = 1)
        => Add(commands => commands.DrawIndexed(indexCount, instanceCount));

    public override void DrawIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DrawIndirect(buffer, firstCommand, commandCount));

    public override void DrawIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DrawIndirect(buffer, firstCommand, commandCount));

    public override void Dispatch(int groupCountX, int groupCountY = 1, int groupCountZ = 1)
        => Add(commands => commands.Dispatch(groupCountX, groupCountY, groupCountZ));

    public override void DispatchIndirect(
        RenderBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DispatchIndirect(buffer, firstCommand, commandCount));

    public override void DispatchIndirect(
        PersistentBufferHandle buffer,
        int firstCommand = 0,
        int commandCount = 1)
        => Add(commands => commands.DispatchIndirect(buffer, firstCommand, commandCount));

    public override void CopyTexture(RenderTextureHandle source, RenderTextureHandle destination)
        => Add(commands => commands.CopyTexture(source, destination));

    public override void BlitTexture(
        RenderTextureHandle source,
        RenderTextureRegion sourceRegion,
        RenderTextureHandle destination,
        RenderTextureRegion destinationRegion)
        => Add(commands => commands.BlitTexture(source, sourceRegion, destination, destinationRegion));

    public override void CopyBuffer(RenderBufferHandle source, RenderBufferHandle destination)
        => Add(commands => commands.CopyBuffer(source, destination));

    private void Add(Action<RenderCommandEncoder> command)
    {
        if (m_sealed)
            throw new InvalidOperationException("A recorded render command list is already sealed.");
        m_commands.Add(command);
    }
}
