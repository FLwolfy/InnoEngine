using System;
using System.IO;
using System.Linq;
using System.Numerics;

using Inno.Extensibility.Modules;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
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
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly LogRouter m_logs = new();
    private readonly EditorInteractionRuntime m_runtime;

    public InspectionPipelineTests()
    {
        Directory.CreateDirectory(m_projectRoot);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        m_runtime = new EditorInteractionRuntime(
            new EditorContext(m_projectRoot),
            m_types,
            m_logs,
            [m_types, m_serialization]);
        m_runtime.Start();
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void InlineChildFailureIsConsumedAndReadonlyDisabledScopeRemainsBalanced()
    {
        EditorContext editor = m_runtime.context;
        using var drawers = new PropertyDrawerRegistry(
            m_runtime.interactions,
            m_types,
            m_serialization,
            []);
        var renderer = new SerializedPropertyRenderer(
            drawers,
            m_runtime.interactions,
            new NoopEditService(),
            m_logs);
        Assert.IsType<InlineParentDrawer>(drawers.Resolve(typeof(InlineParent)));
        var owner = new InlineOwner();
        SerializedProperty property = Assert.Single(m_serialization.GetProperties(owner));
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
            try
            {
                renderer.Draw(editor, owner, "owner", property);
                NativeImGui.TextUnformatted("Content after the failing inline child.");
            }
            finally
            {
                NativeImGui.End();
            }
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
