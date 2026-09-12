using System.Collections.Generic;
using Inno.Core.Graphs;

namespace Inno.Rendering.Shaders;

/// <summary>Forwards a typed connection without adding instructions or changing resource-effect order.</summary>
[ShaderNodeCompilerExtension]
public sealed class ShaderRerouteNodeCompiler : IShaderNodeCompiler
{
    /// <inheritdoc />
    public string definitionId => "inno.shader.reroute";
    /// <inheritdoc />
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        ShaderSourceType type = context.Read("valueType", new ShaderGraphType { id = "float" }).CreateType();
        return [new("input", type, GraphPortDirection.Input), new("value", type, GraphPortDirection.Output)];
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue> { ["value"] = context.Input("input") };
}
