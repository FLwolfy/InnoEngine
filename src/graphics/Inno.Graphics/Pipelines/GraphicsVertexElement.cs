
namespace Inno.Graphics;

/// <summary>
/// Describes a vertex element.
/// </summary>

public sealed class GraphicsVertexElement
{
    public required string semantic { get; init; }

    public int semanticIndex { get; init; }

    public VertexFormat format { get; init; }

    public int offset { get; init; }
}
