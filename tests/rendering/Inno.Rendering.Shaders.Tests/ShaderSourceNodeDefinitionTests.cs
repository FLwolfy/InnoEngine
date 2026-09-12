using System.Linq;
using Inno.Core.Graphs;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed class ShaderSourceNodeDefinitionTests
{
    private static readonly GraphNodeRecord S_node = new(new GraphNodeId("test-node"), "inno.shader.source");

    [Fact]
    public void FunctionPortsUseSemanticNamesAndInOutCreatesBothDirections()
    {
        var definition = new ShaderSourceNodeDefinition(new("Shade", ShaderSourceType.Atomic("float4"),
            [new("uv", ShaderSourceType.Atomic("float2"), ShaderSourceParameterDirection.Input),
             new("color", ShaderSourceType.Atomic("float4"), ShaderSourceParameterDirection.InputOutput)], new("source", 1, 1)));
        Assert.Equal(["input.uv", "input.color", "output.color", "return"], definition.GetPorts(S_node).Select(static port => port.id.value));
        Assert.Equal(GraphPortCapacity.Multiple, definition.GetPorts(S_node)[2].capacity);
        Assert.Same(definition.GetPorts(S_node), definition.GetPorts(S_node));
    }

    [Fact]
    public void AggregatesExposeMembersWithoutDiscardingTheirWholeValuePorts()
    {
        ShaderSourceType aggregate = ShaderSourceType.Structure("Surface", [new("normal", ShaderSourceType.Atomic("float3"))]);
        var definition = new ShaderSourceNodeDefinition(new("Shade", aggregate,
            [new("surface", aggregate, ShaderSourceParameterDirection.Input)], new("source", 1, 1)));
        Assert.Equal(["input.surface", "input.surface.normal", "return", "return.normal"],
            definition.GetPorts(S_node).Select(static port => port.id.value));
        Assert.Equal(definition.GetPorts(S_node)[0].valueTypeId, definition.GetPorts(S_node)[2].valueTypeId);
    }

    [Fact]
    public void ArrayLengthsAndStructureLayoutsCannotAliasGraphValueTypes()
    {
        string FirstPortType(ShaderSourceType type) => new ShaderSourceNodeDefinition(new("Shade", type, [], new("source", 1, 1)))
            .GetPorts(S_node)[0].valueTypeId;
        Assert.NotEqual(FirstPortType(ShaderSourceType.ArrayOf(ShaderSourceType.Atomic("float"), 2)),
            FirstPortType(ShaderSourceType.ArrayOf(ShaderSourceType.Atomic("float"), 3)));
        Assert.NotEqual(FirstPortType(ShaderSourceType.Structure("Surface", [new("a", ShaderSourceType.Atomic("float3"))])),
            FirstPortType(ShaderSourceType.Structure("Surface", [new("a", ShaderSourceType.Atomic("float4"))])));
    }
}
