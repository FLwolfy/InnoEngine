using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering;

namespace Inno.Rendering.MaterialGraph;

/// <summary>
/// Provides stable identifiers used by material graph nodes and ports.
/// </summary>
public static class MaterialGraphIds
{
    /// <summary>
    /// Gets the single material output node definition identifier.
    /// </summary>
    public const string outputNode = "inno.material-graph.output";

    /// <summary>
    /// Gets the typed material value node definition identifier.
    /// </summary>
    public const string valueNode = "inno.material-graph.value";

    /// <summary>
    /// Gets the value node's sole output port.
    /// </summary>
    public static GraphPortId valuePort => new("value");
}

/// <summary>
/// Provides stable graph type identifiers for material-owned shader values.
/// </summary>
public static class MaterialGraphValueTypes
{
    /// <summary>
    /// Gets the stable graph type identifier for a shader property.
    /// </summary>
    /// <param name="type">
    /// Shader property type.
    /// </param>
    /// <returns>
    /// A stable material-value type identifier.
    /// </returns>
    public static string GetId(ShaderPropertyType type) => $"inno.material.{type}";

    /// <summary>
    /// Gets the stable graph type identifier for a material value.
    /// </summary>
    /// <param name="kind">
    /// Material value kind.
    /// </param>
    /// <returns>
    /// A stable material-value type identifier.
    /// </returns>
    public static string GetId(MaterialValueKind kind)
        => kind switch
        {
            MaterialValueKind.Float => GetId(ShaderPropertyType.Float),
            MaterialValueKind.Vector => GetId(ShaderPropertyType.Vector4),
            MaterialValueKind.Color => GetId(ShaderPropertyType.Color),
            MaterialValueKind.Matrix => GetId(ShaderPropertyType.Matrix4x4),
            MaterialValueKind.Texture => GetId(ShaderPropertyType.Texture2D),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}

/// <summary>
/// Creates canonical, fully connected material mapping documents from shader reflection.
/// </summary>
public static class MaterialGraphDocumentFactory
{
    /// <summary>
    /// Creates a canonical graph for one selected shader.
    /// </summary>
    /// <param name="shader">
    /// Selected shader, or <see langword="null"/> for an empty output.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used for node values.
    /// </param>
    /// <param name="previous">
    /// Optional prior document whose compatible property values are retained.
    /// </param>
    /// <param name="techniqueId">
    /// Technique selection stored on Material Output.
    /// </param>
    /// <returns>
    /// A detached editable graph document.
    /// </returns>
    public static GraphDocument Create(
        ShaderAsset? shader,
        SerializationRegistry serialization,
        GraphDocument? previous = null,
        ShaderTechniqueId techniqueId = default)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        if (shader is not null && shader.identity.persistentId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A shader must have a persistent identity before it can be selected by Material Graph.");
        }
        Dictionary<string, MaterialValue> retained = ReadRetainedValues(previous, serialization);
        var document = new GraphDocument();
        var outputId = new GraphNodeId("material-output");
        var output = new GraphNodeRecord(outputId, MaterialGraphIds.outputNode)
        {
            position = new GraphPosition(120f, 40f)
        };
        if (shader is not null)
        {
            output.SetValue(
                "shaderId",
                GraphSerializedValue.From(shader.identity.persistentId, serialization));
        }
        ShaderTechniqueId retainedTechnique = MaterialGraphDocumentModel.ReadTechniqueId(previous, serialization);
        if (retainedTechnique.isValid
            && MaterialGraphDocumentModel.ReadShaderId(previous, serialization) == shader?.identity.persistentId)
        {
            techniqueId = retainedTechnique;
        }
        output.SetValue(
            "techniqueId",
            GraphSerializedValue.From(techniqueId.value ?? string.Empty, serialization));
        document.AddNode(output);
        ShaderPropertyDefinition[] properties = shader?.definition?.properties
            .Where(MaterialGraphNodeResolver.IsEditable)
            .OrderBy(static value => value.id.value, StringComparer.Ordinal)
            .ToArray() ?? [];
        for (int index = 0; index < properties.Length; index++)
        {
            ShaderPropertyDefinition property = properties[index];
            MaterialValue value = retained.TryGetValue(property.id.value, out MaterialValue existing)
                && IsCompatible(property.type, existing.kind)
                    ? existing
                    : property.defaultValue;
            var valueId = new GraphNodeId($"property-{property.id.value}");
            var node = new GraphNodeRecord(valueId, MaterialGraphIds.valueNode)
            {
                position = new GraphPosition(-320f, index * 92f)
            };
            node.SetValue("propertyId", GraphSerializedValue.From(property.id.value, serialization));
            node.SetValue("displayName", GraphSerializedValue.From(property.displayName, serialization));
            node.SetValue("propertyType", GraphSerializedValue.From(property.type, serialization));
            node.SetValue("value", GraphSerializedValue.From(value, serialization));
            document.AddNode(node);
            document.AddEdge(new GraphEdgeRecord(
                new GraphEdgeId($"map-{property.id.value}"),
                new GraphEndpoint(valueId, MaterialGraphIds.valuePort),
                new GraphEndpoint(outputId, new GraphPortId(property.id.value))));
        }
        return document;
    }

    /// <summary>
    /// Creates the canonical one-to-one authoring graph for an ordinary material.
    /// </summary>
    /// <param name="material">
    /// Material whose shader, technique, and current property values seed the graph.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used for node values.
    /// </param>
    /// <returns>
    /// A detached editable graph that maps back to the supplied material.
    /// </returns>
    public static GraphDocument Create(
        MaterialAsset material,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(material);
        GraphDocument document = Create(
            material.shader,
            serialization,
            techniqueId: material.techniqueId);
        foreach (GraphNodeRecord node in document.nodes.Where(static value =>
                     value.definitionId == MaterialGraphIds.valueNode))
        {
            if (!node.TryGetValue("propertyId", out GraphSerializedValue? serializedId))
                continue;
            var propertyId = new ShaderPropertyId(serializedId!.Deserialize<string>(serialization));
            if (!material.TryGet(propertyId, out MaterialValue value))
                continue;
            node.SetValue("value", GraphSerializedValue.From(value, serialization));
        }
        return document;
    }

    private static Dictionary<string, MaterialValue> ReadRetainedValues(
        GraphDocument? document,
        SerializationRegistry serialization)
    {
        var result = new Dictionary<string, MaterialValue>(StringComparer.Ordinal);
        if (document is null)
            return result;
        foreach (GraphNodeRecord node in document.nodes.Where(static value =>
                     value.definitionId == MaterialGraphIds.valueNode))
        {
            if (!node.TryGetValue("propertyId", out GraphSerializedValue? propertyId))
                continue;
            result[propertyId!.Deserialize<string>(serialization)] =
                MaterialGraphNodeResolver.ReadValue(node, serialization);
        }
        return result;
    }

    private static bool IsCompatible(ShaderPropertyType type, MaterialValueKind kind)
        => type switch
        {
            ShaderPropertyType.Float => kind == MaterialValueKind.Float,
            ShaderPropertyType.Vector2 or ShaderPropertyType.Vector3 or ShaderPropertyType.Vector4 =>
                kind == MaterialValueKind.Vector,
            ShaderPropertyType.Color => kind == MaterialValueKind.Color,
            ShaderPropertyType.Matrix4x4 => kind == MaterialValueKind.Matrix,
            ShaderPropertyType.Texture2D
                or ShaderPropertyType.Texture2DArray
                or ShaderPropertyType.Texture3D
                or ShaderPropertyType.TextureCube => kind == MaterialValueKind.Texture,
            _ => false
        };
}

/// <summary>
/// Reads the authoritative material selection stored by a Material Output node.
/// </summary>
public static class MaterialGraphDocumentModel
{
    /// <summary>
    /// Reads the selected shader's persistent identity.
    /// </summary>
    /// <param name="document">
    /// Material graph document, or <see langword="null"/>.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by node values.
    /// </param>
    /// <returns>
    /// The selected shader identity, or <see cref="Guid.Empty"/>.
    /// </returns>
    public static Guid ReadShaderId(GraphDocument? document, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        GraphNodeRecord? output = FindOutput(document);
        return output is not null
            && output.TryGetValue("shaderId", out GraphSerializedValue? value)
            ? value!.Deserialize<Guid>(serialization)
            : Guid.Empty;
    }

    /// <summary>
    /// Reads the selected technique from Material Output.
    /// </summary>
    /// <param name="document">
    /// Material graph document, or <see langword="null"/>.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by node values.
    /// </param>
    /// <returns>
    /// The selected technique, or an invalid identity for automatic selection.
    /// </returns>
    public static ShaderTechniqueId ReadTechniqueId(
        GraphDocument? document,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        GraphNodeRecord? output = FindOutput(document);
        if (output is null
            || !output.TryGetValue("techniqueId", out GraphSerializedValue? value))
        {
            return default;
        }
        string techniqueId = value!.Deserialize<string>(serialization);
        return string.IsNullOrWhiteSpace(techniqueId)
            ? default
            : new ShaderTechniqueId(techniqueId);
    }

    private static GraphNodeRecord? FindOutput(GraphDocument? document)
        => document?.nodes.FirstOrDefault(static node => node.definitionId == MaterialGraphIds.outputNode);
}

/// <summary>
/// Resolves the two built-in material graph node definitions against one selected shader.
/// </summary>
public sealed class MaterialGraphNodeResolver : IGraphNodeDefinitionResolver
{
    private readonly IReadOnlyList<GraphNodeDefinition> m_definitions;
    private readonly Dictionary<string, GraphNodeDefinition> m_byId;

    /// <summary>
    /// Creates a resolver for one material graph and serialization generation.
    /// </summary>
    /// <param name="shader">
    /// Shader whose material-owned properties become output ports.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used to inspect value nodes.
    /// </param>
    public MaterialGraphNodeResolver(ShaderAsset? shader, SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        GraphNodeDefinition[] definitions =
        [
            new MaterialOutputNodeDefinition(shader),
            new MaterialValueNodeDefinition(serialization)
        ];
        m_definitions = Array.AsReadOnly(definitions);
        m_byId = definitions.ToDictionary(static value => value.id, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the complete built-in definition set in stable order.
    /// </summary>
    public IReadOnlyList<GraphNodeDefinition> definitions => m_definitions;

    /// <summary>
    /// Tries to resolve a built-in material node definition.
    /// </summary>
    /// <param name="definitionId">
    /// Stable definition identifier.
    /// </param>
    /// <param name="definition">
    /// Receives the definition when found.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the definition exists.
    /// </returns>
    public bool TryResolve(string definitionId, out GraphNodeDefinition? definition)
        => m_byId.TryGetValue(definitionId, out definition);

    private sealed class MaterialOutputNodeDefinition : GraphNodeDefinition
    {
        private readonly GraphPortDefinition[] m_ports;

        internal MaterialOutputNodeDefinition(ShaderAsset? shader)
            : base(MaterialGraphIds.outputNode, "Material Output", "Material")
        {
            m_ports = shader?.definition?.properties
                .Where(static property => IsEditable(property))
                .OrderBy(static property => property.id.value, StringComparer.Ordinal)
                .Select(static property => new GraphPortDefinition(
                    new GraphPortId(property.id.value),
                    property.displayName,
                    MaterialGraphValueTypes.GetId(property.type),
                    GraphPortDirection.Input))
                .ToArray() ?? [];
        }

        /// <summary>
        /// Retrieves the current ports from authoritative state.
        /// </summary>
        /// <param name="node">
        /// The node consumed by get ports; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// An immutable snapshot of the values selected by the operation.
        /// </returns>
public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        {
            ArgumentNullException.ThrowIfNull(node);
            return m_ports;
        }
    }

    private sealed class MaterialValueNodeDefinition(SerializationRegistry serialization)
        : GraphNodeDefinition(MaterialGraphIds.valueNode, "Property Value", "Inputs")
    {
        private readonly SerializationRegistry m_serialization = serialization;

        /// <summary>
        /// Retrieves the current ports from authoritative state.
        /// </summary>
        /// <param name="node">
        /// The node consumed by get ports; ownership remains with the caller unless explicitly stated otherwise.
        /// </param>
        /// <returns>
        /// An immutable snapshot of the values selected by the operation.
        /// </returns>
public override IReadOnlyList<GraphPortDefinition> GetPorts(GraphNodeRecord node)
        {
            ArgumentNullException.ThrowIfNull(node);
            ShaderPropertyType type = ReadType(node, m_serialization);
            return
            [
                new GraphPortDefinition(
                    MaterialGraphIds.valuePort,
                    "Value",
                    MaterialGraphValueTypes.GetId(type),
                    GraphPortDirection.Output,
                    GraphPortCapacity.Multiple)
            ];
        }
    }

    internal static bool IsEditable(ShaderPropertyDefinition property)
        => property.bindingOwner == ShaderPropertyBindingOwner.Material
            && property.bindingKind is not ShaderPropertyBindingKind.StorageTexture
                and not ShaderPropertyBindingKind.StorageBuffer;

    internal static ShaderPropertyType ReadType(
        GraphNodeRecord node,
        SerializationRegistry serialization)
        => node.TryGetValue("propertyType", out GraphSerializedValue? value)
            ? value!.Deserialize<ShaderPropertyType>(serialization)
            : ShaderPropertyType.Float;

    /// <summary>
    /// Reads the typed material value stored by one built-in value node.
    /// </summary>
    /// <param name="node">
    /// Value node record to inspect.
    /// </param>
    /// <param name="serialization">
    /// Serialization generation used by the stored payload.
    /// </param>
    /// <returns>
    /// The stored value, or a default scalar value when absent.
    /// </returns>
    public static MaterialValue ReadValue(
        GraphNodeRecord node,
        SerializationRegistry serialization)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(serialization);
        return node.TryGetValue("value", out GraphSerializedValue? value)
            ? value!.Deserialize<MaterialValue>(serialization)
            : MaterialValue.FromFloat(0f);
    }
}
