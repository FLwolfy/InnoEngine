using Inno.Assets.Core;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

public sealed class TextAsset : AssetObject
{
    [SerializableProperty]
    public string content { get; private set; } = string.Empty;

    [SerializableProperty]
    public string languageHint { get; private set; } = "plain";

    public TextAsset()
    {
    }

    public TextAsset(string content, string languageHint = "plain")
    {
        this.content = content ?? string.Empty;
        this.languageHint = languageHint ?? "plain";
    }
}
