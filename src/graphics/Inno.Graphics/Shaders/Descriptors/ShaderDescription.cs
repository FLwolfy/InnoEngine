namespace Inno.Graphics;

/// <summary>
/// Describes shader creation payload.
/// </summary>

public sealed class ShaderDescription
{
    public ShaderStage stage { get; init; }

    public ShaderLanguage language { get; init; }

    public required ReadOnlyMemory<byte> bytecode { get; init; }

    public string entryPoint { get; init; } = "main";
}
