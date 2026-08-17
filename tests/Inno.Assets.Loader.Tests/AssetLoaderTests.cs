using System;
using System.IO;
using System.Reflection;
using System.Text;

using Inno.Assets.Importers;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Loader.Tests;

public sealed class AssetLoaderTests
{
    [Fact]
    public void AssetImportContext_ReadUtf8Text_Works()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("abc");
        var ctx = new AssetImportContext("A/B.txt", "/tmp/A/B.txt", bytes, "hash");

        Assert.Equal("abc", ctx.ReadUtf8Text());
        Assert.Equal(".txt", ctx.extension);
    }

    [Fact]
    public void Import_WritesMetadataAndArtifact_WithoutLoadingAsset()
    {
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/game.txt";
        WriteText(assets, relativePath, "one");

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.True(loader.Import(relativePath));
            Assert.True(File.Exists(Path.Combine(assets, relativePath + ".imeta")));
            Assert.True(File.Exists(Path.Combine(artifacts, relativePath + ".abin")));
            Assert.Empty(loader.GetLoadedPaths());

            Identity assetId = loader.GetIdentity(relativePath);
            Assert.True(IsValid(assetId));
            Assert.Null(ResolveText(loader, assetId));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Load_LoadsExistingMetadataAndArtifactIntoMemory()
    {
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/load.txt";
        WriteText(assets, relativePath, "loaded");

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.True(loader.Import(relativePath));
            Identity assetId = loader.GetIdentity(relativePath);
            Assert.Null(ResolveText(loader, assetId));

            Assert.NotNull(loader.Load(relativePath, typeof(TextAsset)));
            TextAsset? loaded = ResolveText(loader, assetId);

            Assert.NotNull(loaded);
            Assert.Equal("loaded", loaded!.content);
            Assert.Equal(new[] { relativePath }, loader.GetLoadedPaths());
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Load_ReturnsFalse_WhenArtifactsAreMissingOrStale()
    {
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/stale.txt";
        WriteText(assets, relativePath, "v1");

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.Null(loader.Load(relativePath, typeof(TextAsset)));

            Assert.True(loader.Import(relativePath));
            Assert.NotNull(loader.Load(relativePath, typeof(TextAsset)));
            Identity assetId = loader.GetIdentity(relativePath);
            Guid persistentId = assetId.persistentId;
            Assert.True(loader.Unload(relativePath));

            WriteText(assets, relativePath, "v2");
            Assert.Null(loader.Load(relativePath, typeof(TextAsset)));
            Assert.Null(ResolveText(loader, assetId));

            Assert.True(loader.Import(relativePath));
            Assert.NotNull(loader.Load(relativePath, typeof(TextAsset)));
            TextAsset? reloaded = ResolveText(loader, loader.GetIdentity(relativePath));
            Assert.NotNull(reloaded);
            Assert.Equal("v2", reloaded!.content);
            Assert.Equal(persistentId, reloaded.identity.persistentId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void GetRef_Resolve_AndUnload_TrackLoadedState()
    {
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/ref.txt";
        WriteText(assets, relativePath, "ref-content");

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.False(IsValid(loader.GetIdentity(relativePath)));
            Assert.True(loader.Import(relativePath));

            Identity pathId = loader.GetIdentity(relativePath);
            Assert.True(IsValid(pathId));
            Assert.Null(ResolveText(loader, pathId));

            Assert.NotNull(loader.Load(relativePath, typeof(TextAsset)));
            TextAsset? resolved = ResolveText(loader, pathId);
            Assert.NotNull(resolved);
            Assert.Equal("ref-content", resolved!.content);

            Identity identityRef = pathId;
            Assert.True(IsValid(identityRef));
            Assert.Equal(pathId.persistentId, identityRef.persistentId);

            Assert.True(loader.Unload(pathId));
            Assert.Null(ResolveText(loader, pathId));
            Assert.True(IsValid(loader.GetIdentity(relativePath)));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ImportAndLoad_ReturnFalse_ForTypeMismatchMissingPathAndMissingImporter()
    {
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/type.txt";
        WriteText(assets, relativePath, "text");

        try
        {
            var noImporterLoader = new AssetLoader(assets, artifacts);
            Assert.False(noImporterLoader.Import(relativePath));
            Assert.Null(noImporterLoader.Load(relativePath, typeof(TextAsset)));

            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.False(loader.Import("Missing/file.txt"));
            Assert.Null(loader.Load("Missing/file.txt", typeof(TextAsset)));
            Assert.True(loader.Import(relativePath));
            Assert.Null(loader.Load(relativePath, typeof(TextureAsset)));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void UnloadAll_ClearsEveryLoadedAsset_WithoutDeletingGeneratedFiles()
    {
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        WriteText(assets, "A/one.txt", "one");
        WriteText(assets, "B/two.txt", "two");

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.True(loader.Import("A/one.txt"));
            Assert.True(loader.Import("B/two.txt"));
            Assert.NotNull(loader.Load("A/one.txt", typeof(TextAsset)));
            Assert.NotNull(loader.Load("B/two.txt", typeof(TextAsset)));

            Identity one = loader.GetIdentity("A/one.txt");
            Identity two = loader.GetIdentity("B/two.txt");

            Assert.Equal(new[] { "A/one.txt", "B/two.txt" }, loader.GetLoadedPaths());
            Assert.NotNull(ResolveText(loader, one));
            Assert.NotNull(ResolveText(loader, two));

            loader.UnloadAll();

            Assert.Empty(loader.GetLoadedPaths());
            Assert.Null(ResolveText(loader, one));
            Assert.Null(ResolveText(loader, two));
            Assert.False(loader.Unload(one));
            Assert.True(File.Exists(Path.Combine(assets, "A/one.txt.imeta")));
            Assert.True(File.Exists(Path.Combine(artifacts, "A/one.txt.abin")));
            Assert.True(File.Exists(Path.Combine(assets, "B/two.txt.imeta")));
            Assert.True(File.Exists(Path.Combine(artifacts, "B/two.txt.abin")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Save_UpdatesGeneratedFiles_AndKeepsAssetLoadedWithSameIdentity()
    {
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();

        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relativePath = "Config/save.txt";
        WriteText(assets, relativePath, "before");

        try
        {
            var loader = new AssetLoader(assets, artifacts);
            loader.RegisterImporter(new TextAssetImporter());

            Assert.True(loader.Import(relativePath));
            Assert.NotNull(loader.Load(relativePath, typeof(TextAsset)));

            Identity assetId = loader.GetIdentity(relativePath);
            TextAsset asset = ResolveText(loader, assetId)!;
            Guid beforeId = asset.identity.persistentId;
            byte[] beforeArtifact = File.ReadAllBytes(Path.Combine(artifacts, relativePath + ".abin"));

            SetTextAssetContent(asset, "after");
            Assert.True(loader.Save(asset));

            TextAsset? saved = ResolveText(loader, assetId);
            byte[] afterArtifact = File.ReadAllBytes(Path.Combine(artifacts, relativePath + ".abin"));

            Assert.NotNull(saved);
            Assert.Equal("after", saved!.content);
            Assert.Equal(beforeId, saved.identity.persistentId);
            Assert.Equal("after", File.ReadAllText(Path.Combine(assets, relativePath)));
            Assert.NotEqual(beforeArtifact, afterArtifact);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static void SetTextAssetContent(TextAsset asset, string content)
    {
        PropertyInfo prop = typeof(TextAsset).GetProperty(
            "content",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        prop.SetValue(asset, content);
    }

    private static TextAsset? ResolveText(AssetLoader loader, Identity identity)
        => loader.Resolve(identity, typeof(TextAsset)) as TextAsset;

    private static bool IsValid(Identity identity)
        => identity.persistentId != Guid.Empty;

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
