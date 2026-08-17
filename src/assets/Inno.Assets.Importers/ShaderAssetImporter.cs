using System.Collections.Generic;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;

namespace Inno.Assets.Importers;

[AssetImporterExtension]
internal sealed class ShaderAssetImporter : AssetImporter<ShaderAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".vert", ".frag", ".comp", ".glsl"];

    protected override AssetImportResult<ShaderAsset> Import(AssetImportContext context)
    {
        string source = context.ReadUtf8Text();
        string stage = context.extension switch
        {
            ".vert" => "vertex",
            ".frag" => "fragment",
            ".comp" => "compute",
            _ => "generic"
        };

        var asset = new ShaderAsset(stage, source);
        return new AssetImportResult<ShaderAsset>(asset, Encoding.UTF8.GetBytes(source));
    }

    protected override bool TryExport(ShaderAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = Encoding.UTF8.GetBytes(asset.sourceCode);
        return true;
    }
}
