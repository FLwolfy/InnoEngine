using System;
using System.IO;
using System.Reflection;

using Inno.Adapter.UI.RmlUi;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Diagnostics;
using Inno.Core.Identity;
using Inno.Core.Input;
using Inno.Core.Logging;
using Inno.Core.Mathematics;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Text;
using Inno.UI.Assets;
using Xunit;

namespace Inno.UI.Tests;

public sealed class UiServiceTests : IDisposable
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

    public UiServiceTests()
    {
        m_root = Path.Combine(Path.GetTempPath(), "InnoUiTests", Guid.NewGuid().ToString("N"));
        m_assets = Path.Combine(m_root, "Assets");
        m_library = Path.Combine(m_root, "Library");
        Directory.CreateDirectory(m_assets);
        m_identityScope = m_identities.EnterScope();
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_root, "Assemblies")
        });
        _ = Assembly.Load("Inno.UI.Assets");
        _ = Assembly.Load("Inno.Adapter.UI.RmlUi.Authoring");
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        m_types.Rebuild();
    }

    [Fact]
    public void ImporterPublishesValidatedUtf8Document()
    {
        const string markup = "<rml><head/><body><div>Hello</div></body></rml>";
        File.WriteAllText(Path.Combine(m_assets, "Hud.rml"), markup);
        using AssetLoader loader = CreateLoader();

        UiDocumentAsset document = Assert.IsType<UiDocumentAsset>(
            loader.Load(AssetPath.Project("Hud.rml"), typeof(UiDocumentAsset)));

        Assert.Equal(markup, Assert.IsType<UiDocumentSource>(document.source).text);
        Assert.Equal(RmlUiIdentifiers.documentLanguage, document.source.language);
        Assert.Equal(RmlUiIdentifiers.backend.value, document.implementationId);
    }

    [Fact]
    public void NativeBackendBuildsGeometryAndDrainsDomEvents()
    {
        using var backend = new RmlUiBackend();
        backend.RegisterFont(new(
            File.ReadAllBytes(FontPath()),
            0,
            "Lato",
            TextFontStyle.Normal,
            400,
            fallback: false));
        UiContextHandle context = backend.CreateContext(new UiContextOptions("test", 640, 360));
        UiDocumentHandle document = backend.LoadDocument(context, Rml("""
            <rml>
              <head>
                <style>
                  body { margin: 0; font-family: Lato; font-size: 24px; }
                  #action { width: 180px; height: 64px; background-color: #3366cc; color: white; }
                  .active { background-color: #22aa66; }
                </style>
              </head>
              <body><button id="action">Launch</button></body>
            </rml>
            """));
        backend.ShowDocument(context, document);

        Assert.True(backend.SetClass(context, document, "action", "active", true));
        Assert.True(backend.SetAttribute(context, document, "action", "data-state", "ready"));
        backend.Update(context, EmptyInput());
        UiRenderFrame frame = backend.Render(context);

        Assert.NotEmpty(frame.meshUpdates);
        Assert.NotEmpty(frame.commands);
        Assert.NotEmpty(frame.textureUpdates);
        backend.Update(context, EmptyInput());
        UiRenderFrame unchanged = backend.Render(context);
        Assert.Empty(unchanged.meshUpdates);
        Assert.Empty(unchanged.textureUpdates);
        Assert.NotEmpty(unchanged.commands);

        backend.Update(context, new UiInputSnapshot(
            new Vector2(20f, 20f),
            default,
            KeyModifier.None,
            [],
            [],
            [MouseButton.Left],
            [MouseButton.Left],
            []));
        Assert.Contains(backend.DrainEvents(context), value =>
            value.type == UiEventType.Click && value.targetId == "action");

        backend.CloseDocument(context, document);
        backend.DestroyContext(context);
    }

    [Fact]
    public void MultipleBackendsAcceptTheSameGlobalFontRegistration()
    {
        byte[] font = File.ReadAllBytes(FontPath());
        using var first = new RmlUiBackend();
        using var second = new RmlUiBackend();

        first.RegisterFont(new(font, 0, "SharedLato", TextFontStyle.Normal, 400, false));
        second.RegisterFont(new(font, 0, "SharedLato", TextFontStyle.Normal, 400, false));
    }

    [Fact]
    public void NativeBackendRetiresContextsWithLiveDocumentsAndTextures()
    {
        using var backend = new RmlUiBackend();
        backend.RegisterFont(new(File.ReadAllBytes(FontPath()), 0, "RetirementFont", TextFontStyle.Normal, 400, false));
        for (int index = 0; index < 8; index++)
        {
            UiContextHandle context = backend.CreateContext(new UiContextOptions($"retire-{index}", 320, 180));
            UiDocumentHandle document = backend.LoadDocument(context, Rml("""
                <rml><head><style>
                body { margin: 0; font-family: RetirementFont; font-size: 24px; }
                #panel { width: 100px; height: 50px; background-color: #3366cc; }
                </style></head><body><div id="panel">Panel</div></body></rml>
                """));
            backend.ShowDocument(context, document);
            backend.Update(context, EmptyInput());
            Assert.NotEmpty(backend.Render(context).commands);

            backend.DestroyContext(context);
        }
    }

    [Fact]
    public void NativeHandlesCannotAliasAfterContextOrBackendRetirement()
    {
        UiContextHandle retired;
        UiDocumentHandle retiredDocument;
        using (var first = new RmlUiBackend())
        {
            retired = first.CreateContext(new UiContextOptions("first", 128, 128));
            retiredDocument = first.LoadDocument(retired, Rml(
                "<rml><head/><body><div>First</div></body></rml>"));
        }

        using var second = new RmlUiBackend();
        UiContextHandle current = second.CreateContext(new UiContextOptions("second", 128, 128));
        UiDocumentHandle currentDocument = second.LoadDocument(current, Rml(
            "<rml><head/><body><div>Second</div></body></rml>"));

        Assert.NotEqual(retired, current);
        Assert.NotEqual(retiredDocument, currentDocument);
        Assert.Throws<InvalidOperationException>(() => second.ShowDocument(retired, retiredDocument));
        Assert.Throws<InvalidOperationException>(() => second.ShowDocument(current, retiredDocument));
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

    private static UiInputSnapshot EmptyInput()
        => new(default, default, KeyModifier.None, [], [], [], [], []);

    private static UiDocumentSource Rml(string text)
        => new(RmlUiIdentifiers.documentLanguage, text);

    private static string FontPath()
        => Path.Combine(AppContext.BaseDirectory, "TestData", "LatoLatin-Regular.ttf");
}
