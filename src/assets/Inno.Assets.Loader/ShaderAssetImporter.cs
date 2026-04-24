using System.Collections.Generic;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Types;

namespace Inno.Assets.Loader;

public sealed class ShaderAssetImporter : AssetImporter<ShaderAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".vert", ".frag", ".comp", ".glsl"];

    public override AssetImportResult<ShaderAsset> ImportTyped(in AssetImportContext context)
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

    public override bool TryExportTyped(ShaderAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = Encoding.UTF8.GetBytes(asset.sourceCode);
        return true;
    }
}
