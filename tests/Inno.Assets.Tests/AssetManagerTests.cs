using System;
using System.IO;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerTests
{
    [Fact]
    public void Load_Reimport_HandleResolve_Work()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetsTests", Guid.NewGuid().ToString("N"));
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        Directory.CreateDirectory(Path.Combine(assets, "Config"));

        string rel = "Config/game.txt";
        string abs = Path.Combine(assets, rel);
        File.WriteAllText(abs, "one", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(assets, artifacts, registerBuiltInImporters: true);

            TextAsset first = AssetManager.Load<TextAsset>(rel);
            Assert.Equal("one", first.content);
            Assert.True(first.persistentId != Guid.Empty);

            AssetHandle<TextAsset> handle = AssetManager.GetHandle<TextAsset>(rel);
            Assert.True(AssetManager.TryResolve(handle, out TextAsset resolved));
            Assert.Equal("one", resolved.content);

            File.WriteAllText(abs, "two", Encoding.UTF8);
            TextAsset second = AssetManager.Reimport<TextAsset>(rel);
            Assert.Equal("two", second.content);

            Assert.True(AssetManager.Unload(rel));
            Assert.False(AssetManager.TryGetLoaded<TextAsset>(rel, out _));
        }
        finally
        {
            AssetManager.Shutdown();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
