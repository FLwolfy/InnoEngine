using System;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed partial class ShaderGraphProgramTests
{
    [Fact]
    public void FivePassStatesReferenceTheSameLoweredStageInstances()
    {
        GraphDocument graph = SharedGraph();
        ShaderGraphProgramResult result = Lower(graph);
        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(static value => value.message)));
        Assert.Equal(5, result.passes.Count);
        Assert.Equal(Template().nodes.Count, graph.nodes.Count);
        foreach (ShaderGraphPass pass in result.passes.Skip(1))
        {
            Assert.Same(result.passes[0].stages[0], pass.stages[0]);
            Assert.Same(result.passes[0].stages[1], pass.stages[1]);
        }
    }

    [Fact]
    public void RemovingOnePassKeepsSharedNodesAndRemovingLastPassReleasesItsParameters()
    {
        GraphDocument original = SharedGraph();
        GraphDocument changed = ShaderGraphPrograms.RemovePass(original, "Main", m_serialization, SerializationContext.empty);
        Assert.Equal(original.nodes.Count, changed.nodes.Count);
        Assert.True(Lower(changed).succeeded);
        Assert.Equal(5, ShaderGraphPrograms.Read(original, m_serialization, SerializationContext.empty).Length);
        foreach (string pass in new[] { "Premultiplied", "Additive", "Multiply", "Opaque" })
            changed = ShaderGraphPrograms.RemovePass(changed, pass, m_serialization, SerializationContext.empty);
        Assert.Empty(changed.nodes);
        Assert.Empty(changed.edges);
        Assert.Empty(ShaderGraphDocument.ReadDefinition(changed, m_serialization, SerializationContext.empty).properties);
        Assert.Empty(ShaderGraphPrograms.Read(changed, m_serialization, SerializationContext.empty));
    }

    [Fact]
    public void RemovingASharedStageUpdatesAllReferencesWithoutDeletingTheRemainingStage()
    {
        GraphDocument original = SharedGraph();
        GraphDocument changed = ShaderGraphBindings.RemoveNodes(original, [new("fragment")], m_serialization, SerializationContext.empty);
        Assert.NotNull(changed.FindNode(new("vertex")));
        Assert.All(ShaderGraphPrograms.Read(changed, m_serialization, SerializationContext.empty), binding => Assert.Equal(new[] { "vertex" }, binding.stages));
        Assert.False(Lower(changed).succeeded);
        Assert.Equal(5, ShaderGraphDocument.ReadDefinition(changed, m_serialization, SerializationContext.empty).passes.Length);
        changed = ShaderGraphBindings.RemoveNodes(changed, [new("vertex")], m_serialization, SerializationContext.empty);
        Assert.Empty(ShaderGraphDocument.ReadDefinition(changed, m_serialization, SerializationContext.empty).passes);
    }

    [Fact]
    public void PastePreservesSharedProgramsAndTechniqueRolesWithNewIdentities()
    {
        GraphDocument graph = SharedGraph();
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
        definition.techniques = [new(new("surface"), new("tests.surface"), definition.passes.Select(pass => new ShaderTechniquePass(new(pass.name.ToLowerInvariant()), pass.name)))];
        graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition), m_serialization, SerializationContext.empty));
        byte[] original = GraphDocumentCodec.Encode(graph, m_serialization);
        GraphDocument fragment = ShaderGraphClipboard.Copy(graph, [new("vertex"), new("fragment")], m_serialization, SerializationContext.empty);
        ShaderGraphPasteResult pasted = ShaderGraphClipboard.Paste(graph, fragment, true, null, m_serialization, SerializationContext.empty);
        Assert.Equal(original, GraphDocumentCodec.Encode(graph, m_serialization));
        Assert.Equal(graph.nodes.Count * 2, pasted.document.nodes.Count);
        ShaderDefinition result = ShaderGraphDocument.ReadDefinition(pasted.document, m_serialization, SerializationContext.empty);
        Assert.Equal(2, result.techniques.Length);
        Assert.NotEqual(result.techniques[0].id, result.techniques[1].id);
        Assert.All(result.techniques[1].passes, pass => Assert.EndsWith(" 2", pass.passName));
        Assert.Equal(2, ShaderGraphPrograms.Read(pasted.document, m_serialization, SerializationContext.empty)
            .Where(program => program.pass.EndsWith(" 2", StringComparison.Ordinal)).SelectMany(program => program.stages).Distinct().Count());
        Assert.True(Lower(pasted.document).succeeded);
    }

    [Fact]
    public void CopyAndPasteUnknownNodesPreserveTheirUnresolvedData()
    {
        var graph = new GraphDocument();
        var node = new GraphNodeRecord(new("unknown"), "uninstalled.node");
        node.SetValue("payload", new([1, 3, 5]));
        graph.AddNode(node);
        GraphDocument fragment = ShaderGraphClipboard.Copy(graph, [node.id], m_serialization, SerializationContext.empty);
        ShaderGraphPasteResult pasted = ShaderGraphClipboard.Paste(graph, fragment, true, null, m_serialization, SerializationContext.empty);
        Assert.Equal(2, pasted.document.nodes.Count);
        Assert.NotEqual(node.id, Assert.Single(pasted.insertedNodes));
        Assert.Equal(GraphDocumentCodec.Encode(fragment, m_serialization), GraphDocumentCodec.Encode(graph, m_serialization));
    }

    private GraphDocument SharedGraph()
    {
        GraphDocument graph = Template();
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, m_serialization, SerializationContext.empty);
        definition.passes = new[] { "Main", "Premultiplied", "Additive", "Multiply", "Opaque" }.Select(name =>
        {
            ShaderPassDefinition pass = definition.passes[0];
            pass.name = name;
            return pass;
        }).ToArray();
        graph.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(m_serialization.Serialize(definition), m_serialization, SerializationContext.empty));
        foreach (ShaderPassDefinition pass in definition.passes)
            graph = ShaderGraphPrograms.Bind(graph, pass.name, [new("vertex"), new("fragment")], m_serialization, SerializationContext.empty);
        return graph;
    }
}
