using System;
using System.IO;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Importers;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Reflection;

using Xunit;

namespace Inno.Assets.Loader.Tests;

public sealed class AssetLoaderTests
{
    private const AssetLoadMode C_ALL_LOAD_SOURCES =
        AssetLoadMode.MemoryCache | AssetLoadMode.DiskCache | AssetLoadMode.DiskRaw;

    [Fact]
    public void AssetImportContext_ReadUtf8Text_Works()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        var ctx = new AssetImportContext("A/B.txt", "/tmp/A/B.txt", bytes, "hash");

        Assert.Equal("abc", ctx.ReadUtf8Text());
        Assert.Equal(".txt", ctx.extension);
    }

    [Fact]
    public void LoadMode_MemoryCache_DiskCache_AndDiskRaw_AreTriedInOrder()
    {
        TypeCacheManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/game.txt";
        string source = Path.Combine(assets, "Config", "game.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "one", Encoding.UTF8);

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.Null(loader.Load<TextAsset>(relativePath, AssetLoadMode.None));
            Assert.Null(loader.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache));
            Assert.Null(loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache));

            TextAsset first = loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskRaw)!;
            TextAsset memoryCached = loader.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache)!;

            Assert.Equal("one", first.content);
            Assert.Same(first, memoryCached);
            Assert.True(File.Exists(Path.Combine(assets, relativePath + ".imeta")));
            Assert.True(File.Exists(Path.Combine(artifacts, relativePath + ".abin")));
            Assert.True(loader.Unload(relativePath));
            Assert.Null(loader.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache));

            TextAsset restored = loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache)!;
            Assert.Equal("one", restored.content);
            Assert.NotSame(first, restored);
            Assert.Same(restored, loader.Load<TextAsset>(relativePath, C_ALL_LOAD_SOURCES));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void DiskCache_DoesNotImportMissingOrStaleArtifacts_ButDiskRawDoes()
    {
        TypeCacheManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/stale.txt";
        string source = Path.Combine(assets, "Config", "stale.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "v1", Encoding.UTF8);

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.Null(loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache));

            TextAsset imported = loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskRaw)!;
            Guid persistentId = imported.identity.persistentId;
            Assert.Equal("v1", imported.content);

            Assert.True(loader.Unload(relativePath));
            File.WriteAllText(source, "v2", Encoding.UTF8);

            Assert.Null(loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache));

            TextAsset reimported = loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskRaw)!;
            Assert.Equal("v2", reimported.content);
            Assert.Equal(persistentId, reimported.identity.persistentId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void MemoryCache_WinsBeforeDiskCacheAndDiskRaw_WhenAssetIsLoaded()
    {
        TypeCacheManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/live.txt";
        string source = Path.Combine(assets, "Config", "live.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "loaded", Encoding.UTF8);

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            TextAsset loaded = loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskRaw)!;
            File.WriteAllText(source, "source-changed", Encoding.UTF8);

            TextAsset stillMemory = loader.Load<TextAsset>(relativePath, C_ALL_LOAD_SOURCES)!;
            Assert.Same(loaded, stillMemory);
            Assert.Equal("loaded", stillMemory.content);

            Assert.True(loader.Unload(relativePath));
            TextAsset reimported = loader.Load<TextAsset>(relativePath, C_ALL_LOAD_SOURCES)!;
            Assert.NotSame(loaded, reimported);
            Assert.Equal("source-changed", reimported.content);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void LoadRef_GetRef_Resolve_AndUnload_TrackLoadedState()
    {
        TypeCacheManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/ref.txt";
        string source = Path.Combine(assets, "Config", "ref.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "ref-content", Encoding.UTF8);

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.False(loader.LoadRef<TextAsset>("Missing/nope.txt").isValid);
            Assert.False(loader.LoadRef<TextAsset>(relativePath, AssetLoadMode.None).isValid);
            Assert.False(loader.GetRef<TextAsset>(relativePath).isValid);

            AssetRef<TextAsset> loadedRef = loader.LoadRef<TextAsset>(relativePath, AssetLoadMode.DiskRaw);
            Assert.True(loadedRef.isValid);
            TextAsset? resolved = loader.Resolve(loadedRef);
            Assert.NotNull(resolved);
            Assert.Equal("ref-content", resolved!.content);

            AssetRef<TextAsset> pathRef = loader.GetRef<TextAsset>(relativePath);
            AssetRef<TextAsset> identityRef = loader.GetRef<TextAsset>(loadedRef.identity);
            Assert.True(pathRef.isValid);
            Assert.True(identityRef.isValid);
            Assert.Equal(loadedRef.identity.persistentId, pathRef.identity.persistentId);
            Assert.Equal(loadedRef.identity.persistentId, identityRef.identity.persistentId);

            Assert.True(loader.Unload(loadedRef));
            Assert.Null(loader.Resolve(loadedRef));
            Assert.True(loader.GetRef<TextAsset>(relativePath).isValid);

            TextAsset restored = loader.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache)!;
            Assert.Equal("ref-content", restored.content);
            Assert.Equal(loadedRef.identity.persistentId, restored.identity.persistentId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Load_ReturnsNullAndInvalidRef_ForTypeMismatchMissingPathAndMissingImporter()
    {
        TypeCacheManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/type.txt";
        string source = Path.Combine(assets, "Config", "type.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "text", Encoding.UTF8);

        try
        {
            var noImporterLoader = new AssetLoader(assets, artifacts);
            Assert.Null(noImporterLoader.Load<TextAsset>(relativePath));
            Assert.False(noImporterLoader.LoadRef<TextAsset>(relativePath).isValid);

            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.Null(loader.Load<TextAsset>("Missing/file.txt"));
            Assert.False(loader.LoadRef<TextAsset>("Missing/file.txt").isValid);
            Assert.Null(loader.Load<TextureAsset>(relativePath, AssetLoadMode.DiskRaw));
            Assert.False(loader.LoadRef<TextureAsset>(relativePath, AssetLoadMode.DiskRaw).isValid);
            Assert.False(File.Exists(Path.Combine(assets, relativePath + ".imeta")));
            Assert.False(File.Exists(Path.Combine(artifacts, relativePath + ".abin")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void UnloadAll_ClearsEveryLoadedAsset()
    {
        TypeCacheManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        WriteText(assets, "A/one.txt", "one");
        WriteText(assets, "B/two.txt", "two");

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            AssetRef<TextAsset> one = loader.LoadRef<TextAsset>("A/one.txt");
            AssetRef<TextAsset> two = loader.LoadRef<TextAsset>("B/two.txt");

            Assert.Equal(new[] { "A/one.txt", "B/two.txt" }, loader.GetLoadedPaths());
            Assert.NotNull(loader.Resolve(one));
            Assert.NotNull(loader.Resolve(two));

            loader.UnloadAll();

            Assert.Empty(loader.GetLoadedPaths());
            Assert.Null(loader.Resolve(one));
            Assert.Null(loader.Resolve(two));
            Assert.False(loader.Unload(one));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static void WriteText(string assetsRoot, string relativePath, string content)
    {
        string path = Path.Combine(assetsRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetLoaderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
