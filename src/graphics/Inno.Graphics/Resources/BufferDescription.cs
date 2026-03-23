namespace Inno.Graphics;

/// <summary>
/// Describes graphics buffer creation.
/// </summary>
public sealed class BufferDescription
{
    public required int sizeInBytes { get; init; }

    public GraphicsBufferUsage usage { get; init; }

    public BufferCpuAccess cpuAccess { get; init; }
}
