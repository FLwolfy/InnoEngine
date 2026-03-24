namespace Inno.Graphics;

/// <summary>
/// Describes a shader uniform block.
/// </summary>

public sealed class ShaderUniformLayout
{
    public string name { get; init; } = string.Empty;

    public IReadOnlyList<ShaderUniformMember> members { get; init; } = [];
}
