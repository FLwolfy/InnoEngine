using Inno.Assets.Core;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

namespace Inno.Assets.Types;

/// <summary>
/// Stores decoded text content and its language hint.
/// </summary>
[StableTypeId("907c91cf-215b-42f4-9243-26d9666b231a")]
public sealed class TextAsset : AssetObject
{
    /// <summary>
    /// Gets the decoded text content.
    /// </summary>
    [SerializableProperty]
    public string content { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the language or format hint associated with the text.
    /// </summary>
    [SerializableProperty]
    public string languageHint { get; private set; } = "plain";

    /// <summary>
    /// Creates an empty text asset.
    /// </summary>
    public TextAsset()
    {
    }

    /// <summary>
    /// Creates a text asset with content and an optional language hint.
    /// </summary>
    /// <param name="content">Decoded text content.</param>
    /// <param name="languageHint">Language or format hint.</param>
    public TextAsset(string content, string languageHint = "plain")
    {
        this.content = content ?? string.Empty;
        this.languageHint = languageHint ?? "plain";
    }
}
