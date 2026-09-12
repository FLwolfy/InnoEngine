using System;
using System.Collections.Generic;
using Inno.Core.Graphs;

namespace Inno.Rendering.Shaders;

/// <summary>Extracts one statically selected component from a vector or matrix.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderExtractNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.extract";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        ShaderSourceType type = ShaderSourceType.Atomic(context.Read("type", "float4"));
        var builder = new ShaderIrBuilder();
        ShaderIrValue member = builder.Extract(builder.Input("input", type), context.Read("index", 0));
        return [new("input", type, GraphPortDirection.Input), new("value", member.type, GraphPortDirection.Output)];
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue> { ["value"] = context.builder.Extract(context.Input("input"), context.description.Read("index", 0)) };
}

/// <summary>Samples a graph-connected texture with implicit derivatives or an explicit level of detail.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderSampleNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.sample";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        string type = context.Read("type", "sampled-texture2d");
        string coordinate = type switch { "sampled-texture2d" => "float2", "sampled-texture2d-array" or "sampled-texture3d" or "sampled-texture-cube" => "float3", _ => throw new InvalidOperationException("Unknown sampled texture type.") };
        var ports = new List<ShaderNodePort> { new("texture", ShaderSourceType.Atomic(type), GraphPortDirection.Input), new("coordinate", ShaderSourceType.Atomic(coordinate), GraphPortDirection.Input) };
        if (context.Read("explicitLevel", false)) ports.Add(new("level", ShaderSourceType.Atomic("float"), GraphPortDirection.Input));
        ports.Add(new("value", ShaderSourceType.Atomic("float4"), GraphPortDirection.Output));
        return ports;
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue> { ["value"] = context.description.Read("explicitLevel", false)
            ? context.builder.SampleLevel(context.Input("texture"), context.Input("coordinate"), context.Input("level"))
            : context.builder.Sample(context.Input("texture"), context.Input("coordinate")) };
}
