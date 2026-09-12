using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Defines the native graph document protocol shared by shader import, templates and the editor.</summary>
public static class ShaderGraphDocument
{
    /// <summary>Identifies the graph metadata containing the source-free material and pass contract.</summary>
    public const string definitionKey = "inno.shader.definition";
    /// <summary>Identifies a stage output node; its incoming edges name the stage's GPU outputs.</summary>
    public const string outputDefinitionId = "inno.shader.stage-output";
    /// <summary>Identifies a node property containing its owning stage output node identity.</summary>
    public const string stageKey = "stage";
    /// <summary>Identifies the structured settings stored on a stage output node.</summary>
    public const string settingsKey = "settings";

    /// <summary>Creates an empty graph with a source-free shader contract; incomplete graphs remain serializable.</summary>
    /// <param name="definition">Material parameters, techniques and pass state.</param>
    /// <param name="serialization">The owner converter registry.</param>
    /// <param name="context">The complete owner reference context.</param>
    /// <returns>A detached graph ready for authoring.</returns>
    public static GraphDocument Create(ShaderDefinition definition, SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var graph = new GraphDocument();
        graph.SetMetadata(definitionKey, Encode(serialization.Serialize(definition, context), serialization, context));
        return graph;
    }

    /// <summary>Reads the material and pass contract without evaluating or altering the graph.</summary>
    /// <param name="graph">Native shader graph.</param>
    /// <param name="serialization">The owner converter registry.</param>
    /// <param name="context">The complete owner reference context.</param>
    /// <returns>A detached contract; missing or corrupt metadata is an explicit error.</returns>
    public static ShaderDefinition ReadDefinition(GraphDocument graph, SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.metadata.TryGetValue(definitionKey, out GraphSerializedValue? value))
            throw new InvalidOperationException("The shader graph has no program definition.");
        return serialization.Deserialize<ShaderDefinition>(Decode<byte[]>(value, serialization, context), context);
    }

    /// <summary>Encodes a node or document value through the common native serialization channel.</summary>
    /// <typeparam name="T">Native serializable value type.</typeparam>
    /// <param name="value">Detached authoring value.</param>
    /// <param name="serialization">The owner converter registry.</param>
    /// <param name="context">The complete owner reference context.</param>
    /// <returns>Neutral bytes suitable for graph persistence and history.</returns>
    public static GraphSerializedValue Encode<T>(T value, SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(context);
        return new(serialization.Encode(writer => writer.Write("value", value), context));
    }

    /// <summary>Decodes a graph value against the current owner generation.</summary>
    /// <typeparam name="T">Expected native serializable type.</typeparam>
    /// <param name="value">Neutral graph value.</param>
    /// <param name="serialization">The owner converter registry.</param>
    /// <param name="context">The complete owner reference context.</param>
    /// <returns>A detached current-generation value.</returns>
    public static T Decode<T>(GraphSerializedValue value, SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(serialization);
        ArgumentNullException.ThrowIfNull(context);
        return serialization.Decode(value.data.Span, reader => reader.Read<T>("value"), context);
    }

    /// <summary>Reads a named node value; absent values use the declared default, corrupt values never do.</summary>
    /// <typeparam name="T">Expected native serializable type.</typeparam>
    /// <param name="node">Neutral node record.</param>
    /// <param name="key">Stable property key.</param>
    /// <param name="defaultValue">Value used only when the property is absent.</param>
    /// <param name="serialization">The owner converter registry.</param>
    /// <param name="context">The complete owner reference context.</param>
    /// <returns>The decoded value or explicit absent-property default.</returns>
    public static T Read<T>(GraphNodeRecord node, string key, T defaultValue, SerializationRegistry serialization, SerializationContext context)
        => node.TryGetValue(key, out GraphSerializedValue? value) ? Decode<T>(value!, serialization, context) : defaultValue;
}

/// <summary>Stores one stage's explicit output interface and compute dimensions inside its output node.</summary>
public sealed class ShaderGraphStageSettings : ISerializable
{
    /// <summary>Gets or sets the exact pass identity in the shader definition.</summary>
    [SerializableProperty] public string pass { get; set; } = "Main";
    /// <summary>Gets or sets the programmable stage.</summary>
    [SerializableProperty] public ShaderStage stage { get; set; } = ShaderStage.Fragment;
    /// <summary>Gets or sets output ports and their GPU destinations.</summary>
    [SerializableProperty] public ShaderGraphOutput[] outputs { get; set; } = [];
    /// <summary>Gets or sets compute workgroup width.</summary>
    [SerializableProperty] public int threadsX { get; set; } = 1;
    /// <summary>Gets or sets compute workgroup height.</summary>
    [SerializableProperty] public int threadsY { get; set; } = 1;
    /// <summary>Gets or sets compute workgroup depth.</summary>
    [SerializableProperty] public int threadsZ { get; set; } = 1;
}

/// <summary>Stores one stable output port's destination, independent of adapter source syntax.</summary>
public struct ShaderGraphOutput
{
    /// <summary>Gets or sets the stable input port identity on the stage output node.</summary>
    public string id { get; set; }
    /// <summary>Gets or sets the GPU destination category.</summary>
    public ShaderIrOutputKind kind { get; set; }
    /// <summary>Gets or sets the varying semantic; empty for fixed position, color or depth outputs.</summary>
    public string semantic { get; set; }
    /// <summary>Gets or sets the varying or attachment index.</summary>
    public int location { get; set; }
}

/// <summary>Stores a stage input node's logical interface without native names or GPU handles.</summary>
public sealed class ShaderGraphInputSettings : ISerializable
{
    /// <summary>Gets or sets the stable logical binding identity.</summary>
    [SerializableProperty] public string id { get; set; } = "input";
    /// <summary>Gets or sets the complete neutral type descriptor.</summary>
    [SerializableProperty] public ShaderGraphType type { get; set; } = new();
    /// <summary>Gets or sets the stage input category.</summary>
    [SerializableProperty] public ShaderIrInputKind kind { get; set; }
    /// <summary>Gets or sets the target semantic, not a native expression.</summary>
    [SerializableProperty] public string semantic { get; set; } = "position";
    /// <summary>Gets or sets the interface index.</summary>
    [SerializableProperty] public int location { get; set; }

    /// <summary>Validates the persisted descriptor and freezes its stage binding.</summary>
    /// <returns>An immutable backend-neutral input binding.</returns>
    public ShaderIrStageInput CreateBinding() => new(id, type.CreateType(), kind, semantic, location);
}
