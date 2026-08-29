using System;

namespace Inno.Rendering.Core;

/// <summary>Describes one CPU-to-GPU buffer upload that remains valid for the current frame only.</summary>
public sealed class RenderBufferUploadDescriptor
{
    /// <summary>Creates a frame upload descriptor.</summary>
    /// <param name="elementStride">Element size in bytes.</param>
    /// <param name="usage">Vertex, index, storage, or compatible combined usage.</param>
    /// <param name="vertexLayout">Required layout when vertex usage is present.</param>
    /// <param name="indexFormat">Index representation when index usage is present.</param>
    public RenderBufferUploadDescriptor(
        int elementStride,
        RenderBufferUsage usage,
        RenderVertexLayout? vertexLayout = null,
        RenderIndexFormat indexFormat = RenderIndexFormat.UInt32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementStride);
        RenderBufferUsage allowed = RenderBufferUsage.Vertex
            | RenderBufferUsage.Index
            | RenderBufferUsage.Storage;
        if (usage == 0 || (usage & ~allowed) != 0)
        {
            throw new ArgumentException(
                "Frame uploads support vertex, index, and storage usage only.",
                nameof(usage));
        }
        if ((usage & (RenderBufferUsage.Vertex | RenderBufferUsage.Index))
            == (RenderBufferUsage.Vertex | RenderBufferUsage.Index))
        {
            throw new ArgumentException("An upload cannot be both vertex and index input.", nameof(usage));
        }
        if ((usage & RenderBufferUsage.Vertex) != 0 && vertexLayout is null)
            throw new ArgumentException("Vertex uploads require an interleaved layout.", nameof(vertexLayout));
        if (vertexLayout is not null && vertexLayout.stride != elementStride)
            throw new ArgumentException("Vertex layout stride must match element stride.", nameof(vertexLayout));
        int indexStride = indexFormat == RenderIndexFormat.UInt16 ? 2 : 4;
        if ((usage & RenderBufferUsage.Index) != 0 && indexStride != elementStride)
            throw new ArgumentException("Index format must match element stride.", nameof(indexFormat));

        this.elementStride = elementStride;
        this.usage = usage;
        this.vertexLayout = vertexLayout;
        this.indexFormat = indexFormat;
    }

    /// <summary>Gets the element size in bytes.</summary>
    public int elementStride { get; }

    /// <summary>Gets permitted GPU uses.</summary>
    public RenderBufferUsage usage { get; }

    /// <summary>Gets the vertex layout when vertex usage is present.</summary>
    public RenderVertexLayout? vertexLayout { get; }

    /// <summary>Gets the index representation when index usage is present.</summary>
    public RenderIndexFormat indexFormat { get; }
}

/// <summary>Identifies one range in a frame upload page without exposing its persistent backing buffer.</summary>
public readonly record struct RenderBufferSlice
{
    internal RenderBufferSlice(
        PersistentBufferHandle buffer,
        int firstElement,
        int elementCount,
        RenderBufferUsage usage,
        ulong frameIndex)
    {
        this.buffer = buffer;
        this.firstElement = firstElement;
        this.elementCount = elementCount;
        this.usage = usage;
        this.frameIndex = frameIndex;
    }

    internal PersistentBufferHandle buffer { get; }
    internal ulong frameIndex { get; }

    /// <summary>Gets the first uploaded element in the backing frame page.</summary>
    public int firstElement { get; }

    /// <summary>Gets the number of uploaded elements.</summary>
    public int elementCount { get; }

    /// <summary>Gets permitted GPU uses for this slice.</summary>
    public RenderBufferUsage usage { get; }

    /// <summary>Gets whether this slice was produced by a frame upload service.</summary>
    public bool isValid => buffer.isValid && elementCount > 0;
}
