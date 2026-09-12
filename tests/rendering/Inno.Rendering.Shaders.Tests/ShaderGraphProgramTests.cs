using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed partial class ShaderGraphProgramTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "ShaderProgramTests", Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly ShaderNodeCompilerRegistry m_nodes;

    public ShaderGraphProgramTests()
    {
        m_modules = new(new() { cacheDirectory = m_root });
        m_types = new(m_modules);
        m_serialization = new(m_types);
        m_nodes = new(m_types);
    }

    [Fact]
    public void NativeGraphRoundTripPreservesTheOnlyRasterCreationTemplate()
    {
        GraphDocument graph = Template();
        byte[] bytes = GraphDocumentCodec.Encode(graph, m_serialization);
        GraphDocument restored = GraphDocumentCodec.Decode(bytes, m_serialization);
        Assert.Equal(bytes, GraphDocumentCodec.Encode(restored, m_serialization));
        ShaderGraphProgramResult result = Lower(restored);
        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(static value => value.message)));
        Assert.Equal(new[] { ShaderStage.Vertex, ShaderStage.Fragment }, Assert.Single(result.passes).stages.Select(static value => value.stage));
    }

    [Fact]
    public void MissingStageOrOutputIsRejectedWithoutDeletingUnfinishedGraphRecords()
    {
        GraphDocument graph = Template();
        GraphNodeRecord output = graph.nodes.Single(static value => value.id.value == "fragment");
        graph.RemoveNode(output.id);
        byte[] before = GraphDocumentCodec.Encode(graph, m_serialization);
        Assert.False(Lower(graph).succeeded);
        Assert.Equal(before, GraphDocumentCodec.Encode(graph, m_serialization));
        GraphDocument unconnected = Template();
        unconnected.RemoveEdge(unconnected.edges.First().id);
        Assert.False(Lower(unconnected).succeeded);
    }

    [Fact]
    public void NewCompilerRetainsPropertyPassKeywordTechniqueAndStageValidation()
    {
        GraphDocument graph = Template();
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
        ShaderPropertyDefinition property = definition.properties[0];
        property.stages = ShaderStage.None;
        definition.properties = [property, property];
        definition.passes = [definition.passes[0], definition.passes[0]];
        definition.keywords = [new("quality", ["low", "low"])];
        definition.techniques = [new(new("surface"), new("arbitrary.render-model"), [new(new("forward"), "absent")])];
        string[] codes = ShaderDefinitionValidator.Validate(definition).Where(static value => value.severity == DiagnosticSeverity.Error)
            .Select(static value => value.code).ToArray();
        Assert.Contains("SHADER_DUPLICATE_PROPERTY", codes);
        Assert.Contains("SHADER_DUPLICATE_PASS", codes);
        Assert.Contains("SHADER_PROPERTY_STAGES_INVALID", codes);
        Assert.Contains("SHADER_DUPLICATE_KEYWORD_OPTION", codes);
        Assert.Contains("SHADER_UNKNOWN_TECHNIQUE_PASS", codes);
        graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition), m_serialization, SerializationContext.empty));
        Assert.False(Lower(graph).succeeded);
    }

    [Fact]
    public void CapabilityValidationReportsUnsupportedPassWithoutMutatingTheDefinition()
    {
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(Template(), m_serialization, SerializationContext.empty);
        ShaderPassDefinition pass = definition.passes[0];
        pass.requiredFeatures = GraphicsCapability.Compute;
        definition.passes = [pass];
        RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
        var capabilities = new GraphicsCapabilities(GraphicsApi.Noop, GraphicsCapability.None, new(64, 1, 4096, 0),
            formats, formats, formats, formats, false, false);
        Assert.Contains(ShaderDefinitionValidator.Validate(definition, capabilities), static value =>
            value.code == "SHADER_CAPABILITY_UNAVAILABLE" && value.severity == DiagnosticSeverity.Warning);
        Assert.Equal(GraphicsCapability.Compute, definition.passes[0].requiredFeatures);
    }

    private GraphDocument Template() => ShaderGraphTemplates.CreateRaster(m_serialization, SerializationContext.empty);

    [Fact]
    public void RenamingAnExposedInputAtomicallyChangesItsParameterAndPreservesTheDefault()
    {
        GraphDocument graph = Template();
        GraphNodeRecord color = graph.FindNode(new("color"))!;
        ShaderGraphInputSettings input = ShaderGraphDocument.Read(color, "settings", new ShaderGraphInputSettings(), m_serialization, SerializationContext.empty);
        input.id = "tint";
        GraphDocument changed = ShaderGraphBindings.ChangeInput(graph, color.id, input, m_serialization, SerializationContext.empty);
        ShaderDefinition original = ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
        ShaderDefinition edited = ShaderGraphDocument.ReadDefinition(changed, m_serialization, SerializationContext.empty);
        Assert.Equal("color", Assert.Single(original.properties).id.value);
        Assert.Equal("tint", Assert.Single(edited.properties).id.value);
        Assert.Equal(original.properties[0].defaultValue, edited.properties[0].defaultValue);
        Assert.True(Lower(changed).succeeded);
    }

    [Fact]
    public void PartiallyTypedInputRemainsSerializableInsteadOfDiscardingItsNodeOrEdges()
    {
        GraphDocument graph = Template();
        ShaderGraphInputSettings input = ShaderGraphDocument.Read(graph.FindNode(new("color"))!, "settings", new ShaderGraphInputSettings(), m_serialization, SerializationContext.empty);
        input.id = "";
        GraphDocument changed = ShaderGraphBindings.ChangeInput(graph, new("color"), input, m_serialization, SerializationContext.empty);
        Assert.Equal(graph.nodes.Count, changed.nodes.Count);
        Assert.Equal(graph.edges.Count, changed.edges.Count);
        Assert.False(Lower(changed).succeeded);
        Assert.NotEmpty(GraphDocumentCodec.Encode(changed, m_serialization));
    }
    private ShaderGraphProgramResult Lower(GraphDocument graph) => new ShaderGraphProgramCompiler(m_nodes).Lower(graph, "bgfx",
        new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>(), m_serialization, SerializationContext.empty);

    [Fact]
    public void RemovingLastParameterInputRemovesItsDeclarationWithoutMutatingTheOriginal()
    {
        GraphDocument graph = Template();
        byte[] before = GraphDocumentCodec.Encode(graph, m_serialization);
        GraphDocument edited = ShaderGraphBindings.RemoveNodes(graph, [new("color")], m_serialization, SerializationContext.empty);
        Assert.Null(edited.FindNode(new("color")));
        Assert.Empty(ShaderGraphDocument.ReadDefinition(edited, m_serialization, SerializationContext.empty).properties);
        Assert.Equal(before, GraphDocumentCodec.Encode(graph, m_serialization));
        Assert.DoesNotContain(edited.edges, edge => edge.output.nodeId.value == "color");
    }

    [Fact]
    public void RemovingOneSharedBindingPreservesItsDefaultAndRemainingStageVisibility()
    {
        GraphDocument graph = Template();
        var node = new GraphNodeRecord(new("vertex-color"), "inno.shader.stage-input");
        node.SetValue("stage", ShaderGraphDocument.Encode("vertex", m_serialization, SerializationContext.empty));
        graph.AddNode(node);
        var settings = new ShaderGraphInputSettings { id = "color", kind = ShaderIrInputKind.Uniform, type = new() { id = "float4" } };
        graph = ShaderGraphBindings.ChangeInput(graph, node.id, settings, m_serialization, SerializationContext.empty);
        ShaderPropertyDefinition original = Assert.Single(ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty).properties);
        Assert.Equal(ShaderStage.Vertex | ShaderStage.Fragment, original.stages);
        GraphDocument edited = ShaderGraphBindings.RemoveNodes(graph, [node.id], m_serialization, SerializationContext.empty);
        ShaderPropertyDefinition remaining = Assert.Single(ShaderGraphDocument.ReadDefinition(edited, m_serialization, SerializationContext.empty).properties);
        Assert.Equal(ShaderStage.Fragment, remaining.stages);
        Assert.Equal(original.defaultValue, remaining.defaultValue);
        Assert.True(Lower(edited).succeeded);
    }

    [Fact]
    public void RemovingNodesFromAnIncompleteDocumentDoesNotRequireSuccessfulCompilation()
    {
        var graph = new GraphDocument();
        graph.AddNode(new(new("unknown"), "tests.uninstalled"));
        GraphDocument edited = ShaderGraphBindings.RemoveNodes(graph, [new("unknown")], m_serialization, SerializationContext.empty);
        Assert.Empty(edited.nodes);
        Assert.Single(graph.nodes);
    }

    public void Dispose()
    {
        m_nodes.Dispose(); m_serialization.Dispose(); m_types.Dispose(); m_modules.Dispose();
        if (Directory.Exists(m_root)) Directory.Delete(m_root, true);
    }
}
