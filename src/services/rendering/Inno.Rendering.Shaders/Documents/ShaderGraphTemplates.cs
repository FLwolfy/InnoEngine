using System;
using Inno.Core.Graphs;
using Inno.Core.Mathematics;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Provides small backend-neutral starting documents composed exclusively of ordinary graph nodes.</summary>
public static class ShaderGraphTemplates
{
    /// <summary>Creates a raster graph accepting clip-space XY positions and an exposed RGBA color.</summary>
    /// <param name="serialization">Owner native converter registry.</param>
    /// <param name="context">Complete owner asset/reference context for material defaults.</param>
    /// <returns>A valid two-stage document; pipelines may replace its vertex transform or provide their own templates.</returns>
    public static GraphDocument CreateRaster(SerializationRegistry serialization, SerializationContext context)
    {
        var pass = new ShaderPassDefinition("Main", ShaderProgramKind.Raster, renderState: new()
        { cull = ShaderCullMode.None, depthCompare = ShaderCompareFunction.Always, depthWrite = false, blend = RenderBlendState.alpha, colorWriteMask = 15 });
        var definition = new ShaderDefinition("New Shader", [new(new("color"), "Color", ShaderPropertyType.Color, ShaderStage.Fragment,
            MaterialValue.FromVector(new Vector4(1f, 1f, 1f, 1f)))], [], [pass]);
        GraphDocument graph = ShaderGraphDocument.Create(definition, serialization, context);
        GraphNodeRecord vertex = Node("vertex", ShaderGraphDocument.outputDefinitionId, 770, 50);
        Set(vertex, "settings", new ShaderGraphStageSettings { stage = ShaderStage.Vertex, outputs = [new() { id = "position", kind = ShaderIrOutputKind.ClipPosition }] });
        GraphNodeRecord fragment = Node("fragment", ShaderGraphDocument.outputDefinitionId, 770, 640);
        Set(fragment, "settings", new ShaderGraphStageSettings { stage = ShaderStage.Fragment, outputs = [new() { id = "color", kind = ShaderIrOutputKind.Color }] });
        GraphNodeRecord position = Node("position", "inno.shader.stage-input", 40, 50, vertex);
        Set(position, "settings", new ShaderGraphInputSettings { id = "position", type = new() { id = "float2" }, kind = ShaderIrInputKind.VertexAttribute, semantic = "position" });
        GraphNodeRecord construct = Node("clip-position", "inno.shader.construct", 480, 50, vertex);
        Set(construct, "type", "float4");
        for (int index = 0; index < 2; index++)
        {
            GraphNodeRecord extract = Node("axis-" + index, "inno.shader.extract", 340, 280 + index * 270, vertex);
            Set(extract, "type", "float2"); Set(extract, "index", index);
            Connect(position, "value", extract, "input"); Connect(extract, "value", construct, "component." + index);
            GraphNodeRecord constant = Node("homogeneous-" + index, "inno.shader.constant", 40, 370 + index * 250, vertex);
            Set(constant, "value", (float)index); Connect(constant, "value", construct, "component." + (index + 2));
        }
        Connect(construct, "value", vertex, "position");
        GraphNodeRecord color = Node("color", "inno.shader.stage-input", 480, 920, fragment);
        Set(color, "settings", new ShaderGraphInputSettings { id = "color", type = new() { id = "float4" }, kind = ShaderIrInputKind.Uniform, semantic = "" });
        Connect(color, "value", fragment, "color");
        return graph;

        GraphNodeRecord Node(string id, string type, float x, float y, GraphNodeRecord? stage = null)
        {
            var node = new GraphNodeRecord(new(id), type) { position = new(x, y) };
            if (stage is not null) Set(node, "stage", stage.id.value);
            graph.AddNode(node); return node;
        }
        void Set<T>(GraphNodeRecord node, string key, T value) => node.SetValue(key, ShaderGraphDocument.Encode(value, serialization, context));
        void Connect(GraphNodeRecord a, string output, GraphNodeRecord b, string input)
            => graph.AddEdge(new(new(Guid.NewGuid().ToString("N")), new(a.id, new(output)), new(b.id, new(input))));
    }
}
