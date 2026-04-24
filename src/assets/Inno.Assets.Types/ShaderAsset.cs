using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

public sealed class ShaderAsset : AssetObject
{
    [SerializableProperty]
    public string stage { get; private set; } = "unknown";

    [SerializableProperty]
    public string sourceCode { get; private set; } = string.Empty;

    public ShaderAsset()
    {
    }

    public ShaderAsset(string stage, string sourceCode)
    {
        this.stage = stage ?? "unknown";
        this.sourceCode = sourceCode ?? string.Empty;
    }
}
