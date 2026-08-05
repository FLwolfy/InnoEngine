using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Types;
using Inno.Core.Identity;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerTests
{
    [Fact]
    public void PublicApis_ThrowOrReturnFalse_WhenManagerIsNotInitialized()
    {
        AssetManager.Shutdown();

        Assert.Throws<InvalidOperationException>(() => AssetManager.Import("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Load<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetRef<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetRef<TextAsset>(default(Identity)));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetLoadedPaths());
        Assert.False(AssetManager.Unload("Missing/nope.txt"));
    }

    [Fact]
    public void Import_ReturnsFalse_ForMissingPath()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");

        try
        {
            AssetManager.Initialize(CreateOptions(assets, artifacts, enableWatcher: false));

            Assert.False(AssetManager.Import("Missing/nope.txt"));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Import_ReturnsFalse_WhenNoImporterMatches()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/import.txt";
        WriteSourceFile(assets, relativePath, "imported");

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

            Assert.False(AssetManager.Import(relativePath));
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
    public void Import_WritesOutputs_WithoutLoadingConcreteAsset()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/import.txt";
        WriteSourceFile(assets, relativePath, "imported");

        try
        {
            AssetManager.Initialize(CreateOptions(assets, artifacts, enableWatcher: false));

            Assert.True(AssetManager.Import(relativePath));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, relativePath + ".imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(artifacts, relativePath + ".abin")));

            AssetRef<TextAsset> assetRef = AssetManager.GetRef<TextAsset>(relativePath);

            Assert.True(assetRef.isValid);
            Assert.Null(AssetManager.Resolve(assetRef));
            Assert.Empty(AssetManager.GetLoadedPaths());

            Assert.True(AssetManager.Load<TextAsset>(relativePath));
            TextAsset? resolved = AssetManager.Resolve(assetRef);
            Assert.NotNull(resolved);
            Assert.Equal("imported", resolved!.content);
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
        WriteSourceFile(assets, relativePath, "handle");

        try
        {
            AssetManager.Initialize(CreateOptions(assets, artifacts, enableWatcher: false));

            AssetRef<TextAsset> refBeforeLoad = AssetManager.GetRef<TextAsset>(relativePath);
            Assert.True(refBeforeLoad.isValid);
            Assert.Null(AssetManager.Resolve(refBeforeLoad));

            Assert.True(AssetManager.Import(relativePath));
            Assert.True(AssetManager.Load<TextAsset>(relativePath));
            TextAsset? resolved = AssetManager.Resolve(refBeforeLoad);

            Assert.NotNull(resolved);
            Assert.Equal("handle", resolved!.content);
            Assert.Equal(refBeforeLoad.identity.persistentId, resolved.identity.persistentId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void IdentityRef_Resolve_Unload_AndUnloadAll_Work()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        WriteSourceFile(assets, "A/one.txt", "one");
        WriteSourceFile(assets, "B/two.txt", "two");

        try
        {
            AssetManager.Initialize(CreateOptions(assets, artifacts, enableWatcher: false));

            Assert.True(AssetManager.Import("A/one.txt"));
            Assert.True(AssetManager.Import("B/two.txt"));
            Assert.True(AssetManager.Load<TextAsset>("A/one.txt"));
            Assert.True(AssetManager.Load<TextAsset>("B/two.txt"));

            AssetRef<TextAsset> one = AssetManager.GetRef<TextAsset>("A/one.txt");
            AssetRef<TextAsset> two = AssetManager.GetRef<TextAsset>("B/two.txt");

            Assert.Equal(new[] { "A/one.txt", "B/two.txt" }, AssetManager.GetLoadedPaths());
            Assert.Equal("one", AssetManager.Resolve(one)!.content);
            Assert.Equal("two", AssetManager.Resolve(two)!.content);

            Assert.True(AssetManager.Unload(one));
            Assert.Null(AssetManager.Resolve(one));
            Assert.NotNull(AssetManager.Resolve(two));

            AssetManager.UnloadAll();
            Assert.Empty(AssetManager.GetLoadedPaths());
            Assert.Null(AssetManager.Resolve(two));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Rescan_ReimportsDiskOutputs_AndReloadsOnlyAlreadyLoadedAssets()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string loadedPath = "A/loaded.txt";
        string unloadedPath = "B/unloaded.txt";
        WriteSourceFile(assets, loadedPath, "loaded-v1");
        WriteSourceFile(assets, unloadedPath, "unloaded-v1");

        try
        {
            AssetManager.Initialize(CreateOptions(assets, artifacts, enableWatcher: false));

            AssetRef<TextAsset> loadedRef = AssetManager.GetRef<TextAsset>(loadedPath);
            AssetRef<TextAsset> unloadedRef = AssetManager.GetRef<TextAsset>(unloadedPath);
            Assert.True(loadedRef.isValid);
            Assert.True(unloadedRef.isValid);

            Assert.True(AssetManager.Load<TextAsset>(loadedPath));
            Assert.Equal("loaded-v1", AssetManager.Resolve(loadedRef)!.content);
            Assert.Null(AssetManager.Resolve(unloadedRef));

            byte[] unloadedArtifactBefore = System.IO.File.ReadAllBytes(Path.Combine(artifacts, unloadedPath + ".abin"));
            WriteSourceFile(assets, loadedPath, "loaded-v2");
            WriteSourceFile(assets, unloadedPath, "unloaded-v2");

            AssetManager.Rescan();

            TextAsset? loadedAfterRescan = AssetManager.Resolve(loadedRef);
            Assert.NotNull(loadedAfterRescan);
            Assert.Equal("loaded-v2", loadedAfterRescan!.content);
            Assert.Equal(loadedRef.identity.persistentId, loadedAfterRescan.identity.persistentId);
            Assert.Null(AssetManager.Resolve(unloadedRef));

            byte[] unloadedArtifactAfter = System.IO.File.ReadAllBytes(Path.Combine(artifacts, unloadedPath + ".abin"));
            Assert.NotEqual(unloadedArtifactBefore, unloadedArtifactAfter);

            Assert.True(AssetManager.Load<TextAsset>(unloadedPath));
            Assert.Equal("unloaded-v2", AssetManager.Resolve(unloadedRef)!.content);
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
        WriteSourceFile(assets, relativePath, "one");

        try
        {
            AssetManager.Initialize(CreateOptions(assets, artifacts, enableWatcher: true));
            Assert.True(AssetManager.Import(relativePath));
            Assert.True(AssetManager.Load<TextAsset>(relativePath));
            AssetRef<TextAsset> assetRef = AssetManager.GetRef<TextAsset>(relativePath);

            string graph = AssetManager.GetFileSystemTreeGraph();
            Assert.Contains("Assets/", graph);
            Assert.Contains("game.txt", graph);

            IReadOnlyList<AssetFileEntry> entries = AssetManager.GetFileSystemEntries(includeDirectories: true);
            Assert.Contains(entries, static x => x.relativePath == "Config/game.txt");
            Assert.True(AssetManager.TryGetFileSystemEntry("Config/game.txt", out AssetFileEntry entry));
            Assert.Equal(".txt", entry.extension);

            IReadOnlyList<AssetFileEntry> children = AssetManager.GetFileSystemChildren("Config");
            Assert.Single(children.Where(static x => x.relativePath == "Config/game.txt"));

            using var changed = new AutoResetEvent(false);
            AssetManager.SourceFileSystemChanged += OnChanged;
            try
            {
                System.IO.File.WriteAllText(source, "two", Encoding.UTF8);
                Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));

                bool reloaded = SpinWait.SpinUntil(
                    () => AssetManager.Resolve(assetRef)?.content == "two",
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

    private static AssetManagerOptions CreateOptions(string assets, string artifacts, bool enableWatcher)
    {
        return new AssetManagerOptions
        {
            assetRoot = assets,
            artifactRoot = artifacts,
            autoRegisterBuiltInImporters = true,
            autoRegisterImportersFromTypeCache = false,
            enableFileSystemWatcher = enableWatcher,
            fileWatcherFlushDelayMs = 20
        };
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
