using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;
using Inno.Rendering;
using Inno.Rendering.Shaders;

namespace Inno.Editor.Shaders;

/// <summary>Stores Editor-only parameter presentation by stable binding identity in the Shader's authoring graph.</summary>
public sealed class ShaderParameterPresentation : ISerializable
{
    private const string C_PREFIX = "inno.editor.parameter.";

    /// <summary>Gets or sets the Material Inspector section; empty uses the standard Parameters section.</summary>
    [SerializableProperty] public string group { get; set; } = "";
    /// <summary>Gets or sets the hover description without adding a permanent paragraph.</summary>
    [SerializableProperty] public string description { get; set; } = "";
    /// <summary>Gets or sets whether this Material-owned parameter appears in ordinary Material Inspectors.</summary>
    [SerializableProperty] public bool visible { get; set; } = true;
    /// <summary>Gets or sets whether a scalar Float control uses the declared editing bounds.</summary>
    [SerializableProperty] public bool hasRange { get; set; }
    /// <summary>Gets or sets the inclusive editing minimum; existing values are not changed by presentation metadata.</summary>
    [SerializableProperty] public double minimum { get; set; }
    /// <summary>Gets or sets the inclusive editing maximum.</summary>
    [SerializableProperty] public double maximum { get; set; } = 1;

    /// <summary>Reads detached presentation for one binding, defaulting only when no presentation was authored.</summary>
    /// <param name="graph">Authoring graph, not a runtime Shader program.</param>
    /// <param name="propertyId">Stable parameter identity shared by all its nodes.</param>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner serialization context.</param>
    /// <returns>The authored presentation, or ordinary visible, unbounded defaults.</returns>
    public static ShaderParameterPresentation Read(GraphDocument graph, ShaderPropertyId propertyId,
        SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.metadata.TryGetValue(C_PREFIX + propertyId.value, out GraphSerializedValue? encoded)
            ? serialization.Deserialize<ShaderParameterPresentation>(ShaderGraphDocument.Decode<byte[]>(encoded!, serialization, context), context)
            : new();
    }

    /// <summary>Writes presentation to a detached graph without changing any default, Material, or compiled program.</summary>
    /// <param name="graph">Detached graph owned by the caller's History transaction.</param>
    /// <param name="propertyId">Stable parameter identity.</param>
    /// <param name="presentation">Detached Inspector settings.</param>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner serialization context.</param>
    /// <exception cref="ArgumentException">The identity or range is invalid, or a bound cannot be represented by a scalar Shader Float.</exception>
    public static void Write(GraphDocument graph, ShaderPropertyId propertyId, ShaderParameterPresentation presentation,
        SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(presentation);
        if (!propertyId.isValid || !double.IsFinite(presentation.minimum) || !double.IsFinite(presentation.maximum)
            || presentation.minimum < -float.MaxValue || presentation.maximum > float.MaxValue
            || presentation.minimum > presentation.maximum)
            throw new ArgumentException("Parameter presentation requires a stable identity and finite ordered Float bounds.");
        graph.SetMetadata(C_PREFIX + propertyId.value,
            ShaderGraphDocument.Encode(serialization.Serialize(presentation, context), serialization, context));
    }
}
