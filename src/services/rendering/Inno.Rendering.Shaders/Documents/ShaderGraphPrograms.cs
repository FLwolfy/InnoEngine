using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>References shared stage output identities from a material-selectable pass.</summary>
public struct ShaderGraphPassProgram
{
    /// <summary>Gets or sets the exact pass identity in the runtime shader definition.</summary>
    public string pass { get; set; }
    /// <summary>Gets or sets stable stage output node identities; computation remains owned by those nodes.</summary>
    public string[] stages { get; set; }
}

/// <summary>Edits pass-to-program references without copying computation or changing runtime render states.</summary>
public static class ShaderGraphPrograms
{
    /// <summary>Identifies native metadata holding pass references to shared stage programs.</summary>
    public const string bindingsKey = "inno.shader.programs";

    /// <summary>Reads detached pass-to-stage references, including unresolved authored identities.</summary>
    /// <param name="graph">Shader authoring document.</param>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>Detached native references; corrupt or missing program metadata is an error.</returns>
    public static ShaderGraphPassProgram[] Read(GraphDocument graph, SerializationRegistry serialization, SerializationContext context)
        => graph.metadata.TryGetValue(bindingsKey, out GraphSerializedValue? data)
            ? ShaderGraphDocument.Decode<ShaderGraphPassProgram[]>(data, serialization, context)
            : throw new InvalidOperationException("The shader document has no pass-to-program references.");

    /// <summary>Assigns existing shared stages to one pass in an atomic detached candidate.</summary>
    /// <param name="graph">Source document, never modified.</param>
    /// <param name="pass">Existing pass identity.</param>
    /// <param name="stages">Existing stage output node identities; an empty set preserves an incomplete pass.</param>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>A candidate suitable for one shared History transaction.</returns>
    public static GraphDocument Bind(GraphDocument graph, string pass, IEnumerable<GraphNodeId> stages,
        SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, serialization, context);
        if (!definition.passes.Any(value => value.name == pass)) throw new ArgumentException($"Unknown pass '{pass}'.", nameof(pass));
        string[] references = stages.Select(static value => value.value).ToArray();
        if (references.Distinct(StringComparer.Ordinal).Count() != references.Length)
            throw new ArgumentException("A pass cannot reference the same stage twice.", nameof(stages));
        foreach (string id in references)
            if (graph.FindNode(new(id))?.definitionId != ShaderGraphDocument.outputDefinitionId)
                throw new ArgumentException($"Stage program '{id}' is unavailable.", nameof(stages));
        GraphDocument candidate = graph.Clone();
        ShaderGraphPassProgram[] bindings = Read(graph, serialization, context);
        int index = Array.FindIndex(bindings, value => value.pass == pass);
        var binding = new ShaderGraphPassProgram { pass = pass, stages = references };
        if (index < 0) bindings = [.. bindings, binding]; else bindings[index] = binding;
        Write(candidate, bindings, serialization, context);
        return candidate;
    }

    /// <summary>Removes a pass and only computations whose final referencing pass was removed.</summary>
    /// <param name="graph">Source document, never modified.</param>
    /// <param name="pass">Existing pass identity.</param>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>A candidate preserving programs shared by other passes and their parameter declarations.</returns>
    public static GraphDocument RemovePass(GraphDocument graph, string pass, SerializationRegistry serialization, SerializationContext context)
    {
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(graph, serialization, context);
        if (!definition.passes.Any(value => value.name == pass)) throw new ArgumentException($"Unknown pass '{pass}'.", nameof(pass));
        ShaderGraphPassProgram[] bindings = Read(graph, serialization, context);
        string[] removedStages = bindings.Where(value => value.pass == pass).SelectMany(static value => value.stages).ToArray();
        ShaderGraphPassProgram[] remaining = bindings.Where(value => value.pass != pass).ToArray();
        HashSet<string> retained = remaining.SelectMany(static value => value.stages).ToHashSet(StringComparer.Ordinal);
        GraphDocument candidate = graph.Clone();
        definition.passes = definition.passes.Where(value => value.name != pass).ToArray();
        for (int index = 0; index < definition.techniques.Length; index++)
        {
            ShaderTechniqueDefinition technique = definition.techniques[index];
            technique.passes = technique.passes.Where(value => value.passName != pass).ToArray();
            definition.techniques[index] = technique;
        }
        candidate.SetMetadata(ShaderGraphDocument.definitionKey,
            ShaderGraphDocument.Encode(serialization.Serialize(definition, context), serialization, context));
        Write(candidate, remaining, serialization, context);
        return ShaderGraphBindings.RemoveNodes(candidate, removedStages.Where(id => !retained.Contains(id))
            .Distinct(StringComparer.Ordinal).Select(static id => new GraphNodeId(id)).Where(id => candidate.FindNode(id) is not null), serialization, context);
    }

    internal static void Write(GraphDocument graph, ShaderGraphPassProgram[] programs, SerializationRegistry serialization, SerializationContext context)
        => graph.SetMetadata(bindingsKey, ShaderGraphDocument.Encode(programs, serialization, context));
}
