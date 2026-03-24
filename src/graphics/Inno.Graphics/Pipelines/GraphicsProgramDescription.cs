
namespace Inno.Graphics;

/// <summary>
/// Describes program composition from shader stages.
/// </summary>
public sealed class GraphicsProgramDescription
{
    public required IReadOnlyList<IGraphicsShader> shaders { get; init; }
}
