namespace Inno.Graphics;

/// <summary>
/// Represents a render pass destination.
/// </summary>
public interface IGraphicsRenderTarget : IGraphicsResource
{
    int width { get; }

    int height { get; }
}
