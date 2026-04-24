using Inno.Assets.IO;

using Xunit;

namespace Inno.Assets.IO.Tests;

public sealed class AssetPathTests
{
    [Fact]
    public void Normalize_TrimsAndNormalizesSeparators()
    {
        string p = AssetPath.Normalize("./A\\B/C/");
        Assert.Equal("A/B/C", p);
    }

    [Fact]
    public void Combine_NormalizesResult()
    {
        string p = AssetPath.Combine("Folder", "./Sub\\File.txt");
        Assert.Equal("Folder/Sub/File.txt", p);
    }
}
