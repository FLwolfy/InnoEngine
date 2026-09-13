using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Keeps graph input edits and the material-visible parameter contract in one neutral document change.</summary>
public static class ShaderGraphBindings
{
    /// <summary>Removes selected nodes, stage-owned nodes and declarations which lose their last graph owner.</summary>
    /// <param name="graph">Current shader graph, never mutated by this operation.</param>
    /// <param name="nodeIds">Explicitly selected node identities. Deleting an output also deletes its stage contents.</param>
    /// <param name="serialization">Owner native converter registry.</param>
    /// <param name="context">Complete owner reference context for material defaults.</param>
    /// <returns>A detached candidate suitable for one atomic history entry; unrelated incomplete content is retained.</returns>
    /// <exception cref="ArgumentException">A selected identity is not in the document.</exception>
    public static GraphDocument RemoveNodes(GraphDocument graph, IEnumerable<GraphNodeId> nodeIds,
        SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodeIds);
        HashSet<GraphNodeId> removed = [.. nodeIds];
        foreach (GraphNodeId id in removed)
            if (graph.FindNode(id) is null) throw new ArgumentException("A selected shader node is missing.", nameof(nodeIds));
        GraphDocument candidate = graph.Clone();
        HashSet<string> stages = graph.nodes.Where(node => removed.Contains(node.id) && node.definitionId == ShaderGraphDocument.outputDefinitionId)
            .Select(static node => node.id.value).ToHashSet(StringComparer.Ordinal);
        foreach (GraphNodeRecord node in graph.nodes)
            if (stages.Contains(ShaderGraphDocument.Read(node, "stage", "", serialization, context))) removed.Add(node.id);

        ShaderGraphPassProgram[] programs = graph.metadata.ContainsKey(ShaderGraphPrograms.bindingsKey)
            ? ShaderGraphPrograms.Read(graph, serialization, context) : [];
        HashSet<string> affectedPasses = programs.Where(program => program.stages.Any(stages.Contains))
            .Select(static program => program.pass).ToHashSet(StringComparer.Ordinal);
        for (int index = 0; index < programs.Length; index++)
        {
            ShaderGraphPassProgram program = programs[index];
            program.stages = program.stages.Where(id => !stages.Contains(id)).ToArray();
            programs[index] = program;
            if (program.stages.Length != 0) affectedPasses.Remove(program.pass);
        }
        HashSet<string> affectedBindings = [];
        foreach (GraphNodeRecord node in graph.nodes.Where(node => removed.Contains(node.id) && node.definitionId == "inno.shader.stage-input"))
        {
            ShaderGraphInputSettings input = ShaderGraphDocument.Read(node, "settings", new ShaderGraphInputSettings(), serialization, context);
            if (IsBinding(input)) affectedBindings.Add(input.id);
            affectedBindings.Add(ShaderGraphDocument.Read(node, "bindingIdentity", "", serialization, context));
        }
        foreach (GraphNodeId id in removed) candidate.RemoveNode(id);
        if (!candidate.metadata.ContainsKey(ShaderGraphDocument.definitionKey)) return candidate;
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(candidate, serialization, context);
        ShaderGraphPrograms.Write(candidate, programs.Where(program => !affectedPasses.Contains(program.pass)).ToArray(), serialization, context);
        definition.passes = definition.passes.Where(pass => !affectedPasses.Contains(pass.name)).ToArray();
        for (int i = 0; i < definition.techniques.Length; i++)
        {
            ShaderTechniqueDefinition technique = definition.techniques[i];
            technique.passes = technique.passes.Where(pass => !affectedPasses.Contains(pass.passName)).ToArray();
            definition.techniques[i] = technique;
        }
        var remainingBindings = new Dictionary<string, ShaderStage>(StringComparer.Ordinal);
        foreach (GraphNodeRecord node in candidate.nodes.Where(static node => node.definitionId == "inno.shader.stage-input"))
        {
            ShaderGraphInputSettings input = ShaderGraphDocument.Read(node, "settings", new ShaderGraphInputSettings(), serialization, context);
            string id = string.IsNullOrWhiteSpace(input.id) ? ShaderGraphDocument.Read(node, "bindingIdentity", "", serialization, context) : input.id;
            if (!IsBinding(input) || !affectedBindings.Contains(id)) continue;
            string stageId = ShaderGraphDocument.Read(node, "stage", "", serialization, context);
            GraphNodeRecord? output = string.IsNullOrWhiteSpace(stageId) ? null : candidate.FindNode(new(stageId));
            ShaderStage stage = output is null ? ShaderStage.None : ShaderGraphDocument.Read(output, "settings", new ShaderGraphStageSettings(), serialization, context).stage;
            remainingBindings[id] = remainingBindings.GetValueOrDefault(id) | stage;
        }
        definition.properties = definition.properties.Where(property => !affectedBindings.Contains(property.id.value) || remainingBindings.ContainsKey(property.id.value))
            .Select(property => { if (remainingBindings.TryGetValue(property.id.value, out ShaderStage stagesUsed)) property.stages = stagesUsed; return property; }).ToArray();
        candidate.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(serialization.Serialize(definition, context), serialization, context));
        return candidate;
    }

    /// <summary>Creates a detached candidate with the input and its material/resource declaration updated together.</summary>
    /// <param name="graph">Current shader graph, never mutated by this operation.</param>
    /// <param name="nodeId">Stage input node identity.</param>
    /// <param name="settings">New input settings, including incomplete values while typing.</param>
    /// <param name="serialization">Owner native converter registry.</param>
    /// <param name="context">Complete owner reference context for texture defaults.</param>
    /// <returns>A serializable candidate. Invalid input remains visible for normal graph validation.</returns>
    /// <exception cref="ArgumentException">The identity does not name a stage input node.</exception>
    public static GraphDocument ChangeInput(GraphDocument graph, GraphNodeId nodeId, ShaderGraphInputSettings settings,
        SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(settings);
        GraphDocument candidate = graph.Clone();
        GraphNodeRecord node = candidate.FindNode(nodeId) ?? throw new ArgumentException("The stage input node is missing.", nameof(nodeId));
        if (node.definitionId != "inno.shader.stage-input") throw new ArgumentException("The node is not a stage input.", nameof(nodeId));
        ShaderGraphInputSettings previous = ShaderGraphDocument.Read(node, "settings", new ShaderGraphInputSettings(), serialization, context);
        // Keep the last declared identity through incomplete text input; clearing a name must not erase its default value.
        if (string.IsNullOrWhiteSpace(previous.id)) previous.id = ShaderGraphDocument.Read(node, "bindingIdentity", "", serialization, context);
        node.SetValue("settings", ShaderGraphDocument.Encode(settings, serialization, context));
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(candidate, serialization, context);
        var properties = definition.properties.ToList();
        ShaderPropertyDefinition? previousProperty = properties.Where(value => value.id.value == previous.id).Cast<ShaderPropertyDefinition?>().FirstOrDefault();
        if (IsBinding(previous) && !string.IsNullOrWhiteSpace(previous.id))
            node.SetValue("bindingIdentity", ShaderGraphDocument.Encode(previous.id, serialization, context));
        if (IsBinding(previous) && (!IsBinding(settings) || !string.IsNullOrWhiteSpace(settings.id) && previous.id != settings.id)
            && !candidate.nodes.Where(value => value.id != nodeId && value.definitionId == "inno.shader.stage-input")
                .Select(value => ShaderGraphDocument.Read(value, "settings", new ShaderGraphInputSettings(), serialization, context))
                .Any(value => IsBinding(value) && value.id == previous.id))
            properties.RemoveAll(value => value.id.value == previous.id);
        if (IsBinding(settings) && !string.IsNullOrWhiteSpace(settings.id) && TryPropertyType(settings.type, out ShaderPropertyType type))
        {
            ShaderPropertyDefinition? existing = properties.Where(value => value.id.value == settings.id).Cast<ShaderPropertyDefinition?>().FirstOrDefault();
            ShaderPropertyDefinition? source = existing ?? previousProperty;
            if (type == ShaderPropertyType.Vector4 && source?.type == ShaderPropertyType.Color) type = ShaderPropertyType.Color;
            ShaderPropertyBindingKind kind = settings.kind switch
            {
                ShaderIrInputKind.SampledTexture => ShaderPropertyBindingKind.SampledTexture,
                ShaderIrInputKind.Storage => settings.type.isImage ? ShaderPropertyBindingKind.StorageTexture : ShaderPropertyBindingKind.StorageBuffer,
                _ => ShaderPropertyBindingKind.Uniform
            };
            ShaderStage stages = ShaderStage.None;
            foreach (GraphNodeRecord input in candidate.nodes.Where(static value => value.definitionId == "inno.shader.stage-input"))
            {
                ShaderGraphInputSettings value = ShaderGraphDocument.Read(input, "settings", new ShaderGraphInputSettings(), serialization, context);
                if (value.id != settings.id || !IsBinding(value)) continue;
                string stageId = ShaderGraphDocument.Read(input, "stage", "", serialization, context);
                GraphNodeRecord? output = string.IsNullOrWhiteSpace(stageId) ? null : candidate.FindNode(new(stageId));
                if (output is not null) stages |= ShaderGraphDocument.Read(output, "settings", new ShaderGraphStageSettings(), serialization, context).stage;
            }
            MaterialValue defaultValue = source?.type == type ? source.Value.defaultValue : default;
            var declaration = new ShaderPropertyDefinition(new(settings.id), source?.displayName ?? settings.id, type, stages,
                defaultValue, kind, settings.type.access, source?.bindingOwner ?? (kind is ShaderPropertyBindingKind.StorageBuffer or ShaderPropertyBindingKind.StorageTexture
                    ? ShaderPropertyBindingOwner.RenderPass : ShaderPropertyBindingOwner.Material));
            int index = properties.FindIndex(value => value.id.value == settings.id);
            if (index < 0) properties.Add(declaration); else properties[index] = declaration;
            node.SetValue("bindingIdentity", ShaderGraphDocument.Encode(settings.id, serialization, context));
        }
        definition.properties = properties.ToArray();
        candidate.SetMetadata(ShaderGraphDocument.definitionKey, ShaderGraphDocument.Encode(serialization.Serialize(definition, context), serialization, context));
        return candidate;
    }

    private static bool IsBinding(ShaderGraphInputSettings value)
        => value.kind is ShaderIrInputKind.Uniform or ShaderIrInputKind.SampledTexture or ShaderIrInputKind.Storage;

    private static bool TryPropertyType(ShaderGraphType value, out ShaderPropertyType type)
    {
        type = value.isStorage ? value.isImage ? value.dimension switch
        {
            RenderTextureDimension.Texture3D => ShaderPropertyType.Texture3D,
            RenderTextureDimension.Cube => ShaderPropertyType.TextureCube,
            _ => value.isArray ? ShaderPropertyType.Texture2DArray : ShaderPropertyType.Texture2D
        } : ShaderPropertyType.Buffer : (value.element ?? value).id switch
        {
            "float" => ShaderPropertyType.Float, "float2" => ShaderPropertyType.Vector2, "float3" => ShaderPropertyType.Vector3,
            "float4" => ShaderPropertyType.Vector4, "float4x4" => ShaderPropertyType.Matrix4x4,
            "sampled-texture2d" => ShaderPropertyType.Texture2D, "sampled-texture2d-array" => ShaderPropertyType.Texture2DArray,
            "sampled-texture3d" => ShaderPropertyType.Texture3D, "sampled-texture-cube" => ShaderPropertyType.TextureCube, _ => (ShaderPropertyType)(-1)
        };
        return Enum.IsDefined(type);
    }
}
