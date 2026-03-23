namespace Inno.Graphics;

/// <summary>
/// Defines shader stages.
/// </summary>
public enum ShaderStage
{
    Vertex = 0,
    Fragment,
    Compute
}

/// <summary>
/// Defines shader source language.
/// </summary>
public enum ShaderLanguage
{
    SpirV = 0,
    Hlsl,
    Glsl,
    Metal
}

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

/// <summary>
/// Describes a shader uniform block.
/// </summary>
public sealed class ShaderUniformLayout
{
    public string name { get; init; } = string.Empty;

    public IReadOnlyList<ShaderUniformMember> members { get; init; } = [];
}

/// <summary>
/// Describes a uniform member.
/// </summary>
public sealed class ShaderUniformMember
{
    public string name { get; init; } = string.Empty;

    public int offset { get; init; }

    public int size { get; init; }
}

/// <summary>
/// Describes expected shader resource binding.
/// </summary>
public sealed class ShaderResourceBinding
{
    public string name { get; init; } = string.Empty;

    public int slot { get; init; }
}
