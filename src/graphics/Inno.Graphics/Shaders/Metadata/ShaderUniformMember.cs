namespace Inno.Graphics;

/// <summary>
/// Describes a uniform member.
/// </summary>

public sealed class ShaderUniformMember
{
    public string name { get; init; } = string.Empty;

    public int offset { get; init; }

    public int size { get; init; }
}
