using System;
using System.IO;
using System.Linq;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
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
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoAssetFileSystemTests", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        AssetManager.Shutdown();
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
    }

    [Fact]
    public void ImportBuildsMetadataArtifactCatalogAndFileSystemIndex()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/game.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library));

            Assert.True(AssetManager.TryGetPersistentId("Config/game.txt", out Guid persistentId));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config/game.txt.imeta")));
            Assert.NotEqual(Guid.Empty, persistentId);
            Assert.True(AssetManager.TryGetArtifact(persistentId, "runtime", out AssetArtifactInfo? artifact));
            Assert.NotNull(artifact);
            Assert.StartsWith(Path.Combine(library, "Artifacts"), artifact.absolutePath, StringComparison.Ordinal);
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
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/game.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library));
            TextAsset before = AssetManager.Load<TextAsset>("Config/game.txt");
            Guid persistentId = before.identity.persistentId;
            Write(assets, "Config/game.txt", "two");
            AssetManager.Rescan();
            TextAsset after = AssetManager.Load<TextAsset>(persistentId);

            Assert.Same(before, after);
            Assert.Equal("two", after.content);
            Assert.Equal(persistentId, after.identity.persistentId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void SourceOnlyExternalRename_PreservesIdentityMetaArtifactAndCanonicalInstance()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/old.txt", "one");
        try
        {
            AssetManagerOptions options = AssetManagerOptions.Create(assets, library);
            AssetManager.Initialize(options);
            TextAsset canonical = AssetManager.Load<TextAsset>("Config/old.txt");
            Guid id = canonical.identity.persistentId;
            Assert.True(AssetManager.TryGetInfo(id, out AssetInfo? before));
            Assert.NotNull(before);
            int ownerThread = Environment.CurrentManagedThreadId;
            int observerThread = 0;
            AssetChange? observed = null;
            AssetManager.Changed += changes =>
            {
                observerThread = Environment.CurrentManagedThreadId;
                observed = changes.changes.SingleOrDefault(change => change.kind == AssetChangeKind.Moved);
            };

            System.IO.File.Move(
                Path.Combine(assets, "Config", "old.txt"),
                Path.Combine(assets, "Config", "new.txt"));
            AssetManager.WaitForIdle();

            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config", "old.txt.imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config", "new.txt.imeta")));
            Assert.True(AssetManager.TryGetInfo("Config/new.txt", out AssetInfo? after));
            Assert.NotNull(after);
            Assert.Equal(id, after.persistentId);
            Assert.Equal(before.artifactKey, after.artifactKey);
            Assert.Equal("Config/new.txt", canonical.sourcePath);
            Assert.Same(canonical, AssetManager.Load<TextAsset>(id));
            Assert.Equal(ownerThread, observerThread);
            Assert.True(observed.HasValue);
            Assert.Equal(id, observed.Value.persistentId);
            Assert.Equal("Config/old.txt", observed.Value.oldRelativePath);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ExternalDeleteAndRestore_RetainsTombstoneIdentityAndRestoresInPlace()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/recover.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library));
            TextAsset canonical = AssetManager.Load<TextAsset>("Config/recover.txt");
            Guid id = canonical.identity.persistentId;

            System.IO.File.Delete(Path.Combine(assets, "Config", "recover.txt"));
            AssetManager.WaitForIdle();
            Assert.True(canonical.isMissing);
            Assert.True(AssetManager.TryGetInfo(id, out AssetInfo? missing));
            Assert.Equal(AssetImportStatus.Missing, missing!.status);

            Write(assets, "Config/recover.txt", "two");
            AssetManager.WaitForIdle();
            Assert.False(canonical.isMissing);
            Assert.Equal("two", canonical.content);
            Assert.Equal(id, canonical.identity.persistentId);
            Assert.Same(canonical, AssetManager.Load<TextAsset>(id));
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
