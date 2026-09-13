using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

[Collection("Shader registry fault injection")]
public sealed class ShaderGraphInputDefaultTests : IDisposable
{
    private readonly string m_root = Path.Combine(Path.GetTempPath(), "InnoShaderDefaults", Guid.NewGuid().ToString("N"));
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;

    public ShaderGraphInputDefaultTests()
    { m_modules = new(new() { cacheDirectory = m_root }); m_types = new(m_modules); m_serialization = new(m_types); }

    [Fact]
    public void UnconnectedTypedDefaultsLowerAndRoundTripWithoutSyntheticGraphNodes()
    {
        var graph = new GraphDocument();
        var node = new GraphNodeRecord(new("multiply"), "inno.shader.binary");
        graph.AddNode(node);
        Set(node, "type", "float4"); Set(node, "operation", "multiply");
        ShaderGraphLiteral value = ShaderGraphLiteral.Zero(ShaderSourceType.Atomic("float4"));
        value.scalarBits = [0x3f800000, 0x3f000000, 0x3e800000, 0x3f800000];
        Set(node, ShaderGraphDocument.inputDefaultPrefix + "left", value);
        Set(node, ShaderGraphDocument.inputDefaultPrefix + "right", value);
        GraphDocument restored = GraphDocumentCodec.Decode(GraphDocumentCodec.Encode(graph, m_serialization), m_serialization);
        var catalog = new ShaderNodeCompilerCatalog([new ShaderBinaryNodeCompiler()]);
        ShaderGraphLoweringResult result = Lower(restored, catalog);
        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(value => value.message)));
        Assert.Single(restored.nodes); Assert.Empty(restored.edges);
        Assert.Equal(2, result.block!.instructions.Count(value => value.operation == ShaderIrOperation.Construct));
        Assert.Contains(result.block.instructions, value => value.operation == ShaderIrOperation.Multiply);
    }

    [Fact]
    public void ConnectedInputIgnoresButRetainsItsObsoleteDefault()
    {
        var graph = new GraphDocument();
        var node = new GraphNodeRecord(new("multiply"), "inno.shader.binary");
        var constant = new GraphNodeRecord(new("source"), "inno.shader.constant");
        graph.AddNode(node); graph.AddNode(constant);
        Set(node, "type", "float");
        Set(node, ShaderGraphDocument.inputDefaultPrefix + "left", ShaderGraphLiteral.Zero(ShaderSourceType.Atomic("uint")));
        Set(node, ShaderGraphDocument.inputDefaultPrefix + "right", ShaderGraphLiteral.Zero(ShaderSourceType.Atomic("float")));
        var catalog = new ShaderNodeCompilerCatalog([new ShaderBinaryNodeCompiler(), new ShaderConstantNodeCompiler()]);
        Assert.Contains(Lower(graph, catalog).diagnostics, value => value.code == "SHADER_GRAPH_DEFAULT_TYPE" && value.portId == "left");
        graph.AddEdge(new(new("connect"), new(constant.id, new("value")), new(node.id, new("left"))));
        Assert.True(Lower(graph, catalog).succeeded);
        Assert.True(node.TryGetValue(ShaderGraphDocument.inputDefaultPrefix + "left", out _));
    }

    [Fact]
    public void AggregateDefaultsPreserveExactIntegerBitsAndRejectResourcesOrMalformedValues()
    {
        ShaderSourceType type = ShaderSourceType.Structure("Settings", [
            new("index", ShaderSourceType.Atomic("uint")), new("values", ShaderSourceType.ArrayOf(ShaderSourceType.Atomic("float3"), 2))]);
        ShaderGraphLiteral value = ShaderGraphLiteral.Zero(type);
        Assert.Equal(7, value.scalarBits.Length);
        value.scalarBits[0] = uint.MaxValue;
        var builder = new ShaderIrBuilder();
        ShaderIrValue output = value.Emit(builder, type);
        Assert.Equal(uint.MaxValue, builder.Build(new Dictionary<string, ShaderIrValue> { ["value"] = output }).instructions[0].constantBits);
        Assert.Throws<NotSupportedException>(() => ShaderGraphLiteral.Zero(ShaderSourceType.Atomic("sampled-texture2d")));
        var boolean = new ShaderGraphLiteral { type = new() { id = "bool" }, scalarBits = [2] };
        Assert.Throws<InvalidOperationException>(() => boolean.Emit(new(), ShaderSourceType.Atomic("bool")));
        value.scalarBits = [0];
        Assert.Throws<InvalidOperationException>(() => value.Emit(new(), type));
    }

    private ShaderGraphLoweringResult Lower(GraphDocument graph, ShaderNodeCompilerCatalog catalog)
        => catalog.Lower(new(graph, new Dictionary<string, GraphEndpoint> { ["value"] = new(new("multiply"), new("value")) }, "test.defaults"), m_serialization, SerializationContext.empty);

    private void Set<T>(GraphNodeRecord node, string key, T value)
        => node.SetValue(key, ShaderGraphDocument.Encode(value, m_serialization, SerializationContext.empty));

    public void Dispose()
    { m_serialization.Dispose(); m_types.Dispose(); m_modules.Dispose(); if (Directory.Exists(m_root)) Directory.Delete(m_root, true); }
}
