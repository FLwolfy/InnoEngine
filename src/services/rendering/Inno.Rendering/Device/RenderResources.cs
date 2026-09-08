using System;

namespace Inno.Rendering;

/// <summary>
/// Declares backend-neutral texture storage formats.
/// </summary>
public enum RenderTextureFormat
{
    /// <summary>
    /// Eight-bit single-channel normalized format.
    /// </summary>
    R8,
    /// <summary>
    /// Eight-bit two-channel normalized format.
    /// </summary>
    RG8,
    /// <summary>
    /// Eight-bit four-channel linear normalized format.
    /// </summary>
    RGBA8,
    /// <summary>
    /// Eight-bit four-channel sRGB format.
    /// </summary>
    RGBA8Srgb,
    /// <summary>
    /// Ten-bit RGB and two-bit alpha normalized format.
    /// </summary>
    RGB10A2,
    /// <summary>
    /// Eleven-bit RGB floating-point format.
    /// </summary>
    RG11B10Float,
    /// <summary>
    /// Half-precision four-channel floating-point format.
    /// </summary>
    RGBA16Float,
    /// <summary>
    /// Single-channel 32-bit floating-point format.
    /// </summary>
    R32Float,
    /// <summary>
    /// Twenty-four-bit depth and eight-bit stencil format.
    /// </summary>
    Depth24Stencil8,
    /// <summary>
    /// Thirty-two-bit floating-point depth format.
    /// </summary>
    Depth32Float
}

/// <summary>
/// Identifies the dimensional shape of a backend-neutral texture resource.
/// </summary>
public enum RenderTextureDimension
{
    /// <summary>
    /// Two-dimensional texture with optional array layers.
    /// </summary>
    Texture2D,
    /// <summary>
    /// Three-dimensional volume texture.
    /// </summary>
    Texture3D,
    /// <summary>
    /// Cubemap texture with optional cubemap array layers.
    /// </summary>
    Cube
}

/// <summary>
/// Declares intended texture operations for capability and hazard validation.
/// </summary>
[Flags]
public enum RenderTextureUsage
{
    /// <summary>
    /// Shader-readable texture.
    /// </summary>
    Sampled = 1 << 0,
    /// <summary>
    /// Raster color attachment.
    /// </summary>
    ColorAttachment = 1 << 1,
    /// <summary>
    /// Raster depth or stencil attachment.
    /// </summary>
    DepthStencilAttachment = 1 << 2,
    /// <summary>
    /// Shader-readable and writable unordered texture.
    /// </summary>
    Storage = 1 << 3,
    /// <summary>
    /// Copy operation source.
    /// </summary>
    CopySource = 1 << 4,
    /// <summary>
    /// Copy operation destination.
    /// </summary>
    CopyDestination = 1 << 5,
    /// <summary>
    /// Asynchronous transfer from GPU texture memory to CPU-visible bytes.
    /// </summary>
    Readback = 1 << 6
}

/// <summary>
/// Declares intended buffer operations for capability and hazard validation.
/// </summary>
[Flags]
public enum RenderBufferUsage
{
    /// <summary>
    /// Vertex input data.
    /// </summary>
    Vertex = 1 << 0,
    /// <summary>
    /// Index input data.
    /// </summary>
    Index = 1 << 1,
    /// <summary>
    /// Read-only shader data.
    /// </summary>
    Uniform = 1 << 2,
    /// <summary>
    /// Shader-readable and writable storage data.
    /// </summary>
    Storage = 1 << 3,
    /// <summary>
    /// Indirect command data.
    /// </summary>
    Indirect = 1 << 4,
    /// <summary>
    /// Copy operation source.
    /// </summary>
    CopySource = 1 << 5,
    /// <summary>
    /// Copy operation destination.
    /// </summary>
    CopyDestination = 1 << 6,
    /// <summary>
    /// Contents may be replaced at frame safety points.
    /// </summary>
    Dynamic = 1 << 7
}

/// <summary>
/// Describes a render-graph texture independently from a graphics backend.
/// </summary>
public sealed class RenderTextureDescriptor : IEquatable<RenderTextureDescriptor>
{
    /// <summary>
    /// Creates a texture descriptor.
    /// </summary>
    /// <param name="width">
    /// Texture width in pixels.
    /// </param>
    /// <param name="height">
    /// Texture height in pixels.
    /// </param>
    /// <param name="format">
    /// Storage format.
    /// </param>
    /// <param name="usage">
    /// Permitted operations.
    /// </param>
    /// <param name="mipCount">
    /// Number of mip levels.
    /// </param>
    /// <param name="arrayLayers">
    /// Number of array layers.
    /// </param>
    /// <param name="sampleCount">
    /// Raster sample count.
    /// </param>
    /// <param name="dimension">
    /// Texture dimensional shape.
    /// </param>
    /// <param name="depth">
    /// Base mip depth for a three-dimensional texture.
    /// </param>
    public RenderTextureDescriptor(
        int width,
        int height,
        RenderTextureFormat format,
        RenderTextureUsage usage,
        int mipCount = 1,
        int arrayLayers = 1,
        int sampleCount = 1,
        RenderTextureDimension dimension = RenderTextureDimension.Texture2D,
        int depth = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mipCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arrayLayers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        if (sampleCount is not (1 or 2 or 4 or 8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Sample count must be 1, 2, 4, 8, or 16.");
        }

        switch (dimension)
        {
            case RenderTextureDimension.Texture2D when depth != 1:
                throw new ArgumentException("A two-dimensional texture must have depth one.", nameof(depth));
            case RenderTextureDimension.Texture3D when arrayLayers != 1:
                throw new ArgumentException(
                    "A three-dimensional texture cannot also declare array layers.",
                    nameof(arrayLayers));
            case RenderTextureDimension.Texture3D when sampleCount != 1:
                throw new ArgumentException(
                    "A three-dimensional texture cannot be multisampled.",
                    nameof(sampleCount));
            case RenderTextureDimension.Cube when width != height:
                throw new ArgumentException("Cubemap width and height must be equal.", nameof(height));
            case RenderTextureDimension.Cube when depth != 1:
                throw new ArgumentException("A cubemap must have depth one.", nameof(depth));
            case RenderTextureDimension.Cube when sampleCount != 1:
                throw new ArgumentException("A cubemap cannot be multisampled.", nameof(sampleCount));
            case RenderTextureDimension.Texture2D:
            case RenderTextureDimension.Texture3D:
            case RenderTextureDimension.Cube:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dimension));
        }

        this.width = width;
        this.height = height;
        this.format = format;
        this.usage = usage;
        this.mipCount = mipCount;
        this.arrayLayers = arrayLayers;
        this.sampleCount = sampleCount;
        this.dimension = dimension;
        this.depth = depth;
    }

    /// <summary>
    /// Gets the texture width in pixels.
    /// </summary>
    public int width { get; }

    /// <summary>
    /// Gets the texture height in pixels.
    /// </summary>
    public int height { get; }

    /// <summary>
    /// Gets the storage format.
    /// </summary>
    public RenderTextureFormat format { get; }

    /// <summary>
    /// Gets permitted operations.
    /// </summary>
    public RenderTextureUsage usage { get; }

    /// <summary>
    /// Gets the number of mip levels.
    /// </summary>
    public int mipCount { get; }

    /// <summary>
    /// Gets the number of array layers.
    /// </summary>
    public int arrayLayers { get; }

    /// <summary>
    /// Gets the raster sample count.
    /// </summary>
    public int sampleCount { get; }

    /// <summary>
    /// Gets the dimensional texture shape.
    /// </summary>
    public RenderTextureDimension dimension { get; }

    /// <summary>
    /// Gets the base mip depth for a three-dimensional texture.
    /// </summary>
    public int depth { get; }

    /// <summary>
    /// Gets the number of independently addressable layers or slices at one mip level.
    /// </summary>
    /// <param name="mipLevel">
    /// Zero-based mip level.
    /// </param>
    /// <returns>
    /// The array-layer count for 2D textures, six faces per cubemap layer, or the mip depth for 3D textures.
    /// </returns>
    public int GetSubresourceLayerCount(int mipLevel)
    {
        if ((uint)mipLevel >= (uint)mipCount)
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        return dimension switch
        {
            RenderTextureDimension.Texture2D => arrayLayers,
            RenderTextureDimension.Texture3D => Math.Max(1, depth >> mipLevel),
            RenderTextureDimension.Cube => checked(arrayLayers * 6),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };
    }

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(RenderTextureDescriptor? other)
        => other is not null
            && width == other.width
            && height == other.height
            && format == other.format
            && usage == other.usage
            && mipCount == other.mipCount
            && arrayLayers == other.arrayLayers
            && sampleCount == other.sampleCount
            && dimension == other.dimension
            && depth == other.depth;

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    public override bool Equals(object? obj) => Equals(obj as RenderTextureDescriptor);

    /// <summary>
    /// Computes a hash code from the fields that participate in logical equality.
    /// </summary>
    /// <returns>
    /// A hash code consistent with the implemented equality contract.
    /// </returns>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(width);
        hash.Add(height);
        hash.Add(format);
        hash.Add(usage);
        hash.Add(mipCount);
        hash.Add(arrayLayers);
        hash.Add(sampleCount);
        hash.Add(dimension);
        hash.Add(depth);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Describes a render-graph buffer independently from a graphics backend.
/// </summary>
public sealed class RenderBufferDescriptor : IEquatable<RenderBufferDescriptor>
{
    /// <summary>
    /// Creates a buffer descriptor.
    /// </summary>
    /// <param name="elementCount">
    /// Number of addressable elements.
    /// </param>
    /// <param name="elementStride">
    /// Element size in bytes.
    /// </param>
    /// <param name="usage">
    /// Permitted operations.
    /// </param>
    public RenderBufferDescriptor(int elementCount, int elementStride, RenderBufferUsage usage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementStride);
        this.elementCount = elementCount;
        this.elementStride = elementStride;
        this.usage = usage;
    }

    /// <summary>
    /// Gets the number of addressable elements.
    /// </summary>
    public int elementCount { get; }

    /// <summary>
    /// Gets the element size in bytes.
    /// </summary>
    public int elementStride { get; }

    /// <summary>
    /// Gets permitted operations.
    /// </summary>
    public RenderBufferUsage usage { get; }

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The value to compare with this instance.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(RenderBufferDescriptor? other)
        => other is not null
            && elementCount == other.elementCount
            && elementStride == other.elementStride
            && usage == other.usage;

    /// <summary>
    /// Determines whether this instance and the supplied value represent the same logical state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when both values represent the same logical state; otherwise, <see langword="false"/>.
    /// </returns>
    /// <param name="obj">
    /// The object to compare with this instance.
    /// </param>
    public override bool Equals(object? obj) => Equals(obj as RenderBufferDescriptor);

    /// <summary>
    /// Computes a hash code from the fields that participate in logical equality.
    /// </summary>
    /// <returns>
    /// A hash code consistent with the implemented equality contract.
    /// </returns>
    public override int GetHashCode() => HashCode.Combine(elementCount, elementStride, usage);
}

/// <summary>
/// Identifies a frame-scoped render-graph texture.
/// </summary>
public readonly record struct RenderTextureHandle
{
    internal RenderTextureHandle(int index, uint generation)
    {
        this.index = index;
        this.generation = generation;
    }

    internal int index { get; }
    internal uint generation { get; }

    /// <summary>
    /// Gets whether the handle was created by a render graph.
    /// </summary>
    public bool isValid => index >= 0 && generation != 0;
}

/// <summary>
/// Identifies a frame-scoped render-graph buffer.
/// </summary>
public readonly record struct RenderBufferHandle
{
    internal RenderBufferHandle(int index, uint generation)
    {
        this.index = index;
        this.generation = generation;
    }

    internal int index { get; }
    internal uint generation { get; }

    /// <summary>
    /// Gets whether the handle was created by a render graph.
    /// </summary>
    public bool isValid => index >= 0 && generation != 0;
}

/// <summary>
/// Identifies a persistent device texture without exposing a backend-native handle.
/// </summary>
public readonly record struct PersistentTextureHandle
{
    internal PersistentTextureHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }
    internal uint deviceGeneration { get; }

    /// <summary>
    /// Gets whether the handle identifies a device texture.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>
/// Identifies a persistent device buffer without exposing a backend-native handle.
/// </summary>
public readonly record struct PersistentBufferHandle
{
    internal PersistentBufferHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }
    internal uint deviceGeneration { get; }

    /// <summary>
    /// Gets whether the handle identifies a device buffer.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}

/// <summary>
/// Identifies a persistent presentation surface without exposing a backend framebuffer or swapchain handle.
/// </summary>
public readonly record struct RenderSurfaceHandle
{
    internal RenderSurfaceHandle(ulong value, uint deviceGeneration)
    {
        this.value = value;
        this.deviceGeneration = deviceGeneration;
    }

    internal ulong value { get; }
    internal uint deviceGeneration { get; }

    /// <summary>
    /// Gets whether the handle identifies a presentation surface.
    /// </summary>
    public bool isValid => value != 0 && deviceGeneration != 0;
}
