namespace Inno.Graphics;

/// <summary>
/// Describes a resource binding entry for a resource set.
/// </summary>
public sealed class GraphicsResourceBinding
{
    public int slot { get; init; }

    public GraphicsBindingType bindingType { get; init; }

    public IGraphicsResource resource { get; init; } = default!;
}
