
namespace Inno.Graphics;

/// <summary>
/// Describes input layout creation.
/// </summary>

public sealed class GraphicsInputLayoutDescription
{
    public required IReadOnlyList<GraphicsVertexElement> elements { get; init; }

    public int stride { get; init; }
}
