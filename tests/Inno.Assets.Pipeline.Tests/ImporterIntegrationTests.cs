using System;
using System.IO;
using System.Text;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Modules;
using Inno.Core.Identity;
using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Pipeline.Tests;

public sealed class ImporterIntegrationTests : IDisposable
{
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly LogRouter m_logs = new();
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_identityScope;

    public ImporterIntegrationTests()
    {
        m_identityScope = m_identities.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(Path.GetTempPath(), "InnoImporterTests", "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_identityScope.Dispose();
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
        using var loader = new AssetLoader(
            m_types,
            m_serialization,
            m_identities,
            m_diagnostics,
            m_logs,
            workspace.assetRoot,
            workspace.libraryRoot);

        AssetPath path = AssetPath.Project(relativePath);
        Assert.True(loader.Import(path));
        TextAsset asset = Assert.IsType<TextAsset>(loader.Load(path, typeof(TextAsset)));
        Assert.Equal("content", asset.content);
        Assert.Equal(languageHint, asset.languageHint);
    }

    [Fact]
    public void BinaryImporter_CreatesRuntimeDescriptor()
    {
        using TestWorkspace workspace = new();
        workspace.Write("Data/blob.bytes", [1, 2, 3, 4]);
        using var loader = new AssetLoader(
            m_types,
            m_serialization,
            m_identities,
            m_diagnostics,
            m_logs,
            workspace.assetRoot,
            workspace.libraryRoot);

        BinaryAsset binary = Assert.IsType<BinaryAsset>(loader.Load(AssetPath.Project("Data/blob.bytes"), typeof(BinaryAsset)));

        Assert.Equal(4, binary.byteLength);
    }

    [Fact]
    public void ExportableBuiltInAsset_RoundTripsThroughPublicLoaderContract()
    {
        using TestWorkspace workspace = new();
        using var loader = new AssetLoader(
            m_types,
            m_serialization,
            m_identities,
            m_diagnostics,
            m_logs,
            workspace.assetRoot,
            workspace.libraryRoot);
        var source = new TextAsset("saved", "plain");

        Assert.True(loader.Save(AssetPath.Project("Text/saved.txt"), source));
        TextAsset loaded = Assert.IsType<TextAsset>(loader.Load(AssetPath.Project("Text/saved.txt"), typeof(TextAsset)));

        Assert.Same(source, loaded);
        Assert.Equal("saved", System.IO.File.ReadAllText(Path.Combine(workspace.assetRoot, "Text/saved.txt")));
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
            Directory.CreateDirectory(libraryRoot);
        }

        internal string assetRoot => Path.Combine(m_root, "Assets");
        internal string libraryRoot => Path.Combine(m_root, "Library");

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
