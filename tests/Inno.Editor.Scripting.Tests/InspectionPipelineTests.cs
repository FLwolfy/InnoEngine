using System;
using System.IO;
using System.Linq;
using System.Numerics;

using Inno.Core.Assemblies;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.ImGui;
using EditorWidget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;
using Inno.Editor.Inspection;
using Inno.Editor.Interactions;
using NativeImGui = Inno.Native.ImGui.ImGui;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class InspectionPipelineTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoInspectionPipelineTests",
        Guid.NewGuid().ToString("N"));

    public InspectionPipelineTests()
    {
        Directory.CreateDirectory(m_projectRoot);
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void InlineChildFailureIsConsumedAndReadonlyDisabledScopeRemainsBalanced()
    {
        var editor = new EditorContext(m_projectRoot);
        var interactions = (EditorInteractions)ScriptingTestReflection.Create(
            typeof(EditorInteractions),
            editor);
        using var drawers = new PropertyDrawerRegistry(interactions);
        var renderer = new SerializedPropertyRenderer(drawers, interactions, new NoopEditService());
        var owner = new InlineOwner();
        SerializedProperty property = Assert.Single(SerializationManager.GetProperties(owner));
        InlineParentDrawer.drewAfterFailure = false;
        var nativeContext = NativeImGui.CreateContext();
        try
        {
            Inno.Native.ImGui.ImGuiIOPtr io = NativeImGui.GetIO();
            io.DisplaySize = new Vector2(640f, 480f);
            io.DeltaTime = 1f / 60f;
            io.BackendFlags |= Inno.Native.ImGui.ImGuiBackendFlags.RendererHasTextures;
            io.Fonts.RendererHasTextures = true;
            float originalAlpha = NativeImGui.GetStyle().Alpha;

            NativeImGui.NewFrame();
            _ = NativeImGui.Begin("Inline Property Test");
            renderer.Draw(editor, owner, "owner", property);
            NativeImGui.TextUnformatted("Content after the failing inline child.");
            NativeImGui.End();
            NativeImGui.Render();

            Assert.True(InlineParentDrawer.drewAfterFailure);
            Assert.Equal(originalAlpha, NativeImGui.GetStyle().Alpha);
        }
        finally
        {
            NativeImGui.DestroyContext(nativeContext);
        }
    }

    [Fact]
    public void PanelWindowFailureStillEndsTheWindowAndAllowsLaterContent()
    {
        var nativeContext = NativeImGui.CreateContext();
        try
        {
            PrepareNativeFrame();
            bool isOpen = true;

            Assert.Throws<InvalidOperationException>(() => EditorWidget.PanelWindow(
                "Throwing Panel",
                ref isOpen,
                static () => throw new InvalidOperationException("panel")));
            _ = NativeImGui.Begin("Content After Panel Failure");
            NativeImGui.TextUnformatted("Still drawing.");
            NativeImGui.End();
            NativeImGui.Render();
        }
        finally
        {
            NativeImGui.DestroyContext(nativeContext);
        }
    }

    [Fact]
    public void ModalFailureIsQuarantinedAndPopupStyleStackRemainsBalanced()
    {
        var nativeContext = NativeImGui.CreateContext();
        try
        {
            PrepareNativeFrame();
            Exception? quarantined = null;
            var modal = (EditorModalExtension)ScriptingTestReflection.Create(
                typeof(EditorModalExtension),
                "tests.throwing-modal",
                "Throwing Modal",
                0,
                new ThrowingModal(),
                new Action<Exception>(exception => quarantined = exception));
            Assert.True(modal.TryGetPresentation(out EditorModalExtension.Presentation presentation));

            Type rendererType = typeof(EditorWidget).Assembly.GetType(
                "Inno.Editor.ImGui.EditorModalRenderer",
                throwOnError: true)!;
            _ = ScriptingTestReflection.InvokeStatic<object?>(
                rendererType,
                "Draw",
                modal.id,
                modal.title,
                1f,
                modal,
                presentation,
                new EditorContext(m_projectRoot));
            _ = NativeImGui.Begin("Content After Modal Failure");
            NativeImGui.TextUnformatted("Still drawing.");
            NativeImGui.End();
            NativeImGui.Render();

            Assert.IsType<InvalidOperationException>(quarantined);
            Assert.False(modal.TryGetPresentation(out _));
            Assert.Equal(1f, NativeImGui.GetStyle().Alpha);
        }
        finally
        {
            NativeImGui.DestroyContext(nativeContext);
        }
    }

    private static void PrepareNativeFrame()
    {
        Inno.Native.ImGui.ImGuiIOPtr io = NativeImGui.GetIO();
        io.DisplaySize = new Vector2(640f, 480f);
        io.DeltaTime = 1f / 60f;
        io.BackendFlags |= Inno.Native.ImGui.ImGuiBackendFlags.RendererHasTextures;
        io.Fonts.RendererHasTextures = true;
        NativeImGui.NewFrame();
    }

    private sealed class NoopEditService : IInspectionPropertyEditService
    {
        public bool ChangeProperty(
            object owner,
            string propertyName,
            Action mutation,
            string historyName)
        {
            mutation();
            return true;
        }
    }
}

internal sealed class ThrowingModal : EditorModal
{
    public override bool isVisible => true;

    protected override void OnDraw(EditorContext context)
        => throw new InvalidOperationException("modal");
}

internal sealed class InlineOwner : ISerializable
{
    [SerializableProperty]
    public InlineParent value { get; set; } = new();
}

internal sealed class InlineParent
{
    internal InlineFailure child { get; } = new();
}

internal sealed class InlineFailure;

[PropertyDrawer(typeof(InlineParent), priority: 10_000)]
internal sealed class InlineParentDrawer : IPropertyDrawer
{
    internal static bool drewAfterFailure;

    public void Draw(PropertyDrawContext context)
    {
        var parent = (InlineParent)context.GetValue()!;
        context.DrawInlineChild(
            "child",
            typeof(InlineFailure),
            () => parent.child,
            static _ => { },
            readOnly: true);
        drewAfterFailure = true;
    }
}

[PropertyDrawer(typeof(InlineFailure), priority: 10_000)]
internal sealed class InlineFailureDrawer : IPropertyDrawer
{
    public void Draw(PropertyDrawContext context)
        => throw new InvalidOperationException("Inline drawer failure.");
}
