using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Inno.Assets.Importers;
using Inno.Assets.Loader;
using Inno.Assets.Types;
using Inno.Core.Identity;
using Inno.Core.Reflection;
using Inno.Core.Serialization;

using Xunit;

namespace Inno.Assets.Importers.Tests;

public sealed class ImporterIntegrationTests : IDisposable
{
    public ImporterIntegrationTests()
    {
        _ = typeof(BuiltInAssetImporterPackage).Assembly;
        IdentityManager.Initialize();
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
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

    [Theory]
    [InlineData("Shaders/a.vert", "vertex")]
    [InlineData("Shaders/a.frag", "fragment")]
    [InlineData("Shaders/a.comp", "compute")]
    [InlineData("Shaders/a.glsl", "generic")]
    public void ShaderExtensions_AreAutomaticallyDiscovered(string relativePath, string stage)
    {
        using TestWorkspace workspace = new();
        workspace.Write(relativePath, Encoding.UTF8.GetBytes("void main(){}"));
        using var loader = new AssetLoader(workspace.assetRoot, workspace.artifactRoot);

        ShaderAsset asset = Assert.IsType<ShaderAsset>(loader.Load(relativePath, typeof(ShaderAsset)));
        Assert.Equal(stage, asset.stage);
    }

    [Fact]
    public void BinaryAndPngImporters_CreateRuntimeDescriptors()
    {
        using TestWorkspace workspace = new();
        workspace.Write("Data/blob.bytes", [1, 2, 3, 4]);
        workspace.Write("Textures/image.png", CreatePngHeader(width: 7, height: 9));
        using var loader = new AssetLoader(workspace.assetRoot, workspace.artifactRoot);

        BinaryAsset binary = Assert.IsType<BinaryAsset>(loader.Load("Data/blob.bytes", typeof(BinaryAsset)));
        TextureAsset texture = Assert.IsType<TextureAsset>(loader.Load("Textures/image.png", typeof(TextureAsset)));

        Assert.Equal(4, binary.byteLength);
        Assert.Equal(7, texture.width);
        Assert.Equal(9, texture.height);
        Assert.Equal("png", texture.encoding);
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
        Type[] importerTypes = typeof(BuiltInAssetImporterPackage).Assembly
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

    private static byte[] CreatePngHeader(int width, int height)
    {
        byte[] bytes = new byte[24];
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
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
