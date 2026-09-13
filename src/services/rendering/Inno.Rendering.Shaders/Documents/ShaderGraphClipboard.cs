using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Copies and pastes shader structures as detached atomic candidates, including their parameter and Pass contracts.</summary>
public static class ShaderGraphClipboard
{
    /// <summary>Captures selected nodes and the owned contents of selected stage outputs.</summary>
    /// <param name="graph">Source document, never changed.</param>
    /// <param name="nodes">Selected stable identities.</param>
    /// <param name="serialization">Current native converter registry.</param>
    /// <param name="context">Owner reference context.</param>
    /// <returns>Detached fragment retaining source declarations for a later paste.</returns>
    public static GraphDocument Copy(GraphDocument graph, IEnumerable<GraphNodeId> nodes, SerializationRegistry serialization, SerializationContext context)
    {
        GraphDocument fragment = graph.Clone();
        HashSet<GraphNodeId> selected = [.. nodes];
        HashSet<string> stages = fragment.nodes.Where(node => selected.Contains(node.id) && node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .Select(static node => node.id.value).ToHashSet(StringComparer.Ordinal);
        foreach (GraphNodeRecord node in fragment.nodes.ToArray())
            if (!selected.Contains(node.id) && !stages.Contains(ShaderGraphDocument.Read(node, "stage", "", serialization, context))) fragment.RemoveNode(node.id);
        return fragment;
    }

    /// <summary>Creates a detached paste candidate with new node identities and remapped program, Pass and Technique references.</summary>
    /// <param name="graph">Destination document, never changed.</param>
    /// <param name="fragment">Detached copied fragment, never changed.</param>
    /// <param name="preserveExternalStageReferences">Whether references to existing destination stages belong to the same document.</param>
    /// <param name="activeStage">Destination stage for copied non-stage nodes.</param>
    /// <param name="serialization">Current native converter registry.</param>
    /// <param name="context">Owner reference context.</param>
    /// <returns>A complete candidate suitable for one History transaction.</returns>
    public static ShaderGraphPasteResult Paste(GraphDocument graph, GraphDocument fragment, bool preserveExternalStageReferences,
        GraphNodeId? activeStage, SerializationRegistry serialization, SerializationContext context)
    {
        GraphDocument candidate = graph.Clone();
        ShaderDefinition? definition = candidate.metadata.ContainsKey(ShaderGraphDocument.definitionKey) ? ShaderGraphDocument.ReadDefinition(candidate, serialization, context) : null;
        ShaderDefinition? source = fragment.metadata.ContainsKey(ShaderGraphDocument.definitionKey) ? ShaderGraphDocument.ReadDefinition(fragment, serialization, context) : null;
        Dictionary<GraphNodeId, GraphNodeId> remap = fragment.nodes.ToDictionary(static node => node.id, static _ => new GraphNodeId(Guid.NewGuid().ToString("N")));
        var passes = new Dictionary<string, string>(StringComparer.Ordinal);
        var programs = candidate.metadata.ContainsKey(ShaderGraphPrograms.bindingsKey)
            ? ShaderGraphPrograms.Read(candidate, serialization, context).ToList() : [];
        if (definition is not null && source is not null)
        {
            foreach (ShaderGraphPassProgram program in ShaderGraphPrograms.Read(fragment, serialization, context))
            {
                GraphNodeId[] copiedStages = program.stages.Select(static id => new GraphNodeId(id)).Where(remap.ContainsKey).ToArray();
                if (copiedStages.Length == 0) continue;
                int index = Array.FindIndex(source.passes, pass => pass.name == program.pass);
                if (index < 0) continue;
                string destination = program.pass;
                for (int suffix = 2; definition.passes.Any(pass => pass.name == destination); suffix++) destination = program.pass + " " + suffix;
                passes.Add(program.pass, destination);
                ShaderPassDefinition pass = source.passes[index];
                pass.name = destination;
                definition.passes = [.. definition.passes, pass];
                programs.Add(new() { pass = destination, stages = copiedStages.Select(id => remap[id].value).ToArray() });
            }
        }
        if (definition is not null && source is not null)
        {
            foreach (ShaderTechniqueDefinition technique in source.techniques)
            {
                ShaderTechniquePass[] mappings = technique.passes.Where(mapping => passes.ContainsKey(mapping.passName))
                    .Select(mapping => new ShaderTechniquePass(mapping.role, passes[mapping.passName])).ToArray();
                if (mappings.Length == 0) continue;
                string id = technique.id.value;
                for (int suffix = 2; definition.techniques.Any(value => value.id.value == id); suffix++)
                    id = technique.id.value + "-" + suffix;
                definition.techniques = [.. definition.techniques, new ShaderTechniqueDefinition(new(id), technique.contract, mappings, technique.requiredFeatures)];
            }
        }
        GraphNodeId? targetStage = activeStage ?? candidate.nodes.FirstOrDefault(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)?.id;
        foreach (GraphNodeRecord node in fragment.nodes)
        {
            var clone = new GraphNodeRecord(remap[node.id], node.definitionId) { position = new(node.position.x + 32, node.position.y + 32) };
            foreach (var value in node.values) clone.SetValue(value.Key, value.Value.Clone());
            string stage = ShaderGraphDocument.Read(node, "stage", "", serialization, context);
            if (stage.Length != 0)
            {
                if (remap.TryGetValue(new(stage), out GraphNodeId mapped)) stage = mapped.value;
                else if ((!preserveExternalStageReferences || candidate.FindNode(new(stage)) is null) && targetStage is GraphNodeId target) stage = target.value;
                clone.SetValue("stage", ShaderGraphDocument.Encode(stage, serialization, context));
            }
            candidate.AddNode(clone);
        }
        foreach (GraphEdgeRecord edge in fragment.edges)
            candidate.AddEdge(new(new(Guid.NewGuid().ToString("N")), new(remap[edge.output.nodeId], edge.output.portId), new(remap[edge.input.nodeId], edge.input.portId)));
        if (definition is not null)
        {
            candidate.SetMetadata(ShaderGraphPrograms.bindingsKey, ShaderGraphDocument.Encode(programs.ToArray(), serialization, context));
            // Preserve copied defaults. Reject incompatible destination bindings before publishing any edit.
            if (source is not null)
            {
                HashSet<string> bindings = fragment.nodes.Where(static node => node.definitionId == "inno.shader.stage-input")
                    .Select(node => ShaderGraphDocument.Read(node, "settings", new ShaderGraphInputSettings(), serialization, context).id).ToHashSet(StringComparer.Ordinal);
                definition.properties = [.. definition.properties, .. source.properties.Where(property => bindings.Contains(property.id.value)
                    && !definition.properties.Any(existing => existing.id == property.id))];
            }
            candidate.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(serialization.Serialize(definition, context), serialization, context));
            foreach (GraphNodeRecord node in candidate.nodes.Where(node => remap.Values.Contains(node.id) && node.definitionId == "inno.shader.stage-input").ToArray())
                candidate = ShaderGraphBindings.ChangeInput(candidate, node.id, ShaderGraphDocument.Read(node, "settings", new ShaderGraphInputSettings(), serialization, context), serialization, context);
            ShaderDefinition updated = ShaderGraphDocument.ReadDefinition(candidate, serialization, context);
            foreach (ShaderPropertyDefinition property in definition.properties)
            {
                ShaderPropertyDefinition next = updated.properties.Single(value => value.id == property.id);
                if (next.type != property.type || next.bindingKind != property.bindingKind || next.storageAccess != property.storageAccess || next.bindingOwner != property.bindingOwner)
                    throw new InvalidOperationException($"Cannot paste incompatible shader parameter '{property.id.value}'. Rename the copied parameter first.");
            }
        }
        return new(candidate, remap.Values.ToArray());
    }
}

/// <summary>Contains a detached paste candidate and its newly allocated node identities.</summary>
public sealed class ShaderGraphPasteResult
{
    internal ShaderGraphPasteResult(GraphDocument graph, GraphNodeId[] nodes) { document = graph; insertedNodes = Array.AsReadOnly(nodes); }
    /// <summary>Gets the complete detached candidate; the caller owns its subsequent edits.</summary>
    public GraphDocument document { get; }
    /// <summary>Gets new identities suitable for selecting the pasted nodes.</summary>
    public IReadOnlyList<GraphNodeId> insertedNodes { get; }
}
