using System;
using System.IO;
using System.Reflection;

using Inno.Adapter.Text.FreeTypeHarfBuzz;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Text.Assets;
using Xunit;

namespace Inno.Text.Tests;

public sealed class TextServiceTests : IDisposable
{
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly IdentityAllocator m_identities = new();
    private readonly IDisposable m_identityScope;
    private readonly string m_root;
    private readonly string m_assets;
    private readonly string m_library;
    private readonly LogRouter m_logs = new();
    private readonly ModuleHost m_modules;
    private readonly SerializationRegistry m_serialization;
    private readonly TypeCatalog m_types;

    public TextServiceTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoTextTests", Guid.NewGuid().ToString("N"));
        m_assets = Path.Combine(m_root, "Assets");
        m_library = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(m_assets);
        m_identityScope = m_identities.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = Assembly.Load("Inno.Text.Assets");
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        m_types.Rebuild();
    }

    [Fact]
    public void MetadataCodecRoundTripsAndRejectsCorruption()
    {
        var expected = new FontMetadata(3, 4096);

        Assert.Equal(expected, FontMetadataCodec.Decode(FontMetadataCodec.Encode(expected)));
        Assert.Throws<InvalidOperationException>(() => FontMetadataCodec.Decode([1, 2, 3]));
    }

    [Fact]
    public void ImporterPublishesMetadataAndSeparateEncodedFont()
    {
        byte[] source = File.ReadAllBytes(FontPath());
        File.WriteAllBytes(Path.Combine(m_assets, "Interface.ttf"), source);
        using AssetLoader loader = CreateLoader();

        FontAsset font = Assert.IsType<FontAsset>(
            loader.Load(AssetPath.Project("Interface.ttf"), typeof(FontAsset)));

        Assert.Equal(1, font.metadata!.Value.faceCount);
        Assert.Equal(source.Length, font.metadata.Value.encodedByteLength);
        Assert.True(loader.TryGetArtifact(font.identity.persistentId, "font-data", out AssetArtifactInfo? data));
        Assert.NotNull(data);
        Assert.Equal(source, File.ReadAllBytes(data.absolutePath));
    }

    [Fact]
    public void NativeBackendShapesAndRasterizesUnicodeText()
    {
        using var backend = new FreeTypeHarfBuzzTextBackend();
        TextFontHandle font = backend.LoadFont(File.ReadAllBytes(FontPath()), 0);

        TextLayout layout = backend.Shape(
            font,
            "Hello, UI!",
            new TextStyle(28f),
            TextShapingOptions.automatic);

        Assert.NotEmpty(layout.glyphs);
        Assert.True(layout.width > 0f);
        Assert.True(layout.metrics.lineHeight > 0f);
        GlyphBitmap bitmap = backend.Rasterize(font, layout.glyphs[0].glyphId, 28f);
        Assert.True(bitmap.width > 0);
        Assert.True(bitmap.height > 0);
        Assert.Equal(bitmap.width * bitmap.height, bitmap.pixels.Length);
        backend.ReleaseFont(font);
    }

    public void Dispose()
    {
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        m_identityScope.Dispose();
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    private AssetLoader CreateLoader()
        => new(m_types, m_serialization, m_identities, m_diagnostics, m_logs, m_assets, m_library);

    private static string FontPath()
        => Path.Combine(AppContext.BaseDirectory, "TestData", "LatoLatin-Regular.ttf");
}
