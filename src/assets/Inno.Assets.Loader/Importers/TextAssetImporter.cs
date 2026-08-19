using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;

namespace Inno.Assets.Loader.Importers;

[AssetImporterExtension]
internal sealed class TextAssetImporter : AssetImporter<TextAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } =
        [".txt", ".json", ".yaml", ".yml", ".md", ".xml"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<TextAsset> output,
        CancellationToken cancellationToken)
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
        output.SetAsset(asset);
        await output.WriteArtifactAsync(
            "runtime",
            Encoding.UTF8.GetBytes(content),
            cancellationToken).ConfigureAwait(false);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        TextAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(Encoding.UTF8.GetBytes(asset.content));
}
