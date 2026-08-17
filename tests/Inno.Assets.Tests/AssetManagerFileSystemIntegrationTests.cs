using System;
using System.IO;
using System.Text;

using Inno.Assets.File;
using Inno.Assets.Types;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerFileSystemIntegrationTests : IDisposable
{
    public AssetManagerFileSystemIntegrationTests()
    {
        IdentityManager.Initialize();
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        AssetManager.Shutdown();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        IdentityManager.Shutdown();
    }

    [Fact]
    public void ImportBuildsMetadataArtifactCatalogAndFileSystemIndex()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        Write(assets, "Config/game.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));

            Assert.True(AssetManager.Import("Config/game.txt"));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config/game.txt.imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(artifacts, "Config/game.txt.abin")));
            Assert.True(AssetManager.TryGetPersistentId("Config/game.txt", out Guid persistentId));
            Assert.NotEqual(Guid.Empty, persistentId);
            Assert.True(AssetManager.TryGetAssetType("Config/game.txt", out Type? assetType));
            Assert.Equal(typeof(TextAsset), assetType);
            Assert.True(AssetManager.TryGetFileSystemEntry("Config/game.txt", out AssetFileEntry entry));
            Assert.Equal(".txt", entry.extension);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void RescanReimportsChangedSourceAndPreservesPersistentIdentity()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        Write(assets, "Config/game.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));
            Assert.True(AssetManager.Import("Config/game.txt"));
            TextAsset before = AssetManager.Load<TextAsset>("Config/game.txt");
            Guid persistentId = before.identity.persistentId;
            Assert.True(AssetManager.Unload(before));

            Write(assets, "Config/game.txt", "two");
            AssetManager.Rescan();
            TextAsset after = AssetManager.Load<TextAsset>(persistentId);

            Assert.Equal("two", after.content);
            Assert.Equal(persistentId, after.identity.persistentId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetFileSystemTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string assetRoot, string relativePath, string content)
    {
        string path = Path.Combine(assetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
