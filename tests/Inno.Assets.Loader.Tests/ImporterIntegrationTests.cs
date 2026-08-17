using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Assemblies;
using Inno.Core.Identity;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Loader.Tests;

public sealed class ImporterIntegrationTests : IDisposable
{
    public ImporterIntegrationTests()
    {
        IdentityManager.Initialize();
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoImporterTests", "Assemblies")
        });
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        AssemblyManager.Shutdown();
        IdentityManager.Shutdown();
    }

    [Theory]
    [InlineData("Text/a.txt", "plain")]
    [InlineData("Text/a.json", "json")]
    [InlineData("Text/a.yaml", "yaml")]
    [InlineData("Text/a.yml", "yaml")]
    [InlineData("Text/a.md", "markdown")]
    [InlineData("Text/a.xml", "xml")]
    public void TextExtensions_AreAutomaticallyDiscovered(string relativePath, string languageHint)
    {
        using TestWorkspace workspace = new();
        workspace.Write(relativePath, Encoding.UTF8.GetBytes("content"));
        using var loader = new AssetLoader(workspace.assetRoot, workspace.artifactRoot);

        Assert.True(loader.Import(relativePath));
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load(relativePath, typeof(TextAsset)));
        Assert.Equal("content", asset.content);
        Assert.Equal(languageHint, asset.languageHint);
    }

    [Fact]
    public void BinaryImporter_CreatesRuntimeDescriptor()
    {
        using TestWorkspace workspace = new();
        workspace.Write("Data/blob.bytes", [1, 2, 3, 4]);
        using var loader = new AssetLoader(workspace.assetRoot, workspace.artifactRoot);

        BinaryAsset binary = Assert.IsType<BinaryAsset>(loader.Load("Data/blob.bytes", typeof(BinaryAsset)));

        Assert.Equal(4, binary.byteLength);
    }

    [Fact]
    public void ExportableBuiltInAsset_RoundTripsThroughPublicLoaderContract()
    {
        using TestWorkspace workspace = new();
        using var loader = new AssetLoader(workspace.assetRoot, workspace.artifactRoot);
        var source = new TextAsset("saved", "plain");

        Assert.True(loader.Save("Text/saved.txt", source));
        TextAsset loaded = Assert.IsType<TextAsset>(loader.Load("Text/saved.txt", typeof(TextAsset)));

        Assert.Same(source, loaded);
        Assert.Equal("saved", System.IO.File.ReadAllText(Path.Combine(workspace.assetRoot, "Text/saved.txt")));
    }

    [Fact]
    public void BuiltInImporters_AreInternalAttributedAndOperationStateless()
    {
        Type[] importerTypes = typeof(AssetLoader).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(AssetImporter).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(importerTypes);
        Assert.All(importerTypes, static type =>
        {
            Assert.False(type.IsPublic);
            Assert.NotNull(type.GetCustomAttribute<AssetImporterExtensionAttribute>());
            Assert.DoesNotContain(
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                static field => !field.IsInitOnly);
        });
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string m_root = Path.Combine(
            Path.GetTempPath(),
            "InnoAssetsImporterTests",
            Guid.NewGuid().ToString("N"));

        internal TestWorkspace()
        {
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(artifactRoot);
        }

        internal string assetRoot => Path.Combine(m_root, "Assets");
        internal string artifactRoot => Path.Combine(m_root, "Artifacts");

        internal void Write(string relativePath, byte[] bytes)
        {
            string path = Path.Combine(assetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
    }
}
