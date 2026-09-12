using System;
using Inno.Rendering;

namespace Inno.Rendering.Shaders;

/// <summary>Describes typed storage without embedding a backend register, declaration or resource handle.</summary>
public sealed class ShaderStorageType
{
    private ShaderStorageType(ShaderSourceType valueType, RenderStorageAccess access, RenderTextureFormat? format,
        RenderTextureDimension dimension, bool array)
    {
        if (!Enum.IsDefined(access)) throw new ArgumentOutOfRangeException(nameof(access));
        this.valueType = valueType;
        this.access = access;
        this.format = format;
        this.dimension = dimension;
        this.array = array;
    }

    /// <summary>Gets the buffer element or image load/store value type; image operations use float4.</summary>
    public ShaderSourceType valueType { get; }
    /// <summary>Gets the permitted memory access, independent of Render Graph scheduling.</summary>
    public RenderStorageAccess access { get; }
    /// <summary>Gets the exact storage image format, or null for a structured buffer.</summary>
    public RenderTextureFormat? format { get; }
    /// <summary>Gets the image dimension; ignored for buffers.</summary>
    public RenderTextureDimension dimension { get; }
    /// <summary>Gets whether a two-dimensional storage image has array layers.</summary>
    public bool array { get; }
    /// <summary>Gets whether this descriptor denotes an image rather than a structured buffer.</summary>
    public bool isImage => format.HasValue;

    /// <summary>Creates a structured storage buffer with an explicit element layout.</summary>
    /// <param name="element">Non-void value layout; resources cannot be embedded in buffer elements.</param>
    /// <param name="access">Permitted memory operations.</param>
    /// <returns>An immutable storage buffer description.</returns>
    /// <exception cref="ArgumentException">The element contains void or resource handles.</exception>
    public static ShaderStorageType Buffer(ShaderSourceType element, RenderStorageAccess access)
    {
        ArgumentNullException.ThrowIfNull(element);
        ValidateElement(element);
        return new(element, access, null, RenderTextureDimension.Texture2D, false);
    }

    /// <summary>Creates a formatted storage image; target capabilities must separately support its access and format.</summary>
    /// <param name="format">Linear, non-depth image format supported by the rendering contract.</param>
    /// <param name="access">Permitted memory operations.</param>
    /// <param name="dimension">Two-dimensional or three-dimensional image shape.</param>
    /// <param name="array">Whether a two-dimensional image is an array.</param>
    /// <returns>An immutable image description with float4 load/store values.</returns>
    /// <exception cref="ArgumentException">The format or shape cannot represent storage image operations.</exception>
    public static ShaderStorageType Image(RenderTextureFormat format, RenderStorageAccess access,
        RenderTextureDimension dimension = RenderTextureDimension.Texture2D, bool array = false)
    {
        if (!Enum.IsDefined(format) || format is RenderTextureFormat.RGBA8Srgb or RenderTextureFormat.Depth24Stencil8 or RenderTextureFormat.Depth32Float)
            throw new ArgumentException("A storage image requires a linear color format.", nameof(format));
        if (dimension is not (RenderTextureDimension.Texture2D or RenderTextureDimension.Texture3D) || (array && dimension != RenderTextureDimension.Texture2D))
            throw new ArgumentException("Storage images support 2D, 2D array or 3D shapes.", nameof(dimension));
        return new(ShaderSourceType.Atomic("float4"), access, format, dimension, array);
    }

    /// <summary>Compares complete access, format, shape and element layout contracts.</summary>
    /// <param name="other">Candidate resource description.</param>
    /// <returns>True only when both bindings permit exactly the same operations and values.</returns>
    public bool IsEquivalentTo(ShaderStorageType? other)
        => other is not null && access == other.access && format == other.format && dimension == other.dimension
            && array == other.array && valueType.IsEquivalentTo(other.valueType);

    private static void ValidateElement(ShaderSourceType type)
    {
        if (type.id == "void" || type.storage is not null || type.id.StartsWith("sampled-texture", StringComparison.Ordinal))
            throw new ArgumentException("Storage elements cannot contain void or resource handles.");
        if (type.elementType is not null) ValidateElement(type.elementType);
        foreach (ShaderSourceField field in type.fields) ValidateElement(field.type);
    }
}
