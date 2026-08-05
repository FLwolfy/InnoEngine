using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using Inno.Assets.Core;
using Inno.Assets.File;
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
        System.IO.File.WriteAllText(sourcePath, "one", NoBomUtf8());

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

            Assert.True(AssetManager.Import(relativePath));
            Assert.True(AssetManager.Load<TextAsset>(relativePath));
            TextAsset loaded = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(relativePath))!;
            string metadataPath = Path.Combine(assets, relativePath + ".imeta");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");

            Assert.True(System.IO.File.Exists(metadataPath));
            Assert.True(System.IO.File.Exists(artifactPath));
            Assert.True(SpinWait.SpinUntil(
                () => AssetManager.TryGetFileSystemEntry("Config/game.txt.imeta", out _),
                TimeSpan.FromSeconds(2)));
            Assert.True(AssetManager.TryGetFileSystemEntry("Config/game.txt.imeta", out AssetFileEntry entry));
            Assert.Equal(".imeta", entry.extension);

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
        System.IO.File.WriteAllText(sourcePath, "before", NoBomUtf8());

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));

            Assert.True(AssetManager.Import(relativePath));
            Assert.True(AssetManager.Load<TextAsset>(relativePath));
            TextAsset asset = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(relativePath))!;
            Guid beforePersistentId = asset.identity.persistentId;
            string metadataPath = Path.Combine(assets, relativePath + ".imeta");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");
            AssetMetaProbe beforeMeta = ReadMeta(metadataPath);
            byte[] beforeArtifact = System.IO.File.ReadAllBytes(artifactPath);

            SetTextAssetContent(asset, "after-save");
            Assert.True(AssetManager.Save(asset));

            string savedSource = System.IO.File.ReadAllText(sourcePath);
            Assert.Equal("after-save", savedSource);

            byte[] afterArtifact = System.IO.File.ReadAllBytes(artifactPath);
            AssetMetaProbe afterMeta = ReadMeta(metadataPath);
            TextAsset? memoryAfterSave = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(relativePath));

            Assert.NotNull(memoryAfterSave);
            Assert.Equal("after-save", memoryAfterSave!.content);
            Assert.Equal(beforePersistentId, memoryAfterSave.identity.persistentId);
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
        System.IO.File.WriteAllText(sourcePath, "v1", NoBomUtf8());

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

            Assert.True(AssetManager.Import(relativePath));
            Assert.True(AssetManager.Load<TextAsset>(relativePath));
            AssetRef<TextAsset> assetRef = AssetManager.GetRef<TextAsset>(relativePath);
            TextAsset? initial = AssetManager.Resolve(assetRef);
            Assert.NotNull(initial);
            Assert.Equal("v1", initial.content);

            System.IO.File.WriteAllText(sourcePath, "v2", NoBomUtf8());
            Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => AssetManager.Resolve(assetRef)?.content == "v2",
                TimeSpan.FromSeconds(2)));

            System.IO.File.Delete(sourcePath);
            Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => AssetManager.Resolve(assetRef) is null,
                TimeSpan.FromSeconds(2)));

            string metaPath = Path.Combine(assets, relativePath + ".imeta");
            string artifactPath = Path.Combine(artifacts, relativePath + ".abin");
            Assert.True(SpinWait.SpinUntil(() => !System.IO.File.Exists(metaPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !System.IO.File.Exists(artifactPath), TimeSpan.FromSeconds(2)));
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
        System.IO.File.WriteAllText(oldSourcePath, "rename-me", NoBomUtf8());

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

            Assert.True(AssetManager.Import(oldRelativePath));
            Assert.True(AssetManager.Load<TextAsset>(oldRelativePath));
            AssetRef<TextAsset> assetRef = AssetManager.GetRef<TextAsset>(oldRelativePath);
            TextAsset? initial = AssetManager.Resolve(assetRef);
            Assert.NotNull(initial);
            Assert.Equal("rename-me", initial.content);

            string oldMetaPath = Path.Combine(assets, oldRelativePath + ".imeta");
            string newMetaPath = Path.Combine(assets, newRelativePath + ".imeta");
            string oldArtifactPath = Path.Combine(artifacts, oldRelativePath + ".abin");
            string newArtifactPath = Path.Combine(artifacts, newRelativePath + ".abin");
            Assert.True(System.IO.File.Exists(oldMetaPath));
            Assert.True(System.IO.File.Exists(oldArtifactPath));

            System.IO.File.Move(oldSourcePath, newSourcePath);
            Assert.True(changed.WaitOne(TimeSpan.FromSeconds(3)));

            Assert.True(SpinWait.SpinUntil(() => System.IO.File.Exists(newMetaPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => System.IO.File.Exists(newArtifactPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !System.IO.File.Exists(oldMetaPath), TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !System.IO.File.Exists(oldArtifactPath), TimeSpan.FromSeconds(2)));

            Assert.True(SpinWait.SpinUntil(
                () => !AssetManager.GetLoadedPaths().Contains(oldRelativePath),
                TimeSpan.FromSeconds(2)));

            AssetRef<TextAsset> refreshedRef = AssetManager.GetRef<TextAsset>(newRelativePath);
            TextAsset? resolvedByNewRef = AssetManager.Resolve(refreshedRef);
            Assert.NotNull(resolvedByNewRef);
            Assert.Equal("rename-me", resolvedByNewRef.content);
        }
        finally
        {
            AssetManager.SourceFileSystemChanged -= OnChanged;
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_HighFrequencyBulkOperations_StayConsistentWithoutResidue()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        Directory.CreateDirectory(assets);

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = true,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = true,
                fileWatcherFlushDelayMs = 15
            });

            var rng = new Random(20260424);
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] dirs = ["A", "B/Sub", "C/Sub/Deep"];

            for (int i = 0; i < 160; i++)
            {
                int action = rng.Next(0, 4);
                if (existing.Count == 0)
                    action = 0;

                if (action == 0)
                {
                    string rel = $"{dirs[rng.Next(dirs.Length)]}/f_{i}_{rng.Next(1000)}.txt";
                    WriteSourceFile(assets, rel, $"create-{i}");
                    existing.Add(rel);
                    touched.Add(rel);
                }
                else if (action == 1)
                {
                    string rel = Pick(existing, rng);
                    WriteSourceFile(assets, rel, $"update-{i}");
                }
                else if (action == 2)
                {
                    string rel = Pick(existing, rng);
                    string to = $"{dirs[rng.Next(dirs.Length)]}/m_{i}_{rng.Next(1000)}.txt";
                    MoveSourceFile(assets, rel, to);
                    existing.Remove(rel);
                    existing.Add(to);
                    touched.Add(rel);
                    touched.Add(to);
                }
                else
                {
                    string rel = Pick(existing, rng);
                    DeleteSourceFile(assets, rel);
                    existing.Remove(rel);
                    touched.Add(rel);
                }
            }

            Assert.True(SpinWait.SpinUntil(
                () => IsStorageConsistent(assets, artifacts, existing, touched),
                TimeSpan.FromSeconds(8)));
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Watcher_CompoundRenameAndModify_ConvergesToFinalState()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string p1 = "Flow/one.txt";
        string p2 = "Flow/two.txt";

        WriteSourceFile(assets, p1, "v1");

        try
        {
            AssetManager.Initialize(new AssetManagerOptions
            {
                assetRoot = assets,
                artifactRoot = artifacts,
                autoRegisterBuiltInImporters = true,
                autoRegisterImportersFromTypeCache = false,
                enableFileSystemWatcher = true,
                fileWatcherFlushDelayMs = 15
            });

            Assert.True(AssetManager.Import(p1));
            Assert.True(AssetManager.Load<TextAsset>(p1));

            MoveSourceFile(assets, p1, p2);
            WriteSourceFile(assets, p2, "v2");
            MoveSourceFile(assets, p2, p1);
            WriteSourceFile(assets, p1, "v3");

            Assert.True(SpinWait.SpinUntil(
                () => System.IO.File.Exists(Path.Combine(assets, p1 + ".imeta")) &&
                      System.IO.File.Exists(Path.Combine(artifacts, p1 + ".abin")) &&
                      !System.IO.File.Exists(Path.Combine(assets, p2 + ".imeta")) &&
                      !System.IO.File.Exists(Path.Combine(artifacts, p2 + ".abin")),
                TimeSpan.FromSeconds(5)));
            
            AssetManager.WaitForIdle();

            TextAsset final = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(p1))!;
            Assert.Equal("v3", final.content);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Initialize_Reconcile_RepairsCorruptionAndCleansOrphans()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string relative = "Recover/main.txt";
        string sourcePath = Path.Combine(assets, relative);
        WriteSourceFile(assets, relative, "seed");

        string orphanRelative = "Recover/orphan.txt";
        string orphanMetaPath = Path.Combine(assets, orphanRelative + ".imeta");
        string orphanArtifactPath = Path.Combine(artifacts, orphanRelative + ".abin");

        try
        {
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));
            Assert.True(AssetManager.Import(relative));
            Assert.True(AssetManager.Load<TextAsset>(relative));
            AssetManager.Shutdown();

            string metaPath = Path.Combine(assets, relative + ".imeta");
            string artifactPath = Path.Combine(artifacts, relative + ".abin");
            Assert.True(System.IO.File.Exists(metaPath));
            Assert.True(System.IO.File.Exists(artifactPath));

            System.IO.File.WriteAllBytes(metaPath, [0x01, 0x02, 0x03, 0x04]);
            System.IO.File.Delete(artifactPath);

            WriteSourceFile(assets, orphanRelative, "temp");
            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));
            Assert.True(AssetManager.Import(orphanRelative));
            Assert.True(AssetManager.Load<TextAsset>(orphanRelative));
            AssetManager.Shutdown();
            DeleteSourceFile(assets, orphanRelative);
            Assert.True(System.IO.File.Exists(orphanMetaPath));
            Assert.True(System.IO.File.Exists(orphanArtifactPath));

            AssetManager.Initialize(AssetManagerOptions.Create(assets, artifacts));

            Assert.True(System.IO.File.Exists(metaPath));
            Assert.True(System.IO.File.Exists(artifactPath));
            Assert.True(SpinWait.SpinUntil(
                () => !System.IO.File.Exists(orphanMetaPath) && !System.IO.File.Exists(orphanArtifactPath),
                TimeSpan.FromSeconds(3)));

            AssetMetaProbe repairedMeta = ReadMeta(metaPath);
            Assert.Equal(relative, repairedMeta.relativePath);
            Assert.True(AssetManager.Import(relative));
            Assert.True(AssetManager.Load<TextAsset>(relative));
            TextAsset recovered = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(relative))!;
            Assert.Equal("seed", recovered.content);
            Assert.Equal(repairedMeta.persistentId, recovered.identity.persistentId);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Initialize_Reconcile_ImportsEntireTreeBeforeFirstLoad()
    {
        string root = CreateRoot();
        string assets = Path.Combine(root, "Assets");
        string artifacts = Path.Combine(root, "Artifacts");
        string first = "Config/game.txt";
        string second = "Nested/Deep/readme.txt";
        WriteSourceFile(assets, first, "game");
        WriteSourceFile(assets, second, "readme");

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

            Assert.True(System.IO.File.Exists(Path.Combine(assets, first + ".imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(artifacts, first + ".abin")));
            Assert.True(System.IO.File.Exists(Path.Combine(assets, second + ".imeta")));
            Assert.True(System.IO.File.Exists(Path.Combine(artifacts, second + ".abin")));

            Assert.True(AssetManager.Import(first));
            Assert.True(AssetManager.Import(second));
            Assert.True(AssetManager.Load<TextAsset>(first));
            Assert.True(AssetManager.Load<TextAsset>(second));
            TextAsset firstLoaded = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(first))!;
            TextAsset secondLoaded = AssetManager.Resolve(AssetManager.GetRef<TextAsset>(second))!;
            Assert.Equal("game", firstLoaded.content);
            Assert.Equal("readme", secondLoaded.content);
        }
        finally
        {
            AssetManager.Shutdown();
            DeleteRoot(root);
        }
    }

    private static AssetMetaProbe ReadMeta(string metadataPath)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(metadataPath);
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

    private static void WriteSourceFile(string assetsRoot, string relativePath, string content)
    {
        string path = Path.Combine(assetsRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, NoBomUtf8());
    }

    private static void MoveSourceFile(string assetsRoot, string fromRelativePath, string toRelativePath)
    {
        string fromPath = Path.Combine(assetsRoot, fromRelativePath);
        string toPath = Path.Combine(assetsRoot, toRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(toPath)!);
        if (System.IO.File.Exists(toPath))
            System.IO.File.Delete(toPath);
        System.IO.File.Move(fromPath, toPath);
    }

    private static void DeleteSourceFile(string assetsRoot, string relativePath)
    {
        string path = Path.Combine(assetsRoot, relativePath);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }

    private static string Pick(HashSet<string> set, Random rng)
    {
        int index = rng.Next(set.Count);
        using var enumerator = set.GetEnumerator();
        for (int i = 0; i <= index; i++)
            enumerator.MoveNext();
        return enumerator.Current;
    }

    private static bool IsStorageConsistent(
        string assetsRoot,
        string artifactsRoot,
        HashSet<string> existing,
        HashSet<string> touched)
    {
        foreach (string rel in existing)
        {
            if (!System.IO.File.Exists(Path.Combine(assetsRoot, rel)))
                return false;
            if (!System.IO.File.Exists(Path.Combine(assetsRoot, rel + ".imeta")))
                return false;
            if (!System.IO.File.Exists(Path.Combine(artifactsRoot, rel + ".abin")))
                return false;
        }

        foreach (string rel in touched)
        {
            if (existing.Contains(rel))
                continue;

            if (System.IO.File.Exists(Path.Combine(assetsRoot, rel + ".imeta")))
                return false;
            if (System.IO.File.Exists(Path.Combine(artifactsRoot, rel + ".abin")))
                return false;
        }

        return true;
    }

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
        // Identity
        [SerializableProperty] public Guid persistentId { get; set; }

        // Source
        [SerializableProperty] public string relativePath { get; set; } = string.Empty;
        [SerializableProperty] public string sourceHash { get; set; } = string.Empty;

        // Importer
        [SerializableProperty] public string importerId { get; set; } = string.Empty;
        [SerializableProperty] public int importerVersion { get; set; }

        // Type identity
        [SerializableProperty] public Guid assetTypeStableId { get; set; }

        // Asset data
        [SerializableProperty] public byte[] assetStateBytes { get; set; } = [];
        [SerializableProperty] public string[] dependencies { get; set; } = [];
    }
}
