using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

/// <summary>
/// Stores shader source code and its stage designation.
/// </summary>
[StableTypeId("c625cd28-09fa-496a-a2c4-2ca44b88f27f")]
public sealed class ShaderAsset : AssetObject
{
    /// <summary>
    /// Gets the shader stage name.
    /// </summary>
    [SerializableProperty]
    public string stage { get; private set; } = "unknown";

    /// <summary>
    /// Gets the shader source code.
    /// </summary>
    [SerializableProperty]
    public string sourceCode { get; private set; } = string.Empty;

    /// <summary>
    /// Creates an empty shader asset.
    /// </summary>
    public ShaderAsset()
    {
    }

    /// <summary>
    /// Creates a shader asset for a stage and source string.
    /// </summary>
    /// <param name="stage">Shader stage name.</param>
    /// <param name="sourceCode">Shader source code.</param>
    public ShaderAsset(string stage, string sourceCode)
    {
        this.stage = stage ?? "unknown";
        this.sourceCode = sourceCode ?? string.Empty;
    }
}
