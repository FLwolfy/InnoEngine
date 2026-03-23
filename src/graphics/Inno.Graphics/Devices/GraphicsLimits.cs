namespace Inno.Graphics;

/// <summary>
/// Reports backend and adapter capability limits.
/// </summary>
public sealed class GraphicsLimits
{
    public int maxTextureSize2D { get; init; } = 16384;

    public int maxColorAttachments { get; init; } = 8;

    public int maxVertexAttributes { get; init; } = 16;
}
