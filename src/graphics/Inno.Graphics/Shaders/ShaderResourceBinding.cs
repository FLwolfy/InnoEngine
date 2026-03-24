namespace Inno.Graphics;

/// <summary>
/// Describes expected shader resource binding.
/// </summary>

public sealed class ShaderResourceBinding
{
    public string name { get; init; } = string.Empty;

    public int slot { get; init; }
}
