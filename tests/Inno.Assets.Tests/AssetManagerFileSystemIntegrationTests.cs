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
            Assert.Equal("Config/new.txt", canonical.assetPath.ToString());
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
    public void Move_PreservesIdentityMetadataCanonicalInstanceAndPublishesOneChange()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/old.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library) with
            {
                enableFileSystemWatcher = false
            });
            TextAsset canonical = AssetManager.Load<TextAsset>("Config/old.txt");
            Guid persistentId = canonical.identity.persistentId;
            AssetChange? observed = null;
            AssetManager.Changed += changes =>
                observed = Assert.Single(changes.changes);

            AssetManager.Move("Config/old.txt", "Config/renamed.txt");

            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config/old.txt")));
            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config/old.txt.imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config/renamed.txt")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config/renamed.txt.imeta")));
            Assert.True(AssetManager.TryGetPersistentId("Config/renamed.txt", out Guid movedId));
            Assert.Equal(persistentId, movedId);
            Assert.Same(canonical, AssetManager.Load<TextAsset>(movedId));
            Assert.Equal("Config/renamed.txt", canonical.assetPath.ToString());
            Assert.NotNull(observed);
            Assert.Equal(AssetChangeKind.Moved, observed.Value.kind);
            Assert.Equal("Config/old.txt", observed.Value.oldRelativePath);
            Assert.Equal("Config/renamed.txt", observed.Value.relativePath);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void MoveDirectory_PreservesEveryPersistentIdentityAndMovesFolderMetadata()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Old/Sub/value.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library) with
            {
                enableFileSystemWatcher = false
            });
            Assert.True(AssetManager.TryGetPersistentId("Old", out Guid folderId));
            Assert.True(AssetManager.TryGetPersistentId("Old/Sub/value.txt", out Guid assetId));

            AssetManager.Move("Old", "New");

            Assert.False(Directory.Exists(Path.Combine(assets, "Old")));
            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Old.imeta")));
            Assert.True(Directory.Exists(Path.Combine(assets, "New")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "New.imeta")));
            Assert.True(AssetManager.TryGetPersistentId("New", out Guid movedFolderId));
            Assert.True(AssetManager.TryGetPersistentId("New/Sub/value.txt", out Guid movedAssetId));
            Assert.Equal(folderId, movedFolderId);
            Assert.Equal(assetId, movedAssetId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void MoveAcrossSupportedExtensions_PreservesSourceIdentityAndReplacesRuntimeType()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/value.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library) with
            {
                enableFileSystemWatcher = false
            });
            TextAsset oldAsset = AssetManager.Load<TextAsset>("Config/value.txt");
            Guid id = oldAsset.identity.persistentId;

            AssetManager.Move("Config/value.txt", "Config/value.bin");

            Assert.True(oldAsset.isMissing);
            Assert.True(AssetManager.TryGetPersistentId("Config/value.bin", out Guid movedId));
            Assert.Equal(id, movedId);
            BinaryAsset replacement = AssetManager.Load<BinaryAsset>(id);
            Assert.Equal(3, replacement.byteLength);
            Assert.Equal(new byte[] { (byte)'o', (byte)'n', (byte)'e' }, replacement.runtimePayload.ToArray());
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ExternalAddAndModify_CreateTrackingThenRefreshInPlace()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Directory.CreateDirectory(assets);
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library));
            Write(assets, "Config/live.txt", "one");
            AssetManager.WaitForIdle();
            TextAsset canonical = AssetManager.Load<TextAsset>("Config/live.txt");
            Guid id = canonical.identity.persistentId;
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config", "live.txt.imeta")));

            Write(assets, "Config/live.txt", "two");
            AssetManager.WaitForIdle();

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

    [Fact]
    public void ExternalDeleteCreateWithinWatcherWindow_IsOneModificationWithoutIdentityLoss()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/atomic.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library) with
            {
                fileWatcherFlushDelayMs = 100
            });
            TextAsset canonical = AssetManager.Load<TextAsset>("Config/atomic.txt");
            Guid id = canonical.identity.persistentId;
            var observed = new System.Collections.Generic.List<AssetChange>();
            AssetManager.Changed += changes => observed.AddRange(changes.changes);
            string path = Path.Combine(assets, "Config", "atomic.txt");

            System.IO.File.Delete(path);
            Write(assets, "Config/atomic.txt", "two");
            AssetManager.WaitForIdle();

            Assert.False(canonical.isMissing);
            Assert.Equal("two", canonical.content);
            Assert.Equal(id, canonical.identity.persistentId);
            Assert.True(System.IO.File.Exists(path + ".imeta"));
            Assert.DoesNotContain(observed, change =>
                change.kind is AssetChangeKind.Missing or AssetChangeKind.Removed);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void CreateDirectory_CreatesTrackedFolderWithoutAnArtifact()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Directory.CreateDirectory(Path.Combine(assets, "Config"));
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library) with
            {
                enableFileSystemWatcher = false
            });
            AssetChange? observed = null;
            AssetManager.Changed += changes => observed = Assert.Single(changes.changes);

            AssetManager.CreateDirectory("Config/New Folder");

            Assert.True(Directory.Exists(Path.Combine(assets, "Config", "New Folder")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config", "New Folder.imeta")));
            Assert.True(AssetManager.TryGetInfo("Config/New Folder", out AssetInfo? info));
            Assert.NotNull(info);
            Assert.Equal(AssetSourceKind.Directory, info.sourceKind);
            Assert.True(info.artifactKey.isEmpty);
            Assert.Equal(AssetChangeKind.Added, observed?.kind);
            Assert.Equal(info.persistentId, observed?.persistentId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ExternalPairedRename_PreservesIdentityWithoutCreatingAnotherMetadataFile()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/old.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library));
            Assert.True(AssetManager.TryGetPersistentId("Config/old.txt", out Guid id));
            System.IO.File.Move(
                Path.Combine(assets, "Config", "old.txt"),
                Path.Combine(assets, "Config", "new.txt"));
            System.IO.File.Move(
                Path.Combine(assets, "Config", "old.txt.imeta"),
                Path.Combine(assets, "Config", "new.txt.imeta"));

            AssetManager.WaitForIdle();

            Assert.True(AssetManager.TryGetPersistentId("Config/new.txt", out Guid movedId));
            Assert.Equal(id, movedId);
            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config", "old.txt.imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "Config", "new.txt.imeta")));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ExternalDirectoryRename_PreservesFolderAndDescendantIdentities()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Old/Sub/value.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library));
            Assert.True(AssetManager.TryGetPersistentId("Old", out Guid folderId));
            Assert.True(AssetManager.TryGetPersistentId("Old/Sub/value.txt", out Guid assetId));

            Directory.Move(Path.Combine(assets, "Old"), Path.Combine(assets, "New"));
            AssetManager.WaitForIdle();

            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Old.imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, "New.imeta")));
            Assert.True(AssetManager.TryGetPersistentId("New", out Guid movedFolderId));
            Assert.True(AssetManager.TryGetPersistentId("New/Sub/value.txt", out Guid movedAssetId));
            Assert.Equal(folderId, movedFolderId);
            Assert.Equal(assetId, movedAssetId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ExternalSourceAndMetadataDelete_LeavesOnlyLibraryTombstone()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/value.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library));
            Assert.True(AssetManager.TryGetPersistentId("Config/value.txt", out Guid id));
            System.IO.File.Delete(Path.Combine(assets, "Config", "value.txt"));
            System.IO.File.Delete(Path.Combine(assets, "Config", "value.txt.imeta"));

            AssetManager.WaitForIdle();

            Assert.False(AssetManager.TryGetInfo("Config/value.txt", out _));
            Assert.True(AssetManager.TryGetInfo(id, out AssetInfo? tombstone));
            Assert.NotNull(tombstone);
            Assert.Equal(AssetImportStatus.Missing, tombstone.status);
            Assert.True(tombstone.artifactKey.isEmpty);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void ExternalSourceOnlyDelete_RemovesMetadataAndRecreatedSourceGetsNewIdentity()
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
            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config", "recover.txt.imeta")));
            Assert.False(AssetManager.TryGetInfo("Config/recover.txt", out _));
            Assert.True(AssetManager.TryGetInfo(id, out AssetInfo? missing));
            Assert.Equal(AssetImportStatus.Missing, missing!.status);
            Assert.True(missing.artifactKey.isEmpty);
            Assert.True(missing.lastSuccessfulArtifactKey.isEmpty);

            Write(assets, "Config/recover.txt", "two");
            AssetManager.WaitForIdle();
            TextAsset replacement = AssetManager.Load<TextAsset>("Config/recover.txt");
            Assert.True(canonical.isMissing);
            Assert.False(replacement.isMissing);
            Assert.Equal("two", replacement.content);
            Assert.NotEqual(id, replacement.identity.persistentId);
            Assert.Same(canonical, AssetManager.Load<TextAsset>(id));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Delete_RemovesSourceMetadataAndPathWhileRetainingIdTombstone()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/delete.txt", "one");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library) with
            {
                enableFileSystemWatcher = false,
                cacheOptions = new AssetCacheOptions
                {
                    maximumSizeBytes = 0,
                    garbageCollectionGracePeriod = TimeSpan.Zero
                }
            });
            TextAsset canonical = AssetManager.Load<TextAsset>("Config/delete.txt");
            Guid id = canonical.identity.persistentId;
            Assert.True(AssetManager.TryGetArtifact(id, "runtime", out AssetArtifactInfo? artifact));
            Assert.NotNull(artifact);
            AssetChange? observed = null;
            AssetManager.Changed += changes => observed = Assert.Single(changes.changes);

            AssetManager.Delete("Config/delete.txt");

            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config", "delete.txt")));
            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config", "delete.txt.imeta")));
            Assert.False(AssetManager.TryGetInfo("Config/delete.txt", out _));
            Assert.True(AssetManager.TryGetInfo(id, out AssetInfo? tombstone));
            Assert.NotNull(tombstone);
            Assert.Equal(AssetImportStatus.Missing, tombstone.status);
            Assert.True(tombstone.artifactKey.isEmpty);
            Assert.True(tombstone.lastSuccessfulArtifactKey.isEmpty);
            Assert.True(canonical.isMissing);
            Assert.False(System.IO.File.Exists(artifact.absolutePath));
            Assert.Equal(AssetChangeKind.Removed, observed?.kind);
            Assert.Equal(id, observed?.persistentId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void DeleteDirectory_RemovesMetadataAndTombstonesTheEntireSubtree()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string library = Path.Combine(root, "Library");
        Write(assets, "Config/Sub/first.txt", "one");
        Write(assets, "Config/Sub/second.txt", "two");
        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, library) with
            {
                enableFileSystemWatcher = false
            });
            Assert.True(AssetManager.TryGetPersistentId("Config/Sub", out Guid directoryId));
            Assert.True(AssetManager.TryGetPersistentId("Config/Sub/first.txt", out Guid firstId));
            Assert.True(AssetManager.TryGetPersistentId("Config/Sub/second.txt", out Guid secondId));
            AssetChangeSet? observed = null;
            AssetManager.Changed += changes => observed = changes;

            AssetManager.Delete("Config/Sub");

            Assert.False(Directory.Exists(Path.Combine(assets, "Config", "Sub")));
            Assert.False(System.IO.File.Exists(Path.Combine(assets, "Config", "Sub.imeta")));
            Assert.False(AssetManager.TryGetInfo("Config/Sub", out _));
            Assert.True(AssetManager.TryGetInfo(directoryId, out AssetInfo? directory));
            Assert.True(AssetManager.TryGetInfo(firstId, out AssetInfo? first));
            Assert.True(AssetManager.TryGetInfo(secondId, out AssetInfo? second));
            Assert.Equal(AssetImportStatus.Missing, directory!.status);
            Assert.Equal(AssetImportStatus.Missing, first!.status);
            Assert.Equal(AssetImportStatus.Missing, second!.status);
            Assert.NotNull(observed);
            Assert.All(observed.changes, change => Assert.Equal(AssetChangeKind.Removed, change.kind));
            Assert.Contains(observed.changes, change => change.persistentId == directoryId);
            Assert.Contains(observed.changes, change => change.persistentId == firstId);
            Assert.Contains(observed.changes, change => change.persistentId == secondId);
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
