using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Loader.Tests;

public sealed class ImporterTests
{
    [Fact]
    public void TextImporter_ImportsTextAsset()
    {
        var importer = new TextAssetImporter();
        byte[] raw = Encoding.UTF8.GetBytes("{\"name\":\"inno\"}");
        var ctx = new AssetImportContext("Config/a.json", "/tmp/Config/a.json", raw, "hash");

        AssetImportResult<TextAsset> result = importer.ImportTyped(ctx);
        Assert.Equal("json", result.asset.languageHint);
        Assert.Equal("{\"name\":\"inno\"}", result.asset.content);
    }

    [Fact]
    public void ShaderImporter_DetectsStageFromExtension()
    {
        var importer = new ShaderAssetImporter();
        byte[] raw = Encoding.UTF8.GetBytes("void main(){}");
        var ctx = new AssetImportContext("Shaders/a.vert", "/tmp/Shaders/a.vert", raw, "hash");

        AssetImportResult<ShaderAsset> result = importer.ImportTyped(ctx);
        Assert.Equal("vertex", result.asset.stage);
    }
}
