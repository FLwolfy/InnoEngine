using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed partial class ShaderGraphLoweringTests
{
    [Fact]
    public void TypedEdgesRejectMismatchesAndCyclesBeforeInvokingAnyCompiler()
    {
        var graph = new GraphDocument();
        GraphNodeRecord value = Add(graph, "value", "inno.shader.constant");
        GraphNodeRecord a = Add(graph, "a", "inno.shader.binary");
        GraphNodeRecord b = Add(graph, "b", "inno.shader.binary");
        Connect(graph, a, "value", b, "left");
        Connect(graph, b, "value", a, "left");
        Connect(graph, value, "value", a, "right");
        Connect(graph, value, "value", b, "right");
        ShaderGraphLoweringRequest Request() => new(graph, new Dictionary<string, GraphEndpoint> { ["result"] = Endpoint(a, "value") }, "metal");
        ShaderGraphLoweringResult cycle = Catalog().Lower(Request(), m_serialization, SerializationContext.empty);
        Assert.Contains(cycle.diagnostics, diagnostic => diagnostic.code == "SHADER_GRAPH_CYCLE");
        Set(value, "type", "int");
        ShaderGraphLoweringResult mismatch = Catalog().Lower(Request(), m_serialization, SerializationContext.empty);
        Assert.Contains(mismatch.diagnostics, diagnostic => diagnostic.code == "SHADER_GRAPH_TYPE");
        Assert.Null(mismatch.block);
    }

    [Fact]
    public void IndependentUnusedSourceEffectsKeepStableDocumentOrder()
    {
        var graph = new GraphDocument();
        GraphNodeRecord first = Add(graph, "first", "inno.shader.source");
        GraphNodeRecord second = Add(graph, "second", "inno.shader.source");
        var modules = new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis>
        {
            [first.id] = Source("void Shade() {}", "first.ishadersource"),
            [second.id] = Source("void Shade() {}", "second.ishadersource")
        };
        ShaderGraphLoweringResult result = Catalog().Lower(new(graph, new Dictionary<string, GraphEndpoint>(), "metal", modules), m_serialization, SerializationContext.empty);
        Assert.True(result.succeeded, Errors(result));
        Assert.Equal(["first.ishadersource", "second.ishadersource"], result.block!.instructions.Select(static instruction => instruction.source!.sourcePath));
    }

    [Fact]
    public void ExpandedMembersAndAggregateConnectionsCannotSilentlyOverrideEachOther()
    {
        var graph = new GraphDocument();
        GraphNodeRecord input = Add(graph, "input", "inno.shader.stage-input");
        GraphNodeRecord scalar = Add(graph, "scalar", "inno.shader.constant");
        GraphNodeRecord source = Add(graph, "source", "inno.shader.source");
        ShaderSourceModuleAnalysis module = Source("struct Data { float x; float y; }; float Shade(Data value) { return value.x + value.y; }");
        Connect(graph, input, "value", source, "input.value");
        Connect(graph, scalar, "value", source, "input.value.x");
        var request = new ShaderGraphLoweringRequest(graph, new Dictionary<string, GraphEndpoint> { ["value"] = Endpoint(source, "return") }, "metal",
            new Dictionary<GraphNodeId, ShaderSourceModuleAnalysis> { [source.id] = module },
            new Dictionary<GraphNodeId, ShaderIrStageInput> { [input.id] = new("data", module.function!.parameters[0].type, ShaderIrInputKind.Uniform) });
        ShaderGraphLoweringResult result = Catalog().Lower(request, m_serialization, SerializationContext.empty);
        Assert.False(result.succeeded);
        Assert.Contains(result.diagnostics, diagnostic => diagnostic.message.Contains("both its aggregate and a member", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateProvidersAndCanceledRequestsAreRejectedWithoutPartialResults()
    {
        Assert.Throws<ArgumentException>(() => Catalog(new DoubleCompiler(), new DoubleCompiler()));
        var request = new ShaderGraphLoweringRequest(new(), new Dictionary<string, GraphEndpoint>(), "metal");
        Assert.ThrowsAny<OperationCanceledException>(() => Catalog().Lower(request, m_serialization, SerializationContext.empty, new CancellationToken(true)));
    }

    [Fact]
    public void RegistryDiscoversExtensionsAndRollsBackDuplicateCandidatesWithoutLeakingProviders()
    {
        NodeRegistryProbe.created = NodeRegistryProbe.disposed = 0;
        NodeRegistryProbe.conflict = false;
        using (var registry = new ShaderNodeCompilerRegistry(m_types))
        {
            Assert.Contains("tests.registry.node", registry.definitionIds);
            Assert.Contains("inno.shader.source", registry.definitionIds);
            m_modules.Rebuild();
            Assert.Equal(1, NodeRegistryProbe.created - NodeRegistryProbe.disposed);
            NodeRegistryProbe.conflict = true;
            try { Assert.ThrowsAny<ArgumentException>(m_modules.Rebuild); }
            finally { NodeRegistryProbe.conflict = false; }
            Assert.Contains("tests.registry.node", registry.definitionIds);
            Assert.Equal(1, NodeRegistryProbe.created - NodeRegistryProbe.disposed);
        }
        Assert.Equal(NodeRegistryProbe.created, NodeRegistryProbe.disposed);
    }
}

[ShaderNodeCompilerExtension]
internal sealed class NodeRegistryProbe : IShaderNodeCompiler, IDisposable
{
    internal static int created;
    internal static int disposed;
    internal static bool conflict;
    private readonly string m_id;
    public NodeRegistryProbe() { created++; m_id = conflict ? "inno.shader.constant" : "tests.registry.node"; }
    public string definitionId => m_id;
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context) => [];
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context) => new Dictionary<string, ShaderIrValue>();
    public void Dispose() => disposed++;
}
