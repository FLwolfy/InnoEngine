using System;
using System.Collections.Generic;
using System.IO;
using Inno.Core.Assemblies;
using Inno.Core.Graphs;
using Inno.Core.Reflection;
using Inno.Core.Serialization;
using Xunit;

namespace Inno.Core.Graphs.Tests;

public sealed class GraphValidatorTests : IDisposable
{
    private readonly string m_testRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoCoreGraphsTests",
        Guid.NewGuid().ToString("N"));

    public GraphValidatorTests()
    {
        AssemblyManager.Initialize(new AssemblyManagerOptions
        {
            cacheDirectory = Path.Combine(m_testRoot, "Assemblies")
        });
        TypeCacheManager.Initialize();
        SerializationManager.Initialize();
    }

    public void Dispose()
    {
        SerializationManager.Shutdown();
        TypeCacheManager.Shutdown();
        AssemblyManager.Shutdown();
        if (Directory.Exists(m_testRoot))
            Directory.Delete(m_testRoot, recursive: true);
    }

    [Fact]
    public void Validate_WithCompatibleConnection_IsValid()
    {
        GraphDocument document = CreateConnectedDocument("float", "float");

        GraphValidationResult result = GraphValidator.Validate(document, new Resolver());

        Assert.True(result.isValid);
        Assert.Empty(result.diagnostics);
    }

    [Fact]
    public void Validate_WithDirectedConversion_IsValid()
    {
        GraphDocument document = CreateConnectedDocument("float", "vector3");

        GraphValidationResult result = GraphValidator.Validate(document, new Resolver(), new Conversion());

        Assert.True(result.isValid);
    }

    [Fact]
    public void Validate_WithIncompatibleTypes_ReportsError()
    {
        GraphDocument document = CreateConnectedDocument("texture2d", "float");

        GraphValidationResult result = GraphValidator.Validate(document, new Resolver());

        Assert.False(result.isValid);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "GRAPH_INCOMPATIBLE_TYPES");
    }

    [Fact]
    public void Validate_WithMissingNodeDefinition_PreservesDocumentAsWarning()
    {
        GraphDocument document = new();
        GraphNodeRecord node = new(new GraphNodeId("missing"), "plugin.removed");
        document.AddNode(node);

        GraphValidationResult result = GraphValidator.Validate(document, new Resolver());

        Assert.True(result.isValid);
        Assert.Single(document.nodes);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "GRAPH_MISSING_NODE");
    }

    [Fact]
    public void Validate_WithCycle_ReportsError()
    {
        GraphDocument document = new();
        GraphNodeRecord left = CreateNode("left", "float", "float");
        GraphNodeRecord right = CreateNode("right", "float", "float");
        document.AddNode(left);
        document.AddNode(right);
        document.AddEdge(CreateEdge("left-right", left.id, right.id));
        document.AddEdge(CreateEdge("right-left", right.id, left.id));

        GraphValidationResult result = GraphValidator.Validate(document, new Resolver());

        Assert.False(result.isValid);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.code == "GRAPH_CYCLE");
    }

    [Fact]
    public void RemoveNode_RemovesConnectedEdges()
    {
        GraphDocument document = CreateConnectedDocument("float", "float");

        Assert.True(document.RemoveNode(new GraphNodeId("source")));

        Assert.Single(document.nodes);
        Assert.Empty(document.edges);
    }

    private static GraphDocument CreateConnectedDocument(string outputType, string inputType)
    {
        GraphDocument document = new();
        GraphNodeRecord source = CreateNode("source", outputType, outputType);
        GraphNodeRecord destination = CreateNode("destination", inputType, inputType);
        document.AddNode(source);
        document.AddNode(destination);
        document.AddEdge(CreateEdge("edge", source.id, destination.id));
        return document;
    }

    private static GraphNodeRecord CreateNode(string id, string inputType, string outputType)
    {
        GraphNodeRecord node = new(new GraphNodeId(id), $"node.{id}");
        node.SetValue("inputType", GraphSerializedValue.From(inputType));
        node.SetValue("outputType", GraphSerializedValue.From(outputType));
        return node;
    }

    private static GraphEdgeRecord CreateEdge(string id, GraphNodeId source, GraphNodeId destination)
        => new(
            new GraphEdgeId(id),
            new GraphEndpoint(source, new GraphPortId("out")),
            new GraphEndpoint(destination, new GraphPortId("in")));

    private sealed class Resolver : IGraphNodeDefinitionResolver
    {
        public bool TryResolve(string definitionId, out GraphNodeDefinition? definition)
        {
            if (!definitionId.StartsWith("node.", System.StringComparison.Ordinal))
            {
                definition = null;
                return false;
            }

            definition = new Definition(definitionId);
            return true;
        }
    }

    private sealed class Definition(string id) : GraphNodeDefinition(id, id, "Tests")
    {
        public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        {
            string inputType = node.TryGetValue("inputType", out GraphSerializedValue? input)
                ? input!.Deserialize<string>()!
                : "float";
            string outputType = node.TryGetValue("outputType", out GraphSerializedValue? output)
                ? output!.Deserialize<string>()!
                : "float";

            return
            [
                new GraphPortDefinition(new GraphPortId("in"), "In", inputType, GraphPortDirection.Input),
                new GraphPortDefinition(new GraphPortId("out"), "Out", outputType, GraphPortDirection.Output)
            ];
        }
    }

    private sealed class Conversion : IGraphTypeConversion
    {
        public bool CanConvert(string sourceTypeId, string destinationTypeId)
            => sourceTypeId == "float" && destinationTypeId == "vector3";
    }
}
