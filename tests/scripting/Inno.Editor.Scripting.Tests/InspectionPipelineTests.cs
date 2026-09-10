using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Inno.Extensibility.Modules;
using Inno.Core.Logging;
using Inno.Extensibility.Types;
using Inno.Core.Serialization;
using Inno.Editor.Annotations;
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
        using var attributes = new InspectorAttributeDrawerRegistry(
            m_runtime.interactions,
            m_types,
            m_serialization,
            []);
        var renderer = new SerializedPropertyRenderer(
            drawers,
            attributes,
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

    [Fact]
    public void TextEditStateIsScopedByRendererOwnerAndPropertyPath()
    {
        EditorContext editor = m_runtime.context;
        using var drawers = new PropertyDrawerRegistry(
            m_runtime.interactions,
            m_types,
            m_serialization,
            []);
        using var attributes = new InspectorAttributeDrawerRegistry(
            m_runtime.interactions,
            m_types,
            m_serialization,
            []);
        var firstRenderer = new SerializedPropertyRenderer(
            drawers,
            attributes,
            m_runtime.interactions,
            new NoopEditService(),
            m_logs);
        var secondRenderer = new SerializedPropertyRenderer(
            drawers,
            attributes,
            m_runtime.interactions,
            new NoopEditService(),
            m_logs);
        var firstOwner = new TextStateOwner { value = new TextStateValue("First") };
        var secondOwner = new TextStateOwner { value = new TextStateValue("Second") };
        SerializedProperty firstProperty = Assert.Single(m_serialization.GetProperties(firstOwner));
        SerializedProperty secondProperty = Assert.Single(m_serialization.GetProperties(secondOwner));
        TextStateDrawer.observations.Clear();
        var nativeContext = NativeImGui.CreateContext();
        try
        {
            DrawPropertyFrame(firstRenderer, editor, firstOwner, firstProperty);
            DrawPropertyFrame(firstRenderer, editor, firstOwner, firstProperty);
            DrawPropertyFrame(firstRenderer, editor, secondOwner, secondProperty);
            DrawPropertyFrame(secondRenderer, editor, firstOwner, firstProperty);
        }
        finally
        {
            NativeImGui.DestroyContext(nativeContext);
        }

        Assert.Equal(
            ["First:<new>", "First:First", "Second:<new>", "First:<new>"],
            TextStateDrawer.observations);
    }

    [Fact]
    public void PresentationAttributesComposeAndCustomDrawersRemainExtensible()
    {
        EditorContext editor = m_runtime.context;
        using var drawers = new PropertyDrawerRegistry(
            m_runtime.interactions,
            m_types,
            m_serialization,
            []);
        using var attributes = new InspectorAttributeDrawerRegistry(
            m_runtime.interactions,
            m_types,
            m_serialization,
            []);
        var renderer = new SerializedPropertyRenderer(
            drawers,
            attributes,
            m_runtime.interactions,
            new NoopEditService(),
            m_logs);
        var owner = new AttributeOwner();
        SerializedProperty property = Assert.Single(
            m_serialization.GetProperties(owner),
            static candidate => candidate.name == nameof(AttributeOwner.value));
        AttributeValueDrawer.observations.Clear();
        ProbeInspectorAttributeDrawer.observations.Clear();
        var nativeContext = NativeImGui.CreateContext();
        try
        {
            DrawPropertyFrame(renderer, editor, owner, property);
            Assert.Empty(AttributeValueDrawer.observations);
            Assert.Equal(
                ["update:Friendly Value:False:2:8"],
                ProbeInspectorAttributeDrawer.observations);

            owner.showValue = true;
            DrawPropertyFrame(renderer, editor, owner, property);
        }
        finally
        {
            NativeImGui.DestroyContext(nativeContext);
        }

        Assert.Equal(
            [
                "update:Friendly Value:False:2:8",
                "update:Friendly Value:False:2:8",
                "before",
                "after"
            ],
            ProbeInspectorAttributeDrawer.observations);
        Assert.Equal(["Friendly Value:False:2:8"], AttributeValueDrawer.observations);
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

    private static void DrawPropertyFrame(
        SerializedPropertyRenderer renderer,
        EditorContext editor,
        object owner,
        SerializedProperty property)
    {
        PrepareNativeFrame();
        _ = NativeImGui.Begin("Text State Test");
        try
        {
            renderer.Draw(editor, owner, "owner", property);
        }
        finally
        {
            NativeImGui.End();
        }
        NativeImGui.Render();
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

internal sealed class TextStateOwner : ISerializable
{
    [SerializableProperty]
    public TextStateValue value { get; set; } = new("Unset");
}

internal sealed record TextStateValue(string id);

internal sealed class AttributeOwner : ISerializable
{
    internal bool showValue { get; set; }

    [SerializableProperty]
    [Header("Presentation", "Decorators are composed before the property row.")]
    [Text("Persistent decorator text remains visible.")]
    [Tooltip("Reusable hover guidance.")]
    [InspectorName("Friendly Value")]
    [Range(2, 8)]
    [ShowIf(nameof(showValue))]
    [ProbeInspector]
    public AttributeValue value { get; set; } = new();
}

internal sealed class AttributeValue;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
internal sealed class ProbeInspectorAttribute : InspectorPresentationAttribute;

[InspectorAttributeDrawer(typeof(ProbeInspectorAttribute), priority: 10_000)]
internal sealed class ProbeInspectorAttributeDrawer : IInspectorAttributeDrawer
{
    internal static List<string> observations { get; } = [];

    public void Update(InspectorAttributeDrawContext context)
        => observations.Add(
            $"update:{context.label}:{context.isReadOnly}:{context.minimum}:{context.maximum}");

    public void DrawBefore(InspectorAttributeDrawContext context)
    {
        _ = context;
        observations.Add("before");
    }

    public void DrawAfter(InspectorAttributeDrawContext context)
    {
        _ = context;
        observations.Add("after");
    }
}

[PropertyDrawer(typeof(AttributeValue), priority: 10_000)]
internal sealed class AttributeValueDrawer : IPropertyDrawer
{
    internal static List<string> observations { get; } = [];

    public void Draw(PropertyDrawContext context)
        => observations.Add(
            $"{context.label}:{context.isReadOnly}:{context.minimum}:{context.maximum}");
}

[PropertyDrawer(typeof(TextStateValue), priority: 10_000)]
internal sealed class TextStateDrawer : IPropertyDrawer
{
    internal static List<string> observations { get; } = [];

    public void Draw(PropertyDrawContext context)
    {
        var value = (TextStateValue)context.GetValue()!;
        bool hasState = context.TryGetTextState("editing", out string? state);
        observations.Add($"{value.id}:{(hasState ? state : "<new>")}");
        context.SetTextState("editing", value.id);
    }
}

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
