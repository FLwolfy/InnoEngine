namespace Inno.Rendering;

/// <summary>
/// Represents a high-level texture asset.
/// </summary>
public abstract class Texture
{
    public int width { get; protected init; }

    public int height { get; protected init; }

    public TextureFormat format { get; protected init; } = TextureFormat.Unknown;
}
