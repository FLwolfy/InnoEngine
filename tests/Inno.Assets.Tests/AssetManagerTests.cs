using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.IO;
using Inno.Assets.Types;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerTests
{
    [Fact]
    public void Load_CreatesMetaAndArtifact_InExpectedLocations()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/game.txt";
        string source = Path.Combine(assets, "Config", "game.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "one", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));
            _ = AssetManager.Load<TextAsset>(relativePath);

            string metaPath = Path.Combine(assets, relativePath + ".innoasset");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");

            Assert.True(File.Exists(metaPath));
            Assert.True(File.Exists(artifactPath));
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
        File.WriteAllText(source, "one", Encoding.UTF8);

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));

            TextAsset loaded = AssetManager.Load<TextAsset>(relativePath);
            AssetRef<TextAsset> assetRef = AssetManager.GetRef<TextAsset>(relativePath);

            Assert.True(assetRef.isValid);
            Assert.Equal(loaded.identity.persistentId, assetRef.identity.persistentId);
            Assert.True(AssetManager.TryResolve(assetRef, out TextAsset resolved));
            Assert.Equal("one", resolved.content);

            Assert.True(AssetManager.TryGetLoaded(assetRef.identity, out TextAsset byIdentity));
            Assert.Same(loaded, byIdentity);

            Assert.True(AssetManager.Unload(assetRef));
            Assert.False(AssetManager.TryResolve(assetRef, out _));
            Assert.False(AssetManager.TryGetLoaded<TextAsset>(relativePath, out _));
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
        File.WriteAllText(source, "one", Encoding.UTF8);

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

            _ = AssetManager.Load<TextAsset>(relativePath);

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
                File.WriteAllText(source, "two", Encoding.UTF8);
                Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));

                bool reloaded = SpinWait.SpinUntil(
                    () => AssetManager.TryGetLoaded<TextAsset>(relativePath, out TextAsset a) && a.content == "two",
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
}
