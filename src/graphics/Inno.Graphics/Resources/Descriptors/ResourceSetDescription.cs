namespace Inno.Graphics;

/// <summary>
/// Describes a bindable resource set.
/// </summary>
public sealed class ResourceSetDescription
{
    public IReadOnlyList<GraphicsResourceBinding> bindings { get; init; } = [];
}
