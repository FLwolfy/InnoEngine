using System.Collections.Generic;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;

namespace Inno.Assets.Importers;

[AssetImporterExtension]
internal sealed class TextAssetImporter : AssetImporter<TextAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } =
        [".txt", ".json", ".yaml", ".yml", ".md", ".xml"];

    protected override AssetImportResult<TextAsset> Import(AssetImportContext context)
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

    protected override bool TryExport(TextAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = Encoding.UTF8.GetBytes(asset.content);
        return true;
    }
}
