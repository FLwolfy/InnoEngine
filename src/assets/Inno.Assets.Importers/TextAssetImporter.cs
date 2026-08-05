using System.Collections.Generic;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;

namespace Inno.Assets.Importers;

public sealed class TextAssetImporter : AssetImporter<TextAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } =
        [".txt", ".json", ".yaml", ".yml", ".md", ".xml"];

    public override AssetImportResult<TextAsset> ImportTyped(in AssetImportContext context)
    {
        string content = context.ReadUtf8Text();
        string hint = context.extension switch
        {
            ".json" => "json",
            ".yaml" => "yaml",
            ".yml" => "yaml",
            ".xml" => "xml",
            ".md" => "markdown",
            _ => "plain"
        };

        var asset = new TextAsset(content, hint);
        return new AssetImportResult<TextAsset>(asset, Encoding.UTF8.GetBytes(content));
    }

    public override bool TryExportTyped(TextAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = Encoding.UTF8.GetBytes(asset.content);
        return true;
    }
}
