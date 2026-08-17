using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Inno.Assets.Core;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Tests;

public sealed class AssetManagerTests : IDisposable
{
    public AssetManagerTests()
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
    public void PublicApis_RejectUseBeforeInitialization()
    {
        Assert.Throws<InvalidOperationException>(() => AssetManager.Import("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Load<TextAsset>("Missing/nope.txt"));
        Assert.Throws<InvalidOperationException>(() => AssetManager.Load<TextAsset>(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => AssetManager.GetLoadedPaths());
        Assert.False(AssetManager.Unload("Missing/nope.txt"));
    }

    [Fact]
    public void PathAndPersistentIdLoads_ShareOneInstanceAndCountManualHolds()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/shared.txt", "shared");
        AssetManager.Initialize(workspace.options);
        Assert.True(AssetManager.Import("Text/shared.txt"));
        Assert.True(AssetManager.TryGetPersistentId("Text/shared.txt", out Guid persistentId));

        TextAsset first = AssetManager.Load<TextAsset>("Text/shared.txt");
        TextAsset second = AssetManager.Load<TextAsset>(persistentId);

        Assert.Same(first, second);
        Assert.Equal("shared", first.content);
        Assert.True(AssetManager.Unload(first));
        Assert.Single(AssetManager.GetLoadedPaths());
        Assert.True(AssetManager.Unload(second));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void AutomaticallyDiscoveredImporter_AndOwnerHold_RetainTransitiveDependencies()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/dependency.txt", "dependency");
        workspace.Write("Graphs/root.dependency", "Text/dependency.txt");
        AssetManager.Initialize(workspace.options);
        Assert.True(AssetManager.Import("Text/dependency.txt"));
        Assert.True(AssetManager.Import("Graphs/root.dependency"));

        DependencyAsset root = AssetManager.Load<DependencyAsset>("Graphs/root.dependency");
        var owner = new TestOwner();
        Assert.True(IdentityManager.Register(owner));
        AssetManager.TrackDependencies(owner, root);
        Assert.True(AssetManager.Unload(root));

        Assert.Equal(
            new[] { "Graphs/root.dependency", "Text/dependency.txt" },
            AssetManager.GetLoadedPaths().OrderBy(static path => path, StringComparer.Ordinal));

        Assert.True(IdentityManager.Unregister(owner));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void DirectAssetReference_RestoresMissingPlaceholderAndCanBeSavedAgain()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/missing.txt", "value");
        AssetManager.Initialize(workspace.options);
        Assert.True(AssetManager.Import("Text/missing.txt"));
        TextAsset loaded = AssetManager.Load<TextAsset>("Text/missing.txt");
        Guid persistentId = loaded.identity.persistentId;
        byte[] bytes = SerializationManager.Serialize(new AssetHolder { asset = loaded });
        Assert.True(AssetManager.Unload(loaded));

        workspace.Delete("Text/missing.txt");
        workspace.Delete("Text/missing.txt.imeta");
        workspace.DeleteArtifact("Text/missing.txt.abin");

        AssetHolder restored = SerializationManager.Deserialize<AssetHolder>(bytes);

        Assert.NotNull(restored.asset);
        Assert.True(restored.asset!.isMissing);
        Assert.Equal(persistentId, restored.asset.identity.persistentId);
        Assert.Equal("Text/missing.txt", restored.asset.sourcePath);
        Assert.NotEmpty(SerializationManager.Serialize(restored));
    }

    [Fact]
    public void TrackSerializedReferences_IncludesHiddenSerializableMembers()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Text/hidden.txt", "hidden");
        AssetManager.Initialize(workspace.options);
        Assert.True(AssetManager.Import("Text/hidden.txt"));
        TextAsset asset = AssetManager.Load<TextAsset>("Text/hidden.txt");
        var owner = new TestOwner();
        Assert.True(IdentityManager.Register(owner));

        AssetManager.TrackSerializedReferences(owner, new HiddenAssetHolder(asset));
        Assert.True(AssetManager.Unload(asset));
        Assert.Single(AssetManager.GetLoadedPaths());

        Assert.True(IdentityManager.Unregister(owner));
        Assert.Empty(AssetManager.GetLoadedPaths());
    }

    [Fact]
    public void Import_RejectsDependencyCycleWithCompletePathChain()
    {
        using TestAssetWorkspace workspace = new();
        workspace.Write("Graphs/a.dependency", "Graphs/b.dependency");
        AssetManager.Initialize(workspace.options);
        Assert.True(AssetManager.Import("Graphs/a.dependency"));
        workspace.Write("Graphs/b.dependency", "Graphs/a.dependency");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AssetManager.Import("Graphs/b.dependency"));

        Assert.Contains("Graphs/a.dependency", exception.Message);
        Assert.Contains("Graphs/b.dependency", exception.Message);
        Assert.Contains("->", exception.Message);
    }

    private sealed class AssetHolder : ISerializable
    {
        [SerializableProperty]
        public TextAsset? asset { get; set; }
    }

    private sealed class HiddenAssetHolder(TextAsset asset) : ISerializable
    {
        [SerializableProperty(PropertyVisibility.Hide)]
        private TextAsset? m_asset = asset;

        internal TextAsset? Value => m_asset;
    }

    private sealed class TestOwner : IIdentityObject;

    private sealed class TestAssetWorkspace : IDisposable
    {
        private readonly string m_root = Path.Combine(
            Path.GetTempPath(),
            "InnoAssetManagerTests",
            Guid.NewGuid().ToString("N"));

        internal TestAssetWorkspace()
        {
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(artifactRoot);
        }

        internal string assetRoot => Path.Combine(m_root, "Assets");
        internal string artifactRoot => Path.Combine(m_root, "Artifacts");
        internal AssetManagerOptions options => AssetManagerOptions.Create(assetRoot, artifactRoot);

        internal void Write(string relativePath, string content)
        {
            string path = Path.Combine(assetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        internal void Delete(string relativePath)
        {
            string path = Path.Combine(assetRoot, relativePath);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        internal void DeleteArtifact(string relativePath)
        {
            string path = Path.Combine(artifactRoot, relativePath);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }

        public void Dispose()
        {
            AssetManager.Shutdown();
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
    }
}

[StableTypeId("dbb8bd75-4038-457a-8f75-a51194f89750")]
internal sealed class DependencyAsset : AssetObject;

internal sealed class DependencyAssetImporter : AssetImporter<DependencyAsset>
{
    private static readonly IReadOnlyList<string> C_EXTENSIONS = new[] { ".dependency" };

    public override string importerId => "inno.tests.dependency";
    public override IReadOnlyList<string> supportedExtensions => C_EXTENSIONS;

    public override AssetImportResult<DependencyAsset> ImportTyped(in AssetImportContext context)
    {
        string dependency = context.ReadUtf8Text().Trim();
        return new AssetImportResult<DependencyAsset>(
            new DependencyAsset(),
            context.sourceBytes.ToArray(),
            string.IsNullOrWhiteSpace(dependency) ? [] : new[] { dependency });
    }
}
