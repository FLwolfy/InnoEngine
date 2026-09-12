using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Editor.Graph;
using Inno.Rendering;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed record ShaderClipboardData(byte[] graph, Guid owner);

internal sealed partial class ShaderEditorDocuments
{
    internal ShaderClipboardData Copy(Draft draft)
    {
        GraphDocument fragment = Controller(draft).document.Clone();
        HashSet<GraphNodeId> selected = [.. draft.canvas.selectedNodes];
        HashSet<string> stages = fragment.nodes.Where(node => selected.Contains(node.id) && node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .Select(static node => node.id.value).ToHashSet(StringComparer.Ordinal);
        foreach (GraphNodeRecord node in fragment.nodes.ToArray())
            if (!selected.Contains(node.id) && !stages.Contains(ShaderGraphDocument.Read(node, "stage", "", serialization, context))) fragment.RemoveNode(node.id);
        // Keep declaration bytes with the fragment, not live material defaults or extension objects.
        return new(GraphDocumentCodec.Encode(fragment, serialization), draft.id);
    }

    internal void Paste(Draft draft, ShaderClipboardData clipboard)
    {
        GraphDocument fragment = GraphDocumentCodec.Decode(clipboard.graph, serialization);
        GraphDocumentController controller = Controller(draft);
        GraphDocument candidate = controller.document.Clone();
        ShaderDefinition? definition = candidate.metadata.ContainsKey(ShaderGraphDocument.definitionKey) ? ShaderGraphDocument.ReadDefinition(candidate, serialization, context) : null;
        ShaderDefinition? source = fragment.metadata.ContainsKey(ShaderGraphDocument.definitionKey) ? ShaderGraphDocument.ReadDefinition(fragment, serialization, context) : null;
        Dictionary<GraphNodeId, GraphNodeId> remap = fragment.nodes.ToDictionary(static node => node.id, static _ => new GraphNodeId(Guid.NewGuid().ToString("N")));
        var passes = new Dictionary<string, string>(StringComparer.Ordinal);
        GraphNodeId? targetStage = draft.activeStage ?? candidate.nodes.FirstOrDefault(static node => node.definitionId == ShaderGraphDocument.outputDefinitionId)?.id;
        foreach (GraphNodeRecord node in fragment.nodes)
        {
            var clone = new GraphNodeRecord(remap[node.id], node.definitionId) { position = new(node.position.x + 32, node.position.y + 32) };
            foreach (var value in node.values) clone.SetValue(value.Key, value.Value.Clone());
            string stage = ShaderGraphDocument.Read(node, "stage", "", serialization, context);
            if (stage.Length != 0)
            {
                if (remap.TryGetValue(new(stage), out GraphNodeId mapped)) stage = mapped.value;
                else if ((clipboard.owner != draft.id || candidate.FindNode(new(stage)) is null) && targetStage is GraphNodeId target) stage = target.value;
                clone.SetValue("stage", ShaderGraphDocument.Encode(stage, serialization, context));
            }
            if (node.definitionId == ShaderGraphDocument.outputDefinitionId && definition is not null && source is not null)
            {
                ShaderGraphStageSettings settings = ShaderGraphDocument.Read(node, "settings", new ShaderGraphStageSettings(), serialization, context);
                if (!passes.TryGetValue(settings.pass, out string? destination))
                {
                    destination = settings.pass;
                    for (int suffix = 2; definition.passes.Any(pass => pass.name == destination); suffix++) destination = settings.pass + " " + suffix;
                    passes.Add(settings.pass, destination);
                    int index = Array.FindIndex(source.passes, pass => pass.name == settings.pass);
                    if (index >= 0)
                    {
                        ShaderPassDefinition pass = source.passes[index];
                        pass.name = destination;
                        definition.passes = [.. definition.passes, pass];
                    }
                }
                settings.pass = destination;
                clone.SetValue("settings", ShaderGraphDocument.Encode(settings, serialization, context));
            }
            candidate.AddNode(clone);
        }
        foreach (GraphEdgeRecord edge in fragment.edges)
            candidate.AddEdge(new(new(Guid.NewGuid().ToString("N")), new(remap[edge.output.nodeId], edge.output.portId), new(remap[edge.input.nodeId], edge.input.portId)));
        if (definition is not null)
        {
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
                if (next.type != property.type || next.bindingKind != property.bindingKind || next.storageAccess != property.storageAccess)
                    throw new InvalidOperationException($"Cannot paste incompatible shader parameter '{property.id.value}'. Rename the copied parameter first.");
            }
        }
        controller.ReplaceDocument(candidate, "Paste Shader Nodes");
        draft.canvas.SelectNodes(remap.Values);
        Changed(draft);
    }
}
