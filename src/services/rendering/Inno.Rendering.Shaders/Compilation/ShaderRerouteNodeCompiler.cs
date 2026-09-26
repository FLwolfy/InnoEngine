using System.Collections.Generic;
using Inno.Core.Graphs;

namespace Inno.Rendering.Shaders;

/// <summary>
/// Forwards a typed connection without adding instructions or changing resource-effect order.
/// </summary>
public sealed class ShaderRerouteNodeCompiler : IShaderNodeCompiler
{
    /// <summary>
    /// Gets the definition id text used by the current instance.
    /// </summary>
    public string definitionId => "inno.shader.reroute";
    /// <summary>
    /// Gets a ports required by the implemented contract.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
    {
        ShaderSourceType type = context.Read("valueType", new ShaderGraphType { id = "float" }).CreateType();
        return [new("input", type, GraphPortDirection.Input, required: false), new("value", type, GraphPortDirection.Output)];
    }
    /// <summary>
    /// Lowers this graph node to typed shader IR after validating its inputs.
    /// </summary>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// An immutable snapshot of the values selected by the operation.
    /// </returns>
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue> { ["value"] = context.Input("input") };
}
