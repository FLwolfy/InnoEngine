using System;
using System.IO;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Editor.Inspection.Tests;

public sealed class ConditionalInspectionTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "inno-inspection-" + Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly LogRouter m_logs = new();
    private readonly EditorInteractionRuntime m_runtime;

    public ConditionalInspectionTests()
    {
        Directory.CreateDirectory(m_root);
        m_modules = new(new() { cacheDirectory = Path.Combine(m_root, "Assemblies") });
        m_types = new(m_modules);
        m_serialization = new(m_types);
        m_runtime = new(new EditorContext(m_root), m_types, m_logs, [m_types, m_serialization]);
        m_runtime.Start();
    }
    [Fact]
    public void ConditionalInspectorPeersResolveByPredicateAndRejectActualAmbiguity()
    {
        using var properties = new PropertyDrawerRegistry(m_runtime.interactions, m_types, m_serialization, []);
        using var attributes = new InspectorAttributeDrawerRegistry(m_runtime.interactions, m_types, m_serialization, []);
        var renderer = new SerializedPropertyRenderer(properties, attributes, m_runtime.interactions, new ProbePropertyEdits(), m_logs);
        using var inspectors = new InspectionDrawerRegistry(m_runtime.interactions, CreateDrawer, m_types, m_serialization);
        Assert.True(inspectors.TryResolve(m_runtime.context, new ConditionalTarget(1, false), renderer, out var first, out _));
        Assert.IsType<ConditionalFirst>(first);
        Assert.True(inspectors.TryResolveExact(m_runtime.context, new ConditionalTarget(2, false), renderer, out var second, out _));
        Assert.IsType<ConditionalSecond>(second);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            inspectors.TryResolve(m_runtime.context, new ConditionalTarget(2, true), renderer, out _, out _));
        Assert.Contains("both accept", error.Message);
        Assert.Throws<InvalidOperationException>(() =>
            inspectors.TryResolveExact(m_runtime.context, new ConditionalTarget(2, true), renderer, out _, out _));

        IInspectionDrawer CreateDrawer(Type type)
            => (IInspectionDrawer)Activator.CreateInstance(type, nonPublic: true)!;
    }

    public sealed record ConditionalTarget(int kind, bool ambiguous);
    [InspectionDrawer(typeof(ConditionalTarget), conditional: true)]
    public sealed class ConditionalFirst : InspectionDrawer<ConditionalTarget>
    {
        public override string icon => "";
        protected override bool CanInspect(ConditionalTarget target) => target.kind == 1;
        protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, ConditionalTarget target) => ("First", null);
        protected override void Draw(InspectionDrawContext context, ConditionalTarget target) { }
    }
    [InspectionDrawer(typeof(ConditionalTarget), conditional: true)]
    public sealed class ConditionalSecond : InspectionDrawer<ConditionalTarget>
    {
        public override string icon => "";
        protected override bool CanInspect(ConditionalTarget target) => target.kind == 2;
        protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, ConditionalTarget target) => ("Second", null);
        protected override void Draw(InspectionDrawContext context, ConditionalTarget target) { }
    }
    [InspectionDrawer(typeof(ConditionalTarget), conditional: true)]
    public sealed class ConditionalOverlap : InspectionDrawer<ConditionalTarget>
    {
        public override string icon => "";
        protected override bool CanInspect(ConditionalTarget target) => target.ambiguous;
        protected override (string name, Action<string>? setter) BindName(InspectionDrawContext context, ConditionalTarget target) => ("Overlap", null);
        protected override void Draw(InspectionDrawContext context, ConditionalTarget target) { }
    }
    private sealed class ProbePropertyEdits : IInspectionPropertyEditService
    {
        public bool ChangeProperty(object owner, string propertyName, Action mutation, string historyName)
            => throw new InvalidOperationException("Resolution must not edit a property.");
    }


    public void Dispose()
    {
        m_runtime.Dispose();
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        Directory.Delete(m_root, true);
    }
}

