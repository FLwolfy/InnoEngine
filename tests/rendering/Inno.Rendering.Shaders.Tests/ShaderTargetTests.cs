using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderTargetTests : IDisposable
{
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly ShaderTargetRegistry m_targets;

    public ShaderTargetTests()
    {
        m_modules = new(new() { cacheDirectory = Path.Combine(Path.GetTempPath(), "ShaderTargets", Guid.NewGuid().ToString("N")) });
        m_types = new(m_modules);
        m_serialization = new(m_types);
        m_targets = new(m_types);
    }

    [Fact]
    public void IndependentTargetExpandsIntoTheCommonCompilerWithoutMutatingItsSource()
    {
        GraphDocument authored = ShaderGraphDocument.Create(new("Surface", [], [], []), m_serialization, SerializationContext.empty);
        ShaderGraphDocument.SetTarget(authored, "tests.surface-target", m_serialization, SerializationContext.empty);
        byte[] before = GraphDocumentCodec.Encode(authored, m_serialization);
        GraphDocument expanded = m_targets.Expand(authored, m_serialization, SerializationContext.empty);
        using var nodes = new ShaderNodeCompilerRegistry(m_types);
        ShaderGraphProgramResult result = new ShaderGraphProgramCompiler(nodes).Lower(expanded, "bgfx",
            new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>(), m_serialization, SerializationContext.empty);
        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(static value => value.message)));
        Assert.Equal(before, GraphDocumentCodec.Encode(authored, m_serialization));
        Assert.Contains("tests.surface-target", m_targets.ids);
        Assert.Single(result.passes);
    }

    [Fact]
    public void UnavailableTargetPreservesTheAssignedIdentityAndAllAuthoredData()
    {
        GraphDocument authored = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        ShaderGraphDocument.SetTarget(authored, "tests.unavailable-target", m_serialization, SerializationContext.empty);
        byte[] before = GraphDocumentCodec.Encode(authored, m_serialization);
        Assert.Contains("tests.unavailable-target", Assert.Throws<ShaderTargetUnavailableException>(
            () => m_targets.Expand(authored, m_serialization, SerializationContext.empty)).Message);
        Assert.Equal(before, GraphDocumentCodec.Encode(authored, m_serialization));
    }

    [Fact]
    public void CancellationCannotPublishAnExpansionOrModifyTheSource()
    {
        GraphDocument authored = ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);
        byte[] before = GraphDocumentCodec.Encode(authored, m_serialization);
        Assert.Throws<OperationCanceledException>(() => m_targets.Expand(authored, m_serialization,
            SerializationContext.empty, new CancellationToken(true)));
        Assert.Equal(before, GraphDocumentCodec.Encode(authored, m_serialization));
    }

    [Fact]
    public void TemplatesAreDiscoveredByStableIdentityAndCreateIndependentDocuments()
    {
        using var templates = new ShaderGraphTemplateRegistry(m_types);
        Assert.Contains(templates.templates, static value => value.id == "tests.surface-template");
        GraphDocument first = templates.Create("tests.surface-template", m_serialization, SerializationContext.empty);
        GraphDocument second = templates.Create("tests.surface-template", m_serialization, SerializationContext.empty);
        Assert.Equal("tests.surface-target", ShaderGraphDocument.ReadTarget(first, m_serialization, SerializationContext.empty));
        first.RemoveMetadata(ShaderGraphDocument.targetKey);
        Assert.Equal("tests.surface-target", ShaderGraphDocument.ReadTarget(second, m_serialization, SerializationContext.empty));
        Assert.Throws<InvalidOperationException>(() => templates.Create("missing", m_serialization, SerializationContext.empty));
    }

    public void Dispose()
    { m_targets.Dispose(); m_serialization.Dispose(); m_types.Dispose(); m_modules.Dispose(); }

    [ShaderTarget]
    public sealed class SurfaceTarget : ShaderTarget
    {
        public override string id => "tests.surface-target";
        public override GraphDocument Expand(ShaderTargetContext context, CancellationToken cancellationToken)
            => ShaderGraphTemplates.CreateRaster(context.serialization, context.references);
    }

    [ShaderGraphTemplate]
    public sealed class SurfaceTemplate : ShaderGraphTemplate
    {
        public override string id => "tests.surface-template";
        public override string displayName => "Test Surface";
        public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
        {
            GraphDocument graph = ShaderGraphDocument.Create(new("Surface", [], [], []), serialization, context);
            ShaderGraphDocument.SetTarget(graph, "tests.surface-target", serialization, context);
            return graph;
        }
    }
}
