using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Identity;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerTests
{
    private const AssetLoadMode C_ALL_LOAD_SOURCES =
        AssetLoadMode.MemoryCache | AssetLoadMode.DiskCache | AssetLoadMode.DiskRaw;

    [Fact]
    public void PublicApis_ThrowOrReturnFalse_WhenManagerIsNotInitialized()
    {
        AssetManager.Shutdown();

        Assert.Throws<InvalidOperationException>(() => AssetManager.Import<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Load<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.LoadRef<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetRef<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetRef<TextAsset>(default(Identity)));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetLoadedPaths());
        Assert.False(AssetManager.Unload("Missing/nope.txt"));
    }

    [Fact]
    public void Load_CreatesMetaAndArtifact_InExpectedLocations()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/game.txt";
        string source = Path.Combine(assets, "Config", "game.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "one", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));
            _ = AssetManager.Load<TextAsset>(relativePath)!;

            string metaPath = Path.Combine(assets, relativePath + ".imeta");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");

            Assert.True(System.IO.File.Exists(metaPath));
            Assert.True(System.IO.File.Exists(artifactPath));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Import_LoadsFromDiskRaw_WritesOutputs_AndCachesImportedAsset()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/import.txt";
        string source = Path.Combine(assets, "Config", "import.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "imported", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = false,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = false,
                fileWatcherFlushDelayMs = 20
            });
            AssetManager.RegisterBuiltInImporters();

            string metaPath = Path.Combine(assets, relativePath + ".imeta");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");

            Assert.False(AssetManager.Import<TextAsset>("Missing/nope.txt"));
            Assert.True(AssetManager.Import<TextAsset>(relativePath));
            Assert.True(System.IO.File.Exists(metaPath));
            Assert.True(System.IO.File.Exists(artifactPath));

            TextAsset? memoryAsset = AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache);
            Assert.NotNull(memoryAsset);
            Assert.Equal("imported", memoryAsset!.content);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Import_WithAssetObject_ImportsConcreteAssetTypeAndCachesIt()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/base-type.txt";
        string source = Path.Combine(assets, "Config", "base-type.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "base", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = false,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = false,
                fileWatcherFlushDelayMs = 20
            });
            AssetManager.RegisterBuiltInImporters();

            Assert.True(AssetManager.Import<AssetObject>(relativePath));

            AssetObject? baseAsset = AssetManager.Load<AssetObject>(relativePath, AssetLoadMode.MemoryCache);
            TextAsset? textAsset = AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache);

            Assert.NotNull(baseAsset);
            Assert.NotNull(textAsset);
            Assert.Same(baseAsset, textAsset);
            Assert.Equal("base", textAsset!.content);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Import_ReturnsFalseAndDoesNotWriteOutputs_WhenRequestedTypeDoesNotMatchImporter()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/type.txt";
        string source = Path.Combine(assets, "Config", "type.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "plain text", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = false,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = false,
                fileWatcherFlushDelayMs = 20
            });
            AssetManager.RegisterBuiltInImporters();

            Assert.False(AssetManager.Import<TextureAsset>(relativePath));
            Assert.False(System.IO.File.Exists(Path.Combine(assets, relativePath + ".imeta")));
            Assert.False(System.IO.File.Exists(Path.Combine(artifacts, relativePath + ".abin")));
            Assert.Null(AssetManager.Load<TextureAsset>(relativePath, AssetLoadMode.MemoryCache));
            Assert.Null(AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void LoadModes_ControlMemoryDiskCacheAndDiskRawSources()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/modes.txt";
        string source = Path.Combine(assets, "Config", "modes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "v1", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = true,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = false,
                fileWatcherFlushDelayMs = 20
            });

            Assert.Null(AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.None));
            Assert.Null(AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache));

            TextAsset fromDiskCache = AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache)!;
            Assert.Equal("v1", fromDiskCache.content);
            Assert.Same(fromDiskCache, AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache));

            System.IO.File.WriteAllText(source, "v2", Encoding.UTF8);
            Assert.Same(fromDiskCache, AssetManager.Load<TextAsset>(relativePath, C_ALL_LOAD_SOURCES));

            Assert.True(AssetManager.Unload(relativePath));
            Assert.Null(AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache));
            Assert.Null(AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache));

            TextAsset fromRaw = AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.DiskRaw)!;
            Assert.Equal("v2", fromRaw.content);
            Assert.NotSame(fromDiskCache, fromRaw);
            Assert.Equal(fromDiskCache.identity.persistentId, fromRaw.identity.persistentId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void IdentityRef_Resolve_AndUnload_Work()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/game.txt";
        string source = Path.Combine(assets, "Config", "game.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "one", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));

            TextAsset loaded = AssetManager.Load<TextAsset>(relativePath)!;
            AssetRef<TextAsset> assetRef = AssetManager.LoadRef<TextAsset>(relativePath);

            Assert.True(assetRef.isValid);
            Assert.Equal(loaded.identity.persistentId, assetRef.identity.persistentId);
            TextAsset? resolved = AssetManager.Resolve(assetRef);
            Assert.NotNull(resolved);
            Assert.Equal("one", resolved.content);

            TextAsset? byIdentity = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(assetRef.identity));
            Assert.NotNull(byIdentity);
            Assert.Same(loaded, byIdentity);

            Assert.True(AssetManager.Unload(assetRef));
            Assert.Null(AssetManager.Resolve(assetRef));
            Assert.Null(AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void GetRef_ReturnsPersistentHandleWithoutLoading_WhenMetadataExists()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/handle.txt";
        string source = Path.Combine(assets, "Config", "handle.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "handle", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = true,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = false,
                fileWatcherFlushDelayMs = 20
            });

            AssetRef<TextAsset> refBeforeLoad = AssetManager.GetRef<TextAsset>(relativePath);
            Assert.True(refBeforeLoad.isValid);
            Assert.Null(AssetManager.Resolve(refBeforeLoad));

            TextAsset loaded = AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.DiskCache)!;
            Assert.Equal(refBeforeLoad.identity.persistentId, loaded.identity.persistentId);
            Assert.Same(loaded, AssetManager.Resolve(refBeforeLoad));

            Assert.True(AssetManager.Unload(refBeforeLoad));
            Assert.Null(AssetManager.Resolve(refBeforeLoad));
            Assert.True(AssetManager.GetRef<TextAsset>(loaded.identity).isValid);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void LoadAndLoadRef_ReturnNullOrInvalid_ForMissingPathModeNoneAndTypeMismatch()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/type.txt";
        string source = Path.Combine(assets, "Config", "type.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "plain text", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = false,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = false,
                fileWatcherFlushDelayMs = 20
            });
            AssetManager.RegisterBuiltInImporters();

            Assert.Null(AssetManager.Load<TextAsset>("Missing/nope.txt"));
            Assert.False(AssetManager.LoadRef<TextAsset>("Missing/nope.txt").isValid);
            Assert.Null(AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.None));
            Assert.False(AssetManager.LoadRef<TextAsset>(relativePath, AssetLoadMode.None).isValid);
            Assert.Null(AssetManager.Load<TextureAsset>(relativePath, AssetLoadMode.DiskRaw));
            Assert.False(AssetManager.LoadRef<TextureAsset>(relativePath, AssetLoadMode.DiskRaw).isValid);
            Assert.False(System.IO.File.Exists(Path.Combine(assets, relativePath + ".imeta")));
            Assert.False(System.IO.File.Exists(Path.Combine(artifacts, relativePath + ".abin")));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void UnloadAll_ClearsLoadedPathsAndResolvedReferences()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        WriteSourceFile(assets, "A/one.txt", "one");
        WriteSourceFile(assets, "B/two.txt", "two");

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = true,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = false,
                fileWatcherFlushDelayMs = 20
            });

            AssetRef<TextAsset> one = AssetManager.LoadRef<TextAsset>("A/one.txt");
            AssetRef<TextAsset> two = AssetManager.LoadRef<TextAsset>("B/two.txt");

            Assert.Equal(new[] { "A/one.txt", "B/two.txt" }, AssetManager.GetLoadedPaths());
            Assert.NotNull(AssetManager.Resolve(one));
            Assert.NotNull(AssetManager.Resolve(two));

            AssetManager.UnloadAll();

            Assert.Empty(AssetManager.GetLoadedPaths());
            Assert.Null(AssetManager.Resolve(one));
            Assert.Null(AssetManager.Resolve(two));
            Assert.False(AssetManager.Unload(one));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void FileSystemApi_AndWatcherReimport_Work()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/game.txt";
        string source = Path.Combine(assets, "Config", "game.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        System.IO.File.WriteAllText(source, "one", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = true,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = true,
                fileWatcherFlushDelayMs = 20
            });

            _ = AssetManager.Load<TextAsset>(relativePath)!;

            string graph = AssetManager.GetFileSystemTreeGraph();
            Assert.Contains("Assets/", graph);
            Assert.Contains("game.txt", graph);

            var entries = AssetManager.GetFileSystemEntries(includeDirectories: true);
            Assert.Contains(entries, static x => x.relativePath == "Config/game.txt");
            Assert.True(AssetManager.TryGetFileSystemEntry("Config/game.txt", out AssetFileEntry entry));
            Assert.Equal(".txt", entry.extension);

            var children = AssetManager.GetFileSystemChildren("Config");
            Assert.Single(children.Where(static x => x.relativePath == "Config/game.txt"));

            using var changed = new AutoResetEvent(false);
            AssetManager.SourceFileSystemChanged += OnChanged;
            try
            {
                System.IO.File.WriteAllText(source, "two", Encoding.UTF8);
                Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));

                bool reloaded = SpinWait.SpinUntil(
                    () => AssetManager.Load<TextAsset>(relativePath, AssetLoadMode.MemoryCache)?.content == "two",
                    TimeSpan.FromSeconds(2));
                Assert.True(reloaded);
            }
            finally
            {
                AssetManager.SourceFileSystemChanged -= OnChanged;
            }

            void OnChanged(IReadOnlyList<AssetChangedEvent> _)
                => changed.Set();
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static void WriteSourceFile(string assetsRoot, string relativePath, string content)
    {
        string path = Path.Combine(assetsRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, Encoding.UTF8);
    }
}
