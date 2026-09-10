using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Diagnostics;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering;

namespace Inno.Rendering.MaterialGraph;

/// <summary>
/// Contains deterministic validation diagnostics and mapped material properties.
/// </summary>
public sealed class MaterialGraphEvaluationResult
{
    /// <summary>
    /// Creates an evaluation result.
    /// </summary>
    /// <param name="shader">
    /// Shader selected by Material Output.
    /// </param>
    /// <param name="techniqueId">
    /// Explicit technique selection, or an invalid identity for automatic selection.
    /// </param>
    /// <param name="properties">
    /// Mapped material property entries.
    /// </param>
    /// <param name="diagnostics">
    /// Graph diagnostics in deterministic order.
    /// </param>
    public MaterialGraphEvaluationResult(
        ShaderAsset? shader,
        ShaderTechniqueId techniqueId,
        IReadOnlyList<MaterialPropertyEntry> properties,
        IReadOnlyList<GraphDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.shader = shader;
        this.techniqueId = techniqueId;
        this.properties = Array.AsReadOnly(properties.ToArray());
        this.diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>
    /// Gets the shader selected by Material Output.
    /// </summary>
    public ShaderAsset? shader { get; }

    /// <summary>
    /// Gets the explicitly selected technique, or an invalid identity for automatic selection.
    /// </summary>
    public ShaderTechniqueId techniqueId { get; }

    /// <summary>
    /// Gets material properties mapped by connected nodes.
    /// </summary>
    public IReadOnlyList<MaterialPropertyEntry> properties { get; }

    /// <summary>
    /// Gets deterministic graph and material diagnostics.
    /// </summary>
    public IReadOnlyList<GraphDiagnostic> diagnostics { get; }

    /// <summary>
    /// Gets whether the graph can be committed as a runtime material.
    /// </summary>
    public bool succeeded => diagnostics.All(static value => value.severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Maps a neutral material graph onto an ordinary runtime material without generating shader code.
/// </summary>
public static class MaterialGraphEvaluator
{
    /// <summary>
    /// Validates and evaluates one material graph asset.
    /// </summary>
    /// <param name="asset">
    /// Material graph to evaluate.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by node values.
    /// </param>
    /// <returns>
    /// Mapped property values and structured diagnostics.
    /// </returns>
    public static MaterialGraphEvaluationResult Evaluate(
        MaterialGraphAsset asset,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(serialization);
        if (asset.document is null)
            return Failure("MATERIAL_GRAPH_DOCUMENT_MISSING", "A Material Graph requires a graph document.");
        return Evaluate(asset, asset.document, serialization);
    }

    /// <summary>
    /// Validates an embedded authoring graph against its owning ordinary material.
    /// </summary>
    /// <param name="asset">
    /// Ordinary runtime material that owns the graph.
    /// </param>
    /// <param name="document">
    /// Detached authoring graph.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by node values.
    /// </param>
    /// <returns>
    /// Mapped values and deterministic diagnostics.
    /// </returns>
    public static MaterialGraphEvaluationResult Evaluate(
        MaterialAsset asset,
        GraphDocument document,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(serialization);
        GraphNodeRecord[] outputs = document.nodes
            .Where(static node => node.definitionId == MaterialGraphIds.outputNode)
            .ToArray();
        if (outputs.Length != 1)
        {
            return Failure(
                "MATERIAL_GRAPH_OUTPUT_COUNT",
                $"A Material Graph requires exactly one Material Output node; found {outputs.Length}.");
        }
        Guid shaderId = MaterialGraphDocumentModel.ReadShaderId(document, serialization);
        if (shaderId == Guid.Empty
            || asset.shader is not ShaderAsset shader
            || shader.identity.persistentId != shaderId
            || shader.definition is not ShaderDefinition definition)
        {
            return Failure(
                "MATERIAL_GRAPH_SHADER_MISSING",
                "Material Output requires its selected shader to resolve with a committed definition.");
        }
        ShaderTechniqueId techniqueId = MaterialGraphDocumentModel.ReadTechniqueId(document, serialization);
        var resolver = new MaterialGraphNodeResolver(shader, serialization);
        List<GraphDiagnostic> diagnostics = GraphValidator.Validate(document, resolver).diagnostics.ToList();
        for (int index = 0; index < diagnostics.Count; index++)
        {
            if (diagnostics[index].code == "GRAPH_MISSING_NODE")
            {
                GraphDiagnostic value = diagnostics[index];
                diagnostics[index] = new GraphDiagnostic(
                    value.code,
                    value.message,
                    DiagnosticSeverity.Error,
                    value.nodeId,
                    value.edgeId);
            }
        }

        ValidateTechnique(techniqueId, definition, diagnostics);
        if (HasErrors(diagnostics))
            return new MaterialGraphEvaluationResult(shader, techniqueId, [], diagnostics);

        GraphNodeRecord output = outputs[0];
        Dictionary<ShaderPropertyId, ShaderPropertyDefinition> editable = definition.properties
            .Where(MaterialGraphNodeResolver.IsEditable)
            .ToDictionary(static value => value.id);
        var mapped = new List<MaterialPropertyEntry>(editable.Count);
        foreach (GraphEdgeRecord edge in document.edges
                     .Where(edge => edge.input.nodeId == output.id)
                     .OrderBy(static edge => edge.input.portId.value, StringComparer.Ordinal))
        {
            var propertyId = new ShaderPropertyId(edge.input.portId.value);
            if (!editable.TryGetValue(propertyId, out ShaderPropertyDefinition property))
                continue;
            GraphNodeRecord? source = document.FindNode(edge.output.nodeId);
            if (source is null || source.definitionId != MaterialGraphIds.valueNode)
                continue;
            MaterialValue value = MaterialGraphNodeResolver.ReadValue(source, serialization);
            if (!IsCompatible(property.type, value.kind))
            {
                diagnostics.Add(new GraphDiagnostic(
                    "MATERIAL_GRAPH_VALUE_INCOMPATIBLE",
                    $"'{property.displayName}' expects {property.type}, but the connected value is {value.kind}.",
                    DiagnosticSeverity.Error,
                    source.id,
                    edge.id));
                continue;
            }
            mapped.Add(new MaterialPropertyEntry(property.id, value));
        }

        HashSet<GraphNodeId> connectedNodes = document.edges
            .Where(edge => edge.input.nodeId == output.id)
            .Select(static edge => edge.output.nodeId)
            .ToHashSet();
        foreach (GraphNodeRecord unused in document.nodes.Where(node =>
                     node.definitionId == MaterialGraphIds.valueNode && !connectedNodes.Contains(node.id)))
        {
            diagnostics.Add(new GraphDiagnostic(
                "MATERIAL_GRAPH_VALUE_UNUSED",
                "This property value is not connected to Material Output.",
                DiagnosticSeverity.Warning,
                unused.id));
        }
        return new MaterialGraphEvaluationResult(shader, techniqueId, mapped, diagnostics);
    }

    /// <summary>
    /// Evaluates and commits the derived runtime values to one graph asset.
    /// </summary>
    /// <param name="asset">
    /// Material graph to update.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by node values.
    /// </param>
    /// <returns>
    /// The successful evaluation result.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the graph is invalid.
    /// </exception>
    public static MaterialGraphEvaluationResult Commit(
        MaterialGraphAsset asset,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.document is null)
            throw new InvalidOperationException("A Material Graph requires a graph document.");
        return Commit(asset, asset.document, serialization);
    }

    /// <summary>
    /// Evaluates an embedded graph and atomically replaces the owning material's runtime mapping.
    /// </summary>
    /// <param name="asset">
    /// Ordinary material to update.
    /// </param>
    /// <param name="document">
    /// Detached authoring graph.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by node values.
    /// </param>
    /// <returns>
    /// The successful evaluation result.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the graph is invalid.
    /// </exception>
    public static MaterialGraphEvaluationResult Commit(
        MaterialAsset asset,
        GraphDocument document,
        SerializationRegistry serialization)
    {
        MaterialGraphEvaluationResult result = Evaluate(asset, document, serialization);
        if (!result.succeeded)
        {
            string message = string.Join(
                Environment.NewLine,
                result.diagnostics
                    .Where(static value => value.severity == DiagnosticSeverity.Error)
                    .Select(static value => $"{value.code}: {value.message}"));
            throw new InvalidOperationException(message);
        }
        asset.shader = result.shader;
        asset.techniqueId = result.techniqueId;
        asset.ReplaceProperties(result.properties);
        return result;
    }

    private static void ValidateTechnique(
        ShaderTechniqueId techniqueId,
        ShaderDefinition definition,
        ICollection<GraphDiagnostic> diagnostics)
    {
        if (!techniqueId.isValid)
            return;
        if (definition.techniques.Any(technique => technique.id == techniqueId))
            return;
        diagnostics.Add(new GraphDiagnostic(
            "MATERIAL_GRAPH_TECHNIQUE_MISSING",
            $"Technique '{techniqueId}' is not declared by the selected shader.",
            DiagnosticSeverity.Error));
    }

    private static bool IsCompatible(ShaderPropertyType propertyType, MaterialValueKind valueKind)
        => propertyType switch
        {
            ShaderPropertyType.Float => valueKind == MaterialValueKind.Float,
            ShaderPropertyType.Vector2 or ShaderPropertyType.Vector3 or ShaderPropertyType.Vector4 =>
                valueKind == MaterialValueKind.Vector,
            ShaderPropertyType.Color => valueKind == MaterialValueKind.Color,
            ShaderPropertyType.Matrix4x4 => valueKind == MaterialValueKind.Matrix,
            ShaderPropertyType.Texture2D
                or ShaderPropertyType.Texture2DArray
                or ShaderPropertyType.Texture3D
                or ShaderPropertyType.TextureCube => valueKind == MaterialValueKind.Texture,
            _ => false
        };

    private static bool HasErrors(IEnumerable<GraphDiagnostic> diagnostics)
        => diagnostics.Any(static value => value.severity == DiagnosticSeverity.Error);

    private static MaterialGraphEvaluationResult Failure(string code, string message)
        => new(null, default, [], [new GraphDiagnostic(code, message, DiagnosticSeverity.Error)]);
}
