using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Inno.Assets.Core;
using Inno.Assets.File;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerContractTests : IDisposable
{
    public AssetManagerContractTests()
    {
        _ = typeof(ManagerDependencyImporter);
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoAssetContractTests", "Assemblies")
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
    public void PublicApis_RejectUseBeforeInitialization()
    {
        Assert.Throws<InvalidOperationException>(() => AssetManager.Import("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Load<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Load<TextAsset>(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetLoadedPaths());
        Assert.Throws<InvalidOperationException>(() => AssetManager.UnloadUnusedAssets());
    }

    [Fact]
    public void Initialize_RequiresIdentityTypeCacheAndSerializationInOrder()
    {
        using TestAssetWorkspace workspace = new();
        SerializationManager.Shutdown();

        Assert.Throws<InvalidOperationException>(() => AssetManager.Initialize(workspace.options));

        SerializationManager.Initialize();
        AssetManager.Initialize(workspace.options);
        Assert.True(AssetManager.isInitialized);
    }

    [Fact]
    public void PublicSurface_HasNoRegistrationTrackingOrForcedUnloadApi()
    {
        string[] managerMethods = typeof(AssetManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(static method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] loaderMethods = typeof(AssetLoader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(static method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain("RegisterImporter", managerMethods);
        Assert.DoesNotContain("TrackDependencies", managerMethods);
        Assert.DoesNotContain("TrackSerializedReferences", managerMethods);
        Assert.DoesNotContain("Unload", managerMethods);
        Assert.DoesNotContain("UnloadAll", managerMethods);
        Assert.DoesNotContain("RegisterImporter", loaderMethods);
        Assert.DoesNotContain("Unload", loaderMethods);
        Assert.DoesNotContain("UnloadAll", loaderMethods);
    }

    [Fact]
    public async Task PathGuidTryLoadAndAsyncLoad_ReturnOneCanonicalInstanceWithoutCounts()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/shared.txt", "shared");
        AssetManager.Initialize(workspace.options);

        TextAsset first = AssetManager.Load<TextAsset>("Text/shared.txt");
        TextAsset repeated = AssetManager.Load<TextAsset>("Text/shared.txt");
        TextAsset byId = AssetManager.Load<TextAsset>(first.identity.persistentId);
        Assert.True(AssetManager.TryLoad("Text/shared.txt", out TextAsset? tried));
        TextAsset asyncLoaded = await AssetManager.LoadAsync<TextAsset>(first.identity.persistentId);

        Assert.Same(first, repeated);
        Assert.Same(first, byId);
        Assert.Same(first, tried);
        Assert.Same(first, asyncLoaded);
        Assert.Single(AssetManager.GetLoadedPaths());
        Assert.False(AssetManager.TryLoad("Missing/nope.txt", out TextAsset? missing));
        Assert.Null(missing);
        Assert.False(AssetManager.TryLoad("Text/shared.txt", out BinaryAsset? wrongType));
        Assert.Null(wrongType);
    }

    [Fact]
    public void SaveImportAndRescan_PreserveCanonicalIdentityAndReloadInPlace()
    {
        using TestAssetWorkspace workspace = new();
        AssetManager.Initialize(workspace.options);
        var unsaved = new TextAsset("one", "plain");

        Assert.True(AssetManager.Save("Text/value.txt", unsaved));
        Guid persistentId = unsaved.identity.persistentId;
        long version = unsaved.contentVersion;
        workspace.Write("Text/value.txt", "two");
        AssetManager.Rescan();

        TextAsset reloaded = AssetManager.Load<TextAsset>(persistentId);
        Assert.Same(unsaved, reloaded);
        Assert.Equal("two", reloaded.content);
        Assert.Equal(version + 1, reloaded.contentVersion);
        Assert.True(AssetManager.TryGetPersistentId("Text/value.txt", out Guid catalogId));
        Assert.Equal(persistentId, catalogId);
    }

    [Fact]
    public void AssemblyCatalogActivationReconcilesAssetsBeforePublicationCompletes()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/catalog.txt", "one");
        AssetManager.Initialize(workspace.options);
        TextAsset canonical = AssetManager.Load<TextAsset>("Text/catalog.txt");
        Guid persistentId = canonical.identity.persistentId;

        workspace.Write("Text/catalog.txt", "two");
        TypeCacheManager.Rebuild();

        Assert.Equal("two", canonical.content);
        Assert.Same(canonical, AssetManager.Load<TextAsset>(persistentId));
    }

    [Fact]
    public void DirectAndRecursiveDependencies_AreQueryableWithoutOwnerTracking()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/leaf.txt", "leaf");
        workspace.Write("Graphs/middle.managerdep", "Text/leaf.txt");
        workspace.Write("Graphs/root.managerdep", "Graphs/middle.managerdep");
        AssetManager.Initialize(workspace.options);

        ManagerDependencyAsset root = AssetManager.Load<ManagerDependencyAsset>("Graphs/root.managerdep");
        IReadOnlyList<AssetDependency> direct = AssetManager.GetDependencies(root);
        IReadOnlyList<AssetDependency> recursive = AssetManager.GetDependencies(root, recursive: true);

        Assert.Single(direct);
        Assert.Equal("Graphs/middle.managerdep", direct[0].lastKnownPath);
        Assert.Equal(2, recursive.Count);
        Assert.Contains(recursive, static dependency => dependency.lastKnownPath == "Text/leaf.txt");
    }

    [Fact]
    public void ReferenceInfo_ReportsKnownEngineReferencesButNotClrReferenceCount()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/leaf.txt", "leaf");
        workspace.Write("Graphs/root.managerdep", "Text/leaf.txt");
        AssetManager.Initialize(workspace.options);
        ManagerDependencyAsset root = AssetManager.Load<ManagerDependencyAsset>("Graphs/root.managerdep");
        TextAsset leaf = AssetManager.Load<TextAsset>(Assert.Single(AssetManager.GetDependencies(root)).persistentId);

        AssetReferenceInfo info = AssetManager.GetReferenceInfo(leaf);

        Assert.True(info.isLoaded);
        Assert.Equal(1, info.knownReferenceCount);
        Assert.Equal(AssetReferenceKind.AssetDependency, info.references[0].kind);
        Assert.Null(info.lastSweepReachability);
    }

    [Fact]
    public void DirectAssetReference_RestoresCanonicalOrMissingInstanceAndCanBeSavedAgain()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/missing.txt", "value");
        AssetManager.Initialize(workspace.options);
        TextAsset loaded = AssetManager.Load<TextAsset>("Text/missing.txt");
        Guid persistentId = loaded.identity.persistentId;
        byte[] bytes = SerializationManager.Serialize(new AssetHolder { asset = loaded });

        workspace.Delete("Text/missing.txt");
        AssetManager.Rescan();
        AssetHolder restored = SerializationManager.Deserialize<AssetHolder>(bytes);

        Assert.Same(loaded, restored.asset);
        Assert.True(restored.asset!.isMissing);
        Assert.Equal(persistentId, restored.asset.identity.persistentId);
        Assert.Equal("Text/missing.txt", restored.asset.assetPath.ToString());
        Assert.NotEmpty(SerializationManager.Serialize(restored));
    }

    [Fact]
    public void UnloadUnusedAssets_DoesNotReleaseExternallyReferencedAsset()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/unused.txt", "unused");
        AssetManager.Initialize(workspace.options);
        TextAsset retained = AssetManager.Load<TextAsset>("Text/unused.txt");

        Assert.Equal(0, AssetManager.UnloadUnusedAssets());
        Assert.Single(AssetManager.GetLoadedPaths());
        GC.KeepAlive(retained);

    }

    [Fact]
    public void UnloadUnusedAssets_ReleasesAssetAfterManagedRootIsGone()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/unused.txt", "unused");
        AssetManager.Initialize(workspace.options);
        WeakReference weak = LoadWithoutEscaping("Text/unused.txt");

        Assert.Equal(1, AssetManager.UnloadUnusedAssets());
        Assert.False(weak.IsAlive);
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void AssetReloadedSubscriberFailure_DoesNotRollbackCommittedState()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/reload.txt", "one");
        AssetManager.Initialize(workspace.options);
        TextAsset asset = AssetManager.Load<TextAsset>("Text/reload.txt");
        int observed = 0;
        AssetManager.AssetReloaded += _ => throw new InvalidOperationException("observer");
        AssetManager.AssetReloaded += _ => observed++;

        workspace.Write("Text/reload.txt", "two");
        AssetManager.Rescan();

        Assert.Equal(1, observed);
        Assert.Equal("two", asset.content);
    }

    [Fact]
    public void Shutdown_ClearsStateAndEvents()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/value.txt", "value");
        AssetManager.Initialize(workspace.options);
        _ = AssetManager.Load<TextAsset>("Text/value.txt");
        AssetManager.AssetReloaded += _ => { };

        AssetManager.Shutdown();

        Assert.False(AssetManager.isInitialized);
        Assert.Equal(string.Empty, AssetManager.assetRoot);
        Assert.Equal(string.Empty, AssetManager.artifactRoot);
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void ReplaceSourceMounts_WhenPersistentIdentityConflicts_KeepsActiveGeneration()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/project.txt", "project");
        AssetManager.Initialize(workspace.options);
        Guid projectId = AssetManager.Load<TextAsset>("Text/project.txt").identity.persistentId;
        string pluginRoot = workspace.CreateExternalRoot("ConflictPlugin");
        WriteReadOnlyAsset(
            pluginRoot,
            "conflict.txt",
            "plugin",
            projectId,
            "Inno.Assets.Loader.Importers.TextAssetImporter");
        AssetSourceMount plugin = new(
            new AssetSourceId("tests.conflict"),
            pluginRoot,
            isReadOnly: true);

        Assert.Throws<InvalidDataException>(() => AssetManager.ReplaceSourceMounts(
        [
            new AssetSourceMount(AssetSourceId.project, workspace.assetRoot, isReadOnly: false),
            plugin
        ]));

        Assert.Collection(
            AssetManager.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
        Assert.Equal("project", AssetManager.Load<TextAsset>(projectId).content);
    }

    [Fact]
    public void ReadOnlyMount_AllowsLoadingButRejectsEveryAssetMutation()
    {
        using TestAssetWorkspace workspace = new();
        AssetManager.Initialize(workspace.options);
        string pluginRoot = workspace.CreateExternalRoot("ReadOnlyPlugin");
        WriteReadOnlyAsset(
            pluginRoot,
            "value.txt",
            "plugin",
            Guid.NewGuid(),
            "Inno.Assets.Loader.Importers.TextAssetImporter");
        AssetSourceId pluginId = new("tests.read-only");
        AssetManager.ReplaceSourceMounts(
        [
            new AssetSourceMount(AssetSourceId.project, workspace.assetRoot, isReadOnly: false),
            new AssetSourceMount(pluginId, pluginRoot, isReadOnly: true)
        ]);
        AssetPath pluginPath = new(pluginId, "value.txt");

        Assert.Equal("plugin", AssetManager.Load<TextAsset>(pluginPath).content);
        Assert.Throws<InvalidOperationException>(() =>
            AssetManager.Save(pluginPath, new TextAsset("changed")));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Move(pluginPath.ToString(), "moved.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Delete(pluginPath.ToString()));
        Assert.Throws<InvalidOperationException>(() =>
            AssetManager.CreateDirectory(new AssetPath(pluginId, "Folder").ToString()));
    }

    [Fact]
    public void SourceMountTransaction_HidesCandidateUntilCommitAndRestoresAfterRollback()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/project.txt", "project");
        AssetManager.Initialize(workspace.options);
        string pluginRoot = workspace.CreateExternalRoot("StagedPlugin");
        WriteReadOnlyAsset(
            pluginRoot,
            "value.txt",
            "candidate",
            Guid.NewGuid(),
            "Inno.Assets.Loader.Importers.TextAssetImporter");
        AssetSourceId pluginId = new("tests.staged");
        AssetSourceMount[] mounts =
        [
            new AssetSourceMount(AssetSourceId.project, workspace.assetRoot, isReadOnly: false),
            new AssetSourceMount(pluginId, pluginRoot, isReadOnly: true)
        ];

        using (AssetSourceMountTransaction transaction = AssetManager.PrepareSourceMounts(mounts))
        {
            Assert.Single(AssetManager.sourceMounts);
            Assert.DoesNotContain(
                AssetManager.GetFileSystemEntries(),
                entry => entry.assetPath.source == pluginId);
            Assert.Contains(
                transaction.GetFileSystemEntries(),
                entry => entry.assetPath == new AssetPath(pluginId, "value.txt"));
            Assert.Equal(
                "candidate",
                transaction.Load<TextAsset>(new AssetPath(pluginId, "value.txt")).content);

            transaction.Activate();
            Assert.Contains(AssetManager.sourceMounts, mount => mount.id == pluginId);
            transaction.Rollback();
        }

        Assert.Collection(
            AssetManager.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));

        int changes = 0;
        AssetManager.SourceMountsChanged += () => changes++;
        using AssetSourceMountTransaction committed = AssetManager.PrepareSourceMounts(mounts);
        committed.Activate();
        Assert.Equal(0, changes);
        committed.Complete();

        Assert.Equal(1, changes);
        Assert.Equal(
            "candidate",
            AssetManager.Load<TextAsset>(new AssetPath(pluginId, "value.txt")).content);
    }

    [Fact]
    public void PluginCrossMountDependency_RequiresExplicitSourceDependency()
    {
        using TestAssetWorkspace workspace = new();
        AssetManager.Initialize(workspace.options);
        AssetSourceId providerId = new("tests.provider");
        AssetSourceId consumerId = new("tests.consumer");
        string providerRoot = workspace.CreateExternalRoot("ProviderPlugin");
        string consumerRoot = workspace.CreateExternalRoot("ConsumerPlugin");
        WriteReadOnlyAsset(
            providerRoot,
            "leaf.txt",
            "leaf",
            Guid.NewGuid(),
            "Inno.Assets.Loader.Importers.TextAssetImporter");
        WriteReadOnlyAsset(
            consumerRoot,
            "root.managerdep",
            new AssetPath(providerId, "leaf.txt").ToString(),
            Guid.NewGuid(),
            "inno.tests.manager-dependency");
        AssetSourceMount project = new(AssetSourceId.project, workspace.assetRoot, isReadOnly: false);
        AssetSourceMount provider = new(providerId, providerRoot, isReadOnly: true);
        AssetSourceMount undeclaredConsumer = new(consumerId, consumerRoot, isReadOnly: true);

        Assert.Throws<InvalidDataException>(() => AssetManager.ReplaceSourceMounts(
            [project, provider, undeclaredConsumer]));
        Assert.Collection(
            AssetManager.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));

        AssetSourceMount declaredConsumer = new(
            consumerId,
            consumerRoot,
            isReadOnly: true,
            dependencies: [providerId]);
        AssetManager.ReplaceSourceMounts([project, provider, declaredConsumer]);
        ManagerDependencyAsset root = AssetManager.Load<ManagerDependencyAsset>(
            new AssetPath(consumerId, "root.managerdep"));
        AssetDependency dependency = Assert.Single(AssetManager.GetDependencies(root));

        Assert.Equal(new AssetPath(providerId, "leaf.txt").ToString(), dependency.lastKnownPath);
    }

    [Fact]
    public void ReplaceSourceMounts_WhenPluginIsRemoved_RetiresItsCatalogRecords()
    {
        using TestAssetWorkspace workspace = new();
        AssetManager.Initialize(workspace.options);
        string pluginRoot = workspace.CreateExternalRoot("RemovedPlugin");
        Guid pluginAssetId = Guid.NewGuid();
        WriteReadOnlyAsset(
            pluginRoot,
            "value.txt",
            "plugin",
            pluginAssetId,
            "Inno.Assets.Loader.Importers.TextAssetImporter");
        AssetSourceId pluginId = new("tests.removed");
        AssetSourceMount project = new(AssetSourceId.project, workspace.assetRoot, isReadOnly: false);
        AssetManager.ReplaceSourceMounts(
        [
            project,
            new AssetSourceMount(pluginId, pluginRoot, isReadOnly: true)
        ]);
        Assert.True(AssetManager.TryLoad(pluginAssetId, out TextAsset? loaded));
        Assert.NotNull(loaded);

        AssetManager.ReplaceSourceMounts([project]);

        Assert.Collection(
            AssetManager.sourceMounts,
            mount => Assert.Equal(AssetSourceId.project, mount.id));
        Assert.False(AssetManager.TryLoad(pluginAssetId, out TextAsset? removed));
        Assert.Null(removed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadWithoutEscaping(string relativePath)
    {
        TextAsset asset = AssetManager.Load<TextAsset>(relativePath);
        return new WeakReference(asset);
    }

    private sealed class AssetHolder : ISerializable
    {
        [SerializableProperty]
        public TextAsset? asset { get; set; }
    }

    private sealed class TestAssetWorkspace : IDisposable
    {
        private readonly string m_root = Path.Combine(
            Path.GetTempPath(),
            "InnoAssetManagerTests",
            Guid.NewGuid().ToString("N"));

        internal TestAssetWorkspace()
        {
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(libraryRoot);
        }

        internal string assetRoot => Path.Combine(m_root, "Assets");
        internal string libraryRoot => Path.Combine(m_root, "Library");
        internal AssetManagerOptions options => new()
        {
            assetRoot = assetRoot,
            libraryRoot = libraryRoot,
            enableFileSystemWatcher = false,
            fileWatcherFlushDelayMs = 20,
            sourcePolicy = Inno.Assets.File.AssetSourcePolicy.defaultPolicy,
            cacheOptions = AssetCacheOptions.CreateDefault()
        };

        internal void Write(string relativePath, string content)
        {
            string path = Path.Combine(assetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        internal void Delete(string relativePath)
        {
            string path = Path.Combine(assetRoot, relativePath);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        internal string CreateExternalRoot(string name)
        {
            string path = Path.Combine(m_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            AssetManager.Shutdown();
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
    }

    private static void WriteReadOnlyAsset(
        string root,
        string relativePath,
        string content,
        Guid persistentId,
        string importerId)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        System.IO.File.WriteAllBytes(
            path + ".imeta",
            SerializationManager.Serialize(new TestAssetSourceMeta
            {
                persistentId = persistentId,
                sourceKind = 0,
                importerId = importerId
            }));
    }

    private sealed class TestAssetSourceMeta : ISerializable
    {
        [SerializableProperty]
        public Guid persistentId { get; set; }

        [SerializableProperty]
        public int sourceKind { get; set; }

        [SerializableProperty]
        public string importerId { get; set; } = string.Empty;

        [SerializableProperty]
        public byte[] importerSettingsBytes { get; set; } = [];
    }
}

[StableTypeId("dbb8bd75-4038-457a-8f75-a51194f89750")]
internal sealed class ManagerDependencyAsset : AssetObject;

[AssetImporterExtension]
internal sealed class ManagerDependencyImporter : AssetImporter<ManagerDependencyAsset>
{
    public override string importerId => "inno.tests.manager-dependency";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".managerdep"];

    protected override ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<ManagerDependencyAsset> output,
        CancellationToken cancellationToken)
    {
        string dependency = context.ReadUtf8Text().Trim();
        if (!string.IsNullOrWhiteSpace(dependency))
            output.DependsOnAsset(dependency);
        output.SetAsset(new ManagerDependencyAsset());
        return output.WriteArtifactAsync("runtime", context.sourceBytes, cancellationToken);
    }
}
