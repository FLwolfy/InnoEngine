using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Loader.Tests;

public sealed class AssetLoaderTests : IDisposable
{
    public AssetLoaderTests()
    {
        _ = typeof(PrivateConstructorAssetImporter);
        _ = typeof(TestBuildProcessor);
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoAssetLoaderTests", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
        SlowAssetImporter.Reset();
        ImporterConflictProbe.mode = ImporterConflictMode.None;
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
    }

    [Fact]
    public void Import_WritesCacheWithoutCreatingCanonicalInstance()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Config/game.txt", "one");
        using var loader = workspace.CreateLoader();

        Assert.True(loader.Import("Config/game.txt"));
        Assert.True(System.IO.File.Exists(workspace.SourcePath("Config/game.txt.imeta")));
        Assert.True(loader.TryGetPersistentId("Config/game.txt", out Guid persistentId));
        Assert.NotEqual(Guid.Empty, persistentId);
        Assert.True(loader.TryGetArtifact(persistentId, "runtime", out AssetArtifactInfo? artifact));
        Assert.NotNull(artifact);
        Assert.True(System.IO.File.Exists(artifact.absolutePath));
        Assert.True(loader.TryGetAssetType("Config/game.txt", out Type? assetType));
        Assert.Equal(typeof(TextAsset), assetType);
        Assert.Empty(loader.GetLoadedPaths());
    }

    [Fact]
    public void AutomaticallyDiscoveredPrivateImporter_LoadsWithoutRegistrationApi()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Private/item.privateasset", "private");
        using var loader = workspace.CreateLoader();

        PrivateConstructorAsset asset = Assert.IsType<PrivateConstructorAsset>(
            loader.Load("Private/item.privateasset", typeof(PrivateConstructorAsset)));

        Assert.Equal("private", asset.value);
        TypeCacheManager.Rebuild();
        Assert.Same(asset, loader.Load("Private/item.privateasset", typeof(PrivateConstructorAsset)));
    }

    [Fact]
    public void SameAssetType_CanUseMultipleImportersForDifferentExtensions()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Graphs/a.depgraph", string.Empty);
        workspace.WriteText("Graphs/b.depgraph2", string.Empty);
        using var loader = workspace.CreateLoader();

        Assert.IsType<DependencyAsset>(loader.Load("Graphs/a.depgraph", typeof(DependencyAsset)));
        Assert.IsType<DependencyAsset>(loader.Load("Graphs/b.depgraph2", typeof(DependencyAsset)));
    }

    [Fact]
    public void DuplicateImporterId_IsRejectedDuringAutomaticDiscovery()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Conflict/value.probea", "value");
        using var loader = workspace.CreateLoader();
        ImporterConflictProbe.mode = ImporterConflictMode.DuplicateId;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => loader.Import("Conflict/value.probea"));

        Assert.Contains("importer id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DuplicateImporterExtension_IsRejectedDuringAutomaticDiscovery()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Conflict/value.conflict", "value");
        using var loader = workspace.CreateLoader();
        ImporterConflictProbe.mode = ImporterConflictMode.DuplicateExtension;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => loader.Import("Conflict/value.conflict"));

        Assert.Contains("extension", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathAndPersistentIdLoads_ReturnCanonicalInstance()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/shared.txt", "shared");
        using var loader = workspace.CreateLoader();

        TextAsset byPath = Assert.IsType<TextAsset>(loader.Load("Text/shared.txt", typeof(TextAsset)));
        TextAsset byId = Assert.IsType<TextAsset>(loader.Load(byPath.identity.persistentId, typeof(TextAsset)));

        Assert.Same(byPath, byId);
        Assert.Equal(new[] { "Text/shared.txt" }, loader.GetLoadedPaths());
    }

    [Fact]
    public void CurrentCatalogStamp_LoadsArtifactWithoutOpeningSourceContent()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/cached.txt", "cached");
        using (AssetLoader first = workspace.CreateLoader())
            first.Rescan();
        using AssetLoader loader = workspace.CreateLoader();
        loader.Rescan();
        using var sourceLock = new FileStream(
            workspace.SourcePath("Text/cached.txt"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        TextAsset asset = Assert.IsType<TextAsset>(
            loader.Load("Text/cached.txt", typeof(TextAsset)));

        Assert.Equal("cached", asset.content);
    }

    [Fact]
    public void ChangedStampWithEqualContent_DoesNotReimportCanonicalAsset()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/touched.txt", "value");
        using var loader = workspace.CreateLoader();
        TextAsset before = Assert.IsType<TextAsset>(
            loader.Load("Text/touched.txt", typeof(TextAsset)));
        long version = before.contentVersion;
        string sourcePath = workspace.SourcePath("Text/touched.txt");
        System.IO.File.SetLastWriteTimeUtc(
            sourcePath,
            System.IO.File.GetLastWriteTimeUtc(sourcePath).AddSeconds(1));

        TextAsset after = Assert.IsType<TextAsset>(
            loader.Load("Text/touched.txt", typeof(TextAsset)));

        Assert.Same(before, after);
        Assert.Equal(version, after.contentVersion);
    }

    [Fact]
    public void CurrentDependencyStamp_DoesNotOpenSourceDependencyContent()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Import/schema.inc", "schema");
        workspace.WriteText("Import/root.importgraph", "Import/schema.inc");
        using var loader = workspace.CreateLoader();
        Assert.True(loader.Import("Import/root.importgraph"));
        using var sourceLock = new FileStream(
            workspace.SourcePath("Import/schema.inc"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ImportGraphAsset asset = Assert.IsType<ImportGraphAsset>(
            loader.Load("Import/root.importgraph", typeof(ImportGraphAsset)));

        Assert.False(asset.isMissing);
    }

    [Fact]
    public async Task ConcurrentAsyncLoads_ShareImportAndCancellationOnlyCancelsOneWaiter()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Slow/item.slowasset", "value");
        using var loader = workspace.CreateLoader();
        using var cancellation = new CancellationTokenSource();

        ValueTask<AssetObject?> first = loader.LoadAsync("Slow/item.slowasset", typeof(SlowAsset));
        Assert.True(SlowAssetImporter.importStarted.Wait(TimeSpan.FromSeconds(3)));
        ValueTask<AssetObject?> second = loader.LoadAsync(
            "Slow/item.slowasset",
            typeof(SlowAsset),
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second);
        SlowAssetImporter.allowImport.Set();
        SlowAsset loaded = Assert.IsType<SlowAsset>(await first);

        Assert.Equal("value", loaded.value);
        Assert.Equal(1, SlowAssetImporter.importCount);
    }

    [Fact]
    public void StaleSource_ReimportsAndUpdatesCanonicalInstanceInPlace()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/reload.txt", "one");
        using var loader = workspace.CreateLoader();
        TextAsset before = Assert.IsType<TextAsset>(loader.Load("Text/reload.txt", typeof(TextAsset)));
        Guid persistentId = before.identity.persistentId;
        long version = before.contentVersion;

        workspace.WriteText("Text/reload.txt", "two");
        TextAsset after = Assert.IsType<TextAsset>(loader.Load("Text/reload.txt", typeof(TextAsset)));

        Assert.Same(before, after);
        Assert.Equal(persistentId, after.identity.persistentId);
        Assert.Equal("two", after.content);
        Assert.Equal(version + 1, after.contentVersion);
    }

    [Fact]
    public void RuntimeDependencyCycle_IsAllowedAndLoadsAsOneCanonicalSubgraph()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Graphs/a.depgraph", "Graphs/b.depgraph");
        workspace.WriteText("Graphs/b.depgraph", "Graphs/a.depgraph");
        using var loader = workspace.CreateLoader();

        DependencyAsset root = Assert.IsType<DependencyAsset>(
            loader.Load("Graphs/a.depgraph", typeof(DependencyAsset)));
        IReadOnlyList<AssetDependency> direct = loader.GetDependencies(root);
        IReadOnlyList<AssetDependency> recursive = loader.GetDependencies(root, recursive: true);

        Assert.Single(direct);
        Assert.Single(recursive);
        Assert.Equal("Graphs/b.depgraph", direct[0].lastKnownPath);
        Assert.Equal(2, loader.GetLoadedPaths().Count);
    }

    [Fact]
    public void ImportDependencyCycle_IsRejectedWithClosedPathChain()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Import/a.importgraph", "Import/b.importgraph");
        workspace.WriteText("Import/b.importgraph", "Import/a.importgraph");
        using var loader = workspace.CreateLoader();

        Assert.True(loader.Import("Import/a.importgraph"));
        Assert.False(loader.Import("Import/b.importgraph"));
        Assert.True(loader.TryGetInfo("Import/b.importgraph", out AssetInfo? info));
        Assert.NotNull(info);
        Assert.Equal(AssetImportStatus.Failed, info.status);
        string diagnostic = Assert.Single(info.diagnostics);

        Assert.Contains("Import/a.importgraph", diagnostic);
        Assert.Contains("Import/b.importgraph", diagnostic);
        Assert.Contains("->", diagnostic);
    }

    [Fact]
    public void RenameAndDelete_UpdateCanonicalInstanceWithoutChangingIdentity()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/old.txt", "value");
        using var loader = workspace.CreateLoader();
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load("Text/old.txt", typeof(TextAsset)));
        Guid persistentId = asset.identity.persistentId;

        workspace.Move("Text/old.txt", "Text/new.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Renamed, "Text/old.txt")
        ]);

        Assert.Equal("Text/new.txt", asset.sourcePath);
        Assert.Same(asset, loader.Load(persistentId, typeof(TextAsset)));

        workspace.DeleteSource("Text/new.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Deleted)
        ]);

        Assert.True(asset.isMissing);
        Assert.Equal(persistentId, asset.identity.persistentId);
        Assert.True(asset.runtimePayload.IsEmpty);
        Assert.False(System.IO.File.Exists(workspace.SourcePath("Text/new.txt.imeta")));
        Assert.False(loader.TryGetInfo("Text/new.txt", out _));
        Assert.True(loader.TryGetInfo(persistentId, out AssetInfo? tombstone));
        Assert.NotNull(tombstone);
        Assert.Equal(AssetImportStatus.Missing, tombstone.status);
        Assert.True(tombstone.artifactKey.isEmpty);
        Assert.True(tombstone.lastSuccessfulArtifactKey.isEmpty);
    }

    [Fact]
    public void RecreatedSourceWithoutMetadata_ReceivesNewIdentity()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/recover.txt", "one");
        using var loader = workspace.CreateLoader();
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load("Text/recover.txt", typeof(TextAsset)));

        workspace.DeleteSource("Text/recover.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Deleted)
        ]);
        workspace.WriteText("Text/recover.txt", "two");
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Created)
        ]);

        TextAsset replacement = Assert.IsType<TextAsset>(
            loader.Load("Text/recover.txt", typeof(TextAsset)));
        Assert.True(asset.isMissing);
        Assert.False(replacement.isMissing);
        Assert.Equal("two", replacement.content);
        Assert.NotEqual(asset.identity.persistentId, replacement.identity.persistentId);
        Assert.Same(asset, loader.Load(asset.identity.persistentId, typeof(TextAsset)));
    }

    [Fact]
    public void Tombstone_SurvivesCatalogRestartWithoutOccupyingItsFormerPath()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/value.txt", "one");
        Guid oldId;
        using (AssetLoader loader = workspace.CreateLoader())
        {
            loader.Rescan();
            Assert.True(loader.TryGetPersistentId("Text/value.txt", out oldId));
            workspace.DeleteSource("Text/value.txt");
            loader.ApplySourceChanges([
                new Inno.Assets.File.AssetChangedEvent("Text/value.txt", WatcherChangeTypes.Deleted)
            ]);
        }

        workspace.WriteText("Text/value.txt", "two");
        using AssetLoader restarted = workspace.CreateLoader();
        restarted.Rescan();

        Assert.True(restarted.TryGetInfo(oldId, out AssetInfo? tombstone));
        Assert.Equal(AssetImportStatus.Missing, tombstone!.status);
        Assert.True(restarted.TryGetPersistentId("Text/value.txt", out Guid newId));
        Assert.NotEqual(oldId, newId);
    }

    [Fact]
    public void RestoredSourceAndMetadata_ReactivatesOriginalIdentityInPlace()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/recover.txt", "one");
        using var loader = workspace.CreateLoader();
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load("Text/recover.txt", typeof(TextAsset)));
        Guid id = asset.identity.persistentId;
        byte[] metadata = System.IO.File.ReadAllBytes(workspace.SourcePath("Text/recover.txt.imeta"));

        workspace.DeleteSource("Text/recover.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Deleted)
        ]);
        workspace.WriteText("Text/recover.txt", "two");
        System.IO.File.WriteAllBytes(workspace.SourcePath("Text/recover.txt.imeta"), metadata);
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/recover.txt", WatcherChangeTypes.Created)
        ]);

        TextAsset restored = Assert.IsType<TextAsset>(loader.Load("Text/recover.txt", typeof(TextAsset)));
        Assert.Same(asset, restored);
        Assert.Equal(id, restored.identity.persistentId);
        Assert.False(restored.isMissing);
        Assert.Equal("two", restored.content);
    }

    [Fact]
    public void DeleteCreateRenameFallback_PreservesIdentityOnlyWhenFingerprintMatchIsUnique()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/old.txt", "value");
        using var loader = workspace.CreateLoader();
        loader.Rescan();
        Assert.True(loader.TryGetPersistentId("Text/old.txt", out Guid id));

        workspace.Move("Text/old.txt", "Text/new.txt");
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/old.txt", WatcherChangeTypes.Deleted),
            new Inno.Assets.File.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Created)
        ]);

        Assert.True(loader.TryGetPersistentId("Text/new.txt", out Guid movedId));
        Assert.Equal(id, movedId);
        Assert.False(System.IO.File.Exists(workspace.SourcePath("Text/old.txt.imeta")));
        Assert.True(System.IO.File.Exists(workspace.SourcePath("Text/new.txt.imeta")));
    }

    [Fact]
    public void AmbiguousDeleteCreateRenameFallback_DoesNotGuessAnIdentity()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/first.txt", "same");
        workspace.WriteText("Text/second.txt", "same");
        using var loader = workspace.CreateLoader();
        loader.Rescan();
        Assert.True(loader.TryGetPersistentId("Text/first.txt", out Guid firstId));
        Assert.True(loader.TryGetPersistentId("Text/second.txt", out Guid secondId));

        workspace.DeleteSource("Text/first.txt");
        workspace.DeleteSource("Text/second.txt");
        workspace.WriteText("Text/new.txt", "same");
        loader.ApplySourceChanges([
            new Inno.Assets.File.AssetChangedEvent("Text/first.txt", WatcherChangeTypes.Deleted),
            new Inno.Assets.File.AssetChangedEvent("Text/second.txt", WatcherChangeTypes.Deleted),
            new Inno.Assets.File.AssetChangedEvent("Text/new.txt", WatcherChangeTypes.Created)
        ]);

        Assert.True(loader.TryGetPersistentId("Text/new.txt", out Guid newId));
        Assert.NotEqual(firstId, newId);
        Assert.NotEqual(secondId, newId);
        Assert.True(loader.TryGetInfo("Text/new.txt", out AssetInfo? newInfo));
        Assert.Contains(newInfo!.diagnostics, diagnostic =>
            diagnostic.Contains("matched 2 removed assets", StringComparison.Ordinal));
        Assert.True(loader.TryGetInfo(firstId, out AssetInfo? firstTombstone));
        Assert.True(loader.TryGetInfo(secondId, out AssetInfo? secondTombstone));
        Assert.Equal(AssetImportStatus.Missing, firstTombstone!.status);
        Assert.Equal(AssetImportStatus.Missing, secondTombstone!.status);
    }

    [Fact]
    public void Save_PreservesIdentityIncrementsVersionAndRejectsDifferentPath()
    {
        using TestWorkspace workspace = new();
        using var loader = workspace.CreateLoader();
        var asset = new MutableAsset { value = "one" };

        Assert.True(loader.Save("Data/value.mutableasset", asset));
        Guid persistentId = asset.identity.persistentId;
        long version = asset.contentVersion;
        asset.value = "two";
        Assert.True(loader.Save(asset));

        Assert.Equal(persistentId, asset.identity.persistentId);
        Assert.Equal(version + 1, asset.contentVersion);
        Assert.Equal("two", workspace.ReadText("Data/value.mutableasset"));
        Assert.Same(asset, loader.Load(persistentId, typeof(MutableAsset)));
        Assert.Throws<InvalidOperationException>(() => loader.Save("Data/copy.mutableasset", asset));
    }

    [Fact]
    public void FailedReimport_KeepsPreviousCanonicalStateAndVersion()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Data/stable.mutableasset", "one");
        using var loader = workspace.CreateLoader();
        MutableAsset asset = Assert.IsType<MutableAsset>(
            loader.Load("Data/stable.mutableasset", typeof(MutableAsset)));
        long version = asset.contentVersion;

        workspace.WriteText("Data/stable.mutableasset", "!invalid!");
        Assert.False(loader.Import("Data/stable.mutableasset"));
        Assert.True(loader.TryGetInfo("Data/stable.mutableasset", out AssetInfo? info));
        Assert.NotNull(info);
        Assert.Equal(AssetImportStatus.Failed, info.status);
        Assert.Contains(info.diagnostics, static value => value.Contains("InvalidDataException"));

        Assert.Equal("one", asset.value);
        Assert.Equal(version, asset.contentVersion);
    }

    [Fact]
    public void FailedSave_PreservesCommittedSourceMetaArtifactAndVersion()
    {
        using TestWorkspace workspace = new();
        using var loader = workspace.CreateLoader();
        var asset = new MutableAsset { value = "one" };
        Assert.True(loader.Save("Data/rollback.mutableasset", asset));
        byte[] sourceBefore = System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset"));
        byte[] metaBefore = System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset.imeta"));
        Assert.True(loader.TryGetArtifact(asset.identity.persistentId, "runtime", out AssetArtifactInfo? artifact));
        Assert.NotNull(artifact);
        byte[] artifactBefore = System.IO.File.ReadAllBytes(artifact.absolutePath);
        long versionBefore = asset.contentVersion;
        asset.value = "!invalid!";

        Assert.Throws<InvalidDataException>(() => loader.Save(asset));

        Assert.Equal(sourceBefore, System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset")));
        Assert.Equal(metaBefore, System.IO.File.ReadAllBytes(workspace.SourcePath("Data/rollback.mutableasset.imeta")));
        Assert.Equal(artifactBefore, System.IO.File.ReadAllBytes(artifact.absolutePath));
        Assert.Equal(versionBefore, asset.contentVersion);
    }

    [Fact]
    public void RuntimeDependencyRetention_KeepsDependencyAliveWhileRootIsReferenced()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/dependency.txt", "dependency");
        workspace.WriteText("Graphs/root.depgraph", "Text/dependency.txt");
        using var loader = workspace.CreateLoader();
        DependencyAsset root = Assert.IsType<DependencyAsset>(
            loader.Load("Graphs/root.depgraph", typeof(DependencyAsset)));

        Assert.Equal(0, loader.UnloadUnusedAssets());
        Assert.Equal(2, loader.GetLoadedPaths().Count);
        GC.KeepAlive(root);
    }

    [Fact]
    public void UnusedRuntimeDependencyCycle_IsCollectedAsAUnit()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Graphs/a.depgraph", "Graphs/b.depgraph");
        workspace.WriteText("Graphs/b.depgraph", "Graphs/a.depgraph");
        using var loader = workspace.CreateLoader();
        (WeakReference first, WeakReference second) = LoadCycleWithoutEscaping(loader);

        int released = loader.UnloadUnusedAssets();

        Assert.Equal(2, released);
        Assert.False(first.IsAlive);
        Assert.False(second.IsAlive);
        Assert.Empty(loader.GetLoadedPaths());
    }

    [Fact]
    public void ReferenceDiagnostics_DoNotKeepAssetsAlive()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/dependency.txt", "dependency");
        workspace.WriteText("Graphs/root.depgraph", "Text/dependency.txt");
        using var loader = workspace.CreateLoader();
        WeakReference weak = LoadAndInspectWithoutEscaping(loader);

        Assert.Equal(2, loader.UnloadUnusedAssets());
        Assert.False(weak.IsAlive);
    }

    [Fact]
    public void Dispose_ReleasesRuntimeResourcesExactlyOnce()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Hooks/value.hookasset", "value");
        var loader = workspace.CreateLoader();
        HookAsset asset = Assert.IsType<HookAsset>(loader.Load("Hooks/value.hookasset", typeof(HookAsset)));

        loader.Dispose();
        loader.Dispose();

        Assert.True(asset.payloadChangeCount >= 1);
        Assert.Equal(1, asset.unloadingCount);
        GC.KeepAlive(asset);
    }

    [Fact]
    public void Rescan_TracksUnsupportedSourcesAndFolderIdentityWithoutFakeArtifacts()
    {
        using TestWorkspace workspace = new();
        Directory.CreateDirectory(workspace.SourcePath("EmptyFolder"));
        workspace.WriteText("Unknown/value.unknown", "value");
        using var loader = workspace.CreateLoader();

        loader.Rescan();

        Assert.True(loader.TryGetInfo("EmptyFolder", out AssetInfo? folder));
        Assert.Equal(AssetSourceKind.Directory, folder!.sourceKind);
        Assert.Equal(AssetImportStatus.Imported, folder.status);
        Assert.True(System.IO.File.Exists(workspace.SourcePath("EmptyFolder.imeta")));
        Assert.True(folder.artifactKey.isEmpty);
        Assert.True(loader.TryGetInfo("Unknown/value.unknown", out AssetInfo? unsupported));
        Assert.Equal(AssetImportStatus.Unsupported, unsupported!.status);
        Assert.Equal(Guid.Empty, unsupported.persistentId);
        Assert.False(System.IO.File.Exists(workspace.SourcePath("Unknown/value.unknown.imeta")));
        Assert.True(unsupported.artifactKey.isEmpty);
    }

    [Fact]
    public void SourceMetadata_RestoresPersistentIdentityWhenLibraryIsRebuilt()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/value.txt", "value");
        Guid id;
        using (AssetLoader first = workspace.CreateLoader())
        {
            first.Rescan();
            Assert.True(first.TryGetPersistentId("Text/value.txt", out id));
        }
        Directory.Delete(workspace.libraryRoot, recursive: true);
        Directory.CreateDirectory(workspace.libraryRoot);

        using AssetLoader rebuilt = workspace.CreateLoader();
        rebuilt.Rescan();

        Assert.True(rebuilt.TryGetPersistentId("Text/value.txt", out Guid restored));
        Assert.Equal(id, restored);
    }

    [Fact]
    public void ContentAddressedStore_DeduplicatesEqualImportsAndCollectsUnreachableBundles()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/first.txt", "same");
        workspace.WriteText("Text/second.txt", "same");
        using var loader = workspace.CreateLoader();
        loader.Rescan();
        Assert.True(loader.TryGetInfo("Text/first.txt", out AssetInfo? first));
        Assert.True(loader.TryGetInfo("Text/second.txt", out AssetInfo? second));
        Assert.Equal(first!.artifactKey, second!.artifactKey);
        Assert.True(loader.TryGetArtifact(first.persistentId, "runtime", out AssetArtifactInfo? oldArtifact));
        Assert.NotNull(oldArtifact);

        workspace.WriteText("Text/first.txt", "changed");
        Assert.True(loader.Import("Text/first.txt"));
        Assert.True(loader.TryGetArtifact(first.persistentId, "runtime", out AssetArtifactInfo? currentArtifact));
        Assert.NotNull(currentArtifact);
        Assert.NotEqual(oldArtifact.key, currentArtifact.key);
        Assert.True(System.IO.File.Exists(oldArtifact.absolutePath));

        Assert.Equal(0, loader.CollectArtifacts(TimeSpan.Zero, maximumSizeBytes: 0));
        Assert.True(System.IO.File.Exists(oldArtifact.absolutePath));
        workspace.DeleteSource("Text/second.txt");
        System.IO.File.Delete(workspace.SourcePath("Text/second.txt.imeta"));
        loader.Rescan();
        Assert.True(loader.CollectArtifacts(TimeSpan.Zero, maximumSizeBytes: 0) >= 1);
        Assert.False(System.IO.File.Exists(oldArtifact.absolutePath));
        Assert.True(System.IO.File.Exists(currentArtifact.absolutePath));
    }

    [Fact]
    public async Task AggregateBuildProcessor_ProducesStableContentAddressedOutput()
    {
        using TestWorkspace workspace = new();
        workspace.WriteText("Text/input.txt", "value");
        using var loader = workspace.CreateLoader();
        loader.Rescan();
        Assert.True(loader.TryGetInfo("Text/input.txt", out AssetInfo? input));
        Assert.NotNull(input);
        var definition = new TestBuildDefinitionAsset { label = "bundle" };

        AssetArtifactKey first = await loader.BuildAsync(definition, [input]);
        AssetArtifactKey repeated = await loader.BuildAsync(definition, [input]);

        Assert.False(first.isEmpty);
        Assert.Equal(first, repeated);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference first, WeakReference second) LoadCycleWithoutEscaping(AssetLoader loader)
    {
        AssetObject first = loader.Load("Graphs/a.depgraph", typeof(DependencyAsset))!;
        AssetDependency secondDescriptor = Assert.Single(loader.GetDependencies(first));
        AssetObject second = loader.Load(secondDescriptor.persistentId, typeof(DependencyAsset))!;
        return (new WeakReference(first), new WeakReference(second));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndInspectWithoutEscaping(AssetLoader loader)
    {
        AssetObject root = loader.Load("Graphs/root.depgraph", typeof(DependencyAsset))!;
        AssetDependency descriptor = Assert.Single(loader.GetDependencies(root));
        AssetObject dependency = loader.Load(descriptor.persistentId, typeof(TextAsset))!;
        AssetReferenceInfo info = loader.GetReferenceInfo(dependency);
        Assert.Equal(1, info.knownReferenceCount);
        Assert.Equal(AssetReferenceKind.AssetDependency, info.references[0].kind);
        return new WeakReference(root);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string m_root = Path.Combine(
            Path.GetTempPath(),
            "InnoAssetLoaderTests",
            Guid.NewGuid().ToString("N"));

        internal TestWorkspace()
        {
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(libraryRoot);
        }

        internal string assetRoot => Path.Combine(m_root, "Assets");
        internal string libraryRoot => Path.Combine(m_root, "Library");
        internal AssetLoader CreateLoader() => new(assetRoot, libraryRoot);
        internal string SourcePath(string relativePath) => Path.Combine(assetRoot, relativePath);

        internal void WriteText(string relativePath, string content)
        {
            string path = SourcePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        internal string ReadText(string relativePath) => System.IO.File.ReadAllText(SourcePath(relativePath));

        internal void Move(string oldRelativePath, string newRelativePath)
        {
            string target = SourcePath(newRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.Move(SourcePath(oldRelativePath), target);
        }

        internal void DeleteSource(string relativePath)
        {
            string path = SourcePath(relativePath);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        public void Dispose()
        {
            SlowAssetImporter.allowImport.Set();
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
    }
}

[StableTypeId("501988d0-c069-4dcf-97f6-e899b24e5801")]
internal sealed class PrivateConstructorAsset : AssetObject
{
    [SerializableProperty]
    internal string value { get; set; } = string.Empty;
}

[AssetImporterExtension]
internal sealed class PrivateConstructorAssetImporter : AssetImporter<PrivateConstructorAsset>
{
    private static readonly IReadOnlyList<string> s_extensions = [".privateasset"];

    private PrivateConstructorAssetImporter()
    {
    }

    public override string importerId => "inno.tests.private-constructor";
    public override IReadOnlyList<string> supportedExtensions => s_extensions;

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<PrivateConstructorAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new PrivateConstructorAsset { value = context.ReadUtf8Text() });
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("a49b603c-0f5f-4861-903a-3819513da002")]
internal sealed class DependencyAsset : AssetObject;

[AssetImporterExtension]
internal sealed class DependencyAssetImporter : AssetImporter<DependencyAsset>
{
    public override string importerId => "inno.tests.runtime-dependency";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".depgraph"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<DependencyAsset> output,
        CancellationToken cancellationToken)
    {
        string dependency = context.ReadUtf8Text().Trim();
        if (!string.IsNullOrWhiteSpace(dependency))
            output.DependsOnAsset(dependency);
        output.SetAsset(new DependencyAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[AssetImporterExtension]
internal sealed class AlternateDependencyAssetImporter : AssetImporter<DependencyAsset>
{
    public override string importerId => "inno.tests.runtime-dependency-alternate";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".depgraph2"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<DependencyAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new DependencyAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("a80d363f-8e49-4615-89ee-589613b91c03")]
internal sealed class ImportGraphAsset : AssetObject;

[AssetImporterExtension]
internal sealed class ImportGraphAssetImporter : AssetImporter<ImportGraphAsset>
{
    public override string importerId => "inno.tests.import-dependency";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".importgraph"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ImportGraphAsset> output,
        CancellationToken cancellationToken)
    {
        string dependency = context.ReadUtf8Text().Trim();
        if (!string.IsNullOrWhiteSpace(dependency))
            output.DependsOnSource(dependency);
        output.SetAsset(new ImportGraphAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("490def27-4cdd-48a0-b324-9b7438600404")]
internal sealed class SlowAsset : AssetObject
{
    [SerializableProperty]
    internal string value { get; set; } = string.Empty;
}

[AssetImporterExtension]
internal sealed class SlowAssetImporter : AssetImporter<SlowAsset>
{
    internal static readonly ManualResetEventSlim importStarted = new(false);
    internal static readonly ManualResetEventSlim allowImport = new(false);
    internal static int importCount;

    public override string importerId => "inno.tests.slow";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".slowasset"];

    internal static void Reset()
    {
        importCount = 0;
        importStarted.Reset();
        allowImport.Reset();
    }

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<SlowAsset> output,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref importCount);
        importStarted.Set();
        if (!allowImport.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The slow importer test gate was not released.");
        output.SetAsset(new SlowAsset { value = context.ReadUtf8Text() });
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("fa17dc36-b9cc-4187-b5da-72906ad00505")]
internal sealed class MutableAsset : AssetObject
{
    [SerializableProperty]
    internal string value { get; set; } = string.Empty;
}

[AssetImporterExtension]
internal sealed class MutableAssetImporter : AssetImporter<MutableAsset>
{
    public override string importerId => "inno.tests.mutable";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".mutableasset"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<MutableAsset> output,
        CancellationToken cancellationToken)
    {
        string value = context.ReadUtf8Text();
        if (value == "!invalid!")
            throw new InvalidDataException("The mutable asset source is invalid.");
        output.SetAsset(new MutableAsset { value = value });
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        MutableAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(Encoding.UTF8.GetBytes(asset.value));
}

[StableTypeId("9f9f5f9f-4d86-414f-9ff9-78ad8ec60606")]
internal sealed class HookAsset : AssetObject
{
    internal int payloadChangeCount;
    internal int unloadingCount;

    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
        => Interlocked.Increment(ref payloadChangeCount);

    protected override void OnUnloading()
        => Interlocked.Increment(ref unloadingCount);
}

[AssetImporterExtension]
internal sealed class HookAssetImporter : AssetImporter<HookAsset>
{
    public override string importerId => "inno.tests.hook";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".hookasset"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<HookAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new HookAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

internal enum ImporterConflictMode
{
    None,
    DuplicateId,
    DuplicateExtension
}

internal static class ImporterConflictProbe
{
    internal static ImporterConflictMode mode;
}

[StableTypeId("da675da1-9276-40c4-9964-0eb4b8ff9a07")]
internal sealed class ImporterConflictAsset : AssetObject;

[AssetImporterExtension]
internal sealed class ImporterConflictAssetImporterA : AssetImporter<ImporterConflictAsset>
{
    public override string importerId => ImporterConflictProbe.mode == ImporterConflictMode.DuplicateId
        ? "inno.tests.conflict"
        : "inno.tests.conflict-a";

    public override IReadOnlyList<string> supportedExtensions =>
        ImporterConflictProbe.mode == ImporterConflictMode.DuplicateExtension
            ? [".conflict"]
            : [".probea"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ImporterConflictAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new ImporterConflictAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[AssetImporterExtension]
internal sealed class ImporterConflictAssetImporterB : AssetImporter<ImporterConflictAsset>
{
    public override string importerId => ImporterConflictProbe.mode == ImporterConflictMode.DuplicateId
        ? "inno.tests.conflict"
        : "inno.tests.conflict-b";

    public override IReadOnlyList<string> supportedExtensions =>
        ImporterConflictProbe.mode == ImporterConflictMode.DuplicateExtension
            ? [".conflict"]
            : [".probeb"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ImporterConflictAsset> output,
        CancellationToken cancellationToken)
    {
        output.SetAsset(new ImporterConflictAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}

[StableTypeId("8f87f452-203e-4cd4-9c91-c93af4802141")]
internal sealed class TestBuildDefinitionAsset : AssetObject
{
    [SerializableProperty]
    internal string label { get; set; } = string.Empty;
}

[AssetBuildProcessorExtension]
internal sealed class TestBuildProcessor : AssetBuildProcessor<TestBuildDefinitionAsset>
{
    public override string processorId => "inno.tests.aggregate-build";

    protected override ValueTask BuildAsync(
        AssetBuildContext<TestBuildDefinitionAsset> context,
        AssetArtifactWriter output,
        CancellationToken cancellationToken)
    {
        string value = context.definition.label + ":" + context.inputs.Count;
        return output.WriteAsync("result", Encoding.UTF8.GetBytes(value), cancellationToken);
    }
}
