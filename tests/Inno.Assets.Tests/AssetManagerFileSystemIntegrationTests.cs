using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.IO;
using Inno.Assets.Types;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerFileSystemIntegrationTests
{
    [Fact]
    public void Import_WritesMetadataAndArtifact_AndFilesystemIndexesMetadataFiles()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/game.txt";
        string sourcePath = Path.Combine(assets, "Config", "game.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "one", NoBomUtf8());

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

            TextAsset loaded = AssetManager.Load<TextAsset>(relativePath);
            string metadataPath = Path.Combine(assets, relativePath + ".innoasset");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");

            Assert.True(File.Exists(metadataPath));
            Assert.True(File.Exists(artifactPath));
            Assert.True(SpinWait.SpinUntil(
                () => AssetManager.TryGetFileSystemEntry("Config/game.txt.innoasset", out _),
                TimeSpan.FromSeconds(2)));
            Assert.True(AssetManager.TryGetFileSystemEntry("Config/game.txt.innoasset", out AssetFileEntry entry));
            Assert.Equal(".innoasset", entry.extension);

            AssetMetaProbe meta = ReadMeta(metadataPath);
            Assert.Equal(relativePath, meta.relativePath);
            Assert.Equal(loaded.identity.persistentId, meta.persistentId);
            Assert.Equal("one", loaded.content);
            Assert.False(string.IsNullOrWhiteSpace(meta.sourceHash));
            Assert.False(string.IsNullOrWhiteSpace(meta.importerId));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Save_UpdatesSource_MetadataHash_AndArtifactBytes()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/save.txt";
        string sourcePath = Path.Combine(assets, "Config", "save.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "before", NoBomUtf8());

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));

            TextAsset asset = AssetManager.Load<TextAsset>(relativePath);
            string metadataPath = Path.Combine(assets, relativePath + ".innoasset");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");
            AssetMetaProbe beforeMeta = ReadMeta(metadataPath);
            byte[] beforeArtifact = File.ReadAllBytes(artifactPath);

            SetTextAssetContent(asset, "after-save");
            Assert.True(AssetManager.Save(asset));

            string savedSource = File.ReadAllText(sourcePath);
            Assert.Equal("after-save", savedSource);

            byte[] afterArtifact = File.ReadAllBytes(artifactPath);
            AssetMetaProbe afterMeta = ReadMeta(metadataPath);
            Assert.NotEqual(beforeMeta.sourceHash, afterMeta.sourceHash);
            Assert.NotEqual(beforeArtifact, afterArtifact);
            Assert.Equal("after-save", Encoding.UTF8.GetString(afterArtifact));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_SourceModifyAndDelete_ReimportsThenUnloadsLoadedAsset()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Library", "Artifacts");
        string relativePath = "Config/hot.txt";
        string sourcePath = Path.Combine(assets, "Config", "hot.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "v1", NoBomUtf8());

        using var changed = new AutoResetEvent(false);
        void OnChanged(IReadOnlyList<AssetChangedEvent> _)
            => changed.Set();

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
            AssetManager.SourceFileSystemChanged += OnChanged;

            AssetRef<TextAsset> assetRef = AssetManager.GetRef<TextAsset>(relativePath);
            Assert.True(AssetManager.TryResolve(assetRef, out TextAsset initial));
            Assert.Equal("v1", initial.content);

            File.WriteAllText(sourcePath, "v2", NoBomUtf8());
            Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => AssetManager.TryResolve(assetRef, out TextAsset hot) && hot.content == "v2",
                TimeSpan.FromSeconds(2)));

            File.Delete(sourcePath);
            Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => !AssetManager.TryResolve(assetRef, out _),
                TimeSpan.FromSeconds(2)));

            string metaPath = Path.Combine(assets, relativePath + ".innoasset");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");
            Assert.True(SpinWait.SpinUntil(() => !File.Exists(metaPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !File.Exists(artifactPath), TimeSpan.FromSeconds(2)));
        }
        finally
        {
            AssetManager.SourceFileSystemChanged -= OnChanged;
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_Rename_MovesGeneratedFilesWithoutResidue()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string oldRelativePath = "Config/rename_old.txt";
        string newRelativePath = "Moved/rename_new.txt";
        string oldSourcePath = Path.Combine(assets, "Config", "rename_old.txt");
        string newSourcePath = Path.Combine(assets, "Moved", "rename_new.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(oldSourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newSourcePath)!);
        File.WriteAllText(oldSourcePath, "rename-me", NoBomUtf8());

        using var changed = new AutoResetEvent(false);
        void OnChanged(IReadOnlyList<AssetChangedEvent> _)
            => changed.Set();

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
            AssetManager.SourceFileSystemChanged += OnChanged;

            AssetRef<TextAsset> assetRef = AssetManager.GetRef<TextAsset>(oldRelativePath);
            Assert.True(AssetManager.TryResolve(assetRef, out TextAsset initial));
            Assert.Equal("rename-me", initial.content);

            string oldMetaPath = Path.Combine(assets, oldRelativePath + ".innoasset");
            string newMetaPath = Path.Combine(assets, newRelativePath + ".innoasset");
            string oldArtifactPath = Path.Combine(artifacts, oldRelativePath + ".abin");
            string newArtifactPath = Path.Combine(artifacts, newRelativePath + ".abin");
            Assert.True(File.Exists(oldMetaPath));
            Assert.True(File.Exists(oldArtifactPath));

            File.Move(oldSourcePath, newSourcePath);
            Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));

            Assert.True(SpinWait.SpinUntil(() => File.Exists(newMetaPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(newArtifactPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !File.Exists(oldMetaPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !File.Exists(oldArtifactPath), TimeSpan.FromSeconds(2)));

            TextAsset loadedByNewPath = AssetManager.Load<TextAsset>(newRelativePath);
            Assert.Equal("rename-me", loadedByNewPath.content);
            Assert.True(SpinWait.SpinUntil(
                () => !AssetManager.TryGetLoaded<TextAsset>(oldRelativePath, out _),
                TimeSpan.FromSeconds(2)));

            AssetRef<TextAsset> refreshedRef = AssetManager.GetRef<TextAsset>(newRelativePath);
            Assert.True(AssetManager.TryResolve(refreshedRef, out TextAsset resolvedByNewRef));
            Assert.Equal("rename-me", resolvedByNewRef.content);
        }
        finally
        {
            AssetManager.SourceFileSystemChanged -= OnChanged;
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    private static AssetMetaProbe ReadMeta(string metadataPath)
    {
        byte[] bytes = File.ReadAllBytes(metadataPath);
        SerializingState state = SerializingState.Deserialize(bytes);
        var meta = new AssetMetaProbe();
        ((ISerializable)meta).RestoreState(state);
        return meta;
    }

    private static void SetTextAssetContent(TextAsset asset, string content)
    {
        PropertyInfo prop = typeof(TextAsset).GetProperty(
            "content",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        prop.SetValue(asset, content);
    }

    private static Encoding NoBomUtf8()
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoAssetsFsIntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class AssetMetaProbe : ISerializable
    {
        [SerializableProperty] public Guid persistentId { get; set; }
        [SerializableProperty] public string relativePath { get; set; } = string.Empty;
        [SerializableProperty] public string sourceHash { get; set; } = string.Empty;
        [SerializableProperty] public string importerId { get; set; } = string.Empty;
        [SerializableProperty] public int importerVersion { get; set; }
        [SerializableProperty] public Guid assetTypeStableId { get; set; }
        [SerializableProperty] public int assetRuntimeTypeId { get; set; }
        [SerializableProperty] public byte[] assetStateBytes { get; set; } = [];
        [SerializableProperty] public string[] dependencies { get; set; } = [];
    }
}
