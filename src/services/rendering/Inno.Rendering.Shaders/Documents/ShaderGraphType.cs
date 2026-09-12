using System;
using System.Linq;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Persists a complete shader type as neutral native-serializable data, without extension instances.</summary>
public sealed class ShaderGraphType : ISerializable
{
    /// <summary>Gets or sets the atomic or nominal structure type identity.</summary>
    [SerializableProperty] public string id { get; set; } = "float4";
    /// <summary>Gets or sets the fixed array element type, or null for a non-array.</summary>
    [SerializableProperty] public ShaderGraphType? element { get; set; }
    /// <summary>Gets or sets the fixed array length.</summary>
    [SerializableProperty] public int length { get; set; }
    /// <summary>Gets or sets ordered structure member names.</summary>
    [SerializableProperty] public string[] fieldNames { get; set; } = [];
    /// <summary>Gets or sets ordered structure member types.</summary>
    [SerializableProperty] public ShaderGraphType[] fieldTypes { get; set; } = [];
    /// <summary>Gets or sets whether the descriptor represents a storage binding.</summary>
    [SerializableProperty] public bool isStorage { get; set; }
    /// <summary>Gets or sets whether storage is an image instead of a buffer.</summary>
    [SerializableProperty] public bool isImage { get; set; }
    /// <summary>Gets or sets the complete buffer element type.</summary>
    [SerializableProperty] public ShaderGraphType? storageElement { get; set; }
    /// <summary>Gets or sets storage access.</summary>
    [SerializableProperty] public RenderStorageAccess access { get; set; }
    /// <summary>Gets or sets the image texel format.</summary>
    [SerializableProperty] public RenderTextureFormat format { get; set; }
    /// <summary>Gets or sets the image spatial dimension.</summary>
    [SerializableProperty] public RenderTextureDimension dimension { get; set; } = RenderTextureDimension.Texture2D;
    /// <summary>Gets or sets whether an image has array layers.</summary>
    [SerializableProperty] public bool isArray { get; set; }

    /// <summary>Captures a full immutable source type as reload-safe authoring data.</summary>
    /// <param name="type">Atomic, aggregate or storage type to capture.</param>
    /// <returns>A detached descriptor whose reconstructed type has the same complete interface.</returns>
    public static ShaderGraphType Capture(ShaderSourceType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var result = new ShaderGraphType { id = type.id, element = type.elementType is null ? null : Capture(type.elementType),
            length = type.elementCount, fieldNames = type.fields.Select(static field => field.name).ToArray(),
            fieldTypes = type.fields.Select(static field => Capture(field.type)).ToArray() };
        if (type.storage is ShaderStorageType storage)
        {
            result.isStorage = true;
            result.isImage = storage.isImage;
            result.access = storage.access;
            result.dimension = storage.dimension;
            result.isArray = storage.array;
            if (storage.isImage) result.format = storage.format!.Value;
            else result.storageElement = Capture(storage.valueType);
        }
        return result;
    }

    /// <summary>Validates and freezes the complete descriptor.</summary>
    /// <returns>An immutable type; contradictory or incomplete descriptors fail explicitly.</returns>
    public ShaderSourceType CreateType()
    {
        if (fieldNames.Length != fieldTypes.Length) throw new InvalidOperationException("Shader structure field names and types differ in length.");
        if (isStorage)
        {
            if (element is not null || fieldNames.Length != 0 || length != 0)
                throw new InvalidOperationException("A storage type cannot also be an array or structure.");
            return ShaderSourceType.Storage(isImage
                ? ShaderStorageType.Image(format, access, dimension, isArray)
                : ShaderStorageType.Buffer((storageElement ?? throw new InvalidOperationException("A storage buffer requires its element type.")).CreateType(), access));
        }
        if (element is not null)
        {
            if (fieldNames.Length != 0) throw new InvalidOperationException("An array cannot also declare structure fields.");
            return ShaderSourceType.ArrayOf(element.CreateType(), length);
        }
        if (length != 0 || storageElement is not null) throw new InvalidOperationException("A non-array value contains an unexpected element descriptor.");
        return fieldNames.Length == 0 ? ShaderSourceType.Atomic(id)
            : ShaderSourceType.Structure(id, fieldNames.Select((name, index) => new ShaderSourceField(name, fieldTypes[index].CreateType())).ToArray());
    }
}
