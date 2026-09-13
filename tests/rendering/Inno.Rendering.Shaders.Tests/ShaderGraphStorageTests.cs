using System;
using System.Linq;
using System.Threading.Tasks;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Xunit;

namespace Inno.Rendering.Shaders.Tests;

public sealed partial class ShaderGraphProgramTests
{
    [Fact]
    public void StorageGraphKeepsExplicitEffectsEvenWithoutAValueOutput()
    {
        ShaderGraphProgramResult lowered = Lower(StorageGraph());
        Assert.True(lowered.succeeded, string.Join("\n", lowered.diagnostics.Select(value => value.message)));
        ShaderIrStage stage = Assert.Single(Assert.Single(lowered.passes).stages);
        Assert.Equal(new[] { ShaderIrOperation.StorageStore, ShaderIrOperation.StorageLoad, ShaderIrOperation.StorageAtomicAdd },
            stage.body.instructions.Where(value => value.hasSideEffects).Select(value => value.operation));
    }

    [MetalShaderFact]
    public async Task StorageGraphCompilesThroughTheSameMetalToolchainAsSourceNodes()
    {
        ShaderGraphProgramResult lowered = Lower(StorageGraph());
        Assert.True(lowered.succeeded, string.Join("\n", lowered.diagnostics.Select(value => value.message)));
        var compiler = new ShaderCompiler(new BgfxShadercToolchain(BgfxShaderTargetPlatform.MacOSArm64));
        RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
        var caps = new GraphicsCapabilities(GraphicsApi.Metal, GraphicsCapability.Compute | GraphicsCapability.StorageBuffer, new(256, 8, 8192, 16),
            formats, formats, formats, formats, false, false, formats, formats, formats);
        ShaderStageToolResult result = await compiler.CompileAsync(lowered.passes[0].stages[0], compiler.CreateTarget(caps));
        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(value => value.message)));
    }

    [Fact]
    public void RerouteRetainsAggregateAndStoragePortContracts()
    {
        ShaderSourceType aggregate = ShaderSourceType.Structure("Payload", [new("points", ShaderSourceType.ArrayOf(ShaderSourceType.Atomic("float2"), 3))]);
        foreach (ShaderSourceType type in new[] { aggregate, ShaderSourceType.Storage(ShaderStorageType.Buffer(aggregate, RenderStorageAccess.ReadWrite)) })
        {
            var node = new GraphNodeRecord(new("route"), "inno.shader.reroute");
            node.SetValue("valueType", ShaderGraphDocument.Encode(ShaderGraphType.Capture(type), m_serialization, SerializationContext.empty));
            var ports = m_nodes.DescribePorts(node, m_serialization, SerializationContext.empty);
            Assert.Equal(2, ports.Count);
            Assert.All(ports, port => Assert.True(type.IsEquivalentTo(port.type)));
        }
    }

    private GraphDocument StorageGraph()
    {
        GraphDocument graph = ShaderGraphDocument.Create(new("Compute", [], [], [new("Main", ShaderProgramKind.Compute)]), m_serialization, SerializationContext.empty);
        GraphNodeRecord stage = Node("compute", ShaderGraphDocument.outputDefinitionId);
        Set(stage, "settings", new ShaderGraphStageSettings { stage = ShaderStage.Compute });
        var resourceType = new ShaderGraphType { isStorage = true, access = RenderStorageAccess.ReadWrite, storageElement = new() { id = "uint" } };
        GraphNodeRecord resource = Node("resource", "inno.shader.stage-input");
        var settings = new ShaderGraphInputSettings { id = "buffer", kind = ShaderIrInputKind.Storage, type = resourceType };
        Set(resource, "settings", settings);
        GraphNodeRecord coordinate = Node("coordinate", "inno.shader.constant"); Set(coordinate, "type", "uint"); Set(coordinate, "value", 0u);
        GraphNodeRecord value = Node("constant", "inno.shader.constant"); Set(value, "type", "uint"); Set(value, "value", 7u);
        GraphNodeRecord store = Node("z-store", "inno.shader.storage-store"), load = Node("m-load", "inno.shader.storage-load"), atomic = Node("a-atomic", "inno.shader.storage-atomic-add");
        foreach (GraphNodeRecord effect in new[] { store, load, atomic })
        { Set(effect, "resource", resourceType); Connect(resource, "value", effect, "resource"); Connect(coordinate, "value", effect, "coordinate"); }
        Connect(value, "value", store, "value"); Connect(value, "value", atomic, "value");
        Connect(store, "then", load, "after"); Connect(load, "then", atomic, "after");
        graph = ShaderGraphPrograms.Bind(graph, "Main", [stage.id], m_serialization, SerializationContext.empty);
        return ShaderGraphBindings.ChangeInput(graph, resource.id, settings, m_serialization, SerializationContext.empty);

        GraphNodeRecord Node(string id, string type)
        {
            var node = new GraphNodeRecord(new(id), type);
            if (id != "compute") Set(node, "stage", "compute");
            graph.AddNode(node); return node;
        }
        void Set<T>(GraphNodeRecord node, string key, T data) => node.SetValue(key, ShaderGraphDocument.Encode(data, m_serialization, SerializationContext.empty));
        void Connect(GraphNodeRecord output, string outputPort, GraphNodeRecord input, string inputPort)
            => graph.AddEdge(new(new(Guid.NewGuid().ToString("N")), new(output.id, new(outputPort)), new(input.id, new(inputPort))));
    }
}
