using Inno.Graphics;

namespace Inno.Graphics;

/// <summary>
/// Represents a GPU texture resource.
/// </summary>
public interface IGraphicsTexture : IGraphicsResource
{
    int width { get; }

    int height { get; }

    PixelFormat format { get; }

    void SetData<T>(ReadOnlySpan<T> data, int mipLevel = 0) where T : unmanaged;
}
