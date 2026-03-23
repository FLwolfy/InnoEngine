using Inno.Graphics;

namespace Inno.Graphics;

/// <summary>
/// Represents a GPU buffer.
/// </summary>
public interface IGraphicsBuffer : IGraphicsResource
{
    int sizeInBytes { get; }

    GraphicsBufferUsage usage { get; }

    void SetData<T>(ReadOnlySpan<T> data, int destinationOffsetInBytes = 0) where T : unmanaged;
}
