using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Inno.Adapter.UI.RmlUi;
using Inno.Adapter.UI.RmlUi.Authoring;
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
using Inno.UI.Runtime;
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
        _ = Assembly.Load("Inno.Text.Assets");
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
    public void RmlFrontendScopesFontFamiliesAndHonorsNone()
    {
        var frontend = new RmlUiDocumentFrontend();
        UiDocumentAnalysis first = frontend.Analyze(new("Hud.rml", """
            <rml><head><style>
            @font-face { font-family: Interface; src: url(Lato.ttf); font-weight: 400; }
            body { font-family: Interface; font-size: 18px; }
            .hidden { font-family: none; }
            </style></head><body/></rml>
            """));
        UiDocumentAnalysis second = frontend.Analyze(new("Other.rml", """
            <rml><head><style>
            @font-face { font-family: Interface; src: url(Lato.ttf); font-weight: 400; }
            body { font-family: Interface; }
            </style></head><body/></rml>
            """));
        Assert.True(first.succeeded);
        Assert.True(second.succeeded);
        UiDocumentFontDeclaration face = Assert.Single(first.fonts);
        Assert.Equal("Lato.ttf", face.assetPath);
        Assert.Contains("font-family: " + face.family, first.text);
        Assert.DoesNotContain("@font-face", first.text);
        Assert.DoesNotContain("font-family: none", first.text);
        Assert.NotEqual(face.family, Assert.Single(second.fonts).family);
    }

    [Fact]
    public void UndeclaredFontFamilyProducesNoGlyphDraw()
    {
        using var backend = new RmlUiBackend();
        backend.RegisterFont(new(File.ReadAllBytes(FontPath()), 0, "Declared", TextFontStyle.Normal, 400));
        UiContextHandle context = backend.CreateContext(new UiContextOptions("missing-font", 320, 180));
        UiDocumentHandle document = backend.LoadDocument(context, Rml("""
            <rml><head><style>body { margin: 0; font-family: __inno_ui_undeclared; font-size: 24px; }</style></head>
            <body>Invisible text</body></rml>
            """));
        backend.ShowDocument(context, document);
        backend.Update(context, EmptyInput());
        Assert.Empty(backend.Render(context).commands);
    }

    [Fact]
    public void ElementHitTestIgnoresBlankCanvasAndPointerEventsNone()
    {
        using var backend = new RmlUiBackend();
        UiContextHandle context = backend.CreateContext(new UiContextOptions("hit-test", 320, 180));
        UiDocumentHandle document = backend.LoadDocument(context, Rml("""
            <rml><head><style>
              body { margin: 0; }
              #button { position: absolute; left: 20px; top: 20px; width: 60px; height: 30px; background-color: red; }
              #ghost { position: absolute; left: 100px; top: 20px; width: 60px; height: 30px; pointer-events: none; }
            </style></head><body><div id="button"></div><div id="ghost"></div></body></rml>
            """));
        backend.ShowDocument(context, document);
        backend.Update(context, EmptyInput());
        Assert.True(backend.HasElementAtPoint(context, new Inno.Core.Mathematics.Vector2(30f, 30f)));
        Assert.False(backend.HasElementAtPoint(context, new Inno.Core.Mathematics.Vector2(120f, 30f)));
        Assert.False(backend.HasElementAtPoint(context, new Inno.Core.Mathematics.Vector2(200f, 100f)));
    }

    [Fact]
    public void SwitchingDocumentFamiliesThroughNoneDoesNotReuseOldGlyphs()
    {
        using var backend = new RmlUiBackend();
        var frontend = new RmlUiDocumentFrontend();
        byte[] font = File.ReadAllBytes(FontPath());
        UiContextHandle context = backend.CreateContext(new UiContextOptions("font-switch", 320, 180));
        foreach ((string family, bool visible) in new[]
                 {
                     ("A", true), ("B", true), ("none", false), ("A", true)
                 })
        {
            string face = family == "none" ? string.Empty :
                $"@font-face {{ font-family: {family}; src: url(Lato.ttf); font-weight: 400; }}";
            UiDocumentAnalysis analysis = frontend.Analyze(new("FontSwitch.rml",
                $"<rml><head><style>{face}body {{ margin: 0; font-family: {family}; font-size: 24px; }}</style></head><body>Visible text</body></rml>"));
            Assert.True(analysis.succeeded);
            foreach (UiDocumentFontDeclaration declaration in analysis.fonts)
                backend.RegisterFont(new(font, 0, declaration.family, declaration.style, declaration.weight));
            UiDocumentHandle document = backend.LoadDocument(context, Rml(analysis.text!));
            backend.ShowDocument(context, document);
            backend.Update(context, EmptyInput());
            Assert.Equal(visible, backend.Render(context).commands.Count > 0);
            backend.CloseDocument(context, document);
        }
    }

    [Fact]
    public void CssWeightSelectsDifferentFacesOfOneDeclaredFamily()
    {
        using var backend = new RmlUiBackend();
        backend.RegisterFont(new(File.ReadAllBytes(FontPath()), 0,
            "WeightFamily", TextFontStyle.Normal, 400));
        backend.RegisterFont(new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "LatoLatin-Bold.ttf")), 0,
            "WeightFamily", TextFontStyle.Normal, 700));
        UiContextHandle context = backend.CreateContext(new UiContextOptions("weight-selection", 640, 240));
        UiDocumentHandle document = backend.LoadDocument(context, Rml("""
            <rml><head><style>
              body { margin: 0; font-family: WeightFamily; font-size: 30px; }
              #regular { font-weight: 400; }
              #bold { font-weight: 700; }
            </style></head><body>
              <div id="regular">Wide letters WWW</div>
              <div id="bold">Wide letters WWW</div>
            </body></rml>
            """));
        backend.ShowDocument(context, document);
        backend.Update(context, EmptyInput());
        UiRenderFrame frame = backend.Render(context);
        Assert.True(frame.commands.Where(command => command.texture.isValid)
            .Select(command => command.texture).Distinct().Count() >= 2);
        backend.CloseDocument(context, document);
        backend.DestroyContext(context);
    }

    [Fact]
    public void DestroyingAContextReleasesItsImportedFontArtifact()
    {
        File.Copy(FontPath(), Path.Combine(m_assets, "Lato.ttf"));
        File.WriteAllText(Path.Combine(m_assets, "Hud.rml"), """
            <rml><head><style>
              @font-face { font-family: Interface; src: url(Lato.ttf); font-weight: 400; }
              body { font-family: Interface; font-size: 24px; }
            </style></head><body>HUD</body></rml>
            """);
        using AssetLoader loader = CreateLoader();
        UiDocumentAsset document = Assert.IsType<UiDocumentAsset>(
            loader.Load(AssetPath.Project("Hud.rml"), typeof(UiDocumentAsset)));
        var artifacts = new RetainingArtifactLookup(loader);
        using var runtime = new UiRuntime(new RmlUiBackend(), artifacts);
        runtime.Attach();
        UiContextHandle context = runtime.CreateContext(new UiContextOptions("font-retirement", 320, 180));
        UiDocumentHandle loaded = runtime.LoadDocument(context, document);
        runtime.ShowDocument(context, loaded);
        UiContextHandle second = runtime.CreateContext(new UiContextOptions("font-retirement-second", 320, 180));
        UiDocumentHandle secondDocument = runtime.LoadDocument(second, document);
        runtime.ShowDocument(second, secondDocument);
        Assert.Single(artifacts.retainedKeys);
        runtime.DestroyContext(context);
        Assert.Single(artifacts.retainedKeys);
        runtime.DestroyContext(second);
        Assert.Empty(artifacts.retainedKeys);
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
            400));
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

        first.RegisterFont(new(font, 0, "SharedLato", TextFontStyle.Normal, 400));
        second.RegisterFont(new(font, 0, "SharedLato", TextFontStyle.Normal, 400));
    }

    [Fact]
    public void NativeBackendRetiresContextsWithLiveDocumentsAndTextures()
    {
        using var backend = new RmlUiBackend();
        backend.RegisterFont(new(File.ReadAllBytes(FontPath()), 0, "RetirementFont", TextFontStyle.Normal, 400));
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

    private sealed class RetainingArtifactLookup(AssetLoader loader) : IAssetArtifactLookup
    {
        private readonly ArtifactRetention m_retention = new();

        internal IReadOnlyList<AssetArtifactKey> retainedKeys => m_retention.GetRetainedKeys();

        public ArtifactLease AcquireArtifact(Guid persistentId, string outputName)
        {
            if (!TryGetArtifact(persistentId, outputName, out AssetArtifactInfo? artifact)
                || artifact is null)
                throw new InvalidOperationException("The requested test font artifact is unavailable.");
            return m_retention.Retain(artifact);
        }

        public bool TryGetArtifact(Guid persistentId, string outputName,
            out AssetArtifactInfo? artifact)
            => loader.TryGetArtifact(persistentId, outputName, out artifact);
    }
}
