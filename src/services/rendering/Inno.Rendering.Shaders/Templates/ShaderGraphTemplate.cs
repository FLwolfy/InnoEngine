using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>
/// Declares immutable creation metadata for a Shader graph template.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShaderGraphTemplateAttribute : Attribute
{
    /// <summary>
    /// Creates Shader graph template discovery metadata.
    /// </summary>
    /// <param name="id">
    /// Stable template identity used by creation commands.
    /// </param>
    /// <param name="displayName">
    /// User-facing creation menu label.
    /// </param>
    public ShaderGraphTemplateAttribute(string id, string displayName)
    {
        this.id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Shader graph template identity cannot be empty.", nameof(id))
            : id;
        this.displayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("Shader graph template display name cannot be empty.", nameof(displayName))
            : displayName;
    }

    /// <summary>
    /// Gets the stable template identity used by creation commands.
    /// </summary>
    public string id { get; }

    /// <summary>
    /// Gets the user-facing creation menu label.
    /// </summary>
    public string displayName { get; }
}

/// <summary>
/// Contributes an ordinary shader graph to the shared asset creation workflow.
/// </summary>
public abstract class ShaderGraphTemplate
{
    /// <summary>
    /// Creates a fresh detached graph with its target and parameter declarations.
    /// </summary>
    /// <param name="serialization">
    /// Current owner converters.
    /// </param>
    /// <param name="context">
    /// Complete owner reference context.
    /// </param>
    /// <returns>
    /// A graph ready for native serialization and ordinary shader import.
    /// </returns>
    public abstract GraphDocument Create(SerializationRegistry serialization, SerializationContext context);
}

[ShaderGraphTemplate(ShaderBuiltInIds.rasterTemplate, "Raster")]
internal sealed class RasterShaderGraphTemplate : ShaderGraphTemplate
{
    /// <summary>
    /// Creates and validates a caller-owned value value.
    /// </summary>
    /// <param name="serialization">
    /// The serialization consumed by create; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="context">
    /// The operation scope that provides state, services, and ownership boundaries.
    /// </param>
    /// <returns>
    /// The validated graph document that represents the completed operation.
    /// </returns>
public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
        => ShaderGraphTemplates.CreateRaster(serialization, context);
}

[ShaderGraphTemplate(ShaderBuiltInIds.nodeTemplate, "Reusable Node")]
internal sealed class ReusableShaderGraphNodeTemplate : ShaderGraphTemplate
{
    /// <summary>
    /// Creates a value using this implementation's validated inputs.
    /// </summary>
    /// <param name="serialization">
    /// The serialization consumed by create; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="context">
    /// The context that supplies state and services for this operation.
    /// </param>
    /// <returns>
    /// The validated graph document that represents the completed operation.
    /// </returns>
    public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
    {
        GraphDocument graph = ShaderGraphDocument.Create(new("Graph Node", [], [], []), serialization, context);
        ShaderGraphNodes.WriteSettings(graph, new ShaderGraphNodeSettings(), serialization, context);
        var input = new GraphNodeRecord(new("node-inputs"), ShaderGraphNodes.inputDefinitionId)
        {
            position = new(40, 120)
        };
        input.SetValue(ShaderGraphDocument.settingsKey, ShaderGraphDocument.Encode(new ShaderGraphNodeInputSettings
        {
            ports = [new() { id = "value", type = new() { id = "float" } }]
        }, serialization, context));
        var output = new GraphNodeRecord(new("node-outputs"), ShaderGraphNodes.outputDefinitionId)
        {
            position = new(620, 120)
        };
        output.SetValue(ShaderGraphDocument.settingsKey, ShaderGraphDocument.Encode(new ShaderGraphNodeOutputSettings
        {
            ports = [new() { id = "value", type = new() { id = "float" } }]
        }, serialization, context));
        graph.AddNode(input);
        graph.AddNode(output);
        graph.AddEdge(new(new("node-value"), new(input.id, new("value")), new(output.id, new("value"))));
        return graph;
    }
}

internal static class ShaderBuiltInIds
{
    internal const string rasterTemplate = "inno.shader.raster";
    internal const string nodeTemplate = "inno.shader.node";
}
